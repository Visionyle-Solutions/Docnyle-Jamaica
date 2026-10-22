# DocNyle — OPD Consultation Module — Product Guide

## What DocNyle Is

DocNyle is an outpatient (OPD) consultation and small-clinic management
application for practices with one to a few doctors. It covers the full
visit lifecycle — patient registration and search, the doctor's daily
queue, a structured consultation workspace (vitals, complaints, diagnosis,
SOAP notes, prescriptions, lab orders, referrals, follow-up, sign-off),
scheduling, billing, lab result exchange with external lab partners, and
hospital-level administration (users, permissions, formulary, phrase
library, compliance registrations).

It is built as a browser-based single-page application (HTML/CSS/vanilla
JavaScript) that currently runs entirely against an in-memory data layer
(`js/data.js`). This is Phase 1 of a four-phase plan; see "Architecture
and Migration Path" below.

The product is explicitly designed for multi-country deployment: the two
seeded demo hospitals are in India and Jamaica, and hospital-level
settings (currency, patient ID label, national health ID requirement,
growth chart standard, payment methods, compliance documents) drive
country-specific behavior throughout the app rather than being
hard-coded.

## Target Users and Roles

Access is controlled by a per-hospital role → feature permission matrix
(`permissionMatrix` in `js/data.js`, enforced in `js/router.js` via
`ROUTE_PERMS`, and editable by an Administrator at **Settings → Users &
Access → Permissions**, `#/settings/permissions`). Six roles exist today:

| Role | Who they are | What they can do |
|---|---|---|
| **ADMIN** | Practice owner / office administrator | Full access to every feature: consultation, patients, scheduling, billing, reports, all Settings tabs (hospital profile, vitals config, specialty config, drug formulary, phrase library, user management), audit log, CSV data export/import, and printing. |
| **DOCTOR** | Consulting physician | Consultation workspace, patient records, scheduling, reports, and their own phrase library. Cannot see billing, cannot administer other users, hospital settings, formulary, audit log, or data export/import. |
| **RECEPTIONIST** | Front desk | Patient registration/search, scheduling, and billing (check-in and payment collection), but no access to the clinical consultation workspace or reports. |
| **BILLING** | Billing/accounts staff | Patient records, billing (invoices, balances, day-end reconciliation), reports, and printing. No scheduling or consultation access. |
| **NURSE** | Clinical support staff | Patient records and scheduling (including the Nurse Vitals pre-entry screen). No billing, consultation, or reporting access. |
| **LAB_PARTNER** | External diagnostic lab or imaging center | Only the Lab Portal — sees pending orders for their hospital and uploads results. No other module is visible. |

The sidebar itself is permission-filtered: a navigation group (or an item
inside it) only renders if the signed-in role is granted the underlying
feature flag, so what a role "can't do" also means they never see the
menu item at all, not just that the page would refuse them.

Two demo hospitals are seeded to exercise multi-tenant, country-specific
configuration:

- **Sunrise Multispecialty Clinic** (`HOSP-001`) — Vijayawada, India.
  Currency INR, patient ID label "MRN", National Health ID optional,
  WHO growth charts. This is the hospital the application signs into by
  default and the only one with a full roster of seeded staff and
  patients.
- **Kingston Family Health Centre** (`HOSP-002`) — Kingston, Jamaica.
  Currency JMD, patient ID label "Patient ID", National Health ID
  **mandatory**, CDC growth charts. Seeded with one doctor account
  (Dr. K. Bailey) to demonstrate the country-driven configuration
  differences; it has no seeded patients or appointments of its own.

## Core Workflows

### 1. Patient registration and search

**Search / Register** (`#/patients/search`) lets front-desk or clinical
staff search existing patients by name or MRN, or register a new patient.
Registration requires full name, gender, date of birth, and phone; the
National Health ID field becomes mandatory or optional based on the
signed-in hospital's configuration (mandatory at Kingston Family Health
Centre, optional at Sunrise). Optional fields cover address, smoking/
alcohol/occupation (social history), blood group, emergency contact, and
insurance details.

### 2. The doctor's daily queue

