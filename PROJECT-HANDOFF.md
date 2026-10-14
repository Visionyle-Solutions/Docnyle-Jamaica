# Consult360 — OPD Consultation Module — Project Handoff & Context Memory

**Purpose of this document.** This is the single entry point for resuming
work on this project in a fresh conversation with no prior context. Read
this file first, in full, before touching code or any other doc. It
explains what the product is, the architecture adopted (current and
target), what every file/folder is for, how the codebase will translate
during the ASP.NET Core migration, which documents are authoritative vs.
historical, and exactly what was done in the engagement that produced the
current state — so you don't have to re-derive any of it.

**Document status:** authoritative, current as of this handoff. If a claim
here ever conflicts with the actual code or another doc, the code wins for
runtime behavior and this doc's "Documentation Index" section (§6) says
which other doc wins for planning/architecture questions.

---

## 1. What Consult360 Is

Consult360 is an outpatient (OPD) consultation and small-clinic management
application for practices with one to a few doctors. It covers the full
visit lifecycle: patient registration/search, the doctor's daily queue, a
structured consultation workspace (vitals, complaints, diagnosis, SOAP
notes, prescriptions, lab orders, referrals, follow-up, sign-off),
scheduling, billing, lab result exchange with external lab partners, and
hospital-level administration (users, permissions, formulary, phrase
library, compliance registrations, notifications). It is explicitly
multi-tenant (per-hospital) and multi-country (two seeded demo hospitals:
India and Jamaica), with country-specific behavior driven by hospital
settings rather than hard-coded.

For the full product walkthrough (workflows, roles, clinical engines),
read `docs/PRODUCT-GUIDE.md` — it is accurate as of this handoff and does
not need to be re-derived.

---

## 2. Architecture Adopted — Current State (Phase 1) and Purpose

### 2.1 Why this architecture

The project is deliberately built in **four phases**, and every Phase 1
design decision exists to make Phase 4 (the real backend) a mechanical
translation rather than a rewrite:

- **Phase 1 (current, complete):** a vanilla HTML/CSS/JS single-page app
  with an **in-memory data layer** (`js/data.js`) standing in for a
  database, and a thin **service layer** (`js/api.js`) standing in for a
  REST API. No framework, no build step, no dependencies — runs from any
  static file server.
- **Phase 4 (designed, not yet built):** ASP.NET Core Web API + SQL
  Server 2019, replacing `data.js` with real stored-procedure-backed
  services and `api.js`'s bodies with real `fetch()` calls — **the
  browser SPA itself does not get rewritten.** This is the single most
  important architectural decision in the project and is explained fully
  in §3 and in `docs/ASPNETCORE-MIGRATION-GUIDE.md`.

The mechanism that makes this possible: `js/api.js` was written from day
one with the exact method names, parameters, and return shapes that the
eventual REST API will have. Every UI module calls `window.API.xxx(...)`,
never `window.Data.xxx(...)` directly (one sanctioned exception — see
§2.4). When Phase 4 lands, only `api.js`'s method **bodies** change (from
`ok(window.Data.xxx())` to `await fetch(...)`); nothing that calls
`window.API` needs to change at all.

### 2.2 The governing naming/architecture rules

`docs/DATA-LAYER-ARCHITECTURE.md` is the binding rulebook for this
mechanism and must be read before adding or changing any data-layer code.
Key rules, summarized (see that doc for the complete, authoritative
version):

- **14-verb method taxonomy.** Every `data.js`/`api.js` method name starts
  with exactly one of: `get`, `list`, `search`, `check`, `create`, `save`,
  `update`, `delete`, `submit`, `confirm`, `cancel`, `approve`, `reject`,
  `record`. This verb determines the HTTP verb it becomes in Phase 4 (a
  mapping table is in that doc, §2).
- **`api.js` is a pure pass-through.** No business logic, no DOM access.
  Every method calls exactly one `window.Data` method and wraps it in
  `ok()` (a `Promise` with a simulated 30ms network delay). In Phase 4,
  the body becomes a `fetch()` call with the identical signature.
