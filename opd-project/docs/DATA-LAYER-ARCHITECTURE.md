# Consult360 — Data Layer & Schema Architecture Rules

### Governing Rules for data.js, api.js, and SQL Server 2019 Schema

---

## 1. Purpose

This document defines the binding rules for three tightly coupled layers:

```
data.js (in-memory arrays)  →  api.js (async service)  →  SQL Server 2019 (production)
```

The goal is that migration from Phase 1 (JavaScript) to Phase 4 (ASP.NET / Blazor + SQL Server) requires **zero changes to method signatures, naming, or behavior contracts**. The `api.js` method names become ASP.NET controller action names. The data.js array shapes become Entity Framework models. The schema is encryption-ready and pen-test-ready from day one.

**Current state (October 2026).** Phase 1 has since been ported to ASP.NET Web Forms. The in-memory data layer is now `Services/DataStore.cs` (one `List<T>` per table, models in `Models/*.cs`) with the service methods in `Services/*Service.cs`; `js/data.js` and `js/api.js` no longer exist. The database design lives in `docs/schema.sql`, `docs/stored-procedures.sql` and `docs/audit-log.sql`, and the endpoints in `docs/API-SPECIFICATION.md`. Every rule below applies to those files: wherever a section says `data.js`, read `DataStore.cs` / the services; wherever it says `api.js`, read the stored procedures and endpoints. The JavaScript examples show the original form of each rule.

---

## 2. Method Naming — The 14 Verbs

Every public method in `data.js` and `api.js` must start with exactly one of these 14 verbs. No exceptions. No synonyms. No creative alternatives.

| Verb | HTTP | Meaning | Returns | Example |
|------|------|---------|---------|---------|
| `get` | GET | Single record by primary key | Object or null | `getPatient(patientId)` |
| `list` | GET | Multiple records, filterable | Array (may be empty) | `listAppointments(doctorId, date)` |
| `search` | GET | Full-text / substring search | Array (may be empty) | `searchPatients(term, hospitalId)` |
| `check` | GET | Boolean validation / existence | Boolean | `checkSlotAvailable(doctorId, slotStart)` |
| `create` | POST | Insert new record | Created object with ID | `createPatient(record)` |
| `save` | POST | Upsert (create or update) | Saved object | `saveConsultationVitals(consultId, vitals)` |
| `update` | PUT/PATCH | Modify existing record | Updated object | `updatePatient(patientId, patch)` |
| `delete` | DELETE | Hard or soft delete | Deleted object or boolean | `deleteAllergy(allergyId)` |
| `submit` | POST | Submit for review / approval | Updated object | `submitLabResult(orderId, result)` |
| `confirm` | POST | Finalize a draft / lock | Updated object | `confirmSignOff(consultId, attestation)` |
| `cancel` | POST | Reverse, void, or withdraw | Updated object | `cancelAppointment(appointmentId)` |
| `approve` | POST | Approve a workflow step | Updated object | `approveLabResult(resultId)` |
| `reject` | POST | Reject a workflow step | Updated object | `rejectLabResult(resultId, reason)` |
| `record` | POST | Capture event or audit entry | Created object | `recordAuditEntry(entry)` |

### Verbs NOT allowed

These verbs appear in the current codebase and must be renamed during cleanup:

| Current | Problem | Rename to |
|---------|---------|-----------|
| `add` | Synonym of `create` | `create` |
| `remove` | Synonym of `delete` | `delete` |
| `toggle` | Not a standard verb | `update` (pass the new boolean value explicitly) |
| `lookup` | Synonym of `get` | `get` |
| `mark` | Vague | `update` (pass status explicitly) |
| `generate` | Action, not data verb | `create` or `list` depending on context |
| `report` | Not a data verb — it's a read | `list` or `get` |
| `send` | Side-effect verb — belongs in an engine, not data layer | Move to NotificationEngine |
| `queue` | Same — side-effect | Move to NotificationEngine |
| `export` | Side-effect | Move to ExportService |
| `discontinue` | Domain-specific synonym of `cancel` | `cancel` |
| `authenticate` | Special — acceptable as the ONLY exception for login | Keep as-is |

### Naming pattern

```
verb + Entity + Qualifier (optional)

Examples:
  getPatient(id)                    — single patient by ID
  listPatientsForHospital(hid)      — multiple patients, filtered
  searchFormulary(term, hid)        — full-text search
  createPrescription(record)        — new record
  updateAppointmentStatus(id, st)   — modify specific field
  confirmSignOff(cid, attestation)  — finalize workflow
  recordAuditEntry(entry)           — capture event
```

### Rules

1. Verb is always the first word, lowercase camelCase.
2. Entity is singular (`Patient`, not `Patients`). The verb `list` implies plural.
3. Method must do exactly what the verb says — `get` never creates, `create` never updates.
4. Every method in `api.js` must have a corresponding method in `data.js` with the same name and signature.
5. `api.js` methods do nothing except call `data.js` and wrap the result in `ok()`. No business logic in `api.js`.
6. Business logic, validations, and side-effects belong in **engine** modules or the **module** that calls the API.

### Stored procedures and functions

The same 14 verbs name every stored procedure: `{schema}.usp_{Verb}{Entity}{Qualifier}` in PascalCase —
`opd.usp_CreateAppointment`, `inv.usp_ListItemsForService`, `inv.usp_RecordStockReceipt` (an event: `record`),
`inv.usp_UpdateInventoryItemActive` (a boolean passed explicitly, never a toggle), `inv.usp_SaveItemServices`
(a list replaced: `save`). Scalar / table functions are `{schema}.fn_{Name}` (`cfg.fn_HospitalToday`), views
`{schema}.vw_{Name}`, generated audit triggers `trg_Audit_{Table}`.

