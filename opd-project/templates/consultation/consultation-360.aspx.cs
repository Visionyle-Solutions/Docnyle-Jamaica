using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.Web
{
    /// <summary>One step of the consultation workflow (section stepper + module card header).</summary>
    public class WorkflowStep
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }
        public bool Mandatory { get; set; }
        public string State { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>One row of the consultation page's "Today's Patients" worklist.</summary>
    public class WorklistEntry
    {
        public string Name { get; set; }
        public string Mrn { get; set; }
        public string AgeSex { get; set; }
        public string Time { get; set; }
        public string StatusLabel { get; set; }
        public string StatusTone { get; set; }
        public string Url { get; set; }
        public bool IsActive { get; set; }
        public string Initials { get; set; }
    }
}

namespace OpdProject.templates.consultation
{
    /// <summary>
    /// Consultation 360 workspace. Opening it links today's checked-in appointment (CHECKED_IN → IN_CONSULT);
    /// signing off locks the record, completes the appointment and auto-generates the invoice.
    /// Each section is a user control; this page provides the shared visit context (<see cref="IConsultationHost"/>).
    /// The published consultation configuration decides which cards show, in what order, and which of their fields.
    /// </summary>
    public partial class consultation_360 : BasePage, IConsultationHost
    {
        protected override string RequiredPermission { get { return "consultation"; } }
        public override string PageTitle { get { return Patient != null ? Patient.FullName + " · Consultation" : "Consultation"; } }

        private static readonly string[][] Steps =
        {
            new[] { "vitals", "Vitals", "ti-heart-rate-monitor", "1" }, new[] { "complaints", "Complaints", "ti-list-check", "1" },
            new[] { "diagnosis", "Diagnosis", "ti-stethoscope", "1" }, new[] { "soap", "Observations", "ti-notes", "0" },
            new[] { "labs", "Labs/Imaging", "ti-microscope", "0" }, new[] { "cpoe", "Prescriptions", "ti-prescription", "0" },
            new[] { "services", "Services & Procedures", "ti-first-aid-kit", "0" },
            new[] { "referral", "Referral", "ti-transfer-out", "0" }, new[] { "followup", "Next Visit", "ti-calendar-plus", "0" },
        };

        private Consultation _consultation;
        private ConsultFormView _form;
        private Dictionary<string, WorkflowStep> _steps = new Dictionary<string, WorkflowStep>();
        private bool _stepActivated;
        private Dictionary<string, string> _customValues;
        private readonly Dictionary<string, string> _customErrors = new Dictionary<string, string>();

        /* Topbar / patient bar values */
        protected string ActingAs { get { return CurrentUser != null ? CurrentUser.FullName : ""; } }
        protected string PhysicianSub { get; private set; }
        protected string VisitBadgeHtml { get; private set; }
        protected string AgeSex { get { return Patient == null ? "" : FormatAgeSex(Patient); } }
        protected int ProgressPercent { get; private set; }

        /// <summary>Card the browser should bring into view after this postback (set when a section moves the doctor on).</summary>
        protected string ScrollTarget { get { return _stepActivated ? ActiveStepField.Value : ""; } }

        public Consultation Consultation { get { return _consultation; } }
        public Patient Patient { get; private set; }
        public Hospital ConsultHospital { get { return Hospital; } }
        public string DoctorId { get { return UserId; } }
        public bool IsLocked { get { return _consultation != null && _consultation.IsLocked; } }
        public ConsultFormView FormConfig { get { return _form ?? (_form = ConsultationConfigService.GetView(HospitalId)); } }
        protected string ConsultationIdValue { get { return _consultation != null ? _consultation.ConsultationId : ""; } }

        private IEnumerable<ConsultationSection> Sections
        {
            get { return new ConsultationSection[] { Tier0Section, Tier1Section, VitalsSection, SymptomsSection, DiagnosisSection, SoapSection, LabsSection, CpoeSection, ServicesSection, ReferralSection, FollowupSection, NotesSection }; }
        }

