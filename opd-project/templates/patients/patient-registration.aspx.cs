using System;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Patient registration (/patients/register) and edit (/patients/{id}/edit). The fields live in the shared
    /// patient-form control (also used by the public self-registration portal). Validated server-side with
    /// validator controls; the national health id is mandatory when the hospital settings say so, and new
    /// registrations are blocked when the phone or an id is already on file. A new patient goes on to Coverage &amp; Insurance.
    /// </summary>
    public partial class patient_registration : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return IsEdit ? "Edit Patient" : "New Patient Registration"; } }

        private string EditingId { get { return RouteValue("patientId"); } }
        private bool IsEdit { get { return !string.IsNullOrEmpty(EditingId); } }

        protected void Page_Load(object sender, EventArgs e)
        {
            HeadingText.Text = PageTitle;
            SaveLabel.Text = IsEdit ? "Save changes" : "Register patient";
            PatientFields.Hospital = Hospital;
            PatientFields.Setup();
            if (IsPostBack) return;

            if (IsEdit)
            {
                var p = PatientService.Get(EditingId);
                if (p == null || p.HospitalId != HospitalId)
                {
                    ToastWarning("Patient not found");
                    Go(Urls.PatientSearch);
                    return;
                }
                MrnText.Text = "<span class=\"badge badge--info\">" + Server.HtmlEncode(p.Mrn) + "</span>";
                PatientFields.Fill(p);
            }
        }

        protected void SaveButton_Click(object sender, EventArgs e)
        {
            if (!IsValid) return;
            var record = new Patient { PatientId = IsEdit ? EditingId : null, HospitalId = HospitalId, RegisteredVia = "FRONT_DESK" };
            PatientFields.ApplyTo(record);

            if (IsEdit)
            {
                PatientService.Update(EditingId, record);
                Audit("UPDATE", "Patient", EditingId);
                ToastSuccess("Patient updated");
                Go(Urls.PatientSearch);
                return;
            }

            var dup = PatientService.FindDuplicate(record);
            if (dup != null)
            {
                DuplicateText.Text = "The " + Server.HtmlEncode(PatientFields.DuplicateFieldLabel(dup.Field)) + " " + Server.HtmlEncode(dup.Value) + " is already registered to "
                    + Server.HtmlEncode(dup.Patient.FullName) + " (" + Server.HtmlEncode(dup.Patient.Mrn) + "), so a new patient cannot be registered with it. "
                    + "<a href=\"" + Server.HtmlEncode(Urls.PatientEdit(dup.Patient.PatientId)) + "\">Open this patient</a>";
                DuplicatePanel.Visible = true;
                PatientFields.FocusDuplicate(dup.Field);
                return;
            }

            var created = PatientService.Create(record);
            Audit("CREATE", "Patient", created.PatientId);
            ToastSuccess("Patient registered — " + PatientIdLabel + ": " + created.Mrn);
            /* New patients record coverage (insurance / NHF) first, then continue to the Health Record walkthrough:
               allergies → history → medications → family & social. */
            Go(Urls.PatientCoverage(created.PatientId) + "?new=1");
        }
    }
}