**Legacy exceptions** — procedures from before this rule was enforced; rename them during cleanup (together
with their endpoint in API-SPECIFICATION.md):

| Procedure | Rename to |
|-----------|-----------|
| `cfg.usp_ToggleSpecialty` | `cfg.usp_UpdateSpecialtyActive` (pass `@IsActive`) |
| `opd.usp_DeactivatePatientCoverage` | `opd.usp_UpdatePatientCoverageActive` |
| `bil.usp_EditInvoiceCoverageLine` | `bil.usp_UpdateInvoiceCoverageLine` |
| `bil.usp_RemoveInvoiceCoverageLine` | `bil.usp_DeleteInvoiceCoverageLine` |
| `bil.usp_ApplyCoverageToInvoice` | `bil.usp_CreateInvoiceCoverageLine` |
| `bil.usp_RecalculateInvoiceTotals`, `bil.usp_RecalculateInvoiceSplit` | `bil.usp_UpdateInvoiceTotals`, `bil.usp_UpdateInvoiceSplit` |

`sec.usp_AuthenticateUser` keeps its name (the one allowed exception). The C# services (`Services/*Service.cs`)
still have some older method names of the same kinds (`ToggleDrug`, `ReceiveStock`, …); they follow the same
rename table when the services are cleaned up.

---

## 3. api.js — Service Layer Rules

> Now: the stored procedures (`docs/stored-procedures.sql`) and the endpoints in `docs/API-SPECIFICATION.md` play
> this role — one procedure per endpoint, request fields named after the procedure's parameters (camelCase), the
> hospital taken from the session, never trusted from the body alone.

### Structure

```javascript
window.API = {
  // Every method: verb + Entity + optional qualifier
  // Every method: async, returns Promise
  // Every method: calls exactly one Data method
  // Every method: hospital-scoped via _hid() fallback

  getPatient: (id) => ok(window.Data.getPatient(id)),
  listPatients: (hid) => ok(window.Data.listPatients(hid || _hid())),
  searchPatients: (term, hid) => ok(window.Data.searchPatients(term, hid || _hid())),
  createPatient: (record) => ok(window.Data.createPatient(record)),
  updatePatient: (id, patch) => ok(window.Data.updatePatient(id, patch)),
  deletePatient: (id) => ok(window.Data.deletePatient(id)),
};
```

### Rules

1. **`_hid()` guard on every hospital-scoped method.** If the method operates on data that belongs to a hospital, the hospitalId parameter must have `|| _hid()` fallback. This ensures the session hospital is always used if the caller forgets to pass it.

2. **No business logic.** `api.js` is a pass-through. It exists so that in Phase 4, the body becomes an HTTP `fetch()` call and the signature stays identical.

3. **No DOM access.** `api.js` never reads from or writes to the DOM. It receives parameters and returns data.

4. **No direct calls to `window.Data` from UI modules.** All data access goes through `window.API`. The only exception is `window.Data.hasPermission()`, called synchronously from `router.js` (route permission guard) and `layout.js` (sidebar item visibility) — both need a same-tick, non-awaited answer to decide what to render before the DOM update they're already in the middle of, so this one read-only check stays direct instead of going through `api.js`'s async `ok()` wrapper. Every other data access, in every other module, must go through `window.API`.

### Phase 4 migration

In ASP.NET, each `api.js` method becomes a controller action:

```
api.js                          →  ASP.NET Controller
─────────────────────────────────────────────────────
getPatient(id)                  →  [HttpGet("{id}")] GetPatient(Guid id)
listPatients(hid)               →  [HttpGet] ListPatients([FromQuery] Guid hospitalId)
searchPatients(term, hid)       →  [HttpGet("search")] SearchPatients([FromQuery] string term)
createPatient(record)           →  [HttpPost] CreatePatient([FromBody] PatientDto dto)
updatePatient(id, patch)        →  [HttpPut("{id}")] UpdatePatient(Guid id, [FromBody] PatchDto dto)
deletePatient(id)               →  [HttpDelete("{id}")] DeletePatient(Guid id)
```

The `api.js` body changes from `ok(window.Data.xxx())` to `fetch('/api/patients/xxx')`. Method names, parameter names, and return shapes stay identical. UI modules require zero changes.

---

## 4. data.js — In-Memory Data Layer Rules

> Now: `Services/DataStore.cs` (one `List<T>` per table) and the models in `Models/*.cs`. Property names are the
> SQL column names; the rules below apply unchanged.

### Structure

```javascript
window.Data = (function () {
  'use strict';

  // ── Table: Patients (→ SQL: opd.Patients) ──
  const patients = [
    { PatientId: 'PAT-001', HospitalId: 'HOSP-001', FullName: '...', ... },
  ];

  // ── Table: Consultations (→ SQL: cln.Consultations) ──
  const consultations = [
    { ConsultationId: 'CON-001', HospitalId: 'HOSP-001', ... },
  ];

  return {
    getPatient: (id) => patients.find(p => p.PatientId === id) || null,
    listPatients: (hid) => patients.filter(p => p.HospitalId === hid),
    // ...
  };
})();
```

### Rules

1. **Every array is a SQL table.** The comment above each array must state the target SQL table name: `// → SQL: opd.Patients`.

2. **Every object property is a SQL column.** Property names use PascalCase matching the SQL column name exactly. No camelCase in data objects.

