using System;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// A patient's Coverage &amp; Insurance: lists active coverage and adds private insurance or the country's govt
    /// scheme (NHF in Jamaica) with the same fields as billing checkout's Add Coverage. New registrations arrive
    /// with ?new=1 and continue to the Health Record walkthrough (or skip, staying Self-Pay).
    /// </summary>
    public partial class patient_coverage : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Coverage & Insurance"; } }

        private string PatientId { get { return RouteValue("patientId"); } }
        private bool IsNewRegistration { get { return Request.QueryString["new"] == "1"; } }
        private Patient _patient;

        protected string SchemeLabel { get { return PatientService.GetGovtSchemeProfile(Hospital.Country).Label; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            _patient = PatientService.Get(PatientId);
            if (_patient == null || _patient.HospitalId != HospitalId)
            {
                ToastWarning("Patient not found");
                Go(Urls.PatientSearch);
                return;
            }
            Strip.PatientId = _patient.PatientId;
            NewPatientBanner.Visible = IsNewRegistration;
            if (!IsPostBack)
            {
                CoverageFields.Reset(Hospital, _patient);
                Bind();
            }
        }

        private void Bind()
        {
            /* the same list billing offers: active coverage with a payer of the hospital's country */
            var coverages = PatientService.ListCoverages(_patient.PatientId, Hospital.Country);
            CoverageRepeater.DataSource = coverages;
            CoverageRepeater.DataBind();
            CoverageRepeater.Visible = coverages.Count > 0;
            NoCoverage.Visible = coverages.Count == 0;

            if (IsNewRegistration)
            {
                BackLink.Text = "Edit patient details";
                BackLink.NavigateUrl = Urls.PatientEdit(_patient.PatientId);
                ContinueLink.Text = coverages.Count == 0 ? "Skip — Self-Pay →" : "Continue to health record →";
                ContinueLink.NavigateUrl = Urls.Allergies(_patient.PatientId) + "?new=1";
            }
            else
            {
                BackLink.Text = "Back to patients";
                BackLink.NavigateUrl = Urls.PatientSearch;
                ContinueLink.Visible = false;
            }
        }

        protected string CoverageLabel(PatientCoverage c) { return PatientService.CoverageLabel(c, Hospital.Country); }

        protected void SaveCoverageButton_Click(object sender, EventArgs e)
        {
            Validate("Coverage");
            if (!IsValid) { Bind(); return; }
            var saved = PatientService.AddCoverage(CoverageFields.BuildRecord(HospitalId, _patient));
            Audit("CREATE", "PatientCoverage", saved.CoverageId, CoverageLabel(saved));
            ToastSuccess("Coverage added", CoverageLabel(saved) + " — billing will offer it for verification.");
            CoverageFields.Reset(Hospital, _patient);
            Bind();
        }

        protected void CoverageRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "Deactivate") return;
            var cov = PatientService.GetCoverage((string)e.CommandArgument);
            if (cov == null || cov.PatientId != _patient.PatientId) return;
            PatientService.DeactivateCoverage(cov.CoverageId);
            Audit("UPDATE", "PatientCoverage", cov.CoverageId, "Deactivated");
            ToastSuccess("Coverage deactivated");
            Bind();
        }
    }
}
