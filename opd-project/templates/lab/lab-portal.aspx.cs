using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.lab
{
    /// <summary>
    /// Lab partner portal: find a patient's open orders by mobile number and receive them, mark received,
    /// upload the result with report files (moves the order to RESULTS_READY). A lab partner only ever sees its own
    /// claimed orders; clinic staff with lab_portal see every order and can upload a report on a lab's behalf.
    /// </summary>
    public partial class lab_portal : BasePage
    {
        protected override string RequiredPermission { get { return "lab_portal"; } }
        public override string PageTitle { get { return "Lab Partner Portal"; } }

        protected bool IsPartner { get { return HasRole("LAB_PARTNER"); } }

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
            // API mode: a lab partner may not read patient records; the order rows carry the name and MRN.
            if (DataSource.IsApi("Lab")) return LabService.PatientLabel(patientId) ?? "Unknown";
            var p = PatientService.Get(patientId);
            return p != null ? p.FullName + " · " + p.Mrn : "Unknown";
        }

        protected string DoctorName(string consultationId)
        {
            if (DataSource.IsApi("Lab")) return "";
            var c = ConsultationService.Get(consultationId);
            return c != null ? UserService.UserName(c.DoctorId) : "";
        }

        protected string FileCountLabel(string orderId)
        {
            int n = LabService.CountAttachmentsForOrder(orderId);
            return n == 0 ? "" : " · " + n + (n == 1 ? " file" : " files");
        }

        protected void RefreshButton_Click(object sender, EventArgs e) { Bind(); }

        /* ── Receive a requisition by the patient's mobile number ── */

        /// <summary>The number the current results were found with; every claim re-checks against it.</summary>
        private string SearchedMobile
        {
            get { return ViewState["mobile"] as string; }
            set { ViewState["mobile"] = value; }
        }

        private string[] FoundIds
        {
            get { return ViewState["found"] as string[] ?? new string[0]; }
            set { ViewState["found"] = value; }
        }

        private static string MaskedMobile(string mobile)
        {
            var digits = new string((mobile ?? "").Where(char.IsDigit).ToArray());
            return digits.Length <= 4 ? digits : "…" + digits.Substring(digits.Length - 4);
        }

        protected void FindButton_Click(object sender, EventArgs e)
        {
            Validate("Claim");
            if (!IsValid || !IsPartner) return;
            var mobile = ClaimMobile.Text.Trim();
            SearchedMobile = null;
            FoundIds = new string[0];
            if (!LabService.IsValidMobile(mobile)) { ShowClaimError("Enter the patient's full 10-digit mobile number."); return; }
            if (LabService.IsClaimLocked(UserId)) { ShowClaimError("Too many searches with no matching orders. Please try again in 15 minutes."); return; }

            List<InvestigationOrder> found;
            try
            {
                found = LabService.FindOrdersByMobile(CurrentUser, mobile) ?? new List<InvestigationOrder>();
            }
            catch (ApiException ex) when (ex.ErrorNumber == 50231)
            {
                ShowClaimError("Too many searches with no matching orders. Please try again in 15 minutes.");
                return;
            }
            Audit(found.Count == 0 ? "CLAIM_FAILED" : "READ", "InvestigationOrder", "", "Lab requisition lookup by mobile " + MaskedMobile(mobile) + ": " + found.Count + " open order(s)");
            if (found.Count == 0)
            {
                ShowClaimError("No open orders for that mobile number. Check the number, or the orders may already be with another lab.");
                return;
            }
            SearchedMobile = mobile;
            FoundIds = found.Select(o => o.OrderId).ToArray();
            ClaimErrorPanel.Visible = false;
            BindFound();
        }

        /// <summary>Orders from the last lookup that no lab has taken yet.</summary>
        private List<InvestigationOrder> StillOpen()
        {
            return FoundIds.Select(LabService.GetOrder)
                .Where(o => o != null && string.IsNullOrEmpty(o.LabPartnerId) && (o.Status == "ORDERED" || o.Status == "SENT")).ToList();
        }

        private void BindFound()
        {
            var open = StillOpen();
            FoundPanel.Visible = open.Count > 0;
            FoundTitle.Text = Server.HtmlEncode(open.Count + (open.Count == 1 ? " Open Order" : " Open Orders") + " For Mobile " + MaskedMobile(SearchedMobile));
            ReceiveAllButton.Visible = open.Count > 1;
            FoundRepeater.DataSource = open;
            FoundRepeater.DataBind();
        }

        /// <summary>Takes one order for this lab; false if it no longer qualifies (e.g. another lab took it first).</summary>
        private bool Receive(string orderId)
        {
            if (!FoundIds.Contains(orderId)) return false;
            var order = LabService.ClaimOrder(CurrentUser, orderId, SearchedMobile);
            if (order == null) return false;
            Audit("UPDATE", "InvestigationOrder", order.OrderId, "Received by lab " + CurrentUser.FullName + ": " + order.TestName);
            return true;
        }

        protected void FoundRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "Claim" || !IsPartner || string.IsNullOrEmpty(SearchedMobile)) return;
            var order = LabService.GetOrder((string)e.CommandArgument);
            if (order != null && Receive(order.OrderId)) ToastSuccess("Order received", order.TestName + " — " + PatientLabel(order.PatientId));
            else ToastWarning("That order is no longer available — another lab may have received it");
            BindFound();
            Bind();
        }

        protected void ReceiveAllButton_Click(object sender, EventArgs e)
        {
            if (!IsPartner || string.IsNullOrEmpty(SearchedMobile)) return;
            int received = StillOpen().Count(o => Receive(o.OrderId));
            if (received > 0) ToastSuccess(received + (received == 1 ? " order received" : " orders received"));
            else ToastWarning("Those orders are no longer available — another lab may have received them");
            BindFound();
            Bind();
        }

        private void ShowClaimError(string message)
        {
            ClaimError.Text = Server.HtmlEncode(message);
            ClaimErrorPanel.Visible = true;
            FoundPanel.Visible = false;
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

            if (DataSource.IsApi("Lab"))
            {
                // SubmitLabResult records the result and notifies the ordering doctor on the service side.
                ToastSuccess("Result submitted for " + order.TestName);
                ReloadPage();
                return;
            }
            var patient = PatientService.Get(order.PatientId);
            Audit("CREATE", "LabResult", order.OrderId, "Result uploaded for " + order.TestName + " (" + (patient != null ? patient.FullName : "") + ")"
                + (files.Count > 0 ? " with " + files.Count + " file(s)" : ""));
            NotificationService.SendSingle("LAB_RESULTS_READY", HospitalId, patient, DoctorName(order.ConsultationId),
                new Dictionary<string, string> { { "TestName", order.TestName } });
            // API mode: SubmitLabResult notifies the ordering doctor itself.
            if (!DataSource.IsApi("UserNotifications")) NotifyOrderingDoctor(order, result, files.Count);
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