**Today's Patients** (`#/patients`) is not a flat patient roster — it is
the signed-in doctor's actual appointment queue for the current day, with
each row's status (Booked, Checked-in, In Consult, Completed, Cancelled,
No-show) driving how it's presented. From here the doctor opens a patient
directly into either the consultation workspace or the allergies screen.

### 3. Consultation 360°

The consultation workspace (`#/consultation/patient/:patientId`) is the
clinical core of the product. It is organized as an accordion of
workflow steps, each with a completion state (pending / active /
completed / skipped) shown on a horizontal progress rail:

1. **Vitals** — temperature, BP (systolic/diastolic), pulse, SpO2,
   height, weight, respiratory rate, and a 0–10 emoji-scored pain scale,
   each validated against a clinically sane range (e.g. temperature
   90–110°F, pulse 20–250 bpm, SpO2 0–100%) with a check that systolic
   BP exceeds diastolic. Height and weight entry live-calculates BMI,
   BSA, and mean arterial pressure (MAP).
2. **Complaints** — a tri-state (unset / yes / no) chip picker against a
   hospital-configurable symptom catalog, with a free-text "add custom
   complaint" option.
3. **Diagnosis** — ICD-10 search-and-confirm. Each confirmed diagnosis
   is a merged record: the resolved ICD-10 code plus the doctor's own
   clinical reasoning (laterality, acute/chronic, clinical notes) saved
   together, not just a bare code.
4. **Observations (SOAP)** — free-text Subjective / Objective / Plan
   notes with a specialty phrase library (type `/` to trigger an
   autocomplete of saved phrases), an **auto-generate SOAP draft**
   button that composes a starting note from the visit's vitals,
   complaints, diagnoses, medications, and lab orders for the doctor to
   review and edit before accepting, hands-free **voice dictation** via
   the browser's Speech Recognition API (routes spoken "Subjective" /
   "Objective" / "Plan" to the right field), and a sketch/drawing pad
   for freehand diagrams saved as JPEG attachments to the visit.
5. **Labs/Imaging** — search the lab/imaging test catalog (or add a
   custom test on the fly), order with a priority, and maintain a
   per-doctor favorites shortlist.
6. **Prescriptions (CPOE)** — formulary search or fully manual entry,
   dose/frequency/timing (before food / after food / at night / early
   morning), duration with auto-calculated period end, a live current-
   medication table, "repeat previous prescription" from an earlier
   visit, and discontinuation with confirmation. Before a prescription
   is saved the system runs a **drug–allergy conflict check** against
   the patient's recorded allergies and a **medication reconciliation**
   check (dosage delta vs. the same drug's last dose, therapeutic-class
   duplicates, unintentional overlap with an already-active
   medication); either can be overridden but only with a typed reason
   that is written to the audit trail.
7. **Referral** — specialty, referred-to doctor, reason, and urgency.
8. **Next Visit (Follow-up)** — a follow-up date and notes; saving it
   automatically books the next available appointment slot for that
   date if one exists.

Two features sit outside the numbered workflow:

- **Confidential Notes** — free text visible only to the authoring
  doctor. The content is explicitly exempt from audit logging; only the
  act of writing a note is recorded, not what was written. The field
  remains editable even after the consultation is locked.
- **Sign-off** — a four-point attestation (Subjective / Objective /
  Assessment / Plan each individually checked) that must be completed
  before the "Confirm sign-off" button is enabled. Signing off runs the
  **Contradiction Engine** first and shows any flagged issues for the
  doctor to review (sign-off can still proceed, but flagged
  contradictions are written to the audit trail as acknowledged), locks
  the consultation record, blocks every further mutating action ("this
  record was signed and locked — corrections require an addendum"), and
  auto-generates a billing invoice for the visit (consultation fee plus
  any ordered lab/imaging line items) if one doesn't already exist.
  After lock, corrections are made through a categorized addendum
  (Typo/Clarification, Rx Change, New Lab Data, Clinical Note) appended
  to the record rather than by editing the original.

Throughout the workspace, a pinned **safety strip** shows the patient's
known allergies (color-coded red if any is severe) and active medication
count, and a context panel summarizes visit history, medical history,
recent labs, current medications, medical events, and family/social
history — each expandable for detail without leaving the consultation.

### 4. Scheduling

**Appointments** (`#/scheduling/appointments`) is a per-doctor daily
schedule with booking, rescheduling, and recurring-appointment creation.
**Queue Display** (`#/queue-display`, no login required — intended for a
waiting-room TV) shows the live token queue across all doctors at the
hospital. **Nurse Vitals** (`#/nurse/vitals`) lets nursing staff pre-enter
vitals ahead of the doctor's consultation.

### 5. Billing

Checkout (`#/billing/checkout/:consultationId`) is an itemized invoice
built from the hospital's service charge master (consultation fee, lab/
imaging tests, procedures), with per-line discount and tax, payment
capture against the hospital's configured payment methods (cash, credit
card, UPI for India; cash, credit card, direct insurance for Jamaica),
and invoice cancellation via credit note. **Outstanding Balances**
(`#/billing/balances`) rolls up unpaid amounts per patient, and
**Day-End Closure** (`#/billing/day-end`) reconciles the day's cash
collection.

