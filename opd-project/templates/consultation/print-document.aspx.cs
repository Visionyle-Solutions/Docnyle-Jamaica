using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>Server-rendered print documents for a consultation (replaces the client-side print generator).</summary>
    public partial class print_document : BasePage
    {
        protected override string RequiredPermission { get { return "print"; } }
        public override string PageTitle { get { return "Print"; } }

        private static string H(string s) { return HttpUtility.HtmlEncode(s ?? ""); }

        protected void Page_Load(object sender, EventArgs e)
        {
            var c = ConsultationService.Get(RouteValue("consultationId"));
            if (c == null || c.HospitalId != HospitalId)
            {
                DocumentHtml.Text = "<p>Consultation not found.</p>";
                return;
            }
            var patient = PatientService.Get(c.PatientId);
            var doc = RouteValue("document");
            var sb = new StringBuilder();
            sb.Append(Letterhead());
            sb.Append(PatientInfo(patient, c));
            string title;
            switch (doc)
            {
                case "summary": title = "Consultation Summary"; Summary(sb, c); break;
                case "lab-requisition": title = "Lab Requisition"; LabRequisition(sb, c); break;
                case "referral": title = "Referral Letter"; ReferralLetter(sb, c, patient); break;
                default: title = "Prescription"; Prescription(sb, c); break;
            }
            sb.Append(Addenda(c));
            sb.Append("<div class=\"print-footer\"><div>Generated: " + H(FmtDateTime(DateTime.UtcNow)) + " · Printed by " + H(CurrentUser.FullName)
                + "</div><div class=\"print-signature\"><div class=\"sig-line\">" + H(UserService.UserName(c.DoctorId)) + "<br/>Doctor's Signature</div></div></div>");
            DocumentHtml.Text = sb.ToString();
            DocumentTitle.Text = H(title + " · " + patient.FullName);
            Page.Title = title + " · " + patient.FullName;
            Audit("PRINT", "Consultation", c.ConsultationId, title);
        }

        private string Letterhead()
        {
            var h = Hospital;
            var compliance = HospitalService.ComplianceLine(h.HospitalId);
            return "<div class=\"print-header\">"
                + (string.IsNullOrEmpty(h.LogoBase64) ? "" : "<img src=\"" + HttpUtility.HtmlAttributeEncode(h.LogoBase64) + "\" style=\"max-height:50px; max-width:150px; margin-bottom:4px;\" alt=\"\" /><br/>")
                + "<h2>" + H(h.Name) + "</h2><div class=\"print-subtitle\">" + H(h.LegalAddress) + "</div>"
                + "<div class=\"print-subtitle\">" + H(string.Join(" · ", new[] { h.ContactNumber, h.PrimaryEmail }.Where(x => !string.IsNullOrEmpty(x)))) + "</div>"
                + (string.IsNullOrEmpty(h.LicenseNumber) ? "" : "<div class=\"print-license\">Reg. No: " + H(h.LicenseNumber) + "</div>")
                + (compliance.Length > 0 ? "<div class=\"print-license\">" + H(compliance) + "</div>" : "")
                + "</div>";
        }

        private string PatientInfo(Patient p, Consultation c)
        {
            return "<div class=\"print-patient-info\"><div><strong>Patient:</strong> " + H(p.FullName) + "<br/><strong>" + H(PatientIdLabel) + ":</strong> " + H(p.Mrn)
                + "<br/><strong>Gender:</strong> " + H(p.Gender) + " · <strong>DOB:</strong> " + H(FmtDate(p.Dob)) + " (" + p.AgeYears + "y)</div>"
                + "<div style=\"text-align:right;\"><strong>Date:</strong> " + H(FmtDate(c.VisitLocalDate)) + "<br/><strong>Doctor:</strong> " + H(UserService.UserName(c.DoctorId))
                + (string.IsNullOrEmpty(p.NationalHealthId) ? "" : "<br/><strong>" + H(HealthIdLabel) + ":</strong> " + H(p.NationalHealthId)) + "</div></div>";
        }

        private static void Section(StringBuilder sb, string label, string body)
        {
            sb.Append("<div class=\"print-section\"><div class=\"print-section-label\">" + H(label) + "</div><div class=\"print-section-body\">" + body + "</div></div>");
        }

        private void Prescription(StringBuilder sb, Consultation c)
        {
            var meds = ConsultationService.ListPrescriptions(c.ConsultationId);
            var dx = ConsultationService.ListDiagnoses(c.ConsultationId);
            if (dx.Count > 0) Section(sb, "Diagnosis", H(string.Join("; ", dx.Select(d => d.Icd10Description + " (" + d.Icd10Code + ")"))));
            Section(sb, "Prescription", MedsTable(meds));
            var validUntil = meds.Where(m => m.ValidUntil.HasValue).Select(m => m.ValidUntil.Value).DefaultIfEmpty(Today.AddDays(Hospital.DefaultRxValidityDays)).Max();
            sb.Append("<div class=\"print-validity\">FOR SINGLE DISPENSING ONLY · Valid until: " + H(FmtDate(validUntil)) + "</div>");
            if (c.FollowupDate.HasValue) Section(sb, "Follow-Up", H(FmtDate(c.FollowupDate.Value) + (string.IsNullOrEmpty(c.FollowupNotes) ? "" : " — " + c.FollowupNotes)));
        }

        private static string MedsTable(System.Collections.Generic.List<Prescription> meds)
        {
            if (meds.Count == 0) return "<p>No medications prescribed.</p>";
            var t = new StringBuilder("<table class=\"print-rx-table\"><thead><tr><th>#</th><th>Medicine</th><th>Strength</th><th>Form</th><th>Dosage</th><th>Duration</th><th>Instructions</th></tr></thead><tbody>");
            int i = 0;
            foreach (var m in meds)
                t.Append("<tr><td>" + (++i) + "</td><td>" + H(m.MedicineName) + "</td><td>" + H(m.Strength) + "</td><td>" + H(m.DrugForm) + "</td><td>"
                    + H(m.TakeInQty + " " + m.TakeInUnit + " · " + m.TimesPerDay + "x/" + (m.FrequencyPeriod ?? "day") + (m.TimingLabel.Length > 0 ? " (" + m.TimingLabel + ")" : ""))
                    + "</td><td>" + H(m.DurationValue + " " + m.DurationUnit) + "</td><td>" + H(m.Instructions) + "</td></tr>");
            return t.Append("</tbody></table>").ToString();
        }

        private void Summary(StringBuilder sb, Consultation c)
        {
            var cid = c.ConsultationId;
            /* Custom field values (Consultation Setup): printed with the built-in section they were added to, and
               custom sections under their own heading, after the examination findings. */
            var custom = ConsultationConfigService.ListCustomValues(c.HospitalId, cid);
            var builtIn = new HashSet<string>(ConsultationConfigService.SectionKeys);
            Action<string, string, string> add = (label, html, sectionKey) =>
            {
                html = (html ?? "") + CustomLines(custom.Where(x => x.SectionKey == sectionKey), html);
                if (html.Length > 0) Section(sb, label, html);
            };

            var v = ConsultationService.GetVitals(cid);
            var units = ConsultationConfigService.GetView(c.HospitalId);   /* vitals print in the hospital's default units */
            add("Vitals", v == null ? "" : H(string.Join(" · ", HospitalService.ListVitalsConfig(c.HospitalId, false)
                    .Where(cfg => ConsultationService.GetVital(v, cfg.VitalCode).HasValue)
                    .Select(cfg => cfg.DisplayLabel + ": " + VitalUnitService.FormatWithUnit(units.DefaultUnit(cfg.VitalCode), ConsultationService.GetVital(v, cfg.VitalCode).Value)))
                    + (v.Bmi.HasValue ? " · BMI " + v.Bmi : "")), "vitals");
            var states = ConsultationService.GetSymptomStates(cid);
            var yes = states.Where(kv => kv.Value == "YES").Select(kv => ConsultationService.SymptomLabel(c.HospitalId, kv.Key)).ToList();
            var no = states.Where(kv => kv.Value == "NO").Select(kv => ConsultationService.SymptomLabel(c.HospitalId, kv.Key)).ToList();
            add("Presenting complaints", yes.Count + no.Count == 0 ? "" : H((yes.Count > 0 ? "Present: " + string.Join(", ", yes) : "") + (no.Count > 0 ? (yes.Count > 0 ? " · " : "") + "Denies: " + string.Join(", ", no) : "")), "complaints");
            if (!string.IsNullOrEmpty(c.SubjectiveText)) Section(sb, "Subjective", Multiline(c.SubjectiveText));
            if (!string.IsNullOrEmpty(c.ObjectiveText)) Section(sb, "Objective", Multiline(c.ObjectiveText));
            add("Additional observations", "", "soap");
            foreach (var group in custom.Where(x => !builtIn.Contains(x.SectionKey)).GroupBy(x => x.SectionKey))
                add(string.IsNullOrEmpty(group.First().SectionLabel) ? "Additional findings" : group.First().SectionLabel, "", group.Key);
            var dx = ConsultationService.ListDiagnoses(cid);
            add("Assessment / Diagnosis", string.Join("<br/>", dx.Select((d, i) => H((i + 1) + ". " + d.Icd10Description + " (" + d.Icd10Code + ")"
                    + (string.IsNullOrEmpty(d.AcuteChronic) ? "" : " — " + d.AcuteChronic) + (string.IsNullOrEmpty(d.ClinicalNotes) ? "" : ". " + d.ClinicalNotes)))), "diagnosis");
            if (!string.IsNullOrEmpty(c.PlanText)) Section(sb, "Plan", Multiline(c.PlanText));
            add("Medications", MedsTable(ConsultationService.ListPrescriptions(cid)), "cpoe");
            var orders = ConsultationService.ListOrders(cid);
            add("Investigations ordered", orders.Count == 0 ? "" : H(string.Join(", ", orders.Select(o => o.TestName + " (" + o.TestType + ")"))), "labs");
            var services = ConsultationService.ListServiceItems(cid);
            add("Services & procedures", string.Join("<br/>", services.Select(s => H(ConsultationService.ServiceItemLabel(s)))), "services");
            var refs = ConsultationService.ListReferrals(cid);
            add("Referrals", string.Join("<br/>", refs.Select(r => H(r.Specialty + (string.IsNullOrEmpty(r.DoctorName) ? "" : " (Dr. " + r.DoctorName + ")") + " — " + r.Urgency + ": " + r.Reason))), "referral");
            add("Follow-Up", c.FollowupDate.HasValue ? H(FmtDate(c.FollowupDate.Value) + (string.IsNullOrEmpty(c.FollowupNotes) ? "" : " — " + c.FollowupNotes)) : "", "followup");
        }

        /// <summary>"Label: value" lines for custom field values (dates in the hospital's format), after any existing section text.</summary>
        private string CustomLines(IEnumerable<ConsultationCustomValue> values, string existing)
        {
            var lines = values.Select(x =>
            {
                var shown = x.DisplayValue ?? x.Value;
                if (x.InputType == "DATE") { var d = ParseDate(x.Value); if (d.HasValue) shown = FmtDate(d.Value); }
                return "<div class=\"print-custom\"><strong>" + H(x.FieldLabel) + ":</strong> " + Multiline(shown) + "</div>";
            }).ToList();
            if (lines.Count == 0) return "";
            return (string.IsNullOrEmpty(existing) ? "" : "<div class=\"print-custom-gap\"></div>") + string.Join("", lines);
        }

        private static string Multiline(string text) { return H(text).Replace("\n", "<br/>"); }

        private void LabRequisition(StringBuilder sb, Consultation c)
        {
            var orders = ConsultationService.ListOrders(c.ConsultationId);
            var dx = ConsultationService.ListDiagnoses(c.ConsultationId);
            if (dx.Count > 0) Section(sb, "Clinical indication", H(string.Join("; ", dx.Select(d => d.Icd10Code + " " + d.Icd10Description))));
            if (orders.Count == 0) { Section(sb, "Tests requested", "<p>No lab orders.</p>"); return; }
            var t = new StringBuilder("<table class=\"print-rx-table\"><thead><tr><th>#</th><th>Order ID</th><th>Test</th><th>Type</th><th>Ordered</th><th>Status</th></tr></thead><tbody>");
            int i = 0;
            foreach (var o in orders) t.Append("<tr><td>" + (++i) + "</td><td><strong>" + H(o.OrderId) + "</strong></td><td>" + H(o.TestName) + "</td><td>" + H(o.TestType) + "</td><td>" + H(FmtDateTime(o.OrderedAt)) + "</td><td>" + H(o.Status) + "</td></tr>");
            Section(sb, "Tests requested", t.Append("</tbody></table>").ToString()
                + "<p><em>These tests are not performed at this clinic and are not included in the clinic bill. Please get them done at a diagnostic lab or imaging centre of your choice. If the lab is a DocNyle partner, show them this requisition: they enter the Order ID and your date of birth and send the report straight to your doctor. Otherwise, bring or send the reports to your doctor.</em></p>");
        }

        private void ReferralLetter(StringBuilder sb, Consultation c, Patient p)
        {
            var refs = ConsultationService.ListReferrals(c.ConsultationId);
            if (refs.Count == 0) { Section(sb, "Referral", "<p>No referrals recorded.</p>"); return; }
            var r = refs.Last();
            var dx = ConsultationService.ListDiagnoses(c.ConsultationId);
            var meds = ConsultationService.ListPrescriptions(c.ConsultationId);
            var body = "<p>To: " + H(string.IsNullOrEmpty(r.DoctorName) ? "The Consultant" : "Dr. " + r.DoctorName) + ", " + H(r.Specialty) + "</p>"
                + "<p>Dear Colleague,</p><p>I am referring " + H(p.FullName) + " (" + H(p.Gender) + ", " + p.AgeYears + " years) for your " + (r.Urgency == "URGENT" ? "<strong>urgent</strong> " : "")
                + "opinion and management.</p><p><strong>Reason for referral:</strong> " + H(r.Reason) + "</p>"
                + (dx.Count > 0 ? "<p><strong>Working diagnosis:</strong> " + H(string.Join("; ", dx.Select(d => d.Icd10Code + " " + d.Icd10Description))) + "</p>" : "")
                + (meds.Count > 0 ? "<p><strong>Current medication:</strong> " + H(string.Join("; ", meds.Select(m => m.MedicineName + " " + m.Strength + " " + m.TimesPerDay + "x/day"))) + "</p>" : "")
                + "<p>Thank you for seeing this patient.</p>";
            Section(sb, "Referral letter", body);
        }

        private string Addenda(Consultation c)
        {
            var list = ConsultationService.ListAddenda(c.ConsultationId);
            if (list.Count == 0) return "";
            var sb = new StringBuilder("<div class=\"print-section\"><div class=\"print-section-label\">Addendums</div>");
            int i = 0;
            foreach (var a in list) sb.Append("<div class=\"print-addendum\"><strong>#" + (++i) + "</strong> (" + H(FmtDateTime(a.CreatedAt)) + ") [" + H(a.Category) + "]: " + H(a.AddendumText) + "</div>");
            return sb.Append("</div>").ToString();
        }
    }
}