3. **Every array must have a primary key property.** Named `{EntityName}Id` (e.g., `PatientId`, `ConsultationId`) — the external id. The database's internal `Id` column (§5.3) is never exposed to the application.

4. **Every hospital-scoped array must have `HospitalId`.** This is the multi-tenancy key. No exceptions.

5. **ID generation uses `nextId(prefix)`.** In Phase 4, this becomes `NEWID()` or `IDENTITY` in SQL Server.

6. **No derived/computed properties in stored data.** Computed values (BMI, age, outstanding balance) are calculated at read time, not stored. Exception: denormalized fields for performance (e.g., `PatientName` on Appointment for display — clearly documented as denormalized).

7. **Timestamps are UTC.** ISO 8601 with offset (`2026-10-20T14:00:00Z`) on the wire, `DateTime.UtcNow` (`DateTimeKind.Utc`) in C#, `DATETIMEOFFSET(3)` in SQL Server. Field names: `CreatedAt`, `ModifiedAt`. "Today" is the hospital's date (`TimeZoneService.Today(hospitalId)`), never the server's — see §5.8.

8. **Soft delete with `IsActive` only.** `IsActive = false` is the delete; there is no `IsDeleted` or `DeletedAt`. Hard delete only for junction rows that are replaced as a set and for rows nothing has happened to yet (an invoice line or a consultation service item before sign-off).

9. **Boolean fields prefixed with `Is` or `Has`.** `IsActive`, `IsMandatory`, `HasAllergy`. Not `Active`, `Mandatory`, `Allergy`.

10. **Enum fields store string codes, not numbers.** `Status: 'ACTIVE'` not `Status: 1`. In SQL Server, these become `NVARCHAR(20)` with a CHECK constraint. Self-documenting in queries.

---

## 5. SQL Server 2019 Schema Rules

### 5.1 Schema (Namespace) Strategy

Schemas group related tables. Maximum 5 characters. Every table belongs to exactly one schema.

| Schema | Name | Tables (examples) | Purpose |
|--------|------|-------------------|---------|
| `cfg` | Config | Hospital, HospitalSetting, TimeZone, Specialty, VitalDefinition, DrugFormulary, LabTestCatalog, ICD10Catalog, SymptomCatalog, PhraseBank, MessageTemplate, Payer, Compliance | Reference data, settings |
| `sec` | Security | ApplicationUser, UserRole, DoctorSpecialty, PermissionMatrix, AuditLog, UserNotification, AuditConfig, DataAuditLog | Auth, RBAC, audit |
| `opd` | OPD Core | Patient (+ allergies, history, coverage, consent), Appointment, DoctorSlotConfig, DoctorLeave | Patient management, scheduling |
| `cln` | Clinical | Consultation (+ vitals, symptoms, diagnoses, SOAP, addenda), Prescription, InvestigationOrder, LabResult, Referral, ConsultationServiceItem | Clinical data |
| `bil` | Billing | DoctorFeeSchedule, ServiceChargeMaster, Invoice, InvoiceLineItem, Payment, Receipt, CreditNote | Financial |
| `inv` | Inventory | InventoryItem, InventoryItemService, StockBatch, StockMovement | Clinic stock |
| `msg` | Messaging | NotificationConfig, MessageQueue | Communications |

### 5.2 Table Naming

| Rule | Convention | Example |
|------|-----------|---------|
| Singular noun | Always singular, PascalCase | `Patient`, not `Patients` |
| Schema-qualified | Always reference with schema | `opd.Patient`, not `dbo.Patient` |
| No prefixes | No `tbl_`, no `T_` | `opd.Patient`, not `opd.tblPatient` |
| Named after the model | The C# model the table stores, where one exists | model `Appointment` → `opd.Appointment` |
| Child tables | Parent name first | `cln.ConsultationDiagnosis`, `opd.PatientAllergy` |
| Junction tables | Both entity names joined, no underscore | `cfg.LabTestSpecialty`, `sec.DoctorSpecialty`, `inv.InventoryItemService` |
| History | No `_History` tables: row history is in `sec.DataAuditLog` (§9); Phase 4 adds system-versioned temporal tables for Patient, Appointment, Consultation, Prescription, Invoice | — |

### 5.3 Column Naming

| Rule | Convention | Example |
|------|-----------|---------|
| PascalCase | Every column, identical to the model property name | `FullName`, `CreatedAt` |
| Internal key | `Id` — `BIGINT IDENTITY(1,1)` clustered primary key (`INT` on small reference tables). Never exposed to the application or the API | `opd.Patient.Id` |
| External id | `{Model}Id` — `UNIQUEIDENTIFIER DEFAULT NEWSEQUENTIALID()`, unique. The id the application and API use | `PatientId`, `ConsultationId` |
| Foreign key | Same name as the referenced **external** id; every FK references `{Model}Id`, never `Id` | `HospitalId` references `cfg.Hospital.HospitalId` |
| Junction key | Composite primary key of the two foreign keys; no `Id` column | `PRIMARY KEY (TestId, SpecialtyId)` |
| Boolean | Prefix `Is` or `Has` | `IsActive`, `HasAllergy` |
| Point in time | Suffix `At` (or the domain word: `VisitDate`, `SlotStart`) | `CreatedAt`, `PaidAt`, `SignedAt` |
| Calendar date | Suffix `Date` (`…LocalDate` when it is the local date of an instant) | `ExpiryDate`, `AppointmentDate`, `VisitLocalDate` |
| Time zone | `TimeZoneId` (IANA id) — `cfg.Hospital` keeps its model name `Timezone` | `opd.Appointment.TimeZoneId` |
| Amount/money | Suffix `Amount`, `Fee`, `Charge`, `Price`, `Cost` | `TotalAmount`, `UnitPrice` |
| Count | Suffix `Count` or `Qty` | `RefillCount`, `QtyOnHand` |
| Enum/status | Use the concept name | `Status`, `Severity`, `MovementType` |
| No abbreviations | Except universally known | `Id`, `Qty`, `Bp` |
| No underscores in column names | Use PascalCase only | `FullName`, not `full_name` |

