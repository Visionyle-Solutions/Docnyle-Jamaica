# Consult360 — Data Layer & Schema Architecture Rules

### Governing Rules for data.js, api.js, and SQL Server 2019 Schema

---

## 1. Purpose

This document defines the binding rules for three tightly coupled layers:

```
data.js (in-memory arrays)  →  api.js (async service)  →  SQL Server 2019 (production)
```

The goal is that migration from Phase 1 (JavaScript) to Phase 4 (ASP.NET / Blazor + SQL Server) requires **zero changes to method signatures, naming, or behavior contracts**. The `api.js` method names become ASP.NET controller action names. The data.js array shapes become Entity Framework models. The schema is encryption-ready and pen-test-ready from day one.

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

---

## 3. api.js — Service Layer Rules

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

3. **Every array must have a primary key property.** Named `{EntityName}Id` (e.g., `PatientId`, `ConsultationId`).

4. **Every hospital-scoped array must have `HospitalId`.** This is the multi-tenancy key. No exceptions.

5. **ID generation uses `nextId(prefix)`.** In Phase 4, this becomes `NEWID()` or `IDENTITY` in SQL Server.

6. **No derived/computed properties in stored data.** Computed values (BMI, age, outstanding balance) are calculated at read time, not stored. Exception: denormalized fields for performance (e.g., `PatientName` on Appointment for display — clearly documented as denormalized).

7. **Timestamps use ISO 8601 strings.** `new Date().toISOString()` in JS, `DATETIME2` in SQL Server. Field names: `CreatedAt`, `UpdatedAt`, `DeletedAt`.

8. **Soft delete preferred.** Use `IsActive: true/false` or `DeletedAt: null/timestamp`. Hard delete only for truly transient data (e.g., search cache).

9. **Boolean fields prefixed with `Is` or `Has`.** `IsActive`, `IsMandatory`, `HasAllergy`. Not `Active`, `Mandatory`, `Allergy`.

10. **Enum fields store string codes, not numbers.** `Status: 'ACTIVE'` not `Status: 1`. In SQL Server, these become `NVARCHAR(20)` with a CHECK constraint. Self-documenting in queries.

---

## 5. SQL Server 2019 Schema Rules

### 5.1 Schema (Namespace) Strategy

Schemas group related tables. Maximum 5 characters. Every table belongs to exactly one schema.

| Schema | Name | Tables | Purpose |
|--------|------|--------|---------|
| `opd` | OPD Core | Patients, Appointments, Slots, Queue | Patient management, scheduling |
| `cln` | Clinical | Consultations, Vitals, Diagnoses, Symptoms, Prescriptions, LabOrders, LabResults, Referrals, SOAP, Addendums, ConfidentialNotes | Clinical data |
| `bil` | Billing | Invoices, InvoiceLineItems, Payments, Receipts, DoctorFeeSchedule | Financial |
| `cfg` | Config | Hospitals, VitalDefinitions, MedHistoryConfig, SpecialtyConfig, Compliances, HospitalCompliances, DrugFormulary, LabTestCatalog, ICD10Catalog, SymptomCatalog, PhraseBank, MessageTemplates | Reference data, settings |
| `sec` | Security | ApplicationUsers, PermissionMatrix, AuditLog, LoginAttempts | Auth, RBAC, audit |
| `msg` | Messaging | MessageQueue, NotificationConfig | Communications |

### 5.2 Table Naming

| Rule | Convention | Example |
|------|-----------|---------|
| Singular noun | Always singular, PascalCase | `Patient`, not `Patients` |
| Schema-qualified | Always reference with schema | `opd.Patient`, not `dbo.Patient` |
| No prefixes | No `tbl_`, no `T_` | `opd.Patient`, not `opd.tblPatient` |
| Junction tables | Both entities joined with underscore | `cln.Consultation_Diagnosis` |
| History/audit tables | Suffix `_History` | `sec.LoginAttempt_History` |

### 5.3 Column Naming

