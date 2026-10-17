# Consult360 — WCF Service Specification (DOCNyleUnifiedAPI)

**Version:** 3.3
**Platform:** .NET Framework 4.8 · Windows Communication Foundation (WCF) hosted in IIS ·
SQL Server 2019 (`docs/schema.sql`, `docs/stored-procedures.sql`)
**Service:** `DOCNyleUnifiedAPI` — one `.svc`, one service contract (`IDOCNyleUnifiedAPI`)
**Consumer:** the Consult360 ASP.NET Web Forms application (`OpdProject`)

This document is the service contract for Consult360's data layer. Every operation below is
one `[OperationContract]` on the single WCF contract `IDOCNyleUnifiedAPI`, and every operation
names the stored procedure in `docs/stored-procedures.sql` that backs it. Consult360 does not
use ASP.NET MVC or Web API: the pages are Web Forms, and the data layer is the
`DOCNyleUnifiedAPI` WCF service.

**v3.3 change note:** closes v3.2 Known Gaps #4–#7 (procedures v3.6). `CreatePatient` /
`UpdatePatient` carry every registration field and `RegisteredVia`, and MRNs follow the
hospital's MRN settings; sign-in verifies the salted hash through `sec.usp_GetUserCredential` with
a 5-failure lockout; `CreateAppointment` re-checks the slot (new `Conflict` 50214); slot lists
reject lengths that never end. Operation count unchanged (261). Data contracts only gained
members.

**v3.2 change note:** 54 operations for the features the Web Forms app ran only in memory
(v3.1 Known Gaps #1; `API-INTEGRATION-GUIDE.md` §8, gaps G-1 … G-17), backed by section 22 of
`stored-procedures.sql` (v3.5) and one new table, `sec.LabClaimAttempt` (`schema.sql` v4.1).
New rows are tagged *v3.2*. Also: the patient-portal operations (§5), the first operations that
run without a session other than sign-in; file content for consent documents and lab
attachments travels as MTOM `byte[]`; new error numbers 50200–50249 (§0.6).

**v3.1 change note:** matched to the `DOCNyleUnifiedAPI` solution — a single service
(`DOCNyleUnifiedAPI.svc` / `.svc.cs`), a single interface file (`IDOCNyleUnifiedAPI.cs`), and
the data access in the `DOCNyleUnifiedAPI.DAL` project (`DataRepository.cs`, `Helper.cs`).
v3.0's 13 separate services (`IPatientService`, `IBillingService`, …) are now **regions** of the
one contract; operation names, inputs, returns and procedures are unchanged.

**v3.0 change note:** rewritten for WCF. Routes and HTTP verbs are gone; each REST endpoint is
now an operation. The procedures, their parameters, return shapes and error numbers are
unchanged from v2.3. Also changed:

- `hospitalId` and actor fields (`createdBy`, `modifiedBy`, `recordedByUserId`, …) are no
  longer parameters. The service takes them from the caller's session (§0.4).
- HTTP status codes are replaced by SOAP faults (`ServiceFault`, §0.6). v2.3's "HTTP 409"
  responses are `Conflict` faults.
- Operations v2.3 marked "AUDIT: caller must also call `POST /audit/logs`" now write that audit
  entry themselves, in the same call (§0.7).
- Duplicate entries that only existed for the retired `js/api.js` (`getDoctorFee` beside
  `checkEffectiveFee`, `exportTableCsv` beside `listExportData`) are one operation each.

---

## 0. Conventions

### 0.1 Solution layout

```
Solution 'DOCNyleUnifiedAPI'
├── DOCNyleUnifiedAPI                 WCF Service Application (.NET 4.8, IIS)
│   ├── DOCNyleUnifiedAPI.svc         the one service endpoint
│   │   └── DOCNyleUnifiedAPI.svc.cs  class DOCNyleUnifiedAPI : IDOCNyleUnifiedAPI — every operation
│   ├── IDOCNyleUnifiedAPI.cs         [ServiceContract] IDOCNyleUnifiedAPI + all [DataContract]s
│   │                                 + ServiceFault
│   ├── Web.config                    <system.serviceModel>, connection string, appSettings
│   └── packages.config
└── DOCNyleUnifiedAPI.DAL             Class library (.NET 4.8)
    ├── DataRepository.cs             one method per stored procedure
    ├── Helper.cs                     connection, SESSION_CONTEXT, parameter and reader helpers
    ├── App.Config                    design-time only — runtime settings come from the service's Web.config
    └── packages.config
```

| File | Responsibility |
|------|----------------|
| `IDOCNyleUnifiedAPI.cs` | The `[ServiceContract]` interface with every `[OperationContract]`, grouped in `#region`s that follow §1–§13 of this document; the `[DataContract]` classes (request and response types) and `ServiceFault`, each region's types after its operations. |
| `DOCNyleUnifiedAPI.svc.cs` | Implements the interface. Each operation: check the session and feature (§0.4–0.5), validate input, call `DataRepository`, map rows to data contracts, write the audit entry (§0.7). A shared wrapper turns exceptions into `ServiceFault` (§0.6). No SQL here. |
| `DataRepository.cs` | One public method per stored procedure (`SearchPatients` → `opd.usp_SearchPatients`), `CommandType.StoredProcedure`, parameters by name. Returns rows / `DataTable`s / simple DAL types — never WCF data contracts, so the DAL does not reference the service project. |
| `Helper.cs` | Opens the connection from the `DOCNyle` connection string, runs `sp_set_session_context` (§0.4), adds parameters (null → `DBNull.Value`), reads nullable columns, and retries transient SQL errors. |
| `OpdProject` (Web Forms) | Calls the service through the generated client in **Connected Services** (§0.9). Until the service is deployed, the in-process classes in `Services/*Service.cs` (backed by `DataStore`) stand in for it; each region below names the class it replaces. |

### 0.2 Endpoint and regions

Address: `https://{host}/DOCNyleUnifiedAPI.svc` — one endpoint for every operation. Contract
namespace: `http://schemas.consult360.com/opd/2026/10`.

The contract is one interface; the regions only organize it (and this document). Each region
has a default feature from `sec.PermissionMatrix`; operations that need a different feature say
so.

| § | Region in `IDOCNyleUnifiedAPI` | Replaces (in-process) | Feature (`sec.PermissionMatrix`) |
|---|--------------------------------|-----------------------|----------------------------------|
| 1 | Auth | `UserService.Authenticate` | — (anonymous + session) |
| 2 | Users & permissions | `UserService` | `admin_users` |
| 3 | Hospital & configuration | `HospitalService`, `TimeZoneService` | read: any signed-in user; write: `admin_hospital` |
| 4 | Patients | `PatientService` | `patients` |
| 5 | Scheduling | `SchedulingService` | `scheduling`; settings: `admin_scheduling` |
| 6 | Consultation | `ConsultationService` | `consultation` |
| 7 | Catalog | `CatalogService`, `PhraseService` | `consultation`; formulary admin: `admin_formulary` |
| 8 | Lab | `LabService` | ordering/review: `consultation`; portal: `lab_portal` |
| 9 | Billing | `BillingService` | `billing` |
| 10 | Inventory | `InventoryService` | `admin_inventory` |
| 11 | Reports | `ReportService` | `reports` |
| 12 | Notifications | `NotificationService` | `admin_hospital` |
| 13 | Audit & export | `AuditService`, `ExportService` | `audit_log` / `data_export` |

Roles: ADMIN, DOCTOR, RECEPTIONIST, BILLING, NURSE, LAB_PARTNER. A role reaches an operation
when the permission matrix grants it the region's feature. Operation names are unique across
the whole contract (they are the procedure names, §0.10), so no two regions clash.

### 0.3 Binding and hosting

- **Binding:** `basicHttpBinding`, `security mode="Transport"` (HTTPS only),
  `messageEncoding="Mtom"` so `byte[]` payloads (logos, sketches, CSV exports) travel as binary
  attachments rather than base64 text.
- **Limits:** `maxReceivedMessageSize="52428800"` (50 MB) with matching `readerQuotas`, the same
  ceiling as the site's `httpRuntime maxRequestLength` / `requestLimits`.
- **Behaviors:** `InstanceContextMode.PerCall`, `ConcurrencyMode.Multiple` on the
  `DOCNyleUnifiedAPI` class. No WCF sessions — the caller's session is the `SessionToken` header
  (§0.4). `includeExceptionDetailInFaults="false"` outside Debug builds; metadata
  (`serviceMetadata httpsGetEnabled`) on in Debug so Connected Services can refresh, off in
  Release.
- **Same-server option:** when the service and the site share a machine, a second
  `netNamedPipeBinding` endpoint on the same service can be used instead; the contract does not
  change.

```xml
<!-- DOCNyleUnifiedAPI/Web.config (excerpt) -->
<connectionStrings>
  <add name="DOCNyle" connectionString="…" providerName="System.Data.SqlClient" />
</connectionStrings>
<system.serviceModel>
  <bindings>
    <basicHttpBinding>
      <binding name="docnyleHttps" messageEncoding="Mtom" maxReceivedMessageSize="52428800">
        <readerQuotas maxStringContentLength="52428800" maxArrayLength="52428800" />
        <security mode="Transport" />
      </binding>
    </basicHttpBinding>
  </bindings>
  <behaviors>
    <serviceBehaviors>
      <behavior name="docnyle">
        <serviceMetadata httpsGetEnabled="true" />   <!-- false in Web.Release.config -->
        <serviceDebug includeExceptionDetailInFaults="false" />
      </behavior>
    </serviceBehaviors>
  </behaviors>
  <services>
    <service name="DOCNyleUnifiedAPI.DOCNyleUnifiedAPI" behaviorConfiguration="docnyle">
      <endpoint address="" binding="basicHttpBinding" bindingConfiguration="docnyleHttps"
                contract="DOCNyleUnifiedAPI.IDOCNyleUnifiedAPI" />
    </service>
  </services>
</system.serviceModel>
```

```xml
<%@ ServiceHost Language="C#" Service="DOCNyleUnifiedAPI.DOCNyleUnifiedAPI" CodeBehind="DOCNyleUnifiedAPI.svc.cs" %>
```

### 0.4 Session, caller context and tenancy

- **Sign-in:** `AuthenticateUser` and `CreateUserSession` (§1) are the only operations that run
  without a session. They return the session token the Web Forms app keeps in `AppSession`.
- **Every other call** carries a SOAP header `SessionToken` (namespace as §0.2), added on the
  client by a message inspector registered on the generated client (§0.9). An optional
  `CorrelationId` header is logged with the call.
- **Server side:** each operation in `DOCNyleUnifiedAPI.svc.cs` starts with
  `var caller = Authorize("feature");`. `Authorize` reads the header from
  `OperationContext.Current.IncomingMessageHeaders`, validates the token through
  `DataRepository.CheckSessionValid` (`sec.usp_CheckSessionValid`; a short `MemoryCache` is
  allowed), and returns a `CallerContext` with the user's `UserId`, `HospitalId`, roles and the
  hospital's `TimeZoneId`. A missing, expired or revoked token is an `Unauthenticated` fault.
- **Tenancy:** the hospital always comes from `caller.HospitalId`, never from the message. The
  service passes it to every `DataRepository` method, which uses it as the procedure's
  `@HospitalId`; `Helper` runs
  `EXEC sp_set_session_context N'HospitalId', @hid, @read_only = 1` on every connection it opens
  so row-level security applies (`DATA-LAYER-ARCHITECTURE.md` §6). An id that belongs to another
  hospital is reported as `NotFound`.
- **Actor fields:** `@CreatedBy`, `@ModifiedBy`, `@RecordedByUserId`, `@UploadedByUserId`,
  `@IssuedBy`, `@ReceivedBy`, `@VoidedBy`, `@CheckedInBy` and similar are filled from
  `caller.UserId`. They are not data members of any request.

### 0.5 Authorization

`Authorize("feature")` also checks the caller's roles against `sec.PermissionMatrix`
(`DataRepository.GetPermissionMatrix`, cached per hospital and cleared by `UpdatePermission`)
and raises a `Forbidden` fault when no role grants the feature. The region's feature is in
§0.2; operation-level exceptions are listed with the operation. This is the same check the
pages make today with `UserService.HasPermission`.

### 0.6 Faults

Every operation is declared with `[FaultContract(typeof(ServiceFault))]`. `ServiceFault` is
defined in `IDOCNyleUnifiedAPI.cs`:

```csharp
[DataContract(Namespace = Ns.V1)]
public class ServiceFault
{
    [DataMember(Order = 1)] public FaultCode Code { get; set; }
    [DataMember(Order = 2)] public string Message { get; set; }        // safe to show the user
    [DataMember(Order = 3)] public int? ErrorNumber { get; set; }      // SQL THROW number, e.g. 50040
    [DataMember(Order = 4)] public List<FieldError> Fields { get; set; } // Validation only
}

[DataContract(Namespace = Ns.V1)]
public enum FaultCode
{
    [EnumMember] Validation, [EnumMember] NotFound, [EnumMember] Conflict, [EnumMember] BusinessRule,
    [EnumMember] Unauthenticated, [EnumMember] Forbidden, [EnumMember] Internal
}
```

Each operation body runs inside one wrapper in `DOCNyleUnifiedAPI.svc.cs`
(`return Run(() => { … });`) that converts exceptions to `FaultException<ServiceFault>`:

| Source | `FaultCode` |
|--------|-------------|
| Request fails validation (missing required member, bad enum value, string too long) | `Validation` |
| Procedure returns no row for the id given, or the row belongs to another hospital | `NotFound` |
| A state change the record's current state does not allow — appointment status moves (v2.3's "null / HTTP 409"), invoice cancelled (50121), payer already settled (50122), removing a settled coverage line; v3.2: appointment not BOOKED (50213), slot just taken (50214), schedule locked by another booking (50220), form draft changed by someone else (50241) | `Conflict` |
| Any other `SqlException` with `Number` 50000–59999 (the `THROW`s in `stored-procedures.sql`) | `BusinessRule`, with `ErrorNumber` and the procedure's message |
| `SessionToken` missing / invalid / expired | `Unauthenticated` |
| Feature not granted to any of the caller's roles; confidential note requested by a non-author | `Forbidden` |
| Anything else | `Internal` (message is generic; details go to the server log with the `CorrelationId`) |

