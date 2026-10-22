# DocNyle — OPD Consultation Module — Testing Guide

No build-version string was found anywhere in the running application
(`index.html`, `js/layout.js` topbar, or elsewhere in `js/`). This guide
does not reference a build number for that reason — recommend adding a
visible version indicator (e.g. in the topbar, alongside "DocNyle") so
future test runs can confirm which build they're on.

This guide is organized to match the real sidebar navigation
(`js/layout.js`) and the routes actually registered in `js/router.js` /
`js/app.js`. All test data (patient names, IDs, credentials, validation
ranges) is taken directly from `js/data.js` and the relevant module
source — verify against those files if the seeded data ever changes.

---

## 1. Environment Setup

1. From the project root, start a static file server. The authoritative
   launcher is `START-SERVER.sh` (or `START-SERVER.bat` on Windows),
   which serves on **port 8095**:
   ```bash
   ./START-SERVER.sh
   ```
   Then open `http://localhost:8095`.
   (`README.md` separately suggests `python3 -m http.server 8080` — if
   you use that instead, open port 8080 accordingly. The two docs
   disagree on port; `START-SERVER.sh`/`.bat` is the actual shipped
   launcher and is the one to trust.)
2. Opening `index.html` directly via `file://` will **not** work — the
   app fetches HTML templates with `fetch()`, which is blocked by CORS
   for `file://` URLs. A static server is required.
3. The app boots already signed in as the seeded demo doctor (Dr. A.
   Sharma, `HOSP-001`) — `js/app-context.js` hardcodes this as the
   default in-memory session, and `js/app.js`'s `boot()` also creates a
   matching real `SessionManager` session for that same demo doctor
   (via `API.authenticateUser` + `SessionManager.createSession()`,
   exactly the call shape the real login form uses) before its initial
   `Router.go(...)`. `js/app.js` then navigates straight to the Patient
   Allergies screen for `PAT-001`. No login step is required to reach
   this default state, and `SessionManager.isValid()` is already `true`
   by the time routing happens, so `js/router.js`'s session guard
   (which blocks every route dispatch except `#/login`,
   `#/portal/register`, `#/portal/book`, and `#/queue-display` behind
   `SessionManager.isValid()`) does not block it. If a session is ever
   genuinely invalid or expired when a protected route is requested
   (e.g. after `SessionManager` times out an inactive session), the
   router now redirects to `#/login` instead of leaving `#main-content`
   blank.
4. To exercise the actual login screen, navigate to `#/login` directly.

## 2. Login Credentials (seeded users, `js/data.js`)

All seeded users belong to `HOSP-001` (Sunrise Multispecialty Clinic,
India) except Dr. Bailey, who belongs to `HOSP-002` (Kingston Family
Health Centre, Jamaica). The login form pre-fills the doctor credentials
by default.

| Role | Name | Email | Password |
|---|---|---|---|
| DOCTOR | Dr. A. Sharma | `drsharma@sunrise.in` | `demo123` |
| DOCTOR — General Medicine (MBBS), full-time | Dr. Ravi Teja Varma | `ravi.varma@sunrise.in` | `demo123` |
| DOCTOR — General Medicine (MBBS), full-time | Dr. Lakshmi Prasanna | `lakshmi.prasanna@sunrise.in` | `demo123` |
| DOCTOR — Cardiology, visiting (10:00–14:00, 20-min slots) | Dr. Suresh Reddy | `suresh.reddy@sunrise.in` | `demo123` |
| DOCTOR — Pediatrics, visiting (15:00–18:00, 20-min slots) | Dr. Kavitha Rao | `kavitha.rao@sunrise.in` | `demo123` |
| RECEPTIONIST | Priya Nair | `priya@sunrise.in` | `demo123` |
| DOCTOR (HOSP-002) | Dr. K. Bailey | `k.bailey@kingstonfhc.example` | `demo123` |
| ADMIN | Admin User | `admin@sunrise.in` | `admin123` |
| BILLING | Billing Staff | `billing@sunrise.in` | `demo123` |
| LAB_PARTNER | Sunrise Diagnostics Lab | `lab@sunrisediag.in` | `lab123` |
| LAB_PARTNER | City Imaging Center | `imaging@cityimaging.in` | `lab123` |

