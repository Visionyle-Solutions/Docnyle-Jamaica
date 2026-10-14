using System;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.lab
{
    /// <summary>Doctor's inbox of lab results pending review; marking reviewed stores review notes.</summary>
    public partial class lab_results : BasePage
    {
        protected override string RequiredPermission { get { return "consultation"; } }
        public override string PageTitle { get { return "Lab Results Inbox"; } }

        protected bool CanConsult { get { return Role == "DOCTOR"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) Bind();
        }

        private void Bind()
        {
            var results = LabService.ListPendingReview(HospitalId);
            ResultsRepeater.DataSource = results;
            ResultsRepeater.DataBind();
            NoResults.Visible = results.Count == 0;
        }

        protected string PatientLabel(string patientId)
        {
            var p = PatientService.Get(patientId);
            return p != null ? p.FullName + " · " + p.Mrn : "Unknown";
        }

        /// <summary>"Lab name (person)" for a lab partner upload, or just the person for an upload by clinic staff.</summary>
        protected string UploadedBy(OpdProject.Models.LabResult r)
        {
            var person = UserService.UserName(r.UploadedByUserId);
            var lab = LabService.LabPartnerName(r.LabPartnerId);
            if (lab.Length == 0) return person;
            return string.Equals(lab, person, StringComparison.OrdinalIgnoreCase) ? lab : lab + " (" + person + ")";
        }

        protected void RefreshButton_Click(object sender, EventArgs e) { Bind(); }

        protected void ResultsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var r = LabService.GetResult((string)e.CommandArgument);
            if (r == null) return;
            ReviewResultId.Value = r.ResultId;
            ReviewTitle.Text = Server.HtmlEncode(r.TestName);
            ReviewNotes.Text = "";
            ReviewModal.Visible = true;
        }

        protected void ConfirmReviewButton_Click(object sender, EventArgs e)
        {
            var r = LabService.GetResult(ReviewResultId.Value);
            if (r == null || r.HospitalId != HospitalId) return;
            LabService.MarkReviewed(r.ResultId, ReviewNotes.Text.Trim());
            UserNotificationService.MarkReadForEntity(r.ResultId);
            Audit("UPDATE", "LabResult", r.ResultId, "Marked reviewed: " + r.TestName);
            ToastSuccess(r.TestName + " marked as reviewed");
            ReviewModal.Visible = false;
            Bind();
        }

        protected void CloseModal_Click(object sender, EventArgs e) { ReviewModal.Visible = false; }
    }
}