- **`data.js` arrays are SQL tables.** Every in-memory array has a `// →
  SQL Table: X` comment pointing at its `schema.sql` counterpart.
  Properties are PascalCase and match real SQL column names exactly (no
  camelCase in data objects — this is different from typical JS style,
  and deliberate).
- **Multi-tenancy via `HospitalId`.** Every hospital-scoped array/table
  has a `HospitalId` column; `api.js` methods that touch hospital-scoped
  data accept a `hospitalId` parameter with an `|| _hid()` fallback to the
  current session's hospital.
- **SQL Server schema namespaces:** `opd` (core OPD), `cln` (clinical),
  `bil` (billing), `cfg` (config/reference data), `sec` (security/audit),
  `msg` (messaging). Table/column/constraint naming conventions are all
  specified in that doc, §5.

### 2.3 Layer map (Phase 1, as it runs today)

```
Browser (single page, no build step)
┌────────────────────────────────────────────────────────────┐
│ index.html                                                  │
│  → js/app.js          boot: registers routes, starts router │
│  → js/router.js       hash router (#/...), permission guard │
│  → js/session-manager.js   client-side session/token         │
│  → js/app-context.js  current hospital/user context helpers │
│  → js/layout.js       sidebar/topbar chrome, nav rendering  │
│  → js/template-engine.js   fetches & renders templates/*.html│
│                                                                │
│  → js/modules/**/*.js      one module per screen/feature     │
│     (patients, consultation, scheduling, billing, lab,       │
│      reports, settings, communications)                      │
│  → js/engines/*.js         client-side clinical logic         │
│     (branched-flow, voice-soap, soap-composer,                │
│      rx-reconciler, contradiction) — see §2.5, none of these  │
│      need a server port                                       │
│  → js/components/*.js      shared UI widgets (modal, toast,   │
│      patient-strip, drawer, drawing-pad, print-generator)     │
│                                                                │
│  → js/api.js    ←—— the ONLY layer that changes in Phase 4    │
│  → js/data.js   ←—— deleted entirely in Phase 4 (replaced by  │
│                      the real SQL Server database)             │
└────────────────────────────────────────────────────────────┘
```

`templates/**/*.html` are fragment templates fetched at runtime by
`template-engine.js` and injected into `#main-content` by each module;
they are not full pages and are Phase-4-agnostic (they stay exactly as
they are — see §4).

### 2.4 The one sanctioned `window.Data` bypass

Per `DATA-LAYER-ARCHITECTURE.md` §3 Rule 4 (as corrected during this
engagement — see §7): the only method allowed to be called directly as
`window.Data.hasPermission(...)` instead of through `window.API` is
`hasPermission`, called from both `js/router.js` (route guard) and
`js/layout.js` (sidebar rendering). Every other data access goes through
`window.API`. This was actually enforced as of the last fix pass — 8 other
violation sites were found and corrected (see §7.3).

### 2.5 Client-side clinical engines — none need a server port

`js/engines/*.js` are pure client-side advisory logic, never called
through `api.js`/`data.js`, with no backing stored procedure:

| Engine | Stays client-only because |
|---|---|
| `engine-voice-soap.js` | Wraps the browser's native Web Speech API directly — literally unportable to a server |
| `engine-branched-flow.js` | Pure function over data already in the browser, advisory only |
| `engine-soap-composer.js` | Same — pure function, advisory only |
| `engine-rx-reconciler.js` | Same — pure function, advisory only |
| `engine-contradiction.js` | Same — pure function, advisory only |

This was verified by reading each file, not assumed — full reasoning is in
`docs/ASPNETCORE-MIGRATION-GUIDE.md` §8. The one clinical **safety** check
that *is* server-backed (and must remain non-bypassable) is
`checkDrugAllergyConflicts` (`cln.usp_CheckDrugAllergyConflicts`), which is
a real `data.js`/`api.js`/SQL method, not an "engine."

---