| Rule | Convention | Example |
|------|-----------|---------|
| PascalCase | Every column | `FullName`, `CreatedAt` |
| Primary key | `{TableName}Id` | `PatientId`, `ConsultationId` |
| Foreign key | Same name as the referenced PK | `HospitalId` references `cfg.Hospital.HospitalId` |
| Boolean | Prefix `Is` or `Has` | `IsActive`, `HasAllergy` |
| Date/time | Suffix `At`, `Date`, `From`, `To` | `CreatedAt`, `VisitDate`, `EffectiveFrom` |
| Amount/money | Suffix `Amount`, `Fee`, `Charge` | `TotalAmount`, `ConsultationFee` |
| Count | Suffix `Count` or `Qty` | `RefillCount`, `TakeInQty` |
| Enum/status | Use the concept name | `Status`, `Severity`, `Priority` |
| No abbreviations | Except universally known | `Id` (not `Identifier`), `Qty` (not `Quantity`), `Bp` (not `BloodPressure`) |
| No underscores in column names | Use PascalCase only | `FullName`, not `full_name` |

### 5.4 Data Types — Standard Mapping

| JS type | SQL Server type | When to use |
|---------|----------------|-------------|
| String (short) | `NVARCHAR(50-200)` | Names, codes, labels |
| String (long) | `NVARCHAR(MAX)` | SOAP notes, instructions, Base64 images |
| String (enum) | `NVARCHAR(20)` + CHECK constraint | Status, Severity, Role |
| Number (integer) | `INT` | Counts, sequences |
| Number (decimal) | `DECIMAL(10,2)` | Money, measurements |
| Number (float) | `DECIMAL(5,1)` | Temperature, BMI |
| Boolean | `BIT` | Flags (`IsActive`, `IsMandatory`) |
| Date | `DATE` | Birth date, effective date |
| Timestamp | `DATETIME2(3)` | CreatedAt, UpdatedAt |
| UUID | `UNIQUEIDENTIFIER` | Primary keys, foreign keys |
| JSON blob | `NVARCHAR(MAX)` | Flexible config, avoid if possible |

### 5.5 Constraint Naming

Every constraint is explicitly named. No system-generated names.

| Constraint type | Pattern | Example |
|----------------|---------|---------|
| Primary key | `PK_{Schema}_{Table}` | `PK_opd_Patient` |
| Foreign key | `FK_{Schema}_{Table}_{Column}` | `FK_cln_Consultation_PatientId` |
| Unique | `UQ_{Schema}_{Table}_{Columns}` | `UQ_opd_Patient_HospitalId_Phone` |
| Check | `CK_{Schema}_{Table}_{Column}` | `CK_cln_Consultation_Status` |
| Default | `DF_{Schema}_{Table}_{Column}` | `DF_opd_Patient_IsActive` |
| Index | `IX_{Schema}_{Table}_{Columns}` | `IX_opd_Patient_HospitalId` |
| Unique index | `UX_{Schema}_{Table}_{Columns}` | `UX_sec_User_Email` |

### 5.6 Index Strategy

| Index type | When | Naming |
|-----------|------|--------|
| Primary key (clustered) | Every table | `PK_{Schema}_{Table}` — on `{Table}Id` column |
| HospitalId (non-clustered) | Every hospital-scoped table | `IX_{Schema}_{Table}_HospitalId` — the multi-tenancy filter |
| Foreign key columns | Every FK column | `IX_{Schema}_{Table}_{FKColumn}` — join performance |
| Status/type columns | Tables with status-based queries | `IX_{Schema}_{Table}_Status` |
| Date columns | Tables queried by date range | `IX_{Schema}_{Table}_{DateColumn}` |
| Search columns | Patient name, drug name | Full-text index or `IX` on deterministic-encrypted columns |
| Composite | Frequent multi-column WHERE | `IX_{Schema}_{Table}_{Col1}_{Col2}` |

**Mandatory indexes on every hospital-scoped table:**

```sql
-- Every table gets these two minimum:
CONSTRAINT PK_opd_Patient PRIMARY KEY CLUSTERED (PatientId),
INDEX IX_opd_Patient_HospitalId NONCLUSTERED (HospitalId)
```