### 5.4 Data Types — Standard Mapping

| Model type | SQL Server type | When to use |
|------------|----------------|-------------|
| String (short) | `NVARCHAR(50-200)` | Names, codes, labels |
| String (long) | `NVARCHAR(MAX)` | SOAP notes, instructions, Base64 images |
| String (enum) | `NVARCHAR(20)` + CHECK constraint | Status, Severity, Role |
| Number (integer) | `INT` | Counts, sequences |
| Number (decimal) | `DECIMAL(10,2)` | Money, measurements |
| Number (float) | `DECIMAL(5,1)` | Temperature, BMI |
| Boolean | `BIT` | Flags (`IsActive`, `IsMandatory`) |
| Calendar date | `DATE` | Birth date, expiry, leave, follow-up — the hospital's calendar |
| Point in time | `DATETIMEOFFSET(3)` holding UTC (`+00:00`), default `SYSUTCDATETIME()` | CreatedAt, ModifiedAt, PaidAt, SignedAt |
| Business-event instant | `DATETIMEOFFSET(0)` UTC **plus** `TimeZoneId NVARCHAR(50)` | Appointment SlotStart/SlotEnd, Consultation VisitDate |
| Wall-clock time | `NVARCHAR(5)` `HH:mm`, hospital-local | DoctorSlotConfig StartTime / EndTime |
| Internal key | `BIGINT IDENTITY(1,1)` (`INT` for small reference tables) | `Id` |
| External id | `UNIQUEIDENTIFIER DEFAULT NEWSEQUENTIALID()` | `{Model}Id`, foreign keys |
| JSON blob | `NVARCHAR(MAX)` + `ISJSON` CHECK | Flexible config, avoid if possible |

`DATETIME2` is not used: every point in time carries its offset so it cannot be read as local time.

### 5.5 Constraint Naming

Every constraint is explicitly named. No system-generated names. `{Abbr}` is a short, stable table
abbreviation chosen once per table (`Pat` for Patient, `Bat` for StockBatch).

| Constraint type | Pattern | Example |
|----------------|---------|---------|
| Primary key | `PK_{Schema}_{Abbr}` | `PK_opd_Pat` |
| Foreign key | `FK_{Schema}_{Abbr}_{Column}` | `FK_opd_Pat_HospitalId` |
| Unique (incl. the external id) | `UQ_{Schema}_{Abbr}_{Columns}` | `UQ_opd_Pat_Id`, `UQ_inv_Bat_No` |
| Check | `CK_{Schema}_{Abbr}_{What}` | `CK_inv_Mov_Sign` |
| Default | `DF_{Schema}_{Abbr}_{Column}` | `DF_opd_Pat_Active` |
| Index | `IX_{Schema}_{Abbr}_{Columns}` | `IX_inv_Mov_Item_Date` |

Unique rules are `UQ_` constraints (there are no `UX_` unique indexes).

### 5.6 Index Strategy

| Index type | When | Naming |
|-----------|------|--------|
| Primary key (clustered) | Every table, on `Id` (on the two keys for a junction) | `PK_{Schema}_{Abbr}` |
| External id (unique, non-clustered) | Every table with `{Model}Id` | `UQ_{Schema}_{Abbr}_Id` |
| HospitalId (non-clustered) | Hospital-scoped tables queried by hospital | `IX_{Schema}_{Abbr}_HospitalId` — often with the date column: `(HospitalId, AppointmentDate)` |
| Foreign key columns | FK columns used in joins | `IX_{Schema}_{Abbr}_{FKColumn}` |
| Status/type columns | Tables with status-based queries | `IX_{Schema}_{Abbr}_Status` |
| Date columns | Tables queried by date range — index the **local date** column, not the UTC instant | `IX_opd_Appt_HospitalDate` |
| Filtered | Only part of the rows is queried | `IX_inv_Bat_Item_Expiry … WHERE QtyOnHand > 0` |
| Composite | Frequent multi-column WHERE | `IX_{Schema}_{Abbr}_{Col1}_{Col2}` |

### 5.7 Example Table — Full Convention

```sql
-- Model: Patient.
CREATE TABLE opd.Patient (
    Id BIGINT IDENTITY(1,1) NOT NULL,                                       -- internal key, never exposed
    PatientId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Pat_Id DEFAULT NEWSEQUENTIALID(),   -- external id
    HospitalId UNIQUEIDENTIFIER NOT NULL,                                   -- multi-tenancy
    Mrn NVARCHAR(20) NOT NULL,
    FullName NVARCHAR(200) NOT NULL,                        -- ENCRYPTION: DETERMINISTIC
    Phone NVARCHAR(20) NULL,                                -- ENCRYPTION: DETERMINISTIC
    Gender NVARCHAR(10) NOT NULL CONSTRAINT CK_opd_Pat_Gender CHECK (Gender IN ('MALE','FEMALE','OTHER')),
    Dob DATE NULL,                                                          -- calendar date
    IsActive BIT NOT NULL CONSTRAINT DF_opd_Pat_Active DEFAULT 1,           -- soft delete
    CreatedAt DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_opd_Pat_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_opd_Pat_CreatedBy DEFAULT '00000000-0000-0000-0000-000000000000',
    ModifiedAt DATETIMEOFFSET(3) NULL, ModifiedBy UNIQUEIDENTIFIER NULL,
    CONSTRAINT PK_opd_Pat PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_opd_Pat_Id UNIQUE NONCLUSTERED (PatientId),
    CONSTRAINT UQ_opd_Pat_Hosp_Mrn UNIQUE (HospitalId, Mrn),
    CONSTRAINT FK_opd_Pat_HospitalId FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    INDEX IX_opd_Pat_Hosp_Phone NONCLUSTERED (HospitalId, Phone)              -- tenant filter + lookup
);
```

