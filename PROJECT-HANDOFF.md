# Consult360 — OPD Consultation Module — Project Handoff & Context Memory

**Purpose of this document.** This is the single entry point for resuming
work on this project in a fresh conversation with no prior context. Read
this file first, in full, before touching code or any other doc. It
explains what the product is, the architecture in place today and the
target one (WCF services over SQL Server), what every folder is for, how the
codebase translates during the WCF migration, which documents are
authoritative vs. historical, and what was done to reach the current state.

**Document status:** authoritative, current as of October 2026. If a claim
here ever conflicts with the actual code or another doc, the code wins for
runtime behavior and this doc's "Documentation Index" (§6) says which other
doc wins for planning/architecture questions.

**Architecture note.** Earlier versions of this handoff described a
vanilla-JS single-page app (`js/api.js`, `js/data.js`) whose Phase 4 target
was an ASP.NET Core Web API with MVC-style controllers. Both are out of date:
the app is now **ASP.NET Web Forms on .NET Framework 4.8**, and the Phase 4
service layer is **WCF**. It is the `DOCNyleUnifiedAPI` solution: one `.svc`,
one interface (`IDOCNyleUnifiedAPI`) and a `DOCNyleUnifiedAPI.DAL` project.
Anything that still says ASP.NET Core, MVC, Web API, Blazor, `api.js` or
`data.js` is describing the old plan.

---

## 1. What Consult360 Is

Consult360 is an outpatient (OPD) consultation and small-clinic management
application for practices with one to a few doctors. It covers the full
visit lifecycle: patient registration/search, the doctor's daily queue, a
structured consultation workspace (vitals, complaints, diagnosis, SOAP
notes, prescriptions, lab orders, referrals, services & procedures,
follow-up, sign-off), scheduling, billing with insurance / government-scheme
bill splitting, clinic inventory, lab result exchange with external lab
partners, and hospital-level administration (users, permissions, formulary,
phrase library, consultation form setup, compliance registrations,
notifications). It is multi-tenant (per hospital) and multi-country (two
seeded demo hospitals: India and Jamaica), with country-specific behavior
and the hospital's time zone driven by hospital settings rather than
hard-coded.

For the full product walkthrough (workflows, roles, clinical engines), read
`opd-project/docs/PRODUCT-GUIDE.md`.

---

## 2. Architecture — Current State

### 2.1 Phases

- **Phase 1 (done, historical):** a vanilla HTML/CSS/JS single-page app with
  an in-memory data layer (`js/data.js`) behind a service facade
  (`js/api.js`). Its rules — the 14-verb naming taxonomy, PascalCase fields
  matching SQL columns, `HospitalId` on every tenant table — still govern the
  data layer.
- **Current (running today):** the same product ported to **ASP.NET Web
  Forms, .NET Framework 4.8**, served by IIS / IIS Express. Pages and user
  controls render on the server; data lives in an in-memory store in C#
  (`Services/DataStore.cs`) behind static service classes
  (`Services/*Service.cs`). No database connection yet.
- **Phase 4 (designed, not built):** the **DOCNyleUnifiedAPI** WCF service
  (one `.svc`, one interface, IIS-hosted, .NET 4.8) and its
  `DOCNyleUnifiedAPI.DAL` project calling the SQL Server 2019 stored
  procedures; the Web Forms app calls it through a service reference. The pages
  are not rewritten. See §3 and `opd-project/docs/WCF-MIGRATION-GUIDE.md`.

The mechanism that keeps Phase 4 a contained change: every page reaches data
only through the static service classes. In Phase 4 those classes stay as a
**facade** — their bodies switch from `DataStore` to WCF calls, module by
module, while the 242 call sites in `templates/**` keep calling the same
methods.

### 2.2 The governing naming/architecture rules

`opd-project/docs/DATA-LAYER-ARCHITECTURE.md` is the binding rulebook for the
data layer. Read it before adding or changing data-layer code. Summary:

- **14-verb taxonomy.** Stored procedures and WCF operations start with one
  of: `Get`, `List`, `Search`, `Check`, `Create`, `Save`, `Update`, `Delete`,
  `Submit`, `Confirm`, `Cancel`, `Approve`, `Reject`, `Record`. A WCF operation
  is named after its procedure without schema and `usp_`
  (`opd.usp_SearchPatients` → `SearchPatients`).
