# Consult360 — REST API Specification

**Version:** 2.3
**Scope:** This document is derived 1:1 from `js/api.js` (150 public methods),
`js/data.js` (the Phase 1 business-logic implementation each method wraps),
and `docs/schema.sql` (the SQL Server 2019 schema), per the binding rules in
`docs/DATA-LAYER-ARCHITECTURE.md`. Every method on `window.API` maps to
exactly one endpoint below, using the verb → HTTP mapping table in
architecture doc §2 and the `api.js → ASP.NET Controller` mapping in §3.
Every endpoint names the stored procedure from `docs/stored-procedures.sql`
that backs it.

**v2.1 change note:** a Phase-1 JSDOM functional-test pass found 7 UI call
sites invoking `window.API` methods that didn't exist yet, plus one
documented-but-unwired method (`updatePermission`). Fixing those on the
client added `toggleSpecialty`, `createVitalConfig`, `createDrug`,
`updateDrugStatus`, `updateMessageTemplate`, and `exportTableCsv` to
`api.js`, and this revision adds their endpoints/procedures here (5 new
stored procedures; `exportTableCsv` reuses the existing export procedures).
`updatePermission`'s endpoint is now listed rather than marked
"not currently called." Method count moved from 143 to 150 accordingly.

**v2.3 change note:** `POST/PUT /doctors` is now backed by `sec.usp_SaveDoctorProfile`, and
`PUT /hospitals/{id}` (`cfg.usp_UpdateHospital`) is new; every endpoint now has a procedure.

**v2.2 change note:** re-checked endpoint by endpoint against
`docs/stored-procedures.sql` v3.2. Request fields now use the procedures' parameter
names (the v4.0 schema renames — e.g. `reactionType`, `onsetMonth`, `icd10Description`,
`defaultRoute` — had not reached this document); appointments, slots, consultations and
sign-off describe the UTC / clinic-timezone handling; sign-off deducts clinic stock;
invoice lines carry `inventoryItemId`. New: Services & Procedures items (§5),
Clinic Inventory (§11) and time zones (§2). Known Gaps rewritten (the v4.0 schema
resolved the earlier ones).

**Base URL:** `https://{host}/api`
**Auth:** Bearer JWT (Phase 4). Session token in header: `X-Session-Token`.
**Tenant:** Every request is scoped by `HospitalId` from JWT claims (the
`_hid()` guard in api.js). Server injects it via `SESSION_CONTEXT` for
row-level security (architecture §6).
**Format:** JSON request/response. IDs are UUIDs (the external `{Table}Id`,
never the internal `Id`).
**Dates & times:** every point in time is ISO 8601 UTC with its offset
(`2026-10-20T14:00:00Z` / `+00:00`). Business events tied to a clinic (appointments,
visits) also return `timeZoneId` (IANA, e.g. `America/Jamaica`) and the same instant on
the clinic's clock (`slotStartLocal` = `2026-10-20T09:00:00-05:00`); show them in that
zone for every user. Calendar dates (`date`, `expiryDate`, `followupDate`, report
`fromDate`/`toDate`) are dates in the hospital's timezone, not UTC.
**Actor fields:** `createdBy` / `modifiedBy` / `recordedByUserId` / `uploadedByUserId`
parameters are filled by the API from the signed-in user and are not part of request bodies.
**Roles:** ADMIN, DOCTOR, RECEPTIONIST, BILLING, NURSE, LAB_PARTNER — see
`sec.PermissionMatrix` / the `permissionMatrix` object in data.js. Endpoints
note the roles that data.js's static permission matrix grants access to via
the named feature flag (e.g. `consultation`, `billing`, `admin_users`);
enforcement in Phase 1 is at the router, and in Phase 4 becomes
`[Authorize(Policy = "Require{Feature}")]`.

---

## 1. Authentication, Sessions & Users

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/auth/login` | `{ email, passwordHash }` | `user \| null` | `sec.usp_AuthenticateUser` — *api.js: `authenticateUser`* (also inserts `sec.LoginAttempt`) |
| POST | `/auth/session` | `{ userId, sessionToken, browser?, ipAddress?, expiresAt }` | `{ userSessionId, sessionToken, createdAt, expiresAt }` | `sec.usp_CreateUserSession` |
| GET | `/auth/session` | Header `X-Session-Token` (passed as `@SessionToken`) | `{ valid, userId, expiresAt }` | `sec.usp_CheckSessionValid` |
| GET | `/users?hospitalId=` | `hospitalId` (query, `_hid()` fallback) | `[ User ]` — `{userId, fullName, email, phone, role, specialtyId, licenseNumber, labPartnerId, isActive, createdAt}` | `sec.usp_ListUsers` — *api.js: `listUsers`* |
| POST | `/users` | `{ hospitalId, fullName, email, passwordHash, phone?, roles, specialtyId?, licenseNumber?, labPartnerId? }` | `User` (created) | `sec.usp_CreateUser` — *api.js: `createUser`* |
| PUT | `/users/{id}` | `{ fullName?, phone?, roles?, specialtyId?, licenseNumber? }` | `User` (updated) | `sec.usp_UpdateUser` — *api.js: `updateUser`* |
| PUT | `/users/{id}/active` | — (toggles current value) | `{ userId, isActive }` | `sec.usp_UpdateUserActive` — *api.js: `updateUserActive`*. AUDIT: role-change adjacent action; caller should also call `POST /audit/logs`. |

### Permissions (`admin_users`, `ADMIN` role)

| Verb | Endpoint | Params | Returns | SP |
|------|----------|--------|---------|-----|
| GET | `/permissions?hospitalId=` | `hospitalId` | `{ [role]: { [feature]: boolean } }` (pivoted from flat grid) | `sec.usp_GetPermissionMatrix` — *api.js: `getPermissionMatrix`* |
| GET | `/permissions/roles?hospitalId=` | `hospitalId` | `[ roleName ]` | `sec.usp_ListRoles` — *api.js: `listRoles`* |
| GET | `/permissions/features?hospitalId=` | `hospitalId` | `[ featureName ]` | `sec.usp_ListPermissionFeatures` — *api.js: `listPermissionFeatures`* |
| PUT | `/permissions/{role}/{feature}` | `{ hospitalId, isGranted }` — toggles the current value if `isGranted` is omitted, matching `data.js`'s `updatePermission(role, feature)` | `{ [feature]: boolean }` — the role's updated feature grid | `sec.usp_UpdatePermission` — *api.js: `updatePermission`*. AUDIT: caller should also invoke `POST /audit/logs` (`Action='UPDATE'`, `EntityType='Permission'`). |

---

## 2. Hospitals & Specialties

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/hospitals/{id}` | — (`id` defaults to `_hid()`) | `Hospital` — `{hospitalId, name, address, phone, email, country, currency, timezone, licenseNumber, patientIdLabel, nationalHealthIdLabel, isNationalHealthIdMandatory, defaultTempUnit, defaultRxValidityDays, isActive}` | `cfg.usp_GetHospital` — *api.js: `getHospital`* |
| GET | `/specialties?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ Specialty ]` — `{specialtyId, hospitalId, specialtyName, isActive}`, active only | `cfg.usp_ListSpecialties` — *api.js: `listSpecialties`* |
| POST | `/specialties` | `{ hospitalId, specialtyName }` | `Specialty` (created) | `cfg.usp_CreateSpecialty` — *api.js: `createSpecialty`* |
| GET | `/config/specialties?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ Specialty ]`, incl. inactive (admin config view) | `cfg.usp_ListSpecialtyConfig` — *api.js: `listSpecialtyConfig`* |
| PUT | `/specialties/{id}/toggle-active` | — (toggles current value) | `Specialty` (updated) | `cfg.usp_ToggleSpecialty` — *api.js: `toggleSpecialty`* |
| PUT | `/hospitals/{id}` | `{ name?, legalAddress?, contactNumber?, primaryEmail?, country?, currency?, timezone?, licenseNumber?, patientIdLabel?, nationalHealthIdLabel?, nationalHealthIdMandatory?, growthChartStandard?, defaultTempUnit?, defaultRxValidityDays?, rxPrefix?, receiptPrefix?, invoicePrefix?, labChargePerTest?, logoBase64?, removeLogo? }` — omitted fields keep their value; `timezone` must be an id from `GET /time-zones` | `Hospital` (same as GET) | `cfg.usp_UpdateHospital`. Errors 50070 hospital not found, 50071 unknown time zone, 50072 blank name, 50073 Rx validity < 1 |
| GET | `/time-zones` | — | `[ { timeZoneId, windowsTimeZoneId, displayName } ]` — IANA ids a hospital can use (`cfg.TimeZone`); a hospital's `timezone` drives its "today", report dates and appointment times | `cfg.usp_ListTimeZones` |
| GET | `/payment-methods?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ PaymentMethod ]` — `{paymentMethodId, hospitalId, methodName, isActive}` | `cfg.usp_ListPaymentMethods` — *api.js: `listPaymentMethods`* |