        /// <summary>The consultation workspace runs full-screen: no app sidebar or top bar.</summary>
        protected void Page_Init(object sender, EventArgs e)
        {
            ((SiteMaster)Master).HideChrome = true;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            var patientId = RouteValue("patientId");
            Patient = PatientService.Get(patientId);
            if (Patient == null || Patient.HospitalId != HospitalId) { ShowError("Patient not found."); return; }
            if (!HasRole("DOCTOR")) { ShowError("Only doctors can open a consultation — it is recorded under the signed-in doctor."); return; }

            var cid = ViewState["cid"] as string ?? Request.QueryString["cid"];
            var existing = cid != null ? ConsultationService.Get(cid) : null;
            _consultation = existing != null && existing.PatientId == Patient.PatientId && existing.DoctorId == UserId
                ? existing
                : ConsultationService.Open(Patient.PatientId, UserId, HospitalId);
            ViewState["cid"] = _consultation.ConsultationId;

            var appointments = SchedulingService.ListAppointments(HospitalId, UserId, Today)
                .Where(a => a.Status != "CANCELLED").OrderBy(a => a.SlotStart).ToList();
            var todays = appointments.FirstOrDefault(a => a.PatientId == Patient.PatientId);
            VisitBadgeHtml = todays == null ? "<span class=\"c360-badge c360-badge--routine\">🟢 Routine Outpatient</span>"
                : todays.Type == "NEW" ? "<span class=\"c360-badge c360-badge--routine\">🟢 New Patient</span>"
                : todays.Type == "FOLLOWUP" ? "<span class=\"c360-badge c360-badge--info\">🔵 Follow-Up</span>"
                : "<span class=\"c360-badge c360-badge--info\">" + HttpUtility.HtmlEncode(todays.Type) + "</span>";
            BindWorklist(appointments);

            var specialty = CurrentUser.SpecialtyId != null ? HospitalService.SpecialtyName(CurrentUser.SpecialtyId) : null;
            if (specialty != null && (CurrentUser.Qualifications ?? "").IndexOf(specialty, StringComparison.OrdinalIgnoreCase) >= 0) specialty = null;
            var sub = string.Join(", ", new[] { CurrentUser.Qualifications, specialty }.Where(x => !string.IsNullOrWhiteSpace(x)));
            PhysicianSub = sub.Length > 0 ? "(" + sub + ")" : "";

            /* A custom-field block's Save button posts its section key (plain HTML inputs, read from the form) */
            var saveCustom = IsPostBack ? Request.Form["c360SaveCustom"] : null;
            if (!string.IsNullOrEmpty(saveCustom)) SaveCustomSection(saveCustom);

            if (!IsPostBack) RefreshAll();
        }

        private static string FormatAgeSex(Patient p)
        {
            var sex = string.IsNullOrEmpty(p.Gender) ? "—" : p.Gender.Substring(0, 1).ToUpperInvariant();
            return (p.AgeYears.HasValue ? p.AgeYears + "y" : "—") + " / " + sex;
        }

