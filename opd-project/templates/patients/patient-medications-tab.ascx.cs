using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Health record → Current Medications. Consultation prescriptions are listed read-only (they are changed from
    /// a consultation); medicines prescribed elsewhere are added, edited and discontinued here, after the same
    /// non-bypassable drug-allergy check the prescription card uses.
    /// </summary>
    public partial class patient_medications_tab : UserControl
    {
        public string PatientId { get; set; }

        protected BasePage AppPage { get { return (BasePage)Page; } }

        private static readonly string[][] Frequencies =
        {
            new[] { "OD", "Once daily", "1" }, new[] { "BD", "Twice daily", "2" }, new[] { "TDS", "Three times daily", "3" },
            new[] { "QID", "Four times daily", "4" }, new[] { "HS", "At bedtime", "1" }, new[] { "WEEKLY", "Once a week", "0" },
            new[] { "PRN", "As needed", "0" },
        };

        protected void Page_Load(object sender, EventArgs e)
        {
            var options = new StringBuilder();
            foreach (var d in CatalogService.SearchFormulary(AppSession.CurrentUser.HospitalId, "", false).Select(d => d.BrandName).Distinct())
                options.Append("<option value=\"").Append(HttpUtility.HtmlAttributeEncode(d)).Append("\"></option>");
            FormularyOptions.Text = options.ToString();
        }

        public void Bind()
        {
            var meds = ConsultationService.ListMedicationHistory(PatientId).Where(p => ShowPast.Checked || IsCurrent(p)).ToList();
            MedsRepeater.DataSource = meds;
            MedsRepeater.DataBind();
            NoMeds.Visible = meds.Count == 0;
            NoMedsText.Text = ShowPast.Checked ? "No medicines on record." : "No current medications.";
        }

        protected void ShowPast_CheckedChanged(object sender, EventArgs e) { Bind(); }

        /* ── Display helpers ── */

        protected static bool IsCurrent(Prescription p) { return ConsultationService.IsCurrent(p); }

        protected static string FormLine(Prescription p)
        {
            return string.Join(" · ", new[] { p.DrugForm, p.DeliveryRoute }.Where(x => !string.IsNullOrEmpty(x)));
        }

        protected static string DoseLine(Prescription p)
        {
            if (!string.IsNullOrEmpty(p.DynamicInstruction)) return p.DynamicInstruction;
            var timing = string.IsNullOrEmpty(p.TimingLabel) ? "" : " (" + p.TimingLabel + ")";
            return p.TakeInQty + " " + (p.TakeInUnit ?? "") + " · " + p.TimesPerDay + "x/" + (p.FrequencyPeriod ?? "day") + timing;
        }

        protected string UntilHtml(Prescription p)
        {
            if (p.Status == "DISCONTINUED") return "<span class=\"badge badge--danger\">Discontinued</span>";
            if (!IsCurrent(p)) return "<span class=\"badge badge--success\">Completed</span> <span class=\"text-xs text-muted\">" + HttpUtility.HtmlEncode(AppPage.FmtDate(p.PeriodEnd)) + "</span>";
            return p.PeriodEnd.HasValue ? HttpUtility.HtmlEncode(AppPage.FmtDate(p.PeriodEnd.Value)) : "<span class=\"text-muted\">Ongoing</span>";
        }

        protected static string SourceLine(Prescription p)
        {
            if (p.IsOutside) return p.SourceDetail ?? "";
            var c = string.IsNullOrEmpty(p.ConsultationId) ? null : ConsultationService.Get(p.ConsultationId);
            return c != null ? UserService.UserName(c.DoctorId) : "";
        }

        /* ── Form ── */

        protected void Since_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var d = ParseMonth(args.Value);
            args.IsValid = d.HasValue && d.Value <= ((OpdProject.Web.BasePage)Page).Today;
        }

        private static DateTime? ParseMonth(string value)
        {
            DateTime d;
            return DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }

        public void Reset()
        {
            EditingId.Value = "";
            MedicineInput.Text = StrengthInput.Text = DoseInput.Text = SinceInput.Text = SourceInput.Text = NotesInput.Text = "";
            FormInput.SelectedIndex = FrequencyInput.SelectedIndex = 0;
            FormTitle.Text = "Add Outside Medication";
            SaveButton.Text = "Add";
            AllergyAlertPanel.Visible = false;
        }

        protected void ResetButton_Click(object sender, EventArgs e) { Reset(); }
        protected void CancelAlertButton_Click(object sender, EventArgs e) { AllergyAlertPanel.Visible = false; }

        protected void MedsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var p = ConsultationService.GetPrescription((string)e.CommandArgument, PatientId);
            if (p == null || p.PatientId != PatientId || !p.IsOutside) return;
            if (e.CommandName == "Discontinue")
            {
                ConsultationService.DiscontinuePrescription(p.PrescriptionId);
                Audit("UPDATE", p.PrescriptionId, "Discontinued outside medication " + p.MedicineName);
                AppPage.ToastSuccess("Medication discontinued");
                if (EditingId.Value == p.PrescriptionId) Reset();
                Bind();
                return;
            }
            EditingId.Value = p.PrescriptionId;
            MedicineInput.Text = p.MedicineName;
            StrengthInput.Text = p.Strength;
            HealthRecord.Select(FormInput, p.DrugForm);
            DoseInput.Text = (p.TakeInQty > 0 ? p.TakeInQty.ToString(CultureInfo.InvariantCulture) + " " : "") + (p.TakeInUnit ?? "");
            DoseInput.Text = DoseInput.Text.Trim();
            HealthRecord.Select(FrequencyInput, p.FrequencyMode);
            SinceInput.Text = p.PeriodStart.HasValue ? p.PeriodStart.Value.ToString("yyyy-MM") : "";
            SourceInput.Text = p.SourceDetail;
            NotesInput.Text = p.Instructions;
            FormTitle.Text = "Edit Outside Medication";
            SaveButton.Text = "Update";
            AllergyAlertPanel.Visible = false;
        }

        protected void SaveButton_Click(object sender, EventArgs e) { Save(false); }
        protected void RecordAnywayButton_Click(object sender, EventArgs e) { Save(true); }

        private void Save(bool overrideAllergy)
        {
            Page.Validate("OutsideMed");
            if (!Page.IsValid) return;
            var name = MedicineInput.Text.Trim();
            var isEdit = EditingId.Value.Length > 0;

            /* Same safety check as the prescription card; an override is audited per conflict. */
            var drug = CatalogService.GetDrugByName(name);
            var alerts = ConsultationService.CheckDrugAllergyConflicts(PatientId, name, drug != null ? drug.Composition : null);
            if (alerts.Count > 0 && !overrideAllergy)
            {
                AllergyAlertsRepeater.DataSource = alerts;
                AllergyAlertsRepeater.DataBind();
                AllergyAlertPanel.Visible = true;
                return;
            }

            var freq = Frequencies.First(f => f[0] == FrequencyInput.SelectedValue);
            double qty = 0;
            string unit = null;
            var dose = DoseInput.Text.Trim();
            var m = Regex.Match(dose, @"^\s*(\d+(?:\.\d+)?)\s*(.*)$");
            if (m.Success) { qty = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); unit = m.Groups[2].Value.Trim(); }
            else if (dose.Length > 0) unit = dose;

            var saved = ConsultationService.SaveOutsideMedication(new Prescription
            {
                PrescriptionId = isEdit ? EditingId.Value : null, PatientId = PatientId, Category = "OUTSIDE",
                MedicineName = name, Strength = StrengthInput.Text.Trim(), DrugForm = FormInput.SelectedValue,
                TakeInQty = qty, TakeInUnit = string.IsNullOrEmpty(unit) ? null : unit,
                TimesPerDay = int.Parse(freq[2]), FrequencyMode = freq[0], TimingNi = freq[0] == "HS", Prn = freq[0] == "PRN",
                DynamicInstruction = (dose.Length > 0 ? dose + " · " : "") + freq[1].ToLowerInvariant(),
                PeriodStart = ParseMonth(SinceInput.Text), SourceDetail = SourceInput.Text.Trim(), Instructions = NotesInput.Text.Trim(),
            });
            foreach (var a in alerts) Audit("OVERRIDE_SAFETY", saved.PrescriptionId, a.Message + " | Recorded as outside medication");
            Audit(isEdit ? "UPDATE" : "CREATE", saved.PrescriptionId, "Outside medication " + saved.MedicineName + " " + saved.Strength);
            AppPage.ToastSuccess(isEdit ? "Medication updated" : "Medication added");
            Reset();
            Bind();
        }

        private void Audit(string action, string id, string details)
        {
            var user = AppSession.CurrentUser;
            AuditService.Record(user.HospitalId, user.UserId, action, action == "OVERRIDE_SAFETY" ? "DrugAllergyConflict" : "Prescription", id, details);
        }
    }
}