---

## 3. Patients + Sub-resources

Role: `patients` feature (ADMIN, DOCTOR, RECEPTIONIST, BILLING, NURSE).

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/patients/search?term=&hospitalId=` | `term`, `hospitalId` (`_hid()` fallback); term < 2 chars returns unfiltered hospital list | `[ Patient ]` — `{patientId, mrn, fullName, gender, dob, phone, email, isActive}` | `opd.usp_SearchPatients` — *api.js: `searchPatients`* |
| GET | `/patients/{id}` | — | `Patient` (full record incl. `address`, `nationalHealthId`, `insuranceProvider`, `insurancePolicyNo`, `photoBase64`, `emergencyContactName`, `emergencyContactPhone`) | `opd.usp_GetPatient` — *api.js: `getPatient`*. AUDIT: caller must also call `POST /audit/logs` (Action=`READ`). |
| POST | `/patients` | `{ hospitalId, fullName, gender, dob, phone, email?, address?, nationalHealthId?, insuranceProvider?, insurancePolicyNo?, emergencyContactName?, emergencyContactPhone? }` | `Patient` (created; server generates `mrn`) | `opd.usp_CreatePatient` — *api.js: `createPatient`*. AUDIT: caller must also call `POST /audit/logs` (Action=`CREATE`). |
| PUT | `/patients/{id}` | `{ fullName?, phone?, email?, address?, nationalHealthId?, insuranceProvider?, insurancePolicyNo?, emergencyContactName?, emergencyContactPhone? }` | `Patient` (updated) | `opd.usp_UpdatePatient` — *api.js: `updatePatient`* |

### Allergies

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/patients/{id}/allergies` | — | `[ Allergy ]` — `{patientAllergyId, hospitalId, patientId, allergyType, allergyName, reaction, severity, status, onsetDate}` | `opd.usp_ListAllergies` — *api.js: `listAllergies`* |
| POST | `/patients/{id}/allergies` | `{ hospitalId, allergyType, allergyName, reactionType?, reactionDescription?, severity, isTreated?, isFamilyHistory?, sinceWhen?, notes? }` | `Allergy` (created, `status`='ACTIVE') | `opd.usp_CreateAllergy` — *api.js: `createAllergy`* |
| PUT | `/allergies/{id}` | `{ reactionType?, reactionDescription?, severity?, status?, isTreated?, sinceWhen?, notes? }` | `Allergy` (updated) | `opd.usp_UpdateAllergy` — *api.js: `updateAllergy`* |
| DELETE | `/allergies/{id}` | — | `{ success }` | `opd.usp_DeleteAllergy` — *api.js: `deleteAllergy`*. Soft delete: sets `Status`. |

### Medical History (patient-level entries + hospital config lists)

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/config/medical-history-items?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ MedicalHistoryItem ]` — `{medicalHistoryItemId, hospitalId, itemName, sortOrder}`, active only, sorted | `cfg.usp_ListMedicalHistoryItems` — *api.js: `listMedicalHistoryItems`* |
| GET | `/config/medical-history?hospitalId=` | `hospitalId` | `[ MedicalHistoryItem ]` incl. inactive (admin view) | `cfg.usp_ListMedicalHistoryConfig` — *api.js: `listMedicalHistoryConfig`* |
| POST | `/config/medical-history` | `{ hospitalId, itemName }` | `MedicalHistoryItem` (created) | `cfg.usp_CreateMedicalHistoryConfigItem` — *api.js: `createMedicalHistoryConfigItem`* |
| PUT | `/config/medical-history/{itemName}/status` | `{ hospitalId, isActive }` | `{ success }` | `cfg.usp_UpdateMedicalHistoryItemStatus` — *api.js: `updateMedicalHistoryItemStatus`* |
| GET | `/patients/{id}/medical-history` | — | `[ PatientMedicalHistory ]` — `{patientMedicalHistoryId, patientId, conditionName, sinceMonth, sinceYear, notes, createdAt}` | `opd.usp_ListPatientMedicalHistory` — *api.js: `listPatientMedicalHistory`* |
| GET | `/patients/{id}/medical-history/all` | — | Same shape as above (data.js dual-named method) | `opd.usp_GetPatientMedicalHistory` — *api.js: `getPatientMedicalHistory`* |
| POST | `/patients/{id}/medical-history` | `{ hospitalId, itemId?, conditionName, onsetMonth?, onsetYear?, status?, notes? }` | `PatientMedicalHistory` (created) | `opd.usp_CreatePatientMedicalHistory` — *api.js: `createPatientMedicalHistory`* |
| PUT | `/patients/{id}/medical-history/save` | `{ hospitalId, itemId?, conditionName, onsetMonth?, onsetYear?, status?, notes? }` | `PatientMedicalHistory` (upserted by `conditionName`) | `opd.usp_SavePatientMedicalHistory` — *api.js: `savePatientMedicalHistory`* |
| PUT | `/patients/{id}/medical-history/{conditionName}` | `{ onsetMonth?, onsetYear?, status?, notes? }` | `{ success }` | `opd.usp_UpdatePatientMedicalHistory` — *api.js: `updatePatientMedicalHistory`* |
| DELETE | `/patients/{id}/medical-history/{conditionName}` | — | `{ success }` | `opd.usp_DeletePatientMedicalHistory` — *api.js: `deletePatientMedicalHistory`*. Soft delete (`IsActive=0`). |

### Medical Events

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/config/medical-event-items?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ MedicalEventType ]` — `{medicalEventTypeId, hospitalId, eventTypeName}` | `cfg.usp_ListMedicalEventItems` — *api.js: `listMedicalEventItems`* |
| GET | `/patients/{id}/medical-events` | — | `[ PatientMedicalEvent ]` — `{patientMedicalEventId, patientId, eventType, eventDate, notes, createdAt}` | `opd.usp_ListPatientMedicalEvents` — *api.js: `listPatientMedicalEvents`* |
| POST | `/patients/{id}/medical-events` | `{ hospitalId, eventId?, eventName, eventMonth?, eventYear?, notes? }` | `PatientMedicalEvent` (created) | `opd.usp_CreatePatientMedicalEvent` — *api.js: `createPatientMedicalEvent`* |

### Family History

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/patients/{id}/family-history` | — | `[ FamilyHistory ]` — `{patientFamilyHistoryId, patientId, relation, condition, notes, createdAt}` | `opd.usp_ListPatientFamilyHistory` — *api.js: `listPatientFamilyHistory`* |
| POST | `/patients/{id}/family-history` | `{ hospitalId, relationshipType, conditionName, notes? }` | `FamilyHistory` (created) | `opd.usp_CreatePatientFamilyHistory` — *api.js: `createPatientFamilyHistory`* |

### Past Visits & Previous Prescriptions (Consultation 360 Zone A)

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/patients/{id}/past-visits` | — | `[ { consultationId, visitDate, doctorName, chiefComplaint, primaryDiagnosis } ]`, LOCKED consultations only, newest first | `opd.usp_ListPatientPastVisits` — *api.js: `listPatientPastVisits`* |
| GET | `/patients/{id}/prescriptions/previous?doctorId=` | `doctorId` | `{ found: boolean, consultation: Consultation \| null, prescriptions: [Prescription] }` (most recent LOCKED consultation with this doctor + its ACTIVE prescriptions) | `opd.usp_ListPreviousPrescriptionsByDoctor` — *api.js: `listPreviousPrescriptionsByDoctor`* |