### 6. Lab results

External lab and imaging partners (the `LAB_PARTNER` role) use the
**Lab Portal** (`#/lab-portal`) to see pending orders placed against
their hospital and upload results (value, reference range, technician,
notes). Doctors review incoming results through **Lab Results**
(`#/lab-results`), sourced from the same order records raised during a
consultation.

### 7. Reports

The **Reports** dashboard (`#/reports`) covers five report types over a
selectable date range: daily consultation volume, ICD-10 diagnosis
frequency, prescribing patterns, appointment no-show rate, and revenue
by doctor.

### 8. Administration

**Settings** (`#/settings`) is a single hub with six tabs: Hospital &
Practice (profile, address, compliance registrations such as GSTIN or
MOH license), Clinical Config (vitals definitions, specialty list,
medical-history quick-picks), Billing & Numbers (service charge master,
fee schedule), Notifications (WhatsApp/email provider configuration and
message templates), Users & Access (staff accounts, role assignment),
and System & Data (CSV export of eight data domains, CSV import of
patients, and a link to the audit log). **Permissions**
(`#/settings/permissions`) is the role → feature grid described above.
**Drug Formulary** (`#/settings/formulary`) and **Phrase Library**
(`#/settings/phrases`) manage the prescribing catalog and the SOAP
quick-phrase bank respectively. The **Audit Log**
(`#/audit-log`) records logins/logouts, every create/update/delete on
clinical and administrative records, safety-check overrides, and
contradiction acknowledgements, filterable by action, entity type, and
date range.

## The Clinical Intelligence Engines

Five standalone engines (`js/engines/`) supply decision support inside
the consultation workspace. Each is a pure function the consultation
module calls and then renders the result of — none of them block the
doctor outright; each surfaces information the doctor reviews and can
override with a documented reason.

- **Branched Flow Engine** — adapts the UI to what's actually
  presented: a "fever" complaint highlights the temperature vital and
  suggests infectious-disease labs and ICD-10 codes; demographic and
  specialty context (age, gender, specialty, country) can similarly
  surface or hide relevant fields.
- **Contradiction Engine** — a background reviewer that checks for
  internally inconsistent clinical data before sign-off: vitals that
  conflict with a recorded diagnosis (e.g. a tachycardic pulse alongside
  a bradycardia diagnosis, a hypotensive systolic BP alongside a
  hypertension diagnosis), laterality mismatches, and pediatric
  weight-based dose concerns.
- **Medication Reconciliation Engine (Rx Reconciler)** — on every new
  prescription, compares it against the patient's active medications and
  what's already been prescribed in the current visit for dosage
  changes on the same drug, therapeutic-class duplicates, and
  unintentional repeat orders of an already-active medication.
- **SOAP Composer** — aggregates the visit's vitals, complaints,
  diagnoses, medications, and lab orders into a structured draft SOAP
  note (Subjective/Objective/Assessment/Plan) for the doctor to edit
  before accepting — never auto-saved without review.
- **Voice SOAP** — hands-free dictation into the SOAP text fields using
  the browser's native Web Speech Recognition API, with spoken section
  names ("Subjective", "Objective", "Plan") routing the transcript to
  the matching field. Requires a browser that implements the Web Speech
  API (Chrome or Edge); the app detects and reports when it isn't
  available rather than failing silently.

