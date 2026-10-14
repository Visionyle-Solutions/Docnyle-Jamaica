using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>
    /// Patient context storyboard: visit history, medical history checklist, recent labs, current meds,
    /// medical events, pre-visit intake &amp; consent, family &amp; social history. Rows expand on the server.
    /// </summary>
    public partial class tier1_context_panel : ConsultationSection
    {
        private static readonly string[] Months = { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        protected override string SectionKey { get { return "context"; } }

        protected string Expanded
        {
            get { return ViewState["Expanded"] as string; }
            set { ViewState["Expanded"] = value; }
        }

        private Patient Patient { get { return Host.Patient; } }

        public override void Bind()
        {
            var pid = Patient.PatientId;
            var visits = ConsultationService.ListPastVisits(pid).Where(v => v.ConsultationId != ConsultationId).ToList();
            var history = PatientService.ListMedicalHistory(pid);
            var labs = LabService.ListForPatient(pid);
            var meds = ConsultationService.ListActiveMedications(pid);
            var events = PatientService.ListMedicalEvents(pid);
            var family = PatientService.ListFamilyHistory(pid);
            var appt = SchedulingService.AppointmentForConsultation(Visit);
            var intake = appt != null ? SchedulingService.GetIntakeForAppointment(appt.AppointmentId) : null;
            var consent = SchedulingService.GetConsentRecord(Visit, appt);

            RowsRepeater.DataSource = new List<ContextRow>
            {
                new ContextRow { Key = "visitHistory", Label = "Visit History", Summary = visits.Count == 0 ? "No previous visits" : visits.Count + " Visits · Last: " + AppPage.FmtDate(visits[0].VisitDate) },
                new ContextRow { Key = "medicalHistory", Label = "Medical History", Summary = history.Count == 0 ? "—" : string.Join(", ", history.Select(h => h.ConditionName + (h.OnsetYear.HasValue ? " (" + h.OnsetYear + ")" : ""))) },
                new ContextRow { Key = "recentLabs", Label = "Recent Labs", Summary = labs.Count == 0 ? "No results" : labs.Count + " Results · Latest: " + labs[0].TestName },
                new ContextRow { Key = "currentMeds", Label = "Current Medications", Summary = meds.Count == 0 ? "None" : string.Join(", ", meds.Select(m => m.MedicineName).Take(3)) },
                new ContextRow { Key = "medicalEvents", Label = "Medical Events", Summary = events.Count == 0 ? "—" : string.Join(", ", events.Select(e => e.EventName)) },
                new ContextRow { Key = "intakeConsent", Label = "Intake & Consent", Summary = "Intake " + (intake != null ? "✓" : "Not Submitted") + " · Consent " + (consent != null ? "✓ Signed" : "Pending") },
                new ContextRow { Key = "familySocial", Label = "Family & Social", Summary = family.Count == 0 ? "—" : string.Join(", ", family.Select(f => OpdProject.Web.BasePage.TitleCase(f.RelationshipType) + ": " + f.ConditionName)) },
            }.Where(r => FieldOn(r.Key)).ToList();
            /* configured order (Consultation Setup); rows the setup doesn't list yet keep their place at the end */
            var order = Host.FormConfig.FieldOrder(SectionKey);
            RowsRepeater.DataSource = ((List<ContextRow>)RowsRepeater.DataSource)
                .Select((r, i) => new { r, i, rank = order.IndexOf(r.Key) })
                .OrderBy(x => x.rank < 0 ? int.MaxValue : x.rank).ThenBy(x => x.i).Select(x => x.r).ToList();
            if (Expanded != null && !FieldOn(Expanded)) Expanded = null;
            RowsRepeater.DataBind();
            BindDetail();
        }

        protected void RowsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var key = (string)e.CommandArgument;
            Expanded = Expanded == key ? null : key;
            VisitDetail.Visible = false;
            Bind();
        }

        private void BindDetail()
        {
            DetailPanel.Visible = Expanded != null;
            VisitHistoryDetail.Visible = Expanded == "visitHistory";
            MedicalHistoryDetail.Visible = Expanded == "medicalHistory";
            RecentLabsDetail.Visible = Expanded == "recentLabs";
            CurrentMedsDetail.Visible = Expanded == "currentMeds";
            MedicalEventsDetail.Visible = Expanded == "medicalEvents";
            IntakeConsentDetail.Visible = Expanded == "intakeConsent";
            FamilySocialDetail.Visible = Expanded == "familySocial";
            var pid = Patient.PatientId;
            bool locked = Host.IsLocked;

            switch (Expanded)
            {
                case "visitHistory":
                    var visits = ConsultationService.ListPastVisits(pid).Where(v => v.ConsultationId != ConsultationId).ToList();
                    VisitsRepeater.DataSource = visits;
                    VisitsRepeater.DataBind();
                    NoVisits.Visible = visits.Count == 0;
                    break;
                case "medicalHistory":
                    _history = PatientService.ListMedicalHistory(pid);
                    HistoryRepeater.DataSource = HospitalService.ListMedicalHistoryItems(HospitalId, true);
                    HistoryRepeater.DataBind();
                    var other = _history.FirstOrDefault(h => h.ConditionName == "OTHER");
                    OtherHistory.Text = other != null ? other.Notes : "";
                    SaveHistoryButton.Visible = !locked;
                    break;
                case "recentLabs":
                    var labs = LabService.ListForPatient(pid).Take(6).ToList();
                    LabsRepeater.DataSource = labs;
                    LabsRepeater.DataBind();
                    NoLabs.Visible = labs.Count == 0;
                    break;
                case "currentMeds":
                    var meds = ConsultationService.ListActiveMedications(pid);
                    MedsRepeater.DataSource = meds;
                    MedsRepeater.DataBind();
                    NoMeds.Visible = meds.Count == 0;
                    break;
                case "medicalEvents":
                    var events = PatientService.ListMedicalEvents(pid);
                    EventsRepeater.DataSource = events;
                    EventsRepeater.DataBind();
                    var have = new HashSet<string>(events.Select(x => x.EventId));
                    EventPicksRepeater.DataSource = locked ? new List<MedicalEventItem>() : HospitalService.ListMedicalEventItems(HospitalId).Where(i => !have.Contains(i.EventId)).ToList();
                    EventPicksRepeater.DataBind();
                    break;
                case "intakeConsent":
                    BindIntakeConsent(locked);
                    break;
                case "familySocial":
                    FamilyRepeater.DataSource = PatientService.ListFamilyHistory(pid);
                    FamilyRepeater.DataBind();
                    SocialText.Text = "<div class=\"zone-a-item\"><strong>Smoking:</strong> " + H(Patient.SmokingStatus != null ? OpdProject.Web.BasePage.TitleCase(Patient.SmokingStatus) : "—")
                        + (Patient.SmokingPackYears.HasValue ? " (" + Patient.SmokingPackYears + " Pack/Years)" : "")
                        + " · <strong>Alcohol:</strong> " + H(Patient.AlcoholUse != null ? OpdProject.Web.BasePage.TitleCase(Patient.AlcoholUse) : "—") + " · <strong>Occupation:</strong> " + H(Patient.Occupation ?? "—")
                        + " · <strong>Activity:</strong> " + H(Patient.PhysicalActivity != null ? OpdProject.Web.BasePage.TitleCase(Patient.PhysicalActivity) : "—") + " · <strong>Diet:</strong> " + H(Patient.Diet != null ? OpdProject.Web.BasePage.TitleCase(Patient.Diet) : "—") + "</div>";
                    AddFamilyPanel.Visible = !locked;
                    break;
            }
        }

        private static string H(string s) { return HttpUtility.HtmlEncode(s); }

        private List<PatientMedicalHistory> _history = new List<PatientMedicalHistory>();

        protected void HistoryRepeater_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var item = e.Item.DataItem as MedicalHistoryItem;
            if (item == null) return;
            var existing = _history.FirstOrDefault(h => h.ConditionName == item.ItemName);
            var check = (CheckBox)e.Item.FindControl("HasCondition");
            var month = (DropDownList)e.Item.FindControl("SinceMonth");
            var year = (DropDownList)e.Item.FindControl("SinceYear");
            check.Checked = existing != null;
            check.Enabled = month.Enabled = year.Enabled = !Host.IsLocked;
            month.Items.Add(new ListItem("MM", ""));
            for (int m = 1; m <= 12; m++) month.Items.Add(new ListItem(Months[m], m.ToString()));
            year.Items.Add(new ListItem("YYYY", ""));
            for (int y = DateTime.Today.Year; y > DateTime.Today.Year - 60; y--) year.Items.Add(new ListItem(y.ToString(), y.ToString()));
            if (existing != null && existing.OnsetMonth.HasValue) month.SelectedValue = existing.OnsetMonth.Value.ToString();
            if (existing != null && existing.OnsetYear.HasValue && year.Items.FindByValue(existing.OnsetYear.Value.ToString()) != null) year.SelectedValue = existing.OnsetYear.Value.ToString();
        }

        protected void SaveHistoryButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked()) return;
            var pid = Patient.PatientId;
            int count = 0;
            foreach (RepeaterItem row in HistoryRepeater.Items)
            {
                var name = ((HiddenField)row.FindControl("ConditionName")).Value;
                if (((CheckBox)row.FindControl("HasCondition")).Checked)
                {
                    int m, y;
                    var mv = int.TryParse(((DropDownList)row.FindControl("SinceMonth")).SelectedValue, out m) ? m : (int?)null;
                    var yv = int.TryParse(((DropDownList)row.FindControl("SinceYear")).SelectedValue, out y) ? y : (int?)null;
                    PatientService.SaveMedicalHistory(pid, name, mv, yv, null, Host.DoctorId);
                    count++;
                }
                else PatientService.DeleteMedicalHistory(pid, name);
            }
            if (OtherHistory.Text.Trim().Length > 0) PatientService.SaveMedicalHistory(pid, "OTHER", null, null, OtherHistory.Text.Trim(), Host.DoctorId);
            else PatientService.DeleteMedicalHistory(pid, "OTHER");
            AuditService.Record(HospitalId, Host.DoctorId, "UPDATE", "PatientMedicalHistory", pid, count + " chronic condition(s)");
            AppPage.ToastSuccess("Medical history saved");
            Host.RefreshAll();
        }

        protected void VisitsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var id = (string)e.CommandArgument;
            var c = ConsultationService.Get(id);
            if (c == null || c.PatientId != Patient.PatientId) return;
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-weight:600; margin-bottom:6px;\">Visit " + H(AppPage.FmtDate(c.VisitDate)) + " · " + H(UserService.UserName(c.DoctorId)) + "</div>");
            var v = ConsultationService.GetVitals(id);
            if (v != null)
                sb.Append("<div class=\"zone-a-item\"><strong>Vitals:</strong> " + H(string.Join(", ", HospitalService.ListVitalsConfig(c.HospitalId, false)
                    .Where(cfg => ConsultationService.GetVital(v, cfg.VitalCode).HasValue)
                    .Select(cfg => cfg.DisplayLabel + " " + VitalUnitService.FormatWithUnit(Host.FormConfig.DefaultUnit(cfg.VitalCode), ConsultationService.GetVital(v, cfg.VitalCode).Value)))) + "</div>");
            var yes = ConsultationService.GetSymptomStates(id).Where(kv => kv.Value == "YES").Select(kv => ConsultationService.SymptomLabel(c.HospitalId, kv.Key));
            sb.Append("<div class=\"zone-a-item\"><strong>Complaints:</strong> " + H(string.Join(", ", yes)) + "</div>");
            sb.Append("<div class=\"zone-a-item\"><strong>Diagnoses:</strong> " + H(string.Join("; ", ConsultationService.ListDiagnoses(id).Select(d => d.Icd10Code + " " + d.Icd10Description))) + "</div>");
            sb.Append("<div class=\"zone-a-item\"><strong>Prescriptions:</strong> " + H(string.Join("; ", ConsultationService.ListPrescriptions(id).Select(p => p.MedicineName + " " + p.Strength + " " + p.TimesPerDay + "x/day"))) + "</div>");
            if (!string.IsNullOrEmpty(c.PlanText)) sb.Append("<div class=\"zone-a-item\"><strong>Plan:</strong> " + H(c.PlanText) + "</div>");
            if (c.FollowupDate.HasValue) sb.Append("<div class=\"zone-a-item\"><strong>Follow-Up:</strong> " + H(AppPage.FmtDate(c.FollowupDate.Value)) + " " + H(c.FollowupNotes) + "</div>");
            VisitDetailText.Text = sb.ToString();
            VisitDetail.Visible = true;
        }

        protected void EventPicksRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "AddEvent" || !EnsureNotLocked()) return;
            PatientService.AddMedicalEvent(Patient.PatientId, (string)e.CommandArgument, DateTime.Today.Year, "", Host.DoctorId);
            AuditService.Record(HospitalId, Host.DoctorId, "CREATE", "PatientMedicalEvent", Patient.PatientId, (string)e.CommandArgument);
            AppPage.ToastSuccess("Medical event added");
            Host.RefreshAll();
        }

        private void BindIntakeConsent(bool locked)
        {
            var appt = SchedulingService.AppointmentForConsultation(Visit);
            var intake = appt != null ? SchedulingService.GetIntakeForAppointment(appt.AppointmentId) : null;
            IntakeText.Text = intake == null
                ? "<div class=\"zone-a-empty\">No pre-visit intake submitted" + (appt != null ? "" : " (no appointment found for this visit)") + ".</div>"
                : "<div class=\"text-xs text-muted mb-1\">Patient-submitted · " + H(AppPage.FmtDateTime(intake.SubmittedAt)) + "</div>";
            IntakeRepeater.DataSource = intake != null ? intake.Answers : new List<IntakeAnswer>();
            IntakeRepeater.DataBind();

            var record = SchedulingService.GetConsentRecord(Visit, appt);
            var form = SchedulingService.GetConsentForm(HospitalId);
            SignConsentPanel.Visible = record == null && form != null && !locked;
            if (record != null)
                ConsentText.Text = "<div class=\"zone-a-item\" style=\"color:var(--color-success);\"><strong>✓ " + H(record.TitleSnapshot) + "</strong> — signed by " + H(record.SignedByName)
                    + " on " + H(AppPage.FmtDateTime(record.SignedAt)) + " (v" + record.FormVersion + ")</div>";
            else if (form == null)
                ConsentText.Text = "<div class=\"zone-a-empty\">No consent form configured.</div>";
            else
            {
                ConsentText.Text = "<div class=\"zone-a-item\"><strong>" + H(form.Title) + "</strong><div class=\"text-xs\" style=\"white-space:pre-wrap; margin-top:4px;\">" + H(form.BodyText) + "</div></div>";
                if (ConsentSignName.Text.Length == 0) ConsentSignName.Text = Patient.FullName;
            }
        }

        protected void SignConsentButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked()) return;
            var name = ConsentSignName.Text.Trim();
            if (name.Length < 2)
            {
                AppPage.ToastWarning("Enter the patient or guardian name");
                return;
            }
            var record = SchedulingService.RecordConsent(Visit, name);
            if (record == null) { AppPage.ToastWarning("No consent form configured"); return; }
            AuditService.Record(HospitalId, Host.DoctorId, "CREATE", "ConsentRecord", ConsultationId, "Signed by " + name + " (v" + record.FormVersion + ")");
            AppPage.ToastSuccess("Consent recorded");
            Host.RefreshAll();
        }

        protected void AddFamilyButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked()) return;
            var condition = FamilyCondition.Text.Trim();
            if (condition.Length < 2) { AppPage.ToastWarning("Enter the condition"); return; }
            PatientService.AddFamilyHistory(Patient.PatientId, FamilyRelation.SelectedValue, condition, FamilyNotes.Text.Trim());
            AuditService.Record(HospitalId, Host.DoctorId, "CREATE", "PatientFamilyHistory", Patient.PatientId, FamilyRelation.SelectedValue + ": " + condition);
            FamilyCondition.Text = FamilyNotes.Text = "";
            AppPage.ToastSuccess("Family history added");
            Host.RefreshAll();
        }
    }
}