### 5.7 Example Table — Full Convention

```sql
CREATE TABLE opd.Patient (
    -- Primary key
    PatientId               UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT DF_opd_Patient_PatientId DEFAULT NEWID(),
    
    -- Multi-tenancy
    HospitalId              UNIQUEIDENTIFIER    NOT NULL,
    
    -- PII fields (encryption-ready — see Section 7)
    FullName                NVARCHAR(200)       NOT NULL,
    Phone                   NVARCHAR(20)        NOT NULL,
    Email                   NVARCHAR(100)       NULL,
    [Address]               NVARCHAR(500)       NULL,
    NationalHealthId        NVARCHAR(50)        NULL,
    InsurancePolicyNo       NVARCHAR(50)        NULL,
    PhotoBase64             NVARCHAR(MAX)       NULL,
    EmergencyContactName    NVARCHAR(200)       NULL,
    EmergencyContactPhone   NVARCHAR(20)        NULL,
    
    -- Non-PII demographic
    Gender                  NVARCHAR(10)        NOT NULL
        CONSTRAINT CK_opd_Patient_Gender CHECK (Gender IN ('MALE','FEMALE','OTHER')),
    Dob                     DATE                NOT NULL,
    Mrn                     NVARCHAR(20)        NOT NULL,
    
    -- Insurance
    InsuranceProvider       NVARCHAR(100)       NULL,
    
    -- Metadata
    IsActive                BIT                 NOT NULL
        CONSTRAINT DF_opd_Patient_IsActive DEFAULT 1,
    CreatedAt               DATETIME2(3)        NOT NULL
        CONSTRAINT DF_opd_Patient_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt               DATETIME2(3)        NULL,
    CreatedBy               UNIQUEIDENTIFIER    NULL,
    
    -- Constraints
    CONSTRAINT PK_opd_Patient PRIMARY KEY CLUSTERED (PatientId),
    CONSTRAINT FK_opd_Patient_HospitalId 
        FOREIGN KEY (HospitalId) REFERENCES cfg.Hospital(HospitalId),
    CONSTRAINT UQ_opd_Patient_HospitalId_Mrn 
        UNIQUE (HospitalId, Mrn),
    
    -- Indexes
    INDEX IX_opd_Patient_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_opd_Patient_HospitalId_Phone NONCLUSTERED (HospitalId, Phone),
    INDEX IX_opd_Patient_HospitalId_FullName NONCLUSTERED (HospitalId, FullName),
);
```

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
| `cfg.LabTestCatalog` | Standard test names |

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

| Schema.Table | Column | Encryption | Rationale |
|-------------|--------|-----------|-----------|
| opd.Patient | FullName | DETERMINISTIC | Exact-match patient search |
| opd.Patient | Phone | DETERMINISTIC | Lookup by phone |
| opd.Patient | Email | DETERMINISTIC | Lookup by email |
| opd.Patient | NationalHealthId | DETERMINISTIC | Lookup by Health ID |
| opd.Patient | Address | RANDOMIZED | Display only |
| opd.Patient | InsurancePolicyNo | RANDOMIZED | Display only |
| opd.Patient | PhotoBase64 | RANDOMIZED | Display only |
| opd.Patient | EmergencyContactName | RANDOMIZED | Display only |
| opd.Patient | EmergencyContactPhone | RANDOMIZED | Display only |
| sec.ApplicationUser | FullName | DETERMINISTIC | Login display |
| sec.ApplicationUser | Email | DETERMINISTIC | Login lookup |
| sec.ApplicationUser | Phone | DETERMINISTIC | Contact lookup |
| sec.ApplicationUser | PasswordHash | RANDOMIZED | Never searched |
| msg.MessageQueue | PatientName | RANDOMIZED | Display only |
| msg.MessageQueue | PatientPhone | RANDOMIZED | Display only |
| msg.MessageQueue | MessageText | RANDOMIZED | Display only |
| cfg.HospitalCompliance | RegistrationNumber | RANDOMIZED | Display only |
| cfg.Hospital | Phone | DETERMINISTIC | Lookup |
| cfg.Hospital | Email | DETERMINISTIC | Lookup |

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

