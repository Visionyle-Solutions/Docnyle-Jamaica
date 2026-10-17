# OPD Consultation Module

ASP.NET Web Forms application (.NET Framework 4.8) with an in-memory data
layer, built as a running specification for its Phase 4 back end: the
DOCNyleUnifiedAPI WCF service over SQL Server 2019 — see
`docs/WCF-MIGRATION-GUIDE.md`, `docs/API-SPECIFICATION.md` and
`docs/DATA-LAYER-ARCHITECTURE.md`, and `../PROJECT-HANDOFF.md` for the full
project context.

## Run it locally

The project is an **ASP.NET Web Forms application targeting .NET Framework
4.8**. The SPA shell is `index.aspx` (converted from the original
`index.html`). The view fragments under `templates/` are `.aspx` pages
(converted from `.html`; markup unchanged) fetched at runtime via
`fetch()`. Old `*.html` URLs are redirected to their `.aspx` pages. The
app must be served over HTTP — opening a file directly will NOT work.

1. Open `OpdProject.sln` (one folder up) in **Visual Studio 2022**
   (workload: *ASP.NET and web development*; component: *.NET Framework
   4.8 targeting pack*).
2. Press **F5** (or Ctrl+F5). IIS Express starts on
   `http://localhost:8095/` and opens `index.aspx`.

Alternatively, build once in Visual Studio and then double-click
`START-SERVER.bat`, which runs IIS Express on port 8095.

Then open `http://localhost:8095`. It boots directly into **Today's
Patients** (`#/patients`) — the logged-in doctor's queue for today. From
there, open a patient's allergies or start a consultation.

## What's actually working right now

- Full app shell (sidebar, topbar, router, template engine, modal, toast)
- **Patient identifier strip** — sticky banner (Name, Gender, DOB, Health
  ID, Patient ID/MRN, Hospital) on every patient-scoped page
- **Today's Patients** (`#/patients`) — the logged-in doctor's actual
  appointment queue for today (5 seeded patients across a realistic status
  spread: completed, in-consult, checked-in, booked), not a flat roster
- **Patient Allergies** — matches the client-provided reference screenshot
  exactly (split-panel layout, two-column fields, identical table columns)
- **Consultation 360** — workflow rail, Vitals engine (save + live BMI
  calculation), Chief Complaint tri-state chips, ICD-10 + Diagnosis
  search-and-confirm (merged record — resolved code + doctor's clinical
  reasoning saved together), **CPOE / Prescriptions** (formulary search,
  manual entry, BF/AN/NI/EM timing, current medication table, discontinue
  with confirmation, live medication count in the safety rail),
  **Confidential Notes** (author-only, exempt from audit content — only
  the act of writing is logged, remains editable after lock),
  **4-point sign-off attestation** (all S/O/A/P boxes required before
  confirming; locks the consultation and blocks every other mutating
  action with a clear "corrections need an addendum" message), safety
  rail showing live allergy data and lock status. Voice SOAP is the one
  remaining rail placeholder — shows a "coming in a later phase" notice
  rather than half-built functionality.
