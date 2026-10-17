# Consult360 — OPD Consultation Module
## Phase 4 Migration Guide: WCF (DOCNyleUnifiedAPI) + SQL Server 2019

**Document version:** 2.1 — October 2026
**Scope:** The final migration phase. It moves Consult360's data layer from the in-memory
store inside the Web Forms application (`Services/DataStore.cs` and the static
`Services/*Service.cs` classes) to the **DOCNyleUnifiedAPI** WCF service backed by SQL Server
2019, using the schema, stored procedures and service contract already designed for it.
**Audience:** Engineers doing the migration; reviewers checking the plan before work starts.
**Companion documents:** `API-SPECIFICATION.md` (the WCF contract: one service,
`IDOCNyleUnifiedAPI`, 207 operations), `DATA-LAYER-ARCHITECTURE.md` (naming and schema rules),
`schema.sql` (80 tables), `stored-procedures.sql` (212 procedures, 3 views, 4 functions),
`audit-log.sql`, `PRODUCT-GUIDE.md`, `testing-guide.md`, `API-INTEGRATION-GUIDE.md` (the
Web Forms side of §9 in detail: static-data inventory, facade → operation map, cut-over order).

**Change notes.** v2.1 matches the `DOCNyleUnifiedAPI` solution: one `.svc`, one interface file,
and a `DOCNyleUnifiedAPI.DAL` project (`DataRepository.cs`, `Helper.cs`). v2.0 replaced
`ASPNETCORE-MIGRATION-GUIDE.md`, which planned an ASP.NET Core Web API with MVC-style
controllers behind the old vanilla-JS app. That plan no longer applies: the front end is
**ASP.NET Web Forms on .NET Framework 4.8**, `js/api.js` and `js/data.js` no longer exist, and
the service layer is **WCF**. Documents that describe a Blazor, ASP.NET Core or REST/MVC
target are stale on that point.

---

## Table of Contents

