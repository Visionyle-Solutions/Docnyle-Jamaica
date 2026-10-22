using System;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>Add / edit allergy form, validated with validator controls (group "Allergy").</summary>
    public partial class patient_allergies_form : UserControl
    {
        public string PatientId { get; set; }

        /// <summary>Raised after an allergy is added or updated.</summary>
        public event EventHandler Saved;

        public void LoadAllergy(string allergyId)
        {
            var a = PatientService.GetAllergy(allergyId, PatientId);
            if (a == null) return;
            EditingId.Value = a.AllergyId;
            AllergyTypeInput.SelectedValue = a.AllergyType;
            AllergyNameInput.Text = a.AllergyName;
            ReactionTypeInput.SelectedValue = a.ReactionType;
            ReactionInput.Text = a.ReactionDescription;
            SeverityInput.SelectedValue = a.Severity;
            StatusInput.SelectedValue = a.Status;
            IsTreatedInput.SelectedValue = a.IsTreated ? "true" : "false";
            IsFamilyInput.SelectedValue = a.IsFamilyHistory ? "true" : "false";
            SinceWhenInput.Text = a.SinceWhen.HasValue ? a.SinceWhen.Value.ToString("yyyy-MM-dd") : "";
            NotesInput.Text = a.Notes;
            FormTitle.Text = "Edit Allergy";
            SaveButton.Text = "Update";
        }

        public void Reset()
        {
            EditingId.Value = "";
            AllergyTypeInput.SelectedIndex = 0;
            AllergyNameInput.Text = ReactionInput.Text = SinceWhenInput.Text = NotesInput.Text = "";
            ReactionTypeInput.SelectedIndex = SeverityInput.SelectedIndex = StatusInput.SelectedIndex = 0;
            IsTreatedInput.SelectedValue = IsFamilyInput.SelectedValue = "false";
            FormTitle.Text = "Add Allergy";
            SaveButton.Text = "Add";
        }

        protected void SinceWhen_ServerValidate(object source, ServerValidateEventArgs args)
        {
            DateTime d;
            args.IsValid = DateTime.TryParse(args.Value, out d) && d.Date <= ((OpdProject.Web.BasePage)Page).Today;
        }

        protected void ResetButton_Click(object sender, EventArgs e) { Reset(); }

        protected void SaveButton_Click(object sender, EventArgs e)
        {
            Page.Validate("Allergy");
            if (!Page.IsValid) return;
            var isEdit = !string.IsNullOrEmpty(EditingId.Value);
            var saved = PatientService.SaveAllergy(new PatientAllergy
            {
                AllergyId = isEdit ? EditingId.Value : null, PatientId = PatientId, AllergyType = AllergyTypeInput.SelectedValue,
                AllergyName = AllergyNameInput.Text.Trim(), ReactionType = ReactionTypeInput.SelectedValue, ReactionDescription = ReactionInput.Text.Trim(),
                Severity = SeverityInput.SelectedValue, Status = StatusInput.SelectedValue, IsTreated = IsTreatedInput.SelectedValue == "true",
                IsFamilyHistory = IsFamilyInput.SelectedValue == "true", SinceWhen = DateTime.Parse(SinceWhenInput.Text), Notes = NotesInput.Text.Trim(),
            });
            var user = AppSession.CurrentUser;
            AuditService.Record(user.HospitalId, user.UserId, isEdit ? "UPDATE" : "CREATE", "PatientAllergy", saved.AllergyId, saved.AllergyName + " (" + saved.Severity + ")");
            ((BasePage)Page).ToastSuccess(isEdit ? "Allergy updated" : "Allergy added");
            Reset();
            if (Saved != null) Saved(this, EventArgs.Empty);
        }
    }
}
