using System;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>Health record → Medical History: chronic conditions and surgeries / medical events (list + add/edit form).</summary>
    public partial class patient_history_tab : UserControl
    {
        public string PatientId { get; set; }

        private BasePage AppPage { get { return (BasePage)Page; } }
        protected bool IsEvent { get { return EditingId.Value.Length > 0 ? EditingKind.Value == "EVENT" : RecordType.SelectedValue == "EVENT"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            HealthRecord.FillMonthYear(SinceMonth, SinceYear);
            FillNames();
        }

        public void Bind()
        {
            var conditions = PatientService.ListMedicalHistory(PatientId).OrderBy(h => h.Status == "RESOLVED").ThenByDescending(h => h.OnsetYear ?? 0).ToList();
            ConditionsRepeater.DataSource = conditions;
            ConditionsRepeater.DataBind();
            NoConditions.Visible = conditions.Count == 0;
            var events = PatientService.ListMedicalEvents(PatientId).OrderByDescending(ev => ev.EventYear ?? 0).ThenByDescending(ev => ev.EventMonth ?? 0).ToList();
            EventsRepeater.DataSource = events;
            EventsRepeater.DataBind();
            NoEvents.Visible = events.Count == 0;
        }

        /* ── Display helpers ── */

        protected static string ConditionLabel(PatientMedicalHistory h)
        {
            return h.ConditionName == "OTHER" ? "Other: " + (string.IsNullOrEmpty(h.Notes) ? "—" : h.Notes) : h.ConditionName;
        }

        protected static string SinceLabel(int? month, int? year) { return HealthRecord.SinceLabel(month, year); }

        protected static string StatusBadge(string status)
        {
            return status == "RESOLVED" ? "badge--success" : status == "CONTROLLED" ? "badge--info" : "badge--warning";
        }

        protected string RecordedLabel(string userId, DateTime at)
        {
            return (string.IsNullOrEmpty(userId) ? "" : UserService.UserName(userId) + " · ") + AppPage.FmtDate(at);
        }

        /* ── Form ── */

        private void FillNames()
        {
            var hospitalId = AppSession.CurrentUser.HospitalId;
            NamePick.Items.Clear();
            NamePick.Items.Add(new ListItem("— Select —", ""));
            if (IsEvent)
                foreach (var i in HospitalService.ListMedicalEventItems(hospitalId)) NamePick.Items.Add(new ListItem(i.EventName, i.EventName));
            else
                foreach (var i in HospitalService.ListMedicalHistoryItems(hospitalId, true)) NamePick.Items.Add(new ListItem(i.ItemName, i.ItemName));
            NameLabel.Text = IsEvent ? "Surgery / Event" : "Condition";
            StatusHolder.Visible = !IsEvent;
            NotesInput.Attributes["placeholder"] = IsEvent ? "e.g. Triple vessel CABG" : "e.g. Type 2, on insulin";
        }

        private void SetName(string name)
        {
            var item = NamePick.Items.FindByValue(name ?? "");
            NamePick.ClearSelection();
            if (item != null && item.Value != "") { item.Selected = true; NameOther.Text = ""; }
            else NameOther.Text = name;
        }

        private string EnteredName
        {
            get { var typed = NameOther.Text.Trim(); return typed.Length > 0 ? typed : NamePick.SelectedValue; }
        }

        protected void RecordType_SelectedIndexChanged(object sender, EventArgs e) { FillNames(); }

        protected void NameValidator_ServerValidate(object source, ServerValidateEventArgs args) { args.IsValid = EnteredName.Length > 0; }

        public void Reset()
        {
            EditingId.Value = EditingKind.Value = "";
            RecordType.Enabled = true;
            FillNames();
            NameOther.Text = NotesInput.Text = "";
            SinceMonth.SelectedIndex = SinceYear.SelectedIndex = StatusInput.SelectedIndex = 0;
            FormTitle.Text = "Add To Medical History";
            SaveButton.Text = "Add";
        }

        protected void ResetButton_Click(object sender, EventArgs e) { Reset(); }

        protected void ConditionsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var h = PatientService.GetMedicalHistory((string)e.CommandArgument, PatientId);
            if (h == null || h.PatientId != PatientId) return;
            if (e.CommandName == "Delete")
            {
                PatientService.DeleteMedicalHistoryRecord(h);
                Audit("DELETE", "PatientMedicalHistory", h.PatientMedHistoryId, "Removed " + ConditionLabel(h));
                AppPage.ToastSuccess("Condition removed");
                if (EditingId.Value == h.PatientMedHistoryId) Reset();
                Bind();
                return;
            }
            RecordType.SelectedValue = "CONDITION";
            EditingId.Value = h.PatientMedHistoryId;
            EditingKind.Value = "CONDITION";
            FillNames();
            RecordType.Enabled = false;
            SetName(h.ConditionName == "OTHER" ? h.Notes : h.ConditionName);
            NotesInput.Text = h.ConditionName == "OTHER" ? "" : h.Notes;
            HealthRecord.Select(SinceMonth, h.OnsetMonth.HasValue ? h.OnsetMonth.ToString() : "");
            HealthRecord.Select(SinceYear, h.OnsetYear.HasValue ? h.OnsetYear.ToString() : "");
            HealthRecord.Select(StatusInput, h.Status ?? "ACTIVE");
            FormTitle.Text = "Edit Condition";
            SaveButton.Text = "Update";
        }

        protected void EventsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var ev = PatientService.GetMedicalEvent((string)e.CommandArgument);
            if (ev == null || ev.PatientId != PatientId) return;
            if (e.CommandName == "Delete")
            {
                PatientService.DeleteMedicalEvent(ev.PatientMedEventId);
                Audit("DELETE", "PatientMedicalEvent", ev.PatientMedEventId, "Removed " + ev.EventName);
                AppPage.ToastSuccess("Event removed");
                if (EditingId.Value == ev.PatientMedEventId) Reset();
                Bind();
                return;
            }
            RecordType.SelectedValue = "EVENT";
            EditingId.Value = ev.PatientMedEventId;
            EditingKind.Value = "EVENT";
            FillNames();
            RecordType.Enabled = false;
            SetName(ev.EventName);
            NotesInput.Text = ev.Notes;
            HealthRecord.Select(SinceMonth, ev.EventMonth.HasValue ? ev.EventMonth.ToString() : "");
            HealthRecord.Select(SinceYear, ev.EventYear.HasValue ? ev.EventYear.ToString() : "");
            FormTitle.Text = "Edit Surgery / Event";
            SaveButton.Text = "Update";
        }

        protected void SaveButton_Click(object sender, EventArgs e)
        {
            Page.Validate("MedHistory");
            if (!Page.IsValid) return;
            var user = AppSession.CurrentUser;
            var isEdit = EditingId.Value.Length > 0;
            if (IsEvent)
            {
                var saved = PatientService.SaveMedicalEventRecord(new PatientMedicalEvent
                {
                    PatientMedEventId = isEdit ? EditingId.Value : null, PatientId = PatientId, EventName = EnteredName,
                    EventMonth = HealthRecord.SelectedInt(SinceMonth), EventYear = HealthRecord.SelectedInt(SinceYear),
                    Notes = NotesInput.Text.Trim(), RecordedByUserId = user.UserId,
                });
                Audit(isEdit ? "UPDATE" : "CREATE", "PatientMedicalEvent", saved.PatientMedEventId, saved.EventName);
                AppPage.ToastSuccess(isEdit ? "Event updated" : "Event added");
            }
            else
            {
                var saved = PatientService.SaveMedicalHistoryRecord(new PatientMedicalHistory
                {
                    PatientMedHistoryId = isEdit ? EditingId.Value : null, PatientId = PatientId, ConditionName = EnteredName,
                    OnsetMonth = HealthRecord.SelectedInt(SinceMonth), OnsetYear = HealthRecord.SelectedInt(SinceYear),
                    Status = StatusInput.SelectedValue, Notes = NotesInput.Text.Trim(), RecordedByUserId = user.UserId,
                });
                Audit(isEdit ? "UPDATE" : "CREATE", "PatientMedicalHistory", saved.PatientMedHistoryId, saved.ConditionName + " (" + saved.Status + ")");
                AppPage.ToastSuccess(isEdit ? "Condition updated" : "Condition added");
            }
            Reset();
            Bind();
        }

        private void Audit(string action, string entity, string id, string details)
        {
            var user = AppSession.CurrentUser;
            AuditService.Record(user.HospitalId, user.UserId, action, entity, id, details);
        }
    }
}