On successful login, `LAB_PARTNER` accounts are routed to `#/lab-portal`;
every other role is routed to `#/patients`.

## 3. Role-Based Navigation Matrix

Derived from `permissionMatrix` in `js/data.js` and the `perm` keys on
each sidebar item in `js/layout.js`. A sidebar group only renders if at
least one item inside it is permitted; an item only renders if its
specific `perm` is granted.

| Sidebar item | Route | Required permission | ADMIN | DOCTOR | RECEPTIONIST | BILLING | NURSE | LAB_PARTNER |
|---|---|---|:-:|:-:|:-:|:-:|:-:|:-:|
| New Consultation | `#/patients/search` | `consultation` | ✓ | ✓ | – | – | – | – |
| Lab Results | `#/lab-results` | `consultation` | ✓ | ✓ | – | – | – | – |
| Today's Patients | `#/patients` | `patients` | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Search / Register | `#/patients/search` | `patients` | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Appointments | `#/scheduling/appointments` | `scheduling` | ✓ | ✓ | ✓ | – | ✓ | – |
| Queue Display | `#/queue-display` | `scheduling` | ✓ | ✓ | ✓ | – | ✓ | – |
| Nurse Vitals | `#/nurse/vitals` | `patients` | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Schedule Settings | `#/settings/scheduling` | `admin_scheduling` | ✓ | ✓ | – | – | – | – |
| Invoices | `#/billing/invoices` | `billing` | ✓ | – | ✓ | ✓ | – | – |
| Outstanding Balances | `#/billing/balances` | `billing` | ✓ | – | ✓ | ✓ | – | – |
| Day-End Closure | `#/billing/day-end` | `billing` | ✓ | – | ✓ | ✓ | – | – |
| Reports | `#/reports` | `reports` | ✓ | ✓ | – | ✓ | – | – |
| Message Log | `#/communications` | `scheduling` | ✓ | ✓ | ✓ | – | ✓ | – |
| Lab Portal | `#/lab-portal` | `lab_portal` | ✓ | – | – | – | – | ✓ |
| Drug Formulary | `#/settings/formulary` | `admin_formulary` | ✓ | – | – | – | – | – |
| Phrase Library | `#/settings/phrases` | `admin_phrases` | ✓ | ✓ | – | – | – | – |
| Settings | `#/settings` | `admin_hospital` | ✓ | – | – | – | – | – |
| Permissions | `#/settings/permissions` | `admin_users` | ✓ | – | – | – | – | – |

Additional route-level guards not tied to a sidebar item (from
`ROUTE_PERMS` in `js/router.js`): `#/settings/users` and
`#/settings/vitals`/`#/settings/specialties` (redirect into the Settings
hub) require `admin_users`/`admin_vitals`/`admin_specialty`
respectively; `#/audit-log` requires `audit_log`; `#/data-export` and
`#/data-import` require `data_export`/`data_import` — all four of these
are ADMIN-only under the seeded permission matrix. `#/portal` (patient
self-service, if reached without a sub-path) is public — no permission
required.

**Test 3.1 — Access denial renders correctly.** Sign in as
`priya@sunrise.in` (RECEPTIONIST) and navigate directly to
`#/settings/permissions`. Expect an "Access Denied" card naming the
current role, not a blank page or a JS error, per the guard in
`js/router.js`.

**Test 3.2 — Sidebar hides disallowed items.** Sign in as each role in
turn and confirm the visible sidebar groups/items match the table above
exactly — no extra items, no missing ones.

## 4. Patients

### 4.1 Search / Register (`#/patients/search`)

Seeded patients (`HOSP-001`):

| PatientId | Name | MRN | DOB | Gender |
|---|---|---|---|---|
| PAT-001 | John Doe | MRN-90821 | 1992-03-14 | MALE |
| PAT-002 | Priya Reddy | MRN-90822 | 1988-07-22 | FEMALE |
| PAT-003 | Ramesh Kumar | MRN-90823 | 1965-11-02 | MALE |
| PAT-004 | Ananya Iyer | MRN-90824 | 2001-01-30 | FEMALE |
| PAT-005 | Vikram Singh | MRN-90825 | 1979-05-18 | MALE |