Error numbers in use: 50020–50024 (stock receipts/adjustments), 50030–50033 (consultation
services), 50040 (not enough stock at sign-off), 50050 (hospital has no timezone), 50060–50065
(doctor profiles), 50070–50073 (hospital profile), 50120–50122 (payments / coverage);
v3.2: 50200–50204 (patient records, import), v3.3: 50205–50206 (MRN format), 50210–50212 (payers), 50213–50216 and 50220
(booking), 50217–50219 (outside medicines, orders, drugs), 50230–50236 (lab claim, partners,
attachments), 50240–50246 (consultation form, custom values). `Run` maps 50213, 50214, 50220
and 50241 to `Conflict` beside 50121 / 50122.

### 0.7 Audit

Operations marked **Audited: ACTION** write their `sec.AuditLog` entry themselves (via
`DataRepository.RecordAuditEntry` → `sec.usp_RecordAuditEntry`) inside the same call, with
`EntityType` / `EntityId` set from the operation. Callers do not record those again. Other
screen-level audit events the pages record today with `AuditService.Record(...)` go through the
`RecordAuditEntry` operation (§13).

### 0.8 Data contracts

- All `[DataContract]` classes live in `IDOCNyleUnifiedAPI.cs` beside the interface, use the
  namespace in §0.2, implement `IExtensibleDataObject`, and give every `[DataMember]` an explicit
  `Order`. New members are only ever added (with a higher `Order`, `IsRequired = false`); a
  breaking change gets a new namespace date.
- Member names are PascalCase, the same as the `Models/*.cs` properties they replace.
- **Ids** are `string`, holding the record's external id (`{Table}Id` UUID in SQL; the
  in-memory store's readable ids such as `PAT-001` until then). The internal `Id` column never
  leaves the database.
- **Instants** (`CreatedAt`, `SignedAt`, `SlotStart`, `VisitDate`, …) are `DateTime` with
  `Kind = Utc` (serialized with `Z`).
- **Clinic-clock values** (`SlotStartLocal`, `SlotEndLocal`) are `DateTimeOffset` in the
  hospital's zone, returned beside `TimeZoneId` (IANA, e.g. `America/Jamaica`). Show them in
  that zone for every user.
- **Calendar dates** (`Date`, `LeaveDate`, `ExpiryDate`, `FollowUpDate`, report
  `FromDate`/`ToDate`) are `DateTime` with `Kind = Unspecified` and time 00:00 — a date in the
  hospital's timezone, not UTC.
- **Wall-clock slot times** (`ListAvailableSlots` output, `CreateAppointment` input) are
  `DateTime` with `Kind = Unspecified` and no offset; pass them back unchanged.
- **Times of day** (`StartTime`, `EndTime` in slot windows) are `string` `"HH:mm"`.
- **Money** is `decimal`; quantities are `int`.
- **Optional / partial-update members** are nullable (`string`, `int?`, `decimal?`, `bool?`) with
  `EmitDefaultValue = false`. In an `Update…` request, a member left `null` keeps its stored
  value.
- **Settings maps** (`Dictionary<string,string>`) and other procedure inputs documented as
  `@…Json` are serialized to JSON by the service, not by the caller.
- **Lists** are `List<T>` in the contract; configure the service reference's collection type as
  `System.Collections.Generic.List` so the client sees `List<T>` too. An empty list, never
  `null`, means "none".
- **"Success" results:** operations whose procedure returns only a success flag are `void`; a
  failure is a fault.

### 0.9 Calling from Web Forms

`OpdProject` adds a service reference to `https://{host}/DOCNyleUnifiedAPI.svc?wsdl`
(Connected Services → Add Service Reference, namespace `DocNyleApi`, collection type `List`).
That generates `DocNyleApi.DOCNyleUnifiedAPIClient` and the data contracts; update the
reference whenever `IDOCNyleUnifiedAPI.cs` changes.

```csharp
// OpdProject/Web/DocNyle.cs — one place that creates clients, adds the session header and closes them
public static class DocNyle
{
    public static T Call<T>(Func<DocNyleApi.DOCNyleUnifiedAPIClient, T> call)
    {
        var client = new DocNyleApi.DOCNyleUnifiedAPIClient();      // endpoint from Web.config <client>
        try
        {
            using (new OperationContextScope(client.InnerChannel))
            {
                OperationContext.Current.OutgoingMessageHeaders.Add(
                    MessageHeader.CreateHeader("SessionToken", Ns.V1, AppSession.ServiceToken));
                var result = call(client);
                client.Close();
                return result;
            }
        }
        catch (FaultException) { client.Close(); throw; }   // a fault leaves the channel usable
        catch { client.Abort(); throw; }
    }
}

// in a page:
var patient = DocNyle.Call(api => api.GetPatient(PatientId));
```

Never wrap the client in `using` — `Dispose` throws on a faulted channel and hides the real
error. `BasePage` turns a `FaultException<ServiceFault>` into the page's error toast
(`Detail.Message`), and an `Unauthenticated` fault into the redirect to `login.aspx` it already
does for an expired `AppSession`.

### 0.10 Contract example

```csharp
// DOCNyleUnifiedAPI/IDOCNyleUnifiedAPI.cs
[ServiceContract(Namespace = Ns.V1)]
public interface IDOCNyleUnifiedAPI
{
    #region Patients (§4)

    [OperationContract, FaultContract(typeof(ServiceFault))]
    List<PatientSummary> SearchPatients(string term);

    [OperationContract, FaultContract(typeof(ServiceFault))]
    Patient GetPatient(string patientId);

    [OperationContract, FaultContract(typeof(ServiceFault))]
    Patient CreatePatient(CreatePatientRequest request);
    // …

    #endregion

    #region Billing (§9)
    // …
    #endregion
}
```

```csharp
// DOCNyleUnifiedAPI/DOCNyleUnifiedAPI.svc.cs
[ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple,
                 Namespace = Ns.V1)]
public class DOCNyleUnifiedAPI : IDOCNyleUnifiedAPI
{
    public Patient GetPatient(string patientId)
    {
        return Run(() =>
        {
            var caller = Authorize("patients");
            var row = new DataRepository(caller.HospitalId, caller.UserId).GetPatient(patientId)
                      ?? throw NotFound("Patient not found.");
            Audit(caller, "READ", "Patient", patientId);
            return ToPatient(row);
        });
    }
}
```

```csharp
// DOCNyleUnifiedAPI.DAL/DataRepository.cs
public DataRow GetPatient(string patientId)
{
    using (var conn = Helper.Open(_hospitalId))                       // sets SESSION_CONTEXT
    using (var cmd = Helper.Procedure(conn, "opd.usp_GetPatient"))
    {
        Helper.Add(cmd, "@HospitalId", _hospitalId);
        Helper.Add(cmd, "@PatientId", patientId);
        return Helper.FirstRow(cmd);                                  // null when no row
    }
}
```

Operation names are the procedure name without its schema and `usp_` prefix
(`opd.usp_SearchPatients` → `SearchPatients`), so they keep the 14-verb taxonomy in
`DATA-LAYER-ARCHITECTURE.md` §1, and the matching `DataRepository` method has the same name.

### 0.11 Reading the tables

Each row is one `[OperationContract]` on `IDOCNyleUnifiedAPI`: **Operation** is the C#
signature (request classes are expanded in **Input**), **Returns** gives the data contract and
its members, **Procedure** is the backing stored procedure (and the `DataRepository` method of
the same name), followed by notes.

---

## 1. Auth — sign-in and sessions

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `AuthenticateUser(AuthenticateRequest request)` | `Email, Password` | `AuthenticatedUser` — `UserId, HospitalId, FullName, Email, Phone, Roles, SpecialtyId, LicenseNumber, LabPartnerId, IsActive` — or `null` when the email/password do not match | v3.3: `sec.usp_GetUserCredential` (stored hash `PBKDF2$SHA256${iterations}${salt}${hash}`, `RecentFailures`, `LockedUntil`) → the service verifies the password in C# (a dummy verification for an unknown e-mail, so timing does not reveal it) → `sec.usp_RecordLoginAttempt` → on success `sec.usp_AuthenticateUser` with the stored hash. 5 failures in 15 minutes: `BusinessRule` fault "Too many attempts — try again at {LockedUntil}" without checking the password. `[AnonymousOperation]`. **Audited: LOGIN / LOGIN_FAIL** |
| `CreateUserSession(CreateSessionRequest request)` | `UserId, SessionToken, Browser?, IpAddress?, ExpiresAt` | `UserSession` — `UserSessionId, SessionToken, CreatedAt, ExpiresAt` | `sec.usp_CreateUserSession`. `[AnonymousOperation]`; only accepted for a `UserId` returned by `AuthenticateUser` in the same sign-in |
| `CheckSessionValid()` | — (the `SessionToken` header) | `SessionStatus` — `Valid, UserId, ExpiresAt` | `sec.usp_CheckSessionValid`. Used by `BasePage` to confirm the session on postback |

---

## 2. Users & permissions

Feature: `admin_users` (ADMIN).

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListUsers()` | — | `List<User>` — `UserId, FullName, Email, Phone, Roles, SpecialtyId, LicenseNumber, LabPartnerId, IsActive, CreatedAt` | `sec.usp_ListUsers` |
| `CreateUser(CreateUserRequest request)` | `FullName, Email, Password, Phone?, Roles, SpecialtyId?, LicenseNumber?, LabPartnerId?` | `User` | `sec.usp_CreateUser`. **Audited: CREATE** |
| `UpdateUser(string userId, UpdateUserRequest request)` | `FullName?, Phone?, Roles?, SpecialtyId?, LicenseNumber?` | `User` | `sec.usp_UpdateUser`. **Audited: UPDATE** |
| `UpdateUserActive(string userId)` | — (toggles) | `UserActiveState` — `UserId, IsActive` | `sec.usp_UpdateUserActive`. **Audited: UPDATE** |
| `GetPermissionMatrix()` | — | `List<PermissionGrant>` — `Role, Feature, IsGranted` (the page pivots it to role × feature) | `sec.usp_GetPermissionMatrix` |
| `ListRoles()` | — | `List<string>` | `sec.usp_ListRoles` |
| `ListPermissionFeatures()` | — | `List<string>` | `sec.usp_ListPermissionFeatures` |
| `UpdatePermission(string role, string feature, bool? isGranted)` | `isGranted` `null` toggles the current value | `List<PermissionGrant>` — the role's updated grid | `sec.usp_UpdatePermission`. **Audited: UPDATE** (`EntityType = Permission`) |

---

## 3. Hospital & configuration — profile, settings and configuration lists

Reads: any signed-in user. Writes: `admin_hospital` (ADMIN) unless stated.

### Hospital, time zones, settings

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `GetHospital()` | — | `Hospital` — `HospitalId, Name, Address, Phone, Email, Country, Currency, Timezone, LicenseNumber, PatientIdLabel, NationalHealthIdLabel, IsNationalHealthIdMandatory, DefaultTempUnit, DefaultRxValidityDays, IsActive` | `cfg.usp_GetHospital` |
| `UpdateHospital(UpdateHospitalRequest request)` | `Name?, LegalAddress?, ContactNumber?, PrimaryEmail?, Country?, Currency?, Timezone?, LicenseNumber?, PatientIdLabel?, NationalHealthIdLabel?, NationalHealthIdMandatory?, GrowthChartStandard?, DefaultTempUnit?, DefaultRxValidityDays?, RxPrefix?, ReceiptPrefix?, InvoicePrefix?, LabChargePerTest?, Logo? (byte[]), RemoveLogo?` — `null` keeps the value; `Timezone` must come from `ListTimeZones` | `Hospital` | `cfg.usp_UpdateHospital`. Errors 50070 not found, 50071 unknown time zone, 50072 blank name, 50073 Rx validity < 1. **Audited: UPDATE** |
| `ListTimeZones()` | — | `List<TimeZoneOption>` — `TimeZoneId, WindowsTimeZoneId, DisplayName` | `cfg.usp_ListTimeZones`. The hospital's zone drives its "today", report dates and appointment times |
| `GetHospitalSettings()` | — | `Dictionary<string,string>` — `APPT_SLOT_MINS, APPT_BUFFER_MINS, FREE_FOLLOWUP_ALLOWED, FREE_FOLLOWUP_DAYS, DATE_FORMAT, TIME_FORMAT, MRN_PREFIX, MRN_FORMAT, MRN_SEQ, DOCTOR_NAME_TEMPLATE` | `cfg.usp_GetHospitalSettings` |
| `SaveHospitalSettings(Dictionary<string,string> values)` | sent as `@ValuesJson`; `MRN_SEQ` ignored | settings map | `cfg.usp_SaveHospitalSettings`. Feature `admin_scheduling`. A new `DOCTOR_NAME_TEMPLATE` re-renders every doctor's `FullName` in the procedure |
| `ListPaymentMethods()` | — | `List<PaymentMethod>` — `PaymentMethodId, MethodName, IsActive` | `cfg.usp_ListPaymentMethods` |

### Specialties

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListSpecialties()` | — | `List<Specialty>` — `SpecialtyId, SpecialtyName, IsActive`; active only | `cfg.usp_ListSpecialties` |
| `ListSpecialtyConfig()` | — | `List<Specialty>` incl. inactive (admin view) | `cfg.usp_ListSpecialtyConfig` |
| `CreateSpecialty(string specialtyName)` | — | `Specialty` | `cfg.usp_CreateSpecialty` |
| `ToggleSpecialty(string specialtyId)` | — (toggles) | `Specialty` | `cfg.usp_ToggleSpecialty` |

