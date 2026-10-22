using System;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.portal
{
    /// <summary>
    /// Public self-registration: the same patient-form fields as staff registration, validated on the server,
    /// issues an MRN, audited as a portal registration. Duplicates (same phone / TRN / health id) are blocked,
    /// but — this page being public — without revealing who the existing record belongs to.
    /// </summary>
    public partial class self_register : BasePage
    {
        protected override bool AllowAnonymous { get { return true; } }
        public override string PageTitle { get { return "Patient Registration"; } }

        /// <summary>Portal hospital: ?h=HOSP-xxx, else the signed-in user's, else the first demo hospital (API mode: the service's portal hospital).</summary>
        protected Hospital PortalHospital { get { return PortalService.GetHospital(Request.QueryString["h"], Hospital); } }

        protected string PatientIdLabelText { get { return string.IsNullOrEmpty(PortalHospital.PatientIdLabel) ? "MRN" : PortalHospital.PatientIdLabel; } }
        protected string BookUrl { get { return Urls.To("portal/book") + "?h=" + HttpUtility.UrlEncode(PortalHospital.HospitalId); } }

        protected void Page_Init(object sender, EventArgs e)
        {
            ((SiteMaster)Master).HideChrome = true;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            PatientFields.Hospital = PortalHospital;
            PatientFields.Setup();
            LogoHtml.Text = string.IsNullOrEmpty(PortalHospital.LogoBase64)
                ? "<img src=\"" + ResolveUrl("~/assets/Medcity.png") + "\" alt=\"Medcity Orthopaedic & Medical Centre\" style=\"max-height:70px;\" />"
                : "<img src=\"" + HttpUtility.HtmlAttributeEncode(PortalHospital.LogoBase64) + "\" alt=\"\" style=\"max-height:50px;\" />";
        }

        protected void Consent_ServerValidate(object source, ServerValidateEventArgs args) { args.IsValid = ConsentCheck.Checked; }

        protected void SubmitButton_Click(object sender, EventArgs e)
        {
            if (!IsValid) return;
            var record = new Patient { HospitalId = PortalHospital.HospitalId, RegisteredVia = "PATIENT_PORTAL" };
            PatientFields.ApplyTo(record);

            string duplicateField;
            var p = PortalService.Register(record, out duplicateField);
            if (p == null)
            {
                DuplicateText.Text = (string.IsNullOrEmpty(duplicateField) ? "A patient with these details" : "A patient with this " + Server.HtmlEncode(PatientFields.DuplicateFieldLabel(duplicateField)))
                    + " is already registered with the clinic. Use <a href=\"" + Server.HtmlEncode(BookUrl) + "\">Book an appointment</a> with your "
                    + Server.HtmlEncode(PatientIdLabelText) + " or phone number, or contact the clinic"
                    + (string.IsNullOrEmpty(PortalHospital.ContactNumber) ? "" : " on " + Server.HtmlEncode(PortalHospital.ContactNumber)) + " if your details have changed.";
                DuplicatePanel.Visible = true;
                if (!string.IsNullOrEmpty(duplicateField)) PatientFields.FocusDuplicate(duplicateField);
                return;
            }

            NewMrn.Text = HttpUtility.HtmlEncode(p.Mrn);
            FormPanel.Visible = false;
            SuccessPanel.Visible = true;
        }
    }
}