        private void BindWorklist(List<AppointmentView> appointments)
        {
            var rows = appointments.Select(a => new WorklistEntry
            {
                Name = a.PatientName,
                Mrn = a.PatientMrn,
                AgeSex = a.Patient != null ? FormatAgeSex(a.Patient) : "",
                Time = EventTime(a),
                StatusLabel = StatusLabel(a.Status),
                StatusTone = a.Status == "IN_CONSULT" ? "active" : a.Status == "CHECKED_IN" ? "waiting" : a.Status == "COMPLETED" ? "done" : "muted",
                Url = Urls.Consultation(a.PatientId),
                IsActive = a.PatientId == Patient.PatientId,
                Initials = string.Concat(a.PatientName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0]))),
            }).ToList();
            WorklistCount.Text = rows.Count + " Patient" + (rows.Count == 1 ? "" : "s");
            WorklistEmpty.Visible = rows.Count == 0;
            WorklistRepeater.DataSource = rows;
            WorklistRepeater.DataBind();
            WorklistRailRepeater.DataSource = rows;
            WorklistRailRepeater.DataBind();
        }

        private void ShowError(string message)
        {
            NotFound.Visible = true;
            NotFoundMessage.Message = message;
            Workspace.Visible = false;
        }

        /* ── IConsultationHost ── */

        public void RefreshAll()
        {
            _consultation = ConsultationService.Get(_consultation.ConsultationId);
            var yes = ConsultationService.GetSymptomStates(_consultation.ConsultationId).Where(kv => kv.Value == "YES")
                .Select(kv => ConsultationService.SymptomLabel(HospitalId, kv.Key)).ToList();
            var flow = BranchedFlowEngine.Evaluate(yes, Patient.AgeYears, Patient.Gender, Hospital.Country);
            VitalsSection.HighlightVitals = flow.HighlightVitals;
            LabsSection.SuggestedLabs = flow.SuggestedLabs;
            BranchedFlowBanner.Text = RenderFlowBanner(flow);
            foreach (var s in Sections) s.Bind();
        }

        public void ActivateStep(string step) { ActiveStepField.Value = step; _stepActivated = true; }

        private static string RenderFlowBanner(BranchedFlowResult r)
        {
            if (r.Banners.Count == 0) return "";
            var html = "<div id=\"branchedFlowBanner\"><div class=\"c360-intel-sub\">Suggested pathway</div>";
            foreach (var b in r.Banners)
                html += "<div class=\"c360-alert c360-alert--info\"><i class=\"ti ti-route c360-alert-icon\" aria-hidden=\"true\"></i><div class=\"c360-alert-text\">" + HttpUtility.HtmlEncode(b) + "</div></div>";
            if (r.SuggestedLabs.Count > 0) html += "<div class=\"c360-intel-note\"><strong>Suggested labs:</strong> " + HttpUtility.HtmlEncode(string.Join(", ", r.SuggestedLabs)) + "</div>";
            if (r.SuggestedIcd.Count > 0) html += "<div class=\"c360-intel-note\"><strong>Consider ICD-10:</strong> " + HttpUtility.HtmlEncode(string.Join(", ", r.SuggestedIcd)) + "</div>";
            return html + "</div>";
        }

        /* ── Workflow state (computed from data every request) ── */

        private void ComputeSteps()
        {
            var cid = _consultation.ConsultationId;
            var vitals = ConsultationService.GetVitals(cid);
            var states = ConsultationService.GetSymptomStates(cid);
            var dx = ConsultationService.ListDiagnoses(cid);
            var orders = ConsultationService.ListOrders(cid);
            var meds = ConsultationService.ListPrescriptions(cid);
            var refs = ConsultationService.ListReferrals(cid);
            var services = ConsultationService.ListServiceItems(cid);
            var c = _consultation;
            Func<bool, string> done = b => b ? "completed" : "pending";
            Func<string, bool, string> skippable = (k, b) => b ? "completed" : c.SkippedSteps.Contains(k) ? "skipped" : "pending";

            var summary = new Dictionary<string, string>
            {
                { "vitals", vitals == null ? "" : string.Join(" · ", new[] {
                    vitals.TempValue.HasValue ? VitalUnitService.FormatWithUnit(FormConfig.DefaultUnit("TEMP"), vitals.TempValue.Value) : null,
                    vitals.BpSystolic.HasValue ? VitalUnitService.Format(FormConfig.DefaultUnit("BP_SYS"), vitals.BpSystolic.Value) + "/" + (vitals.BpDiastolic.HasValue ? VitalUnitService.FormatWithUnit(FormConfig.DefaultUnit("BP_DIA"), vitals.BpDiastolic.Value) : "—") : null,
                    vitals.PulseBpm.HasValue ? VitalUnitService.FormatWithUnit(FormConfig.DefaultUnit("PULSE"), vitals.PulseBpm.Value) : null }.Where(x => x != null)) },
                { "complaints", string.Join(", ", states.Where(kv => kv.Value == "YES").Select(kv => ConsultationService.SymptomLabel(HospitalId, kv.Key))) },
                { "diagnosis", string.Join(", ", dx.Select(d => d.Icd10Code)) },
                { "soap", string.IsNullOrEmpty(c.SubjectiveText) ? "" : (c.SubjectiveText.Length > 40 ? c.SubjectiveText.Substring(0, 40) + "..." : c.SubjectiveText) },
                { "labs", orders.Count > 0 ? orders.Count + " Ordered" : c.SkippedSteps.Contains("labs") ? "Not Needed" : "" },
                { "cpoe", meds.Count > 0 ? meds.Count + " Prescribed" : "" },
                { "services", services.Count > 0 ? services.Count + " Added" : c.SkippedSteps.Contains("services") ? "Not Needed" : "" },
                { "referral", refs.Count > 0 ? refs[0].Specialty : c.SkippedSteps.Contains("referral") ? "Not Needed" : "" },
                { "followup", c.FollowupDate.HasValue ? FmtDate(c.FollowupDate.Value) : c.SkippedSteps.Contains("followup") ? "Not Needed" : "" },
                { "notes", ConsultationService.GetConfidentialNote(cid, UserId) != null ? "Saved" : "" },
            };
            var state = new Dictionary<string, string>
            {
                { "vitals", done(ConsultationService.HasAnyVital(vitals)) },
                { "complaints", done(states.Values.Any(v => v == "YES" || v == "NO")) },
                { "diagnosis", done(dx.Count > 0) },
                { "soap", done(!string.IsNullOrEmpty(c.SubjectiveText) || !string.IsNullOrEmpty(c.ObjectiveText) || !string.IsNullOrEmpty(c.PlanText)) },
                { "labs", skippable("labs", orders.Count > 0) },
                { "cpoe", done(meds.Count > 0) },
                { "services", skippable("services", services.Count > 0) },
                { "referral", skippable("referral", refs.Count > 0) },
                { "followup", skippable("followup", c.FollowupDate.HasValue) },
                { "notes", "pending" },
            };
            _steps = Steps.Select(s => new WorkflowStep { Key = s[0], Label = s[1], Icon = s[2], Mandatory = s[3] == "1", State = state[s[0]], Summary = summary[s[0]] })
                .Concat(new[] { new WorkflowStep { Key = "notes", Label = "Confidential Notes", Icon = "ti-lock", State = "pending", Summary = summary["notes"] } })
                .ToDictionary(s => s.Key);

            /* Custom sections: complete once something is recorded and every required field is filled */
            foreach (var s in FormConfig.CustomSections)
            {
                var fields = FormConfig.CustomFields(s.Key);
                var filled = fields.Count(HasSavedValue);
                var missingRequired = fields.Any(f => f.RequiredAtSignOff && !HasSavedValue(f));
                _steps[s.Key] = new WorkflowStep
                {
                    Key = s.Key, Label = s.Label, Icon = s.Icon, Mandatory = fields.Any(f => f.RequiredAtSignOff),
                    State = filled > 0 && !missingRequired ? "completed" : "pending",
                    Summary = filled > 0 ? filled + " Of " + fields.Count + " Filled" : "",
                };
            }
        }

        /* ── Custom sections & fields ── */

        private Dictionary<string, string> CustomValues
        {
            get { return _customValues ?? (_customValues = ConsultationConfigService.GetCustomValues(_consultation.ConsultationId)); }
        }

        private bool HasSavedValue(ConsultFormField f)
        {
            string v;
            return CustomValues.TryGetValue(f.Key, out v) && !string.IsNullOrEmpty(v);
        }

        /// <summary>What a custom input shows: the value just posted with its section (keeps unsaved typing across other actions), else the saved one.</summary>
        private string CustomValue(string sectionKey, string fieldKey)
        {
            if (IsPostBack && !IsLocked && Request.Form["cfs_" + sectionKey] != null)
                return string.Join("|", (Request.Form.GetValues(CustomFieldRenderer.InputName(fieldKey)) ?? new string[0]).Where(x => !string.IsNullOrEmpty(x)));
            string v;
            return CustomValues.TryGetValue(fieldKey, out v) ? v : "";
        }

        private void SaveCustomSection(string sectionKey)
        {
            var section = FormConfig.GetSection(sectionKey);
            var fields = FormConfig.CustomFields(sectionKey);
            if (section == null || fields.Count == 0) return;
            ActivateStep(sectionKey);
            if (IsLocked) { ToastWarning("Consultation is locked", "This record was signed and locked. Corrections require an addendum."); return; }
            var values = new Dictionary<string, string>();
            foreach (var f in fields)
            {
                var posted = (Request.Form.GetValues(CustomFieldRenderer.InputName(f.Key)) ?? new string[0])
                    .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).ToList();
                var v = f.InputType == "MULTI" ? string.Join("|", posted.Where(k => f.Values.Any(o => o.Key == k)).Distinct()) : posted.FirstOrDefault() ?? "";
                double number;
                switch (f.InputType)
                {
                    case "NUMBER":
                        if (v.Length > 0 && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) _customErrors[f.Key] = "Enter a number";
                        break;
                    case "DATE":
                        if (v.Length > 0 && ParseDate(v) == null) _customErrors[f.Key] = "Enter a valid date";
                        break;
                    case "YESNO":
                        if (v.Length > 0 && v != "YES" && v != "NO") _customErrors[f.Key] = "Choose Yes or No";
                        break;
                    case "SELECT":
                        if (v.Length > 0 && !f.Values.Any(o => o.Key == v)) _customErrors[f.Key] = "Choose one of the options";
                        break;
                    case "TEXTAREA":
                        if (v.Length > 2000) v = v.Substring(0, 2000);
                        break;
                    default:
                        if (v.Length > 250) v = v.Substring(0, 250);
                        break;
                }
                values[f.Key] = v;
            }
            if (_customErrors.Count > 0) { ToastWarning("Please fix the highlighted fields", section.Label); return; }
            ConsultationConfigService.SaveCustomValues(HospitalId, _consultation.ConsultationId, sectionKey, values, UserId);
            _customValues = null;
            Audit("UPDATE", "ConsultationCustomFields", _consultation.ConsultationId, section.Label);
            ToastSuccess(section.IsCustom ? section.Label + " saved" : "Additional fields saved", section.IsCustom ? null : section.Label);
        }

        /// <summary>Custom fields at the foot of a built-in card.</summary>
        public string CustomFieldsHtml(string step)
        {
            var section = FormConfig.GetSection(step);
            if (section == null || section.IsCustom || _consultation == null) return "";
            return CustomFieldRenderer.Block(section, FormConfig.CustomFields(step), k => CustomValue(step, k), _customErrors, IsLocked, false);
        }

        /// <summary>A custom section's whole card.</summary>
        public string CustomCardHtml(string step)
        {
            var section = FormConfig.GetSection(step);
            if (section == null || !section.IsCustom || !section.Enabled || !_steps.ContainsKey(step)) return "";
            var fields = FormConfig.CustomFields(step);
            return CardOpenHtml(step)
                + (string.IsNullOrEmpty(section.Description) ? "" : "<div class=\"text-xs text-muted mb-2\">" + HttpUtility.HtmlEncode(section.Description) + "</div>")
                + CustomFieldRenderer.Block(section, fields, k => CustomValue(step, k), _customErrors, IsLocked, true)
                + "</div></div>";
        }

        /// <summary>Enabled workflow steps in the configured order (the context card is not a workflow step).</summary>
        private List<WorkflowStep> OrderedSteps()
        {
            return FormConfig.SectionOrder.Where(k => _steps.ContainsKey(k) && FormConfig.SectionOn(k)).Select(k => _steps[k]).ToList();
        }

        /// <summary>Opens a module card: header (icon, title, required tag, summary, state badge) and body.</summary>
        public string CardOpenHtml(string key)
        {
            if (key == "context")
                return "<div class=\"c360-card\" id=\"card-context\" data-step=\"context\"><div class=\"c360-card-head\"><span class=\"c360-card-title\">"
                    + "<i class=\"ti ti-user-heart\" aria-hidden=\"true\"></i> Patient Context &amp; History</span></div><div class=\"c360-card-body c360-card-body--flush\">";
            WorkflowStep s;
            if (!_steps.TryGetValue(key, out s)) return "<div><div>";
            var badge = s.State == "completed" ? "<span class=\"c360-badge c360-badge--routine\">✓ Complete</span>"
                : s.State == "skipped" ? "<span class=\"c360-badge c360-badge--muted\">Not Needed</span>"
                : s.Mandatory ? "<span class=\"c360-badge c360-badge--warn\">Required</span>" : "";
            return "<div class=\"c360-card c360-card--" + s.State + "\" id=\"card-" + key + "\" data-step=\"" + key + "\">"
                + "<div class=\"c360-card-head\"><span class=\"c360-card-title\"><i class=\"ti " + s.Icon + "\" aria-hidden=\"true\"></i> " + HttpUtility.HtmlEncode(s.Label) + "</span>"
                + "<span class=\"c360-card-meta\">" + (string.IsNullOrEmpty(s.Summary) ? "" : "<span class=\"c360-card-summary\">" + HttpUtility.HtmlEncode(s.Summary) + "</span>") + badge + "</span></div>"
                + "<div class=\"c360-card-body\" id=\"cardBody-" + key + "\">";
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            if (_consultation == null) return;
            ComputeSteps();

            /* Cards and stepper follow the published configuration: order, and only the enabled sections */
            CardList.Order = FormConfig.SectionOrder;
            foreach (var card in CardList.Cards) card.Visible = FormConfig.SectionOn(card.Step);
            //RxLinkHolder.Visible = FormConfig.SectionOn("cpoe");
            var context = new WorkflowStep { Key = "context", Label = "Patient Context", Icon = "ti-user-heart", State = "info" };
            StepsRepeater.DataSource = FormConfig.SectionOrder.Where(k => FormConfig.SectionOn(k) && (k == "context" || _steps.ContainsKey(k)))
                .Select(k => k == "context" ? context : _steps[k]).ToList();
            StepsRepeater.DataBind();

            /* Clinical Intelligence: live contradictions + completion progress */
            var alerts = Contradictions();
            IntelAlertsRepeater.DataSource = alerts;
            IntelAlertsRepeater.DataBind();
            NoAlertsHolder.Visible = alerts.Count == 0;
            var workflow = OrderedSteps().Where(s => s.Key != "notes").ToList();
            var done = workflow.Count(s => s.State == "completed" || s.State == "skipped");
            ProgressPercent = workflow.Count == 0 ? 0 : done * 100 / workflow.Count;
            ProgressText.Text = done + " Of " + workflow.Count + " Sections Complete";
            var missing = IsLocked ? new List<string>() : MissingMandatory();
            RequiredMissingHolder.Visible = missing.Count > 0;
            RequiredMissingText.Text = HttpUtility.HtmlEncode(string.Join(", ", missing));

            SignOffHolder.Visible = !IsLocked;
            LockedBadgeHolder.Visible = IsLocked;
            LockedHolder.Visible = IsLocked;
            if (IsLocked)
            {
                BillingLink.NavigateUrl = Urls.Checkout(_consultation.ConsultationId);
                BillingLink.Visible = Can("billing");
                PrintHolder.Visible = Can("print");
                AddendaRepeater.DataSource = ConsultationService.ListAddenda(_consultation.ConsultationId);
                AddendaRepeater.DataBind();
            }
        }

        protected string AddendumCategoryLabel(string code)
        {
            switch (code)
            {
                case "RX_CHANGE": return "Rx Change";
                case "NEW_LAB_DATA": return "New Lab Data";
                case "CLINICAL_NOTE": return "Clinical Note";
                default: return "Typo/Clarification";
            }
        }

        /* ── Sign-off ── */

        private List<string> MissingMandatory()
        {
            var cid = _consultation.ConsultationId;
            var missing = new List<string>();
            if (!ConsultationService.HasAnyVital(ConsultationService.GetVitals(cid))) missing.Add("Vitals");
            if (!ConsultationService.GetSymptomStates(cid).Values.Any(v => v == "YES" || v == "NO")) missing.Add("Complaints");
            if (ConsultationService.ListDiagnoses(cid).Count == 0) missing.Add("Diagnosis");
            /* Custom fields marked "required before sign-off", in shown sections */
            foreach (var key in FormConfig.SectionOrder.Where(FormConfig.SectionOn))
                foreach (var f in FormConfig.CustomFields(key).Where(f => f.RequiredAtSignOff && !HasSavedValue(f)))
                    missing.Add(FormConfig.GetSection(key).Label + " › " + f.Label);
            return missing;
        }

        /// <summary>"Your own words" observations still empty — only for the notes the configuration shows.</summary>
        private List<string> MissingPersonal()
        {
            var c = ConsultationService.Get(_consultation.ConsultationId);
            var missing = new List<string>();
            if (!FormConfig.FieldOn("soap", "personal")) return missing;
            if (FormConfig.FieldOn("soap", "subjective") && string.IsNullOrWhiteSpace(c.SubjectivePersonal)) missing.Add("Subjective");
            if (FormConfig.FieldOn("soap", "objective") && string.IsNullOrWhiteSpace(c.ObjectivePersonal)) missing.Add("Objective");
            if (FormConfig.FieldOn("soap", "plan") && string.IsNullOrWhiteSpace(c.PlanPersonal)) missing.Add("Plan");
            return missing;
        }

        private List<ClinicalAlert> Contradictions()
        {
            var cid = _consultation.ConsultationId;
            var yes = ConsultationService.GetSymptomStates(cid).Where(kv => kv.Value == "YES").Select(kv => ConsultationService.SymptomLabel(HospitalId, kv.Key)).ToList();
            return ContradictionEngine.Evaluate(ConsultationService.GetVitals(cid), ConsultationService.ListDiagnoses(cid), yes,
                ConsultationService.ListPrescriptions(cid), Patient.AgeYears);
        }

        protected void OpenSignOffButton_Click(object sender, EventArgs e)
        {
            if (IsLocked) return;
            var dx = ConsultationService.ListDiagnoses(_consultation.ConsultationId);
            var primary = dx.FirstOrDefault(d => d.IsPrimary) ?? dx.FirstOrDefault();
            SignSummary.Text = HttpUtility.HtmlEncode(Patient.FullName + " (" + Patient.Mrn + ") — Primary Diagnosis: "
                + (primary != null ? primary.Icd10Code + " " + primary.Icd10Description : "not recorded"));
            var contradictions = Contradictions();
            ContradictionsHolder.Visible = contradictions.Count > 0;
            ContradictionCount.Text = contradictions.Count + " Issue" + (contradictions.Count > 1 ? "s" : "");
            ContradictionsRepeater.DataSource = contradictions;
            ContradictionsRepeater.DataBind();
            var mandatory = MissingMandatory();
            MandatoryHolder.Visible = mandatory.Count > 0;
            MandatoryMissing.Text = HttpUtility.HtmlEncode(string.Join(", ", mandatory));
            var personal = MissingPersonal();
            PersonalHolder.Visible = personal.Count > 0;
            PersonalMissing.Text = HttpUtility.HtmlEncode(string.Join(", ", personal));
            AttestS.Checked = AttestO.Checked = AttestA.Checked = AttestP.Checked = AttestSkipPersonal.Checked = false;
            ConfirmSignOffButton.Enabled = mandatory.Count == 0;
            SignOffModal.Visible = true;
        }

        protected void CloseSignOffButton_Click(object sender, EventArgs e) { SignOffModal.Visible = false; }

        protected void AttestValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            if (MissingMandatory().Count > 0) { args.IsValid = false; AttestValidator.Text = "Complete the required sections first."; return; }
            if (!(AttestS.Checked && AttestO.Checked && AttestA.Checked && AttestP.Checked)) { args.IsValid = false; AttestValidator.Text = "Confirm all four sections have been reviewed."; return; }
            if (MissingPersonal().Count > 0 && !AttestSkipPersonal.Checked) { args.IsValid = false; AttestValidator.Text = "Fill in your personal observations, or confirm you choose to sign off without them."; return; }
            var shortages = InventoryService.Shortages(_consultation.ConsultationId);
            if (shortages.Count > 0) { args.IsValid = false; AttestValidator.Text = HttpUtility.HtmlEncode("Not enough stock — reduce or remove in Services & Procedures: " + string.Join("; ", shortages)); return; }
            args.IsValid = true;
        }

        protected void ConfirmSignOffButton_Click(object sender, EventArgs e)
        {
            if (IsLocked) return;
            Validate("SignOff");
            if (!IsValid) { SignOffModal.Visible = true; return; }
            var cid = _consultation.ConsultationId;
            // Stock is deducted all-or-nothing before the lock; another visit may have used it since the check above.
            var shortages = InventoryService.ConsumeForConsultation(cid, UserId);
            if (shortages.Count > 0)
            {
                AttestValidator.IsValid = false;
                AttestValidator.Text = HttpUtility.HtmlEncode("Stock ran out — reduce or remove in Services & Procedures: " + string.Join("; ", shortages));
                SignOffModal.Visible = true;
                return;
            }
            foreach (var c in Contradictions()) Audit("ACKNOWLEDGE_CONTRADICTION", "Contradiction", cid, c.Type + ": " + c.Message);
            ConsultationService.SignOff(cid, UserId);
            Audit("SIGN_OFF", "Consultation", cid, "4-point attestation confirmed");

            var existing = BillingService.GetInvoiceForConsultation(cid);
            if (existing == null)
            {
                var inv = BillingService.GenerateInvoiceForConsultation(cid);
                Audit("CREATE", "Invoice", inv.InvoiceId, "Auto-generated at sign-off");
                ToastInfo("Invoice " + inv.InvoiceNumber + " auto-generated", inv.ChargeNote);
            }
            ToastSuccess("Consultation signed and locked", "Visit marked as completed");
            Go(Urls.Consultation(Patient.PatientId) + "?cid=" + HttpUtility.UrlEncode(cid));
        }

        protected void SaveAddendumButton_Click(object sender, EventArgs e)
        {
            Validate("Addendum");
            if (!IsValid || !IsLocked) return;
            var text = AddendumText.Text.Trim();
            ConsultationService.AddAddendum(_consultation.ConsultationId, UserId, AddendumCategory.SelectedValue, text);
            Audit("CREATE", "Addendum", _consultation.ConsultationId, "[" + AddendumCategory.SelectedValue + "] " + (text.Length > 100 ? text.Substring(0, 100) : text));
            AddendumText.Text = "";
            ToastSuccess("Addendum added");
        }
    }
}