### 5.8 Date & Time Rules

1. **Store UTC.** Every point in time is `DATETIMEOFFSET` with `+00:00`. Column defaults use `SYSUTCDATETIME()`;
   C# writes `DateTime.UtcNow`.
2. **System events** (created, modified, login, payment, audit times) are shown in the **viewer's own timezone**,
   detected from the browser (the `c360tz` cookie set by `Site.Master`), falling back to the hospital's.
   `HospitalService.FormatDate/FormatTime` convert any `DateTimeKind.Utc` value automatically.
3. **Business events** (appointments, visits) store the UTC instant **and** the clinic's `TimeZoneId`, plus the
   local calendar date (`AppointmentDate`, `VisitLocalDate`) for day lists. They are shown in the clinic's
   timezone only — "9:00 AM" for every viewer, wherever they are (`BasePage.EventTime`).
4. **Bookings are made on the clinic's wall clock.** Slots are generated from hospital-local windows;
   `opd.usp_CreateAppointment` takes `@SlotStartLocal` / `@SlotEndLocal` and converts to UTC.
5. **"Today" is the hospital's date** — `cfg.fn_HospitalToday(@HospitalId)` in SQL, `TimeZoneService.Today` in C#.
   Never `GETDATE()`, `CAST(SYSUTCDATETIME() AS DATE)` or `DateTime.Today`. Report ranges, day-end, stock expiry,
   prescription validity and the audit-log filter all use hospital-local dates.
6. **Time zone ids are IANA** (`America/Jamaica`). `cfg.TimeZone` maps them to the Windows names SQL Server's
   `AT TIME ZONE` needs; `cfg.Hospital.Timezone` references it (default `America/Jamaica`).
7. A persisted computed column cannot convert UTC to local time (`AT TIME ZONE` is not deterministic): store the
   local date as a normal column, set by the procedure that writes the row.

### 5.9 Stored Procedure Rules

1. **External ids only.** Parameters and results use `{Model}Id`; the internal `Id` appears only in `ORDER BY`
   tie-breakers and number sequences, never in a result set.
2. **No bare `OUTPUT`.** Audit triggers sit on most tables, and SQL Server rejects `OUTPUT inserted…` without `INTO`
   on a table with triggers (error 334). Always `OUTPUT … INTO @Out` (a table variable), then `SELECT … FROM @Out`.
3. **Never write NULL into `CreatedBy`.** Pass the acting user, or fall back to the System user:
   `ISNULL(@CreatedBy, '00000000-0000-0000-0000-000000000000')`. `ModifiedBy` may be NULL.
4. **Errors are `THROW 50xxx` with a readable message**, documented next to the endpoint. Multi-statement writes
   use `SET XACT_ABORT ON` and one transaction; "all or nothing" rules (stock deduction at sign-off) run inside it.
5. **Hospital scope.** Every procedure that reads or writes hospital data filters on `@HospitalId` (or a row's own
   `HospitalId`), and rejects ids that belong to another hospital.
6. **Soft delete** sets `IsActive = 0`; lists filter `IsActive = 1` unless the caller asks for inactive rows.

---

## 6. Multi-Tenancy Rules

### Every hospital-scoped table MUST have

1. `HospitalId UNIQUEIDENTIFIER NOT NULL` column.
2. Foreign key to `cfg.Hospital(HospitalId)`.
3. Non-clustered index on `HospitalId`.
4. Unique constraints scoped to `(HospitalId, ...)` — e.g., MRN is unique per hospital, not globally.

### Tables that are NOT hospital-scoped (shared reference data)

| Table | Why shared |
|-------|-----------|
| `cfg.ICD10Catalog` | WHO standard — same globally |
| `cfg.Compliance` | Regulatory master — seeded by software company |
| `cfg.Payer` | Insurers and government schemes |
| `cfg.TimeZone` | IANA → Windows time zone map |
| `cfg.ContradictionRule`, `cfg.BranchFlowRule` | Clinical rule sets (reserved for planned features) |

**Child and junction tables without `HospitalId`** (`bil.InvoiceLineItem`, `bil.InvoiceCoverageLine`,
`bil.EligibilityVerification`, `sec.UserRole`, `sec.DoctorSpecialty`, `cfg.LabTestSpecialty`,
`inv.InventoryItemService`, `cln.DoctorLabFavorite`) take their hospital from the parent row; procedures reach
them through the parent, and the audit configuration derives the hospital the same way (`HospitalIdSource`).

**Hospital settings that change behaviour** — currency, `Timezone` (§5.8), the phone rule (§8) — live on
`cfg.Hospital`, never in code.

### Row-Level Security (Phase 4)

