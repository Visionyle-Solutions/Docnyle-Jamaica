using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Health record → Visits: the patient's signed consultations with their diagnoses and the rest of the visit,
    /// read-only. The page only shows this tab to doctors and nurses.
    /// </summary>
    public partial class patient_visits_tab : UserControl
    {
        public string PatientId { get; set; }

        private BasePage AppPage { get { return (BasePage)Page; } }

        public void Bind()
        {
            var user = AppSession.CurrentUser;
            var canPrint = UserService.HasPermission(user, "print");
            var meds = ConsultationService.ListMedicationHistory(PatientId);
            var visits = ConsultationService.ListPastVisits(PatientId).Select(v =>
            {
                var c = ConsultationService.Get(v.ConsultationId);
                return new VisitRecord
                {
                    DateLabel = AppPage.FmtDate(v.VisitDate),
                    DoctorName = v.DoctorName,
                    PrimaryDiagnosis = v.PrimaryDiagnosis,
                    ActionsHtml = Actions(c, user, canPrint),
                    DetailHtml = Detail(c, meds.Where(m => m.ConsultationId == c.ConsultationId).ToList()),
                };
            }).ToList();
            VisitsRepeater.DataSource = visits;
            VisitsRepeater.DataBind();
            NoVisits.Visible = visits.Count == 0;
        }

        private string Actions(Models.Consultation c, Models.AppUser user, bool canPrint)
        {
            var sb = new StringBuilder();
            /* A doctor can reopen only their own consultation (the consultation page enforces the same rule) */
            if (user.HasRole("DOCTOR") && c.DoctorId == user.UserId)
                Link(sb, Urls.Consultation(c.PatientId) + "?cid=" + HttpUtility.UrlEncode(c.ConsultationId), "ti-stethoscope", "Open Visit", false);
            if (canPrint)
            {
                Link(sb, Urls.Print("summary", c.ConsultationId), "ti-file-text", "Summary", true);
                Link(sb, Urls.Print("prescription", c.ConsultationId), "ti-printer", "Rx", true);
            }
            return sb.ToString();
        }

        private static void Link(StringBuilder sb, string url, string icon, string label, bool newTab)
        {
            sb.Append("<a class=\"btn btn--xs btn--ghost\" href=\"").Append(HttpUtility.HtmlAttributeEncode(url)).Append('"')
              .Append(newTab ? " target=\"_blank\"" : "").Append("><i class=\"ti ").Append(icon).Append("\" aria-hidden=\"true\"></i> ")
              .Append(HttpUtility.HtmlEncode(label)).Append("</a>");
        }

        private string Detail(Models.Consultation c, List<Models.Prescription> meds)
        {
            var cid = c.ConsultationId;
            Func<string, string> H = HttpUtility.HtmlEncode;
            var sb = new StringBuilder("<dl class=\"record-visit-detail\">");
            Action<string, string> row = (label, html) => sb.Append("<dt>").Append(label).Append("</dt><dd>").Append(html).Append("</dd>");

            var dx = ConsultationService.ListDiagnoses(cid);
            row("Diagnoses", dx.Count == 0 ? "—" : string.Join("", dx.Select(d =>
                "<div><strong>" + H(d.Icd10Code) + "</strong> " + H(d.Icd10Description)
                + (d.IsPrimary ? " <span class=\"badge badge--info\" style=\"font-size:10px; padding:0 6px;\">Primary</span>" : "")
                + (string.IsNullOrEmpty(d.Laterality) || d.Laterality == "N/A" ? "" : " · " + H(d.Laterality))
                + (string.IsNullOrEmpty(d.AcuteChronic) || d.AcuteChronic == "N/A" ? "" : " · " + H(d.AcuteChronic))
                + (string.IsNullOrEmpty(d.ClinicalNotes) ? "" : "<div class=\"text-xs text-muted\">" + H(d.ClinicalNotes) + "</div>") + "</div>")));

            var complaints = ConsultationService.GetSymptomStates(cid).Where(kv => kv.Value == "YES")
                .Select(kv => ConsultationService.SymptomLabel(c.HospitalId, kv.Key)).ToList();
            row("Complaints", complaints.Count == 0 ? "—" : H(string.Join(", ", complaints)));

            var vitals = ConsultationService.GetVitals(cid);
            if (vitals != null)
            {
                var parts = new[]
                {
                    vitals.TempValue.HasValue ? "Temp " + vitals.TempValue + "°F" : null,
                    vitals.BpSystolic.HasValue ? "BP " + vitals.BpSystolic + "/" + vitals.BpDiastolic : null,
                    vitals.PulseBpm.HasValue ? "Pulse " + vitals.PulseBpm : null,
                    vitals.WeightKg.HasValue ? "Wt " + vitals.WeightKg + " kg" : null,
                }.Where(p => p != null).ToList();
                if (parts.Count > 0) row("Vitals", H(string.Join(" · ", parts)));
            }

            row("Medicines", meds.Count == 0 ? "—" : H(string.Join("; ", meds.Select(m => m.MedicineName + (string.IsNullOrEmpty(m.Strength) ? "" : " " + m.Strength)
                + (string.IsNullOrEmpty(m.DynamicInstruction) ? " · " + m.TimesPerDay + "x/day" : " · " + m.DynamicInstruction)))));

            var orders = ConsultationService.ListOrders(cid);
            if (orders.Count > 0) row("Tests Ordered", H(string.Join(", ", orders.Select(o => o.TestName))));

            var services = ConsultationService.ListServiceItems(cid);
            if (services.Count > 0) row("Services", H(string.Join("; ", services.Select(ConsultationService.ServiceItemLabel))));
            var refs = ConsultationService.ListReferrals(cid);
            if (refs.Count > 0) row("Referral", H(string.Join("; ", refs.Select(r => r.Specialty))));

            if (c.FollowupDate.HasValue || !string.IsNullOrEmpty(c.FollowupNotes))
                row("Follow-Up", H((c.FollowupDate.HasValue ? AppPage.FmtDate(c.FollowupDate.Value) : "") + (string.IsNullOrEmpty(c.FollowupNotes) ? "" : " — " + c.FollowupNotes)));

            var addenda = ConsultationService.ListAddenda(cid);
            if (addenda.Count > 0)
                row("Addenda", string.Join("", addenda.Select(a => "<div class=\"text-xs\"><span class=\"text-muted\">" + H(AppPage.FmtDateTime(a.CreatedAt)) + "</span> " + H(a.AddendumText) + "</div>")));

            return sb.Append("</dl>").ToString();
        }
    }
}
