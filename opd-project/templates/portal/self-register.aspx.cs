using System;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.portal
{
    /// <summary>Public self-registration: validated on the server, issues an MRN, audited as a portal registration.</summary>
    public partial class self_register : BasePage
    {
        protected override bool AllowAnonymous { get { return true; } }
        public override string PageTitle { get { return "Patient Registration"; } }

        /// <summary>Portal hospital: ?h=HOSP-xxx, else the signed-in user's, else the first demo hospital.</summary>
        protected Hospital PortalHospital
        {
            get
            {
                var h = Request.QueryString["h"] != null ? HospitalService.GetHospital(Request.QueryString["h"]) : null;
                return h ?? Hospital;
            }
        }

        protected string HealthIdLabelText { get { return string.IsNullOrEmpty(PortalHospital.NationalHealthIdLabel) ? "National ID" : PortalHospital.NationalHealthIdLabel; } }
        protected string PatientIdLabelText { get { return string.IsNullOrEmpty(PortalHospital.PatientIdLabel) ? "MRN" : PortalHospital.PatientIdLabel; } }
        protected string BookUrl { get { return Urls.To("portal/book") + "?h=" + HttpUtility.UrlEncode(PortalHospital.HospitalId); } }

        protected void Page_Init(object sender, EventArgs e)
        {
            ((SiteMaster)Master).HideChrome = true;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            HealthIdRequired.Enabled = HealthIdMark.Visible = PortalHospital.NationalHealthIdMandatory;
            HealthIdRequired.ErrorMessage = HealthIdLabelText + " is required";
            LogoHtml.Text = string.IsNullOrEmpty(PortalHospital.LogoBase64)
                ? "<img src=\"" + ResolveUrl("~/assets/logo.png") + "\" alt=\"Consult360\" style=\"max-height:70px;\" />"
                : "<img src=\"" + HttpUtility.HtmlAttributeEncode(PortalHospital.LogoBase64) + "\" alt=\"\" style=\"max-height:50px;\" />";
        }

        protected void Dob_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var d = ParseDate(args.Value);
            args.IsValid = d.HasValue && d.Value <= DateTime.Today && d.Value >= DateTime.Today.AddYears(-130);
        }

        protected void Consent_ServerValidate(object source, ServerValidateEventArgs args) { args.IsValid = ConsentCheck.Checked; }

        private static string Clean(string s) { s = (s ?? "").Trim(); return s.Length == 0 ? null : s; }

        protected void SubmitButton_Click(object sender, EventArgs e)
        {
            if (!IsValid) return;
            var p = PatientService.Create(new Patient
            {
                HospitalId = PortalHospital.HospitalId, FullName = FullName.Text.Trim(), Phone = Phone.Text.Trim(), Gender = Gender.SelectedValue,
                Dob = ParseDate(Dob.Text), Email = Clean(Email.Text), Address = Clean(Address.Text), NationalHealthId = Clean(HealthId.Text),
                EmergencyContactName = Clean(EmergencyName.Text), EmergencyContactPhone = Clean(EmergencyPhone.Text),
                InsuranceProvider = Clean(Insurance.Text), InsurancePolicyNo = Clean(PolicyNo.Text), RegisteredVia = "PATIENT_PORTAL",
            });
            AuditService.Record(PortalHospital.HospitalId, null, "CREATE", "Patient", p.PatientId, "Self-registered via patient portal");
            NewMrn.Text = HttpUtility.HtmlEncode(p.Mrn);
            FormPanel.Visible = false;
            SuccessPanel.Visible = true;
        }
    }
}