```sql
-- Create security policy
CREATE SECURITY POLICY opd.PatientFilter
    ADD FILTER PREDICATE sec.fn_HospitalFilter(HospitalId)
    ON opd.Patient WITH (STATE = ON);

-- Filter function
CREATE FUNCTION sec.fn_HospitalFilter(@HospitalId UNIQUEIDENTIFIER)
RETURNS TABLE WITH SCHEMABINDING AS
    RETURN SELECT 1 AS result
    WHERE @HospitalId = CAST(SESSION_CONTEXT(N'HospitalId') AS UNIQUEIDENTIFIER);
```

This ensures that even if application code has a bug, the database itself prevents cross-tenant data access.

---

## 7. Encryption-Ready Schema Design

### Rules that enable future Always Encrypted

1. **Never use PII columns in JOIN, ORDER BY, or GROUP BY.** Join on surrogate keys (`PatientId`), not on `FullName` or `Phone`. This is already true in the current schema ✅.

2. **Never use PII columns in computed columns or indexed views.** Always Encrypted columns cannot participate in computations on the server side.

3. **Search columns need deterministic encryption.** `Phone`, `Email`, `NationalHealthId` — these support exact-match `WHERE` clauses. Mark them for deterministic encryption.

4. **Display-only columns use randomized encryption.** `FullName`, `Address`, `PhotoBase64` — more secure, but only support retrieval, not search.

5. **Keep encryption metadata in comments.**

```sql
-- ENCRYPTION: DETERMINISTIC (searchable by exact match)
Phone                   NVARCHAR(20)        NOT NULL,

-- ENCRYPTION: RANDOMIZED (display only, not searchable)
[Address]               NVARCHAR(500)       NULL,

-- ENCRYPTION: NONE (clinical data, not PII)
Gender                  NVARCHAR(10)        NOT NULL,
```

### Columns marked for encryption (complete list)

The `-- ENCRYPTION:` comments in `schema.sql` are the source of truth; this table mirrors them.

| Schema.Table | Column | Encryption | Rationale |
|-------------|--------|-----------|-----------|
| cfg.Hospital | ContactNumber | DETERMINISTIC | Lookup |
| cfg.Hospital | PrimaryEmail | DETERMINISTIC | Lookup |
| sec.ApplicationUser | FullName | DETERMINISTIC | Login display |
| sec.ApplicationUser | Email | DETERMINISTIC | Login lookup |
| sec.ApplicationUser | Phone | DETERMINISTIC | Contact lookup |
| opd.Patient | FullName | DETERMINISTIC | Exact-match patient search |
| opd.Patient | Phone | DETERMINISTIC | Lookup by phone |
| opd.Patient | Mobile | DETERMINISTIC | Lookup by mobile (lab portal claim) |
| opd.Patient | Email | DETERMINISTIC | Lookup by email |
| opd.Patient | NationalHealthId | DETERMINISTIC | Lookup by Health ID |
| opd.Patient | Trn | DETERMINISTIC | Lookup by TRN (Jamaica) |
| opd.Patient | Address | RANDOMIZED | Display only |
| opd.Patient | InsuranceProvider | RANDOMIZED | Display only |
| opd.Patient | InsurancePolicyNo | RANDOMIZED | Display only |
| opd.Patient | EmergencyContactName | RANDOMIZED | Display only |
| opd.Patient | EmergencyContactPhone | RANDOMIZED | Display only |
| opd.PatientCoverage | MemberId | DETERMINISTIC | Eligibility lookup |
| opd.PatientCoverage | PolicyNumber | DETERMINISTIC | Eligibility lookup |
| opd.PatientCoverage | GovtCardNumber | DETERMINISTIC | Government scheme lookup |
| cln.ConfidentialNote | NoteText | RANDOMIZED | Display only |
| msg.NotificationConfig | ApiKey | RANDOMIZED | Secret, never searched |
| msg.MessageQueue | PatientName | RANDOMIZED | Display only |
| msg.MessageQueue | PatientPhone | RANDOMIZED | Display only |
| msg.MessageQueue | MessageText | RANDOMIZED | Display only |

`sec.ApplicationUser.PasswordHash` is a salted hash — hashed, not encrypted, and never returned by any procedure.
Columns that are encrypted or marked here are **masked** in the row-level audit log (§9): the change is recorded,
the value is not.

---

## 8. Pen-Test-Ready Design Rules

### Input validation at the data layer

```javascript
// data.js — validate before storing
createPatient: (record) => {
  // Length validation
  if (!record.FullName || record.FullName.length > 200) throw new Error('Invalid FullName');
  if (!record.Phone || record.Phone.length > 20) throw new Error('Invalid Phone');
  
  // Format validation
  if (record.Email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(record.Email)) throw new Error('Invalid Email');
  
  // Enum validation
  if (!['MALE','FEMALE','OTHER'].includes(record.Gender)) throw new Error('Invalid Gender');
  
  // Sanitize — strip HTML from text fields
  record.FullName = _stripHtml(record.FullName);
  record.Address = record.Address ? _stripHtml(record.Address) : null;
  
  // Create
  const row = { PatientId: nextId('PAT'), CreatedAt: new Date().toISOString(), IsActive: true, ...record };
  patients.push(row);
  return row;
},
```

### Rules

1. **Validate at the data layer, not just the UI.** UI validation is for user experience. Data layer validation is for security. Both must exist.

2. **Strip HTML from all text inputs** using a `_stripHtml()` helper. Exception: rich-text fields (SOAP notes) use a whitelist sanitizer allowing only `<b>`, `<i>`, `<br>`, `<ul>`, `<li>`.

