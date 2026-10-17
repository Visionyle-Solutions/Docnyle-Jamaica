# Consult360 — OPD Consultation Module
## Phase 4 Migration Guide: ASP.NET Core Web API + SQL Server 2019

**Document version:** 1.0 — September 2026
**Scope:** This is the final migration phase. It converts the Phase 1
vanilla-JS SPA + in-memory data layer into a production ASP.NET Core (net8.0,
current LTS) Web API backed by SQL Server 2019, using the schema, stored
procedures, and REST contract already designed for this purpose.
**Audience:** Engineers executing the migration; reviewers validating the
plan before work begins.
**Companion documents:** `DATA-LAYER-ARCHITECTURE.md` (binding naming/schema
rules this guide implements), `API-SPECIFICATION.md` (the 130-endpoint
contract), `schema.sql` (51-table DDL), `stored-procedures.sql` (152
procedures), `PRODUCT-GUIDE.md`, `testing-guide.md`, `ENCRYPTION-STRATEGY.md`,
`PEN-TEST-STRATEGY.md`.

**Scope correction vs. earlier planning documents.** `docs/migration-map.md`
and parts of `docs/PRODUCT-GUIDE.md` describe the Phase 4 target as
"ASP.NET Core / Blazor," with `.razor` pages replacing the HTML/JS front
end. That is **not** the target this guide implements. Per the current
mandate, the browser SPA (`index.html`, `app.js`, `router.js`, the module
and template files) stays vanilla JS and is **not rewritten**. Only
`js/api.js` changes — its method bodies switch from calling `window.Data.*`
to calling `fetch()` against the new Web API. Everywhere below, "Phase 4"
means ASP.NET Core Web API + SQL Server 2019 behind the existing SPA, not a
Blazor rewrite. Treat this document as superseding the Blazor framing in
those two files for planning purposes; they are not edited as part of this
guide.

---

## Table of Contents

