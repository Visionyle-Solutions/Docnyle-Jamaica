using System;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>Health record → Family &amp; Social: family history (list + add/edit form) and the social history card.</summary>
    public partial class patient_family_social_tab : UserControl
    {
        public string PatientId { get; set; }

        private BasePage AppPage { get { return (BasePage)Page; } }

        public void Bind()
        {
            BindFamily();
            var p = PatientService.Get(PatientId);
            HealthRecord.Select(SmokingInput, p.SmokingStatus);
            PackYearsInput.Text = p.SmokingPackYears.HasValue ? p.SmokingPackYears.ToString() : "";
            HealthRecord.Select(AlcoholInput, p.AlcoholUse);
            OccupationInput.Text = p.Occupation;
            HealthRecord.Select(ActivityInput, p.PhysicalActivity);
            HealthRecord.Select(DietInput, p.Diet);
        }

        private void BindFamily()
        {
            var family = PatientService.ListFamilyHistory(PatientId);
            FamilyRepeater.DataSource = family;
            FamilyRepeater.DataBind();
            NoFamily.Visible = family.Count == 0;
        }

        public void Reset()
        {
            EditingId.Value = "";
            RelationInput.SelectedIndex = 0;
            ConditionInput.Text = NotesInput.Text = "";
            FormTitle.Text = "Add family history";
            SaveButton.Text = "Add";
        }

        protected void ResetButton_Click(object sender, EventArgs e) { Reset(); }

        protected void FamilyRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var f = PatientService.GetFamilyHistory((string)e.CommandArgument);
            if (f == null || f.PatientId != PatientId) return;
            if (e.CommandName == "Delete")
            {
                PatientService.DeleteFamilyHistory(f.FamilyHistoryId);
                Audit("DELETE", "PatientFamilyHistory", f.FamilyHistoryId, "Removed " + f.RelationshipType + ": " + f.ConditionName);
                AppPage.ToastSuccess("Family history removed");
                if (EditingId.Value == f.FamilyHistoryId) Reset();
                BindFamily();
                return;
            }
            EditingId.Value = f.FamilyHistoryId;
            HealthRecord.Select(RelationInput, f.RelationshipType);
            ConditionInput.Text = f.ConditionName;
            NotesInput.Text = f.Notes;
            FormTitle.Text = "Edit family history";
            SaveButton.Text = "Update";
        }

        protected void SaveButton_Click(object sender, EventArgs e)
        {
            Page.Validate("Family");
            if (!Page.IsValid) return;
            var relation = RelationInput.SelectedValue;
            var condition = ConditionInput.Text.Trim();
            var notes = NotesInput.Text.Trim();
            if (EditingId.Value.Length > 0)
            {
                PatientService.UpdateFamilyHistory(EditingId.Value, relation, condition, notes);
                Audit("UPDATE", "PatientFamilyHistory", EditingId.Value, relation + ": " + condition);
                AppPage.ToastSuccess("Family history updated");
            }
            else
            {
                PatientService.AddFamilyHistory(PatientId, relation, condition, notes);
                Audit("CREATE", "PatientFamilyHistory", PatientId, relation + ": " + condition);
                AppPage.ToastSuccess("Family history added");
            }
            Reset();
            BindFamily();
        }

        protected void SaveSocialButton_Click(object sender, EventArgs e)
        {
            Page.Validate("Social");
            if (!Page.IsValid) return;
            int years;
            int? packYears = int.TryParse(PackYearsInput.Text.Trim(), out years) ? years : (int?)null;
            Func<DropDownList, string> val = d => d.SelectedValue.Length > 0 ? d.SelectedValue : null;
            var occupation = OccupationInput.Text.Trim();
            PatientService.UpdateSocialHistory(PatientId, val(SmokingInput), packYears, val(AlcoholInput),
                occupation.Length > 0 ? occupation : null, val(ActivityInput), val(DietInput));
            Audit("UPDATE", "Patient", PatientId, "Social history updated");
            AppPage.ToastSuccess("Social history saved");
        }

        private void Audit(string action, string entity, string id, string details)
        {
            var user = AppSession.CurrentUser;
            AuditService.Record(user.HospitalId, user.UserId, action, entity, id, details);
        }
    }
}