### Medical history & medical event lists (hospital config)

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListMedicalHistoryItems()` | — | `List<MedicalHistoryItem>` — `MedicalHistoryItemId, ItemName, SortOrder`; active, sorted | `cfg.usp_ListMedicalHistoryItems` |
| `ListMedicalHistoryConfig()` | — | `List<MedicalHistoryItem>` incl. inactive | `cfg.usp_ListMedicalHistoryConfig` |
| `CreateMedicalHistoryConfigItem(string itemName)` | — | `MedicalHistoryItem` | `cfg.usp_CreateMedicalHistoryConfigItem` |
| `UpdateMedicalHistoryItemStatus(string itemName, bool isActive)` | — | `void` | `cfg.usp_UpdateMedicalHistoryItemStatus` |
| `ListMedicalEventItems()` | — | `List<MedicalEventType>` — `MedicalEventTypeId, EventTypeName` | `cfg.usp_ListMedicalEventItems` |

### Vitals configuration

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListVitalsConfig()` | — | `List<VitalDefinition>` — `VitalDefinitionId, VitalCode, DisplayLabel, AllowedUnits, MinValue, MaxValue, SortOrder, IsEnabled`; sorted | `cfg.usp_ListVitalsConfig` |
| `ListEnabledVitalsConfig()` | — | `List<VitalDefinition>`, enabled only | `cfg.usp_ListEnabledVitalsConfig` |
| `UpdateVitalEnabled(string vitalCode, bool isEnabled)` | — | `void` | `cfg.usp_UpdateVitalEnabled` |
| `CreateVitalConfig(CreateVitalConfigRequest request)` | `VitalCode, DisplayLabel, AllowedUnits, MinValue?, MaxValue?, SortOrder?` | `VitalDefinition` | `cfg.usp_CreateVitalConfig` |

### Consultation form designer (v3.2)

Feature: reads any signed-in user (the consultation renders the published form); writes
`admin_consultation`. The designer's editing rules (toggle, move, add sections / fields /
options / units, presets, `ListPublishProblems`, `ListDraftChanges`, restore defaults) stay in
`ConsultationConfigService` and work on the whole `ConsultFormConfig` document; the service stores
that document. `Sections` and `VitalUnits` are the JSON of `List<ConsultFormSection>` and
`List<VitalUnitSetting>`. A hospital with no published row uses the built-in default form.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `GetConsultationFormConfig(string configState)` | `PUBLISHED` (default) or `DRAFT` | `ConsultFormDocument` — `ConfigState, Version, PublishedAt, PublishedBy, Sections, VitalUnits, ModifiedAt`, or `null` | `cfg.usp_GetConsultationFormConfig` |
| `SaveConsultationFormDraft(SaveFormDraftRequest request)` | `Sections, VitalUnits?, ExpectedModifiedAt?` — the draft's `ModifiedAt` as last read (`null` for the first save) | `ConsultFormDocument` (the draft) | `cfg.usp_SaveConsultationFormDraft`. 50240 invalid JSON; 50241 (`Conflict`) someone else saved the draft since it was read |
| `ConfirmConsultationFormPublish()` | — | `FormPublishResult` — `Version, PublishedAt` | `cfg.usp_ConfirmConsultationFormPublish`. Before calling the procedure the service rejects a draft with problems (`Validation`), returns the current version when nothing changed, creates pending custom vitals (`cfg.usp_CreateVitalConfig`) and re-saves the draft without the pending flags; the procedure copies draft → published (version + 1) and sets `cfg.VitalDefinition.IsEnabled` from `VitalUnits`. 50242 no draft. **Audited: UPDATE** (`EntityType = ConsultationForm`) |
| `CancelConsultationFormDraft()` | — | `void` — the draft becomes a copy of the published form (or is removed when nothing is published) | `cfg.usp_CancelConsultationFormDraft` |

### Compliance

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListCompliances(string country)` | — | `List<Compliance>` — `ComplianceId, ComplianceName, Country, IsMandatory, DisplayLabel, PrintLabel, ValidationRegex, SortOrder`; includes `Country = ALL` rows | `cfg.usp_ListCompliances` |
| `ListHospitalCompliances()` | — | `List<HospitalCompliance>` — `HospitalComplianceId, ComplianceId, ComplianceName, DisplayLabel, PrintLabel, IsMandatory, RegistrationNumber, IssuedOn, ExpiresOn` | `cfg.usp_ListHospitalCompliances` |
| `SaveHospitalCompliance(SaveHospitalComplianceRequest request)` | `ComplianceId, RegistrationNumber, IssuedDate?, ExpiryDate?` | `HospitalCompliance` (upserted) | `cfg.usp_SaveHospitalCompliance` |

---

## 4. Patients — patients and their records

Feature: `patients` (ADMIN, DOCTOR, RECEPTIONIST, BILLING, NURSE).

### Patients

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `SearchPatients(string term)` | `term` under 2 characters returns the hospital's list unfiltered | `List<PatientSummary>` — `PatientId, Mrn, FullName, Gender, Dob, Phone, Email, IsActive` | `opd.usp_SearchPatients` |
| `GetPatient(string patientId)` | — | `Patient` — summary members plus `Address, NationalHealthId, InsuranceProvider, InsurancePolicyNo, Photo (byte[]), EmergencyContactName, EmergencyContactPhone` | `opd.usp_GetPatient`. **Audited: READ** |
| `CreatePatient(CreatePatientRequest request)` | `FullName, Gender, Dob, Phone, Email?, Address?, NationalHealthId?, InsuranceProvider?, InsurancePolicyNo?, EmergencyContactName?, EmergencyContactPhone?`; v3.3: `Prefix?, FirstName?, MiddleName?, LastName?, Trn?, CountryCode?, Mobile?, PreferredLanguage?, AddrLine1?, AddrLine2?, City?, Region?, Country?, MaritalStatus?, ReferralSource?, PreferredDoctorId?, BloodGroup?, Occupation?` (`RegisteredVia` is set by the operation: `FRONT_DESK` here) | `Patient` — `PatientId, Mrn, FullName, CreatedAt` (the service reads the rest with `GetPatient` when a page needs it) | `opd.usp_CreatePatient`. v3.3: `Mrn` = `MRN_FORMAT` (`{prefix}`, `{year}`, `{seq:N}`, `{seq}`; default `{prefix}-{seq:5}`) with the next `MRN_SEQ`, allocated under a lock (`cfg.fn_FormatMrn`); a number already in use is skipped. 50205 the format makes MRNs longer than 20 characters, 50206 no free number. **Audited: CREATE** |
| `UpdatePatient(string patientId, UpdatePatientRequest request)` | `FullName?, Phone?, Email?, Address?, NationalHealthId?, InsuranceProvider?, InsurancePolicyNo?, EmergencyContactName?, EmergencyContactPhone?`; v3.3: `Gender?, Dob?` and every structured field of `CreatePatientRequest`. `null` keeps the stored value; `""` clears an optional text field | `Patient` (v3.3: the full record, `GetPatient`'s shape) | `opd.usp_UpdatePatient`. **Audited: UPDATE** |
| `CheckDuplicatePatient(CheckDuplicateRequest request)` *v3.2* | `PatientId?` (the patient being edited), `NationalHealthId?, Trn?, Mobile?` | `PatientDuplicate` — `Field` (`NationalHealthId` / `Trn` / `Mobile`), `Value, PatientId, Mrn, FullName` — or `null` | `opd.usp_CheckDuplicatePatient`. Checked in that order; ids compared without spaces / hyphens; mobile by digits (≥ 7, one may end with the other) |
| `UpdatePatientSocialHistory(string patientId, SocialHistoryRequest request)` *v3.2* | `SmokingStatus?, SmokingPackYears?, AlcoholUse?, Occupation?, PhysicalActivity?, Diet?` — all six replaced, `null` clears | `SocialHistory` | `opd.usp_UpdatePatientSocialHistory`. **Audited: UPDATE** |
| `CreatePatientsBatch(List<ImportPatientRow> rows)` *v3.2* | `ImportPatientRow` = `FullName, Gender, Dob, Phone, Email?, Address?, NationalHealthId?`; sent as `@PatientsJson` | `List<Patient>` — `PatientId, Mrn, FullName, CreatedAt` | `opd.usp_CreatePatientsBatch` — all rows or none; `RegisteredVia = CSV_IMPORT`. 50203 unreadable, 50204 names the first invalid row. Feature `data_import`. **Audited: IMPORT** (one entry, row count) |

### Allergies

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListAllergies(string patientId)` | — | `List<Allergy>` — `PatientAllergyId, PatientId, AllergyType, AllergyName, Reaction, Severity, Status, OnsetDate` | `opd.usp_ListAllergies` |
| `CreateAllergy(string patientId, CreateAllergyRequest request)` | `AllergyType, AllergyName, ReactionType?, ReactionDescription?, Severity, IsTreated?, IsFamilyHistory?, SinceWhen?, Notes?` | `Allergy` (`Status = ACTIVE`) | `opd.usp_CreateAllergy` |
| `UpdateAllergy(string allergyId, UpdateAllergyRequest request)` | `ReactionType?, ReactionDescription?, Severity?, Status?, IsTreated?, SinceWhen?, Notes?` | `Allergy` | `opd.usp_UpdateAllergy` |
| `DeleteAllergy(string allergyId)` | — | `void` | `opd.usp_DeleteAllergy` — soft delete (sets `Status`). **Audited: UPDATE** |

### Medical history

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListPatientMedicalHistory(string patientId)` | — | `List<PatientMedicalHistory>` — `PatientMedicalHistoryId, PatientId, ConditionName, SinceMonth, SinceYear, Notes, CreatedAt` | `opd.usp_ListPatientMedicalHistory` |
| `GetPatientMedicalHistory(string patientId)` | — | same shape, incl. inactive entries | `opd.usp_GetPatientMedicalHistory` |
| `CreatePatientMedicalHistory(string patientId, MedicalHistoryRequest request)` | `ItemId?, ConditionName, OnsetMonth?, OnsetYear?, Status?, Notes?` | `PatientMedicalHistory` | `opd.usp_CreatePatientMedicalHistory` |
| `SavePatientMedicalHistory(string patientId, MedicalHistoryRequest request)` | as above | `PatientMedicalHistory` (upserted by `ConditionName`) | `opd.usp_SavePatientMedicalHistory`. **Audited: UPDATE** |
| `UpdatePatientMedicalHistory(string patientId, string conditionName, UpdateMedicalHistoryRequest request)` | `OnsetMonth?, OnsetYear?, Status?, Notes?` | `void` | `opd.usp_UpdatePatientMedicalHistory` |
| `DeletePatientMedicalHistory(string patientId, string conditionName)` | — | `void` | `opd.usp_DeletePatientMedicalHistory` — soft delete (`IsActive = 0`) |

### Medical events and family history

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListPatientMedicalEvents(string patientId)` | — | `List<PatientMedicalEvent>` — `PatientMedicalEventId, PatientId, EventType, EventDate, Notes, CreatedAt` | `opd.usp_ListPatientMedicalEvents` |
| `CreatePatientMedicalEvent(string patientId, CreateMedicalEventRequest request)` | `EventId?, EventName, EventMonth?, EventYear?, Notes?` | `PatientMedicalEvent` | `opd.usp_CreatePatientMedicalEvent`. **Audited: CREATE** |
| `ListPatientFamilyHistory(string patientId)` | — | `List<FamilyHistory>` — `PatientFamilyHistoryId, PatientId, Relation, Condition, Notes, CreatedAt` | `opd.usp_ListPatientFamilyHistory` |
| `CreatePatientFamilyHistory(string patientId, CreateFamilyHistoryRequest request)` | `RelationshipType, ConditionName, Notes?` | `FamilyHistory` | `opd.usp_CreatePatientFamilyHistory`. **Audited: CREATE** |
| `GetPatientMedicalEvent(string patientMedEventId)` *v3.2* | — | `PatientMedicalEvent` — `PatientMedEventId, PatientId, EventId, EventName, EventMonth, EventYear, Notes, RecordedAt, RecordedByUserId` | `opd.usp_GetPatientMedicalEvent` |
| `UpdatePatientMedicalEvent(string patientMedEventId, UpdateMedicalEventRequest request)` *v3.2* | `EventName, EventMonth?, EventYear?, Notes?` — `EventId` is re-resolved from the name against the hospital's event list | `PatientMedicalEvent` | `opd.usp_UpdatePatientMedicalEvent`. 50200 blank name. **Audited: UPDATE** |
| `DeletePatientMedicalEvent(string patientMedEventId)` *v3.2* | — | `void` | `opd.usp_DeletePatientMedicalEvent` — soft delete. **Audited: DELETE** |
| `GetPatientFamilyHistory(string familyHistoryId)` *v3.2* | — | `FamilyHistory` — `FamilyHistoryId, PatientId, RelationshipType, ConditionName, Notes, RecordedAt` | `opd.usp_GetPatientFamilyHistory` |
| `UpdatePatientFamilyHistory(string familyHistoryId, UpdateFamilyHistoryRequest request)` *v3.2* | `RelationshipType, ConditionName, Notes?` | `FamilyHistory` | `opd.usp_UpdatePatientFamilyHistory`. 50201 blank relative / condition. **Audited: UPDATE** |
| `DeletePatientFamilyHistory(string familyHistoryId)` *v3.2* | — | `void` | `opd.usp_DeletePatientFamilyHistory` — soft delete. **Audited: DELETE** |