## Multi-Tenancy and Hospital Model

Every clinical and administrative record carries a `HospitalId`, and
nearly every hospital-level configuration item — currency, patient ID
labeling, National Health ID requirement, growth chart standard,
default prescription validity, payment methods, compliance document
types, vitals/symptom/lab catalogs, service charges, and doctor fee
schedules — is scoped per hospital rather than hard-coded, so the same
codebase serves clinics with materially different regulatory and billing
environments (demonstrated here with India and Jamaica). Cross-hospital
data isolation exists as a guard function in the data layer today, but
it is a no-op unless a caller's hospital ID is explicitly set — full
row-level enforcement is a Phase 4 (SQL Server) commitment, described
below.

## Architecture and Migration Path

DocNyle is deliberately built as what its own documentation calls "a
running specification" for a future server-backed system, in four
phases:

- **Phase 1 (current)** — the entire application is static
  HTML/CSS/JavaScript served from any static file server. `js/data.js`
  holds all data as in-memory arrays seeded on load; every array is
  designed to map 1:1 to a named table in the SQL Server schema
  (`docs/schema.sql`) that the later phases will use. `js/api.js` wraps
  every data operation behind an async method following a fixed
  14-verb naming convention (`get`, `list`, `search`, `check`, `create`,
  `save`, `update`, `delete`, `submit`, `confirm`, `cancel`, `approve`,
  `reject`, `record`), so each method's future HTTP verb and endpoint is
  already implied by its name (documented in full in
  `docs/API-SPECIFICATION.md`).
- **Current** — the application has since been ported to ASP.NET Web
  Forms (.NET Framework 4.8); the in-memory data now lives in
  `Services/DataStore.cs` behind the C# service classes.
- **Phase 4 (target)** — the DOCNyleUnifiedAPI WCF service backed by SQL
  Server 2019, with each stored procedure becoming an operation of the same
  name on its one contract (`IDOCNyleUnifiedAPI`) and the Web Forms pages
  unchanged (`docs/API-SPECIFICATION.md`, `docs/WCF-MIGRATION-GUIDE.md`) —
  the intent is that migrating requires no change to method signatures,
  naming, or behavioral contracts, only a change in what sits behind them.
  Stored procedures for the full data layer already exist in
  `docs/stored-procedures.sql`.

Because data lives only in memory in Phase 1, nothing entered in the
application survives a page reload — this is expected today, not a
defect, and is the reason a demo session always starts from the same
seeded patients and appointments.

## Current Limitations

These are current-build gaps visible directly in the code, not
speculation about what might be added later:

- **No persistence.** All data is in-memory JavaScript arrays; a page
  reload resets every hospital back to its seeded state. There is no
  database behind Phase 1.
- **Notification channels are configured but not connected.** The
  Settings → Notifications tab lets an administrator enter WhatsApp and
  email provider credentials and edit message templates, but the
  underlying provider configuration ships disabled by default with no
  API key set — messages are queued/rendered against templates, not
  actually delivered to a real WhatsApp or SMTP provider.
- **CSV import supports one table.** The Data Import tool (Settings →
  System & Data, and `#/data-import`) is built generically but only has
  a "Patients" table wired up today; other domains are export-only.
  Note that this bulk CSV path is a separate feature from the doctor's
  in-visit patient registration form, which is unaffected by this
  limitation.
- **The ICD-10 catalog is a small demo set.** Diagnosis search runs
  against a hand-typed list of six codes for demonstration purposes; a
  production deployment needs a full bulk ICD-10 import, which the
  schema anticipates but Phase 1 does not ship.
- **Cross-hospital data isolation is not yet enforced.** The data
  layer's hospital-access guard exists but is a pass-through unless a
  caller's hospital ID is supplied; hard row-level tenant isolation is
  part of the Phase 4 SQL Server migration, not the current build.
- **Session enforcement is same-browser-only.** Login sessions are
  tracked via `localStorage` (single active session per user, per
  browser, 15-minute inactivity timeout) rather than a server-issued,
  cross-device session — this is documented in the code as an explicit
  Phase 1 stand-in for the server-side session table planned for
  Phase 4.
