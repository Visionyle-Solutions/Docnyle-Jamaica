using System;
using System.Linq;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Printable "Patient Consent for Sharing Health Information", pre-filled from the hospital, the patient's registration
    /// details and their preferred physician (blank lines are left for anything not on file, to be completed by hand).
    /// </summary>
    public partial class patient_consent_form : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Consent Form"; } }

        protected Patient P { get; private set; }
        protected string HospitalContact { get; private set; }
        protected string DoctorLine { get; private set; }
        protected string DoctorExample { get; private set; }
        protected string Dob { get; private set; }
        protected string Phone { get; private set; }
        protected string TodayText { get; private set; }
        protected string Printed { get; private set; }
        protected string FormVersion { get { return PatientService.ConsentFormVersion; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            P = PatientService.Get(RouteValue("patientId"));
            if (P == null || P.HospitalId != HospitalId)
            {
                ToastWarning("Patient not found");
                Go(Urls.PatientSearch);
                return;
            }

            HospitalContact = string.Join(" · ", new[] { Hospital.ContactNumber, Hospital.PrimaryEmail }.Where(x => !string.IsNullOrEmpty(x)));
            var doctor = string.IsNullOrEmpty(P.PreferredDoctorId) ? null : UserService.GetUser(P.PreferredDoctorId);
            if (doctor != null && doctor.HospitalId == HospitalId)
            {
                var specialty = HospitalService.SpecialtyName(doctor.SpecialtyId);
                DoctorLine = doctor.FullName
                    + (string.IsNullOrEmpty(doctor.Qualifications) ? "" : ", " + doctor.Qualifications)
                    + (string.IsNullOrEmpty(specialty) ? "" : " · " + specialty)
                    + (string.IsNullOrEmpty(doctor.LicenseNumber) ? "" : " · Reg. No: " + doctor.LicenseNumber);
                DoctorExample = " (" + doctor.FullName + ")";
            }
            else
            {
                DoctorLine = "";
                DoctorExample = "";
            }
            Dob = P.Dob.HasValue ? FmtDate(P.Dob) : "";
            Phone = !string.IsNullOrEmpty(P.Phone) ? P.Phone : P.Mobile;
            TodayText = FmtDate(Today);
            Printed = FmtDateTime(DateTime.UtcNow);
            Audit("PRINT", "PatientConsentForm", P.PatientId, "Consent form " + FormVersion + " printed");
        }
    }
}
