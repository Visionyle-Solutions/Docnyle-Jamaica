using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Hosting;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>
    /// External lab / imaging partner exchange: a lab claims an order from the patient's requisition,
    /// uploads the result (typed values and/or report files), and the doctor reviews it.
    /// A LAB_PARTNER user only ever sees and touches orders claimed by its own LabPartnerId;
    /// clinic staff with the lab_portal permission see every order of their hospital.
    /// </summary>
    public static partial class LabService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        public const long MaxFileBytes = 20L * 1024 * 1024;
        public const int MaxFilesPerUpload = 10;

        /// <summary>Accepted report formats. The content type is always ours, never the browser's.</summary>
        private static readonly Dictionary<string, string> FileTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".pdf", "application/pdf" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" }, { ".png", "image/png" }, { ".dcm", "application/dicom" },
        };

        public static string AcceptedExtensions { get { return string.Join(",", FileTypes.Keys); } }

        /// <summary>MIME type for an extension accepted by <see cref="CheckFile"/>.</summary>
        public static string ContentTypeFor(string ext) { return FileTypes[ext]; }

        /* ── Lab partners (the organisations; their staff are LAB_PARTNER users) ── */

        public static List<LabPartner> ListLabPartners(string hospitalId)
        {
            if (Api)
                return HospitalCache.Get("lab-partners", () => cnHelperObj.ListLabPartners(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().LabPartners ?? new List<LAB_LabPartnerInfo>())
                    .Where(p => p.IsActive).Select(ToModel).OrderBy(p => p.Name).ToList();
            lock (DataStore.Sync) return DataStore.LabPartners.Where(p => p.HospitalId == hospitalId && p.IsActive).OrderBy(p => p.Name).ToList();
        }

        /// <summary>API mode: GetLabPartner needs lab_portal or admin_users; for anyone else the name is not available (null).</summary>
        public static LabPartner GetLabPartner(string labPartnerId)
        {
            if (Api)
            {
                if (string.IsNullOrEmpty(labPartnerId)) return null;
                return RequestCache.Get("lab-partner:" + labPartnerId, () =>
                {
                    try { return ToModel(DocNyle.OrNull(() => cnHelperObj.GetLabPartner(AppSession.ApiHospitalId, AppSession.ApiUserId, labPartnerId, AppSession.ApiHashValue).EnsureSuccess())?.LabPartner); }
                    catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden) { return null; }
                });
            }
            lock (DataStore.Sync) return DataStore.LabPartners.FirstOrDefault(p => p.LabPartnerId == labPartnerId);
        }

        public static string LabPartnerName(string labPartnerId)
        {
            var p = string.IsNullOrEmpty(labPartnerId) ? null : GetLabPartner(labPartnerId);
            return p != null ? p.Name : "";
        }

        /// <summary>Returns the hospital's existing partner with this name (case-insensitive), or creates it.</summary>
        public static LabPartner SaveLabPartner(string hospitalId, string name)
        {
            name = (name ?? "").Trim();
            if (Api)
            {
                var saved = ToModel(cnHelperObj.SaveLabPartner(new LAB_SaveLabPartnerReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, Name = name }).EnsureSuccess().LabPartner);
                HospitalCache.Clear("lab-partners");
                return saved;
            }
            lock (DataStore.Sync)
            {
                var p = DataStore.LabPartners.FirstOrDefault(x => x.HospitalId == hospitalId && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p != null) { p.IsActive = true; return p; }
                p = new LabPartner { LabPartnerId = DataStore.NextId("LAB"), HospitalId = hospitalId, Name = name, IsActive = true };
                DataStore.LabPartners.Add(p);
                return p;
            }
        }

        /* ── Claim lockout: 5 mobile lookups that find nothing in 15 minutes lock the user out of looking up for 15 minutes ── */

        private const int MaxClaimFailures = 5;
        private static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(15);
        private static readonly Dictionary<string, List<DateTime>> ClaimFailures = new Dictionary<string, List<DateTime>>();

        private static bool IsPartner(AppUser user) { return user != null && user.HasRole("LAB_PARTNER"); }

        /// <summary>Whether the user may act on the order: same hospital, and for a lab partner, an order it claimed.</summary>
        public static bool CanHandle(AppUser user, InvestigationOrder order)
        {
            if (user == null || order == null || order.HospitalId != user.HospitalId) return false;
            if (!IsPartner(user)) return true;
            return !string.IsNullOrEmpty(user.LabPartnerId) && order.LabPartnerId == user.LabPartnerId;
        }

        /// <summary>Orders shown in the Lab Portal for this user (open orders plus ones awaiting the doctor's review).</summary>
        public static List<InvestigationOrder> ListPortalOrders(AppUser user)
        {
            if (Api) return ApiPortalOrders().Where(o => CanHandle(user, o)).ToList();
            lock (DataStore.Sync)
                return DataStore.Orders.Where(o => (o.Status == "ORDERED" || o.Status == "SENT" || o.Status == "RESULTS_READY") && CanHandle(user, o))
                    .OrderByDescending(o => o.OrderedAt).ToList();
        }

        /// <summary>API mode: an order this user was shown (portal list or mobile lookup), else from ListPendingLabOrders.</summary>
        public static InvestigationOrder GetOrder(string orderId)
        {
            if (Api) return SessionIndex.Get<InvestigationOrder>("laborder", orderId) ?? ApiPortalOrders().FirstOrDefault(o => o.OrderId == orderId);
            lock (DataStore.Sync) return DataStore.Orders.FirstOrDefault(o => o.OrderId == orderId);
        }

        /// <summary>API mode: the lockout lives in SearchLabOrdersByMobile (fault 50231), so this pre-check passes.</summary>
        public static bool IsClaimLocked(string userId)
        {
            if (Api) return false;
            lock (DataStore.Sync)
            {
                List<DateTime> list;
                if (!ClaimFailures.TryGetValue(userId, out list)) return false;
                list.RemoveAll(t => t < DateTime.UtcNow - ClaimWindow);
                return list.Count >= MaxClaimFailures;
            }
        }

        /// <summary>Digits only, compared on the last 10 so "+91-91111 11115" and "9111111115" match.</summary>
        private static string MobileKey(string phone)
        {
            var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
            return digits.Length > 10 ? digits.Substring(digits.Length - 10) : digits;
        }

        /// <summary>The number is long enough to look up (a full mobile number, not a fragment).</summary>
        public static bool IsValidMobile(string mobile) { return MobileKey(mobile).Length >= 10; }

        /// <summary>Open, unclaimed order in the lab's hospital for a patient registered with this mobile number.</summary>
        private static bool IsClaimableFor(AppUser user, InvestigationOrder o, string key)
        {
            if (o.HospitalId != user.HospitalId || !string.IsNullOrEmpty(o.LabPartnerId) || (o.Status != "ORDERED" && o.Status != "SENT")) return false;
            var p = DataStore.Patients.FirstOrDefault(x => x.PatientId == o.PatientId);
            return p != null && MobileKey(p.Phone) == key;
        }

        /// <summary>
        /// A lab partner looks up a patient's requisition by mobile number: every open order not yet taken by another
        /// lab, for every patient in the lab's hospital registered with that number. A search that finds nothing counts
        /// towards the claim lockout, so numbers cannot be probed at speed. Returns null when not allowed or locked out.
        /// </summary>
        public static List<InvestigationOrder> FindOrdersByMobile(AppUser user, string mobile)
        {
            if (!IsPartner(user) || string.IsNullOrEmpty(user.LabPartnerId) || !IsValidMobile(mobile)) return null;
            var key = MobileKey(mobile);
            if (Api) return ApiFindOrdersByMobile(mobile);
            lock (DataStore.Sync)
            {
                if (IsClaimLocked(user.UserId)) return null;
                var found = DataStore.Orders.Where(o => IsClaimableFor(user, o, key)).OrderByDescending(o => o.OrderedAt).ToList();
                if (found.Count == 0)
                {
                    List<DateTime> list;
                    if (!ClaimFailures.TryGetValue(user.UserId, out list)) ClaimFailures[user.UserId] = list = new List<DateTime>();
                    list.Add(DateTime.UtcNow);
                }
                else ClaimFailures.Remove(user.UserId);
                return found;
            }
        }

        /// <summary>
        /// The lab takes an order found by <see cref="FindOrdersByMobile"/>. The mobile number is checked again here so a
        /// posted order id cannot claim an order that the lookup did not return. Null if it no longer qualifies
        /// (e.g. another lab took it first).
        /// </summary>
        public static InvestigationOrder ClaimOrder(AppUser user, string orderId, string mobile)
        {
            if (!IsPartner(user) || string.IsNullOrEmpty(user.LabPartnerId) || !IsValidMobile(mobile)) return null;
            if (Api) return ApiClaimOrder(orderId, mobile);
            lock (DataStore.Sync)
            {
                var o = GetOrder(orderId);
                if (o == null || !IsClaimableFor(user, o, MobileKey(mobile))) return null;
                o.LabPartnerId = user.LabPartnerId;
                o.Status = "SENT";
                return o;
            }
        }

        /// <summary>Lab marks an order as received/sent for processing.</summary>
        public static void MarkSent(string orderId)
        {
            if (Api)
            {
                var o = GetOrder(orderId);
                if (o == null || o.Status != "ORDERED") return;
                cnHelperObj.UpdateOrderStatus(new LAB_UpdateOrderStatusReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, OrderId = orderId, Status = "SENT" }).EnsureSuccess();
                o.Status = "SENT";
                RequestCache.Remove("lab-portal-orders");
                return;
            }
            lock (DataStore.Sync)
            {
                var o = GetOrder(orderId);
                if (o != null && o.Status == "ORDERED") o.Status = "SENT";
            }
        }

        /// <summary>Records the result and moves the order to RESULTS_READY. Returns null if the order already has a result.</summary>
        public static LabResult SubmitResult(LabResult record)
        {
            if (Api) return ApiSubmitResult(record);
            lock (DataStore.Sync)
            {
                var o = GetOrder(record.OrderId);
                if (o == null || o.Status == "RESULTS_READY" || DataStore.LabResults.Any(r => r.OrderId == record.OrderId)) return null;
                record.ResultId = DataStore.NextId("RES");
                record.LabPartnerId = o.LabPartnerId;
                record.UploadedAt = DateTime.UtcNow;
                record.Status = "PENDING_REVIEW";
                DataStore.LabResults.Add(record);
                o.Status = "RESULTS_READY";
                return record;
            }
        }

        public static List<LabResult> ListPendingReview(string hospitalId)
        {
            if (Api)
                return (cnHelperObj.ListPendingReviewResults(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().LabResults ?? new List<LAB_LabResultInfo>()).Select(ToModel)
                    .Where(r => r.Status == "PENDING_REVIEW" || string.IsNullOrEmpty(r.Status)).OrderByDescending(r => r.UploadedAt).ToList();
            lock (DataStore.Sync) return DataStore.LabResults.Where(r => r.HospitalId == hospitalId && r.Status == "PENDING_REVIEW").OrderByDescending(r => r.UploadedAt).ToList();
        }

        public static List<LabResult> ListForPatient(string patientId)
        {
            if (Api)
                return RequestCache.Get("lab-results:" + patientId, () =>
                    (cnHelperObj.ListLabResultsForPatient(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().LabResults ?? new List<LAB_LabResultInfo>()).Select(ToModel).OrderByDescending(r => r.UploadedAt).ToList());
            lock (DataStore.Sync) return DataStore.LabResults.Where(r => r.PatientId == patientId).OrderByDescending(r => r.UploadedAt).ToList();
        }

        /// <summary>API mode: a result this user was shown (review inbox or patient list).</summary>
        public static LabResult GetResult(string resultId)
        {
            if (Api) return SessionIndex.Get<LabResult>("labresult", resultId);
            lock (DataStore.Sync) return DataStore.LabResults.FirstOrDefault(r => r.ResultId == resultId);
        }

        /// <summary>API mode: UpdateLabResultStatus also clears the doctor's notification for the result.</summary>
        public static void MarkReviewed(string resultId, string notes)
        {
            if (Api)
            {
                cnHelperObj.UpdateLabResultStatus(new LAB_UpdateLabResultStatusReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, LabResultId = resultId, ReviewNotes = notes ?? "" }).EnsureSuccess();
                var shown = SessionIndex.Get<LabResult>("labresult", resultId);
                if (shown != null) { shown.Status = "REVIEWED"; shown.ReviewedAt = DateTime.UtcNow; shown.ReviewNotes = notes ?? ""; }
                return;
            }
            lock (DataStore.Sync)
            {
                var r = GetResult(resultId);
                if (r == null) return;
                r.Status = "REVIEWED";
                r.ReviewedAt = DateTime.UtcNow;
                r.ReviewNotes = notes ?? "";
            }
        }

        /* ── Report files ── */

        private static string StorageRoot { get { return HostingEnvironment.MapPath("~/App_Data/lab-reports"); } }

        /// <summary>
        /// Checks one uploaded file before anything is saved: allowed extension, size, and that the content
        /// really is that format (file signature), so a renamed executable or HTML page is rejected.
        /// Returns an error message, or null when the file is acceptable. Leaves the stream at position 0.
        /// Scanned documents (e.g. a signed consent form) pass allowDicom = false.
        /// </summary>
        public static string CheckFile(string fileName, long length, Stream content, bool allowDicom = true)
        {
            var name = Path.GetFileName(fileName ?? "");
            var ext = Path.GetExtension(name);
            if (!allowDicom && FileTypes.ContainsKey(ext) && FileTypes[ext] == "application/dicom") return name + ": only PDF, JPG or PNG files are accepted";
            if (string.IsNullOrEmpty(name) || !FileTypes.ContainsKey(ext)) return name + (allowDicom ? ": only PDF, JPG, PNG or DICOM (.dcm) files are accepted" : ": only PDF, JPG or PNG files are accepted");
            if (length <= 0) return name + ": the file is empty";
            if (length > MaxFileBytes) return name + ": larger than " + (MaxFileBytes / (1024 * 1024)) + " MB";

            var head = new byte[132];
            content.Position = 0;
            int read = 0, n;
            while (read < head.Length && (n = content.Read(head, read, head.Length - read)) > 0) read += n;
            content.Position = 0;

            bool valid;
            switch (FileTypes[ext])
            {
                case "application/pdf": valid = read >= 5 && Encoding.ASCII.GetString(head, 0, 5) == "%PDF-"; break;
                case "image/png": valid = read >= 4 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47; break;
                case "image/jpeg": valid = read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF; break;
                default: valid = read >= 132 && Encoding.ASCII.GetString(head, 128, 4) == "DICM"; break;
            }
            return valid ? null : name + ": the file content does not match its type";
        }

        /// <summary>Stores a checked file under App_Data (never web-served) and records it against the result.</summary>
        public static LabResultAttachment SaveAttachment(LabResult result, string fileName, long length, Stream content, string userId)
        {
            if (Api) return ApiSaveAttachment(result, fileName, content);
            var name = Path.GetFileName(fileName);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var att = new LabResultAttachment
            {
                AttachmentId = DataStore.NextId("ATT"), ResultId = result.ResultId, OrderId = result.OrderId, HospitalId = result.HospitalId,
                LabPartnerId = result.LabPartnerId, FileName = name, ContentType = FileTypes[ext], SizeBytes = length,
                UploadedByUserId = userId, UploadedAt = DateTime.UtcNow,
            };
            att.StoredFileName = Guid.NewGuid().ToString("N") + ext;
            var dir = Path.Combine(StorageRoot, result.HospitalId);
            Directory.CreateDirectory(dir);
            content.Position = 0;
            using (var fs = new FileStream(Path.Combine(dir, att.StoredFileName), FileMode.CreateNew, FileAccess.Write))
                content.CopyTo(fs);
            lock (DataStore.Sync) DataStore.LabResultAttachments.Add(att);
            return att;
        }

        public static List<LabResultAttachment> ListAttachments(string resultId)
        {
            if (Api) return (cnHelperObj.ListLabResultAttachments(AppSession.ApiHospitalId, AppSession.ApiUserId, resultId, null, AppSession.ApiHashValue).EnsureSuccess().Attachments ?? new List<LAB_LabAttachmentInfo>()).Select(ToModel).OrderBy(a => a.UploadedAt).ToList();
            lock (DataStore.Sync) return DataStore.LabResultAttachments.Where(a => a.ResultId == resultId).OrderBy(a => a.UploadedAt).ToList();
        }

        public static int CountAttachmentsForOrder(string orderId)
        {
            if (Api) return (cnHelperObj.ListLabResultAttachments(AppSession.ApiHospitalId, AppSession.ApiUserId, null, orderId, AppSession.ApiHashValue).EnsureSuccess().Attachments ?? new List<LAB_LabAttachmentInfo>()).Count;
            lock (DataStore.Sync) return DataStore.LabResultAttachments.Count(a => a.OrderId == orderId);
        }

        public static LabResultAttachment GetAttachment(string attachmentId)
        {
            if (Api) return ToModel(DocNyle.OrNull(() => cnHelperObj.GetLabResultAttachment(AppSession.ApiHospitalId, AppSession.ApiUserId, attachmentId, AppSession.ApiHashValue).EnsureSuccess())?.Attachment);
            lock (DataStore.Sync) return DataStore.LabResultAttachments.FirstOrDefault(a => a.AttachmentId == attachmentId);
        }

        /// <summary>
        /// The report file's bytes, or null when missing: GetLabResultAttachmentContent in API mode (the service applies the
        /// same who-may-view rule), else the copy under App_Data.
        /// </summary>
        public static byte[] GetAttachmentContent(LabResultAttachment att)
        {
            if (Api)
            {
                FileContentResp file;
                try { file = DocNyle.OrNull(() => cnHelperObj.GetLabResultAttachmentContent(AppSession.ApiHospitalId, AppSession.ApiUserId, att.AttachmentId, AppSession.ApiHashValue).EnsureSuccess()); }
                catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden) { return null; }
                return file != null && file.File != null && !string.IsNullOrEmpty(file.File.ContentBase64) ? Convert.FromBase64String(file.File.ContentBase64) : null;
            }
            var path = Path.Combine(StorageRoot, att.HospitalId, att.StoredFileName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>
        /// Who may open a report file: a lab partner only its own lab's files; clinic staff of the same
        /// hospital who can see clinical data (consultation) or run the lab portal on the clinic's behalf.
        /// </summary>
        public static bool CanViewAttachment(AppUser user, LabResultAttachment att)
        {
            if (user == null || att == null || att.HospitalId != user.HospitalId) return false;
            if (IsPartner(user)) return !string.IsNullOrEmpty(user.LabPartnerId) && att.LabPartnerId == user.LabPartnerId;
            return UserService.HasPermission(user, "consultation") || UserService.HasPermission(user, "lab_portal");
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        private static bool Api { get { return DataSource.IsApi("Lab"); } }

        private static string CallerHospital { get { return AppSession.ApiUser != null ? AppSession.ApiUser.HospitalId : null; } }

        private static LabPartner ToModel(LAB_LabPartnerInfo p)
        {
            return p == null ? null : new LabPartner { LabPartnerId = p.LabPartnerId, HospitalId = CallerHospital, Name = p.Name, IsActive = p.IsActive };
        }

        /// <summary>An order row, remembered in the session index so a later postback (claim / mark sent / upload) can find it by id.</summary>
        private static InvestigationOrder ToModel(LAB_InvestigationOrderInfo o)
        {
            if (o == null) return null;
            if (!string.IsNullOrEmpty(o.PatientName)) SessionIndex.Put("labpatient", o.PatientId, o.PatientName + (string.IsNullOrEmpty(o.PatientMrn) ? "" : " · " + o.PatientMrn));
            return SessionIndex.Put("laborder", o.OrderId, new InvestigationOrder
            {
                OrderId = o.OrderId, HospitalId = CallerHospital, ConsultationId = o.ConsultationId, PatientId = o.PatientId, TestId = o.TestId,
                TestName = o.TestName, TestType = o.TestType, Status = o.Status, OrderedAt = DocNyle.InstantOrMin(o.OrderedAt), LabPartnerId = o.LabPartnerId,
            });
        }

        /// <summary>"Name · MRN" for a patient on an order this user was shown (lab partners cannot read patient records).</summary>
        public static string PatientLabel(string patientId)
        {
            return string.IsNullOrEmpty(patientId) ? null : SessionIndex.Get<string>("labpatient", patientId);
        }

        private static LabResult ToModel(LAB_LabResultInfo r)
        {
            if (r == null) return null;
            return SessionIndex.Put("labresult", r.ResultId, new LabResult
            {
                ResultId = r.ResultId, OrderId = r.OrderId, HospitalId = CallerHospital, PatientId = r.PatientId, ConsultationId = r.ConsultationId,
                TestName = r.TestName, ResultSummary = r.ResultSummary, ResultValue = r.ResultValue, ReferenceRange = r.ReferenceRange,
                Interpretation = r.Interpretation, Technician = r.Technician, Notes = r.Notes, UploadedAt = DocNyle.InstantOrMin(r.UploadedAt),
                Status = r.Status, ReviewedAt = DocNyle.ParseInstant(r.ReviewedAt), ReviewNotes = r.ReviewNotes,
            });
        }

        private static LabResultAttachment ToModel(LAB_LabAttachmentInfo a)
        {
            if (a == null) return null;
            return new LabResultAttachment
            {
                AttachmentId = a.AttachmentId, ResultId = a.ResultId, OrderId = a.OrderId, HospitalId = CallerHospital, LabPartnerId = a.LabPartnerId,
                FileName = a.FileName, ContentType = a.ContentType, SizeBytes = a.SizeBytes, UploadedByUserId = a.UploadedByUserId,
                UploadedAt = DocNyle.InstantOrMin(a.UploadedAt),
            };
        }

        private static List<InvestigationOrder> ApiPortalOrders()
        {
            return RequestCache.Get("lab-portal-orders", () =>
                (cnHelperObj.ListPendingLabOrders(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Orders ?? new List<LAB_InvestigationOrderInfo>()).Select(ToModel).OrderByDescending(o => o.OrderedAt).ToList());
        }

        /// <summary>
        /// SearchLabOrdersByMobile logs every lookup and enforces the claim lockout on the server (fault 50231, rethrown
        /// for the page to show "try again in 15 minutes").
        /// </summary>
        private static List<InvestigationOrder> ApiFindOrdersByMobile(string mobile)
        {
            return (cnHelperObj.SearchLabOrdersByMobile(new LAB_SearchLabOrdersByMobileReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, Mobile = mobile }).EnsureSuccess().Orders ?? new List<LAB_InvestigationOrderInfo>())
                .Select(ToModel).OrderByDescending(o => o.OrderedAt).ToList();
        }

        private static InvestigationOrder ApiClaimOrder(string orderId, string mobile)
        {
            try
            {
                return ToModel(cnHelperObj.ConfirmLabOrderClaim(new LAB_ConfirmLabOrderClaimReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, OrderId = orderId, Mobile = mobile }).EnsureSuccess().Order);
            }
            catch (ApiException ex) when (ex.Kind == ApiFaultKind.Conflict || ex.Kind == ApiFaultKind.NotFound || ex.Kind == ApiFaultKind.BusinessRule)
            {
                return null;
            }
        }

        /// <summary>SubmitLabResult moves the order to RESULTS_READY and notifies the ordering doctor itself. Null if the order already has a result.</summary>
        private static LabResult ApiSubmitResult(LabResult r)
        {
            try
            {
                var saved = cnHelperObj.SubmitLabResult(new LAB_SubmitLabResultReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    OrderId = r.OrderId, ResultSummary = r.ResultSummary, ResultValue = r.ResultValue, ReferenceRange = r.ReferenceRange,
                    Interpretation = string.IsNullOrEmpty(r.Interpretation) ? null : r.Interpretation, Technician = r.Technician, Notes = r.Notes,
                }).EnsureSuccess().LabResult;
                RequestCache.Remove("lab-portal-orders");
                var m = ToModel(saved) ?? r;
                if (string.IsNullOrEmpty(m.ResultId)) m.ResultId = r.ResultId;
                if (m.LabPartnerId == null) m.LabPartnerId = AppSession.ApiUser != null ? AppSession.ApiUser.LabPartnerId : null;
                return m;
            }
            catch (ApiException ex) when (ex.Kind == ApiFaultKind.Conflict)
            {
                return null;
            }
        }

        private static LabResultAttachment ApiSaveAttachment(LabResult result, string fileName, Stream content)
        {
            content.Position = 0;
            using (var ms = new MemoryStream())
            {
                content.CopyTo(ms);
                return ToModel(cnHelperObj.CreateLabResultAttachment(new LAB_CreateLabResultAttachmentReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    ResultId = result.ResultId, FileName = Path.GetFileName(fileName), ContentBase64 = Convert.ToBase64String(ms.ToArray()),
                }).EnsureSuccess().Attachment);
            }
        }
    }
}
