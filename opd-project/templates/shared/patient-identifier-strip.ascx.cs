using System;
using System.Web.UI;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.shared
{
    /// <summary>Patient identifier banner: name, gender, DOB/age, national health id and MRN (labels follow hospital settings).</summary>
    public partial class patient_identifier_strip : UserControl
    {
        public string PatientId { get; set; }

        /// <summary>Optional badge HTML (e.g. NEW / FOLLOW-UP) appended to the patient name.</summary>
        public string BadgeHtml { get; set; }

        protected Patient Patient { get; private set; }
        protected string DobDisplay { get; private set; }
        protected string HealthIdLabel { get; private set; }
        protected string PatientIdLabel { get; private set; }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            Patient = PatientService.Get(PatientId);
            StripHolder.Visible = Patient != null;
            if (Patient == null) return;
            var hospital = HospitalService.GetHospital(Patient.HospitalId);
            DobDisplay = HospitalService.FormatDate(Patient.HospitalId, Patient.Dob);
            HealthIdLabel = string.IsNullOrEmpty(hospital.NationalHealthIdLabel) ? "National ID" : hospital.NationalHealthIdLabel;
            PatientIdLabel = string.IsNullOrEmpty(hospital.PatientIdLabel) ? "MRN" : hospital.PatientIdLabel;
            BadgeText.Text = BadgeHtml;
        }
    }
}