1. Search for "Reddy" — expect exactly Priya Reddy (PAT-002).
2. Search for "MRN-90823" — expect exactly Ramesh Kumar.
3. Clear the search box (or leave it under 2 characters) — expect the
   full hospital patient list, per `searchPatients()`'s "term shorter
   than 2 chars → return all" behavior in `js/data.js`.

### 4.2 Registration validation

Required fields, per `patient-search.js` and
`templates/patients/patient-registration.html`: **Full name**,
**Gender**, **Date of birth**, **Phone**. **National Health ID** is
additionally required only when the signed-in hospital marks it
mandatory (Kingston Family Health Centre / `HOSP-002` does; Sunrise /
`HOSP-001` does not).

1. Submit the registration form with all four required fields blank —
   expect four sequential warnings (name → gender → DOB → phone) and no
   patient created.
2. Fill all required fields and submit — expect a new patient with an
   auto-generated MRN and immediate appearance in search results.
3. (If testable under `HOSP-002` context) leave National Health ID blank
   — expect a blocking warning naming the hospital's configured label
   ("National ID").

### 4.3 Today's Patients queue (`#/patients`)

Today's seeded queue for Dr. Sharma (all times are for the current
calendar day):

| Time | Patient | Type | Status |
|---|---|---|---|
| 9:00 AM | John Doe (PAT-001) | Follow-up | Completed |
| 9:15 AM | Priya Reddy (PAT-002) | New | In Consult |
| 9:30 AM | Ramesh Kumar (PAT-003) | Follow-up | Checked-in |
| 10:00 AM | Ananya Iyer (PAT-004) | New | Booked |
| 10:15 AM | Vikram Singh (PAT-005) | Follow-up | Booked |

1. Confirm all five rows render with the status badges above (Completed
   = success/green, In Consult / Checked-in = warning/amber, Booked =
   info/blue).
2. Click a row's consultation action — expect navigation to
   `#/consultation/patient/PAT-00x`.

### 4.4 Patient Allergies (`#/patients/:id/allergies`)

Seeded allergies:

| Patient | Allergen | Type | Severity | Status |
|---|---|---|---|---|
| PAT-001 (John Doe) | Penicillin | Drug | Severe | Active |
| PAT-003 (Ramesh Kumar) | Shellfish | Food | Moderate | Active |

1. Open PAT-001's allergies. Confirm the sticky patient identifier
   strip shows Name, Gender, DOB, National Health ID, MRN, Hospital, and
   the table shows Penicillin / Severe / Active.
2. Add a new allergy (Type, Reaction type, and Severity are all
   required, per `patient-allergies-form.html`) — e.g. Food / Peanuts /
   Severe — and confirm it appears in the table immediately.
3. Delete an allergy — expect a confirmation prompt before removal.
4. Open PAT-002's (Priya Reddy) allergies — expect an empty table
   (no seeded allergies for this patient).

## 5. Consultation 360° (`#/consultation/patient/:patientId`)

Use PAT-001 (has a severe Penicillin allergy — good for exercising the
safety strip and drug-allergy conflict check).

### 5.1 Safety strip and workflow rail

1. On entry, confirm the pinned safety strip shows the Penicillin/Severe
   allergy in red/danger styling (per `_renderTier0()` — any `SEVERE`
   allergy sets `tier0-strip--danger`).
2. Confirm the progress rail shows eight steps: Vitals, Complaints,
   Diagnosis, Observations, Labs/Imaging, Prescriptions, Referral, Next
   Visit — each starting "pending" except the active one.

### 5.2 Vitals — validation ranges

Ranges are enforced in `js/modules/consultation/consultation-360.js`
(`VITAL_RANGES`); verify these exact bounds:

| Vital | Valid range |
|---|---|
| Temperature | 90–110 °F |
| BP Systolic | 50–300 mmHg |
| BP Diastolic | 20–200 mmHg |
| Pulse | 20–250 bpm |
| SpO2 | 0–100 % |
| Height | 20–250 cm |
| Weight | 0.5–500 kg |
| Respiratory Rate | 5–60 breaths/min (label shows "12-20 normal") |
| Pain Scale | 0–10 |
| Waist Circumference | 30–200 cm (disabled by default in seed config) |
| Head Circumference | 20–70 cm, pediatric-only (disabled by default) |