### Consent documents (v3.2)

Signed consent forms uploaded on the patient's record. The file is kept in the service's file
store (`{FileStoreRoot}/consent-forms/{HospitalId}/{StoredFileName}`, today
`App_Data/consent-forms`), never in the database; the procedures hold its metadata.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListPatientConsentDocuments(string patientId)` | — | `List<ConsentDocument>` — `ConsentDocumentId, PatientId, FormVersion, FileName, ContentType, SizeBytes, UploadedByUserId, UploadedAt`; newest first | `opd.usp_ListPatientConsentDocuments` |
| `GetPatientConsentDocument(string consentDocumentId)` | — | `ConsentDocument` | `opd.usp_GetPatientConsentDocument` |
| `GetPatientConsentDocumentContent(string consentDocumentId)` | — | `FileContent` — `FileName, ContentType, Content (byte[])` | `opd.usp_GetPatientConsentDocument` + file store. **Audited: READ** |
| `CreatePatientConsentDocument(string patientId, UploadFileRequest request)` | `FileName, Content (byte[], ≤ 50 MB, PDF / image — `LabService.FileTypes`), FormVersion?` | `ConsentDocument` | Service writes the file (`StoredFileName` = new GUID + extension), then `opd.usp_CreatePatientConsentDocument`; deletes the file if the procedure fails. 50202 patient not found. **Audited: CREATE** |

### Coverage (insurance / government scheme)

Feature: `patients` to manage a patient's coverages; `billing` also reaches these.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListPayers(string country, string category)` | `country` = hospital country; `category` `PRIVATE` / `GOVT_SCHEME` or `null` for both | `List<Payer>` — `PayerId, Name, Category, Country` | `cfg.usp_ListPayers` |
| `ListPatientCoverages(string patientId, bool includeInactive)` | — | `List<Coverage>` — `CoverageId, CoverageType, PayerId, PayerName, PlanName, MemberId, PolicyNumber, GroupNumber, PolicyHolderName, Relationship, ClaimMode, GovtCardNumber, CardImageUrl, EffectiveDate, ExpiryDate, Status, Priority, IsActive, LastVerificationStatus`; empty = Self-Pay | `opd.usp_ListPatientCoverages` |
| `SavePatientCoverage(string patientId, SaveCoverageRequest request)` | `CoverageId?` (null = new), `CoverageType` (`PRIVATE_INSURANCE` / `GOVT_SCHEME`), `PayerId, PlanName?, MemberId?, PolicyNumber?, GroupNumber?, PolicyHolderName?, Relationship?, ClaimMode?` (`CASHLESS` / `REIMBURSEMENT`), `GovtCardNumber?, CardImage? (byte[]), EffectiveDate?, ExpiryDate?, Status?` | `Coverage` | `opd.usp_SavePatientCoverage` |
| `DeactivatePatientCoverage(string coverageId)` | — | `void` | `opd.usp_DeactivatePatientCoverage` |

Payer administration (v3.2) — feature `admin_hospital` (Settings → Billing → Payers). `cfg.Payer` is
per **country**, not per hospital: every hospital in the country shares the list.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListAllPayers(string category)` | country = the caller's hospital's; `category` or `null` | `List<Payer>` incl. inactive — `PayerId, Name, Category, Country, IsActive` | `cfg.usp_ListAllPayers` |
| `GetPayer(string payerId)` | — | `Payer` | `cfg.usp_GetPayer` |
| `SavePayer(string name, string category)` | country = the caller's hospital's | `Payer` — new, or a re-enabled inactive payer of that name | `cfg.usp_SavePayer`. 50210 already on the list, 50211 name used under the other category, 50212 blank name / bad category. **Audited: CREATE** |
| `UpdatePayerActive(string payerId, bool? isActive)` | `null` toggles | `Payer` | `cfg.usp_UpdatePayerActive`; only payers of the caller's country. **Audited: UPDATE** |

---

## 5. Scheduling — appointments, availability and schedule settings

Feature: `scheduling` (ADMIN, DOCTOR, RECEPTIONIST, NURSE). Settings operations (marked ⚙)
need `admin_scheduling` (ADMIN + DOCTOR by default) — screen: Settings → Scheduling → Schedule
Settings (tabs Hospital Defaults · Doctors · Appointment Types · Intake & Consent).

`Appointment` members: `AppointmentId, PatientId, PatientName, Mrn, DoctorId, DoctorName, DoctorRoom, SlotStart, SlotEnd` (UTC), `TimeZoneId, SlotStartLocal, SlotEndLocal` (clinic clock), `Type, Status, Notes, CheckedInAt, ConsultStartedAt, CompletedAt`. Each list fills the members its procedure returns.

### Appointments

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListTodayAppointmentsForDoctor(string doctorId)` | — | `List<Appointment>` — the hospital's today (its timezone), by `SlotStart` | `opd.usp_ListTodayAppointmentsForDoctor` |
| `ListAppointmentsForDate(string doctorId, DateTime date)` | `date` = hospital-local date → `@AppointmentDate` | `List<Appointment>` incl. the check-in / consult / completed stamps | `opd.usp_ListAppointmentsForDate` |
| `ListAppointmentsForHospitalDate(DateTime date)` | as above | `List<Appointment>` — every doctor's, with `DoctorId, DoctorName, DoctorRoom`; by `SlotStart`. Front-desk view on Today's Patients | `opd.usp_ListAppointmentsForHospitalDate` |
| `CreateAppointment(CreateAppointmentRequest request)` | `PatientId, DoctorId, SlotStartLocal, SlotEndLocal` (wall-clock `DateTime`, a slot from `ListAvailableSlots`), `Type?, Notes?` | `Appointment` — `AppointmentId, AppointmentDate, SlotStart, SlotEnd, TimeZoneId, SlotStartLocal, SlotEndLocal, Status = BOOKED` | `opd.usp_CreateAppointment`. Stored as UTC + the hospital's `TimeZoneId`. Error 50050: hospital has no timezone. v3.3: re-checks under the per-doctor-day lock that the slot is still one of the doctor's free slots (a slot that already started today is allowed — walk-ins); 50214 taken (`Conflict`), 50220 schedule locked (`Conflict`) |
| `UpdateAppointmentStatus(string appointmentId, string status)` | — | `Appointment` — `AppointmentId, Status, CheckedInAt, ConsultStartedAt, CompletedAt` | `opd.usp_UpdateAppointmentStatus`. Lifecycle `BOOKED → CHECKED_IN` (front desk) `→ IN_CONSULT` (doctor opens the consultation) `→ COMPLETED` (sign-off); `CANCELLED` / `NO_SHOW` only from `BOOKED` or `CHECKED_IN`. A move the current status does not allow (procedure returns no row) is a `Conflict` fault. Stamps `CheckedInAt` (+ `@CheckedInBy`), `ConsultStartedAt`, `CompletedAt` |
| `GetAppointment(string appointmentId)` *v3.2* | — | `Appointment` (all members, incl. the check-in / consult / completed stamps and `CreatedAt`) | `opd.usp_GetAppointment` |
| `ListAppointmentsForPatient(string patientId)` *v3.2* | — | `List<Appointment>` — every status, newest first | `opd.usp_ListAppointmentsForPatient`. Feature `patients` also reaches this |
| `ListAppointmentsForRange(AppointmentRangeQuery query)` *v3.2* | `FromDate, ToDate` (hospital-local, inclusive), `DoctorId?, Kind?` (`consultations` = CHECKED_IN / IN_CONSULT / COMPLETED, `appointments` = not cancelled, `cancelled`), `Status?` | `List<Appointment>` by `SlotStart` | `opd.usp_ListAppointmentsForRange` — Today's Patients and the dashboard drill-down. A doctor-only caller (`AppUser.SeesOwnPatientsOnly`) always gets their own `DoctorId`. Feature `patients` |
| `GetAppointmentDashboard(DateTime? today)` *v3.2* | `today` default = hospital's today | `AppointmentDashboard` — `Counts` (`Kind, TodayCount, WeekCount, MonthCount` for the three kinds; week = Monday–Sunday) and `TodayByStatus` (`Status, Count`) | `opd.usp_GetAppointmentDashboard` (two result sets). Doctor-only callers see their own. Feature `patients` |
| `UpdateAppointmentSlot(string appointmentId, DateTime newSlotStartLocal)` *v3.2* | a `SlotStart` from `ListAvailableSlots` for the same doctor, unchanged | `Appointment` — the **new** appointment; the original becomes `CANCELLED` (as `SchedulingService.Reschedule`) | `opd.usp_UpdateAppointmentSlot` — one transaction, under a per-doctor-day lock. 50213 not BOOKED, 50214 slot not free (`Conflict`). **Audited: UPDATE** |
| `CreateRecurringAppointments(RecurringBookingRequest request)` *v3.2* | `PatientId, DoctorId, StartDate, EveryDays` (1–365), `Count` (1–52), `Type?` | `List<Appointment>` — those booked; a day with no free slot is skipped | `opd.usp_CreateRecurringAppointments` — first free future slot of the type's length each day. 50215 bad range. **Audited: CREATE** (one entry per appointment) |

Every booking operation — `CreateAppointment` (v3.3), `UpdateAppointmentSlot`,
`CreateRecurringAppointments` and `CreatePortalAppointment` — re-checks the slot under one
per-doctor-day application lock (`opd.usp_CheckSlotAvailable`, internal), so two users cannot book
the same time.

