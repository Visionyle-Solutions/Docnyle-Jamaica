using System;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>Add specialties and toggle them active/inactive (duplicate names are rejected).</summary>
    public partial class specialty_config : UserControl
    {
        private string HospitalId { get { return AppSession.CurrentUser.HospitalId; } }

        public void Bind()
        {
            SpecialtiesRepeater.DataSource = HospitalService.ListSpecialties(HospitalId, false);
            SpecialtiesRepeater.DataBind();
        }

        protected void AddSpecialtyButton_Click(object sender, EventArgs e)
        {
            Page.Validate("Specialty");
            if (!Page.IsValid) return;
            var name = NewSpecialtyName.Text.Trim();
            if (HospitalService.ListSpecialties(HospitalId, false).Any(s => string.Equals(s.SpecialtyName, name, StringComparison.OrdinalIgnoreCase)))
            {
                ((BasePage)Page).ToastWarning("That specialty already exists");
                return;
            }
            var s2 = HospitalService.CreateSpecialty(HospitalId, name);
            AuditService.Record(HospitalId, AppSession.CurrentUser.UserId, "CREATE", "Specialty", s2.SpecialtyId, name);
            ((BasePage)Page).ToastSuccess("Specialty added", name);
            NewSpecialtyName.Text = "";
            Bind();
        }

        protected void SpecialtiesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            HospitalService.ToggleSpecialty((string)e.CommandArgument);
            AuditService.Record(HospitalId, AppSession.CurrentUser.UserId, "UPDATE", "Specialty", (string)e.CommandArgument, "Toggled active");
            ((BasePage)Page).ToastSuccess("Specialty updated");
            Bind();
        }
    }
}
