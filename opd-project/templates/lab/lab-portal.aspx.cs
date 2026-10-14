using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.lab
{
    /// <summary>
    /// Lab partner portal: claim an order from the patient's requisition (Order ID + date of birth), mark received,
    /// upload the result with report files (moves the order to RESULTS_READY). A lab partner only ever sees its own
    /// claimed orders; clinic staff with lab_portal see every order and can upload a report on a lab's behalf.
    /// </summary>
    public partial class lab_portal : BasePage
    {
        protected override string RequiredPermission { get { return "lab_portal"; } }
        public override string PageTitle { get { return "Lab Partner Portal"; } }

        protected bool IsPartner { get { return Role == "LAB_PARTNER"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            ClaimPanel.Visible = IsPartner;
            ReportFiles.Attributes["accept"] = LabService.AcceptedExtensions;
            if (!IsPostBack) Bind();
        }

        private void Bind()
        {
            var orders = LabService.ListPortalOrders(CurrentUser);
            OrdersRepeater.DataSource = orders;
            OrdersRepeater.DataBind();
            NoOrders.Visible = orders.Count == 0;
        }

        protected string PatientLabel(string patientId)
        {
            var p = PatientService.Get(patientId);
            return p != null ? p.FullName + " · " + p.Mrn : "Unknown";
        }

        protected string DoctorName(string consultationId)
        {
            var c = ConsultationService.Get(consultationId);
            return c != null ? UserService.UserName(c.DoctorId) : "";
        }

        protected string FileCountLabel(string orderId)
        {
            int n = LabService.CountAttachmentsForOrder(orderId);
            return n == 0 ? "" : " · " + n + (n == 1 ? " file" : " files");
        }

        protected void RefreshButton_Click(object sender, EventArgs e) { Bind(); }

        protected void ClaimButton_Click(object sender, EventArgs e)
        {
            Validate("Claim");
            if (!IsValid || !IsPartner) return;
            if (LabService.IsClaimLocked(UserId))
            {
                ShowClaimError("Too many unsuccessful attempts. Please try again in 15 minutes.");
                return;
            }
            DateTime dob;
            var orderId = ClaimOrderId.Text.Trim();
            var order = DateTime.TryParseExact(ClaimDob.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dob)
                ? LabService.ClaimOrder(CurrentUser, orderId, dob) : null;
            if (order == null)
            {
                Audit("CLAIM_FAILED", "InvestigationOrder", orderId, "Lab requisition claim did not match");
                ShowClaimError("No open order matches that Order ID and date of birth. Check the requisition, or the order may already be with another lab.");
                return;
            }
            Audit("UPDATE", "InvestigationOrder", order.OrderId, "Received by lab " + CurrentUser.FullName + ": " + order.TestName);
            ToastSuccess("Order received", order.TestName + " — " + PatientLabel(order.PatientId));
            ReloadPage();
        }

        private void ShowClaimError(string message)
        {
            ClaimError.Text = Server.HtmlEncode(message);
            ClaimErrorPanel.Visible = true;
            Bind();
        }

        protected void OrdersRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var order = LabService.GetOrder((string)e.CommandArgument);
            if (!LabService.CanHandle(CurrentUser, order)) return;
            if (e.CommandName == "Receive")
            {
                LabService.MarkSent(order.OrderId);
                Audit("UPDATE", "InvestigationOrder", order.OrderId, "Received by lab: " + order.TestName);
                ToastSuccess("Order marked as received");
                Bind();
                return;
            }
            if (order.Status == "RESULTS_READY") return;
            UploadOrderId.Value = order.OrderId;
            UploadTitle.Text = Server.HtmlEncode(order.TestName);
            UploadFor.Text = Server.HtmlEncode(order.TestName + " (" + order.OrderId + ") — " + PatientLabel(order.PatientId));
            ResultSummary.Text = ResultValue.Text = ReferenceRange.Text = Technician.Text = ResultNotes.Text = "";
            Interpretation.SelectedIndex = 0;
            FileErrorPanel.Visible = false;
            UploadModal.Visible = true;
        }

        private List<System.Web.HttpPostedFile> PostedFiles
        {
            get { return ReportFiles.PostedFiles.Where(f => f != null && !string.IsNullOrEmpty(f.FileName)).ToList(); }
        }

        protected void SummaryOrFile_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = ResultSummary.Text.Trim().Length > 0 || PostedFiles.Count > 0;
        }

        protected void SubmitResultButton_Click(object sender, EventArgs e)
        {
            var order = LabService.GetOrder(UploadOrderId.Value);
            if (!LabService.CanHandle(CurrentUser, order)) { UploadModal.Visible = false; return; }

            Validate("Result");
            var files = PostedFiles;
            var errors = new List<string>();
            if (files.Count > LabService.MaxFilesPerUpload) errors.Add("Attach at most " + LabService.MaxFilesPerUpload + " files");
            else errors.AddRange(files.Select(f => LabService.CheckFile(f.FileName, f.ContentLength, f.InputStream)).Where(m => m != null));
            if (!IsValid || errors.Count > 0)
            {
                FileError.Text = Server.HtmlEncode(string.Join("; ", errors) + (files.Count > 0 ? (errors.Count > 0 ? ". " : "") + "Please select the files again." : ""));
                FileErrorPanel.Visible = FileError.Text.Length > 0;
                UploadModal.Visible = true;
                return;
            }

            var result = LabService.SubmitResult(new LabResult
            {
                OrderId = order.OrderId, PatientId = order.PatientId, ConsultationId = order.ConsultationId, HospitalId = HospitalId,
                TestName = order.TestName, ResultSummary = ResultSummary.Text.Trim(), ResultValue = ResultValue.Text.Trim(),
                ReferenceRange = ReferenceRange.Text.Trim(), Interpretation = Interpretation.SelectedValue, Technician = Technician.Text.Trim(),
                Notes = ResultNotes.Text.Trim(), UploadedByUserId = UserId,
            });
            if (result == null)
            {
                ToastWarning("A result has already been uploaded for " + order.TestName);
                UploadModal.Visible = false;
                Bind();
                return;
            }
            foreach (var f in files)
            {
                var att = LabService.SaveAttachment(result, f.FileName, f.ContentLength, f.InputStream, UserId);
                Audit("CREATE", "LabResultAttachment", att.AttachmentId, att.FileName + " (" + LabService.FormatSize(att.SizeBytes) + ") for " + order.TestName);
            }

            var patient = PatientService.Get(order.PatientId);
            Audit("CREATE", "LabResult", order.OrderId, "Result uploaded for " + order.TestName + " (" + (patient != null ? patient.FullName : "") + ")"
                + (files.Count > 0 ? " with " + files.Count + " file(s)" : ""));
            NotificationService.SendSingle("LAB_RESULTS_READY", HospitalId, patient, DoctorName(order.ConsultationId),
                new Dictionary<string, string> { { "TestName", order.TestName } });
            NotifyOrderingDoctor(order, result, files.Count);
            ToastSuccess("Result submitted for " + order.TestName);
            ReloadPage();
        }

        /// <summary>In-app notification for the doctor who ordered the test; a Critical result is flagged as urgent.</summary>
        private void NotifyOrderingDoctor(InvestigationOrder order, LabResult result, int fileCount)
        {
            var consultation = ConsultationService.Get(order.ConsultationId);
            if (consultation == null) return;
            bool critical = result.Interpretation == "CRITICAL";
            var from = LabService.LabPartnerName(result.LabPartnerId);
            var message = PatientLabel(order.PatientId)
                + (string.IsNullOrEmpty(from) ? "" : " · from " + from)
                + (fileCount > 0 ? " · " + fileCount + (fileCount == 1 ? " file" : " files") : "")
                + (string.IsNullOrEmpty(result.ResultValue) ? "" : " · " + result.ResultValue);
            UserNotificationService.Notify(HospitalId, consultation.DoctorId, "LAB_RESULT",
                (critical ? "CRITICAL result: " : "Lab result ready: ") + order.TestName, message,
                Urls.To("lab-results") + "#" + result.ResultId, critical, result.ResultId);
        }

        protected void CloseModal_Click(object sender, EventArgs e) { UploadModal.Visible = false; }
    }
}