### Audit log schema

```sql
CREATE TABLE sec.AuditLog (
    AuditLogId          UNIQUEIDENTIFIER    NOT NULL
        CONSTRAINT DF_sec_AuditLog_Id DEFAULT NEWID(),
    HospitalId          UNIQUEIDENTIFIER    NOT NULL,
    UserId              UNIQUEIDENTIFIER    NOT NULL,
    Action              NVARCHAR(20)        NOT NULL
        CONSTRAINT CK_sec_AuditLog_Action 
        CHECK (Action IN ('CREATE','READ','UPDATE','DELETE','LOGIN','LOGIN_FAIL',
                          'EXPORT','PRINT','OVERRIDE','SIGNOFF','SUBMIT','APPROVE','REJECT')),
    EntityType          NVARCHAR(50)        NOT NULL,
    EntityId            NVARCHAR(36)        NULL,
    Details             NVARCHAR(500)       NULL,  -- No PII here
    IpAddress           NVARCHAR(45)        NULL,  -- Phase 2
    UserAgent           NVARCHAR(200)       NULL,  -- Phase 2
    CreatedAt           DATETIME2(3)        NOT NULL
        CONSTRAINT DF_sec_AuditLog_CreatedAt DEFAULT SYSUTCDATETIME(),
    
    CONSTRAINT PK_sec_AuditLog PRIMARY KEY CLUSTERED (AuditLogId),
    INDEX IX_sec_AuditLog_HospitalId NONCLUSTERED (HospitalId),
    INDEX IX_sec_AuditLog_UserId NONCLUSTERED (UserId),
    INDEX IX_sec_AuditLog_CreatedAt NONCLUSTERED (CreatedAt DESC),
    INDEX IX_sec_AuditLog_EntityType_EntityId NONCLUSTERED (EntityType, EntityId),
);
```

---

## 10. Metadata Columns — Standard Set

Every table (except pure reference/catalog tables) must include these columns:

| Column | Type | Default | Purpose |
|--------|------|---------|---------|
| `CreatedAt` | `DATETIME2(3)` | `SYSUTCDATETIME()` | When the record was created |
| `CreatedBy` | `UNIQUEIDENTIFIER` | NULL | Who created it (UserId) |
| `UpdatedAt` | `DATETIME2(3)` | NULL | Last modification timestamp |
| `UpdatedBy` | `UNIQUEIDENTIFIER` | NULL | Who last modified it |
| `IsActive` | `BIT` | 1 | Soft delete flag |

**Exception:** `sec.AuditLog` is append-only — no `UpdatedAt`, `UpdatedBy`, or `IsActive`. Audit entries are immutable.

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

Before adding a new table to `data.js` or `schema.sql`, verify:

- [ ] Table name is singular PascalCase, no prefix
- [ ] Assigned to correct schema (opd/cln/bil/cfg/sec/msg)
- [ ] Primary key is `{TableName}Id` of type `UNIQUEIDENTIFIER`
- [ ] `HospitalId` column exists (if hospital-scoped)
- [ ] Foreign key to `cfg.Hospital` exists (if hospital-scoped)
- [ ] Index on `HospitalId` exists
- [ ] All FK columns have indexes
- [ ] `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IsActive` columns exist
- [ ] All constraints are explicitly named using the convention
- [ ] Enum columns have CHECK constraints with allowed values
- [ ] PII columns are marked with encryption annotation comment
- [ ] Boolean columns are prefixed with `Is` or `Has`
- [ ] No PII used as primary key, foreign key, or join column
- [ ] Corresponding data.js array exists with comment `// → SQL: {schema}.{Table}`
- [ ] All methods use standard 14-verb naming
- [ ] api.js wrapper exists with `_hid()` guard if hospital-scoped
- [ ] Audit entries use entity IDs, never PII values

---

*Document version: 1.0 — August 2026*
*Applicable to: Consult360 Build v5.7+*
*Companion documents: ENCRYPTION-STRATEGY.md, PEN-TEST-STRATEGY.md*