- **Models are SQL tables.** `Models/*.cs` properties are PascalCase and match
  `schema.sql` column names; every `DataStore` list mirrors one table.
- **Multi-tenancy via `HospitalId`.** Every hospital-scoped table has a
  `HospitalId`. In Phase 4 it comes only from the caller's validated session,
  never from a message, and SQL Server Row-Level Security filters on
  `SESSION_CONTEXT('HospitalId')`.
- **Schema namespaces:** `opd` (core OPD), `cln` (clinical), `bil` (billing),
  `cfg` (config/reference), `sec` (security/audit), `msg` (messaging), `inv`
  (clinic inventory).
- **No ad-hoc SQL.** Every database access is a named stored procedure.

### 2.3 Layer map (as it runs today)

```
Browser
┌────────────────────────────────────────────────────────────────┐
│ server-rendered HTML · css/** · js/site.js                     │
│ js/engines/engine-voice-soap.js (Web Speech API dictation)     │
│ js/components/component-drawing-pad.js (sketch canvas)         │
│ notifications.ashx polling (in-app notifications)              │
└───────────────────────────────┬────────────────────────────────┘
                                │ page requests / postbacks
                                ▼
OpdProject — ASP.NET Web Forms, .NET 4.8 (IIS Express :8095)
┌────────────────────────────────────────────────────────────────┐
│ Global.asax · Web/RouteConfig.cs (friendly URLs, 43 routes)    │
│ Site.Master · index.aspx                                       │
│ templates/**/*.aspx, *.ascx + code-behind — one per screen     │
│ Web/BasePage.cs — sign-in + RequiredPermission guard, toasts   │
│ Web/AppSession.cs — signed-in user in InProc session (15 min)  │
│                                                                │
│ Services/*Service.cs  ←— the facade; bodies switch in Phase 4  │
│ Services/ClinicalEngines.cs, VitalUnitService, PhoneService,   │
│   TimeZoneService — pure logic, stays in-process               │
│ Services/DataStore.cs ←— in-memory tables + seed; deleted in   │
│   Phase 4 (replaced by SQL Server behind WCF)                  │
└────────────────────────────────────────────────────────────────┘
```

### 2.4 Clinical engines — stay in-process

`Services/ClinicalEngines.cs` (`ContradictionEngine`, `RxReconciler`,
`SoapComposer`, `BranchedFlowEngine`) holds advisory, non-blocking logic over
data the page already loaded; it has no data access and stays in the Web Forms
app. Voice SOAP and the drawing pad are browser APIs. The one clinical safety
check that is server-side is the drug-allergy check
(`cln.usp_CheckDrugAllergyConflicts`), which Phase 4 runs inside
`CreatePrescription` so it cannot be skipped
(`WCF-MIGRATION-GUIDE.md` §5.4, §8).

---

## 3. Architecture — Target State (Phase 4, designed but not built)

**Stack:** WCF on .NET Framework 4.8, hosted in IIS, over SQL Server 2019.
The Web Forms app is retained; only the bodies of its `Services/*Service.cs`
facade change.

```
Solution 'DOCNyleUnifiedAPI'
├─ DOCNyleUnifiedAPI             WCF Service Application
│  ├─ DOCNyleUnifiedAPI.svc(.cs) the one service: class DOCNyleUnifiedAPI —
│  │                             every operation, Run/Authorize/Audit helpers
│  ├─ IDOCNyleUnifiedAPI.cs      the one [ServiceContract] (13 regions),
│  │                             all [DataContract]s, ServiceFault
│  └─ Web.config                 binding, DOCNyle connection string
└─ DOCNyleUnifiedAPI.DAL         class library
   ├─ DataRepository.cs          one method per stored procedure
   └─ Helper.cs                  connection + SESSION_CONTEXT, parameters, retry

OpdProject.sln (this repository)
└─ opd-project/                  Web Forms site: Connected Services →
                                 DocNyleApi.DOCNyleUnifiedAPIClient; Web/DocNyle.cs
                                 adds the session header; facade calls it
db/ (DbUp migrator), tests/      Phase 4 additions
```

- **Contract:** `opd-project/docs/API-SPECIFICATION.md` v3.3 — one contract,
  `IDOCNyleUnifiedAPI`, 261 operations in 13 regions, each naming its stored
  procedure (and the `DataRepository` method of the same name); data-contract
  conventions, faults, audit rules.
