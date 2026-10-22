using System;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>Allergy list with Edit / Deactivate commands (soft delete — the record is kept as INACTIVE).</summary>
    public partial class patient_allergies_body : UserControl
    {
        public string PatientId { get; set; }

        /// <summary>Raised with the allergy id when the user clicks Edit.</summary>
        public event EventHandler<CommandEventArgs> EditRequested;

        public void Bind()
        {
            var items = PatientService.ListAllergies(PatientId, !ShowInactive.Checked);
            ItemsRepeater.DataSource = items;
            ItemsRepeater.DataBind();
            EmptyRow.Visible = items.Count == 0;
        }

        protected void ShowInactive_CheckedChanged(object sender, EventArgs e) { Bind(); }

        protected void ItemsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var id = (string)e.CommandArgument;
            if (e.CommandName == "EditAllergy")
            {
                if (EditRequested != null) EditRequested(this, new CommandEventArgs("Edit", id));
                return;
            }
            if (e.CommandName == "Deactivate")
            {
                var a = PatientService.GetAllergy(id, PatientId);
                PatientService.DeactivateAllergy(id, PatientId);
                var page = (BasePage)Page;
                AuditService.Record(AppSession.CurrentUser.HospitalId, AppSession.CurrentUser.UserId, "UPDATE", "PatientAllergy", id, "Deactivated " + (a != null ? a.AllergyName : ""));
                page.ToastSuccess("Allergy deactivated");
                Bind();
            }
        }
    }
}
