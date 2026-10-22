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
            ManagePayersLink.Visible = Can("admin_hospital");
            ManagePayersLink.NavigateUrl = Urls.To("settings") + "?tab=billing#insurers";
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
            BindConsent();

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

        private void BindConsent()
        {
            var docs = PatientService.ListConsentDocuments(_patient.PatientId);
            ConsentRepeater.DataSource = docs;
            ConsentRepeater.DataBind();
            ConsentRepeater.Visible = docs.Count > 0;
            ConsentBadge.Text = docs.Count > 0 ? "<span class=\"badge badge--success\">Signed copy on file</span>" : "<span class=\"badge badge--warning\">Not signed yet</span>";
            PrintConsentLink.NavigateUrl = Urls.PatientConsentForm(_patient.PatientId);
            NoDoctorHint.Visible = string.IsNullOrEmpty(_patient.PreferredDoctorId);
            SetDoctorLink.NavigateUrl = Urls.PatientEdit(_patient.PatientId);
        }

        protected void UploadConsentButton_Click(object sender, EventArgs e)
        {
            ConsentErrorPanel.Visible = false;
            var file = ConsentFile.PostedFile;
            var error = !ConsentFile.HasFile ? "Choose the scanned, signed consent form to upload."
                : LabService.CheckFile(file.FileName, file.ContentLength, file.InputStream, allowDicom: false);
            if (error != null)
            {
                ConsentError.Text = error;
                ConsentErrorPanel.Visible = true;
                Bind();
                return;
            }
            var doc = PatientService.SaveConsentDocument(_patient, file.FileName, file.ContentLength, file.InputStream, UserId);
            Audit("CREATE", "PatientConsentDocument", doc.ConsentDocumentId, "Signed consent form " + doc.FormVersion + " uploaded: " + doc.FileName + " (" + LabService.FormatSize(doc.SizeBytes) + ")");
            ToastSuccess("Signed consent uploaded", _patient.FullName + "'s signed consent form is on file.");
            Bind();
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
            var cov = PatientService.GetCoverage((string)e.CommandArgument, _patient.PatientId);
            if (cov == null || cov.PatientId != _patient.PatientId) return;
            PatientService.DeactivateCoverage(cov.CoverageId, _patient.PatientId);
            Audit("UPDATE", "PatientCoverage", cov.CoverageId, "Deactivated");
            ToastSuccess("Coverage deactivated");
            Bind();
        }
    }
}