### Availability

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListDoctorSlotConfig(string doctorId)` | — | `List<SlotConfig>` — `SlotConfigId, DoctorId, DayOfWeek, StartTime, EndTime`; several windows per weekday allowed, none = closed | `opd.usp_ListDoctorSlotConfig` |
| `SaveDoctorSlotConfig(string doctorId, List<SlotWindow> windows)` ⚙ | `SlotWindow` = `DayOfWeek, StartTime, EndTime` (`HH:mm`, hospital wall clock); replaces all windows; sent as `@WindowsJson` | `List<SlotConfig>` | `opd.usp_SaveDoctorSlotConfig` |
| `ListAvailableSlots(string doctorId, DateTime date, int? durationMinutes)` | `date` hospital-local; `durationMinutes` = the appointment type's | `List<AvailableSlot>` — `SlotStart, SlotEnd` (wall clock, no offset — pass unchanged to `CreateAppointment`), `IsBooked, TimeZoneId` | `opd.usp_ListAvailableSlots`. None on leave days or weekdays without windows; slot = `durationMinutes` or `APPT_SLOT_MINS` (doctor → hospital); step = slot + `APPT_BUFFER_MINS`; `IsBooked` = overlaps a non-cancelled appointment (+ buffer). v3.3: a slot length under 5 or over 720 minutes returns no slots; a negative buffer counts as 0 |
| `GetDoctorScheduleForDate(string doctorId, DateTime date)` | — | `DoctorSchedule` — `DayOfWeek, Windows (List<SlotWindow>), WorkingDays, SlotMinutes, BufferMinutes, OnLeave, LeaveReason` | `opd.usp_GetDoctorScheduleForDate` |
| `ListDoctorLeave(string doctorId)` | — | `List<DoctorLeave>` — `LeaveId, LeaveDate, Reason` | `opd.usp_ListDoctorLeave` |
| `CreateDoctorLeave(string doctorId, DateTime leaveDate, string reason)` ⚙ | — | `DoctorLeave` (the existing row if that date is already leave) | `opd.usp_CreateDoctorLeave` |
| `DeleteDoctorLeave(string leaveId)` ⚙ | — | `void` | `opd.usp_DeleteDoctorLeave` |

### Doctor settings, profiles, charges and fees

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `GetDoctorSettings(string doctorId)` | — | `Dictionary<string, ResolvedSetting>` — `ResolvedSetting` = `Value, Source` (`DOCTOR` / `HOSPITAL` / `NONE`) for `APPT_SLOT_MINS, FREE_FOLLOWUP_ALLOWED, FREE_FOLLOWUP_DAYS, CHARGE_MODEL, CHARGE_UNIT_MINS, CHARGE_UNIT_RATE` | `opd.usp_GetDoctorSettings` |
| `SaveDoctorSettings(string doctorId, Dictionary<string,string> values)` ⚙ | sent as `@ValuesJson`; `""` / `null` removes the override | same as Get | `opd.usp_SaveDoctorSettings` |
| `ListDoctorProfiles()` | — | `List<DoctorProfile>` — `UserId, DisplayName, Prefix, FirstName, MiddleName, LastName, Qualifications, DisplayNameOverride, LicenseNumber, Email, Phone, Room, Languages, Bio, SpecialtyIds, SpecialtyNames` (never a password) | `sec.usp_ListDoctorProfiles` |
| `SaveDoctorProfile(SaveDoctorProfileRequest request)` ⚙ | `DoctorId?` (null = new doctor login), `Email, Password?` (required for a new doctor, ignored otherwise; hashed into `@PasswordHash`), `Prefix?, FirstName?, MiddleName?, LastName?, Qualifications?, DisplayNameOverride?, LicenseNumber?, Phone?, Room?, Languages?, Bio?, SpecialtyIds?` (ordered; first = primary; sent as `@SpecialtyIdsJson`; other hospitals' ids ignored) | `SavedDoctorProfile` — `DoctorId, FullName, Email, Phone, SpecialtyId, LicenseNumber, Prefix, FirstName, MiddleName, LastName, Qualifications, DisplayNameOverride, Room, Languages, Bio, IsActive, Specialties (List<DoctorSpecialty>: SpecialtyId, SpecialtyName, IsPrimary)` | `sec.usp_SaveDoctorProfile`. `FullName` = `DisplayNameOverride`, else the hospital's `DOCTOR_NAME_TEMPLATE`. A new doctor gets 0-amount NEW / FOLLOWUP fee rows from the hospital's today. Errors 50060 email required, 50061 name required, 50062 email in use, 50063 bad `SpecialtyIds`, 50064 new doctor without a password, 50065 doctor not in this hospital. **Audited: CREATE / UPDATE** |
| `GetDoctorCharge(string doctorId)` | — | `DoctorCharge` — `Model` (`FLAT` / `UNIT`), `NewFee, FollowupFee, UnitMinutes, UnitRate` | `opd.usp_GetDoctorSettings` + `bil.DoctorFeeSchedule` |
| `SaveDoctorCharge(string doctorId, DoctorCharge charge)` ⚙ | as returned | `DoctorCharge` | `opd.usp_SaveDoctorSettings` + `bil.DoctorFeeSchedule` (a changed flat fee closes the current fee row and starts a new one today), in one transaction |
| `CheckEffectiveFee(string doctorId, string consultationType, DateTime? onDate)` | `onDate` default = hospital's today | `FeeSchedule` or `null` — `FeeScheduleId, DoctorId, ConsultationType, FeeAmount, Currency, EffectiveFrom, EffectiveTo` | `bil.usp_CheckEffectiveFee` |
| `ListDoctorFees()` | — | `List<FeeSchedule>` | `bil.usp_ListDoctorFees`. Feature `billing` also reaches this |

### Appointment types

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListAppointmentTypes(bool includeInactive)` | — | `List<AppointmentType>` — `AppointmentTypeId, Code, Name, DurationMinutes` (`null` = doctor's slot), `Color, IsChargeable, FeeCategory, SortOrder, IsActive` | `cfg.usp_ListAppointmentTypes` |
| `SaveAppointmentType(SaveAppointmentTypeRequest request)` ⚙ | `AppointmentTypeId?` (null = new), `Name, DurationMinutes?, Color, IsChargeable, FeeCategory` | `AppointmentType` (code generated from the name on create) | `cfg.usp_SaveAppointmentType` |
| `UpdateAppointmentTypeActive(string appointmentTypeId, bool isActive)` ⚙ | — | `AppointmentType` | `cfg.usp_UpdateAppointmentTypeActive` |

### Intake questions, consent and clinical conditions

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListIntakeQuestions()` | — | `List<IntakeQuestion>` — `QuestionId, Label, QuestionType` (`TEXT` / `NUMBER` / `CHOICE`), `Options` | `cfg.usp_ListIntakeQuestions` |
| `CreateIntakeQuestion(string label, string questionType, string options)` ⚙ | — | `IntakeQuestion` | `cfg.usp_CreateIntakeQuestion` |
| `DeleteIntakeQuestion(string questionId)` ⚙ | — | `void` | `cfg.usp_DeleteIntakeQuestion` — soft delete |
| `GetConsentForm()` | — | `ConsentForm` — `ConsentFormId, Title, BodyText, Version` | `cfg.usp_GetConsentForm` |
| `SaveConsentForm(string title, string bodyText)` ⚙ | — | `ConsentForm` (version bumped) | `cfg.usp_SaveConsentForm` |
| `SubmitIntakeResponse(string appointmentId, string patientId, List<IntakeAnswer> answers)` | `IntakeAnswer` = `QuestionId, Answer` | `IntakeResponse` | `opd.usp_SubmitIntakeResponse` |
| `RecordConsent(RecordConsentRequest request)` | `ConsentFormId, PatientId, AppointmentId?, ConsultationId?, SignedByName, Method` | `ConsentRecord` (title/text snapshotted, append-only) | `opd.usp_RecordConsent`. **Audited: CREATE** |
| `GetVisitIntakeAndConsent(string consultationId)` | — | `VisitIntakeAndConsent` — `Appointment, IntakeResponse, ConsentForm, ConsentRecord` | `opd.usp_GetVisitIntakeAndConsent` |
| `ListClinicalConditions()` | — | `List<ClinicalCondition>` — `ConditionId, Name, Icd10Prefixes` | `cfg.usp_ListClinicalConditions` |
| `CreateClinicalCondition(string name, string icd10Prefixes)` ⚙ | — | `ClinicalCondition` | `cfg.usp_CreateClinicalCondition` |
| `DeleteClinicalCondition(string conditionId)` ⚙ | — | `void` | `cfg.usp_DeleteClinicalCondition` — soft delete |

Clinical conditions drive condition-linked phrases (§7 `ListPhrases` / `SearchPhrases`).

### Patient portal (v3.2, anonymous)

Used by `portal/self-register.aspx` and `portal/self-book.aspx`. These are `[AnonymousOperation]`s:
no `SessionToken`. The hospital is **not** taken from the message: the service resolves it from
its own configuration (`appSettings["PortalHospitalId"]`, or a host-name → hospital map when
several hospitals share one deployment). Each is rate-limited per client IP (`ClientIp` header,
e.g. 30 calls / minute), and returns only what the pages show.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `GetPortalBookingOptions()` | — | `PortalOptions` — hospital `Name, PatientIdLabel, Country, TimeZoneId`; `Doctors` (`DoctorId, DisplayName, Specialties, WorkingDays`); `AppointmentTypes` (active); `IntakeQuestions` | Existing procedures: `cfg.usp_GetHospital`, `sec.usp_ListDoctorProfiles` (active doctors only), `cfg.usp_ListAppointmentTypes`, `cfg.usp_ListIntakeQuestions` |
| `ListPortalAvailableSlots(string doctorId, DateTime date, int? durationMinutes)` | as `ListAvailableSlots` | free, future slots only | `opd.usp_ListAvailableSlots` |
| `CreatePortalPatient(CreatePatientRequest request)` | as `CreatePatient` | `PortalPatient` — `PatientId, Mrn, DisplayName` | `opd.usp_CheckDuplicatePatient` first (a match is a `Validation` fault that does not reveal the other patient), then `opd.usp_CreatePatient` with `@RegisteredVia = PATIENT_PORTAL`. **Audited: CREATE** |
| `GetPortalPatient(string mrn, string phone)` | one of them; phone needs ≥ 7 digits | `PortalPatient` — `PatientId, Mrn, DisplayName` ("Ravi K.") or `null` | `opd.usp_GetPortalPatient`. Never returns the full name, phone or date of birth |
| `CreatePortalAppointment(PortalBookingRequest request)` | `PatientId, DoctorId, SlotStartLocal, SlotEndLocal, Type, Answers?` (`IntakeAnswer` list) | `Appointment` — `AppointmentId, SlotStartLocal, SlotEndLocal, DoctorName` | `opd.usp_CreatePortalAppointment` (re-checks the slot under the doctor-day lock; `Notes = Booked online`), then `opd.usp_SubmitIntakeResponse` when there are answers. 50214 slot taken (`Conflict`), 50216 unknown doctor / visit type. **Audited: CREATE** |

**Identity:** as today, an MRN or a phone number alone finds the patient. Asking for a second
factor (date of birth or an SMS code) is a product decision; `opd.usp_GetPortalPatient` is the
place to add it.

---

## 6. Consultation — the consultation workspace

Feature: `consultation` (ADMIN, DOCTOR).

### Consultation

`Consultation` members: `ConsultationId, PatientId, DoctorId, AppointmentId, VisitDate` (UTC), `TimeZoneId, VisitLocalDate, Status, IsLocked`, plus for `GetConsultationById`: `SubjectiveText, ObjectiveText, PlanText, SubjectivePersonal, ObjectivePersonal, PlanPersonal, FollowupDate, FollowupNotes, SkippedSteps`.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `GetOrCreateConsultation(string patientId, string doctorId, string appointmentId)` | `appointmentId` optional | `Consultation` — the existing IN_PROGRESS one, or a new one with `VisitDate` = now (UTC), the hospital's `TimeZoneId` and its `VisitLocalDate` | `cln.usp_GetOrCreateConsultation` |
| `GetConsultationById(string consultationId)` | — | `Consultation` (full) | `cln.usp_GetConsultationById`. **Audited: READ** |
| `ListConsultationsForDate(DateTime visitDate)` | hospital-local date, matched on `VisitLocalDate` | `List<Consultation>` — `ConsultationId, PatientId, DoctorId, AppointmentId, VisitDate, Status` | `cln.usp_ListConsultationsForDate` |
| `SaveSoapNotes(string consultationId, SoapNotes notes)` | `SubjectiveText?, ObjectiveText?, PlanText?, SubjectivePersonal?, ObjectivePersonal?, PlanPersonal?, SkippedSteps?` | `void` (no change once `IsLocked`) | `cln.usp_SaveSoapNotes`. `SkippedSteps` (JSON array) also carries `ConsultationService.SkipStep`: the facade reads the list from `GetConsultationById`, adds the step and saves it |
| `SaveFollowup(string consultationId, DateTime? followUpDate, string followUpNotes)` | — | `void` | `cln.usp_SaveFollowup` |
| `ConfirmSignOff(string consultationId)` | — (doctor = caller; the service computes `@SignatureHash` over the signed content) | `Attestation` — `AttestationId, SignedAt` | `cln.usp_ConfirmSignOff`. In one transaction: deducts the stocked items listed under Services & Procedures (earliest-expiry batch first, `inv.usp_RecordStockConsumption`), locks the consultation (`Status = LOCKED`) and completes its appointment (`IN_CONSULT → COMPLETED`). Error 50040 ("Not enough stock: …") rolls everything back — call `ListStockShortages` first. **Audited: SIGNOFF** |
| `ListPatientPastVisits(string patientId)` | — | `List<PastVisit>` — `ConsultationId, VisitDate, DoctorName, ChiefComplaint, PrimaryDiagnosis`; LOCKED only, newest first | `opd.usp_ListPatientPastVisits` |
| `GetInProgressConsultation(string patientId, string doctorId)` *v3.2* | — | `Consultation` or `null` — the doctor's IN_PROGRESS consultation, without creating one (`ConsultationService.FindInProgress`) | `cln.usp_GetInProgressConsultation` |
| `SaveConsultationCustomValues(string consultationId, string sectionKey, List<CustomFieldValue> values)` *v3.2* | `CustomFieldValue` = `FieldKey, Value` — an empty `Value` removes the field; the service adds `SectionLabel, FieldLabel, InputType, DisplayValue` from the published form | `void` | `cln.usp_SaveConsultationCustomValues` (`@ValuesJson`). 50245 consultation not found or signed, 50246 unreadable |
| `ListConsultationCustomValues(string consultationId)` *v3.2* | — | `List<CustomFieldValue>` — `SectionKey, FieldKey, Value, SectionLabel, FieldLabel, InputType, DisplayValue, UpdatedAt, UpdatedBy`, in the published form's section / field order (sorted by the service) | `cln.usp_ListConsultationCustomValues` |

### Vitals

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `GetConsultationVitals(string consultationId)` | — | `Vitals` — `ConsultationVitalId, TempValue, BpSystolic, BpDiastolic, PulseBpm, SpO2, RespRate, HeightCm, WeightKg, Bmi, PainScale, WaistCircCm, HeadCircCm` | `cln.usp_GetConsultationVitals` |
| `SaveVitals(string consultationId, SaveVitalsRequest request)` | `TempValue?, BpSystolic?, BpDiastolic?, PulseBpm?, SpO2?, RespRate?, HeightCm?, WeightKg?, PainScale?, WaistCircCm?, HeadCircCm?, CustomValues? (Dictionary<string,string> → @CustomValuesJson)` — values in base units | `Vitals` (upserted; `Bmi` computed from height + weight) | `cln.usp_SaveVitals`. Also reachable with `patients` (NURSE, from Nurse Vitals) |

Vitals configuration is in the Hospital & configuration region (§3).

### Symptoms

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListSymptomCatalog()` | — | `List<Symptom>` — `SymptomCatalogId, SymptomName, Category` | `cfg.usp_ListSymptomCatalog` |
| `ListSymptomQuickPicks()` | — | `List<Symptom>`, `Category = QUICK_PICK` | `cfg.usp_ListSymptomQuickPicks` |
| `ListSymptomOthers()` | — | `List<Symptom>`, `Category = OTHER` | `cfg.usp_ListSymptomOthers` |
| `CreateCustomSymptom(string displayLabel)` | — | `Symptom` (or the existing row with that name) | `cfg.usp_CreateCustomSymptom` |
| `ListConsultationSymptoms(string consultationId)` | — | `List<ConsultationSymptom>` — `ConsultationSymptomId, ConsultationId, SymptomName, State` | `cln.usp_ListConsultationSymptoms` |
| `UpdateSymptomState(string consultationId, string symptomName, string state)` | `state` `YES` / `NO` / `NOT_ASKED` | `void` (upsert) | `cln.usp_UpdateSymptomState` |

### Diagnoses

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `SearchIcd10Catalog(string term)` | — | `List<Icd10>` — `Icd10CatalogId, Code, Description, Synonyms`; code-prefix / description / synonym match | `cfg.usp_SearchIcd10Catalog` |
| `ListConsultationDiagnoses(string consultationId)` | — | `List<Diagnosis>` — `ConsultationDiagnosisId, Icd10Code, Description, IsPrimary, Laterality, Chronicity, SequenceOrder`; sorted | `cln.usp_ListConsultationDiagnoses` |
| `CreateDiagnosis(string consultationId, CreateDiagnosisRequest request)` | `Icd10Code, Icd10Description, ClinicalNotes?, Laterality?, AcuteChronic?` | `Diagnosis` (the first on a consultation is primary) | `cln.usp_CreateDiagnosis` |
| `DeleteDiagnosis(string diagnosisId)` | — | `void` | `cln.usp_DeleteDiagnosis` — soft delete |

### Prescriptions

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListPrescriptions(string consultationId)` | — | `List<Prescription>`, `Status = ACTIVE` | `cln.usp_ListPrescriptions` |
| `ListActiveMedicationsForPatient(string patientId)` | — | `List<Prescription>`, ACTIVE across all visits | `cln.usp_ListActiveMedicationsForPatient` |
| `ListPreviousPrescriptionsByDoctor(string patientId, string doctorId)` | — | `PreviousPrescriptions` — `Found, Consultation` (most recent LOCKED visit with this doctor, or `null`), `Prescriptions` (its ACTIVE ones) | `opd.usp_ListPreviousPrescriptionsByDoctor` |
| `CreatePrescription(string consultationId, CreatePrescriptionRequest request)` | `PatientId, Source?, SourceDetail?, MedicineName, Strength?, GenericClass?, TherapeuticClass?, DrugForm?, DeliveryRoute?, Category?, TakeInQty, TimesPerDay?, DurationValue?, DurationUnit?, Instructions?, Prn?, TimingBf?, TimingAn?, TimingNi?, TimingEm?, ValidityDays?, RepeatedFromConsultationId?` | `Prescription` | `cln.usp_CreatePrescription`. **Audited: CREATE** |
| `CancelPrescription(string prescriptionId)` | — | `void` (`Status = DISCONTINUED`) | `cln.usp_CancelPrescription` |
| `CheckDrugAllergyConflicts(string patientId, string medicineName, string composition)` | `composition` optional | `List<DrugAllergyAlert>` — `Severity, AllergyName, AllergySeverity, Message` | `cln.usp_CheckDrugAllergyConflicts`. When the doctor overrides an alert, the page calls `RecordAuditEntry` (Action `OVERRIDE`) |
| `ListPatientMedicationHistory(string patientId)` *v3.2* | — | `List<Prescription>` — every prescription and outside medicine, all statuses, newest first by period start; with `Source, SourceDetail, DoctorId, DoctorName` | `cln.usp_ListPatientMedicationHistory`. Feature `patients` also reaches this (Health Record → Medications) |
| `SaveOutsideMedication(string patientId, OutsideMedicationRequest request)` *v3.2* | `PrescriptionId?` (null = new), `MedicineName, Strength?, DrugForm?, TakeInQty?, TakeInUnit?, TimesPerDay?, TimingNi?, Prn?, FrequencyMode?, DynamicInstruction?, Instructions?, PeriodStart?, SourceDetail?` (who prescribed it) | `Prescription` (`Source = OUTSIDE`, no consultation; a new one copies generic / therapeutic class from the formulary) | `cln.usp_SaveOutsideMedication`. 50217 blank name. Feature `patients` also reaches this. **Audited: CREATE / UPDATE** |

### Investigation orders and referrals

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `CreateInvestigationOrder(string consultationId, CreateOrderRequest request)` | `PatientId, TestId?, TestName, TestType?` | `InvestigationOrder` (`Status = ORDERED`) | `cln.usp_CreateInvestigationOrder` |
| `ListInvestigationOrders(string consultationId)` | — | `List<InvestigationOrder>` | `cln.usp_ListInvestigationOrders` |
| `DeleteInvestigationOrder(string orderId)` *v3.2* | — | `void` | `cln.usp_DeleteInvestigationOrder` — soft delete, only while `ORDERED` and the consultation is not signed (50218) |
| `CreateReferral(string consultationId, CreateReferralRequest request)` | `PatientId, Specialty, DoctorName?, Urgency?, Reason?` | `Referral` | `cln.usp_CreateReferral` |
| `ListReferrals(string consultationId)` | — | `List<Referral>` | `cln.usp_ListReferrals` |

### Sketches, confidential notes and addendums

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `SaveSketch(string consultationId, SaveSketchRequest request)` | `PatientId, Title?, ImageData (byte[], PNG)` | `Sketch` (always a new row) | `cln.usp_SaveSketch` |
| `ListSketches(string consultationId)` | — | `List<Sketch>`, newest first | `cln.usp_ListSketches` |
| `ListPatientSketches(string patientId)` | — | `List<Sketch>` across all visits, newest first | `cln.usp_ListPatientSketches` |
| `GetConfidentialNote(string consultationId)` | — (author = caller) | `ConfidentialNote` or `null` | `cln.usp_GetConfidentialNote`. Only ever returned to its author |
| `SaveConfidentialNote(string consultationId, string noteText)` | — | `ConfidentialNote` (upserted, author-scoped) | `cln.usp_SaveConfidentialNote` |
| `CreateAddendum(string consultationId, string category, string addendumText)` | `category` optional | `Addendum` (append-only) | `cln.usp_CreateAddendum` |
| `ListAddendums(string consultationId)` | — | `List<Addendum>`, chronological | `cln.usp_ListAddendums` |

### Services & procedures (with optional stocked item)

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListConsultationServiceItems(string consultationId)` | — | `List<ServiceItem>` — `ConsultationServiceId, ConsultationId, PatientId, ServiceChargeId, ServiceName, Category, Qty, InventoryItemId, ItemName, ItemUnit, ItemQty, Notes, AddedByUserId, CreatedAt` | `cln.usp_ListConsultationServiceItems` |
| `CreateConsultationServiceItem(string consultationId, CreateServiceItemRequest request)` | `ServiceChargeId, Qty?, InventoryItemId?, ItemQty?, Notes?` — the item must be linked to the service (`ListItemsForService`); `ItemQty` may not exceed what is available less what this visit already lists | `ServiceItem` — `ConsultationServiceId, ServiceName, Qty, ItemName, ItemQty` | `cln.usp_CreateConsultationServiceItem`. Stock is checked here and deducted at sign-off; the service and the item are billed on separate invoice lines. Errors 50030 consultation not found or signed off, 50031 not enough stock, 50032 service not found, 50033 item not used with this service |
| `DeleteConsultationServiceItem(string consultationId, string consultationServiceId)` | — | `void` (before sign-off only) | `cln.usp_DeleteConsultationServiceItem` — hard delete |
| `ListStockShortages(string consultationId)` | — | `List<StockShortage>` — `InventoryItemId, ItemName, Unit, Needed, Available`; empty = sign-off can proceed | `inv.usp_ListStockShortages` |

---

## 7. Catalog — formulary, lab tests, phrases

Feature: `consultation`. Formulary changes (`CreateDrug`, `UpdateDrugStatus`) need
`admin_formulary`; `CreateLabTest` needs `admin_hospital`; `CreatePhrase` is reachable with
`consultation` (a doctor's own phrase, from the SOAP card) or `admin_phrases` (any scope), and
`DeletePhrase` needs `admin_phrases` unless the caller wrote the phrase.

### Drug formulary

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `SearchFormulary(string term)` | — | `List<Drug>` — `DrugFormularyId, BrandName, GenericName, TherapeuticClass, GenericClass, Composition, Strength, DrugForm, DeliveryRoute, DefaultDosage, DefaultFrequency, DefaultDuration, DefaultInstructions, IsControlled, IsActive` | `cfg.usp_SearchFormulary` |
| `GetDrugByName(string name)` | — | `Drug` or `null` | `cfg.usp_GetDrugByName` |
| `CreateDrug(CreateDrugRequest request)` | `BrandName, GenericName?, Composition?, TherapeuticClass?, GenericClass?, DrugForm?, DefaultRoute?, DefaultDosage?, DefaultFrequency?, DefaultDurationDays?, DefaultInstructions?, IsControlledSubstance?` | `Drug` | `cfg.usp_CreateDrug` |
| `UpdateDrugStatus(string drugFormularyId)` | — (toggles) | `Drug` | `cfg.usp_UpdateDrugStatus` |
| `UpdateDrug(string drugId, UpdateDrugRequest request)` *v3.2* | `BrandName, GenericName?, Composition?, TherapeuticClass?, GenericClass?, DrugForm?, DefaultDosage?, DefaultFrequency?, DefaultDurationDays?, DefaultInstructions?, IsControlledSubstance` — all replaced; `DefaultRoute` kept | `Drug` | `cfg.usp_UpdateDrug`. 50219 blank brand. Feature `admin_formulary`. **Audited: UPDATE** |

### Lab tests and favorites

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `SearchLabTests(string term, string specialtyId)` | `specialtyId` optional | `List<LabTest>` — `LabTestCatalogId, TestName, TestType, DefaultSampleType` | `cfg.usp_SearchLabTests` |
| `CreateLabTest(CreateLabTestRequest request)` | `TestName, TestType?, Category?, SpecialtyIds?` | `LabTest` | `cfg.usp_CreateLabTest` |
| `ListDoctorLabFavorites(string doctorId, string specialtyId)` | `specialtyId` optional | `List<LabTest>` | `cln.usp_ListDoctorLabFavorites` |
| `CreateLabFavorite(string doctorId, string testId)` | — | `LabFavorite` (created or existing) | `cln.usp_CreateLabFavorite` |
| `DeleteLabFavorite(string doctorId, string testId)` | — | `void` | `cln.usp_DeleteLabFavorite` — hard delete (junction row) |

### Phrases (SOAP quick-insert)

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListPhrases(PhraseQuery query)` | `Section` (S/O/A/P), `SpecialtyId?, DoctorId?, ConsultationId?` | `List<Phrase>` — `PhraseBankId, UserId, Category, TriggerCode, PhraseText`; priority DOCTOR > SPECIALTY > GLOBAL | `cfg.usp_ListPhrases`. With `ConsultationId`, condition-linked phrases appear only when one of the visit's ICD-10 diagnoses matches the condition; a doctor's phrase hides a broader one with the same trigger |
| `SearchPhrases(PhraseQuery query, string term)` | as above + `term` | `List<Phrase>`, top 5, trigger / text match | `cfg.usp_SearchPhrases` |
| `CreatePhrase(CreatePhraseRequest request)` | `Scope?, SpecialtyId?, DoctorId?, ConditionId?, Section?, TriggerKeyword?, PhraseText` | `Phrase` | `cfg.usp_CreatePhrase` |
| `DeletePhrase(string phraseBankId)` | — | `void` | `cfg.usp_DeletePhrase` — soft delete (`IsActive = 0`) |

---

## 8. Lab — lab portal and results

Feature: `lab_portal` (LAB_PARTNER, ADMIN) for the portal operations; `consultation` for
results review (marked 🩺).

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListPendingLabOrders()` | — | `List<InvestigationOrder>` incl. `PatientName, PatientMrn`; `Status = ORDERED` | `cln.usp_ListPendingLabOrders` |
| `UpdateOrderStatus(string orderId, string status)` | — | `void` | `cln.usp_UpdateOrderStatus` |
| `SubmitLabResult(SubmitLabResultRequest request)` | `OrderId, LabPartnerId?, ResultSummary?, ResultValue?, ReferenceRange?, Interpretation?, Technician?, Notes?` | `LabResult` (`Status = UPLOADED`; the order becomes `COMPLETED`) | `cln.usp_SubmitLabResult`. **Audited: SUBMIT** |
| `ListLabResultsForPatient(string patientId)` 🩺 | — | `List<LabResult>`, newest first | `cln.usp_ListLabResultsForPatient` |
| `ListPendingReviewResults()` 🩺 | — | `List<LabResult>` incl. `PatientName, PatientMrn`; `Status = UPLOADED` | `cln.usp_ListPendingReviewResults` |
| `UpdateLabResultStatus(string labResultId, string reviewNotes)` 🩺 | `reviewNotes` optional | `void` (`Status = REVIEWED`) | `cln.usp_UpdateLabResultStatus`. **Audited: APPROVE** |

`SubmitLabResult` also notifies the ordering doctor in-app (`sec.usp_CreateUserNotification`, *v3.2*,
replacing the page's `UserNotificationService.Notify` call), and `UpdateLabResultStatus` clears
everyone's notifications about that result (`sec.usp_UpdateUserNotificationReadForEntity`). Both
procedures are internal to those operations.

Lab partners, claim by mobile number and attachments (v3.2):

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListLabPartners()` | — | `List<LabPartner>` — `LabPartnerId, Name, IsActive`; active only | `cfg.usp_ListLabPartners`. Feature `admin_users` (linking a lab login to its lab) |
| `GetLabPartner(string labPartnerId)` | — | `LabPartner` | `cfg.usp_GetLabPartner` |
| `SaveLabPartner(string name)` | — | `LabPartner` — the existing one of that name (re-enabled) or a new one | `cfg.usp_SaveLabPartner`. 50232 blank name. Feature `admin_users` |
| `SearchLabOrdersByMobile(string mobile)` | the patient's full mobile number (≥ 10 digits) | `List<InvestigationOrder>` — open orders (ORDERED / SENT) not yet taken by a lab, for every patient of the hospital with that number; with `PatientName, PatientMrn` | `cln.usp_SearchLabOrdersByMobile` — lab partner callers only, with their own `LabPartnerId`. Every lookup is logged in `sec.LabClaimAttempt`; 5 lookups with no match in 15 minutes lock the user out (50231). 50230 not a full number |
| `ConfirmLabOrderClaim(string orderId, string mobile)` | — | `InvestigationOrder` — `LabPartnerId` = the caller's lab, `Status = SENT` — or `null` when no longer claimable | `cln.usp_ConfirmLabOrderClaim`; the first of two competing claims wins. **Audited: UPDATE** |
| `CreateLabResultAttachment(string resultId, UploadFileRequest request)` | `FileName, Content (byte[], ≤ 50 MB, `LabService.FileTypes`)` | `LabAttachment` — `AttachmentId, ResultId, OrderId, LabPartnerId, FileName, ContentType, SizeBytes, UploadedByUserId, UploadedAt` | Service writes the file (`{FileStoreRoot}/lab-reports/{HospitalId}/…`), then `cln.usp_CreateLabResultAttachment` (order and lab taken from the result). 50235 result not found. **Audited: CREATE** |
| `ListLabResultAttachments(string resultId, string orderId)` | one of them | `List<LabAttachment>` | `cln.usp_ListLabResultAttachments`. 50236 neither given. Lab partners see only their own lab's files |
| `GetLabResultAttachment(string attachmentId)` | — | `LabAttachment` | `cln.usp_GetLabResultAttachment` |
| `GetLabResultAttachmentContent(string attachmentId)` | — | `FileContent` — `FileName, ContentType, Content (byte[])` | `cln.usp_GetLabResultAttachment` + file store; `LabService.CanViewAttachment` rules (a lab partner only its own lab's; others need `consultation` or `lab_portal`). **Audited: READ** |

---

## 9. Billing — charges, invoices, payments, coverage

Feature: `billing` (ADMIN, RECEPTIONIST, BILLING).

### Consultation charge and service charge master

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `GetConsultationCharge(string consultationId)` | — | `ConsultationCharge` — `ConsultationType, AppointmentTypeName, DurationMinutes, ChargeModel, BaseFee, FeeAmount, IsFreeFollowup, AnchorDate, Basis, Note, Description` | `bil.usp_GetConsultationCharge`. Used when the invoice is generated |
| `ListServiceCharges()` | — | `List<ServiceCharge>` — `ServiceChargeId, ServiceCode, ServiceName, Category, DefaultAmount, TaxPercent, Currency` | `bil.usp_ListServiceCharges` |
| `GetServiceCharge(string serviceChargeId)` | — | `ServiceCharge` | `bil.usp_GetServiceCharge` |
| `SearchServiceCharges(string term)` | — | `List<ServiceCharge>` | `bil.usp_SearchServiceCharges` |
| `CreateServiceCharge(CreateServiceChargeRequest request)` | `ServiceCode, ServiceName, Category, DefaultAmount, TaxPercent` | `ServiceCharge` | `bil.usp_CreateServiceCharge` |
| `ListAllServiceCharges()` *v3.2* | — | `List<ServiceCharge>` incl. inactive, with `IsActive`; by category, name | `bil.usp_ListAllServiceCharges` (Settings → Services) |
| `UpdateServiceChargeActive(string serviceChargeId, bool? isActive)` *v3.2* | `null` toggles | `ServiceCharge` — `ServiceChargeId, ServiceName, IsActive` | `bil.usp_UpdateServiceChargeActive`. **Audited: UPDATE** |

### Invoices and line items

`Invoice` members: `InvoiceId, InvoiceNumber, ConsultationId, PatientId, DoctorId, ConsultationType, IsFreeFollowup, ChargeNote, Currency, SubTotal, DiscountAmount, TaxAmount, TotalAmount, PaidAmount, PatientPaidAmount, Status, CreatedAt`.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `CreateInvoice(CreateInvoiceRequest request)` | `ConsultationId?, PatientId, DoctorId?, ConsultationType?, IsFreeFollowup?, ChargeNote?, Currency?` | `Invoice` (`Status = PENDING`, server-generated `InvoiceNumber`) | `bil.usp_CreateInvoice` |
| `GetInvoiceForConsultation(string consultationId)` | — | `Invoice` or `null`; ignores CANCELLED | `bil.usp_GetInvoiceForConsultation` |
| `ListInvoices()` | — | `List<Invoice>`, newest first | `bil.usp_ListInvoices` |
| `GetInvoice(string invoiceId)` | — | `Invoice` | `bil.usp_GetInvoice` |
| `UpdateInvoice(string invoiceId, UpdateInvoiceRequest request)` | `Status?, SubTotal?, DiscountAmount?, TaxAmount?, TotalAmount?` | `Invoice` | `bil.usp_UpdateInvoice` |
| `CancelInvoice(string invoiceId, string reason)` | — | `CreditNote` (for the full amount; invoice `Status = CANCELLED`) | `bil.usp_CancelInvoice`. **Audited: CANCEL** |
| `ListCreditNotes(string invoiceId)` | — | `List<CreditNote>` | `bil.usp_ListCreditNotes` |
| `CreateInvoiceLineItem(string invoiceId, CreateLineItemRequest request)` | `ServiceChargeId?, InventoryItemId?` (a stocked item from Clinic Inventory), `Description, Category, Qty?, UnitAmount, DiscountPercent?, TaxPercent?` | `LineItem` (server computes `DiscountAmount, TaxAmount, LineTotal`) | `bil.usp_CreateInvoiceLineItem` |
| `ListInvoiceLineItems(string invoiceId)` | — | `List<LineItem>`, by `SortOrder` | `bil.usp_ListInvoiceLineItems` |
| `DeleteInvoiceLineItem(string invoiceId, string lineItemId)` | — | `void` | `bil.usp_DeleteInvoiceLineItem` — hard delete |
| `RecalculateInvoiceTotals(string invoiceId)` | — | `Invoice` (totals recomputed from line items) | `bil.usp_RecalculateInvoiceTotals` |

### Payments and receipts

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `CreatePayment(string invoiceId, CreatePaymentRequest request)` | `ComponentType?` (`PATIENT` / `INSURANCE`), `CoverageLineId?` (required for INSURANCE — the claim being settled), `PaymentMethod, Amount, Reference?` | `Payment` (recalculates `PaidAmount`, `PatientPaidAmount`, coverage `SettledAmount`, `Status`) | `bil.usp_CreatePayment`. Errors 50120 INSURANCE without a coverage line on this invoice, 50121 invoice cancelled (`Conflict`) |
| `ListPaymentsForInvoice(string invoiceId)` | — | `List<Payment>`, newest first | `bil.usp_ListPaymentsForInvoice` |
| `ListPayments()` | — | `List<Payment>`, newest first | `bil.usp_ListPayments` |
| `UpdatePaymentVoid(string paymentId, string voidReason)` | — | `void` — payments are voided, never deleted; recalculates the invoice | `bil.usp_UpdatePaymentVoid`. **Audited: VOID** |
| `CreateReceipt(string invoiceId, CreateReceiptRequest request)` | `PatientId?, TotalAmount, Currency?` | `Receipt` (server-generated `ReceiptNumber`) | `bil.usp_CreateReceipt`. When the receipt is printed the page calls `RecordAuditEntry` (Action `PRINT`) |
| `GetReceiptForInvoice(string invoiceId)` | — | `Receipt` or `null` | `bil.usp_GetReceiptForInvoice` |

### Coverage and bill split

`Charge (invoice TotalAmount) = Σ verified coverage liabilities + patient share`. Supports
Self-Pay, private insurance only, government scheme only (CGHS / Aarogyasri, NHF, Medicare …),
and private + government together. Each coverage is verified per invoice (eligibility status +
authorization + approved % or fixed amount); liabilities are allocated in coverage `Priority`
order and capped so their sum never exceeds the charge. `PERCENT` approvals follow the invoice
total when charges change. The invoice is `PAID` once the patient share is settled (or
everything is paid); covered amounts are receivables from the payer, settled later with
`INSURANCE` payments against the coverage line. Screen: Billing Checkout → *Coverage & bill
split*.

`BillingSplit` members: `GrossAmount, InsuranceLiability, GovtLiability, CoveredAmount, PatientLiability, PatientPaid, PatientDue, PatientOverpaid, PayerOutstanding, Status, Lines (List<CoverageLine>: CoverageLineId, CoverageId, PayerName, CoverageType, Priority, AuthorizationNumber, ApprovedType, ApprovedValue, IsManualAmount, Liability, SettledAmount)`.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListEligibilityVerifications(string coverageId)` | — | `List<EligibilityVerification>` — `VerificationId, InvoiceId, Status, AuthorizationNumber, ApprovedType, ApprovedValue, Notes, VerifiedBy, VerifiedAt`; oldest first | `bil.usp_ListEligibilityVerifications` |
| `ApplyCoverageToInvoice(string invoiceId, ApplyCoverageRequest request)` | `CoverageId, Status` (`VERIFIED` / `PENDING` / `REJECTED`), `AuthorizationNumber?, ApprovedType` (`PERCENT` / `AMOUNT`), `ApprovedValue, Notes?` | `BillingSplit` — records the verification; `VERIFIED` applies / updates the coverage line, otherwise removes it | `bil.usp_ApplyCoverageToInvoice`. `Conflict` (50121 / 50122) if the invoice is cancelled or the payer already settled the line. **Audited: UPDATE** (`EntityType = Invoice`) |
| `EditInvoiceCoverageLine(string coverageLineId, decimal liability)` | — | `BillingSplit` — manual override, still capped to the charge | `bil.usp_EditInvoiceCoverageLine` |
| `RemoveInvoiceCoverageLine(string coverageLineId)` | — | `BillingSplit` | `bil.usp_RemoveInvoiceCoverageLine`. `Conflict` once the payer has settled |
| `GetInvoiceBillingSplit(string invoiceId)` | — | `BillingSplit` | `bil.usp_GetInvoiceBillingSplit` |

`bil.usp_RecalculateInvoiceSplit` is internal (not an operation): it re-derives liabilities,
patient share, paid amounts and status, and runs inside line-item recalculation, payments, voids
and every coverage change.

---

## 10. Inventory — clinic inventory

Feature: `admin_inventory` (ADMIN by default); `ListItemsForService` is also reachable with
`consultation`.

Stock the clinic uses during a visit, kept apart from the drug formulary. Stock is held per
batch (batch number + expiry); a batch is usable through its expiry date (the hospital's
calendar date) and used earliest-expiry first. Every change is a row in the stock ledger.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListInventoryItems(bool includeInactive)` | — | `List<InventoryItemSummary>` — `InventoryItemId, ItemCode, ItemName, Category, Unit, UnitPrice, TaxPercent, ReorderLevel, IsActive, Available, Expired, NextExpiry`; `Available` counts unexpired batches only | `inv.usp_ListInventoryItems` |
| `GetInventoryItem(string inventoryItemId)` | — | `InventoryItem` — `InventoryItemId, ItemCode, ItemName, Category, Unit, UnitPrice, TaxPercent, ReorderLevel, IsActive` | `inv.usp_GetInventoryItem` |
| `SaveInventoryItem(SaveInventoryItemRequest request)` | `InventoryItemId?` (null = new), `ItemCode, ItemName, Category` (`MEDICATION` / `INJECTION` / `CONSUMABLE` / `OTHER`), `Unit, UnitPrice, TaxPercent?, ReorderLevel?`; `ItemCode` unique per hospital | `SavedInventoryItem` — `InventoryItemId, ItemName` | `inv.usp_SaveInventoryItem` |
| `UpdateInventoryItemActive(string inventoryItemId, bool isActive)` | — | `InventoryItemActiveState` — `InventoryItemId, IsActive` | `inv.usp_UpdateInventoryItemActive` |
| `ListItemServices(string inventoryItemId)` | — | `List<LinkedService>` — `ServiceChargeId, ServiceName` | `inv.usp_ListItemServices` |
| `SaveItemServices(string inventoryItemId, List<string> serviceChargeIds)` | replaces the list (sent comma-separated as `@ServiceChargeIds`); empty = linked to nothing | `List<LinkedService>` | `inv.usp_SaveItemServices` |
| `ListItemsForService(string serviceChargeId)` | — | `List<ServiceStockItem>` — `InventoryItemId, ItemName, Category, Unit, Available`; active items linked to the service (the Item field in Services & Procedures) | `inv.usp_ListItemsForService` |
| `ListStockBatches(string inventoryItemId, bool includeEmpty)` | — | `List<StockBatch>` — `StockBatchId, InventoryItemId, BatchNo, ExpiryDate, QtyReceived, QtyOnHand, UnitCost, Supplier, ReceivedByUserId, ReceivedAt, IsExpired`; earliest expiry first | `inv.usp_ListStockBatches` |
| `RecordStockReceipt(string inventoryItemId, StockReceiptRequest request)` | `BatchNo, ExpiryDate, Qty, UnitCost?, Supplier?` — a batch number already on file is topped up (same expiry only) | `List<StockBatch>` (the item's batches) | `inv.usp_RecordStockReceipt`. Errors 50020 qty < 1, 50021 item not found, 50022 batch on file with another expiry |
| `RecordStockAdjustment(string stockBatchId, int delta, string reason)` | signed `delta` (negative for expiry write-off, damage, count short) | `void` | `inv.usp_RecordStockAdjustment`. Errors 50023 delta 0, 50024 would go below zero |
| `ListStockMovements(string inventoryItemId, int? take)` | `take` default 50 | `List<StockMovement>` — `StockMovementId, MovementType` (`RECEIPT` / `CONSUMPTION` / `ADJUSTMENT`), `Qty` (signed), `Reason, ConsultationId, UserId, CreatedAt, StockBatchId, BatchNo`; newest first | `inv.usp_ListStockMovements` |

`inv.usp_RecordStockConsumption` is internal: `ConfirmSignOff` (§6) calls it to deduct a
consultation's stocked items earliest-expiry first, all or nothing (error 50040 lists the
shortages; a second call is a no-op).

---

## 11. Reports

Feature: `reports` (ADMIN, DOCTOR, BILLING). `fromDate` / `toDate` / `date` are hospital-local
calendar dates.

| Operation | Input | Returns | Procedure / view |
|-----------|-------|---------|------------------|
| `ListConsultationVolumeReport(DateTime fromDate, DateTime toDate)` | — | `List<ConsultationVolumeRow>` — `Date, ConsultationCount` | `cln.usp_ListConsultationVolumeReport` |
| `ListDiagnosisFrequencyReport(DateTime fromDate, DateTime toDate)` | — | `List<DiagnosisFrequencyRow>` — `Icd10Code, Description, DiagnosisCount`; ranked | `cln.usp_ListDiagnosisFrequencyReport` |
| `ListPrescribingReport(DateTime fromDate, DateTime toDate)` | — | `List<PrescribingRow>` — `MedicineName, PrescriptionCount`; ranked | `cln.usp_ListPrescribingReport` |
| `ListNoShowReport(DateTime fromDate, DateTime toDate)` | — | `NoShowReport` — `Total, NoShows, NoShowRate, ByStatus (List<StatusCountRow>: Status, StatusCount)` | `opd.usp_ListNoShowReport` |
| `ListRevenueReport(DateTime fromDate, DateTime toDate)` | — | `List<RevenueRow>` — `DoctorId, InvoiceCount, TotalInvoiced, TotalPaid` | `bil.usp_ListRevenueReport` |
| `ListRevenueByServiceReport(DateTime fromDate, DateTime toDate)` | — | `List<RevenueByServiceRow>` — `Category, TotalRevenue, InvoiceCount, InvoiceDate` | `bil.vw_RevenueByServiceCategory` (view, filtered by the service) |
| `GetDayEndSummary(DateTime date)` | — | `DayEndSummary` — `InvoiceCount, TotalBilled, TotalCollected, TotalOutstanding, ByComponent (List<PaymentComponentRow>: ComponentType, PaymentMethod, TotalAmount, PaymentCount)` | `bil.vw_DayEndSummary` + `bil.vw_PaymentsByComponent` (views). Feature `billing` also reaches this |

---

## 12. Notifications — message templates and the outbound queue

Feature: `admin_hospital` (ADMIN) for templates and channel config. `CreateQueuedMessage` and
`SubmitQueuedMessages` (appointment reminders, no-show messages), `ListMessageLog` and
`GetMessageStats` (the Message Log screen) are also reachable with `scheduling`.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListMessageTemplates()` | — | `List<MessageTemplate>` — `MessageTemplateId, Name, TriggerEvent, Channel, TemplateText, IsEnabled` | `cfg.usp_ListMessageTemplates` |
| `UpdateMessageTemplateStatus(string messageTemplateId)` | — (toggles) | `void` | `cfg.usp_UpdateMessageTemplateStatus` |
| `UpdateMessageTemplate(string messageTemplateId, UpdateTemplateRequest request)` | `Name?, Trigger?, Channel?, Text?, IsEnabled?` — only supplied members change | `MessageTemplate` | `cfg.usp_UpdateMessageTemplate` |
| `GetNotificationConfig()` | — | `List<ChannelConfig>` — `Channel, Provider, ConfigJson, IsEnabled`; the API key is never returned | `msg.usp_GetNotificationConfig` |
| `SaveNotificationConfig(string channel, SaveChannelConfigRequest request)` | `Provider?, ApiKey?` (write-only; encrypted column), `PhoneNumberId?, BusinessAccountId?, SmtpHost?, SmtpPort?, FromEmail?, FromName?, Enabled?` | `void` (upserted) | `msg.usp_SaveNotificationConfig`. **Audited: UPDATE** (never logs the key) |
| `CreateQueuedMessage(QueueMessageRequest request)` | `PatientId?, Channel, TemplateName?, PatientName?, PatientPhone?, MessageText?` | `QueuedMessage` (`Status = QUEUED` if the channel is enabled, else `SIMULATED`) | `msg.usp_CreateQueuedMessage` |
| `SubmitQueuedMessages()` | — | `void` — moves QUEUED / SIMULATED rows to SENT | `msg.usp_SubmitQueuedMessages` |
| `ListMessageLog()` | — | `List<QueuedMessage>`, newest first | `msg.usp_ListMessageLog` |
| `GetMessageStats()` | — | `MessageStats` — `Total, Sent, Simulated, Failed` | `msg.usp_GetMessageStats` |

In-app notifications for staff (the top-bar bell, `notifications.ashx`) — *v3.2*. Feature: any
signed-in user; each user reaches only their own.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `ListUserNotifications(int take)` | default 15 | `List<UserNotification>` — `NotificationId, Kind, Title, Message, Url, IsCritical, EntityId, ReadAt, CreatedAt`; unread critical first, then newest | `sec.usp_ListUserNotifications` |
| `GetUserNotificationUnreadCount()` | — | `int` | `sec.usp_GetUserNotificationUnreadCount`. Polled by every page (`Site.Master`) — cache per user for a few seconds |
| `UpdateUserNotificationRead(string notificationId)` | — | `UserNotification` or `null` (not the caller's) | `sec.usp_UpdateUserNotificationRead` |
| `UpdateUserNotificationsReadAll()` | — | `int` — how many were marked | `sec.usp_UpdateUserNotificationsReadAll` |

---

## 13. Audit & export — audit log and data export

Features: `audit_log` for reading the log, `data_export` for exports (ADMIN only).
`RecordAuditEntry` is open to any signed-in user.

| Operation | Input | Returns | Procedure / notes |
|-----------|-------|---------|-------------------|
| `RecordAuditEntry(AuditEntryRequest request)` | `Action, EntityType, EntityId?, Details?` — user, IP and user agent come from the call (`CallerContext` and the message properties) | `AuditEntry` (append-only) | `sec.usp_RecordAuditEntry`. For screen-level events; operations marked **Audited** already record their own |
| `ListAuditLogs()` | — | `List<AuditEntry>`, newest first | `sec.usp_ListAuditLogs` |
| `ListAuditLogsFiltered(AuditFilter filter)` | `Action?, EntityType?, UserId?, FromDate?, ToDate?, Top?` (default 200) | `List<AuditEntry>`, newest first | `sec.usp_ListAuditLogsFiltered` |
| `ListExportTables()` | — | `List<ExportTableInfo>` — `TableKey, RowCount` for the allow-listed tables | `sec.usp_ListExportTables` |
| `ListExportData(string tableKey)` | `tableKey`: `patients` / `appointments` / `consultations` / `prescriptions` / `invoices` / `paymentTransactions` / `receipts` / `auditLogs` | `ExportFile` — `FileName, ContentType = text/csv, Content (byte[], UTF-8 CSV: header row + quoted values)` | One fixed procedure per table: `sec.usp_ListExportData_Patients`, `sec.usp_ListExportData_Appointments`, `sec.usp_ListExportData_Consultations`, `sec.usp_ListExportData_Prescriptions`, `sec.usp_ListExportData_Invoices`, `sec.usp_ListExportData_PaymentTransactions`, `sec.usp_ListExportData_Receipts`, `sec.usp_ListExportData_AuditLogs`. An unknown key is a `Validation` fault — the service never builds SQL from it (architecture §8 rule 5). **Audited: EXPORT** (`Details = tableKey`, never the rows) |

---

## Operation Count Summary

All operations are on the one contract `IDOCNyleUnifiedAPI` (`DOCNyleUnifiedAPI.svc`).

| § | Region | v3.1 | v3.2 added | Operations |
|---|--------|-----:|-----------:|-----------:|
| 1 | Auth | 3 | — | 3 |
| 2 | Users & permissions | 8 | — | 8 |
| 3 | Hospital & configuration | 22 | 4 | 26 |
| 4 | Patients | 22 | 17 | 39 |
| 5 | Scheduling (incl. patient portal) | 34 | 11 | 45 |
| 6 | Consultation | 40 | 6 | 46 |
| 7 | Catalog | 13 | 1 | 14 |
| 8 | Lab | 6 | 9 | 15 |
| 9 | Billing | 27 | 2 | 29 |
| 10 | Inventory | 11 | — | 11 |
| 11 | Reports | 7 | — | 7 |
| 12 | Notifications | 9 | 4 | 13 |
| 13 | Audit & export | 5 | — | 5 |
| | **Total** | **207** | **54** | **261** |

Every procedure and view in `stored-procedures.sql` backs an operation above, except the
internal helpers (`cfg.fn_*`, `bil.usp_RecalculateInvoiceSplit`,
`inv.usp_RecordStockConsumption`, and v3.2's `opd.usp_CheckSlotAvailable`,
`sec.usp_CreateUserNotification`, `sec.usp_UpdateUserNotificationReadForEntity`, and v3.3's
`sec.usp_GetUserCredential`, `sec.usp_RecordLoginAttempt`, both inside `AuthenticateUser`), noted
where they run. Five v3.2 operations reuse existing procedures (`GetPatientConsentDocumentContent`,
`GetLabResultAttachmentContent`, `GetPortalBookingOptions`, `ListPortalAvailableSlots`,
`CreatePortalPatient`).

---

## Known Gaps / TODOs

1. **Closed in v3.2: features that had no procedures.** The consultation form designer, in-app
   notifications, consent documents and lab attachments, lab partners, payer administration,
   social history, outside medicines, reschedule / recurring booking, the CSV import, and the
   other in-memory-only methods found while mapping the facade (`API-INTEGRATION-GUIDE.md` §8)
   now have procedures (section 22 of `stored-procedures.sql`) and operations above. Files travel
   as MTOM `byte[]` within the 50 MB quota; a streamed (`transferMode="Streamed"`) endpoint is an
   optimization for later, not a requirement.
2. **Pure logic stays in-process.** `ContradictionEngine`, `RxReconciler`, `SoapComposer`,
   `BranchedFlowEngine`, `VitalUnitService`, `PhoneService` and `TimeZoneService` formatting
   helpers have no data access and are not services; the Web Forms app keeps calling them
   directly.
3. **Hard deletes** (`bil.usp_DeleteInvoiceLineItem`, `cln.usp_DeleteLabFavorite`,
   `cln.usp_DeleteConsultationServiceItem` before sign-off) are intentional: those rows have no
   `IsActive` column, or nothing has happened to them yet.
4. **Password hashing — closed in v3.3.** `AuthenticateUser`, `CreateUser` and `SaveDoctorProfile`
   take the plain `Password` over HTTPS; the service hashes it (PBKDF2-SHA256, per-user salt,
   ≥ 100,000 iterations) into `@PasswordHash`. Sign-in reads the stored hash with
   `sec.usp_GetUserCredential`, verifies in C#, logs the attempt with `sec.usp_RecordLoginAttempt`
   (`sec.LoginAttempt`, no longer reserved) and only then calls `sec.usp_AuthenticateUser`. The
   password is never logged, audited or returned.
5. **Patient registration fields and MRN — closed in v3.3.** `opd.usp_CreatePatient` and
   `opd.usp_UpdatePatient` take every field of the registration form and `RegisteredVia`
   (`CreatePortalPatient` and `CreatePatientsBatch` set it); MRNs follow the hospital's
   `MRN_PREFIX` / `MRN_FORMAT` / `MRN_SEQ`, allocated under a lock instead of `MAX(Id) + 1`.
6. **Front-desk double booking — closed in v3.3.** `opd.usp_CreateAppointment` re-checks the
   slot under the per-doctor-day lock. It can therefore not be wrapped in `INSERT … EXEC`; the
   SQL callers that capture its result pass `@SkipSlotCheck = 1` after checking themselves.
7. **Slot list loop — closed in v3.3.** `opd.usp_ListAvailableSlots` returns no slots for a length
   under 5 or over 720 minutes and treats a negative buffer as 0.
8. **Searching on encrypted columns.** `opd.usp_CheckDuplicatePatient`,
   `cln.usp_SearchLabOrdersByMobile` and `opd.usp_GetPortalPatient` strip digits from
   `opd.Patient.Phone` / `Mobile` on the server. Once Always Encrypted is applied to those columns
   (schema comments: DETERMINISTIC) that is impossible; add a `MobileKey` column written by the
   service (last 10 digits) and index it first.

---

*Version 3.3 (WCF, DOCNyleUnifiedAPI) — 261 operations, checked against docs/stored-procedures.sql v3.6 (266
procedures, 3 views, 7 functions; compiled and smoke-tested on SQL Server LocalDB at compatibility level 150) and
docs/schema.sql v4.1. Supersedes v3.2, v3.1 (207 operations) and v2.3 (REST endpoints).*