- **Binding:** `basicHttpBinding` over HTTPS with MTOM (50 MB quotas, matching
  the site's upload limit) at `https://{host}/DOCNyleUnifiedAPI.svc`; optional
  `netNamedPipeBinding` when site and service share a machine. `PerCall`
  instancing, no WCF sessions.
- **Auth:** sign-in through the `AuthenticateUser` / `CreateUserSession`
  operations; the site keeps a session token in `AppSession` and sends it as a
  `SessionToken` SOAP header; each operation calls `Authorize(feature)`, which
  validates the token (`sec.usp_CheckSessionValid`), builds a `CallerContext`
  (`UserId`, `HospitalId`, roles) and checks `sec.PermissionMatrix`. Passwords
  are PBKDF2-hashed in the service.
- **Data access:** ADO.NET stored-procedure calls in `DataRepository` /
  `Helper`, not Entity Framework — the project is fully stored-procedure based
  (`WCF-MIGRATION-GUIDE.md` §4).
- **Rollout:** a `DataSource:{Module}` app setting per module (`InProc` /
  `Wcf`) so modules switch one at a time and parity can be tested side by
  side; `DataStore` is deleted at the end.

**The schema and procedures already exist and are not being redesigned** —
`schema.sql` (81 tables), `stored-procedures.sql` (266 procedures, 3 views,
7 functions) and `audit-log.sql`. Read `WCF-MIGRATION-GUIDE.md` in full before
starting Phase 4 work: it has the project layout, worked examples taken from
the current C# services, phase gates P0–P6 and the risk table.

---

## 4. File & Folder Translation Map — Current → Phase 4

| Current path | Phase 4 fate |
|---|---|
| `templates/**/*.aspx`, `*.ascx` + code-behind | **Unchanged**, except where a facade method's shape changes (e.g. symptom cycling) and where a page's own `Audit(...)` call duplicates an audited operation — those calls are removed when the module switches. |
| `Site.Master`, `index.aspx`, `Global.asax`, `Web/RouteConfig.cs`, `Web/Navigation.cs` | **Unchanged.** |
| `css/**`, `assets/**`, `js/**` | **Unchanged.** |
| `Web/BasePage.cs` | **Small change:** turns `FaultException<ServiceFault>` into toasts / redirects; `Can(...)` reads the permission matrix from `IUserService`. |
| `Web/AppSession.cs` | **Small change:** also keeps the WCF session token; sign-out ends it. |
| Connected Services → `DocNyleApi` | **New** — service reference to `DOCNyleUnifiedAPI.svc` (generated client and data contracts; update it whenever `IDOCNyleUnifiedAPI.cs` changes). |
| `Web/DocNyle.cs` | **New** — the only way the site calls the service: creates the client, adds the `SessionToken` header, closes / aborts. |
| `Services/*Service.cs` | **Become the facade.** Method bodies switch from `DataStore` to `DocNyle.Call(api => api.Xxx(…))` per module; models are mapped from the generated data contracts. |
| `Services/ClinicalEngines.cs`, `VitalUnitService.cs`, `PhoneService.cs`, `TimeZoneService.cs` | **Stay in-process** (§2.4). |
| `Services/DataStore.cs` | **Deleted** at the end (P6). Its seed becomes `db/Scripts/0005_SeedDemoData.sql`. |
| `Models/*.cs` | **Kept during the migration**; replaced by the contracts' data types once nothing maps between them. |
| `notifications.ashx` | **Small change:** reads through the in-app notification operations (spec v3.2 §12) once `UserNotificationService` switches. |
| `App_Data/consent-forms/**`, `App_Data/lab-reports/**` | **Move** to the services' file store once attachment/consent-document procedures and a streamed endpoint exist. |
| `docs/schema.sql`, `docs/audit-log.sql` | **Become the deployed database**, versioned with DbUp. |
| `docs/stored-procedures.sql` | **Become the deployed procedures** ("always run" DbUp scripts), called only from `DOCNyleUnifiedAPI.DAL/DataRepository.cs`. |
| `docs/API-SPECIFICATION.md` | **The literal WCF contract.** Every row is one `[OperationContract]` on `IDOCNyleUnifiedAPI` and one `DataRepository` method. |
| `docs/DATA-LAYER-ARCHITECTURE.md` | **Naming and tenancy reference** for the contracts and procedures. |
| `docs/WCF-MIGRATION-GUIDE.md` | **The execution plan**; not a living doc after Phase 4 ships. |
| `docs/testing-guide.md` | **The parity contract** — its scenarios become integration / parity tests or a manual pass before cutover. |
| `docs/PRODUCT-GUIDE.md` | **Stays the product reference**; update only if Phase 4 changes visible behavior. |
| `README.md`, `START-SERVER.bat/.sh` | **Updated** to start both the site and `DOCNyleUnifiedAPI`. |

---

## 5. Current File Inventory

Paths are relative to `opd-project/`.

### 5.1 App shell & infrastructure

| File | Purpose |
|---|---|
| `OpdProject.csproj` (solution `../OpdProject.sln`) | Web Forms project, .NET Framework 4.8 |
| `Web.config` | Session timeout (15 min), upload limits (50 MB), friendly-URL routing, `c360:` control prefix |
| `Global.asax(.cs)` | Start-up: route registration, `DataStore.Seed()` |
| `Site.Master(.cs)` | Sidebar/topbar chrome, toasts, browser time-zone cookie |
| `index.aspx` | Entry page |
| `notifications.ashx` | In-app notification feed polled by the browser |
| `Web/BasePage.cs` | Base for every page: sign-in, `RequiredPermission`, `Can(feature)`, toasts, `Audit(...)`, date/time formatting |
| `Web/AppSession.cs` | Signed-in user in ASP.NET session; toast queue |
| `Web/RouteConfig.cs`, `Web/Navigation.cs` | Friendly URLs; permission-filtered sidebar |
| `Web/ConsultCards.cs`, `Web/ConsultationSection.cs`, `Web/HealthRecord.cs`, `Web/VitalUnitUi.cs`, `Web/PhoneValidator.cs` | Consultation card rendering, health-record helpers, vital-unit inputs, phone validator control |
| `START-SERVER.bat` / `.sh` | IIS Express on port 8095 |

### 5.2 Data layer (in-memory, today)

| File | Purpose |
|---|---|
| `Services/DataStore.cs` | One `List<T>` per table + seed data (2 hospitals, demo users across all six roles, `PAT-001`…`PAT-005`) |
| `Services/UserService.cs` | Users, sign-in, permission matrix; `AuditService` |
| `Services/HospitalService.cs` | Hospital profile, settings, formats, compliance, vitals/specialty/history config |
| `Services/PatientService.cs` | Patients, allergies, history, events, family/social, consent documents, payers, coverage |
| `Services/SchedulingService.cs` | Appointments, slots, leave, doctor settings/profiles/charges, appointment types, intake & consent |
| `Services/ConsultationService.cs` | Consultation workspace: vitals, symptoms, diagnoses, prescriptions, orders, referrals, services, notes, sketches, sign-off |
| `Services/ConsultationConfigService.cs` | Consultation form designer (sections, fields, units, draft/publish) |
| `Services/CatalogService.cs` | Formulary, lab tests & favorites, clinical conditions; `PhraseService` |
| `Services/LabService.cs` | Lab partners, portal orders, results, attachments |
| `Services/BillingService.cs` | Consultation charge, invoices, line items, coverage split, payments, receipts |
| `Services/InventoryService.cs` | Items, batches, receipts, adjustments, consumption |
| `Services/OperationsService.cs` | `ReportService`, `NotificationService`, `ExportService` (CSV export / patient import) |
| `Services/UserNotificationService.cs` | In-app notifications |
| `Services/ClinicalEngines.cs`, `VitalUnitService.cs`, `PhoneService.cs`, `TimeZoneService.cs` | Pure logic (§2.4) |
| `Models/*.cs` | Entity classes mirroring `schema.sql` |

### 5.3 Screens (`templates/**`)

| Folder | Pages / controls |
|---|---|
| `patients/` | today list, search, registration (+ form, history/medications/visits/family-social tabs), allergies, coverage, consent form/file |
| `consultation/` | `consultation-360.aspx` and its cards (vitals, symptoms, diagnosis, SOAP, CPOE, labs, referral, services, follow-up, confidential notes, safety strip, context panel), print document |
| `scheduling/` | appointment calendar, nurse vitals, queue display |
| `billing/` | checkout (+ coverage form), invoice list/print, patient balances, day end |
| `lab/` | lab portal, lab results, report file |
| `reports/` | home dashboard, dashboard list, reports dashboard |
| `communications/` | message log |
| `portal/` | patient self-register, self-book |
| `settings/` | login, settings hub (+ hospital profile, specialty, vitals controls), user management, permissions, schedule settings, consultation setup, formulary, phrases, inventory, audit log, data export/import |
| `shared/` | access denied, error state, patient identifier strip |

### 5.4 Docs (`docs/`) — see §6

---

## 6. Documentation Index — What Each Doc Is and Its Trust Level

| Doc (`opd-project/docs/`) | Status | What it's for |
|---|---|---|
| `API-SPECIFICATION.md` | **Authoritative, current (v3.3)** | The WCF contract `IDOCNyleUnifiedAPI` (`DOCNyleUnifiedAPI.svc`): 261 operations in 13 regions, data-contract and fault conventions, each operation's stored procedure. |
| `WCF-MIGRATION-GUIDE.md` | **Authoritative, current (v2.1)** | The Phase 4 execution plan. Replaces `ASPNETCORE-MIGRATION-GUIDE.md` (renamed and rewritten). |
| `API-INTEGRATION-GUIDE.md` | **Authoritative, current (v1.2)** | The client side of Phase 4: where static data lives in `OpdProject` (`DataStore`, facade bodies, bypasses, hard-coded lists), what stays static, the facade → operation map per service class, cut-over order, and facade methods that still have no operation. |
| `DATA-LAYER-ARCHITECTURE.md` | **Authoritative** for naming, schema and tenancy rules | Its examples are written for the JS era; read `data.js` as `DataStore`/services and `api.js` as the WCF operations (its own header note says so). |
| `schema.sql` | **Authoritative, deploy-ready** | SQL Server 2019 DDL (v4.1), 81 tables in `opd/cln/bil/cfg/sec/msg/inv`. |
| `stored-procedures.sql` | **Authoritative, deploy-ready** | v3.6: 266 procedures, 3 views, 7 functions; every one backs a WCF operation or is a noted internal helper. |
| `audit-log.sql` | **Authoritative** | Audit log table and procedures. |
| `PRODUCT-GUIDE.md` | **Current for behavior** | What each screen does. Its "Phase 4" paragraph predates the WCF decision. |
| `testing-guide.md` | **Current for behavior** | Manual QA scenarios with the seeded data; the parity contract for Phase 4. |
| `README.md` (project root `opd-project/`) | **Partly stale** | Run instructions are current (Web Forms, port 8095); its intro and docs table still list JS-era files that are not in the repository. |
| `PROJECT-HANDOFF.md` (repository root, this file) | **Authoritative, current** | You're reading it. |

Older planning docs referenced by earlier handoffs (`migration-map.md`,
`PROJECT_ARCHITECTURE_RULES_V3_0.md`, `ENCRYPTION-STRATEGY.md`,
`PEN-TEST-STRATEGY.md`, `OPD_Module_Inventory.md`, …) are **not in this
repository**. Where they described a Blazor or ASP.NET Core target, treat
that as superseded.

---

## 7. History — What Was Done, In Order

### 7.1–7.7 JS single-page-app era (summary)

Before the Web Forms port, the project was a vanilla-JS SPA. In that period:
the stored procedures and REST-style API spec were brought to full coverage
of the 150 `api.js` methods; `testing-guide.md` and `PRODUCT-GUIDE.md` were
rewritten against the live code; an ASP.NET Core migration guide was written;
a JSDOM functional-test pass found and fixed a boot blocker, missing API
methods, dead vitals validation, data-layer bypasses and stale routes; and the
three layers (`api.js`, API spec, procedures) were re-synced. Those files
(`js/api.js`, `js/data.js`, the JS modules and HTML templates) no longer
exist; the rules they established carry forward through
`DATA-LAYER-ARCHITECTURE.md`.

### 7.8 Port to ASP.NET Web Forms (.NET Framework 4.8)

The SPA was ported to server-rendered Web Forms: one `.aspx` / `.ascx` per
screen under `templates/`, friendly URLs, `BasePage` permission guards, and
an in-memory C# data layer (`DataStore` + `Services/*Service.cs`) seeded with
the same demo data. Later work on this codebase added, among others: patient
registration and coverage (insurance / government schemes) with bill
splitting, consent forms and documents, schedule settings (availability,
leave, appointment types, doctor charges, intake & consent), the consultation
form designer with custom sections/fields/vitals/units, services & procedures
with clinic inventory and stock consumption at sign-off, UTC storage with
clinic-time-zone display, and the matching schema / procedure changes
(schema v4.0, procedures v3.3).

### 7.9 WCF service layer specified (October 2026)

The service layer was fixed as **WCF** rather than ASP.NET Core / MVC.
`API-SPECIFICATION.md` was rewritten from REST endpoints to WCF operations
aligned with the existing service classes (v3.0), then matched to the
`DOCNyleUnifiedAPI` solution — one `.svc`, one interface
(`IDOCNyleUnifiedAPI`, 207 operations in 13 regions) and a DAL project with
`DataRepository` / `Helper` (v3.1) — with tenancy and actor fields taken from the session, SOAP faults
instead of HTTP status codes, and audit entries written by the operations
themselves. `ASPNETCORE-MIGRATION-GUIDE.md` was replaced by
`WCF-MIGRATION-GUIDE.md` (v2.0), and this handoff was updated to match.


### 7.10 Static-data inventory and gap procedures (October 2026)

`API-INTEGRATION-GUIDE.md` was written: where static data lives in `OpdProject` (the
`DataStore`, the facade bodies, page bypasses such as the WhatsApp/Email settings edited in place,
hard-coded lists that have tables), what stays static, the facade → operation map per service
class and the cut-over waves. Mapping the facade found 17 groups of in-memory-only methods; all
got procedures (`stored-procedures.sql` v3.5, section 22: 52 procedures, 2 functions) and
operations (`API-SPECIFICATION.md` v3.2: 54 operations, incl. anonymous patient-portal ones),
plus one table (`sec.LabClaimAttempt`, `schema.sql` v4.1). The SQL was compiled and smoke-tested
on LocalDB; it has not been run through the WCF service yet. Procedures v3.6 / spec v3.3 then
closed four follow-ups: full registration fields and settings-driven MRNs in
`opd.usp_CreatePatient` / `UpdatePatient`, the salted sign-in check (`sec.usp_GetUserCredential`,
`sec.usp_RecordLoginAttempt`, lockout), the front-desk slot re-check in `opd.usp_CreateAppointment`,
and a loop guard in `opd.usp_ListAvailableSlots`.
---

## 8. Current State Snapshot

| Metric | Value |
|---|---|
| Front end | ASP.NET Web Forms, .NET Framework 4.8, IIS Express port 8095 |
| Data layer today | In-memory (`DataStore`), re-seeded on every app-domain start |
| Friendly routes (`Web/RouteConfig.cs`) | 43 |
| `schema.sql` tables | 81 (7 schemas) |
| `stored-procedures.sql` | 266 procedures, 3 views, 7 functions |
| WCF contract (`API-SPECIFICATION.md`) | `IDOCNyleUnifiedAPI` — 1 service, 261 operations — designed, not built |
| Seeded demo hospitals | 2 (`HOSP-001` India / INR, `HOSP-002` Jamaica / JMD) |
| Seeded demo patients | 5 (`PAT-001`…`PAT-005`) |
| Roles | ADMIN, DOCTOR, RECEPTIONIST, BILLING, NURSE, LAB_PARTNER |
| Features with no procedures yet | None. Open: a `MobileKey` column before encrypting phone numbers; portal identity check (product decision) — spec Known Gap #8 and the §5 portal note |

---

## 9. How to Resume Work — Suggested Entry Points

- **Starting Phase 4 (WCF):** read `WCF-MIGRATION-GUIDE.md` in full, then
  `API-SPECIFICATION.md` §0 and `DATA-LAYER-ARCHITECTURE.md`. The service code
  lives in the separate `DOCNyleUnifiedAPI` solution, not in this repository. Begin with the
  P0/P1 gates (guide §13).
- **Changing the current app:** find the screen in §5.3 and its service in
  §5.2; follow `DATA-LAYER-ARCHITECTURE.md`'s naming rules; open the solution
  in Visual Studio 2022 and run with F5 (or `START-SERVER.bat`).
- **Adding data behavior:** add the stored procedure to
  `stored-procedures.sql`, the operation to `API-SPECIFICATION.md` (then
  `IDOCNyleUnifiedAPI.cs` and `DataRepository.cs`) in the same change as the in-memory service method, so the three never drift.
- **"What does the app currently do?":** trust the code first, then
  `PRODUCT-GUIDE.md` and `testing-guide.md`.