### Sketches (across all visits — see also §7 Consultations)

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/patients/{id}/sketches` | — | `[ Sketch ]`, newest first | `cln.usp_ListPatientSketches` — *api.js: `listPatientSketches`* |

---

## 4. Scheduling — Appointments & Slots

Role: `scheduling` feature (ADMIN, DOCTOR, RECEPTIONIST, NURSE).

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/doctors/{doctorId}/appointments/today?hospitalId=` | `hospitalId` | `[ Appointment ]` — {appointmentId, patientId, patientName, mrn, slotStart, slotEnd, timeZoneId, slotStartLocal, slotEndLocal, type, status, notes}; the hospital's today (its timezone), sorted by `slotStart`. `slotStart`/`slotEnd` are UTC; `…Local` are the same instants on the clinic's clock | `opd.usp_ListTodayAppointmentsForDoctor` — *api.js: `listTodayAppointmentsForDoctor`* |
| GET | `/doctors/{doctorId}/appointments?hospitalId=&date=` | `hospitalId`, `date` (hospital-local date → `@AppointmentDate`) | `[ Appointment ]` — {appointmentId, patientId, patientName, mrn, slotStart, slotEnd, timeZoneId, slotStartLocal, slotEndLocal, type, status, notes, checkedInAt, consultStartedAt, completedAt} | `opd.usp_ListAppointmentsForDate` — *api.js: `listAppointmentsForDate`* |
| GET | `/appointments?hospitalId=&date=` | `hospitalId`, `date` (hospital-local date → `@AppointmentDate`) | `[ Appointment ]` — every doctor's appointments for the date: {appointmentId, patientId, patientName, mrn, slotStart, slotEnd, timeZoneId, slotStartLocal, slotEndLocal, type, status, notes, doctorId, doctorName, doctorRoom, checkedInAt, consultStartedAt, completedAt}; sorted by `slotStart`. Front-desk (receptionist) view on Today's Patients | `opd.usp_ListAppointmentsForHospitalDate` — *api.js: `listAppointmentsForHospitalDate`* |
| GET | `/doctors/{doctorId}/slot-config` | — | `[ SlotConfig ]` — weekly availability windows `{slotConfigId, hospitalId, doctorId, dayOfWeek, startTime, endTime}`; several per weekday allowed, none = closed | `opd.usp_ListDoctorSlotConfig` — *api.js: `listDoctorSlotConfig`* |
| PUT | `/doctors/{doctorId}/slot-config?hospitalId=` | `[ { dayOfWeek, startTime, endTime } ]` (replaces all windows; sent as `@WindowsJson`; times are the hospital's wall clock, `HH:mm`) | `[ SlotConfig ]` | `opd.usp_SaveDoctorSlotConfig` — *api.js: `saveDoctorSlotConfig`* |
| GET | `/doctors/{doctorId}/available-slots?hospitalId=&date=&duration=` | `hospitalId`, `date` (hospital-local → `@AppointmentDate`), optional `duration` (appointment type minutes → `@DurationMinutes`) | `[ { slotStart, slotEnd, isBooked, timeZoneId } ]` — `slotStart`/`slotEnd` are the hospital's wall-clock times (`2026-10-20T09:00:00`, no offset): pass them unchanged to `POST /appointments`. MediSlot engine: none on leave days or weekdays without windows; slot = `duration` or `APPT_SLOT_MINS` (doctor → hospital); step = slot + `APPT_BUFFER_MINS`; `isBooked` = overlaps a non-cancelled appointment (+ buffer) | `opd.usp_ListAvailableSlots` — *api.js: `listAvailableSlots`* |
| GET | `/doctors/{doctorId}/schedule?hospitalId=&date=` | `hospitalId`, `date` | `{ dayOfWeek, windows:[{startTime,endTime}], workingDays:[…], slotMinutes, bufferMinutes, onLeave, leaveReason }` | `opd.usp_GetDoctorScheduleForDate` — *api.js: `getDoctorScheduleForDate`* |
| POST | `/appointments` | `{ hospitalId, patientId, doctorId, slotStartLocal, slotEndLocal, type?, notes? }` — `slotStartLocal`/`slotEndLocal` are the hospital's wall-clock times (a slot from `available-slots`); stored as UTC plus the hospital's `timeZoneId` | `{ appointmentId, appointmentDate, slotStart, slotEnd, timeZoneId, slotStartLocal, slotEndLocal, status }` (`status`='BOOKED'; `slotStart`/`slotEnd` UTC) | `opd.usp_CreateAppointment` — *api.js: `createAppointment`*. Error 50050: hospital has no timezone |
| PUT | `/appointments/{id}/status` | `{ status }` | `Appointment` `{appointmentId, status, checkedInAt, consultStartedAt, completedAt}`, or `null` (HTTP 409) when the move is not allowed. Lifecycle: `BOOKED → CHECKED_IN` (front desk) `→ IN_CONSULT` (doctor opens the consultation) `→ COMPLETED` (sign-off); `CANCELLED`/`NO_SHOW` only from `BOOKED` or `CHECKED_IN`. Stamps `checkedInAt` (+ `checkedInBy`), `consultStartedAt`, `completedAt` | `opd.usp_UpdateAppointmentStatus` — *api.js: `updateAppointmentStatus`* |

---

### Schedule Settings (`admin_scheduling`, ADMIN + DOCTOR by default)

Screen: `#/settings/scheduling` (sidebar → Scheduling → Schedule Settings) — tabs Hospital Defaults · Doctors · Appointment Types · Intake & Consent. Formats live under Settings → Hospital & Practice; condition-linked macros in the Phrase Library.

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/hospitals/{hospitalId}/settings` | — | `{ APPT_SLOT_MINS, APPT_BUFFER_MINS, FREE_FOLLOWUP_ALLOWED, FREE_FOLLOWUP_DAYS, DATE_FORMAT, TIME_FORMAT, MRN_PREFIX, MRN_FORMAT, MRN_SEQ, DOCTOR_NAME_TEMPLATE }` | `cfg.usp_GetHospitalSettings` — *api.js: `getHospitalSettings`* |
| PUT | `/hospitals/{hospitalId}/settings` | `{ key: value, … }` (sent as `@ValuesJson`; MRN_SEQ ignored); a new DOCTOR_NAME_TEMPLATE re-renders every doctor's `fullName` (in the procedure) | settings map | `cfg.usp_SaveHospitalSettings` — *api.js: `saveHospitalSettings`* |
| GET | `/doctors/{doctorId}/settings?hospitalId=` | — | `{ key: { value, source: DOCTOR\|HOSPITAL\|NONE } }` for APPT_SLOT_MINS, FREE_FOLLOWUP_ALLOWED, FREE_FOLLOWUP_DAYS, CHARGE_MODEL, CHARGE_UNIT_MINS, CHARGE_UNIT_RATE | `opd.usp_GetDoctorSettings` — *api.js: `getDoctorSettings`* |
| PUT | `/doctors/{doctorId}/settings?hospitalId=` | `{ key: value }` (sent as `@ValuesJson`) — `""`/`null` removes the override | same as GET | `opd.usp_SaveDoctorSettings` — *api.js: `saveDoctorSettings`* |
| GET | `/doctors/{doctorId}/leave?hospitalId=` | — | `[ { leaveId, leaveDate, reason } ]` | `opd.usp_ListDoctorLeave` — *api.js: `listDoctorLeave`* |
| POST | `/doctors/{doctorId}/leave` | `{ hospitalId, leaveDate, reason }` | leave row (existing row if that date is already leave) | `opd.usp_CreateDoctorLeave` — *api.js: `createDoctorLeave`* |
| DELETE | `/doctor-leave/{leaveId}` | — | `{ success }` | `opd.usp_DeleteDoctorLeave` — *api.js: `deleteDoctorLeave`* |
| GET | `/appointment-types?hospitalId=&includeInactive=` | — | `[ { appointmentTypeId, code, name, durationMinutes (null = doctor's slot), color, isChargeable, feeCategory, sortOrder, isActive } ]` | `cfg.usp_ListAppointmentTypes` — *api.js: `listAppointmentTypes`* |
| POST/PUT | `/appointment-types` | `{ appointmentTypeId?, name, durationMinutes?, color, isChargeable, feeCategory }` | type (code generated from the name on create) | `cfg.usp_SaveAppointmentType` — *api.js: `saveAppointmentType`* |
| PUT | `/appointment-types/{id}/active` | `{ isActive }` | type | `cfg.usp_UpdateAppointmentTypeActive` — *api.js: `updateAppointmentTypeActive`* |
| GET | `/hospitals/{hospitalId}/doctors` | — | `[ DoctorProfile ]` — `{ userId, displayName, prefix, firstName, middleName, lastName, qualifications, displayNameOverride, licenseNumber, email, phone, room, languages, bio, specialtyIds, specialtyNames }` (no password) | `sec.usp_ListDoctorProfiles` — *api.js: `listDoctorProfiles` / `getDoctorProfile`* |
| POST / PUT | `/doctors` · `/doctors/{id}` | `{ hospitalId, email, passwordHash?, prefix?, firstName?, middleName?, lastName?, qualifications?, displayNameOverride?, licenseNumber?, phone?, room?, languages?, bio?, specialtyIds? }` — POST creates a DOCTOR login (`passwordHash` required); PUT updates (`{id}` → `@DoctorId`, password ignored). `specialtyIds` is an ordered array sent as `@SpecialtyIdsJson`: the first becomes the primary specialty; another hospital's ids are ignored | DoctorProfile — `{doctorId, hospitalId, fullName, email, phone, specialtyId, licenseNumber, prefix, firstName, middleName, lastName, qualifications, displayNameOverride, room, languages, bio, isActive}` plus `[ { specialtyId, specialtyName, isPrimary } ]`. `fullName` = `displayNameOverride`, else the hospital's `DOCTOR_NAME_TEMPLATE`. A new doctor gets 0-amount NEW / FOLLOWUP fee rows from the hospital's today | `sec.usp_SaveDoctorProfile` — *api.js: `saveDoctorProfile`*. Errors 50060 email required, 50061 name required, 50062 email in use, 50063 bad `specialtyIds`, 50064 new doctor without `passwordHash`, 50065 doctor not found in this hospital |
| GET/PUT | `/doctors/{doctorId}/charge?hospitalId=` | PUT `{ model: FLAT\|UNIT, newFee, followupFee, unitMinutes, unitRate }` (a changed flat fee closes the current fee row and starts a new one today) | `{ model, newFee, followupFee, unitMinutes, unitRate }` | via `opd.usp_SaveDoctorSettings` + `bil.DoctorFeeSchedule` — *api.js: `getDoctorCharge` / `saveDoctorCharge`* |
| GET | `/consultations/{id}/charge` | — | `{ consultationType, appointmentTypeName, durationMinutes, chargeModel, baseFee, feeAmount, isFreeFollowup, anchorDate, basis, note, description }` — used when the invoice is generated | `bil.usp_GetConsultationCharge` — *api.js: `getConsultationCharge`* |
| GET | `/intake-questions?hospitalId=` | — | `[ { questionId, label, questionType: TEXT\|NUMBER\|CHOICE, options } ]` | `cfg.usp_ListIntakeQuestions` — *api.js: `listIntakeQuestions`* |
| POST / DELETE | `/intake-questions` · `/intake-questions/{id}` | `{ label, questionType, options }` | question / `{ success }` (soft delete) | `cfg.usp_CreateIntakeQuestion` / `cfg.usp_DeleteIntakeQuestion` — *api.js: `createIntakeQuestion` / `deleteIntakeQuestion`* |
| GET / PUT | `/consent-form?hospitalId=` | PUT `{ title, bodyText }` (bumps version) | `{ consentFormId, title, bodyText, version }` | `cfg.usp_GetConsentForm` / `cfg.usp_SaveConsentForm` — *api.js: `getConsentForm` / `saveConsentForm`* |
| POST | `/appointments/{id}/intake` | `{ hospitalId, patientId, answers }` | intake response | `opd.usp_SubmitIntakeResponse` — *api.js: `submitIntakeResponse`* |
| POST | `/consent-records` | `{ hospitalId, consentFormId, patientId, appointmentId?, consultationId?, signedByName, method }` | consent record (title/text snapshotted, append-only) | `opd.usp_RecordConsent` — *api.js: `recordConsent`* |
| GET | `/consultations/{id}/intake-consent` | — | `{ appointment, intakeResponse, consentForm, consentRecord }` | `opd.usp_GetVisitIntakeAndConsent` — *api.js: `getVisitIntakeAndConsent`* |
| GET / POST / DELETE | `/clinical-conditions` · `/clinical-conditions/{id}` | GET `?hospitalId=`; POST `{ hospitalId, name, icd10Prefixes }`; DELETE by `{id}` (`@ConditionId`) | conditions (soft delete) | `cfg.usp_ListClinicalConditions` / `cfg.usp_CreateClinicalCondition` / `cfg.usp_DeleteClinicalCondition` — *api.js: `listClinicalConditions` / `createClinicalCondition` / `deleteClinicalCondition`* |

Phrases (`listPhrases`, `searchPhrases`) take an optional `consultationId`: condition-linked phrases are returned only when one of the consultation's ICD-10 diagnoses matches the condition, and a doctor's phrase hides a broader phrase with the same trigger.

## 5. Consultations — Core

Role: `consultation` feature (ADMIN, DOCTOR).

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/consultations` | `{ patientId, doctorId, hospitalId, appointmentId? }` | `Consultation` — `{consultationId, hospitalId, patientId, doctorId, appointmentId, visitDate, timeZoneId, visitLocalDate, status, isLocked}` (existing IN_PROGRESS one, or newly created with `visitDate` = now in UTC, the hospital's `timeZoneId` and its local `visitLocalDate`) | `cln.usp_GetOrCreateConsultation` — *api.js: `getOrCreateConsultation`* |
| GET | `/consultations/{id}` | — | `Consultation` — `{consultationId, hospitalId, patientId, doctorId, appointmentId, visitDate, timeZoneId, visitLocalDate, status, isLocked, subjectiveText, objectiveText, planText, subjectivePersonal, objectivePersonal, planPersonal, followupDate, followupNotes, skippedSteps}` | `cln.usp_GetConsultationById` — *api.js: `getConsultationById`*. AUDIT: caller must also call `POST /audit/logs` (Action=`READ`). |
| PUT | `/consultations/{id}/soap` | `{ subjectiveText?, objectiveText?, planText?, subjectivePersonal?, objectivePersonal?, planPersonal?, skippedSteps? }` | `{ success }` (no-op if `isLocked`) | `cln.usp_SaveSoapNotes` — *api.js: `saveSoapNotes`* |
| PUT | `/consultations/{id}/followup` | `{ followUpDate?, followUpNotes? }` | `{ success }` | `cln.usp_SaveFollowup` — *api.js: `saveFollowup`* |
| POST | `/consultations/{id}/sign-off` | `{ doctorId }` (+ `signatureHash`, computed by the API over the signed content) | `Attestation` — `{attestationId, signedAt}`; in one transaction: deducts the stocked items listed in Services & Procedures (earliest-expiry batch first), locks the consultation (`status`='LOCKED') and completes its appointment (`IN_CONSULT → COMPLETED`) | `cln.usp_ConfirmSignOff` — *api.js: `confirmSignOff`*. Error 50040 ("Not enough stock: …") rolls the whole sign-off back; check `GET /consultations/{id}/stock-shortages` first. AUDIT: caller must also call `POST /audit/logs` (Action=`SIGNOFF`). |
| GET | `/consultations?hospitalId=&date=` | `hospitalId`, `date` (hospital-local visit date → `@VisitDate`, matched on `visitLocalDate`) | `[ Consultation ]` — `{consultationId, patientId, doctorId, appointmentId, visitDate, status}` | `cln.usp_ListConsultationsForDate` — *api.js: `listConsultationsForDate`* |

### Vitals

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/consultations/{id}/vitals` | — | `Vitals` — `{consultationVitalId, tempValue, bpSystolic, bpDiastolic, pulseBpm, spO2, respRate, heightCm, weightKg, bmi, painScale, waistCircCm, headCircCm}` | `cln.usp_GetConsultationVitals` — *api.js: `getConsultationVitals`* |
| POST | `/consultations/{id}/vitals` | `{ hospitalId, tempValue, bpSystolic, bpDiastolic, pulseBpm, spO2, respRate, heightCm, weightKg, painScale, waistCircCm, headCircCm, customValuesJson? }` | `Vitals` (upserted; `bmi` computed server-side from height+weight) | `cln.usp_SaveVitals` — *api.js: `saveVitals`* |
| GET | `/config/vitals?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ VitalDefinition ]` — `{vitalDefinitionId, hospitalId, vitalCode, displayLabel, allowedUnits, minValue, maxValue, sortOrder, isEnabled}`, sorted | `cfg.usp_ListVitalsConfig` — *api.js: `listVitalsConfig`* |
| GET | `/config/vitals/enabled?hospitalId=` | `hospitalId` | `[ VitalDefinition ]`, `isEnabled=true` only | `cfg.usp_ListEnabledVitalsConfig` — *api.js: `listEnabledVitalsConfig`* |
| PUT | `/config/vitals/{vitalCode}/enabled` | `{ hospitalId, isEnabled }` | `{ success }` | `cfg.usp_UpdateVitalEnabled` — *api.js: `updateVitalEnabled`* |
| POST | `/config/vitals` | `{ hospitalId, vitalCode, displayLabel, allowedUnits, minValue?, maxValue?, sortOrder? }` | `VitalDefinition` (created) | `cfg.usp_CreateVitalConfig` — *api.js: `createVitalConfig`* |

### Symptoms

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/catalog/symptoms?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ Symptom ]` — `{symptomCatalogId, symptomName, category}` | `cfg.usp_ListSymptomCatalog` — *api.js: `listSymptomCatalog`* |
| GET | `/catalog/symptoms/quick-picks?hospitalId=` | `hospitalId` | `[ Symptom ]`, `category`='QUICK_PICK' | `cfg.usp_ListSymptomQuickPicks` — *api.js: `listSymptomQuickPicks`* |
| GET | `/catalog/symptoms/others?hospitalId=` | `hospitalId` | `[ Symptom ]`, `category`='OTHER' | `cfg.usp_ListSymptomOthers` — *api.js: `listSymptomOthers`* |
| POST | `/catalog/symptoms` | `{ hospitalId, displayLabel }` | `Symptom` (created, or existing row if name already present) | `cfg.usp_CreateCustomSymptom` — *api.js: `createCustomSymptom`* |
| GET | `/consultations/{id}/symptoms` | — | `[ ConsultationSymptom ]` — `{consultationSymptomId, consultationId, symptomName, state}` | `cln.usp_ListConsultationSymptoms` — *api.js: `listConsultationSymptoms`* |
| PUT | `/consultations/{id}/symptoms/{symptomName}/state` | `{ hospitalId, state: 'YES'\|'NO'\|'NOT_ASKED' }` | `{ success }` | `cln.usp_UpdateSymptomState` — *api.js: `updateSymptomState`* (upsert) |

### Phrases (SOAP quick-insert)

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/phrases?hospitalId=&section=&specialtyId=&doctorId=` | `hospitalId`, `section` (S/O/A/P), `specialtyId?`, `doctorId?` | `[ Phrase ]` — `{phraseBankId, hospitalId, userId, category, triggerCode, phraseText}`, DOCTOR > SPECIALTY > GLOBAL scope priority | `cfg.usp_ListPhrases` — *api.js: `listPhrases`* |
| GET | `/phrases/search?term=&hospitalId=&section=&specialtyId=&doctorId=` | as above + `term` | `[ Phrase ]`, top 5, trigger/text match | `cfg.usp_SearchPhrases` — *api.js: `searchPhrases`* |
| POST | `/phrases` | `{ hospitalId, scope?, specialtyId?, doctorId?, conditionId?, section?, triggerKeyword?, phraseText }` | `Phrase` (created) | `cfg.usp_CreatePhrase` — *api.js: `createPhrase`* |
| DELETE | `/phrases/{id}` | — | `{ success }` | `cfg.usp_DeletePhrase` — *api.js: `deletePhrase`*. Soft delete (`IsActive=0`); data.js hard-deletes in-memory. |

### Diagnoses / ICD-10

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/catalog/icd10/search?term=` | `term` | `[ Icd10 ]` — `{icd10CatalogId, code, description, synonyms}`, code-prefix/description/synonym match | `cfg.usp_SearchIcd10Catalog` — *api.js: `searchIcd10`* |
| GET | `/consultations/{id}/diagnoses` | — | `[ Diagnosis ]` — `{consultationDiagnosisId, icd10Code, description, isPrimary, laterality, chronicity, sequenceOrder}`, sorted | `cln.usp_ListConsultationDiagnoses` — *api.js: `listConsultationDiagnoses`* |
| POST | `/consultations/{id}/diagnoses` | `{ hospitalId, icd10Code, icd10Description, clinicalNotes?, laterality?, acuteChronic? }` | `Diagnosis` (created; first diagnosis on a consultation defaults `isPrimary`=true) | `cln.usp_CreateDiagnosis` — *api.js: `createDiagnosis`* |
| DELETE | `/diagnoses/{id}` | — | `{ success }` | `cln.usp_DeleteDiagnosis` — *api.js: `deleteDiagnosis`*. Soft delete. |

### Prescriptions & Drug Formulary

Role: prescribing requires `consultation` feature.

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/formulary/search?term=&hospitalId=` | `term`, `hospitalId` (`_hid()` fallback) | `[ Drug ]` — `{drugFormularyId, brandName, genericName, therapeuticClass, composition, strength, drugForm, deliveryRoute, defaultDosage, defaultFrequency, defaultDuration, defaultInstructions, isControlled}` | `cfg.usp_SearchFormulary` — *api.js: `searchFormulary`*. TODO: schema lacks `targetSpecialties`/`isGeneralMedicine`/`genericClass` fields present in data.js — see stored-procedures.sql §11 note. |
| GET | `/formulary/lookup?name=` | `name` | `Drug \| null` | `cfg.usp_GetDrugByName` — *api.js: `getDrugByName`* |
| POST | `/formulary` | `{ hospitalId, brandName, genericName?, composition?, therapeuticClass?, genericClass?, drugForm?, defaultRoute?, defaultDosage?, defaultFrequency?, defaultDurationDays?, defaultInstructions?, isControlledSubstance? }` | `Drug` (created) | `cfg.usp_CreateDrug` — *api.js: `createDrug`* |
| PUT | `/formulary/{id}/status` | — (toggles current value) | `Drug` (updated) | `cfg.usp_UpdateDrugStatus` — *api.js: `updateDrugStatus`* |
| GET | `/consultations/{id}/prescriptions` | — | `[ Prescription ]`, `status`='ACTIVE' only | `cln.usp_ListPrescriptions` — *api.js: `listPrescriptions`* |
| GET | `/patients/{id}/active-medications` | — | `[ Prescription ]`, `status`='ACTIVE', across all visits | `cln.usp_ListActiveMedicationsForPatient` — *api.js: `listActiveMedicationsForPatient`* |
| POST | `/consultations/{id}/prescriptions` | `{ hospitalId, source?, patientId, sourceDetail?, medicineName, strength?, genericClass?, therapeuticClass?, drugForm?, deliveryRoute?, category?, takeInQty, timesPerDay?, durationValue?, durationUnit?, instructions?, prn?, timingBf?, timingAn?, timingNi?, timingEm?, validityDays?, repeatedFromConsultationId? }` | `Prescription` (created) | `cln.usp_CreatePrescription` — *api.js: `createPrescription`*. AUDIT: caller must also call `POST /audit/logs` (Action=`CREATE`). |
| PUT | `/prescriptions/{id}/cancel` | — | `{ success }` | `cln.usp_CancelPrescription` — *api.js: `cancelPrescription`*. Sets `status`='DISCONTINUED'. |
| GET | `/patients/{id}/drug-allergy-check?medicineName=&composition=` | `medicineName`, `composition?` | `[ { severity, allergyName, allergySeverity, message } ]` | `cln.usp_CheckDrugAllergyConflicts` — *api.js: `checkDrugAllergyConflicts`*. AUDIT: if an alert is overridden, caller must also call `POST /audit/logs` (Action=`OVERRIDE`). |

### Lab / Investigations

Role: ordering requires `consultation`; lab portal endpoints require `lab_portal` (LAB_PARTNER, ADMIN).

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/doctors/{doctorId}/lab-favorites?specialtyId=` | `specialtyId?` | `[ LabTest ]` — `{labTestCatalogId, testName, testType, defaultSampleType}` | `cln.usp_ListDoctorLabFavorites` — *api.js: `listDoctorLabFavorites`* |
| GET | `/catalog/lab-tests/search?term=&specialtyId=` | `term`, `specialtyId?` | `[ LabTest ]` | `cfg.usp_SearchLabTests` — *api.js: `searchLabTests`* |
| POST | `/catalog/lab-tests` | `{ hospitalId, testName, testType?, category?, specialtyIds? }` | `LabTest` (created) | `cfg.usp_CreateLabTest` — *api.js: `createLabTest`* |
| POST | `/doctors/{doctorId}/lab-favorites` | `{ testId }` | `Favorite` (created or existing) | `cln.usp_CreateLabFavorite` — *api.js: `createLabFavorite`* |
| DELETE | `/doctors/{doctorId}/lab-favorites/{testId}` | — | `{ success }` | `cln.usp_DeleteLabFavorite` — *api.js: `deleteLabFavorite`*. Hard delete (junction table, no soft-delete column). |
| POST | `/consultations/{id}/investigation-orders` | `{ hospitalId, patientId, testId?, testName, testType? }` | `InvestigationOrder` (created, `status`='ORDERED') | `cln.usp_CreateInvestigationOrder` — *api.js: `createInvestigationOrder`* |
| GET | `/consultations/{id}/investigation-orders` | — | `[ InvestigationOrder ]` | `cln.usp_ListInvestigationOrders` — *api.js: `listInvestigationOrders`* |
| GET | `/lab-portal/pending-orders?hospitalId=` | `hospitalId` | `[ InvestigationOrder ]` incl. `patientName`, `patientMrn`, `status`='ORDERED' | `cln.usp_ListPendingLabOrders` — *api.js: `listPendingLabOrders`* |
| PUT | `/lab-portal/orders/{id}/status` | `{ status }` | `{ success }` | `cln.usp_UpdateOrderStatus` — *api.js: `updateOrderStatus`* |
| POST | `/lab-portal/results` | `{ orderId, hospitalId, labPartnerId?, resultSummary?, resultValue?, referenceRange?, interpretation?, technician?, notes? }` | `LabResult` (created, `status`='UPLOADED'); also sets the order's `status`='COMPLETED' | `cln.usp_SubmitLabResult` — *api.js: `submitLabResult`*. AUDIT: caller must also call `POST /audit/logs` (Action=`SUBMIT`). |
| GET | `/patients/{id}/lab-results` | — | `[ LabResult ]`, newest first | `cln.usp_ListLabResultsForPatient` — *api.js: `listLabResultsForPatient`* |
| GET | `/lab-results/pending-review?hospitalId=` | `hospitalId` | `[ LabResult ]` incl. `patientName`/`patientMrn`, `status`='UPLOADED' | `cln.usp_ListPendingReviewResults` — *api.js: `listPendingReviewResults`* |
| PUT | `/lab-results/{id}/review` | `{ reviewNotes? }` | `{ success }` | `cln.usp_UpdateLabResultStatus` — *api.js: `updateLabResultStatus`*. Sets `status`='REVIEWED'. AUDIT: caller must also call `POST /audit/logs` (Action=`APPROVE`). |

### Referrals

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/consultations/{id}/referrals` | `{ hospitalId, patientId, specialty, doctorName?, urgency?, reason? }` | `Referral` (created) | `cln.usp_CreateReferral` — *api.js: `createReferral`* |
| GET | `/consultations/{id}/referrals` | — | `[ Referral ]` | `cln.usp_ListReferrals` — *api.js: `listReferrals`* |

### Sketches / Drawing Pad

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/consultations/{id}/sketches` | `{ hospitalId, patientId, doctorId, title?, imageData }` | `Sketch` (created — always inserts a new row, never updates, despite the "save" verb) | `cln.usp_SaveSketch` — *api.js: `saveSketch`* |
| GET | `/consultations/{id}/sketches` | — | `[ Sketch ]`, newest first | `cln.usp_ListSketches` — *api.js: `listSketches`* |

### Confidential Notes & Addendums

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/consultations/{id}/confidential-note?doctorId=` | `doctorId` | `ConfidentialNote \| null` — author-only: only ever returned to `createdBy`=`doctorId` | `cln.usp_GetConfidentialNote` — *api.js: `getConfidentialNote`* |
| POST | `/consultations/{id}/confidential-note` | `{ hospitalId, doctorId, noteText }` | `ConfidentialNote` (upserted, author-scoped) | `cln.usp_SaveConfidentialNote` — *api.js: `saveConfidentialNote`* |
| POST | `/consultations/{id}/addendums` | `{ hospitalId, doctorId, category?, addendumText }` | `Addendum` (created; append-only, never updated) | `cln.usp_CreateAddendum` — *api.js: `createAddendum`* |
| GET | `/consultations/{id}/addendums` | — | `[ Addendum ]`, chronological | `cln.usp_ListAddendums` — *api.js: `listAddendums`* |

---

### Services & Procedures (with optional stocked item)

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/consultations/{id}/services` | — | `[ ServiceItem ]` — `{consultationServiceId, consultationId, patientId, serviceChargeId, serviceName, category, qty, inventoryItemId, itemName, itemUnit, itemQty, notes, addedByUserId, createdAt}` | `cln.usp_ListConsultationServiceItems` |
| POST | `/consultations/{id}/services` | `{ serviceChargeId, qty?, inventoryItemId?, itemQty?, notes? }` — the item is optional and must be linked to the service (`GET /service-charges/{id}/items`); `itemQty` may not exceed what is available less what this visit already lists | `{ consultationServiceId, serviceName, qty, itemName, itemQty }` | `cln.usp_CreateConsultationServiceItem`. Stock is only checked here; it is deducted at sign-off. Errors 50030 consultation not found or signed off, 50031 not enough stock, 50032 service not found, 50033 item not used with this service. At sign-off the service and the item are billed on separate invoice lines. |
| DELETE | `/consultations/{id}/services/{serviceId}` | — | `{ success }` (before sign-off only; nothing to reverse) | `cln.usp_DeleteConsultationServiceItem` |
| GET | `/consultations/{id}/stock-shortages` | — | `[ { inventoryItemId, itemName, unit, needed, available } ]` — stocked items the visit lists more of than is in stock; empty = sign-off can proceed | `inv.usp_ListStockShortages` |

---

## 6. Billing

Role: `billing` feature (ADMIN, RECEPTIONIST, BILLING).

### Fee Schedule

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/doctors/{doctorId}/fee?type=&onDate=` | `type` (`consultationType`), `onDate?` | `FeeSchedule \| null` — `{feeScheduleId, doctorId, consultationType, feeAmount, currency, effectiveFrom, effectiveTo}` | `bil.usp_CheckEffectiveFee` — *api.js: `checkEffectiveFee`* and *api.js: `getDoctorFee`* (both call the same underlying SP) |
| GET | `/fee-schedule?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ FeeSchedule ]` | `bil.usp_ListDoctorFees` — *api.js: `listDoctorFees`* |

### Service Charge Master

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/service-charges?hospitalId=` | `hospitalId` (`_hid()` fallback) | `[ ServiceCharge ]` — `{serviceChargeId, serviceCode, serviceName, category, defaultAmount, taxPercent, currency}` | `bil.usp_ListServiceCharges` — *api.js: `listServiceCharges`* |
| GET | `/service-charges/{id}` | — | `ServiceCharge` | `bil.usp_GetServiceCharge` — *api.js: `getServiceCharge`* |
| GET | `/service-charges/search?term=&hospitalId=` | `term`, `hospitalId` (`_hid()` fallback) | `[ ServiceCharge ]` | `bil.usp_SearchServiceCharges` — *api.js: `searchServiceCharges`* |
| POST | `/service-charges` | `{ hospitalId, serviceCode, serviceName, category, defaultAmount, taxPercent }` | `ServiceCharge` (created) | `bil.usp_CreateServiceCharge` — *api.js: `createServiceCharge`* |

### Invoices

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/invoices` | `{ hospitalId, consultationId?, patientId, doctorId?, consultationType?, isFreeFollowup?, chargeNote?, currency? }` | `Invoice` (created, `status`='PENDING', server-generated `invoiceNumber`) | `bil.usp_CreateInvoice` — *api.js: `createInvoice`* |
| GET | `/consultations/{cid}/invoice` | — | `Invoice \| null`, excludes `status`='CANCELLED' | `bil.usp_GetInvoiceForConsultation` — *api.js: `getInvoiceForConsultation`* |
| GET | `/invoices?hospitalId=` | `hospitalId` | `[ Invoice ]`, newest first | `bil.usp_ListInvoices` — *api.js: `listInvoices`* |
| GET | `/invoices/{id}` | — | `Invoice` | `bil.usp_GetInvoice` — *api.js: `getInvoice`* |
| PUT | `/invoices/{id}` | `{ status?, subTotal?, discountAmount?, taxAmount?, totalAmount? }` | `Invoice` (updated) | `bil.usp_UpdateInvoice` — *api.js: `updateInvoice`* |
| POST | `/invoices/{id}/cancel` | `{ reason, issuedBy }` | `CreditNote` (created for the full invoice amount); sets invoice `status`='CANCELLED' | `bil.usp_CancelInvoice` — *api.js: `cancelInvoice`* |
| GET | `/invoices/{id}/credit-notes` | — | `[ CreditNote ]` | `bil.usp_ListCreditNotes` — *api.js: `listCreditNotes`* |

### Invoice Line Items

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/invoices/{id}/line-items` | `{ serviceChargeId?, inventoryItemId?, description, category, qty?, unitAmount, discountPercent?, taxPercent? }` — `inventoryItemId` for a stocked item billed from Clinic Inventory | `LineItem` (created; server computes `discountAmount`, `taxAmount`, `lineTotal`) | `bil.usp_CreateInvoiceLineItem` — *api.js: `createInvoiceLineItem`* |
| GET | `/invoices/{id}/line-items` | — | `[ LineItem ]`, sorted by `sortOrder` | `bil.usp_ListInvoiceLineItems` — *api.js: `listInvoiceLineItems`* |
| DELETE | `/invoices/{id}/line-items/{lid}` | — | `{ success }` | `bil.usp_DeleteInvoiceLineItem` — *api.js: `deleteInvoiceLineItem`*. Hard delete (no soft-delete column on this table). |
| POST | `/invoices/{id}/recalculate` | — | `Invoice` (updated `subTotal`/`discountAmount`/`taxAmount`/`totalAmount`, recomputed from line items) | `bil.usp_RecalculateInvoiceTotals` — *api.js: `recalculateInvoiceTotals`* |

### Payments

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/invoices/{id}/payments` | `{ hospitalId, componentType? (PATIENT\|INSURANCE), coverageLineId? (required for INSURANCE — the claim being settled), paymentMethod, amount, reference?, receivedBy? }` | `Payment` (created; recalculates the split — `paidAmount`, `patientPaidAmount`, coverage `settledAmount`, `status`) | `bil.usp_CreatePayment` — *api.js: `createPayment`* |
| GET | `/invoices/{id}/payments` | — | `[ Payment ]`, newest first | `bil.usp_ListPaymentsForInvoice` — *api.js: `listPaymentsForInvoice`* |
| GET | `/payments?hospitalId=` | `hospitalId` | `[ Payment ]`, newest first | `bil.usp_ListPayments` — *api.js: `listPayments`* |
| PUT | `/payments/{id}/void` | `{ voidReason, voidedBy }` | `{ success }` — cannot delete, only void; recalculates invoice | `bil.usp_UpdatePaymentVoid` — *api.js: `updatePaymentVoid`* |

### Receipts

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/invoices/{id}/receipt` | `{ hospitalId, patientId?, totalAmount, currency? }` | `Receipt` (created, server-generated `receiptNumber`) | `bil.usp_CreateReceipt` — *api.js: `createReceipt`*. AUDIT: caller must also call `POST /audit/logs` (Action=`PRINT`) when the receipt is printed. |
| GET | `/invoices/{id}/receipt` | — | `Receipt \| null` | `bil.usp_GetReceiptForInvoice` — *api.js: `getReceiptForInvoice`* |

### Coverage & Bill Split (insurance / govt scheme — ported from MediSlot)

`Charge (invoice totalAmount) = Σ verified coverage liabilities + Patient share`. Supports Self-Pay (no coverage), Private insurance only, Govt scheme only (CGHS / Aarogyasri, NHF, Medicare …), and Private + Govt together. Each coverage is verified per invoice (eligibility status + authorization + approved % or fixed amount); liabilities are allocated in coverage `priority` order and capped so their sum never exceeds the charge. `PERCENT` approvals follow the invoice total when charges are added/removed. The invoice becomes `PAID` once the **patient share** is settled (or everything is paid) — covered amounts are receivables from the payer, settled later with `INSURANCE` payments against the coverage line. Screen: `#/billing/checkout/:consultationId` → *Coverage & bill split* card. Role: `billing`.

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/payers?country=&category=` | `country` (hospital country), optional `category` `PRIVATE\|GOVT_SCHEME` | `[ { payerId, name, category, country } ]` | `cfg.usp_ListPayers` — *api.js: `listPayers`* |
| GET | `/patients/{id}/coverages?includeInactive=` | optional `includeInactive` | `[ Coverage ]` — `{coverageId, coverageType, payerId, payerName, planName, memberId, policyNumber, groupNumber, policyHolderName, relationship, claimMode, govtCardNumber, cardImageUrl, effectiveDate, expiryDate, status, priority, isActive, lastVerificationStatus}`; none = Self-Pay | `opd.usp_ListPatientCoverages` — *api.js: `listPatientCoverages`* |
| POST / PUT | `/patients/{id}/coverages[/{coverageId}]` | `{ coverageType (PRIVATE_INSURANCE\|GOVT_SCHEME), payerId, planName?, memberId?, policyNumber?, groupNumber?, policyHolderName?, relationship?, claimMode? (CASHLESS\|REIMBURSEMENT), govtCardNumber?, cardImage?, effectiveDate?, expiryDate?, status? }` | `Coverage` | `opd.usp_SavePatientCoverage` — *api.js: `savePatientCoverage`* |
| PUT | `/coverages/{id}/deactivate` | — | `{ success }` | `opd.usp_DeactivatePatientCoverage` — *api.js: `deactivatePatientCoverage`* |
| GET | `/coverages/{id}/verifications` | — | `[ { verificationId, invoiceId, status, authorizationNumber, approvedType, approvedValue, notes, verifiedBy, verifiedAt } ]`, oldest first | `bil.usp_ListEligibilityVerifications` — *api.js: `listEligibilityVerifications`* |
| POST | `/invoices/{id}/coverage` | `{ coverageId, status (VERIFIED\|PENDING\|REJECTED), authorizationNumber?, approvedType (PERCENT\|AMOUNT), approvedValue, notes? }` | `BillingSplit` — records the verification; `VERIFIED` applies / updates the coverage line, otherwise removes it. 409 if the invoice is cancelled or the payer already settled against the line | `bil.usp_ApplyCoverageToInvoice` — *api.js: `applyCoverageToInvoice`*. AUDIT: caller also calls `POST /audit/logs` (Action=`UPDATE`, EntityType=`Invoice`). |
| PUT | `/invoice-coverage-lines/{id}` | `{ liability }` | `BillingSplit` — manual override, still capped to the charge | `bil.usp_EditInvoiceCoverageLine` — *api.js: `editInvoiceCoverageLine`* |
| DELETE | `/invoice-coverage-lines/{id}` | — | `BillingSplit`; 409 once settled by the payer | `bil.usp_RemoveInvoiceCoverageLine` — *api.js: `removeInvoiceCoverageLine`* |
| GET | `/invoices/{id}/billing-split` | — | `BillingSplit` — `{grossAmount, insuranceLiability, govtLiability, coveredAmount, patientLiability, patientPaid, patientDue, patientOverpaid, payerOutstanding, status, lines:[{coverageLineId, coverageId, payerName, coverageType, priority, authorizationNumber, approvedType, approvedValue, isManualAmount, liability, settledAmount}]}` | `bil.usp_GetInvoiceBillingSplit` — *api.js: `getInvoiceBillingSplit`* |
| — | (internal) | — | Re-derives liabilities, patient share, paid amounts and status; run by line-item recalculation, payments, voids and every coverage change | `bil.usp_RecalculateInvoiceSplit` |

---

## 7. Reports

Role: `reports` feature (ADMIN, DOCTOR, BILLING).

| Verb | Endpoint | Params | Returns | SP / View |
|------|----------|--------|---------|-----------|
| GET | `/reports/consultation-volume?hospitalId=&fromDate=&toDate=` | `hospitalId`, `fromDate`, `toDate` | `[ { date, consultationCount } ]` | `cln.usp_ListConsultationVolumeReport` — *api.js: `listConsultationVolumeReport`* |
| GET | `/reports/diagnosis-frequency?hospitalId=&fromDate=&toDate=` | as above | `[ { icd10Code, description, diagnosisCount } ]`, ranked desc | `cln.usp_ListDiagnosisFrequencyReport` — *api.js: `listDiagnosisFrequencyReport`* |
| GET | `/reports/prescribing-patterns?hospitalId=&fromDate=&toDate=` | as above | `[ { medicineName, prescriptionCount } ]`, ranked desc | `cln.usp_ListPrescribingReport` — *api.js: `listPrescribingReport`* |
| GET | `/reports/no-show-rate?hospitalId=&fromDate=&toDate=` | as above | `{ total, noShows, noShowRate }` + `[ { status, statusCount } ]` | `opd.usp_ListNoShowReport` — *api.js: `listNoShowReport`* |
| GET | `/reports/revenue?hospitalId=&fromDate=&toDate=` | as above | `[ { doctorId, invoiceCount, totalInvoiced, totalPaid } ]` | `bil.usp_ListRevenueReport` — *api.js: `listRevenueReport`* |
| GET | `/reports/revenue-by-service?hospitalId=&fromDate=&toDate=` | as above | `[ { category, totalRevenue, invoiceCount, invoiceDate } ]` | `bil.vw_RevenueByServiceCategory` (view) |
| GET | `/reports/day-end?hospitalId=&date=` | `hospitalId`, `date` | `{ invoiceCount, totalBilled, totalCollected, totalOutstanding }` + `[ { componentType, paymentMethod, totalAmount, paymentCount } ]` | `bil.vw_DayEndSummary` + `bil.vw_PaymentsByComponent` (views) |

---

## 8. Compliance

Role: `admin_hospital` feature (ADMIN).

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/compliances?country=` | `country` | `[ Compliance ]` — `{complianceId, complianceName, country, isMandatory, displayLabel, printLabel, validationRegex, sortOrder}`, includes country='ALL' rows | `cfg.usp_ListCompliances` — *api.js: `listCompliances`* |
| GET | `/hospital-compliances?hospitalId=` | `hospitalId` | `[ HospitalCompliance ]` — `{hospitalComplianceId, hospitalId, complianceId, complianceName, displayLabel, printLabel, isMandatory, registrationNumber, issuedOn, expiresOn}` | `cfg.usp_ListHospitalCompliances` — *api.js: `listHospitalCompliances`* |
| PUT | `/hospital-compliances` | `{ hospitalId, complianceId, registrationNumber, issuedDate?, expiryDate? }` | `HospitalCompliance` (upserted) | `cfg.usp_SaveHospitalCompliance` — *api.js: `saveHospitalCompliance`* |

---

## 9. Notifications / Messaging

Role: `admin_hospital` for config/templates (ADMIN); queue/log used by the NotificationEngine.

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/notifications/templates?hospitalId=` | `hospitalId` | `[ MessageTemplate ]` — `{messageTemplateId, hospitalId, name, triggerEvent, channel, templateText, isEnabled}` | `cfg.usp_ListMessageTemplates` — *api.js: `listMessageTemplates`* |
| PUT | `/notifications/templates/{id}/status` | — (toggles current value) | `{ success }` | `cfg.usp_UpdateMessageTemplateStatus` — *api.js: `updateMessageTemplateStatus`* |
| PATCH | `/notifications/templates/{id}` | `{ name?, trigger?, channel?, text?, isEnabled? }` — partial update, only supplied fields change | `MessageTemplate` (updated) | `cfg.usp_UpdateMessageTemplate` — *api.js: `updateMessageTemplate`*. Distinct from the status-only toggle above. |
| GET | `/notifications/config?hospitalId=` | `hospitalId` | `[ { channel, provider, configJson, isEnabled } ]` — `apiKey` never returned (ENCRYPTION: RANDOMIZED, write-only) | `msg.usp_GetNotificationConfig` — *api.js: `getNotificationConfig`* |
| PUT | `/notifications/config/{channel}` | `{ hospitalId, provider?, apiKey?, phoneNumberId?, businessAccountId?, smtpHost?, smtpPort?, fromEmail?, fromName?, enabled? }` | `{ success }` (upserted) | `msg.usp_SaveNotificationConfig` — *api.js: `saveNotificationConfig`* |
| POST | `/notifications/queue` | `{ hospitalId, patientId?, channel, templateName?, patientName?, patientPhone?, messageText? }` | `Message` (created; `status`='QUEUED' if channel enabled, else 'SIMULATED') | `msg.usp_CreateQueuedMessage` — *api.js: `createQueuedMessage`* |
| POST | `/notifications/send` | `{ hospitalId }` | `{ success }` — batch-transitions QUEUED/SIMULATED rows to SENT | `msg.usp_SubmitQueuedMessages` — *api.js: `submitQueuedMessages`* |
| GET | `/notifications/log?hospitalId=` | `hospitalId` | `[ Message ]`, newest first | `msg.usp_ListMessageLog` — *api.js: `listMessageLog`* |
| GET | `/notifications/stats?hospitalId=` | `hospitalId` | `{ total, sent, simulated, failed }` | `msg.usp_GetMessageStats` — *api.js: `getMessageStats`* |

---

## 10. Audit Log & Data Export

Role: `audit_log` / `data_export` features (ADMIN only).

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| POST | `/audit/logs` | `{ hospitalId, userId?, action, entityType, entityId?, details?, ipAddress?, userAgent? }` | `AuditEntry` (created; append-only) | `sec.usp_RecordAuditEntry` — *api.js: `recordAuditEntry`* |
| GET | `/audit/logs?hospitalId=` | `hospitalId` | `[ AuditEntry ]`, newest first | `sec.usp_ListAuditLogs` — *api.js: `listAuditLogs`* |
| GET | `/audit/logs/filtered?hospitalId=&action=&entityType=&userId=&fromDate=&toDate=&top=200` | filters, `top` default 200 | `[ AuditEntry ]`, newest first | `sec.usp_ListAuditLogsFiltered` — *api.js: `listAuditLogsFiltered`* |
| GET | `/export/tables?hospitalId=` | `hospitalId` | `[ { tableKey, rowCount } ]` for the allow-listed exportable tables | `sec.usp_ListExportTables` — *api.js: `listExportTables`* |
| GET | `/export/{tableKey}?hospitalId=&format=csv` | `hospitalId`; `tableKey` one of `patients\|appointments\|consultations\|prescriptions\|invoices\|paymentTransactions\|receipts\|auditLogs` | CSV file download (header row + quoted values, server-serialized) | `sec.usp_ListExportData_{TableKey}` — *api.js: `listExportData`, `exportTableCsv`*. One fixed procedure per allow-listed table (no dynamic SQL against a client-supplied table name — see architecture §8 rule 5). `exportTableCsv(tableName)` is a same-behavior alias of `listExportData(tableName)` added on the client during a bug-fix pass (a Settings-Hub CSV-export button was calling a method name that didn't exist yet) — both names route to the same endpoint/procedure, no separate SP. AUDIT: caller must also call `POST /audit/logs` (Action=`EXPORT`, Details=`tableKey`, never the exported rows themselves). |

---

## 11. Clinic Inventory (`admin_inventory`, ADMIN by default)

Stock the clinic uses during a visit, kept apart from the Drug Formulary. Stock is held per
batch (batch number + expiry); a batch is usable through its expiry date (the hospital's
calendar date) and used earliest-expiry first. Every change is a row in the stock ledger.

| Verb | Endpoint | Body / Params | Returns | SP |
|------|----------|---------------|---------|-----|
| GET | `/inventory/items?hospitalId=&includeInactive=` | `hospitalId`, optional `includeInactive` | `[ InventoryItem ]` — `{inventoryItemId, itemCode, itemName, category, unit, unitPrice, taxPercent, reorderLevel, isActive, available, expired, nextExpiry}`; `available` counts unexpired batches only | `inv.usp_ListInventoryItems` |
| GET | `/inventory/items/{id}` | — | `InventoryItem` — `{inventoryItemId, hospitalId, itemCode, itemName, category, unit, unitPrice, taxPercent, reorderLevel, isActive}` | `inv.usp_GetInventoryItem` |
| POST / PUT | `/inventory/items` · `/inventory/items/{id}` | `{ hospitalId, itemCode, itemName, category, unit, unitPrice, taxPercent?, reorderLevel? }` — `category` MEDICATION / INJECTION / CONSUMABLE / OTHER; `itemCode` unique per hospital | `{ inventoryItemId, itemName }` | `inv.usp_SaveInventoryItem` (inserts when `@InventoryItemId` is null) |
| PUT | `/inventory/items/{id}/active` | `{ isActive }` | `{ inventoryItemId, isActive }` | `inv.usp_UpdateInventoryItemActive` |
| GET | `/inventory/items/{id}/services` | — | `[ { serviceChargeId, serviceName } ]` — the services the item is used with | `inv.usp_ListItemServices` |
| PUT | `/inventory/items/{id}/services` | `{ serviceChargeIds: [ … ] }` (sent comma-separated as `@ServiceChargeIds`; replaces the list; empty = linked to nothing) | same as GET | `inv.usp_SaveItemServices` |
| GET | `/service-charges/{id}/items?hospitalId=` | `hospitalId` | `[ { inventoryItemId, itemName, category, unit, available } ]` — active items linked to the service: the Item field in Services & Procedures | `inv.usp_ListItemsForService` |
| GET | `/inventory/items/{id}/batches?includeEmpty=` | optional `includeEmpty` | `[ StockBatch ]` — `{stockBatchId, inventoryItemId, batchNo, expiryDate, qtyReceived, qtyOnHand, unitCost, supplier, receivedByUserId, receivedAt, isExpired}`, earliest expiry first | `inv.usp_ListStockBatches` |
| POST | `/inventory/items/{id}/receipts` | `{ hospitalId, batchNo, expiryDate, qty, unitCost?, supplier? }` — a batch number already on file is topped up (same expiry only) | `[ StockBatch ]` (the item's batches) | `inv.usp_RecordStockReceipt`. Errors 50020 qty < 1, 50021 item not found, 50022 batch on file with another expiry |
| POST | `/inventory/batches/{id}/adjustments` | `{ delta, reason }` — signed quantity (negative for expiry write-off, damage, count short) | `{ success }` | `inv.usp_RecordStockAdjustment`. Errors 50023 delta 0, 50024 would go below zero |
| GET | `/inventory/items/{id}/movements?take=` | optional `take` (default 50) | `[ StockMovement ]` — `{stockMovementId, movementType, qty, reason, consultationId, userId, createdAt, stockBatchId, batchNo}`, newest first; `movementType` RECEIPT / CONSUMPTION / ADJUSTMENT, `qty` signed | `inv.usp_ListStockMovements` |
| — | *(internal, not an endpoint)* | — | Deducts a consultation's stocked items earliest-expiry first, all or nothing; error 50040 lists the shortages; a second call is a no-op | `inv.usp_RecordStockConsumption` — called by `cln.usp_ConfirmSignOff` |

---

## Endpoint Count Summary

| Section | Endpoints |
|---------|-----------|
| 1. Authentication, Sessions & Users | 11 |
| 2. Hospitals & Specialties | 8 |
| 3. Patients + Sub-resources | 26 |
| 4. Scheduling — Appointments & Slots | 30 |
| 5. Consultations — Core | 60 |
| 6. Billing | 32 |
| 7. Reports | 7 |
| 8. Compliance | 3 |
| 9. Notifications / Messaging | 9 |
| 10. Audit Log & Data Export | 5 |
| 11. Clinic Inventory (`admin_inventory`, ADMIN by default) | 11 |
| **Total** | **202** |

*(Some `api.js` methods share one endpoint/procedure — e.g. `checkEffectiveFee`
and `getDoctorFee` both resolve to `GET /doctors/{doctorId}/fee`; `listAllergies`-
style duplicate method definitions in `api.js` collapse to their single
last-defined signature per the project's "later definition wins" rule;
`exportTableCsv`/`listExportData` share one endpoint. All 150 current
`api.js` methods are accounted for above or in `docs/stored-procedures.sql`'s
method→SP list.)*

---

## Known Gaps / TODOs

The five schema-vs-data.js gaps listed in v2.1 (allergy `INACTIVE` status, per-hospital
symptom and lab-test catalogs, formulary `GenericClass`, medical-history `ItemId`) were
resolved by schema v4.0 and its procedures; v2.3 added the two missing procedures
(`sec.usp_SaveDoctorProfile`, `cfg.usp_UpdateHospital`). Notes:

1. **Hard deletes** (`bil.usp_DeleteInvoiceLineItem`, `cln.usp_DeleteLabFavorite`,
   `cln.usp_DeleteConsultationServiceItem` before sign-off) are intentional: those rows have no
   `IsActive` column, or nothing has happened to them yet.

Every procedure in `stored-procedures.sql` is listed above, except the internal helpers (`cfg.fn_*`, `bil.usp_RecalculateInvoiceSplit`, `inv.usp_RecordStockConsumption`, noted where used).

---

*Version 2.3 — checked against docs/stored-procedures.sql v3.3 (212 procedures, 3 views, 4 functions)*
*and docs/schema.sql (80 tables: 75 application + 5 reserved). Supersedes v2.1 (136 endpoints / 157 SPs).*