- **Schedule Settings** (`#/settings/scheduling`, sidebar → Scheduling), modelled on
  MediSlot — tabs:
  - *Hospital Defaults*: default slot length, buffer between appointments,
    free follow-up policy (allowed + window in days).
  - *Doctors*: profiles (prefix/names/qualifications, display-name override,
    registration no., room, languages, several specialties, bio; “Add doctor”
    creates a login) and *Scheduling & charges* — weekly availability
    (several windows per weekday, no window = closed), full-day leave,
    slot-length / free-follow-up overrides, flat or unit-based fees.
  - *Appointment Types*: name, duration (blank = doctor's slot), colour, fee
    category, chargeable.
  - *Intake & Consent*: pre-visit intake questions (answered when booking
    online, shown in the consultation) and the consent text (signed in the
    consultation → Patient Context → Intake & Consent).
  Every booking screen (Appointments, recurring series, Patient Self-Booking,
  consultation follow-up) builds its slots from these settings
  (`listAvailableSlots`); invoices use `getConsultationCharge` (type, charge
  model, free follow-up). Permission: `admin_scheduling` (ADMIN + DOCTOR).
- **Formats** (Settings → Hospital & Practice): date format, time format, MRN
  prefix/format (`{prefix} {year} {seq:N}`), doctor display-name template.
- **Phrase Library**: hospital-wide / specialty / doctor “/” macros, optionally
  linked to a condition (matched by ICD-10 prefix); a doctor's phrase replaces
  a broader one with the same trigger.
- **Consultation Setup** (`/settings/consultation`, sidebar → Consultation →
  Consultation Setup; permission `admin_consultation`, ADMIN + DOCTOR): enable /
  disable Consultation 360 sections, the fields inside them and the options of
  choice fields (e.g. Laterality → Bilateral), and drag sections into order. Add the
  hospital's own **custom sections** (name, description, icon), **custom fields**
  (short/long text, number, date, yes/no, single or multiple choice; optionally
  required before sign-off) on custom sections or the clinical built-in ones, and
  **custom options** (on custom choice fields and on Laterality, Acute/Chronic and
  Urgency). Custom values are saved per consultation and printed on the
  Consultation Summary (with their built-in section, or under the custom section's
  own heading; labels are captured at save time so old prints don't change).
  **Vitals**: switch each vital on/off and add custom vitals (name, unit, valid
  range — created when published) — used by the consultation and Nurse Vitals;
  Settings → Clinical lists the vitals read-only — and per vital switch units on/off, pick the default, add common
  units (kPa, m, ft-in, g, …) or your own with a conversion factor, or apply a
  unit-system preset (Metric / Common clinic mix / US customary). Vitals are
  entered in any enabled unit (consultation and Nurse Vitals; switching converts
  the typed value) and always stored in the base unit (°F, mmHg, cm, kg), so
  ranges, BMI and history stay correct; screens and prints use the default unit. Edits
  are a draft with a live preview; **Publish** shows the change list, bumps the
  version and audits it. The consultation reads only the published setup — hidden
  sections drop out of the stepper, progress and sign-off checks. Vitals,
  Complaints and Diagnosis stay on (required for sign-off). Engine:
  `Services/ConsultationConfigService.cs`; cards render in order via
  `Web/ConsultCards.cs`.
- In-memory data layer (`js/data.js`) seeded with two demo hospitals
  (India + Jamaica) and 5 patients to exercise country-specific config
  (currency, National Health ID mandatory/optional, payment methods) and
  a realistic daily queue

**Verified, not just written**: every phase of this app was actually
booted in a headless browser (jsdom) and driven through its real
interaction flows — search, click, type, confirm — not just checked for
syntax errors or file existence. This caught four real bugs before they
shipped (see below). Manual `file://` opening will still show a blank
page — see "Run it locally".

### Bugs found and fixed during verification

1. **Loading-state overwrite bug** (Allergies): the initial `_load()`
   implementation replaced the *entire* `#main-content` container with a
   loading spinner, which wiped out the shell's div IDs the render step
   needed — causing a blank page after the first render attempt. Fixed by
   scoping the loading/error state to the table region only.
2. **Double-dispatch race on boot**: `Router.init()` was both registering
   the `hashchange` listener *and* immediately dispatching once itself,
   while `app.js` also triggered a navigation — this produced two
   concurrent dispatches racing to write `#main-content`, leaving stray
   content behind. Fixed by making `Router.go()` the single path that can
   ever trigger a dispatch.
3. **Sidebar navigation stripping the URL fragment**: the nav `<a>`
   elements had no `href`, but without `preventDefault()` the browser
   still ran a default navigation that reset the URL and dropped the hash
   — discovered while testing the Consultation module's sidebar link.
   Fixed by calling `preventDefault()` on every nav click.
4. **ICD-10 search results field mismatch**: the search API returns rows
   shaped `{Icd10Code, Description}`, but the results template expected
   `{Code, Description}` — search found the right record but rendered a
   blank code. Fixed by mapping field names at the point results are
   stored, not in the template.

## What's documented but not yet built

Everything else in the inventory — see `docs/roadmap.md` for the build
order and `docs/schema.sql` for the full 61-table design already in place
for every domain, whether built yet or not.

## Documentation index (`docs/`)

| File | Contents |
|---|---|
| `OPD_Module_Inventory.md` | Full field-level spec for every page/domain, across all planning sessions |
| `features-and-decisions.md` | Consolidated decisions log — what was agreed and why |
| `roadmap.md` | Build order from here |
| `schema.sql` | Full SQL Server 2019 DDL, 61 tables |
| `schema.md` | How to read the schema — append-only tables, server-populated fields, confidential-notes handling |
| `api-contract.md` | Every `API.xxx()` method and its Phase 4 HTTP destination |
| `WCF-MIGRATION-GUIDE.md` | Phase 4 plan: in-memory services → DOCNyleUnifiedAPI WCF service + SQL Server |
| `API-SPECIFICATION.md` | The WCF contract `IDOCNyleUnifiedAPI` (one `.svc`, 207 operations) |
| `PROJECT_ARCHITECTURE_RULES_V3_0.md` | The governing architecture rules (as provided) |