3. **Validate enum values against allowed lists.** `Gender IN ('MALE','FEMALE','OTHER')`. In SQL Server, this becomes a CHECK constraint. In data.js, validate before insert.

4. **Validate string lengths.** Every `NVARCHAR(n)` column has a maximum length. Enforce it in both data.js and SQL Server.

5. **Never build queries with string concatenation.** In Phase 4, always use parameterized queries or EF Core LINQ. In data.js, array `.filter()` and `.find()` are inherently safe from SQL injection.

6. **Log security events, not PII.** Audit entries reference entity IDs, never patient names. The audit log viewer resolves names at display time via a separate lookup.

7. **Phone numbers follow the hospital's rule.** A Jamaican hospital (`Country = JM`) accepts only Jamaican numbers:
   optional `+1`, area code 876 or 658, 7 digits — `^(?:\+?1[\s-]?)?(?:876|658)[\s-]?\d{3}[\s-]?\d{4}$`. One rule
   (`Services/PhoneService.cs`), one validator (`<c360:PhoneValidator>`), checked in the browser **and** on the server.
   Hospitals in other countries keep the generic check.

---

## 9. Audit Trail Rules

### What to log

| Event | Must log | Fields |
|-------|----------|--------|
| Login success | ✅ | UserId, IP (Phase 2), Timestamp |
| Login failure | ✅ | Email attempted, IP, Timestamp |
| Patient record created | ✅ | UserId, PatientId, Timestamp |
| Patient record viewed | ✅ | UserId, PatientId, Timestamp |
| Consultation opened | ✅ | UserId, ConsultationId, Timestamp |
| Consultation signed off | ✅ | UserId, ConsultationId, Timestamp |
| Prescription created | ✅ | UserId, ConsultationId, PrescriptionId |
| Safety alert overridden | ✅ | UserId, ConsultationId, AlertType, OverrideReason |
| Lab result uploaded | ✅ | UserId (lab partner), OrderId, Timestamp |
| Lab result reviewed | ✅ | UserId (doctor), ResultId, Timestamp |
| Data exported (CSV) | ✅ | UserId, TableExported, RowCount, Timestamp |
| Print generated | ✅ | UserId, DocumentType, PatientId, Timestamp |
| User role changed | ✅ | AdminUserId, TargetUserId, OldRole, NewRole |
| Permission changed | ✅ | AdminUserId, Role, Feature, NewValue |
| Settings changed | ✅ | AdminUserId, SettingName, Timestamp |

### What NOT to log

| Data | Why not |
|------|---------|
| Patient name, phone, email | PII — log PatientId instead |
| SOAP note content | Clinical data — log ConsultationId instead |
| Prescription details | Log PrescriptionId instead |
| Password (even hashed) | Never log credentials |
| Full message text | Log MessageId instead |

### Two audit logs

| Log | Written by | Holds |
|-----|-----------|-------|
| `sec.AuditLog` | The application (`AuditService.Record`, `sec.usp_RecordAuditEntry`) | User-level events in the table above: LOGIN, PRINT, EXPORT, SIGNOFF, OVERRIDE … |
| `sec.DataAuditLog` | Generated triggers (`docs/audit-log.sql`) | Every INSERT / UPDATE / SOFT_DELETE / REACTIVATE / DELETE on an audited table: the full row on insert/delete, only the changed columns on update (`{"Col":{"o":old,"n":new}}`) |

Rules for the row-level log:

1. **Configured, not hand-written.** `sec.AuditConfig` classifies every table — Tier 0 not audited, 1 configuration,
   2 operational / financial / security, 3 clinical / PHI — with excluded columns (blobs, snapshots), masked
   columns (PHI and secrets: change recorded, value never copied) and domain actor columns (`VerifiedBy`,
   `VoidedBy` …). `sec.usp_GenerateAuditTriggers` builds the triggers from it; applying them needs a signed-off
   classification (`ReviewedBy`).
2. **Honest actor.** The System user (`00000000-…`) is recorded as unknown (NULL), and `ModifiedBy` only counts when
   the UPDATE itself set it.
3. **Insert-only.** `sec.DataAuditLog` refuses UPDATE and DELETE (DENY to public plus an INSTEAD OF trigger).
4. **Fail-open.** An audit failure writes an `AUDIT_ERROR` row and never blocks the clinical write.
5. **New table → new `sec.AuditConfig` row**, then regenerate the triggers.

### Audit log schema (`sec.AuditLog`)

```sql
CREATE TABLE sec.AuditLog (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    AuditLogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_sec_Aud_Id DEFAULT NEWSEQUENTIALID(),
    HospitalId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NULL,                       -- NULL for failed logins of unknown users
    [Action] NVARCHAR(20) NOT NULL CONSTRAINT CK_sec_Aud_Action CHECK ([Action] IN ('CREATE','READ','UPDATE','DELETE',
        'LOGIN','LOGIN_FAIL','LOGOUT','EXPORT','PRINT','OVERRIDE','SIGNOFF','SUBMIT','APPROVE','REJECT')),
    EntityType NVARCHAR(50) NOT NULL, EntityId NVARCHAR(50) NULL,
    Details NVARCHAR(500) NULL,                         -- no PII here
    IpAddress NVARCHAR(45) NULL, UserAgent NVARCHAR(200) NULL,
    [Timestamp] DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_sec_Aud_Ts DEFAULT SYSUTCDATETIME(),
    -- + the standard metadata columns (§10)
    CONSTRAINT PK_sec_Aud PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_sec_Aud_Id UNIQUE NONCLUSTERED (AuditLogId),
    INDEX IX_sec_Aud_HospitalId NONCLUSTERED (HospitalId, [Timestamp] DESC)
);
```