## 3. Architecture Adopted — Target State (Phase 4, designed but not built)

**Stack:** ASP.NET Core (current LTS, e.g. net8.0) Web API + SQL Server
2019. Not ASP.NET Framework, not Blazor, not a front-end rewrite — the
existing vanilla-JS SPA is retained; **only `js/api.js` changes.**

**Important scope correction carried in this handoff:** two older planning
docs (`docs/migration-map.md` and parts of `docs/PRODUCT-GUIDE.md`'s
predecessor content) describe the Phase 4 target as "ASP.NET Core /
Blazor" — a full front-end rewrite with `.razor` pages. **That is not the
target.** `docs/ASPNETCORE-MIGRATION-GUIDE.md` explicitly supersedes that
framing (see its own header note) and is the authoritative target
architecture. If you encounter the Blazor framing anywhere else, treat it
as stale.

**Solution structure (from the migration guide):**

```
Consult360.sln
├─ Consult360.Api             Web API host — Controllers, Program.cs,
│                              appsettings.json, JWT auth, CORS, DI wiring
├─ Consult360.Application     Business logic ported module-by-module from
│                              data.js, DTOs (from types.js)
├─ Consult360.Infrastructure  Dapper repositories calling the existing
│                              stored-procedures.sql (152→157 procedures),
│                              auth/session plumbing
├─ Consult360.Domain          Shared domain types/enums
└─ Consult360.Tests           xUnit — parity tests against
                               docs/testing-guide.md's scenarios
```

**Data access:** Dapper, not EF Core — justified in the migration guide
§4: the project is already 100% stored-procedure-based, and EF Core's
change tracking/Code-First migrations would fight that design rather than
help it.

**The schema and stored procedures already exist and are not being
redesigned** — `docs/schema.sql` (51 tables, SQL Server 2019 DDL) and
`docs/stored-procedures.sql` (157 procedures) were purpose-built for this
migration and are deploy-ready as-is. The migration work is: standing up
the ASP.NET Core solution, porting `data.js`'s actual business logic into
C# services module-by-module, wiring controllers to match
`docs/API-SPECIFICATION.md`'s 136 documented endpoints, and implementing
real auth (JWT + the existing-but-unused `sec.UserSession`/permission
tables) in place of the current client-only session.

For the full step-by-step (auth design, migrations/deployment strategy,
testing/parity strategy, phase gates P0–P7, risks/mitigations, worked C#
examples pulled from real `data.js` logic), read
`docs/ASPNETCORE-MIGRATION-GUIDE.md` in full — it is long (1,487 lines)
specifically so it doesn't need to be re-derived or re-asked-for.

---

## 4. File & Folder Translation Map — Phase 1 → Phase 4

This is the direct answer to "how files and folders finally translate
during or after migration." Every current path, and what happens to it.