1. Enter Temp = 111 and save — expect a field-level error ("Must be
   90-110°F") and the save to be blocked.
2. Enter Temp = 98.6, Pulse = 72, SpO2 = 98, Height = 170, Weight = 70
   and save — expect success, and the live calculation strip to show
   BMI (24.2, "Normal"), BSA (1.82 m²), and no MAP (BP not entered).
3. Enter BP Systolic = 80, BP Diastolic = 90 (systolic ≤ diastolic) and
   save — expect "Systolic must be greater than diastolic" and the save
   to be blocked.
4. Enter BP Systolic = 120, BP Diastolic = 80 and save — expect success
   and MAP ≈ 93 mmHg to appear in the calculation strip.
5. Confirm the workflow rail marks "Vitals" completed once any vital
   value is saved.

### 5.3 Complaints

1. Toggle a quick-pick symptom (e.g. Fever) through its three states:
   unset → Yes → No → unset. Confirm the chip visibly reflects each
   state.
2. Set a complaint to "Fever" = Yes and confirm the Branched Flow Engine
   banner appears above the Diagnosis section ("Fever pathway
   activated…") and the Temperature vital field is highlighted.
3. Add a free-text custom complaint and confirm it's immediately marked
   Yes and appears among the quick-pick chips.

### 5.4 Diagnosis (ICD-10)

Seeded ICD-10 catalog (`js/data.js`, 6 entries only — this is a small
demo set, not a production code list):

| Code | Description |
|---|---|
| R50.9 | Fever, unspecified |
| J00 | Acute nasopharyngitis (common cold) |
| I10 | Essential (primary) hypertension |
| E11.9 | Type 2 diabetes mellitus without complications |
| A90 | Dengue fever |
| G43.9 | Migraine, unspecified |

1. Search "fever" — expect R50.9 (via synonym match) and, if the
   Branched Flow banner from 5.3 is active, A90 suggested as well.
2. Search a single character — expect a hint to type more, no results
   query fired (search only triggers at 2+ characters).
3. Pick R50.9, fill laterality/acute-chronic/clinical notes as desired,
   confirm — expect it to appear in the confirmed diagnoses list and
   the workflow rail to mark "Diagnosis" completed.
4. Remove the diagnosis — expect it to disappear from the list and the
   rail to revert to pending (if it was the only diagnosis).

### 5.5 Observations (SOAP)

1. Type into Subjective, Objective, Plan and save — expect a success
   toast and the rail to mark "Observations" completed.
2. Click **Auto-generate SOAP** with vitals/complaints/diagnosis already
   entered from 5.2–5.4 — expect a preview modal with all four sections
   (S/O/A/P) pre-filled from that data, editable before accepting.
3. Type `/` followed by at least one character in the Subjective field
   with the doctor's cursor active — expect a phrase-trigger dropdown
   (seeded General Medicine phrases include `/fever`, `/cough`,
   `/norelief`, `/appetite`, `/denies` for Subjective).
4. Click **Start dictation** — if the browser doesn't support the Web
   Speech API, expect a warning naming Chrome/Edge as required, not a
   silent failure or crash. (This is a fully implemented feature, not a
   placeholder — confirm the button actually starts/stops listening in
   a supporting browser.)

### 5.6 Labs/Imaging

1. Search "CBC" — expect "Complete Blood Count (CBC)" among results.
2. Order it — expect it added to the order list with status "Ordered"
   and the rail to mark "Labs/Imaging" completed.
3. Star a search result to favorite it, then re-open the search later
   in the same visit and confirm the star stays highlighted.
4. Attempt to order the same test twice — expect a warning ("Already
   ordered") and no duplicate line.
5. Use **Skip** on this step instead — expect the rail to show
   "skipped" rather than "pending" or "completed".

### 5.7 Prescriptions (CPOE)

1. Search the formulary for "Crocin" (Paracetamol, seeded in
   `drugFormularyCatalog`) and select it — expect the pending
   prescription form to populate dosage (500mg), frequency (1-1-1),
   duration (3 days), and instructions ("After food") from the
   formulary defaults.
2. Confirm the prescription — expect it to appear in the current
   medication table and the rail to mark "Prescriptions" completed.
3. **Drug-allergy conflict check:** for PAT-001 (Penicillin allergy),
   search and attempt to prescribe "Augmentin" (Amoxicillin +
   Clavulanate — a penicillin-class antibiotic). Expect a blocking
   "Drug-Allergy Conflict Detected" modal that requires a typed override
   reason before the prescription can proceed; confirm the override is
   written to the audit log (Action `OVERRIDE_SAFETY`).
4. **Medication reconciliation:** prescribe the same drug a second time
   in the same visit (or with a different dose) — expect a
   "Medication Reconciliation Alert" modal requiring an override reason
   before proceeding, logged as `ACKNOWLEDGE_RECON`.
5. **Controlled substance:** none of the ten seeded formulary drugs are
   flagged `IsControlledSubstance: true` — this warning path exists in
   code (`_handlePickDrug`) but cannot be exercised against seed data as
   shipped; verify it via a manually-entered drug if you set
   `IsControlledSubstance` in test data, or treat as a code-review-only
   check.
6. Discontinue a medication — expect a confirmation prompt naming the
   drug before it's removed from the active list.
7. Use **Repeat previous prescription** on a patient with a prior
   signed-off visit — expect a checklist of that visit's medications,
   with unchecked items excluded from what's added to today's visit.

### 5.8 Referral

1. Fill Specialty and Reason (both required) and submit without a
   Specialty — expect a blocking warning.
2. Fill both and submit — expect the referral to appear in the list and
   the rail to mark "Referral" completed.

### 5.9 Next Visit (Follow-up)

1. Submit without a date — expect a blocking warning.
2. Pick a future date and save — expect a success toast. If an
   available slot exists for that doctor on that date, expect a second
   toast confirming the follow-up appointment was auto-booked; if none
   is available, expect a toast saying so with instructions to book
   manually.

### 5.10 Confidential Notes

1. Write a note and save — expect a success toast.
2. Check the audit log afterward — expect an `UPDATE` /
   `ConfidentialNote` entry recording only that a note was written, with
   no note content present anywhere in the log (per the "exempt from
   audit content" design in `js/data.js`).
3. After sign-off (5.11), confirm the Confidential Notes field is still
   editable — unlike every other section, it is explicitly not
   lock-guarded.

### 5.11 Sign-off and locking

1. Open sign-off with incomplete personal-observation fields (the "your
   own words" S/O/P fields, distinct from the phrase-assisted SOAP
   fields) — expect a warning banner listing which are missing and an
   "I choose to sign off without filling these" checkbox gating the
   confirm button.
2. Check all four attestation boxes (Subjective / Objective / Assessment
   / Plan reviewed) — expect **Confirm sign-off** to only enable once
   all four (plus the personal-observation waiver, if applicable) are
   checked.
3. If any Contradiction Engine rule fires (e.g. save a Pulse of 110 —
   tachycardic — while a Bradycardia-coded diagnosis is present), expect
   an alert ribbon inside the sign-off modal listing the conflict; sign-
   off can still proceed, but confirm an `ACKNOWLEDGE_CONTRADICTION`
   audit entry is written per flagged issue.
4. Confirm sign-off — expect: the consultation locks; every mutating
   action elsewhere in the workspace (save vitals, add diagnosis,
   prescribe, etc.) now shows "This record was signed and locked —
   corrections require an addendum" instead of saving; and (if no
   invoice already exists for the visit) a new invoice is auto-created
   with a consultation-fee line item and one line item per lab/imaging
   order placed during the visit.
5. Add a post-lock addendum (choose a category: Typo/Clarification, Rx
   Change, New Lab Data, Clinical Note) — expect it appended to an
   addendum list on the record, timestamped, without altering the
   original locked content.

### 5.12 Printing

1. Print Prescription — expect a preview containing hospital letterhead,
   patient identifiers, current medications, and any addendums.
2. Print Consultation Summary — expect vitals, complaints, diagnoses,
   medications, lab orders, and referrals all present.
3. Print Lab Requisition with no lab orders placed — expect a blocking
   warning ("No lab orders to print") rather than an empty document.
4. Print Referral Letter with no referral placed — expect the same
   pattern ("No referrals to print").

## 6. Scheduling

### 6.1 Appointments (`#/scheduling/appointments`)

1. Open today's schedule — expect the five seeded PAT-001…PAT-005
   appointments listed in section 4.3, color-coded by status.
2. Book a new appointment into an open slot — expect it to appear
   immediately with status Booked.
3. Create a recurring appointment series — expect one appointment per
   occurrence, each independently visible on its date.

### 6.2 Queue Display (`#/queue-display`)

1. Navigate here **without** being signed in (this route is one of the
   four exempted from the session-validity guard in `router.js`) —
   expect it to render regardless of session state.
2. Confirm it aggregates today's non-cancelled appointments across every
   active doctor at the hospital, sorted by time.

### 6.3 Nurse Vitals (`#/nurse/vitals`)

1. Sign in as NURSE (no seeded nurse account exists — either grant a
   test user the NURSE role via Settings → Users, or code-review this
   screen under the ADMIN account) and confirm pre-entry fields for
   Temp, BP Systolic/Diastolic, Pulse, SpO2, Respiratory Rate, Height,
   Weight are present and route into the same vitals record the doctor
   later sees in Consultation 360°.

## 7. Billing

### 7.1 Checkout (`#/billing/checkout/:consultationId`)

Seeded consultation fee schedule (`js/data.js`, `HOSP-001`): New
consultation ₹500, Follow-up ₹300 (both INR, effective 2026-01-01).

1. Open checkout for a signed-off consultation — expect a consultation
   fee line item already present (auto-created at sign-off) plus one
   line per lab/imaging order from that visit.
2. Add a custom line item with a discount % and tax % — expect the
   subtotal, discount, tax, and total to recompute correctly and the
   discount/tax rows to only render when non-zero.
3. Capture a payment — expect the payment method dropdown to list the
   hospital's configured methods (Cash, Credit Card, UPI for
   `HOSP-001`) and the payment to be reflected in the invoice's paid
   amount, with an audit entry (`CREATE` / `Payment`).
4. Cancel a paid invoice — expect a credit note flow rather than a
   silent deletion.

### 7.2 Outstanding Balances (`#/billing/balances`)

1. Confirm it groups by patient, summing total billed vs. total paid
   across all their invoices.

### 7.3 Day-End Closure (`#/billing/day-end`)

1. Confirm it reconciles the day's captured cash payments against
   recorded invoices.

## 8. Lab Portal / Lab Results

### 8.1 Lab Portal (`#/lab-portal`) — LAB_PARTNER role

1. Sign in as `lab@sunrisediag.in` / `lab123` (Sunrise Diagnostics Lab)
   — expect immediate redirect to `#/lab-portal` on login, and the
   sidebar to show only the Lab Partner group.
2. Confirm pending orders for `HOSP-001` are listed with a status
   indicator (pending vs. results-ready color).
3. Upload a result (summary, value, reference range, technician,
   notes) against a pending order — expect the order status to update
   to "Results Ready".

### 8.2 Lab Results (`#/lab-results`) — DOCTOR/ADMIN

1. Sign in as a doctor and confirm results uploaded in 8.1 appear in
   the result inbox for review, with interpretation coloring (Critical
   = danger red, Abnormal = warning amber, else success green) as
   defined in `consultation-360.js`'s `recentLabs` renderer.

## 9. Reports (`#/reports`)

1. Confirm all five report sections render for a date range containing
   the seeded visit data: Daily Consultation Volume, ICD-10 Diagnosis
   Frequency, Prescribing (Rx) Patterns, Appointment No-Show Rate,
   Revenue by Doctor.
2. Narrow the date range to exclude all activity — expect each chart to
   show "No data for this period", not a broken/empty render.

## 10. Administration (Settings, `#/settings`)

### 10.1 Hospital & Practice tab

1. Edit hospital name/address/phone/email/currency and save — expect
   the topbar hospital name to update immediately.
2. Confirm compliance registrations render per country: for `HOSP-001`
   (India) — GSTN (mandatory), ROHINI ID, Clinical Establishment
   Registration, ABDM Registration; for `HOSP-002` (Jamaica) — MOH
   License (mandatory), Tax Registration Number (mandatory).

### 10.2 Clinical Config tab

1. Add a new vitals definition (e.g. a custom vital code) and confirm
   it appears in the Consultation 360° vitals list on the next visit
   opened.
2. Add/edit a specialty and confirm it's selectable elsewhere the
   specialty list is used (formulary target specialties, lab test
   catalog).

### 10.3 Billing & Numbers tab

1. Confirm the seeded service charge master (10 entries — consultation
   fees, CBC/LFT/RFT/X-Ray/ECG, wound dressing, injection,
   nebulization) is listed and editable.

### 10.4 Notifications tab

1. Confirm WhatsApp and Email provider fields are present but the
   toggle defaults to disabled and no API key/SMTP credentials are
   pre-filled (`_notifConfig` in `js/data.js` ships with
   `enabled: false` and empty credentials) — this is expected: messages
   render against templates but are not delivered to a live provider
   in this build.
2. Confirm the five seeded message templates are listed: Appointment
   Reminder, No-Show Follow-up, Lab Results Ready, Follow-up Reminder,
   Prescription Ready.

### 10.5 Users & Access tab / User Management (`#/settings/users`)

1. Create a new user with Name, Email, Role required — expect the new
   password to default to `demo123` if left blank.
2. Toggle a user Active/Inactive — expect the badge to update and
   (per `authenticateUser` in `js/data.js`) an inactive user's login
   attempt to fail with "Invalid credentials or inactive account".

### 10.6 System & Data tab

1. Export each of the eight listed CSV domains (patients,
   consultations, prescriptions, appointments, invoices, auditLogs,
   labResults, allergies) — expect a file download per click.
2. Import a CSV of patients: upload a file missing a required column
   (FullName, Gender, Dob, or Phone) — expect per-row validation errors
   listed, and only fully-valid rows counted as importable; confirm the
   import summary count matches valid rows only, and an audit entry
   (`IMPORT`) is recorded afterward.
3. Confirm the Data Import target table selector currently only offers
   "Patients" — other domains are not yet wired for import (matches
   `IMPORT_TABLES` in `js/modules/settings/user-management.js`).

### 10.7 Permissions (`#/settings/permissions`)

1. As ADMIN, toggle a permission off for a role (e.g. turn off
   `patients` for RECEPTIONIST) and confirm it persists for the
   session; sign in as that role afterward and confirm the
   corresponding sidebar item and route both become inaccessible.

### 10.8 Drug Formulary (`#/settings/formulary`)

1. Confirm all ten seeded drugs are listed (Crocin, Augmentin, Amlodac,
   Glycomet, Cetzine, Omez, Ascoril-D, Otrivin, Monocef, Candid Mouth
   Paint) with brand/generic/composition/class fields editable.

### 10.9 Phrase Library (`#/settings/phrases`)

1. Filter by section (S/O/P) and confirm the seeded General Medicine
   phrases split correctly: 5 Subjective (`/fever`, `/norelief`,
   `/cough`, `/appetite`, `/denies`), 5 Objective (`/gen`, `/cvs`,
   `/rs`, `/abd`, `/nad`), 4 Plan (`/rest`, `/review`, `/lifestyle`,
   `/emergency`).
2. Edit a phrase's trigger keyword and text, save, and confirm it's
   immediately reflected in the Consultation 360° phrase chips.

### 10.10 Audit Log (`#/audit-log`)

1. Filter by Action = `OVERRIDE_SAFETY` after completing test 5.7.3 —
   expect the logged conflict message and override reason to be
   visible.
2. Filter by a date range excluding all activity — expect "No audit
   entries for this filter."

## 11. Session Behavior

1. Sign in, then remain idle. At 12 minutes of inactivity, expect a
   dismissible "Session expiring soon" banner (bottom-right); at 15
   minutes, expect a full-screen "Session Expired" modal with a
   "Sign in again" button routing to `#/login`.
2. Sign in as the same user in a second browser tab — expect the first
   tab to receive a "signed in on another browser tab" expiry modal
   (single active session per user, per browser, enforced via
   `localStorage`).
3. Note: no logout button or menu action exists anywhere in the UI —
   `SessionManager.destroySession()` is only ever reachable
   programmatically (e.g. from a console/dev-tools call), not from a
   click path. The Settings → Permissions screen's own hint text
   ("Logout/login to see sidebar updates") currently has no logout
   control to follow through on — flag this as a gap if a logout
   affordance is expected in this build.