1. [Executive Summary & Target Architecture](#1-executive-summary--target-architecture)
2. [Guiding Principles](#2-guiding-principles)
3. [Solution Project Structure](#3-solution-project-structure)
4. [Data Access Strategy](#4-data-access-strategy)
5. [Business Logic Porting](#5-business-logic-porting)
6. [API Layer / Controllers](#6-api-layer--controllers)
7. [Authentication, Authorization & Multi-Tenancy](#7-authentication-authorization--multi-tenancy)
8. [Client-Side Engines Decision](#8-client-side-engines-decision)
9. [Front-End Integration](#9-front-end-integration)
10. [Database Deployment & Migrations Strategy](#10-database-deployment--migrations-strategy)
11. [Testing & Parity Strategy](#11-testing--parity-strategy)
12. [Migration Sequence & Phase Gates](#12-migration-sequence--phase-gates)
13. [Risks & Mitigations](#13-risks--mitigations)
14. [Appendix](#14-appendix)

---

## 1. Executive Summary & Target Architecture

Consult360's Phase 1 build is a static HTML/CSS/vanilla-JS SPA with an
in-memory data layer (`js/data.js`) wrapped by an async service facade
(`js/api.js`). That facade was deliberately designed — per
`DATA-LAYER-ARCHITECTURE.md` — so that its 143 method signatures become the
Web API's controller actions with no renaming, and so that the SQL Server
schema and stored procedures could be authored ahead of the .NET code. Both
of those artifacts already exist and are frozen: `docs/schema.sql` (51
tables across 6 schemas) and `docs/stored-procedures.sql` (152 stored
procedures). This phase's job is to stand up the ASP.NET Core Web API that
sits between the unchanged browser SPA and that unchanged database, and to
port `data.js`'s business logic — which is real, non-trivial, hand-written
JavaScript, not a declarative rules engine — into C# services that call
those procedures.

### Target architecture

```
┌───────────────────────────────────────────────────────────────────────┐
│  BROWSER — unchanged Phase 1 SPA                                      │
│  index.html · app.js · router.js · session-manager.js · app-context.js│
│  js/modules/**  js/components/**  js/engines/** (all 5 stay here)     │
│  ┌───────────────────────────────────────────────────────────────┐   │
│  │  js/api.js  — ONLY FILE THAT CHANGES                          │   │
│  │  Method bodies: ok(window.Data.xxx()) → fetch('/api/...')     │   │
│  │  Same 143 method names, same signatures, same return shapes.  │   │
│  └───────────────────────────────────────────────────────────────┘   │
└──────────────────────────────┬────────────────────────────────────────┘
                                │  HTTPS / JSON / Bearer JWT
                                ▼
┌───────────────────────────────────────────────────────────────────────┐
│  Consult360.Api  — ASP.NET Core 8 Web API (Kestrel)                   │
│  Program.cs (minimal hosting) · appsettings.{env}.json                │
│  Middleware pipeline: HTTPS redirect → security headers → CORS →      │
│    exception handler → authentication (JWT) → HospitalId/RLS          │
│    context → authorization (policy) → rate limiter → endpoints        │
│  Controllers/ — one per functional module (13), matching              │
│    API-SPECIFICATION.md's 130 endpoints 1:1                           │
└──────────────────────────────┬────────────────────────────────────────┘
                                │  DTOs in/out — DI-injected services
                                ▼
┌───────────────────────────────────────────────────────────────────────┐
│  Consult360.Application  — class library, no ASP.NET/SQL dependency   │
│  Services/  — one per module (PatientService, ConsultationService,    │
│    SchedulingService, PrescriptionService, BillingService, …)         │
│    — the ported data.js business logic (validation, filters,          │
│    upsert/soft-delete rules, slot generation, fee lookup, tri-state   │
│    toggling, drug-allergy pre-check) lives here, as plain C#          │
│  DTOs/ · Validators/ (FluentValidation) · Interfaces (IXxxRepository) │
└──────────────────────────────┬────────────────────────────────────────┘
                                │  interface calls only (no SqlClient here)
                                ▼
┌───────────────────────────────────────────────────────────────────────┐
│  Consult360.Infrastructure  — class library                           │
│  Repositories/  — Dapper, one per module, each method = one           │
│    stored-procedures.sql call (QueryAsync / QueryMultipleAsync /      │
│    ExecuteAsync against OUTPUT inserted.* result sets)                │
│  Auth/  — JWT issuing, ASP.NET Core Identity PasswordHasher           │
│  Security/  — HospitalContextAccessor, SESSION_CONTEXT injector       │
│  Resilience/  — Polly retry/circuit-breaker policies for SqlClient    │
│  Integrations/  — WhatsApp/Email/SMS providers (msg schema, Phase 2+) │
└──────────────────────────────┬────────────────────────────────────────┘
                                │  ADO.NET (SqlClient) via Dapper
                                ▼
┌───────────────────────────────────────────────────────────────────────┐
│  SQL Server 2019 — ALREADY DESIGNED, NOT REDESIGNED HERE               │
│  docs/schema.sql — 51 tables, 6 schemas (opd/cln/bil/cfg/sec/msg)     │
│  docs/stored-procedures.sql — 152 procedures, the ONLY write/read     │
│    surface the application code is allowed to use (no ad-hoc SQL)     │
│  Row-Level Security via SESSION_CONTEXT('HospitalId') — architecture  │
│    §6; Always Encrypted on the PII columns listed in                  │
│    ENCRYPTION-STRATEGY.md §1.2                                        │
└───────────────────────────────────────────────────────────────────────┘
```

**What does not change:** the SPA's HTML/CSS/JS modules, templates,
routing, the 5 clinical engines, the schema, and the stored procedures.
**What is net-new:** the four .NET projects (`Api`, `Application`,
`Infrastructure`, plus a `Domain`/shared-model layer folded into
`Application`) and a real authentication/session system replacing
`session-manager.js`'s client-only enforcement.

---

## 2. Guiding Principles

1. **The UI barely changes.** Only `js/api.js`'s method bodies change, from
   `ok(window.Data.xxx())` to `fetch('/api/...')`. No other SPA file is
   edited as part of this migration. This was the entire point of building
   the `api.js` facade in Phase 1 (`DATA-LAYER-ARCHITECTURE.md` §3).

2. **The controller contract is not designed here — it is implemented
   here.** `api.js`'s 143 method names/signatures and
   `docs/API-SPECIFICATION.md`'s 130 REST endpoints were already derived
   from the 14-verb taxonomy in `DATA-LAYER-ARCHITECTURE.md` §2. This
   guide's job is to produce ASP.NET Core controller actions that match
   that contract exactly — not to redesign the API surface.

3. **The schema and stored procedures are frozen inputs, not redesign
   targets.** `schema.sql` (51 tables) and `stored-procedures.sql` (152
   procedures) already exist, are SQL Server 2019-compatible, and already
   implement most of the persistence-side business rules (upserts via
   `MERGE`, soft deletes, `OUTPUT inserted.*`, BMI computed in
   `cln.usp_SaveVitals`, etc.). This migration deploys and versions them —
   it does not rewrite them, except to fix the handful of documented gaps
   in API-SPECIFICATION.md's "Known Gaps" section if the business decides
   to close them.

4. **`data.js`'s business logic is real work, not a mechanical
   translation.** Unlike a fully data-driven rules engine, `data.js`
   contains genuine hand-written algorithms — the slot-generation loop,
   the tri-state symptom cycle, the effective-dated fee lookup, the
   drug-allergy substring/class matcher, the MRN/ID generation scheme. A
   large fraction of these already have equivalent (and sometimes more
   complete — see the BMI example in §5) logic baked into the stored
   procedures. The porting work is therefore **verification and thin
   service-layer wrapping** for procedures that already encode the rule,
   and **genuine C# reimplementation** for the handful of behaviors that
   are UI-orchestration logic never pushed into a procedure (see §5's
   module-by-module methodology). Treat this as a real engineering task
   with its own test plan — not a copy-paste exercise.

5. **No ad-hoc SQL from application code.** Every database access goes
   through a named stored procedure from `stored-procedures.sql`. This
   was already a stated pen-test requirement (`PEN-TEST-STRATEGY.md` §3.3)
   and is easy to enforce once Dapper repositories are the only call site.

6. **Trust nothing the client sends for tenant scoping.** `_hid()` in
   `api.js` is a convenience fallback in Phase 1, not a security boundary
   — `PEN-TEST-STRATEGY.md` §3.4 flags this explicitly. In Phase 4, the
   `HospitalId` used for every query comes from the validated JWT claim,
   never from a request body/query parameter, even if the client sends
   one.

7. **Additive, not a rewrite of decided architecture.** Table names,
   schema namespaces (`opd`/`cln`/`bil`/`cfg`/`sec`/`msg`), the 14-verb
   method taxonomy, and the dual-key pattern (`Id` internal /
   `{Table}Id` external `UNIQUEIDENTIFIER`) are binding and are not
   revisited here.

---

## 3. Solution Project Structure

```
Consult360.sln
├── src/
│   ├── Consult360.Api/                  ← ASP.NET Core Web API host (net8.0)
│   │   ├── Program.cs                   (minimal hosting model)
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── appsettings.Staging.json
│   │   ├── appsettings.Production.json
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs
│   │   │   ├── HospitalsController.cs
│   │   │   ├── PatientsController.cs
│   │   │   ├── SchedulingController.cs
│   │   │   ├── ConsultationsController.cs
│   │   │   ├── VitalsController.cs           (nested under Consultations route)
│   │   │   ├── SymptomsController.cs
│   │   │   ├── DiagnosesController.cs
│   │   │   ├── PrescriptionsController.cs
│   │   │   ├── FormularyController.cs
│   │   │   ├── LabController.cs
│   │   │   ├── BillingController.cs
│   │   │   ├── ReportsController.cs
│   │   │   ├── ComplianceController.cs
│   │   │   ├── NotificationsController.cs
│   │   │   ├── AuditController.cs
│   │   │   └── AdminController.cs            (permissions, users, config)
│   │   ├── Middleware/
│   │   │   ├── ExceptionHandlingMiddleware.cs
│   │   │   ├── HospitalContextMiddleware.cs  (JWT claim → SESSION_CONTEXT)
│   │   │   └── SecurityHeadersMiddleware.cs
│   │   └── Filters/
│   │       └── ValidateModelAttribute.cs
│   │
│   ├── Consult360.Application/          ← business logic, no ASP.NET/SQL refs
│   │   ├── Services/
│   │   │   ├── Patients/PatientService.cs, AllergyService.cs, MedicalHistoryService.cs
│   │   │   ├── Scheduling/SchedulingService.cs, SlotGenerationService.cs
│   │   │   ├── Consultations/ConsultationService.cs, VitalsService.cs,
│   │   │   │     SymptomService.cs, SoapService.cs, SignOffService.cs
│   │   │   ├── Diagnoses/DiagnosisService.cs, Icd10SearchService.cs
│   │   │   ├── Prescriptions/PrescriptionService.cs, FormularyService.cs,
│   │   │   │     DrugAllergyCheckService.cs
│   │   │   ├── Lab/LabOrderService.cs, LabResultService.cs
│   │   │   ├── Billing/InvoiceService.cs, PaymentService.cs, FeeScheduleService.cs
│   │   │   ├── Reports/ReportingService.cs
│   │   │   ├── Notifications/NotificationService.cs
│   │   │   └── Audit/AuditService.cs, PermissionService.cs
│   │   ├── Dtos/                    (request/response records, 1 file per module)
│   │   ├── Validators/              (FluentValidation, mirrors data.js §8 rules)
│   │   ├── Interfaces/              (IPatientRepository, IConsultationRepository, …)
│   │   └── Common/                  (Result<T> envelope, PagedResult<T>, verb-taxonomy
│   │                                  base types, HospitalScoped marker interface)
│   │
│   ├── Consult360.Infrastructure/   ← Dapper, auth, external integrations
│   │   ├── Data/
│   │   │   ├── SqlConnectionFactory.cs
│   │   │   ├── DapperTypeHandlers.cs     (DateOnly, UNIQUEIDENTIFIER mapping)
│   │   │   └── Repositories/             (mirrors Application/Interfaces, 1:1)
│   │   │       ├── PatientRepository.cs  (calls opd.usp_* procedures)
│   │   │       ├── ConsultationRepository.cs (calls cln.usp_* procedures)
│   │   │       ├── BillingRepository.cs  (calls bil.usp_* procedures)
│   │   │       └── …
│   │   ├── Security/
│   │   │   ├── JwtTokenService.cs
│   │   │   ├── HospitalContextAccessor.cs
│   │   │   └── SessionContextSetter.cs   (sets SQL Server SESSION_CONTEXT)
│   │   ├── Resilience/
│   │   │   └── SqlResiliencePolicies.cs  (Polly retry/circuit-breaker)
│   │   └── Integrations/
│   │       ├── WhatsAppMessageSender.cs  (msg schema, behind a feature flag)
│   │       └── EmailMessageSender.cs
│   │
│   └── Consult360.Domain/           ← (optional, can fold into Application/Models)
│       └── Entities/                (POCOs mirroring schema.sql column names,
│                                      PascalCase, used as Dapper row targets)
│
└── tests/
    ├── Consult360.UnitTests/        (xUnit — Application services, no DB)
    │   ├── Scheduling/SlotGenerationServiceTests.cs
    │   ├── Consultations/SymptomStateServiceTests.cs
    │   ├── Billing/FeeScheduleServiceTests.cs
    │   └── Prescriptions/DrugAllergyCheckServiceTests.cs
    └── Consult360.IntegrationTests/  (xUnit + Testcontainers.MsSql)
        ├── Repositories/PatientRepositoryTests.cs
        ├── Repositories/ConsultationRepositoryTests.cs
        └── ApiContractTests/         (WebApplicationFactory, asserts response
                                        shapes against API-SPECIFICATION.md)
```

### Responsibilities and dependency direction

| Project | Depends on | Must never reference |
|---|---|---|
| `Consult360.Api` | `Application`, `Infrastructure` (for DI registration only, in `Program.cs`) | `Microsoft.Data.SqlClient` directly in a controller |
| `Consult360.Application` | `Domain` only | `Microsoft.AspNetCore.*`, `Dapper`, `Microsoft.Data.SqlClient` — this project has **zero** web or SQL dependencies, so business logic is testable in isolation and the persistence technology could change without touching it |
| `Consult360.Infrastructure` | `Application` (implements its interfaces), `Domain` | Nothing back up the chain |
| `Consult360.Domain` | Nothing | Everything else |

This mirrors the dependency-inversion shape implied by
`DATA-LAYER-ARCHITECTURE.md`'s own layering (`data.js` → `api.js` → SQL):
`Application` plays the role `data.js` played (the business rules), `Api`
plays the role `api.js` played (the thin, signature-preserving pass-through
that becomes the HTTP boundary), and `Infrastructure` is the new layer that
didn't exist in Phase 1 — the actual SQL Server access.

---

## 4. Data Access Strategy

### Decision: Dapper, not EF Core, for stored-procedure execution

**Why.** The project's entire data-access surface is already
stored-procedure-based: 152 procedures in `stored-procedures.sql`, each
implementing one `data.js`/`api.js` method's exact behavior (soft deletes,
`MERGE`-based upserts, `OUTPUT inserted.*` for created rows, allow-listed
per-table export procedures instead of dynamic SQL). EF Core's core value
proposition — a change-tracked object graph, LINQ-to-SQL translation,
migrations that generate schema from C# model classes — actively works
against that design:

- EF Core Code-First migrations would want to *own* the schema and
  generate its own DDL, conflicting with `schema.sql`'s hand-authored
  dual-key pattern (`Id BIGINT IDENTITY` clustered PK,
  `{Table}Id UNIQUEIDENTIFIER DEFAULT NEWSEQUENTIALID()` external key) and
  its explicit constraint-naming convention (`PK_{Schema}_{Table}`,
  `FK_{Schema}_{Table}_{Column}`, etc. — `DATA-LAYER-ARCHITECTURE.md` §5.5).
  EF Core can be pointed at an existing schema ("Database First"), but at
  that point it is only being used as a query/mapping layer — which is
  exactly what Dapper is, with far less overhead.
- Every write already goes through a procedure that encodes business rules
  server-side (e.g. `cln.usp_SaveVitals` computes BMI in the `MERGE`
  statement itself — see §5.1). Using EF Core's `DbSet.Add()` / change
  tracking would either bypass those procedures (reintroducing the
  business logic in C# and duplicating it) or reduce EF Core to
  `FromSqlRaw`/`ExecuteSqlRaw` calls, at which point it provides no benefit
  over Dapper and adds a heavier `DbContext` object graph to keep in sync
  with schema changes made directly in SQL scripts.
- Some procedures return **multiple result sets** that need `QueryMultiple`
  (e.g. `bil.usp_CreateInvoice` alongside a later
  `bil.usp_RecalculateInvoiceTotals` call pattern; the day-end/reporting
  views), and Dapper's `QueryMultipleAsync` is a direct, simple fit;
  EF Core has no first-class equivalent.

Dapper is used exclusively for stored-procedure execution:
`CommandType.StoredProcedure`, parameters bound by name, `QueryAsync` for
`SELECT`-returning procedures, `ExecuteAsync` for pure side-effect
procedures, and `QuerySingleOrDefaultAsync` for procedures whose `SELECT`
(or `OUTPUT inserted.*`) returns exactly one row.

### Repository pattern

One repository class per functional module, implementing an interface
defined in `Consult360.Application.Interfaces`. Each repository method maps
1:1 to a stored procedure — there is no query-building, no LINQ expression
translation, and no "repository does something clever" logic. All business
logic (validation, computed values not already handled by the procedure,
orchestration across more than one procedure call) lives one layer up, in
`Consult360.Application.Services`.

```csharp
// Consult360.Application/Interfaces/IPatientRepository.cs
public interface IPatientRepository
{
    Task<PatientRow?> GetAsync(Guid patientId, CancellationToken ct);
    Task<IReadOnlyList<PatientRow>> SearchAsync(string term, Guid hospitalId, CancellationToken ct);
    Task<PatientRow> CreateAsync(CreatePatientParams p, CancellationToken ct);
    Task<PatientRow?> UpdateAsync(Guid patientId, UpdatePatientParams p, CancellationToken ct);
}

// Consult360.Infrastructure/Data/Repositories/PatientRepository.cs
public sealed class PatientRepository : IPatientRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PatientRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<PatientRow?> GetAsync(Guid patientId, CancellationToken ct)
    {
        using var conn = _factory.CreateOpen();
        var cmd = new CommandDefinition(
            "opd.usp_GetPatient",
            new { PatientId = patientId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct);
        return await conn.QuerySingleOrDefaultAsync<PatientRow>(cmd);
    }

    public async Task<IReadOnlyList<PatientRow>> SearchAsync(string term, Guid hospitalId, CancellationToken ct)
    {
        using var conn = _factory.CreateOpen();
        var cmd = new CommandDefinition(
            "opd.usp_SearchPatients",
            new { Term = term, HospitalId = hospitalId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct);
        var rows = await conn.QueryAsync<PatientRow>(cmd);
        return rows.AsList();
    }

    public async Task<PatientRow> CreateAsync(CreatePatientParams p, CancellationToken ct)
    {
        using var conn = _factory.CreateOpen();
        // opd.usp_CreatePatient ends with `OUTPUT inserted.PatientId, inserted.Mrn,
        // inserted.FullName, inserted.CreatedAt` — a single-row result set, exactly
        // like every other create procedure in stored-procedures.sql.
        var cmd = new CommandDefinition(
            "opd.usp_CreatePatient",
            p, // anonymous/record param object; property names match @ParamNames
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct);
        return await conn.QuerySingleAsync<PatientRow>(cmd);
    }
}
```

### Handling `OUTPUT inserted.*`

Every `create`/`save`-verb procedure in `stored-procedures.sql` ends its
`INSERT` with an `OUTPUT inserted.<columns>` clause (see the pattern at
`sec.usp_CreateUser`, `opd.usp_CreatePatient`, `cln.usp_CreatePrescription`,
etc. — 20+ occurrences). From ADO.NET/Dapper's perspective this behaves
exactly like a `SELECT`: the procedure returns a one-row result set, so
`QuerySingleAsync<T>`/`QuerySingleOrDefaultAsync<T>` reads it with no
special handling required. No `SqlParameter` with `ParameterDirection.Output`
is needed for these — the pattern the schema already uses is result-set
based, not scalar-output-parameter based, and the repositories should match
that rather than introducing a second convention.

### Multi-result-set handling (`QueryMultiple`)

Procedures that return more than one logical result set — for example a
reporting endpoint that returns both summary rows and a breakdown (`GET
/reports/day-end` → `{ invoiceCount, totalBilled, … }` + `[ { componentType,
paymentMethod, totalAmount, paymentCount } ] }`, backed by
`bil.vw_DayEndSummary` + `bil.vw_PaymentsByComponent`) — are wrapped in a
single procedure that issues two `SELECT`s, consumed with
`QueryMultipleAsync`:

```csharp
public async Task<DayEndSummaryRow> GetDayEndSummaryAsync(Guid hospitalId, DateOnly date, CancellationToken ct)
{
    using var conn = _factory.CreateOpen();
    using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
        "bil.usp_GetDayEndSummary", // wraps the two views in one call
        new { HospitalId = hospitalId, Date = date },
        commandType: CommandType.StoredProcedure, cancellationToken: ct));

    var summary = await multi.ReadSingleAsync<DayEndSummaryRow>();
    var byMethod = (await multi.ReadAsync<PaymentByComponentRow>()).AsList();
    summary.PaymentsByComponent = byMethod;
    return summary;
}
```

*(Where a report is currently expressed as two separate views without a
wrapping procedure, add a thin wrapping procedure — this is schema-additive,
not a redesign of the views.)*

### Connection & transaction management

- `ISqlConnectionFactory` is a scoped-per-request service that opens a
  `SqlConnection` against the connection string for the current
  environment. Every repository call opens, uses, and disposes its own
  connection (`using var conn = _factory.CreateOpen()`); connection pooling
  is handled by ADO.NET, not manually.
- Where a single logical operation spans more than one procedure call (for
  example: create an invoice line item, then recalculate invoice totals),
  the `Application` service wraps both repository calls in an explicit
  `SqlConnection`/`SqlTransaction` passed down via an
  `IUnitOfWork`/`TransactionScope` abstraction, so a failure in the second
  call rolls back the first. Prefer pushing that pairing into a single
  procedure when the two steps are always used together (as
  `bil.usp_CreatePayment` already does by recalculating the invoice's
  `paidAmount`/`status` in the same procedure) — multi-procedure
  application-level transactions are the exception, not the default
  pattern, because most compound operations are already procedure-internal.

### Retry policy for transient SQL Server faults

Wrap `SqlConnection.OpenAsync`/command execution in a Polly retry policy
targeting the small set of transient SQL Server error numbers (`-2`
timeout, `4060`, `40197`, `40501`, `40613`, `49918`, `49919`, `49920`,
`11001`), with exponential backoff and a max of 3 retries, registered once
in `Consult360.Infrastructure.Resilience.SqlResiliencePolicies` and applied
via the `ISqlConnectionFactory` so every repository gets it for free without
each one hand-rolling retry logic.

```csharp
public static IAsyncPolicy GetSqlRetryPolicy() =>
    Policy
        .Handle<SqlException>(ex => TransientSqlErrorNumbers.Contains(ex.Number))
        .Or<TimeoutException>()
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));
```

---

## 5. Business Logic Porting

### Methodology

For each `data.js` module, go through every exported method and classify
it into one of three buckets before writing any C#:

1. **Fully absorbed by the stored procedure.** The procedure already
   implements the rule end-to-end (e.g. BMI computation in
   `cln.usp_SaveVitals`'s `MERGE`, soft-delete semantics in
   `opd.usp_DeleteAllergy`, primary-diagnosis-defaulting in
   `cln.usp_CreateDiagnosis`). The C# service method is a thin pass-through
   to the repository plus DTO mapping — no reimplementation needed, but the
   service-layer unit test should still assert the *contract* (e.g. "first
   diagnosis on a consultation comes back `IsPrimary = true`") so a future
   procedure change that breaks the contract fails a test, not production.

2. **Needs a genuine C# port.** The rule is orchestration or computation
   that either happens before a database call (request validation,
   deriving a query parameter) or was never pushed into a procedure because
   it's read-only/derived and doesn't belong in persistence (e.g. the
   available-slots response shape used by the front end, the permission
   matrix pivot from the flat `(Role, Feature, IsGranted)` grid
   `sec.usp_GetPermissionMatrix` returns into the `{ROLE: {feature: bool}}`
   object `api.js`'s `getPermissionMatrix` contract promises). This is
   real work — see the three worked examples below.

3. **Pre-flight / defense-in-depth logic that duplicates a procedure on
   purpose.** A small number of `data.js` methods (drug-allergy conflict
   checking is the clearest case) exist as safety checks the *client*
   calls before submitting a write. `cln.usp_CheckDrugAllergyConflicts`
   already exists as the server-side equivalent (§5.4), but
   `PEN-TEST-STRATEGY.md` §3.5 explicitly flags that in Phase 1 "the alert
   fires in UI but can be bypassed by calling the API directly." The C#
   port for this class of method is therefore not just "call the
   procedure" — it's "call the procedure from inside
   `PrescriptionService.CreatePrescriptionAsync` itself, before writing the
   prescription, not only when the client asks for a pre-check," closing
   the bypass the pen-test doc identifies.

Go module by module in this order (roughly ascending complexity, and
matching the SPA's own module boundaries so one engineer can own one
module's C# port end-to-end): **Config/Reference data → Patients →
Allergies/Medical History → Scheduling → Consultation core (SOAP, sign-off)
→ Vitals → Symptoms → Diagnoses/ICD-10 → Phrases → Formulary/Prescriptions
→ Lab → Billing → Reports → Notifications → Audit/Permissions/Settings.**

### 5.1 Worked example — effective-dated fee lookup

`data.js`'s `getEffectiveFee` is a small, easy-to-get-subtly-wrong
date-range lookup (open-ended `EffectiveTo`, inclusive boundaries):

```javascript
// js/data.js
getEffectiveFee: (doctorId, consultationType, onDate) => {
  const date = onDate || new Date().toISOString().slice(0, 10);
  return doctorFeeSchedule.find((f) =>
    f.DoctorId === doctorId &&
    f.ConsultationType === consultationType &&
    f.EffectiveFrom <= date &&
    (!f.EffectiveTo || f.EffectiveTo >= date));
},
```

This is backed by `bil.usp_CheckEffectiveFee`, which must reproduce the
exact same boundary semantics in T-SQL (`EffectiveFrom <= @OnDate AND
(EffectiveTo IS NULL OR EffectiveTo >= @OnDate)`). The C# port is a thin
service wrapper — but it is the place to fix the one real risk in the
original: a caller passing an invalid/empty date string. The C# version
makes "today" the actual default via a `TimeProvider`, not a string
default, and validates `onDate` before it ever reaches SQL:

```csharp
// Consult360.Application/Services/Billing/FeeScheduleService.cs
public sealed class FeeScheduleService : IFeeScheduleService
{
    private readonly IBillingRepository _repo;
    private readonly TimeProvider _clock;

    public async Task<FeeScheduleDto?> GetEffectiveFeeAsync(
        Guid doctorId, string consultationType, DateOnly? onDate, CancellationToken ct)
    {
        var effectiveDate = onDate ?? DateOnly.FromDateTime(_clock.GetUtcNow().DateTime);
        var row = await _repo.CheckEffectiveFeeAsync(doctorId, consultationType, effectiveDate, ct);
        return row is null ? null : FeeScheduleDto.FromRow(row);
    }
}
```

Backing procedure (`bil.usp_CheckEffectiveFee`, already written) performs
the actual range filter in T-SQL — the C# layer's job is only to supply a
trustworthy default date and map the DTO, matching bucket 1 above.

### 5.2 Worked example — tri-state symptom toggle

The symptom state machine (`UNSET → YES/NO/NOT_ASKED`, cycled by repeated
clicks in the UI) is persisted by `updateSymptomState`, which is a simple
upsert in `data.js`:

```javascript
// js/data.js
updateSymptomState: (consultationId, symptomCode, newState) => {
  let row = consultationSymptoms.find((s) => s.ConsultationId === consultationId && s.SymptomCode === symptomCode);
  if (!row) {
    row = { SymptomId: nextId('SYMP'), ConsultationId: consultationId, SymptomCode: symptomCode, State: 'UNSET', Duration: null, Severity: null, FreeTextNotes: null };
    consultationSymptoms.push(row);
  }
  row.State = newState;
  return row;
},
```

Two things matter for the C# port that are easy to lose in translation:

- **The tri-state *cycle* itself is UI logic, not data-layer logic** — the
  comment in `api.js`/`data.js` is explicit that "tri-state cycle handled
  by the caller, this just persists." The consultation module in the
  browser decides `UNSET → YES → NO → UNSET` (or whatever the actual cycle
  order is) and sends the resulting state; the API only ever receives and
  persists a final state value. The C# service must **not** try to
  re-derive "next state from current state" — it validates that the
  incoming state is one of `YES`/`NO`/`NOT_ASKED` (the schema's
  `CK_cln_ConsultationSymptom_State` CHECK constraint enum) and persists it
  as given, exactly matching the original's division of responsibility.
- **The upsert-with-default-row pattern** is business logic worth keeping
  explicit in the service layer, not hidden purely in the procedure's
  `MERGE`, because the DTO returned to the client needs the same shape
  whether the row existed before or not:

```csharp
// Consult360.Application/Services/Consultations/SymptomService.cs
public sealed class SymptomService : ISymptomService
{
    private static readonly HashSet<string> AllowedStates = new(StringComparer.Ordinal)
        { "YES", "NO", "NOT_ASKED" };

    public async Task<ConsultationSymptomDto> UpdateStateAsync(
        Guid consultationId, string symptomCode, string newState, Guid hospitalId, CancellationToken ct)
    {
        if (!AllowedStates.Contains(newState))
            throw new ValidationException($"'{newState}' is not a valid symptom state.");

        // cln.usp_UpdateSymptomState performs the find-or-create MERGE itself;
        // the service's job is input validation plus DTO mapping, matching
        // bucket 1 for the persistence rule and bucket 2 for the state-value
        // contract enforcement that data.js never had to do (JS has no enums).
        var row = await _repo.UpdateSymptomStateAsync(consultationId, symptomCode, newState, hospitalId, ct);
        return ConsultationSymptomDto.FromRow(row);
    }
}
```

### 5.3 Worked example — slot-generation algorithm

`listAvailableSlots` is the clearest case of genuine algorithmic logic in
`data.js` — it walks a doctor's weekly schedule in fixed-size increments
and marks which slots are already booked:

```javascript
// js/data.js
listAvailableSlots: (doctorId, hospitalId, dateStr) => {
  const dayNames = ['SUN', 'MON', 'TUE', 'WED', 'THU', 'FRI', 'SAT'];
  const dayOfWeek = dayNames[new Date(dateStr).getDay()];
  const config = doctorSlotConfig.find((s) => s.DoctorId === doctorId && s.DayOfWeek === dayOfWeek && s.IsActive);
  if (!config) return [];
  const booked = appointments
    .filter((a) => a.DoctorId === doctorId && a.SlotStart.slice(0, 10) === dateStr && a.Status !== 'CANCELLED')
    .map((a) => a.SlotStart);
  const slots = [];
  const [startH, startM] = config.StartTime.split(':').map(Number);
  const [endH, endM] = config.EndTime.split(':').map(Number);
  let current = startH * 60 + startM;
  const end = endH * 60 + endM;
  while (current + config.SlotDurationMinutes <= end) {
    const hh = String(Math.floor(current / 60)).padStart(2, '0');
    const mm = String(current % 60).padStart(2, '0');
    const slotStart = `${dateStr}T${hh}:${mm}:00`;
    const nextMin = current + config.SlotDurationMinutes;
    /* ...builds slotEnd the same way... */
    slots.push({ SlotStart: slotStart, SlotEnd: slotEnd, IsBooked: booked.includes(slotStart) });
    current = nextMin;
  }
  return slots;
},
```

This exact algorithm has **already been ported to T-SQL** in
`opd.usp_ListAvailableSlots`, using a recursive CTE to walk the same
minute-increment loop and a day-of-week lookup table to avoid
locale-dependent `DATENAME`:

```sql
;WITH Slots AS (
    SELECT CAST(@StartTime AS TIME) AS SlotTimeStart
    UNION ALL
    SELECT DATEADD(MINUTE, @DurationMin, SlotTimeStart) FROM Slots
    WHERE DATEADD(MINUTE, @DurationMin, SlotTimeStart) < CAST(@EndTime AS TIME)
)
SELECT
    CONVERT(NVARCHAR(10), @AppointmentDate, 23) + 'T' + FORMAT(SlotTimeStart, 'HH\:mm') + ':00' AS SlotStart,
    CONVERT(NVARCHAR(10), @AppointmentDate, 23) + 'T' + FORMAT(DATEADD(MINUTE, @DurationMin, SlotTimeStart), 'HH\:mm') + ':00' AS SlotEnd,
    CASE WHEN EXISTS (SELECT 1 FROM opd.Appointment a WHERE ... AND a.Status <> 'CANCELLED') THEN 1 ELSE 0 END AS IsBooked
FROM Slots
OPTION (MAXRECURSION 200);
```

This is a bucket-1 case: the algorithm is already fully implemented
server-side, and the C# service is deliberately a thin pass-through —
**but** it is exactly the kind of logic where "thin pass-through" still
needs a real regression test, because a subtle SQL/JS mismatch here (an
off-by-one on the loop boundary, an inclusive vs. exclusive end time) would
silently double-book or drop the last slot of the day. The parity test for
this endpoint (§11) runs the same `(doctorId, date)` inputs against both
the Phase 1 `data.js` implementation and the Phase 4 API and diffs the
returned slot arrays element-for-element, specifically because this is
loop/boundary logic, not a simple filter:

```csharp
// Consult360.Application/Services/Scheduling/SchedulingService.cs
public async Task<IReadOnlyList<AvailableSlotDto>> ListAvailableSlotsAsync(
    Guid doctorId, Guid hospitalId, DateOnly date, CancellationToken ct)
{
    var rows = await _repo.ListAvailableSlotsAsync(doctorId, hospitalId, date, ct);
    // No re-derivation of the slot grid in C# — opd.usp_ListAvailableSlots is the
    // single source of truth for slot boundaries, matching data.js's contract that
    // "no config for that weekday" returns an empty array, not an error.
    return rows.Select(AvailableSlotDto.FromRow).ToList();
}
```

### 5.4 Worked example — drug-allergy conflict checking (bucket 3)

`checkDrugAllergyConflicts` combines a direct substring match against the
patient's active allergies with a hard-coded penicillin-class rule:

```javascript
// js/data.js
checkDrugAllergyConflicts: (patientId, medicineName, composition) => {
  const allergies = patientAllergies.filter((a) => a.PatientId === patientId && a.Status === 'ACTIVE');
  const alerts = [];
  const drugLower = (medicineName + ' ' + (composition || '')).toLowerCase();
  allergies.forEach((a) => {
    const allergyLower = a.AllergyName.toLowerCase();
    if (drugLower.includes(allergyLower) || allergyLower.includes(medicineName.toLowerCase().split(' ')[0])) {
      alerts.push({ Severity: 'CRITICAL', AllergyName: a.AllergyName, AllergySeverity: a.Severity, Message: `...` });
    }
    if (allergyLower.includes('penicillin') && (drugLower.includes('amoxicillin') || drugLower.includes('ampicillin') || drugLower.includes('penicillin') || drugLower.includes('augmentin'))) {
      if (!alerts.some((al) => al.AllergyName === a.AllergyName)) alerts.push({ /* ... */ });
    }
  });
  return alerts;
},
```

`cln.usp_CheckDrugAllergyConflicts` already reproduces this exact
substring/class logic in T-SQL. The genuine C# work here is **not**
reimplementing the matcher — it's closing the bypass path
`PEN-TEST-STRATEGY.md` §3.5 calls out by name ("Bypass drug-allergy check
— Alert fires in UI but can be bypassed by calling the API directly"):

```csharp
// Consult360.Application/Services/Prescriptions/PrescriptionService.cs
public async Task<PrescriptionDto> CreatePrescriptionAsync(
    CreatePrescriptionRequest req, Guid hospitalId, CancellationToken ct)
{
    // Server-side enforcement: the same check the SPA runs client-side before
    // submitting is run again here, unconditionally, before the write. The
    // caller cannot skip it by calling POST /consultations/{id}/prescriptions
    // directly — closing PEN-TEST-STRATEGY.md §3.5's "bypass drug-allergy
    // check" finding for Phase 4.
    var conflicts = await _drugAllergyCheck.CheckAsync(
        req.PatientId, req.MedicineName, req.Composition, ct);

    var critical = conflicts.Where(c => c.Severity == "CRITICAL").ToList();
    if (critical.Count > 0 && !req.OverrideAcknowledged)
    {
        // Non-blocking by product design (data.js/UI never hard-blocks either) —
        // but the override must be explicit and is what gets audited, not silently
        // accepted.
        return PrescriptionDto.RequiresOverride(critical);
    }
    if (critical.Count > 0 && req.OverrideAcknowledged)
    {
        await _audit.RecordAsync(new AuditEntry(hospitalId, req.PrescriberUserId,
            "OVERRIDE", "Prescription", null,
            $"Drug-allergy alert overridden for {req.MedicineName}"), ct);
    }

    var created = await _repo.CreatePrescriptionAsync(req, hospitalId, ct);
    return PrescriptionDto.FromRow(created);
}
```

This preserves the product's non-blocking-alert philosophy (the doctor can
still prescribe after acknowledging) while making the check something the
server enforces rather than something the client merely happens to call.

### Module-by-module bucket summary

| Module | Representative methods | Bucket |
|---|---|---|
| Patients | `searchPatients`, `createPatient` (MRN generation), `updatePatient` | 1 — procedures already implement search/MRN/upsert |
| Allergies | `createAllergy`, `deleteAllergy` (soft delete → `Status='INACTIVE'`, mapped to schema's `RESOLVED` — see API-SPEC "Known Gaps" #1) | 1, with a documented semantic gap to resolve or accept |
| Scheduling | `listAvailableSlots` | 1, but algorithmic — needs the parity test in §11, not just a contract test |
| Consultation core | `getOrCreateConsultation`, `confirmSignOff` (4-point attestation, locks record) | 1 for persistence; 2 for the "reject writes to a locked consultation" guard, which must be enforced in the service layer *and* the procedure (defense in depth, since `saveSoapNotes` etc. are separate calls) |
| Vitals | `saveVitals` | 1 — BMI computed in the procedure's `MERGE`, more complete than `data.js` itself (see §5.1's callout) |
| Symptoms | `updateSymptomState` | 2 — state-value validation genuinely belongs in C#, cycle logic stays client-side |
| Diagnoses | `createDiagnosis` (first diagnosis defaults `isPrimary`) | 1 |
| Prescriptions | `createPrescription`, `checkDrugAllergyConflicts` | 3 — see §5.4 |
| Billing | `getEffectiveFee`, `recalculateInvoiceTotals`, `cancelInvoice` (issues a credit note) | 1, with the date-default fix in §5.1 |
| Reports | all `list*Report` methods | 1 — pure aggregation queries/views |
| Audit | `recordAuditEntry` | 2 — server must populate `IpAddress`/`UserAgent` itself (client-supplied values are not trustworthy for an audit trail; `data.js`'s comment already flags these as "server-populated in Phase 4+") |

---

## 6. API Layer / Controllers

Controllers are organized to match the SPA's own module boundaries and
`API-SPECIFICATION.md`'s section breakdown (13 controllers for 130
endpoints), not a generic CRUD-per-table scaffold. Every controller action
maps to exactly one row in `API-SPECIFICATION.md`; the specification
document remains the source of truth for exact routes/params/shapes and is
not re-derived here — see §14 for the pointer and a representative sample
per module.

### Conventions

- `[ApiController]` on every controller — automatic 400 responses for
  invalid model state, automatic `[FromBody]` inference.
- DTOs, never entity/row types, cross the controller boundary in either
  direction. `Consult360.Domain` row types (`PatientRow`, `AppointmentRow`,
  …) never appear in a controller signature.
- Validation via FluentValidation validators registered in
  `Consult360.Application`, invoked through
  `AddFluentValidationAutoValidation()` so a bad request never reaches a
  service method — this replaces `data.js`'s inline validation
  (`DATA-LAYER-ARCHITECTURE.md` §8's length/format/enum checks) with the
  same rules expressed as validator classes, one per DTO.
- Routing follows `API-SPECIFICATION.md` literally — nested resource routes
  (`/patients/{id}/allergies`, `/consultations/{id}/vitals`) map to
  `[Route("api/patients/{patientId:guid}/allergies")]`-style attribute
  routes on the owning controller, not a flat route table.
- API versioning: URL-segment versioning (`/api/v1/...`) from day one via
  `Asp.Versioning.Http`, even though only one version exists at cutover —
  this avoids a breaking route change the first time the API needs to
  evolve independently of the SPA.
- Global exception handling middleware converts unhandled exceptions into
  a consistent JSON error envelope (`{ "error": { "code", "message",
  "traceId" } }`) and — per `PEN-TEST-STRATEGY.md` §3.6 — never leaks stack
  traces or file paths outside `Development`.

### Worked example — `PatientsController.GetPatient`

```csharp
[ApiController]
[Route("api/v1/patients")]
[Authorize(Policy = "RequirePatients")]
public sealed class PatientsController : ControllerBase
{
    private readonly IPatientService _service;
    private readonly IAuditService _audit;
    private readonly IHospitalContextAccessor _hospitalContext;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PatientDto>> GetPatient(Guid id, CancellationToken ct)
    {
        var patient = await _service.GetPatientAsync(id, ct);
        if (patient is null) return NotFound();

        // AUDIT: API-SPECIFICATION.md §3 requires a READ audit entry on this route.
        await _audit.RecordAsync(new AuditEntry(
            _hospitalContext.HospitalId, _hospitalContext.UserId,
            "READ", "Patient", id.ToString(), null), ct);

        return Ok(patient);
    }
}
```

### Worked example — `ConsultationsController.SaveVitals`

```csharp
[ApiController]
[Route("api/v1/consultations/{consultationId:guid}/vitals")]
[Authorize(Policy = "RequireConsultation")]
public sealed class VitalsController : ControllerBase
{
    private readonly IVitalsService _service;

    [HttpGet]
    public async Task<ActionResult<VitalsDto>> GetVitals(Guid consultationId, CancellationToken ct)
    {
        var vitals = await _service.GetVitalsAsync(consultationId, ct);
        return vitals is null ? NotFound() : Ok(vitals);
    }

    [HttpPost]
    public async Task<ActionResult<VitalsDto>> SaveVitals(
        Guid consultationId, [FromBody] SaveVitalsRequest req, CancellationToken ct)
    {
        var hospitalId = HttpContext.Items["HospitalId"] as Guid? ?? throw new UnauthorizedAccessException();
        var saved = await _service.SaveVitalsAsync(consultationId, hospitalId, req, ct);
        // cln.usp_SaveVitals is a no-op against a locked consultation per the
        // API spec ("no-op if isLocked"); the service returns the current row
        // either way, so the response contract matches data.js's saveVitals,
        // which never itself checked IsLocked — the lock guard lives in the
        // procedure/service, not duplicated ad hoc per field.
        return Ok(saved);
    }
}
```

### Worked example — `PrescriptionsController.CreatePrescription`

```csharp
[HttpPost]
[Route("api/v1/consultations/{consultationId:guid}/prescriptions")]
[Authorize(Policy = "RequireConsultation")]
public async Task<ActionResult<PrescriptionDto>> CreatePrescription(
    Guid consultationId, [FromBody] CreatePrescriptionRequest req, CancellationToken ct)
{
    req = req with { ConsultationId = consultationId };
    var hospitalId = _hospitalContext.HospitalId;
    var result = await _service.CreatePrescriptionAsync(req, hospitalId, ct);

    if (result.RequiresOverride)
        return Conflict(result); // 409 — client must resubmit with OverrideAcknowledged=true

    await _audit.RecordAsync(new AuditEntry(hospitalId, _hospitalContext.UserId,
        "CREATE", "Prescription", result.PrescriptionId.ToString(), null), ct);
    return CreatedAtAction(nameof(GetPrescription), new { consultationId, id = result.PrescriptionId }, result);
}
```

### Response envelope

`api.js`'s Phase 1 contract wraps every value in a resolved `Promise` with
no error envelope (`ok(value)` — errors throw as JS exceptions). Phase 4
keeps successful responses as bare JSON bodies (matching the shapes in
`API-SPECIFICATION.md` exactly, so `api.js`'s `fetch()` bodies need no
unwrapping logic beyond `response.json()`), and standardizes only the
**error** shape via the exception-handling middleware, since Phase 1 never
had a consistent error contract to begin with — `api.js`'s Phase 4 `fetch`
bodies check `response.ok` and throw a JS `Error` built from the envelope's
`message` field for any non-2xx response, preserving today's
throw-on-failure calling convention for UI modules.

---

## 7. Authentication, Authorization & Multi-Tenancy

### Replacing `session-manager.js` / `app-context.js`

Phase 1's session model is entirely client-side: `session-manager.js`
generates a random token in the browser, stores it in `localStorage`,
enforces "one active session per user" via a `storage` event listener
across tabs, and auto-expires after 15 minutes of inactivity — none of
which the server can trust or verify (`PEN-TEST-STRATEGY.md` §3.1 flags
exactly this: "Session in JavaScript memory," "Vertical privilege
escalation... AppContext stores role in JS memory — can be modified via
dev tools"). `app-context.js`'s `_session.userRole`/`hospitalId` are
similarly just JS variables set at login with no server-side verification
on subsequent calls.

Phase 4 replaces this with a **hybrid JWT + server-tracked session**
design, because the schema already anticipates both halves of it:
`sec.usp_AuthenticateUser`, `sec.usp_CreateUserSession`, and
`sec.usp_CheckSessionValid` exist in `stored-procedures.sql` — a
`sec.UserSession` table backing real server-side session tracking already
exists in the schema, it's simply unused by the Phase 1 client-only
implementation.

- **JWT bearer token** — issued at login, contains `sub` (UserId), `role`,
  and a `HospitalId` claim. Every subsequent request is authenticated by
  validating this token's signature and expiry (stateless, no DB round trip
  needed for authorization itself). Short-lived (e.g. 15 minutes, matching
  the SPA's existing inactivity window) with a refresh-token flow, since
  ASP.NET Core Identity's default cookie model doesn't fit a pure API +
  separately-hosted SPA well — bearer tokens do.
- **Server-side `sec.UserSession` row per login** — created via
  `sec.usp_CreateUserSession` at the same time the JWT is issued. This
  row is what gives Phase 4 the "only one active session per user" behavior
  `session-manager.js` today only approximates via `localStorage` (which
  fails across different browsers/devices and can't be revoked by an
  admin). `sec.usp_CheckSessionValid` backs an explicit
  `GET /auth/session` endpoint the SPA can poll instead of trusting its own
  `localStorage` — and an admin-initiated revoke (`ADMIN_REVOKE`, already a
  reason code `session-manager.js`'s expired-modal UI anticipates) becomes
  possible for the first time, by simply invalidating the session row.
- **Password hashing** — `sec.ApplicationUser.PasswordHash` currently holds
  literal demo strings (`'demo123'`) in the seed data; `PEN-TEST-STRATEGY.md`
  §4 flags this as a Critical pre-test item ("Hash passwords — bcrypt/scrypt
  in Phase 2"). `sec.usp_AuthenticateUser` compares `@PasswordHash` as
  received, so the **hashing happens in C#** (via
  `Microsoft.AspNetCore.Identity.PasswordHasher<T>` or `BCrypt.Net-Next`)
  before the hash is ever sent to the procedure — the procedure's contract
  (compare a hash to a stored hash) does not need to change, only what the
  application computes and stores as that hash. Seed data must be
  re-hashed as part of database seeding (§10), not left as plaintext.
- **Login rate limiting** — ASP.NET Core's built-in `Microsoft.AspNetCore.
  RateLimiter` middleware, a fixed-window limiter on `POST /auth/login`
  keyed by IP + attempted email, closing `PEN-TEST-STRATEGY.md` §4 item 2
  ("5 failures → 15-minute lockout").

### `HospitalId` scoping — server-enforced, not client-supplied

`api.js`'s `_hid()` helper is a **convenience default**, not a security
boundary — every hospital-scoped method signature accepts an explicit
`hospitalId` parameter that the caller *could* override, and Phase 1 has
no server to reject a mismatched one. `PEN-TEST-STRATEGY.md` §3.4 names
this directly: "`_hid()` injects session hospital as fallback — but
doesn't reject explicit wrong IDs... Phase 2: server middleware must
REJECT any request where provided HospitalId ≠ JWT HospitalId."

Phase 4 implements this as `HospitalContextMiddleware`:

```csharp
public sealed class HospitalContextMiddleware
{
    private readonly RequestDelegate _next;

    public async Task InvokeAsync(HttpContext context, ISessionContextSetter sessionContext)
    {
        var claim = context.User.FindFirst("HospitalId");
        if (claim is null || !Guid.TryParse(claim.Value, out var hospitalId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Any client-supplied HospitalId in the query string or body is
        // IGNORED for authorization purposes — this claim is the only source
        // of truth, closing PEN-TEST-STRATEGY.md §3.4's IDOR finding.
        context.Items["HospitalId"] = hospitalId;

        // Set SQL Server SESSION_CONTEXT for this connection's lifetime so
        // the database's own Row-Level Security policy (architecture §6)
        // enforces the same boundary even if application code has a bug.
        await sessionContext.SetHospitalIdAsync(hospitalId);

        await _next(context);
    }
}
```

Controllers and services read `HttpContext.Items["HospitalId"]` (or an
injected `IHospitalContextAccessor` wrapping it), never a client-supplied
value, for every repository call that needs a `HospitalId` parameter.

### Row-Level Security

`DATA-LAYER-ARCHITECTURE.md` §6 already specifies the RLS design
(`sec.fn_HospitalFilter` + a `SECURITY POLICY` per hospital-scoped table,
filtering on `SESSION_CONTEXT('HospitalId')`). Phase 4's job is to (a)
apply those policies as part of the database deployment (§10) across all
hospital-scoped tables, not only `opd.Patient`, and (b) have every
connection opened by `ISqlConnectionFactory` call
`sp_set_session_context @key = N'HospitalId', @value = @HospitalId` (via
`ISessionContextSetter`) immediately after opening, before any repository
query runs on that connection — this is what makes RLS actually apply per
request rather than being dormant infrastructure. This is deliberately
**defense in depth**: the `HospitalId` parameter every stored procedure
already accepts is still passed explicitly and still filters explicitly;
RLS is the database-level backstop if that application-level filter is
ever missed on a new procedure.

### Policy-based authorization from the permission matrix

`data.js`'s `permissionMatrix` is a static, in-memory `{ROLE: {feature:
bool}}` grid checked by `hasPermission(role, feature)` — called from
`router.js`'s route guard on the client, with no server-side equivalent
today. `sec.PermissionMatrix` already exists in the schema as a
**per-hospital-configurable** table (`sec.usp_GetPermissionMatrix` filters
`WHERE HospitalId = @HospitalId`) — a real improvement over `data.js`'s
single global matrix that the migration should adopt, not flatten back
down to a hard-coded grid (see §13's risk about permission-matrix drift for
why this consistency matters).

Seventeen feature flags exist today (`consultation`, `patients`, `scheduling`,
`billing`, `reports`, `admin_hospital`, `admin_scheduling`, `admin_vitals`, `admin_specialty`,
`admin_formulary`, `admin_phrases`, `admin_users`, `audit_log`,
`data_export`, `data_import`, `print`, `lab_portal`), each becoming an
ASP.NET Core authorization policy backed by a custom
`IAuthorizationHandler` that queries the per-hospital matrix (cached
per-request, refreshed on a short TTL, since it's now configurable and no
longer a compile-time constant):

```csharp
public sealed class FeaturePermissionHandler : AuthorizationHandler<FeaturePermissionRequirement>
{
    private readonly IPermissionService _permissions;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, FeaturePermissionRequirement requirement)
    {
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        var hospitalId = context.User.FindFirst("HospitalId")?.Value;
        if (role is null || hospitalId is null) return;

        if (await _permissions.HasPermissionAsync(Guid.Parse(hospitalId), role, requirement.Feature))
            context.Succeed(requirement);
    }
}

// Program.cs
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RequireConsultation", p => p.Requirements.Add(new FeaturePermissionRequirement("consultation")))
    .AddPolicy("RequirePatients", p => p.Requirements.Add(new FeaturePermissionRequirement("patients")))
    // ... one per feature flag, generated from the same feature-name list
    //     sec.usp_ListPermissionFeatures returns, so the policy set can never
    //     silently drift from the database's own feature list.
    ;
```

This is the direct Phase 4 replacement for `router.js`'s `ROUTE_PERMS` map
and `_checkPermission` guard — the client-side guard is **not removed**
(it still gives instant UI feedback / hides inaccessible nav items without
a round trip) but it is no longer the enforcement point; every request
still passes through the server-side policy check regardless of what the
client-side router decided.

---

## 8. Client-Side Engines Decision

All five engines in `js/engines/` **stay 100% client-side in Phase 4.
None require a C# service port.** This is a deliberate, per-engine
determination from reading each file, not a default assumption — the
opposite call (port everything to a server-side rules engine, as a
differently-shaped prior migration guide for an unrelated product did) does
not fit what these engines actually do here.

| Engine | What it does | Inputs | Blocking? | Verdict |
|---|---|---|---|---|
| **Voice SOAP** (`engine-voice-soap.js`) | Wraps the browser's native `SpeechRecognition`/`webkitSpeechRecognition` Web Speech API, parses spoken section names ("Subjective"/"Objective"/"Plan"), writes transcript text directly into SOAP `<textarea>` DOM elements | Live microphone audio, browser API | No — dictation aid only | **Client-only, cannot be ported.** This is a wrapper around a browser API that has no server-side equivalent; there is nothing to move. |
| **Branched Flow** (`engine-branched-flow.js`) | Pure function: complaint keywords + demographic/country context → suggested labs/ICD-10 codes/vitals-to-highlight + a banner string. Returns UI directives; caller renders them | Data already fetched to the browser (active complaints, patient age/gender, specialty, country) | No — advisory suggestions only, never writes anything | **Stays client.** Zero PHI leaves the browser differently than today; nothing it computes is persisted or trusted for a decision; it is presentation logic, not a clinical safety gate. |
| **SOAP Composer** (`engine-soap-composer.js`) | Pure function: aggregates vitals/symptoms/diagnoses/medications/lab orders already on screen into a draft S/O/A/P text block | Data already fetched to the browser | No — draft text the doctor must review/edit before `saveSoapNotes` persists anything | **Stays client.** It only ever produces a *draft* the doctor edits; the actual persistence path (`saveSoapNotes` → `cln.usp_SaveSoapNotes`) is unchanged and already server-validated (rejects writes to a locked consultation). |
| **Rx Reconciler** (`engine-rx-reconciler.js`) | Pure function: dosage-delta %, therapeutic-class duplicate, and same-visit/active-medication overlap checks against a candidate prescription | Patient's active meds + current-visit meds, already fetched to the browser | No — produces a non-blocking alert ribbon; not called through `api.js`/`data.js` at all today | **Stays client**, with one caveat: unlike `checkDrugAllergyConflicts` (a `data.js`/procedure-backed method — see §5.4), this engine's checks have **no server-side equivalent today** and are not exposed as an API method. Because they are advisory-only and the product's own design never blocks on them, this is an acceptable gap for this migration phase, not a regression — but it is a real difference from the allergy check and should be flagged in a future compliance review if the business ever wants dosage-delta/duplicate-class warnings to be non-bypassable (which would require adding a new stored procedure and API endpoint that does not exist yet, out of scope here). |
| **Contradiction Engine** (`engine-contradiction.js`) | Pure function: vitals-vs-diagnosis conflict rules, laterality mismatch detection, pediatric weight-based dose limit checks | Data already fetched to the browser | No — same non-blocking alert ribbon pattern, same "not currently exposed via api.js" status as Rx Reconciler | **Stays client**, same caveat as Rx Reconciler above. |

**Why this differs from a "port every engine" default.** `PRODUCT-GUIDE.md`
itself describes all five as "a pure function the consultation module calls
and then renders the result of — none of them block the doctor outright."
None of the four non-voice engines are invoked through `api.js` or have a
corresponding `data.js` method or stored procedure — they operate
exclusively on data the browser already holds after calling the real API
methods, and their output is advisory, never authoritative. Porting them
to C# would add a server round trip for a UI convenience with no security
or data-integrity benefit, since nothing they compute is ever trusted as
the final word (contrast this with `checkDrugAllergyConflicts`, which *is*
a `data.js`/procedure-backed method with a real server-side implementation
port in §5.4, precisely because it is treated as a safety check, not
merely a UI hint).

---

## 9. Front-End Integration

### `js/api.js` — the only SPA file that changes

Every method body changes from `ok(window.Data.xxx())` to a `fetch()` call
against the new API, preserving the exact method name and parameter order
so every UI module that calls `window.API.xxx(...)` needs zero changes:

```javascript
// Before (Phase 1)
getPatient: (id) => ok(window.Data.getPatient(id)),

// After (Phase 4)
getPatient: (id) => _fetch(`/api/v1/patients/${id}`),
```

```javascript
// js/api.js — Phase 4 shared fetch helper, replaces the ok()/_hid() helpers
const API_BASE = window.Consult360Config?.apiBaseUrl || '/api/v1';

async function _fetch(path, options = {}) {
  const token = window.SessionManager ? window.SessionManager.getToken() : null;
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(options.headers || {}),
    },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error?.message || `Request failed: ${res.status}`);
  }
  if (res.status === 204) return null;
  return res.json();
}
```

`_hid()`'s role — silently defaulting a missing `hospitalId` parameter —
is retired from `api.js` itself, because the server now derives
`HospitalId` from the JWT unconditionally (§7) and ignores any
client-supplied value for authorization purposes; `api.js` can keep sending
it for readability/backward compatibility with UI call sites, but it is no
longer load-bearing.

### CORS configuration

If the SPA is hosted on a different origin than the API (see the two
deployment options below), CORS must allow the SPA's origin with
credentials for the `Authorization` header:

```csharp
// Program.cs
builder.Services.AddCors(options => options.AddPolicy("Spa", policy =>
    policy.WithOrigins(builder.Configuration["Spa:Origin"]!)
          .AllowAnyHeader()
          .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
          .AllowCredentials()));

var app = builder.Build();
app.UseCors("Spa");
```

### Static file serving — two options

| Option | Description | Trade-offs |
|---|---|---|
| **A — Serve the SPA from `wwwroot` under the ASP.NET Core app** | Copy `index.html`, `js/`, `css/`, `templates/` etc. into `Consult360.Api/wwwroot`; enable `app.UseStaticFiles()` and a SPA fallback route. `api.js`'s `API_BASE` becomes a relative path (`/api/v1`), no CORS needed at all. | Simplest deployment (one app, one URL, one TLS cert); ties the SPA's release cadence to the API's; every API deploy also redeploys the SPA even if it didn't change. |
| **B — Keep the SPA on its existing static host** (the current Python/static server, or any CDN/static hosting) and point `api.js` at the API's separate base URL | `API_BASE` is an absolute URL (`https://api.consult360.example/api/v1`); CORS (above) is required. | Decouples SPA and API release cycles; matches the current Phase 1 deployment model most closely; requires CORS configuration and a second TLS certificate/domain. |

Recommendation: **Option A for the initial Phase 4 cutover** (fewer moving
parts while everything else is changing at once), with the explicit note
that switching to Option B later is a configuration change only
(`API_BASE` + CORS), not a code change, since `api.js` already isolates
every network call behind that one constant.

### What stays untouched

`index.html`, `app.js`, `router.js`, `session-manager.js` (only its
`getToken()`/session-check calls now hit the real `GET /auth/session`
endpoint instead of trusting `localStorage` alone), `app-context.js` (its
`init()` already calls `window.API.getHospital()`, so it needs no change —
only what that call does underneath changes), every file under
`js/modules/`, `js/components/`, `js/templates/`, and all five files under
`js/engines/` (§8). This is the direct payoff of the `api.js` facade
pattern `DATA-LAYER-ARCHITECTURE.md` was designed to enable.

---

## 10. Database Deployment & Migrations Strategy

`schema.sql` and `stored-procedures.sql` are hand-authored T-SQL, not
EF Core Code-First output — versioning them going forward needs a
SQL-script-based migration tool, not `dotnet ef migrations`.

### Recommended: DbUp

DbUp runs a numbered/timestamped sequence of `.sql` scripts against a
target database, tracking which have already run in a `SchemaVersions`
table, and is a small enough dependency (a single NuGet package, invoked
from a console app or CI step) to fit a project of this size without the
overhead of a full SSDT `.sqlproj`/database-project build pipeline. An SSDT
database project is a reasonable alternative if the team prefers
Visual-Studio-integrated schema comparison/publish tooling — either choice
satisfies the same requirement (versioned, repeatable, idempotent-safe
script application); this guide recommends DbUp for its lower operational
overhead and straightforward CI integration.

```
db/
├── Scripts/
│   ├── 0001_InitialSchema.sql        (= current docs/schema.sql, split per schema if large)
│   ├── 0002_StoredProcedures.sql     (= current docs/stored-procedures.sql)
│   ├── 0003_RowLevelSecurityPolicies.sql
│   ├── 0004_AlwaysEncryptedColumns.sql   (Phase 2+ per ENCRYPTION-STRATEGY.md timeline)
│   └── 0005_SeedDemoData.sql
├── Consult360.DbMigrator/            (console app, runs DbUp against a connection string)
└── Consult360.DbMigrator.Tests/      (verifies scripts apply cleanly to a fresh DB)
```

Stored procedures are re-deployable by re-running their `CREATE OR ALTER
PROCEDURE` scripts (every procedure in `stored-procedures.sql` already uses
`CREATE OR ALTER`, which is idempotent by design) — these can live in their
own always-reapplied script category (DbUp supports "always run" scripts
distinct from "run once" migrations), so a procedure fix doesn't need a new
numbered migration file each time.

### Environment promotion

`dev → test → staging → production`, each with its own connection string
in `appsettings.{Environment}.json` / environment variables (never
committed secrets — use a secrets manager or ASP.NET Core User Secrets
locally, Azure Key Vault or equivalent in deployed environments). The
`DbMigrator` console app runs as an explicit CI/CD pipeline step before the
API deployment step in every environment, so schema/procedure changes are
always applied before the code that depends on them goes live.

### Seeding demo data

`data.js`'s seeded arrays (2 hospitals, 7 users, 5 patients, allergies,
formulary, lab catalog, phrase bank, etc.) become an idempotent
`0005_SeedDemoData.sql` script using `MERGE` (or `IF NOT EXISTS INSERT`) so
re-running it against an already-seeded database is a no-op rather than a
duplicate-row error — this preserves the exact same demo dataset
`testing-guide.md`'s manual test scripts assume (same seeded patients,
same login credentials), just sourced from SQL instead of a JS array. One
important adjustment versus the literal `data.js` content: seeded
`PasswordHash` values must be real hashes of the documented demo passwords
(`demo123`, `admin123`, `lab123`), not the plaintext strings themselves —
see §7's password-hashing note.

---

## 11. Testing & Parity Strategy

### `testing-guide.md` becomes the regression/parity contract

`testing-guide.md`'s manual QA script — organized by module (Patients,
Consultation 360°, Scheduling, Billing, Lab Portal, Reports,
Administration), each with specific seeded users/patients and expected
outcomes — is the behavioral contract this migration must not break. Every
section of that guide becomes either an `Consult360.IntegrationTests` test
case (automatable) or an explicit manual regression pass against the new
API-backed deployment before cutover (for anything genuinely UI-only, like
visual layout). The seeded data referenced throughout it (`PAT-001`
through `PAT-005`, the `USR-00x` login credentials, `HOSP-001`/`HOSP-002`)
must resolve to the same records post-migration — which is exactly what
§10's idempotent seed script guarantees.

### Test project structure

- **`Consult360.UnitTests`** (xUnit, no database) — tests
  `Consult360.Application` services in isolation, with repository
  interfaces mocked (Moq/NSubstitute). This is where the bucket-2 logic
  from §5 gets its real test coverage: slot-generation boundary cases
  (no config for a weekday → empty array; last slot exactly fitting the
  end time; a fully booked day), symptom-state enum validation, fee-lookup
  date-boundary cases (`EffectiveTo` null vs. set, exact boundary dates),
  and the drug-allergy override/audit flow.
- **`Consult360.IntegrationTests`** (xUnit + `Testcontainers.MsSql`) —
  spins up a real SQL Server 2019 container per test run, applies
  `db/Scripts/` via the `DbMigrator`, and exercises repositories directly
  against real stored procedures — this is the only way to catch
  parameter/type mismatches between C# DTOs and procedure signatures
  (§13's risk table), and the only reliable way to test `MERGE`-based
  upserts, `OUTPUT inserted.*` shapes, and RLS policies end-to-end. A
  LocalDB-based fallback is acceptable for local development machines
  without Docker, but CI runs against the containerized SQL Server 2019 to
  match production as closely as possible.
- **API contract tests** (`WebApplicationFactory<Program>`) — assert that
  each endpoint's actual JSON response shape matches what
  `API-SPECIFICATION.md` documents (field names/types, not just status
  codes), catching drift between the specification document and the
  implementation before it reaches the SPA, which has no runtime schema
  validation of its own.

### Parity testing methodology

For any endpoint backed by genuine algorithmic logic (§5.3's slot
generation is the paradigm case, but the same applies to the drug-allergy
matcher and the permission-matrix pivot), write a parity test that runs
the **same inputs** against the Phase 1 `data.js` implementation (via a
headless JS test runner, e.g. Node + the existing `data.js` file loaded
directly) and the Phase 4 API, and diffs the outputs. This is stronger
than either side's unit tests alone, because it directly verifies the "no
change to behavior" guarantee this whole migration is built around,
rather than trusting that both implementations independently satisfy the
same prose description of the rule.

---

## 12. Migration Sequence & Phase Gates

| Phase | Scope | Exit gate |
|---|---|---|
| **P0 — Frozen inputs** | `schema.sql`, `stored-procedures.sql`, `API-SPECIFICATION.md`, `DATA-LAYER-ARCHITECTURE.md` — already exist, already reviewed. No new work in this phase; this table starts from here. | Sign-off that these four documents are the frozen contract for everything below; any change to them after this point requires an explicit addendum, not a silent edit mid-migration. |
| **P1 — Solution skeleton** | Create the 4 .NET projects (§3), wire up DI, `Program.cs`'s middleware pipeline, `appsettings.*.json` per environment, `ISqlConnectionFactory` against a dev SQL Server 2019 instance with `schema.sql`/`stored-procedures.sql` already applied via DbUp (§10). No business logic yet — one smoke-test endpoint (`GET /api/v1/health`) proves the pipeline end-to-end. | Solution builds, `dotnet test` runs (empty test suite passes), health endpoint returns 200 against a real SQL Server 2019 connection, DbUp applies cleanly to a fresh database. |
| **P2 — Business logic port, module by module** | Work through the module order in §5's methodology, one module at a time: repository → service → unit tests → integration tests, for that module only, before moving to the next. Each module's stored procedures already exist; this phase is the C# side only. | Every module in §5's table has a `Consult360.Application` service with unit test coverage for its bucket-2/bucket-3 logic, and an `Consult360.IntegrationTests` suite exercising its repository against a real database. No controllers wired yet. |
| **P3 — Controllers + auth** | Wire every service from P2 into a controller action matching `API-SPECIFICATION.md` (§6), implement JWT issuance + `HospitalContextMiddleware` + policy-based authorization (§7). | Every one of the 130 endpoints in `API-SPECIFICATION.md` is implemented and covered by an API contract test (§11); `swagger.json`/OpenAPI output matches the specification document; a manual Postman/Bruno pass against every endpoint with each of the 6 seeded roles confirms correct 200/401/403 behavior. |
| **P4 — Point `api.js` at the new API, feature-flagged** | Ship the Phase 4 `api.js` (§9) behind a runtime config flag (`window.Consult360Config.apiBaseUrl`), so the same SPA build can point at either the old in-memory `data.js` path or the new API — allowing a side-by-side parallel run in a staging environment before full cutover. | The SPA, pointed at the new API, passes a full manual walkthrough of every section of `testing-guide.md` with no behavioral difference observed; `js/data.js` is not yet deleted (kept as the flagged-off fallback). |
| **P5 — Parity testing against `testing-guide.md`** | Execute every `testing-guide.md` scenario against the Phase 4-backed deployment with the exact same seeded users/patients; run the automated parity tests from §11 for the algorithmic endpoints. Any discrepancy is a P5 blocker, not a "fix later" note. | Zero open discrepancies between documented Phase 1 behavior and observed Phase 4 behavior across every `testing-guide.md` section; all automated parity tests green. |
| **P6 — Production cutover** | Point the production connection string at the production SQL Server 2019 instance, enable the RLS security policies (§7) in production, flip the `api.js` config flag to the new API for all users, rotate demo credentials to real ones. | Production traffic is served entirely by the new stack for a full business cycle (e.g. one week) with no `data.js`-path fallback traffic and no unresolved P5-class discrepancies reported. |
| **P7 — Decommission Phase 1 path** | Delete `js/data.js` and the `ok()`/in-memory-array code paths from the repository (the SPA no longer references `window.Data` for anything except, if retained, `hasPermission` client-side convenience checks — replace those call sites with a cached read of the server-issued permission policy set). Remove the feature flag from P4. | `js/data.js` is deleted from the SPA bundle; no runtime code path references it; the migration is complete. |

---

## 13. Risks & Mitigations

| Risk | Why it's real here | Mitigation |
|---|---|---|
| **`data.js` business-logic subtleties lost in translation** | `data.js` is genuine hand-written JS (not a declarative ruleset) — boundary conditions in date-range lookups, loop off-by-ones in slot generation, string-matching heuristics in the allergy checker are easy to reproduce *almost* correctly. | The bucket classification in §5 plus mandatory parity tests (§11) for anything in bucket 2/3; unit tests assert the *contract*, not just "compiles and returns something." |
| **Stored procedure parameter/type mismatches with C# DTOs** | 152 procedures, many with 10+ parameters and nullable/default values (e.g. `cln.usp_SaveVitals`'s 12 optional vital fields) — a mismatched parameter name, an `INT` vs `DECIMAL` mismatch, or a missed `NULL`-default silently produces wrong results rather than a compile error with Dapper's dynamic parameter binding. | `Consult360.IntegrationTests` exercises every repository method against a real SQL Server 2019 instance (Testcontainers), not mocks — this is the only layer that would actually catch a parameter mismatch; strongly-typed parameter objects (records, not anonymous objects) for every repository call, name-checked against the procedure signature in a code review checklist. |
| **Permission-matrix drift between client-side `hasPermission` and server-side policy** | `router.js`'s client-side guard and the new server-side `FeaturePermissionHandler` (§7) are two independent enforcement points reading (potentially) two different sources if not deliberately kept in sync — this is exactly the kind of drift a pen tester looks for. | Single source of truth: `sec.PermissionMatrix` (per-hospital, DB-backed) is authoritative; the client-side guard fetches the same matrix via `GET /permissions` at login/session start rather than embedding its own copy, so there is only one place the grid is actually defined. |
| **Session/`HospitalId` spoofing now that auth is real** | Phase 1 has nothing to spoof (everything is client-trusted by design); Phase 4 introduces the first real trust boundary, and it's the single most pen-test-relevant change in this migration (`PEN-TEST-STRATEGY.md` §3.4). | `HospitalContextMiddleware` (§7) derives `HospitalId` exclusively from the validated JWT claim, never from client-supplied query/body values, with RLS as a database-level backstop; integration tests specifically assert that a request with a mismatched client-supplied `HospitalId` still resolves against the JWT's hospital, not the client's claim. |
| **Performance regression from network round-trips replacing in-memory array access** | Every `data.js` call was a synchronous in-memory `Array.filter()`; every Phase 4 call is now a network round trip + a stored procedure execution. Pages that previously fired several `API.xxx()` calls in quick succession (e.g. the Consultation 360° workspace loading vitals, symptoms, diagnoses, and prescriptions on open) will be noticeably slower if each becomes a separate HTTP round trip. | Favor procedures/endpoints that already batch related reads (e.g. a single `GetConsultationById` response that includes nested vitals/symptoms rather than four separate calls, where `API-SPECIFICATION.md`'s existing shape allows it); add response caching (`ResponseCache`/output caching) for read-heavy, rarely-changing reference data (formulary, ICD-10 catalog, phrase bank); add server-side pagination for any `list*` endpoint whose result set can grow large in production (patient search, audit log, invoices) even though the Phase 1 in-memory arrays never needed it at seed-data scale. |
| **Drug-allergy / clinical-safety checks bypassable via direct API calls** | Called out explicitly in `PEN-TEST-STRATEGY.md` §3.5; Phase 1's check only ever runs client-side. | §5.4's server-side enforcement inside `PrescriptionService.CreatePrescriptionAsync`, not merely a callable pre-check endpoint the client can choose to skip. |
| **Password/credential handling regression during seed migration** | Seed data currently stores demo passwords as plaintext-equivalent strings; a careless seed script port could carry that forward into a "production-looking" database. | Seed script (§10) hashes every seeded password through the same `PasswordHasher`/`BCrypt` code path used at runtime — no seeded row ever contains a plaintext-comparable value, and this is asserted by an integration test that attempts to log in with the documented demo credentials through the real auth endpoint, not by reading the seed script's literal text. |

---

## 14. Appendix

### 14.1 Representative method-mapping tables (per module)

The full 130-endpoint mapping is `docs/API-SPECIFICATION.md`; the full
143-method inventory is `js/api.js`; the full 152-procedure inventory is
`docs/stored-procedures.sql`. The tables below are a representative sample
per module — enough to make the `api.js method → Controller action → Route
→ Stored Procedure` pattern unambiguous for an engineer picking up any
module's port.

**Patients**

| api.js method | Controller.Action | Route | Stored Procedure |
|---|---|---|---|
| `searchPatients(term, hid)` | `PatientsController.Search` | `GET /patients/search?term=&hospitalId=` | `opd.usp_SearchPatients` |
| `getPatient(id)` | `PatientsController.GetPatient` | `GET /patients/{id}` | `opd.usp_GetPatient` |
| `createPatient(record)` | `PatientsController.CreatePatient` | `POST /patients` | `opd.usp_CreatePatient` |
| `updatePatient(id, patch)` | `PatientsController.UpdatePatient` | `PUT /patients/{id}` | `opd.usp_UpdatePatient` |
| `listAllergies(patientId)` | `AllergiesController.List` | `GET /patients/{id}/allergies` | `opd.usp_ListAllergies` |
| `deleteAllergy(id)` | `AllergiesController.Delete` | `DELETE /allergies/{id}` | `opd.usp_DeleteAllergy` |

**Scheduling**

| api.js method | Controller.Action | Route | Stored Procedure |
|---|---|---|---|
| `listTodayAppointmentsForDoctor(doctorId, hid)` | `SchedulingController.ListToday` | `GET /doctors/{doctorId}/appointments/today?hospitalId=` | `opd.usp_ListTodayAppointmentsForDoctor` |
| `listDoctorSlotConfig(doctorId)` | `SchedulingController.GetSlotConfig` | `GET /doctors/{doctorId}/slot-config` | `opd.usp_ListDoctorSlotConfig` |
| `listAvailableSlots(doctorId, hid, date)` | `SchedulingController.ListAvailableSlots` | `GET /doctors/{doctorId}/available-slots?hospitalId=&date=` | `opd.usp_ListAvailableSlots` |
| `createAppointment(record)` | `SchedulingController.CreateAppointment` | `POST /appointments` | `opd.usp_CreateAppointment` |
| `updateAppointmentStatus(id, status)` | `SchedulingController.UpdateStatus` | `PUT /appointments/{id}/status` | `opd.usp_UpdateAppointmentStatus` |

**Consultation core / Vitals / Symptoms**

| api.js method | Controller.Action | Route | Stored Procedure |
|---|---|---|---|
| `getOrCreateConsultation(pid, did, hid)` | `ConsultationsController.GetOrCreate` | `POST /consultations` | `cln.usp_GetOrCreateConsultation` |
| `saveVitals(consultationId, patch)` | `VitalsController.SaveVitals` | `POST /consultations/{id}/vitals` | `cln.usp_SaveVitals` |
| `updateSymptomState(cid, code, state)` | `SymptomsController.UpdateState` | `PUT /consultations/{id}/symptoms/{symptomName}/state` | `cln.usp_UpdateSymptomState` |
| `saveSoapNotes(cid, s, o, p)` | `ConsultationsController.SaveSoap` | `PUT /consultations/{id}/soap` | `cln.usp_SaveSoapNotes` |
| `confirmSignOff(record)` | `ConsultationsController.SignOff` | `POST /consultations/{id}/sign-off` | `cln.usp_ConfirmSignOff` |

**Prescriptions / Formulary / Lab**

| api.js method | Controller.Action | Route | Stored Procedure |
|---|---|---|---|
| `searchFormulary(term, hid)` | `FormularyController.Search` | `GET /formulary/search?term=&hospitalId=` | `cfg.usp_SearchFormulary` |
| `createPrescription(record)` | `PrescriptionsController.CreatePrescription` | `POST /consultations/{id}/prescriptions` | `cln.usp_CreatePrescription` |
| `checkDrugAllergyConflicts(pid, name, comp)` | `PrescriptionsController.CheckAllergyConflicts` | `GET /patients/{id}/drug-allergy-check?medicineName=&composition=` | `cln.usp_CheckDrugAllergyConflicts` |
| `submitLabResult(record)` | `LabController.SubmitResult` | `POST /lab-portal/results` | `cln.usp_SubmitLabResult` |
| `listPendingLabOrders(hid)` | `LabController.ListPendingOrders` | `GET /lab-portal/pending-orders?hospitalId=` | `cln.usp_ListPendingLabOrders` |

**Billing**

| api.js method | Controller.Action | Route | Stored Procedure |
|---|---|---|---|
| `checkEffectiveFee(doctorId, type, onDate)` | `BillingController.GetEffectiveFee` | `GET /doctors/{doctorId}/fee?type=&onDate=` | `bil.usp_CheckEffectiveFee` |
| `createInvoice(record)` | `BillingController.CreateInvoice` | `POST /invoices` | `bil.usp_CreateInvoice` |
| `createPayment(record)` | `BillingController.CreatePayment` | `POST /invoices/{id}/payments` | `bil.usp_CreatePayment` |
| `cancelInvoice(id, reason, userId)` | `BillingController.CancelInvoice` | `POST /invoices/{id}/cancel` | `bil.usp_CancelInvoice` |
| `recalculateInvoiceTotals(invoiceId)` | `BillingController.Recalculate` | `POST /invoices/{id}/recalculate` | `bil.usp_RecalculateInvoiceTotals` |

**Audit / Permissions / Settings**

| api.js method | Controller.Action | Route | Stored Procedure |
|---|---|---|---|
| `recordAuditEntry(entry)` | `AuditController.RecordEntry` | `POST /audit/logs` | `sec.usp_RecordAuditEntry` |
| `getPermissionMatrix()` | `AdminController.GetPermissionMatrix` | `GET /permissions?hospitalId=` | `sec.usp_GetPermissionMatrix` |
| `listUsers(hid)` | `AdminController.ListUsers` | `GET /users?hospitalId=` | `sec.usp_ListUsers` |
| `updateUserActive(id)` | `AdminController.ToggleUserActive` | `PUT /users/{id}/active` | `sec.usp_UpdateUserActive` |
| `listExportData(tableKey)` | `AuditController.ExportTable` | `GET /export/{tableKey}?hospitalId=&format=csv` | `sec.usp_ListExportData_{TableKey}` |

*(Full 130-row table: `docs/API-SPECIFICATION.md`. Full 152-procedure
listing with parameter signatures: `docs/stored-procedures.sql`.)*

### 14.2 Condensed phase checklist

- **P0** — Confirm `schema.sql`/`stored-procedures.sql`/`API-SPECIFICATION.md`/`DATA-LAYER-ARCHITECTURE.md` are frozen and reviewed.
- **P1** — 4 .NET projects created; DI wired; DbUp applies schema+procedures to dev SQL Server 2019; health endpoint green.
- **P2** — Per module: repository → service → unit tests → integration tests, following §5's bucket classification.
- **P3** — Every `API-SPECIFICATION.md` endpoint implemented + contract-tested; JWT + `HospitalContextMiddleware` + policy authorization in place.
- **P4** — `api.js` Phase 4 build shipped behind a feature flag; parallel-run capable.
- **P5** — Full `testing-guide.md` walkthrough + automated parity tests, zero open discrepancies.
- **P6** — Production cutover, RLS enabled, one full business cycle on the new stack with no fallback traffic.
- **P7** — `js/data.js` deleted; feature flag removed; migration complete.

### 14.3 Reference documents

| Document | Use |
|---|---|
| `docs/DATA-LAYER-ARCHITECTURE.md` | Binding naming/schema/verb-taxonomy rules this guide implements |
| `docs/schema.sql` | The 51-table SQL Server 2019 DDL (deployed via §10, not redesigned) |
| `docs/stored-procedures.sql` | The 152 stored procedures every repository method calls |
| `docs/API-SPECIFICATION.md` | The 130-endpoint REST contract every controller action implements |
| `docs/PRODUCT-GUIDE.md` | Module inventory, engine descriptions, current-limitations context |
| `docs/testing-guide.md` | The regression/parity contract for §11 |
| `docs/ENCRYPTION-STRATEGY.md` | Always Encrypted column list and phased encryption timeline referenced in §7 |
| `docs/PEN-TEST-STRATEGY.md` | The specific vulnerabilities this migration's auth/authorization design (§7) and risk table (§13) close |