| Phase 1 path | Phase 4 fate |
|---|---|
| `index.html` | **Unchanged.** Still the SPA shell. (Two serving options exist — see migration guide §9 — either keep it on today's static server pointed at the new API's base URL, or serve it from the ASP.NET Core app's `wwwroot`.) |
| `css/*.css` | **Unchanged.** No styling changes are part of this migration. |
| `assets/*` | **Unchanged.** |
| `js/app.js`, `router.js`, `layout.js`, `session-manager.js`, `app-context.js`, `template-engine.js`, `types.js` | **Unchanged**, except `session-manager.js`/`app-context.js` start receiving real server-issued JWTs/session tokens instead of the current client-only session object — same call shape, real backing. |
| `js/modules/**/*.js` | **Unchanged.** These already call `window.API`, never touch data directly (one sanctioned exception, §2.4) — nothing here needs to know Phase 4 happened. |
| `js/engines/*.js` | **Unchanged, stay client-side.** See §2.5. |
| `js/components/*.js` | **Unchanged**, except the print generator's `Data.listHospitalCompliances` call already goes through `API` as of the current fix pass — no further change needed. |
| `templates/**/*.html` | **Unchanged.** Fragment templates are transport-agnostic. |
| `js/api.js` | **Rewritten, method-by-method, same file.** Every method body changes from `ok(window.Data.xxx(...))` to `await fetch('/api/...')`. Signatures and return shapes stay identical (this is the whole point — see migration guide §9 for the before/after diff pattern). |
| `js/routes.js` | **Retired or kept as a reference-only client-routing table.** It has no Phase-4 role (ASP.NET Core routing is server-side, defined in Controllers); it was cleaned up during this engagement to accurately reflect `router.js` (see §7.2) but has no further migration purpose beyond documenting the client hash-route surface. |
| `js/data.js` | **Deleted entirely.** Replaced by the real SQL Server 2019 database (`docs/schema.sql`, already designed) accessed through `docs/stored-procedures.sql` (already designed) via `Consult360.Infrastructure`'s Dapper repositories. Its *business logic* (filters, computed fields, validation, status transitions) does not disappear — it gets ported into `Consult360.Application` C# services, module by module (migration guide §5 has the methodology and worked examples). |
| `docs/schema.sql` | **Becomes the deployed database.** Version/promote it with a SQL-script migration tool (DbUp recommended, migration guide §10) rather than EF Core Code-First. |
| `docs/stored-procedures.sql` | **Becomes the deployed stored procedures**, called from `Consult360.Infrastructure` via Dapper. |
| `docs/API-SPECIFICATION.md` | **Becomes the literal controller/route contract** for `Consult360.Api`. Every row is one controller action. |
| `docs/DATA-LAYER-ARCHITECTURE.md` | **Becomes the naming-convention reference** for the C# port — verb taxonomy, schema namespacing, multi-tenancy rules all carry forward unchanged into the ASP.NET Core code. |
| `docs/ASPNETCORE-MIGRATION-GUIDE.md` | **The execution plan.** Consumed once, during the migration; not a living doc after Phase 4 ships (though its risk/mitigation table is worth keeping as an incident-postmortem checklist). |
| `docs/testing-guide.md` | **Becomes the parity-test source.** Its scenarios (seeded patients, expected validation behavior, role-based visibility) become the xUnit integration test suite's fixtures/assertions in `Consult360.Tests`. |
| `docs/PRODUCT-GUIDE.md` | **Stays as the product reference** — describes behavior, not implementation, so it doesn't change with the backend swap (update only if a Phase 4 auth/UX change genuinely alters user-visible behavior, e.g. real logout). |
| `README.md`, `START-SERVER.sh/.bat` | **Superseded by the ASP.NET Core project's own run instructions** (`dotnet run`, `docker-compose`, or equivalent) once Phase 4 ships; keep them for Phase 1 standalone demo purposes until then. |
| `docs/PROJECT_ARCHITECTURE_RULES_V3_0.md`, `docs/UNIVERSAL-ARCHITECTURE-RULEBOOK.md`, `docs/consultation-360-redesign-spec.md`, `docs/OPD_Module_Inventory.md`, `docs/workflow-gap-analysis.md`, `docs/MEDCITY-GAP-ANALYSIS.md`, `docs/ENCRYPTION-STRATEGY.md`, `docs/PEN-TEST-STRATEGY.md`, `docs/PROJECT-MASTER-v5.7.md` | **Historical/reference — not touched by this engagement.** See §6 for what each is and its trust level. `ENCRYPTION-STRATEGY.md`/`PEN-TEST-STRATEGY.md` content is folded into the migration guide's security section rather than duplicated; treat those two as still-relevant detail references. `docs/migration-map.md` specifically is **superseded** on the Blazor-vs-vanilla-JS question (§3). |

---

## 5. Complete Current File Inventory (Phase 1, as of this handoff)

### 5.1 App shell & infrastructure (`js/`, root)

| File | Purpose |
|---|---|
| `index.html` | SPA shell — sidebar/topbar containers, `#main-content`, script tags |
| `js/app.js` | Boot sequence: registers all hash routes, auto-creates a demo session on fresh load (fixed during this engagement — see §7.1), starts the router |
| `js/router.js` | Hash router; permission-guarded dispatch (`ROUTE_PERMS`); redirects to `#/login` if no valid session (added during this engagement) |
| `js/routes.js` | A `window.Routes` constant lookup table for route strings — cleaned up to match `router.js` exactly during this engagement (§7.2); not authoritative, `router.js` is |
| `js/layout.js` | Renders sidebar/topbar chrome; permission-filtered nav |
| `js/session-manager.js` | Client-side session/token lifecycle, multi-tab logout, expiry modal |
| `js/app-context.js` | Current hospital/user context accessors (`hospitalId()`, etc.) |
| `js/template-engine.js` | Fetches `templates/*.html` fragments and interpolates them |
| `js/types.js` | Shared JS "type" shape references (informal, no TS) |
| `js/api.js` | Service layer — **the only file that changes in Phase 4**. 150 methods as of this handoff |
| `js/data.js` | In-memory data layer + seed data — **deleted in Phase 4** |

### 5.2 Feature modules (`js/modules/**`)

| Folder | Modules |
|---|---|
| `patients/` | `patient-portal.js`, `patient-list.js`, `patient-search.js`, `patient-allergies.js` |
| `consultation/` | `consultation-360.js` — the core clinical workspace (vitals, symptoms, SOAP, diagnoses, prescriptions, labs, referrals, confidential notes, sign-off) |
| `scheduling/` | `appointment-calendar.js`, `queue-and-nurse.js` |
| `billing/` | `billing-checkout.js`, `billing-extras.js` |
| `lab/` | `lab-portal.js` |
| `reports/` | `reports-dashboard.js` |
| `communications/` | `notification-engine.js` |
| `settings/` | `settings-hub.js` (current unified admin screen), `admin-settings.js` (older standalone screens — Hospital Profile, Vitals Config, Specialty Config, Formulary; hospital/vitals/specialty are now dead-routed in favor of `settings-hub.js`, see §7.2), `permission-matrix.js`, `phrase-management.js`, `user-management.js` (includes the real `LoginModule`), `audit-log.js` |

### 5.3 Clinical engines (`js/engines/`) — all client-side, see §2.5

`engine-branched-flow.js`, `engine-voice-soap.js`, `engine-soap-composer.js`,
`engine-rx-reconciler.js`, `engine-contradiction.js`.

### 5.4 Shared components (`js/components/`)

`component-modal.js`, `component-toast.js`, `component-patient-strip.js`,
`component-drawer.js`, `component-drawing-pad.js`,
`component-print-generator.js`.

### 5.5 Templates (`templates/**`)

Fragment HTML, one file per screen/section, organized by the same folders
as `js/modules/` (`patients/`, `consultation/`, `scheduling/`, `billing/`,
`settings/`, `reports/`, plus `shared/` for loading/error/empty states and
the patient identifier strip).

### 5.6 Docs (`docs/`) — see §6 for the full index with trust levels

---

## 6. Documentation Index — What Each Doc Is and Its Trust Level

| Doc | Status | What it's for |
|---|---|---|
| `DATA-LAYER-ARCHITECTURE.md` | **Authoritative, current** | The binding naming/architecture rulebook (§2.2 above). Read before any data-layer change. |
| `schema.sql` | **Authoritative, current, deploy-ready** | Full SQL Server 2019 DDL, 51 tables across `opd/cln/bil/cfg/sec/msg`. |
| `stored-procedures.sql` | **Authoritative, current, deploy-ready** | 157 procedures + 3 views, v2.1, covers all 150 `api.js` methods 1:1. |
| `API-SPECIFICATION.md` | **Authoritative, current** | 136 documented REST endpoints, v2.1, matches `api.js`/`stored-procedures.sql` exactly. |
| `ASPNETCORE-MIGRATION-GUIDE.md` | **Authoritative, current** | The Phase 4 execution plan — read this before starting any ASP.NET Core work. Explicitly corrects the Blazor framing in older docs. |
| `PRODUCT-GUIDE.md` | **Authoritative, current** | Rewritten during this engagement, grounded in the live code (not the accumulated phase drift the original had). |
| `testing-guide.md` | **Authoritative, current** | Rewritten during this engagement; matches the real router/permission matrix; the fixed-boot-behavior callout reflects the current, fixed code. |
| `ENCRYPTION-STRATEGY.md` | **Reference, not re-verified this engagement** | Always-Encrypted column strategy, deterministic vs. randomized. Still relevant; not touched. |
| `PEN-TEST-STRATEGY.md` | **Reference, not re-verified this engagement** | Security posture notes, including the drug-allergy-conflict bypass risk referenced in the migration guide. Still relevant; not touched. |
| `OPD_Module_Inventory.md` | **Historical reference** | Earlier module/feature inventory; superseded in accuracy by `PRODUCT-GUIDE.md` and this handoff, but may have detail not repeated elsewhere. |
| `PROJECT-MASTER-v5.7.md` | **Historical reference** | Earlier project-state snapshot (build-numbered); not re-verified. |
| `PROJECT_ARCHITECTURE_RULES_V3_0.md`, `UNIVERSAL-ARCHITECTURE-RULEBOOK.md` | **Historical reference, largely superseded by `DATA-LAYER-ARCHITECTURE.md`** | Earlier architecture-rule drafts; `DATA-LAYER-ARCHITECTURE.md` is the current one to follow when the two disagree. |
| `consultation-360-redesign-spec.md` | **Historical reference** | Design spec for the current Consultation 360 screen; useful for UX rationale, not re-verified against current code. |
| `workflow-gap-analysis.md`, `MEDCITY-GAP-ANALYSIS.md` | **Historical reference** | Earlier gap-analysis snapshots; not re-verified — the JSDOM functional-test findings (§7) are the current, verified gap list. |
| `migration-map.md` | **Superseded on one point** | Describes Phase 4 as ASP.NET Core/Blazor — this is **wrong** per the current mandate (§3); everything else in it not re-verified. |
| `README.md` | **Partially stale** | Says port 8080; the actual `START-SERVER.sh`/`.bat` use port 8095 — trust the scripts. |
| `PROJECT-HANDOFF.md` (this file) | **Authoritative, current** | You're reading it. |

---

## 7. Session History — What Was Done, In Order

This section is the changelog. Read it to understand *why* the code and
docs look the way they do, without having to reconstruct it from diffs.

### 7.1 Data-layer & API documentation generation (v1 → v2.0)

Starting from the originally uploaded project (which already had a first
draft `schema.sql`, `stored-procedures.sql` (27 SPs), `API-SPECIFICATION.md`
(115 endpoints), and `DATA-LAYER-ARCHITECTURE.md`), the stored procedures
and API spec were rewritten to full coverage: **152 procedures** and
**130 endpoints**, covering all **143** `api.js` methods that existed at
the time, verified against the real `schema.sql` tables (no invented
objects) and against `data.js`'s actual business logic.

### 7.2 Testing Guide & Product Guide rewrite

The original `testing-guide.md` (1,537 lines, "Build v2.9") and
`PRODUCT-GUIDE.md` had accumulated phase drift — stale build numbers, a
wrong server port claim, references to a "coming later" voice-dictation
feature that was actually already implemented, and (critically) reliance
on `js/routes.js`, which turned out to be stale for **24 of its 30**
route constants relative to the real `router.js`. Both docs were rewritten
from scratch grounded in the live router/module code.

### 7.3 ASP.NET Core Migration Guide

`docs/ASPNETCORE-MIGRATION-GUIDE.md` was written (1,487 lines),
structured similarly to an unrelated sample migration doc the user
provided for formatting reference only (that sample's product-specific
content was not reused). Key decisions: Dapper over EF Core; none of the
5 client engines need a server port (verified by reading each file); a
hybrid JWT + server-tracked session (using the schema's already-designed
but unused `sec.UserSession` tables); and an explicit correction of the
Blazor-vs-vanilla-JS scope conflict found in older planning docs.

### 7.4 JSDOM functional test pass

A real Node.js + JSDOM harness (static file server + `runScripts:
"dangerously"`, real DOM events dispatched on real elements, not
function calls) was built and run against the live app. Findings: **1
Blocker, 5 High, 4 Medium, 4 Low** — full detail was delivered separately
as `JSDOM-FUNCTIONAL-TEST-FINDINGS.md` (not re-included in this handoff;
see that file if you need the original evidence/repro steps). Headline
findings:

- **Blocker:** the app booted into a permanently blank content area — the
  boot path never created a session, and the router's session guard
  silently blocked every route.
- **High:** 8 UI call sites invoked `window.API` methods that didn't
  exist; vitals range validation was dead code (the DOM element it
  checked for was never rendered); 8 call sites bypassed `window.API`
  in violation of the architecture doc's own rule; `routes.js` was stale
  for 24/30 entries; a stale-render race threw uncaught exceptions on
  fast navigation.
- Plus assorted Medium/Low findings (duplicate route registrations,
  duplicate `api.js` method definitions, browser-API polyfill gaps).

### 7.5 Bug-fix pass 1

All findings from §7.4 were fixed and re-verified (both harness runs
clean, zero console errors):

- `app.js` now creates a real demo session before the initial route
  dispatch; `router.js` redirects to `#/login` instead of silently
  blocking when no session exists.
- Added the 7 missing `window.API` methods, each backed by real logic
  (not stubs): `updatePermission`, `createVitalConfig`, `createDrug`,
  `updateDrugStatus`, `toggleSpecialty` (new `data.js` method), `naming
  fix for `saveConsultationVitals`→`saveVitals`, and `updateMessageTemplate`.
- Added the missing `vital-err-{code}` DOM elements so range/cross-field
  vitals validation actually fires and blocks bad saves.
- Converted the 8 `window.Data` bypass call sites to go through
  `window.API`; corrected `DATA-LAYER-ARCHITECTURE.md`'s "only exception"
  claim to accurately name `hasPermission` (used by both `router.js` and
  `layout.js`).
- Rewrote `routes.js`: 12 corrected paths, 13 orphaned entries removed.
- Null-checked ~10 stale-render `.innerHTML` sites in
  `consultation-360.js` and `reports-dashboard.js`.
- Removed the dead duplicate route registrations in `app.js` and the
  shadowed duplicate method definitions in `api.js`.

### 7.6 Bug-fix pass 2 — two pre-existing bugs surfaced during pass 1

Pass 1 surfaced (but deliberately didn't fix, to stay scoped) two bugs in
the standalone `SpecialtyConfigModule` (`js/modules/settings/admin-settings.js`):
a dead feature-detection check (`if (!window.Data.addSpecialty)` — a
method that never existed, so "Add specialty" always silently no-op'd),
and a signature mismatch between `api.js`'s `createSpecialty(record)`
wrapper and the actual live (shadowed-duplicate-winner) `data.js`
implementation, which took `(hospitalId, name)`. Both were fixed:
the dead check was removed, and `api.js`'s wrapper now matches
`(hospitalId, name)` — which, it turned out, is also what
`stored-procedures.sql`'s `cfg.usp_CreateSpecialty` already expected, so
this fix aligned the client with an already-correct SQL/API-spec design,
not the other way around. Verified with a targeted JSDOM script that
drove the real "Add specialty" button end-to-end (row count went 3→4,
correct field values, zero errors).

Note: `SpecialtyConfigModule` itself is currently **unreachable from any
route** (superseded by `settings-hub.js`'s unified screen in an earlier
fix) — it was fixed anyway since it's still shipped, loaded code, but this
is worth a decision (delete the dead module, or re-wire it) before Phase 4
porting work reaches the Specialty Config area.

### 7.7 Cross-layer sync fix (v2.0 → v2.1)

Bug-fix passes 1 and 2 grew `api.js` from 143 to **150** methods (7 new:
`toggleSpecialty`, `createVitalConfig`, `createDrug`, `updateDrugStatus`,
`updateMessageTemplate`, `exportTableCsv`, plus wiring up the
previously-unused `updatePermission`). This was caught by explicitly
checking rather than assuming, and closed out:

- **5 new stored procedures** added to `stored-procedures.sql` (152→157):
  `cfg.usp_ToggleSpecialty`, `cfg.usp_CreateVitalConfig`,
  `cfg.usp_CreateDrug`, `cfg.usp_UpdateDrugStatus`,
  `cfg.usp_UpdateMessageTemplate`. (`exportTableCsv` needed no new
  procedure — it's a same-behavior alias of `listExportData`, sharing the
  existing `sec.usp_ListExportData_*` set. `updatePermission`'s procedure
  already existed, just wasn't wired to a documented endpoint.)
- **7 new/corrected endpoint entries** added to `API-SPECIFICATION.md`
  (130→136 endpoints), and the false "not currently called from api.js"
  note on `updatePermission` corrected.
- Verified: 157 `CREATE OR ALTER PROCEDURE` statements with 157 matched
  `BEGIN`/`END` pairs (no syntax drift), every table the 5 new procedures
  touch confirmed to exist in `schema.sql`, all 7 methods confirmed
  traceable in the updated `API-SPECIFICATION.md`.

**Current state: all three layers (`api.js`, `API-SPECIFICATION.md`,
`stored-procedures.sql`) are in lockstep at 150 methods / 136 endpoints /
157 procedures.**

---

## 8. Current State Snapshot (key numbers, as of this handoff)

| Metric | Value |
|---|---|
| `api.js` public methods | 150 |
| `stored-procedures.sql` procedures | 157 (+ 3 reporting views) |
| `API-SPECIFICATION.md` documented endpoints | 136 |
| `schema.sql` tables | 51, across 6 schemas (`opd/cln/bil/cfg/sec/msg`) |
| `router.js` registered routes | 30 |
| Seeded demo hospitals | 2 (`HOSP-001` India / INR, `HOSP-002` Jamaica / JMD) |
| Seeded demo patients | 5 (`PAT-001`…`PAT-005`; `PAT-003` has the richest demo data) |
| Seeded demo users/roles | 7 users across ADMIN, DOCTOR, RECEPTIONIST, BILLING, NURSE, LAB_PARTNER |
| Known open item | `SpecialtyConfigModule` is fixed but unreachable from routing — decide keep/delete before Phase 4 touches that area (§7.6) |
| JSDOM regression status | Clean — both full harness runs, zero console errors, as of the last fix pass |

---

## 9. How to Resume Work — Suggested Entry Points

- **Continuing Phase 4 (ASP.NET Core) work:** read
  `docs/ASPNETCORE-MIGRATION-GUIDE.md` in full, then
  `docs/DATA-LAYER-ARCHITECTURE.md`. Start with the P0/P1 phase gates in
  the migration guide §12.
- **Making a Phase 1 code change:** read `docs/DATA-LAYER-ARCHITECTURE.md`
  §2–4 first (naming rules), find the relevant module in §5 of this doc,
  and re-run the JSDOM harness pattern from §7.4 (harness files are
  scratch tooling, not shipped in the project — rebuild per that
  section's method if needed) to confirm no regression.
- **Adding a new `api.js` method:** always add the matching stored
  procedure and `API-SPECIFICATION.md` entry in the same change — §7.7 is
  a cautionary example of what happens when that's skipped even briefly.
- **Deciding the `SpecialtyConfigModule` question (§7.6, §8):** either
  delete the dead standalone module and its template, or re-wire a route
  to it — whichever, do it before porting the Specialty Config area to
  C#, so the port has one canonical implementation to work from.
- **Any question about "what does the app currently do"**: trust
  `docs/PRODUCT-GUIDE.md` and `docs/testing-guide.md` over any older doc
  in the historical-reference list (§6) — those two were rewritten
  against the live code, the others were not re-verified this engagement.