---

## 10. Metadata Columns — Standard Set

**Every table** includes these columns:

| Column | Type | Default | Purpose |
|--------|------|---------|---------|
| `CreatedAt` | `DATETIMEOFFSET(3) NOT NULL` | `SYSUTCDATETIME()` | When the row was created (UTC) |
| `CreatedBy` | `UNIQUEIDENTIFIER NOT NULL` | System user `00000000-0000-0000-0000-000000000000` | Who created it (UserId); System when no user acted (seed data, self-registration, background jobs) |
| `ModifiedAt` | `DATETIMEOFFSET(3) NULL` | NULL | Last modification (UTC) |
| `ModifiedBy` | `UNIQUEIDENTIFIER NULL` | NULL | Who last modified it |
| `IsActive` | `BIT NOT NULL` | 1 | Business state and soft delete (`0` = deleted) |

There is no `IsDeleted`, `DeletedAt`, `UpdatedAt` or `UpdatedBy`.

**Exceptions:**
- Junction tables (two-column keys) have no `IsActive`: their rows are replaced as a set.
- `sec.DataAuditLog` is insert-only — `ModifiedAt` / `ModifiedBy` stay NULL and `IsActive` stays 1.
- Append-only ledgers (`inv.StockMovement`, `sec.AuditLog`) are never updated after insert.

---

## 11. Phase 4 Migration Mapping

### data.js → Entity Framework

| data.js | EF Core |
|---------|---------|
| `const patients = [...]` | `DbSet<Patient> Patients` |
| `patients.find(p => p.PatientId === id)` | `await _db.Patients.FindAsync(id)` |
| `patients.filter(p => p.HospitalId === hid)` | `await _db.Patients.Where(p => p.HospitalId == hid).ToListAsync()` |
| `patients.push(row)` | `_db.Patients.Add(entity); await _db.SaveChangesAsync()` |
| `Object.assign(existing, patch)` | `_db.Entry(entity).CurrentValues.SetValues(dto); await _db.SaveChangesAsync()` |

### api.js → ASP.NET Controller

| api.js | ASP.NET |
|--------|---------|
| `getPatient: (id) => ok(...)` | `[HttpGet("{id}")] public async Task<ActionResult<PatientDto>> GetPatient(Guid id)` |
| `listPatients: (hid) => ok(...)` | `[HttpGet] public async Task<ActionResult<List<PatientDto>>> ListPatients()` |
| `createPatient: (record) => ok(...)` | `[HttpPost] public async Task<ActionResult<PatientDto>> CreatePatient(CreatePatientDto dto)` |
| `_hid()` fallback | `User.Claims.First(c => c.Type == "HospitalId").Value` from JWT |

### Router → ASP.NET Middleware

| JS router | ASP.NET |
|-----------|---------|
| `ROUTE_PERMS` map | `[Authorize(Policy = "RequireConsultation")]` attribute |
| `hasPermission(role, perm)` | Policy-based authorization with `IAuthorizationHandler` |
| `_hid()` session guard | `HospitalIdMiddleware` reads JWT claim, sets `SESSION_CONTEXT` for RLS |

---

## 12. Checklist — Before Writing Any New Table

Before adding a new table to `schema.sql` (and its list to `DataStore.cs`), verify:

- [ ] Table name is singular PascalCase, no prefix, named after its model
- [ ] Assigned to correct schema (cfg/sec/opd/cln/bil/inv/msg)
- [ ] Internal key `Id BIGINT IDENTITY` (clustered PK) and external `{Model}Id UNIQUEIDENTIFIER DEFAULT NEWSEQUENTIALID()` (unique) — or a two-column key for a junction
- [ ] Foreign keys reference the external `{Model}Id`
- [ ] `HospitalId` column, FK to `cfg.Hospital` and index exist (if hospital-scoped), or the hospital is reachable through the parent
- [ ] All FK columns used in joins have indexes
- [ ] `CreatedAt`, `CreatedBy` (NOT NULL, System default), `ModifiedAt`, `ModifiedBy`, `IsActive` columns exist
- [ ] Points in time are `DATETIMEOFFSET` UTC; business events also carry `TimeZoneId` and a local date column; calendar dates are `DATE`
- [ ] All constraints are explicitly named `{Type}_{Schema}_{Abbr}_…`
- [ ] Enum columns have CHECK constraints with allowed values
- [ ] PII columns are marked with an `-- ENCRYPTION:` comment (and added to §7)
- [ ] Boolean columns are prefixed with `Is` or `Has`
- [ ] No PII used as primary key, foreign key, or join column
- [ ] Procedures use the 14 verbs (`usp_{Verb}{Entity}`), `OUTPUT … INTO`, and never write NULL into `CreatedBy`
- [ ] A `sec.AuditConfig` row classifies the table (tier, excluded / masked columns), and the triggers are regenerated
- [ ] Endpoints are in `API-SPECIFICATION.md` with the procedure's parameters as request fields
- [ ] Audit entries use entity IDs, never PII values

---

*Document version: 1.1 — October 2026 (dual keys, UTC / time zone rules, metadata columns, two audit logs,*
*inv schema, stored-procedure rules, phone rule, current encryption list). v1.0 — August 2026.*
*Applicable to: Consult360 Build v5.7+; schema.sql v4.0, stored-procedures.sql v3.4, audit-log.sql*
*Companion documents: ENCRYPTION-STRATEGY.md, PEN-TEST-STRATEGY.md*