1. [Executive Summary & Target Architecture](#1-executive-summary--target-architecture)
2. [Guiding Principles](#2-guiding-principles)
3. [Solution Structure](#3-solution-structure)
4. [Data Access — DOCNyleUnifiedAPI.DAL](#4-data-access--docnyleunifiedapidal)
5. [Porting the Business Logic](#5-porting-the-business-logic)
6. [The Service — DOCNyleUnifiedAPI.svc](#6-the-service--docnyleunifiedapisvc)
7. [Authentication, Authorization & Multi-Tenancy](#7-authentication-authorization--multi-tenancy)
8. [Logic That Stays In-Process](#8-logic-that-stays-in-process)
9. [Web Forms Integration](#9-web-forms-integration)
10. [Hosting & Deployment](#10-hosting--deployment)
11. [Database Deployment & Migrations](#11-database-deployment--migrations)
12. [Testing & Parity Strategy](#12-testing--parity-strategy)
13. [Migration Sequence & Phase Gates](#13-migration-sequence--phase-gates)
14. [Risks & Mitigations](#14-risks--mitigations)
15. [Appendix](#15-appendix)

---

## 1. Executive Summary & Target Architecture

Today Consult360 is one ASP.NET Web Forms application (`OpdProject`, .NET Framework 4.8,
IIS / IIS Express). Its pages and user controls (`templates/**/*.aspx`, `*.ascx`) call static
service classes (`PatientService`, `ConsultationService`, `BillingService`, …) that read and
write `List<T>` tables in `DataStore`, seeded at start-up and lost when the app domain
recycles. The SQL Server schema and stored procedures were written ahead of the .NET data
access, and `API-SPECIFICATION.md` defines the WCF contract that sits between the two.

Phase 4 builds out the `DOCNyleUnifiedAPI` solution against that contract, points it at SQL
Server, and switches the Web Forms application from the in-memory store to the service —
module by module, without rewriting the pages.

### Target architecture

```
┌──────────────────────────────────────────────────────────────────────────┐
│  BROWSER — server-rendered Web Forms pages (no change)                   │
│  css/** · js/site.js · js/engines/engine-voice-soap.js ·                 │
│  js/components/component-drawing-pad.js · notifications.ashx polling     │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  HTTPS — postbacks, page requests
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  OpdProject — ASP.NET Web Forms, .NET 4.8, IIS                           │
│  templates/**/*.aspx/.ascx + code-behind · Site.Master · Web/BasePage    │
│  Web/AppSession (signed-in user + service session token)                 │
│  Services/*Service.cs — kept as the FACADE the pages call; bodies switch │
│    from DataStore to DocNyle.Call(api => api.Xxx(…)) per module          │
│  Connected Services → DocNyleApi.DOCNyleUnifiedAPIClient (generated)     │
│  Services/ClinicalEngines.cs, VitalUnitService, PhoneService,            │
│    TimeZoneService formatting — stay in-process (§8)                     │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  SOAP over HTTPS (basicHttpBinding, MTOM)
                                │  SessionToken header
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  DOCNyleUnifiedAPI — WCF Service Application, .NET 4.8, IIS              │
│  DOCNyleUnifiedAPI.svc → class DOCNyleUnifiedAPI : IDOCNyleUnifiedAPI    │
│  IDOCNyleUnifiedAPI.cs — one [ServiceContract], 207 operations in 13     │
│    regions, all [DataContract]s, ServiceFault                            │
│  Each operation: Authorize(feature) → validate → DataRepository → map    │
│    → audit, inside Run(…) which turns exceptions into ServiceFault       │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  project reference
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  DOCNyleUnifiedAPI.DAL — class library, .NET 4.8                         │
│  DataRepository.cs — one method per stored procedure                     │
│  Helper.cs — connection, sp_set_session_context, parameters, retry       │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  ADO.NET (System.Data.SqlClient)
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  SQL Server 2019 — ALREADY DESIGNED, NOT REDESIGNED HERE                 │
│  schema.sql — 80 tables in opd/cln/bil/cfg/sec/msg/inv                   │
│  stored-procedures.sql — the only read/write surface (no ad-hoc SQL)     │
│  audit-log.sql · Row-Level Security on SESSION_CONTEXT('HospitalId')     │
└──────────────────────────────────────────────────────────────────────────┘
```

**What does not change:** the pages, user controls, master page, routes
(`Web/RouteConfig.cs`), CSS, the browser scripts, the schema and the stored procedures.
**What is built out:** `DOCNyleUnifiedAPI` (the `.svc`, its code-behind, `IDOCNyleUnifiedAPI.cs`)
and `DOCNyleUnifiedAPI.DAL` (`DataRepository.cs`, `Helper.cs`), plus the database migrator and
test projects.
**What changes in `OpdProject`:** a service reference in Connected Services, the bodies of the
`Services/*Service.cs` facade methods, `AppSession` (it also keeps the service session token),
`BasePage`'s fault handling, and the seeding in `DataStore` (retired at the end).

---

## 2. Guiding Principles

1. **The pages barely change.** The static service classes the pages already call
   (242 call sites in `templates/**`) stay as a facade. Their bodies move from `DataStore` to the
   service; where a facade method's signature already matches an operation, nothing that calls
   it changes. Where it does not (for example `ConsultationService.CycleSymptom`, §5.2), the
   facade adapts.
2. **The contract is implemented here, not designed here.** `API-SPECIFICATION.md` already
   names every operation, data contract, fault and backing procedure. Changes go through that
   document first, then `IDOCNyleUnifiedAPI.cs`, then the service reference is updated.
3. **The schema and procedures are frozen inputs.** `schema.sql` and `stored-procedures.sql`
   already hold most persistence rules (upserts via `MERGE`, soft deletes, `OUTPUT inserted.*`,
   BMI in `cln.usp_SaveVitals`, stock consumption in `cln.usp_ConfirmSignOff`). The migration
   deploys and versions them. New procedures are added only for the gaps listed in
   `API-SPECIFICATION.md` "Known Gaps".
4. **The in-memory services hold real business logic.** `SchedulingService.ListAvailableSlots`,
   `ConsultationService.CheckDrugAllergyConflicts`, `BillingService.RecalculateTotals`,
   `InventoryService.ConsumeForConsultation` are hand-written algorithms. Many already have a
   T-SQL twin; each one still needs a parity test (§12).
5. **No ad-hoc SQL.** Every database call is a named stored procedure, made from
   `DataRepository` through `Helper` — the only code that references `SqlClient`.
6. **The client never decides the tenant.** `HospitalId` comes from the validated session, never
   from a message member. `BasePage.HospitalId`'s `"HOSP-001"` fallback is a demo convenience and
   must not reach the service.
7. **Additive, not a redesign.** Table names, schema namespaces, the 14-verb naming taxonomy
   and the dual-key pattern (`Id` internal / `{Table}Id` external `UNIQUEIDENTIFIER`) are not
   revisited.

---

## 3. Solution Structure

```
Solution 'DOCNyleUnifiedAPI'
├── DOCNyleUnifiedAPI                     WCF Service Application (.NET 4.8, IIS)
│   ├── DOCNyleUnifiedAPI.svc             the one endpoint
│   │   └── DOCNyleUnifiedAPI.svc.cs      class DOCNyleUnifiedAPI : IDOCNyleUnifiedAPI
│   │                                     — all 207 operations (#regions as in the spec), plus the
│   │                                       private Run / Authorize / Audit / mapping helpers
│   ├── IDOCNyleUnifiedAPI.cs             [ServiceContract] interface, [DataContract]s, ServiceFault
│   ├── Web.config                        system.serviceModel, DOCNyle connection string
│   └── packages.config
└── DOCNyleUnifiedAPI.DAL                 Class library (.NET 4.8)
    ├── DataRepository.cs                 one public method per stored procedure
    ├── Helper.cs                         Open (+ SESSION_CONTEXT), Procedure, Add, readers, retry
    ├── App.Config                        design time only; runtime config is the service's Web.config
    └── packages.config

Alongside (Phase 4 additions):
├── db/Scripts/ + db/Consult360.DbMigrator/   DbUp scripts and runner (§11)
└── tests/                                    unit, integration (LocalDB), parity (§12)

Consumer (existing repository):
└── OpdProject.sln / opd-project/             Web Forms site
    ├── Connected Services/DocNyleApi/         generated DOCNyleUnifiedAPIClient + data contracts
    ├── Web/DocNyle.cs                         NEW — creates the client, adds SessionToken, closes/aborts
    └── Services/*Service.cs                   facade (bodies switch, §9)
```

### Responsibilities and dependency direction

| Project | References | Must never reference |
|---|---|---|
| `OpdProject` | the `DocNyleApi` service reference, `System.ServiceModel` | `DOCNyleUnifiedAPI.DAL`, `SqlClient` |
| `DOCNyleUnifiedAPI` | `DOCNyleUnifiedAPI.DAL`, `System.ServiceModel`, `System.Runtime.Serialization` | `System.Web.UI`, `OpdProject`, `SqlClient` directly (all SQL goes through the DAL) |
| `DOCNyleUnifiedAPI.DAL` | `System.Data`, `System.Data.SqlClient`, `System.Configuration` | `DOCNyleUnifiedAPI` — it returns rows / DAL types, never WCF data contracts |

The site gets the contract from the service's WSDL (Connected Services), not from a shared
assembly. Whenever `IDOCNyleUnifiedAPI.cs` changes, run **Update Service Reference** in
`OpdProject` so a contract change shows up as a compile error on the site.

---

## 4. Data Access — DOCNyleUnifiedAPI.DAL

### Decision: ADO.NET stored-procedure calls, not Entity Framework

The data surface is already stored-procedure based, with every procedure implementing one
operation's exact behavior (soft deletes, `MERGE` upserts, `OUTPUT inserted.*`, allow-listed
per-table export procedures instead of dynamic SQL). Entity Framework 6's migrations would want
to own the schema and fight `schema.sql`'s hand-written dual keys and constraint names; used only
to call procedures, it adds a `DbContext` to keep in sync and gives nothing. `DataRepository`
therefore calls procedures with plain ADO.NET through `Helper`. (Dapper is a drop-in option
inside `Helper` if the team adds it to `packages.config`; the repository shape does not change.)

Rules: `CommandType.StoredProcedure`; parameters by name; `null` → `DBNull.Value`; one result
row → `DataRow` (or `null`), many rows → `DataTable`, side-effect only → `ExecuteNonQuery`.

### `Helper.cs`

```csharp
// DOCNyleUnifiedAPI.DAL/Helper.cs
public static class Helper
{
    private static readonly string Cs = ConfigurationManager.ConnectionStrings["DOCNyle"].ConnectionString;

    /// Opens a pooled connection and pins it to the caller's hospital for row-level security.
    public static SqlConnection Open(string hospitalId)
    {
        var conn = new SqlConnection(Cs);
        conn.Open();
        using (var cmd = new SqlCommand("sp_set_session_context", conn) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.AddWithValue("@key", "HospitalId");
            cmd.Parameters.AddWithValue("@value", hospitalId);
            cmd.Parameters.AddWithValue("@read_only", 1);
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    public static SqlCommand Procedure(SqlConnection conn, string name, SqlTransaction tx = null)
    {
        return new SqlCommand(name, conn, tx) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
    }

    public static void Add(SqlCommand cmd, string name, object value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public static DataTable Table(SqlCommand cmd)   { return Retry(() => { var t = new DataTable(); new SqlDataAdapter(cmd).Fill(t); return t; }); }
    public static DataRow   FirstRow(SqlCommand cmd) { var t = Table(cmd); return t.Rows.Count > 0 ? t.Rows[0] : null; }
    public static DataSet   Sets(SqlCommand cmd)    { return Retry(() => { var d = new DataSet(); new SqlDataAdapter(cmd).Fill(d); return d; }); }
    public static int       Execute(SqlCommand cmd) { return Retry(() => cmd.ExecuteNonQuery()); }

    public static T Get<T>(DataRow row, string column) { return row[column] == DBNull.Value ? default(T) : (T)row[column]; }

    // Retry, transient error numbers: see "Transient faults" below.
}
```

### `DataRepository.cs`

One public method per procedure, named after it. The constructor takes the caller's
`HospitalId` and `UserId` so no method can forget them.

```csharp
// DOCNyleUnifiedAPI.DAL/DataRepository.cs
public class DataRepository
{
    private readonly string _hospitalId, _userId;
    public DataRepository(string hospitalId, string userId) { _hospitalId = hospitalId; _userId = userId; }

    #region Patients
    public DataTable SearchPatients(string term)
    {
        using (var conn = Helper.Open(_hospitalId))
        using (var cmd = Helper.Procedure(conn, "opd.usp_SearchPatients"))
        {
            Helper.Add(cmd, "@HospitalId", _hospitalId);
            Helper.Add(cmd, "@Term", term);
            return Helper.Table(cmd);
        }
    }

    public DataRow GetPatient(string patientId) { /* opd.usp_GetPatient — as API-SPECIFICATION §0.10 */ }

    public DataRow CreatePatient(PatientInput p)
    {
        // opd.usp_CreatePatient ends with OUTPUT inserted.* — a one-row result set.
        using (var conn = Helper.Open(_hospitalId))
        using (var cmd = Helper.Procedure(conn, "opd.usp_CreatePatient"))
        {
            Helper.Add(cmd, "@HospitalId", _hospitalId);
            Helper.Add(cmd, "@FullName", p.FullName);
            // … one Add per procedure parameter, names exactly as in stored-procedures.sql
            Helper.Add(cmd, "@CreatedBy", _userId);
            return Helper.FirstRow(cmd);
        }
    }
    #endregion
}
```

`DataRepository` uses the same `#region`s as `IDOCNyleUnifiedAPI.cs`. Inputs with many
parameters take a small DAL input class (`PatientInput`) defined in `DataRepository.cs`, so the
DAL never depends on the WCF data contracts; the service maps request → input and row → data
contract.

### `OUTPUT inserted.*` and multiple result sets

Every create/save procedure returns the written row through `OUTPUT inserted.<columns>`, which
reads like a `SELECT`: no output parameters. Procedures that return more than one result set —
`GetDayEndSummary` (summary + by-component rows), `ListNoShowReport` (totals + by-status rows),
`GetVisitIntakeAndConsent` — are read with `Helper.Sets` (one `DataTable` per result set):

```csharp
public DataSet ListNoShowReport(DateTime from, DateTime to)
{
    using (var conn = Helper.Open(_hospitalId))
    using (var cmd = Helper.Procedure(conn, "opd.usp_ListNoShowReport"))
    {
        Helper.Add(cmd, "@HospitalId", _hospitalId);
        Helper.Add(cmd, "@FromDate", from);
        Helper.Add(cmd, "@ToDate", to);
        return Helper.Sets(cmd);          // Tables[0] = totals, Tables[1] = by status
    }
}
```

Where a report is two views with no wrapping procedure (`bil.vw_DayEndSummary` +
`bil.vw_PaymentsByComponent`), add a thin procedure that selects both — schema-additive, not a
redesign of the views.

### Transactions

Compound work belongs in one procedure, as it already is: `cln.usp_ConfirmSignOff` deducts
stock, locks the consultation and completes the appointment in one transaction;
`bil.usp_CreatePayment` recalculates the bill split itself. An operation that genuinely calls
two procedures together (`SaveDoctorCharge`: doctor settings + fee schedule) gets one
`DataRepository` method that opens one connection and one `SqlTransaction` and passes it to
`Helper.Procedure(conn, name, tx)` for both. Avoid `TransactionScope`, which escalates to MSDTC
when a second connection opens.

### Transient faults

`Helper.Retry` retries 3 times with back-off from 200 ms for SQL error numbers `-2` (timeout),
`1205` (deadlock victim), `4060`, `40197`, `40501`, `40613`, `49918`–`49920` and `11001`. Retry
reads and idempotent `MERGE` writes only — `DataRepository` passes `retry: false` for `Create…`
procedures.

---

## 5. Porting the Business Logic

### Methodology

For each `Services/*Service.cs` class, list its public methods and put each in one bucket
before writing code:

1. **Already in the procedure.** The procedure implements the rule end to end (BMI in
   `cln.usp_SaveVitals`, soft delete in `opd.usp_DeleteAllergy`, first-diagnosis-is-primary in
   `cln.usp_CreateDiagnosis`, slot generation in `opd.usp_ListAvailableSlots`). The operation is
   `DataRepository` call + mapping. Its test asserts the contract ("first diagnosis comes back
   `IsPrimary = true`") so a procedure change that breaks it fails a test, not production.
2. **Needs C# in the service.** Input validation, defaults, pivoting a flat result into the
   contract's shape, password hashing, computing `@SignatureHash`, writing audit entries.
3. **Safety checks that must not be skippable.** `CheckDrugAllergyConflicts` today is called by
   the CPOE card before it saves; a caller that skips it can still prescribe. The service runs
   the check inside `CreatePrescription` itself (§5.4).
4. **Presentation logic that stays in the page or facade.** The symptom chip cycle, label
   formatting, `ServiceItemLabel`, `FormatDate` — anything that only shapes what a page shows.

Order (ascending complexity, one engineer per region end to end): **Hospital & configuration →
Users & permissions → Patients → Scheduling → Consultation core & sign-off → Vitals → Symptoms →
Diagnoses → Catalog (phrases, formulary) → Prescriptions → Lab → Inventory → Billing → Reports →
Notifications → Audit & export.**

### 5.1 Worked example — effective-dated fee lookup (bucket 1 + 2)

```csharp
// today: OpdProject/Services/SchedulingService.cs
public static decimal FlatFee(string doctorId, string category, DateTime onDate)
{
    var f = DataStore.Fees.FirstOrDefault(x => x.DoctorId == doctorId && x.ConsultationType == category
        && x.EffectiveFrom.Date <= onDate.Date && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value.Date >= onDate.Date));
    return f != null ? f.FeeAmount : 0;
}
```

`bil.usp_CheckEffectiveFee` applies the same inclusive range in T-SQL. The C# part is the
default date: "today" must be the **hospital's** today (its timezone), not the server's, so a
fee that changes at midnight in Kingston is right for a server in UTC.

```csharp
// DOCNyleUnifiedAPI/DOCNyleUnifiedAPI.svc.cs — #region Scheduling
public FeeSchedule CheckEffectiveFee(string doctorId, string consultationType, DateTime? onDate)
{
    return Run(() =>
    {
        var caller = Authorize("scheduling");
        var date = (onDate ?? caller.HospitalToday).Date;      // CallerContext knows the hospital's zone
        var row = Repo(caller).CheckEffectiveFee(doctorId, consultationType, date);
        return row == null ? null : ToFeeSchedule(row);
    });
}
```

The facade keeps `FlatFee` returning `0` when there is no row, as the pages expect.

### 5.2 Worked example — the symptom chip cycle (bucket 4)

```csharp
// today: OpdProject/Services/ConsultationService.cs
public static string CycleSymptom(string consultationId, string code)
{
    var row = DataStore.Symptoms.FirstOrDefault(s => s.ConsultationId == consultationId && s.SymptomCode == code);
    if (row == null) { row = new ConsultationSymptom { … State = "UNSET" }; DataStore.Symptoms.Add(row); }
    row.State = row.State == "UNSET" ? "YES" : row.State == "YES" ? "NO" : "UNSET";
    return row.State;
}
```

The cycle (`UNSET → YES → NO → UNSET`) is how the chip behaves, not a data rule. The contract
only persists a final state: `UpdateSymptomState(consultationId, symptomName, state)` with
`YES` / `NO` / `NOT_ASKED`. So the **facade** computes the next state and sends it; the service
validates the value against the `CK_cln_ConsultationSymptom_State` set and never re-derives it.

```csharp
// OpdProject/Services/ConsultationService.cs — facade after the switch
public static string CycleSymptom(string consultationId, string code)
{
    var current = GetSymptomStates(consultationId);          // ListConsultationSymptoms
    string state;
    current.TryGetValue(code, out state);
    var next = state == "YES" ? "NO" : state == "NO" ? "NOT_ASKED" : "YES";
    DocNyle.Call(api => { api.UpdateSymptomState(consultationId, code, next); return 0; });
    return next == "NOT_ASKED" ? "UNSET" : next;              // the chip still shows UNSET
}
```

### 5.3 Worked example — slot generation (bucket 1, parity-critical)

`SchedulingService.ListAvailableSlots` walks each weekly window in steps of slot + buffer,
returns nothing on leave days, and marks a slot booked when it overlaps a non-cancelled
appointment plus the buffer:

```csharp
for (int m = ToMinutes(w.StartTime); m + slot <= wEnd; m += slot + buffer)
{
    int end = m + slot;
    slots.Add(new TimeSlot { SlotStart = day.AddMinutes(m), SlotEnd = day.AddMinutes(end),
                             IsBooked = taken.Any(t => m < t.End && end + buffer > t.Start) });
}
```

`opd.usp_ListAvailableSlots` implements the same walk with a recursive CTE. The operation is a
pass-through — but an off-by-one at a window's end or a different overlap test would silently
drop the last slot or double-book. The parity test (§12) runs the same doctor/date/duration
inputs through the in-memory method and through the `ListAvailableSlots` operation and compares
the slot lists element by element, including days with several windows, leave days, a fully
booked day, and a slot that exactly fills the window.

### 5.4 Worked example — drug-allergy check made non-bypassable (bucket 3)

`ConsultationService.CheckDrugAllergyConflicts` (substring match plus the penicillin-class
rule) is mirrored by `cln.usp_CheckDrugAllergyConflicts`. Today the CPOE card calls it and then
calls `AddPrescription`; nothing stops a caller from calling only the second. In the service,
`CreatePrescription` runs the check itself:

```csharp
// DOCNyleUnifiedAPI/DOCNyleUnifiedAPI.svc.cs — #region Consultation
public Prescription CreatePrescription(string consultationId, CreatePrescriptionRequest request)
{
    return Run(() =>
    {
        var caller = Authorize("consultation");
        var repo = Repo(caller);
        var critical = repo.CheckDrugAllergyConflicts(request.PatientId, request.MedicineName, request.Composition)
                           .AsEnumerable().Where(r => r.Field<string>("Severity") == "CRITICAL").ToList();
        if (critical.Count > 0 && request.AllergyOverrideAcknowledged != true)
            throw Conflict("Allergy alert must be acknowledged before prescribing.");

        if (critical.Count > 0)
            Audit(caller, "OVERRIDE", "Prescription", null, "Drug-allergy alert overridden for " + request.MedicineName);

        var row = repo.CreatePrescription(consultationId, ToPrescriptionInput(request));
        Audit(caller, "CREATE", "Prescription", Helper.Get<Guid>(row, "PrescriptionId").ToString(), request.MedicineName);
        return ToPrescription(row);
    });
}
```

This keeps the product's non-blocking behavior (the doctor can still prescribe after
acknowledging) while the server, not the page, enforces it. `AllergyOverrideAcknowledged` is a
new optional member of `CreatePrescriptionRequest`; add it to `API-SPECIFICATION.md` §6 when
this lands.

### 5.5 Worked example — sign-off (bucket 1 + 2)

`ConsultationService.SignOff` today hashes `consultationId:doctorId:ticks`, adds the
attestation, locks the record and completes the appointment; stock is consumed separately by
`InventoryService.ConsumeForConsultation`. `cln.usp_ConfirmSignOff` does all of it — including
stock — in one transaction and throws 50040 when stock is short. The C# work: compute
`@SignatureHash` as a real hash (SHA-256 over the signed SOAP text, diagnoses and prescriptions,
not a base64 of ids), take the doctor from the caller (a doctor can only sign their own
consultation), and let `Run` map 50040 to a `BusinessRule` fault whose message lists the
shortages. The facade drops its separate `ConsumeForConsultation` call.

### Module bucket summary

| Module (current class) | Representative methods | Bucket |
|---|---|---|
| `HospitalService` | `GetHospital`, `SaveSettings`, `ListVitalsConfig`, `SaveHospitalCompliance` | 1 |
| `UserService` | `Authenticate`, `CreateUser`, `TogglePermission`, `GetPermissionMatrix` | 2 — password hashing, matrix pivot |
| `PatientService` | `Search`, `Create` (MRN), `SaveAllergy`, `SaveMedicalHistory`, `AddCoverage` | 1; `FindDuplicate` has no procedure yet (Known Gaps) |
| `SchedulingService` | `ListAvailableSlots`, `UpdateStatus`, `SaveDoctorProfile`, `SaveDoctorCharge` | 1, parity-critical; status moves → `Conflict` fault |
| `ConsultationService` | `GetOrCreate`, `SaveVitals`, `CycleSymptom`, `AddPrescription`, `SignOff` | 1 / 3 / 4 — see §5.2, 5.4, 5.5 |
| `CatalogService`, `PhraseService` | `SearchFormulary`, `ToggleFavorite`, `ListVisiblePhrases` | 1; `ToggleFavorite` becomes Create/Delete in the facade |
| `LabService` | `SubmitResult`, `MarkReviewed`, `ClaimOrder`, attachments | 1; claim-by-mobile and attachments need procedures (Known Gaps) |
| `BillingService` | `GenerateInvoiceForConsultation`, `RecalculateTotals`, `ApplyCoverage`, `RecordPayment` | 1 for each procedure; 2 for invoice generation, which orchestrates charge + lines + recalc |
| `InventoryService` | `ReceiveStock`, `AdjustStock`, `Shortages` | 1 |
| `ReportService`, `NotificationService`, `ExportService`, `AuditService` | reports, queue, CSV export, audit | 1; 2 for audit IP / user agent (taken from the request, not the caller) |
| `ConsultationConfigService`, `UserNotificationService` | form designer, in-app notifications | Not migrated yet — no procedures (Known Gaps); stay on `DataStore` until they exist |

---

## 6. The Service — DOCNyleUnifiedAPI.svc

`API-SPECIFICATION.md` is the contract; this section covers how to build it in the two files of
the service project.

### 6.1 `IDOCNyleUnifiedAPI.cs`

- One `[ServiceContract(Namespace = Ns.V1)] public interface IDOCNyleUnifiedAPI`, with
  `#region`s in the order of the spec (§1 Auth … §13 Audit & export). Every operation has
  `[OperationContract]` and `[FaultContract(typeof(ServiceFault))]`.
- Operation name = procedure name without schema and `usp_` — unique across the whole contract.
- After the interface, the `[DataContract]` classes, grouped in the same regions, then
  `ServiceFault`, `FaultCode`, `FieldError` and the `Ns` constant
  (`http://schemas.consult360.com/opd/2026/10`). Data contracts implement
  `IExtensibleDataObject`, number every member (`Order`) and only ever gain members. Dates follow
  spec §0.8.
- The file will be long (≈ 200 operations and their types). Keep it navigable with regions; if it
  becomes unwieldy, the data contracts can move to `partial` or extra files later without
  changing the contract — the spec does not depend on the file split.

### 6.2 `DOCNyleUnifiedAPI.svc.cs`

```csharp
[ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple,
                 Namespace = Ns.V1)]
public class DOCNyleUnifiedAPI : IDOCNyleUnifiedAPI
{
    #region Patients

    public Patient GetPatient(string patientId)
    {
        return Run(() =>
        {
            var caller = Authorize("patients");
            var row = Repo(caller).GetPatient(patientId);
            if (row == null) throw NotFound("Patient not found.");
            Audit(caller, "READ", "Patient", patientId);
            return ToPatient(row);
        });
    }

    #endregion

    #region Plumbing

    private static DataRepository Repo(CallerContext c) { return new DataRepository(c.HospitalId, c.UserId); }

    private CallerContext Authorize(string feature) { /* §7.2 / §7.3 */ }

    private void Audit(CallerContext c, string action, string entityType, string entityId, string details = null)
    { /* Repo(c).RecordAuditEntry(…) with the forwarded client IP / user agent headers */ }

    private static T Run<T>(Func<T> body) { /* §6.3 */ }

    #endregion
}
```

Every operation has the same shape: `Authorize` → validate → `DataRepository` → map → `Audit`,
inside `Run`. No SQL and no static state in this class. Row → contract mapping methods
(`ToPatient(DataRow)`, …) sit at the end of each region.

```xml
<%@ ServiceHost Language="C#" Service="DOCNyleUnifiedAPI.DOCNyleUnifiedAPI" CodeBehind="DOCNyleUnifiedAPI.svc.cs" %>
```

### 6.3 Faults — `Run`

```csharp
private static T Run<T>(Func<T> body)
{
    try { return body(); }
    catch (FaultException<ServiceFault>) { throw; }                        // NotFound(), Conflict(), Validation()…
    catch (SqlException ex) when (ex.Number == 50121 || ex.Number == 50122)
    { throw Fault(FaultCode.Conflict, ex.Message, ex.Number); }
    catch (SqlException ex) when (ex.Number >= 50000 && ex.Number < 60000)
    { throw Fault(FaultCode.BusinessRule, ex.Message, ex.Number); }
    catch (Exception ex)
    {
        Log.Error(ex, CorrelationId);                                       // full detail stays on the server
        throw Fault(FaultCode.Internal, "Something went wrong. Reference: " + CorrelationId, null);
    }
}
```

The `NotFound`, `Conflict`, `Validation`, `Unauthenticated` and `Forbidden` helpers build
`FaultException<ServiceFault>` with the codes in spec §0.6. `includeExceptionDetailInFaults`
stays `false` outside Debug, so stack traces and SQL text never reach the site.

### 6.4 Audit

Operations marked **Audited** in the spec write their entry themselves through `Audit(...)`
(`DataRepository.RecordAuditEntry` → `sec.usp_RecordAuditEntry`), with the IP and user agent of
the original browser request — the site forwards those in two optional headers (`ClientIp`,
`ClientUserAgent`) next to `SessionToken`. The `BasePage.Audit(...)` calls pages make today for
other events become the `RecordAuditEntry` operation. Where a page and an audited operation would
now both record the same event (for example `patient-allergies-body.ascx.cs` after
`DeleteAllergy`), remove the page's call when that module switches over.

---

## 7. Authentication, Authorization & Multi-Tenancy

### 7.1 From `AppSession` to a service session

Today `AppSession` keeps the signed-in `UserId` in ASP.NET InProc session (15-minute timeout,
`Web.config`) and `UserService.Authenticate` compares the plain password in `DataStore`. Phase 4:

1. `login.aspx` calls the `AuthenticateUser` operation with the email and password.
2. On success the site generates a random 256-bit token and calls `CreateUserSession` with an
   expiry matching the session timeout; `sec.UserSession` holds it.
3. `AppSession.SignIn` stores both the `UserId` and the token (`AppSession.ServiceToken`).
   `Web/DocNyle.cs` adds the token to every outgoing call.
4. `AppSession.SignOut` ends the ASP.NET session; the `sec.UserSession` row expires or is revoked
   (an admin can revoke it — not possible today).
5. `BasePage` treats an `Unauthenticated` fault like a lost session: sign out and redirect to
   `login.aspx?returnUrl=…`, which it already does for an expired session.

`AppSession.CurrentUser` re-reads the user each request so a disabled account loses access at
once. Keep that, but cache the user for the request (`HttpContext.Items`) so one page does not
call the service several times for it.

### 7.2 Session validation — `Authorize`, part 1

`Authorize(feature)` reads the `SessionToken` header from
`OperationContext.Current.IncomingMessageHeaders` (missing → `Unauthenticated`), validates it with
`DataRepository.CheckSessionValid` (`sec.usp_CheckSessionValid`; cache valid tokens in
`MemoryCache` for up to 60 seconds), loads the user, roles, `HospitalId` and the hospital's time
zone, and returns a `CallerContext`. `AuthenticateUser` and `CreateUserSession` are the only
operations that do not call `Authorize`.

### 7.3 Permissions — `Authorize`, part 2

Then it checks the caller's roles against `sec.PermissionMatrix` (per hospital; cache per
hospital for a short time and clear it when `UpdatePermission` runs) and throws `Forbidden` when
no role grants the feature. It is the server twin of `BasePage.RequiredPermission` /
`BasePage.Can(...)`, which stay for hiding pages and buttons. Both read the same matrix — the
site's checks call the `GetPermissionMatrix` operation once per session instead of reading
`DataStore.Permissions`, so there is one source of truth.

Features in use: `consultation`, `patients`, `scheduling`, `billing`, `reports`, `print`,
`lab_portal`, `admin_hospital`, `admin_scheduling`, `admin_consultation`, `admin_vitals`,
`admin_specialty`, `admin_formulary`, `admin_phrases`, `admin_inventory`, `admin_users`,
`audit_log`, `data_export`, `data_import`.

### 7.4 Tenancy and Row-Level Security

`HospitalId` comes only from `CallerContext`. No data contract has a `HospitalId` member as
input; a record id from another hospital is reported as `NotFound`. `DataRepository` is
constructed with the caller's `HospitalId`, passes it to every procedure, and `Helper.Open` sets
`SESSION_CONTEXT('HospitalId')` read-only on every connection. The deployment applies the RLS
policies from `DATA-LAYER-ARCHITECTURE.md` §6 to every hospital-scoped table. The procedures still
filter on `@HospitalId` explicitly — RLS is the backstop if a new procedure forgets.

### 7.5 Passwords

Seed users today hold plain passwords (`demo123`, `admin123`) in `DataStore`. The service hashes
with PBKDF2 (`Rfc2898DeriveBytes`, SHA-256, per-user salt, ≥ 100,000 iterations). A salted hash
cannot be recomputed from the password alone, so `sec.usp_AuthenticateUser`'s "compare
`@PasswordHash`" contract needs a companion procedure that returns the stored hash for an email
(proposed `sec.usp_GetUserCredential`, with a `DataRepository.GetUserCredential` method); the
service verifies in C#, then calls `AuthenticateUser` and audits `LOGIN` or `LOGIN_FAIL`. Add that
procedure in P1. The seed script stores real hashes of the documented demo passwords (§11). Lock
an account for 15 minutes after 5 failed attempts.

---

## 8. Logic That Stays In-Process

These have no data access and gain nothing from a network hop; the Web Forms app keeps calling
them directly:

| Code | What it does | Why it stays |
|---|---|---|
| `ContradictionEngine` (`Services/ClinicalEngines.cs`) | vitals-vs-diagnosis, laterality and pediatric-dose alerts | Advisory, non-blocking, works on data the page already loaded |
| `RxReconciler` | dose-delta, duplicate-class and overlap alerts | Same. Unlike the allergy check, these have no procedure; making them non-bypassable would need new procedures — out of scope |
| `SoapComposer` | drafts S/O/A/P text from the visit's data | Produces a draft the doctor edits; `SaveSoapNotes` persists |
| `BranchedFlowEngine` | complaint-driven suggestions | Advisory only |
| `VitalUnitService`, `PhoneService`, `HospitalService.FormatDate/FormatTime/FormatMrn`, `TimeZoneService` conversions | unit conversion, phone rules, display formats | Pure functions; their inputs (hospital settings, vitals config) come from the service |
| `js/engines/engine-voice-soap.js`, `js/components/component-drawing-pad.js` | browser speech recognition, sketch canvas | Browser APIs — nothing to move; the sketch is saved through `SaveSketch` |

The one clinical safety check that *is* server-side is the drug-allergy check (§5.4).

---

## 9. Web Forms Integration

### 9.1 Service reference

In `OpdProject`: Connected Services → Add Service Reference →
`https://localhost:{port}/DOCNyleUnifiedAPI.svc?wsdl`, namespace `DocNyleApi`, **Advanced →
Collection type: `System.Collections.Generic.List`**, reuse types in referenced assemblies off.
This generates `DocNyleApi.DOCNyleUnifiedAPIClient`, the data contracts and the `<client>`
section in `Web.config`. Run **Update Service Reference** after every change to
`IDOCNyleUnifiedAPI.cs`.

### 9.2 The facade switch

Each `Services/*Service.cs` class keeps its public methods. Its body moves from `DataStore` to
the service, behind one setting per module so modules switch one at a time and can switch back:

```xml
<!-- OpdProject/Web.config -->
<appSettings>
  <!-- InProc = DataStore (today) · Wcf = DOCNyleUnifiedAPI -->
  <add key="DataSource:Patients" value="Wcf" />
  <add key="DataSource:Billing"  value="InProc" />
</appSettings>
```

```csharp
// OpdProject/Services/PatientService.cs
public static Patient Get(string patientId)
{
    if (DataSource.IsWcf("Patients"))
        return PatientMap.FromContract(DocNyle.Call(api => api.GetPatient(patientId)));
    lock (DataStore.Sync) return DataStore.Patients.FirstOrDefault(p => p.PatientId == patientId);
}
```

`PatientMap` converts the generated `DocNyleApi.Patient` to the existing `Models.Patient` so
pages and data binding stay as they are. When every module is on `Wcf` and parity has passed
(P4), the `InProc` branches and the mapping are removed and pages can bind to the generated
types directly.

### 9.3 `Web/DocNyle.cs`

The one place that creates clients (spec §0.9): it creates a `DOCNyleUnifiedAPIClient`, adds the
`SessionToken` (and `ClientIp` / `ClientUserAgent`, `CorrelationId`) headers inside an
`OperationContextScope`, calls the operation, and `Close()`s — or `Abort()`s on a communication
error. Never wrap the client in `using` (`Dispose` throws on a faulted channel and hides the real
error). The generated client caches its `ChannelFactory` per endpoint configuration, so creating
one per call is fine.

```xml
<!-- OpdProject/Web.config — generated by the service reference, then tuned -->
<system.serviceModel>
  <bindings>
    <basicHttpBinding>
      <binding name="BasicHttpsBinding_IDOCNyleUnifiedAPI" messageEncoding="Mtom"
               maxReceivedMessageSize="52428800" sendTimeout="00:00:30">
        <readerQuotas maxStringContentLength="52428800" maxArrayLength="52428800" />
        <security mode="Transport" />
      </binding>
    </basicHttpBinding>
  </bindings>
  <client>
    <endpoint address="https://localhost:44395/DOCNyleUnifiedAPI.svc"
              binding="basicHttpBinding" bindingConfiguration="BasicHttpsBinding_IDOCNyleUnifiedAPI"
              contract="DocNyleApi.IDOCNyleUnifiedAPI" name="BasicHttpsBinding_IDOCNyleUnifiedAPI" />
  </client>
</system.serviceModel>
```

### 9.4 Errors on pages

`BasePage` catches `FaultException<DocNyleApi.ServiceFault>` from event handlers (override
`OnError` plus a `TryCall` helper for handlers that want to stay on the page): `Validation` and
`BusinessRule` show `Detail.Message` with `ToastDanger` and keep the form; `NotFound` shows the
existing error state; `Forbidden` redirects to `access-denied`; `Unauthenticated` signs out
(§7.1); `Internal` shows a generic error with the reference.

### 9.5 Round trips

Each facade call is now a network call. Pages that make many reads on load —
`consultation-360.aspx` and its cards, `billing-checkout.aspx`, the settings hub — get slower if
every card calls separately. Mitigations, in order: reuse values within a request (cache in
`HttpContext.Items`), cache reference data per hospital for a few minutes (`ListSpecialties`,
`ListVitalsConfig`, `ListAppointmentTypes`, `GetHospitalSettings`; searches are not cached), and
only then add a combined read operation to the contract for a screen that still needs it.

---

## 10. Hosting & Deployment

- **IIS:** `DOCNyleUnifiedAPI` is its own IIS application (its own app pool, .NET CLR v4.0,
  Integrated pipeline) — on the same server as the site to start with. The site's app pool
  identity needs no database rights; the service's identity (or a SQL login in an encrypted
  `connectionStrings` section, `aspnet_regiis -pef`) gets `EXECUTE` on the schemas and nothing
  else.
- **One endpoint:** `https://{host}/DOCNyleUnifiedAPI.svc`. HTTPS only (`security
  mode="Transport"`); IIS Express in development uses its development certificate on an
  `https://localhost:443xx` port.
- **Same machine:** optionally add a `netNamedPipeBinding` endpoint to the same service (requires
  the WAS / Non-HTTP activation feature) and point the site's client endpoint at it. The contract
  does not change.
- **Development:** open both solutions (or add `DOCNyleUnifiedAPI` to `OpdProject.sln` as an
  existing project) and start both; `START-SERVER.bat` gains a second IIS Express site for the
  service.
- **Config per environment:** `Web.Debug.config` / `Web.Release.config` transforms set the client
  endpoint address (site), the `DOCNyle` connection string, `serviceMetadata` and `serviceDebug`
  (service). No secrets in source control. The DAL's `App.Config` is not read at run time.

---

## 11. Database Deployment & Migrations

`schema.sql`, `stored-procedures.sql` and `audit-log.sql` are hand-written T-SQL; version them
with a script runner, not EF migrations.

### DbUp

```
db/
├── Scripts/
│   ├── 0001_Schema.sql                 (= docs/schema.sql)
│   ├── 0002_AuditLog.sql               (= docs/audit-log.sql)
│   ├── 0003_RowLevelSecurity.sql
│   ├── 0004_SeedReferenceData.sql      (time zones, payers, compliances, catalogs)
│   └── 0005_SeedDemoData.sql           (demo hospitals, users, patients — dev/test only)
├── Procedures/                         (= docs/stored-procedures.sql split per schema; "always run")
└── Consult360.DbMigrator/              (console app, .NET 4.8)
```

Every procedure uses `CREATE OR ALTER`, so the `Procedures/` folder is an "always run" script
group and a procedure fix does not need a new numbered script. The migrator runs as a deployment
step before the service is updated, in every environment.

### Seeding

`DataStore.Seed()` (two hospitals — `HOSP-001` India / INR, `HOSP-002` Jamaica / JMD — demo users,
`PAT-001`…`PAT-005`, allergies, formulary, lab catalog, phrases, inventory) becomes
`0005_SeedDemoData.sql`, idempotent (`MERGE` / `IF NOT EXISTS`), so `testing-guide.md`'s scenarios
find the same records and logins. The readable demo ids become fixed GUIDs, and the seed keeps a
lookup table of `PAT-001` → GUID for the parity tests. Seeded passwords are PBKDF2 hashes of the
documented demo passwords, generated by the same hasher the service uses — never plain text.

---

## 12. Testing & Parity Strategy

- **Unit tests** (MSTest or xUnit on .NET 4.8): the `DOCNyleUnifiedAPI` operations with
  `DataRepository` behind an interface (`IDataRepository`, extracted for testing) and a fake —
  validation, defaults (hospital "today"), fault mapping in `Run`, `Authorize` decisions, audit
  calls, the allergy-override flow.
- **Integration tests:** a fresh SQL Server LocalDB database per run, built by the DbMigrator.
  `DataRepository` tests call each procedure with real parameters — the only place a misspelt
  parameter name or `INT` / `DECIMAL` mismatch shows up. Service tests self-host
  `DOCNyleUnifiedAPI` with `ServiceHost` on a `net.pipe` address and call it with
  `ChannelFactory<IDOCNyleUnifiedAPI>`, asserting member values and fault codes. For every region,
  one test asserts that a second hospital's ids return `NotFound`.
- **Parity tests:** the in-memory `Services/*Service.cs` code and the WCF service are both C#, so
  the same inputs can run through both in one test — the facade with `DataSource:X = InProc`
  against seeded `DataStore`, and with `Wcf` against the seeded database — and the results
  compared. Required for slot generation, fee lookup, consultation charge (free follow-up, unit
  charging), bill split / coverage allocation, stock consumption order (earliest expiry first),
  drug-allergy matching and the permission matrix.
- **`testing-guide.md`** is the behavioral contract: every section either becomes an automated
  test or a manual pass against the WCF-backed site before cutover.

---

## 13. Migration Sequence & Phase Gates

| Phase | Scope | Exit gate |
|---|---|---|
| **P0 — Frozen inputs** | `schema.sql`, `stored-procedures.sql`, `audit-log.sql`, `API-SPECIFICATION.md`, `DATA-LAYER-ARCHITECTURE.md`. | Signed off as the contract; later changes go in as explicit addenda. |
| **P1 — Skeleton** | `IDOCNyleUnifiedAPI.cs` with every operation and data contract; `DOCNyleUnifiedAPI.svc.cs` with `Run`, `Authorize`, `Audit`; `Helper.cs` with `Open` (+ SESSION_CONTEXT), parameters and retry; DbMigrator building a dev database; `sec.usp_GetUserCredential` (§7.5). Auth region working end to end; service reference added to `OpdProject`. | Both projects build; the site signs in through the service against SQL Server; a call without a token is rejected; DbUp applies to an empty database cleanly. |
| **P2 — Operations, region by region** | In the order of §5: `DataRepository` methods → operations → unit tests → integration tests, one region at a time. | Every operation in the spec is implemented and has an integration test; fault codes match spec §0.6. |
| **P3 — Facade switch, module by module** | Turn `DataSource:{Module}` to `Wcf` in a test environment, fix mapping and page error handling (§9). | Each switched module passes its `testing-guide.md` section with no behavior change; parity tests for that module green. |
| **P4 — Parity run** | All modules on `Wcf` in staging; full `testing-guide.md` walkthrough with every seeded role; all parity tests. | Zero open differences between in-memory and WCF behavior. |
| **P5 — Production cutover** | Production database built by the migrator (reference data only — no demo seed), RLS enabled, real users created, demo credentials absent. | One full business cycle (e.g. a week) on WCF with no fallback to `InProc`. |
| **P6 — Decommission the in-memory path** | Delete `DataStore`, the `InProc` branches, the `DataSource` settings and the model ↔ contract mapping where the generated types can be used directly. | No code references `DataStore`; the facade classes are thin wrappers over `DocNyle.Call` (or are removed in favor of direct calls). |

---

## 14. Risks & Mitigations

| Risk | Why it is real here | Mitigation |
|---|---|---|
| Business-logic differences between the C# services and the procedures | Boundary rules (fee dates, slot windows, coverage capping, expiry-first stock) were written twice, once in C# and once in T-SQL | Bucket classification (§5) and mandatory parity tests (§12) for every parity-critical method |
| Procedure parameter / type mismatches | 212 procedures, many with 10+ optional parameters; ADO.NET binds by name at run time | One `DataRepository` method per procedure with parameter names copied from `stored-procedures.sql`; integration test per method on LocalDB |
| One very large contract file | ≈ 200 operations and their data contracts in `IDOCNyleUnifiedAPI.cs`; merge conflicts when several people work on regions | Strict region order matching the spec; one region per pull request; data contracts can move to partial files without changing the contract |
| Stale service reference in the site | The site's types come from WSDL, not a shared assembly | Update Service Reference is part of every contract change; CI fails if the generated `Reference.cs` differs from the live WSDL |
| Chattier pages after the switch | Every facade call becomes a SOAP round trip; Consultation 360 loads many cards | Per-request and per-hospital caching (§9.5) before adding combined operations; measure page load in P3 |
| Tenant leakage | The service is the first real trust boundary; `BasePage.HospitalId` defaults to `HOSP-001` | `HospitalId` only from the caller; `DataRepository` constructed with it; RLS backstop; cross-hospital `NotFound` test per region |
| Two permission sources drift | `BasePage.Can` and `Authorize` both check permissions | Both read `sec.PermissionMatrix`; the site loads it from `GetPermissionMatrix`, not `DataStore` |
| Client misuse in the site | `using` on a faulted client, headers not added | `Web/DocNyle.cs` is the only way pages reach the service (§9.3); code review checklist |
| Large payloads | Logos, sketches, card images and CSV exports travel as `byte[]` | MTOM, 50 MB quotas matching IIS limits; a streamed endpoint for file transfers once those procedures exist |
| Credentials carried over from the demo | `DataStore` holds plain passwords | PBKDF2 hashes in the seed, generated by the runtime hasher; a test signs in with the demo logins through the service |
| Features with no procedures yet | Form designer, in-app notifications, lab attachments, consent documents, lab claim, duplicate check | Those modules stay `InProc` until their procedures and operations are added (spec Known Gaps); P6 cannot finish until they are |

---

## 15. Appendix

### 15.1 Representative mapping (current method → operation → `DataRepository` / procedure)

The operation and its `DataRepository` method share a name.

| Current (`OpdProject/Services`) | `IDOCNyleUnifiedAPI` operation | Procedure |
|---|---|---|
| `UserService.Authenticate(email, password)` | `AuthenticateUser` | `sec.usp_AuthenticateUser` (+ `sec.usp_GetUserCredential`) |
| `UserService.TogglePermission(role, feature)` | `UpdatePermission(role, feature, null)` | `sec.usp_UpdatePermission` |
| `HospitalService.GetSettings(hid)` | `GetHospitalSettings()` | `cfg.usp_GetHospitalSettings` |
| `PatientService.Search(hid, term)` | `SearchPatients(term)` | `opd.usp_SearchPatients` |
| `PatientService.Get(id)` | `GetPatient(id)` | `opd.usp_GetPatient` |
| `PatientService.DeactivateAllergy(id)` | `DeleteAllergy(id)` | `opd.usp_DeleteAllergy` |
| `SchedulingService.ListAvailableSlots(did, hid, date, dur)` | `ListAvailableSlots(did, date, dur)` | `opd.usp_ListAvailableSlots` |
| `SchedulingService.UpdateStatus(id, status, by)` | `UpdateAppointmentStatus(id, status)` | `opd.usp_UpdateAppointmentStatus` |
| `SchedulingService.FlatFee(did, cat, date)` | `CheckEffectiveFee(did, cat, date)` | `bil.usp_CheckEffectiveFee` |
| `ConsultationService.GetOrCreate(pid, did, hid)` | `GetOrCreateConsultation(pid, did, null)` | `cln.usp_GetOrCreateConsultation` |
| `ConsultationService.CycleSymptom(cid, code)` | `UpdateSymptomState(cid, code, next)` | `cln.usp_UpdateSymptomState` |
| `ConsultationService.AddPrescription(record)` | `CreatePrescription(cid, request)` | `cln.usp_CreatePrescription` (+ allergy check) |
| `ConsultationService.SignOff(cid, did)` | `ConfirmSignOff(cid)` | `cln.usp_ConfirmSignOff` |
| `CatalogService.ToggleFavorite(did, tid)` | `CreateLabFavorite` / `DeleteLabFavorite` | `cln.usp_CreateLabFavorite` / `cln.usp_DeleteLabFavorite` |
| `LabService.SubmitResult(record)` | `SubmitLabResult(request)` | `cln.usp_SubmitLabResult` |
| `BillingService.RecalculateTotals(id)` | `RecalculateInvoiceTotals(id)` | `bil.usp_RecalculateInvoiceTotals` |
| `BillingService.RecordPayment(…)` | `CreatePayment(invoiceId, request)` | `bil.usp_CreatePayment` |
| `InventoryService.ReceiveStock(…)` | `RecordStockReceipt(itemId, request)` | `inv.usp_RecordStockReceipt` |
| `ExportService.ToCsv(key, hid)` | `ListExportData(key)` | `sec.usp_ListExportData_{Table}` |
| `AuditService.Record(…)` | `RecordAuditEntry(request)` | `sec.usp_RecordAuditEntry` |

The full list is `API-SPECIFICATION.md` (207 operations, each naming its procedure).

### 15.2 Phase checklist

- **P0** — Contract documents frozen and reviewed.
- **P1** — Interface, service plumbing, DAL `Helper`, DbMigrator; sign-in through WCF; service reference in the site.
- **P2** — Every operation implemented with integration tests.
- **P3** — Modules switched to `Wcf` one by one in test; parity per module.
- **P4** — Everything on `Wcf` in staging; full `testing-guide.md` pass; parity green.
- **P5** — Production cutover with RLS, real users, no demo data.
- **P6** — `DataStore` and the `InProc` path deleted.

### 15.3 Reference documents

| Document | Use |
|---|---|
| `docs/API-SPECIFICATION.md` | The WCF contract `IDOCNyleUnifiedAPI` implements |
| `docs/DATA-LAYER-ARCHITECTURE.md` | Naming, schema and verb-taxonomy rules |
| `docs/schema.sql`, `docs/audit-log.sql` | The DDL deployed in §11 |
| `docs/stored-procedures.sql` | The procedures every `DataRepository` method calls |
| `docs/PRODUCT-GUIDE.md` | What each screen does |
| `docs/testing-guide.md` | The behavioral contract for §12 |
