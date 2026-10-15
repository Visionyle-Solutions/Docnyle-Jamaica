using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Hosting;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// External lab / imaging partner exchange: a lab claims an order from the patient's requisition,
    /// uploads the result (typed values and/or report files), and the doctor reviews it.
    /// A LAB_PARTNER user only ever sees and touches orders claimed by its own LabPartnerId;
    /// clinic staff with the lab_portal permission see every order of their hospital.
    /// </summary>
    public static class LabService
    {
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
            lock (DataStore.Sync) return DataStore.LabPartners.Where(p => p.HospitalId == hospitalId && p.IsActive).OrderBy(p => p.Name).ToList();
        }

        public static LabPartner GetLabPartner(string labPartnerId)
        {
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
            lock (DataStore.Sync)
                return DataStore.Orders.Where(o => (o.Status == "ORDERED" || o.Status == "SENT" || o.Status == "RESULTS_READY") && CanHandle(user, o))
                    .OrderByDescending(o => o.OrderedAt).ToList();
        }

        public static InvestigationOrder GetOrder(string orderId)
        {
            lock (DataStore.Sync) return DataStore.Orders.FirstOrDefault(o => o.OrderId == orderId);
        }

        public static bool IsClaimLocked(string userId)
        {
            lock (DataStore.Sync)
            {
                List<DateTime> list;
                if (!ClaimFailures.TryGetValue(userId, out list)) return false;
                list.RemoveAll(t => t < DateTime.Now - ClaimWindow);
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
            lock (DataStore.Sync)
            {
                if (IsClaimLocked(user.UserId)) return null;
                var found = DataStore.Orders.Where(o => IsClaimableFor(user, o, key)).OrderByDescending(o => o.OrderedAt).ToList();
                if (found.Count == 0)
                {
                    List<DateTime> list;
                    if (!ClaimFailures.TryGetValue(user.UserId, out list)) ClaimFailures[user.UserId] = list = new List<DateTime>();
                    list.Add(DateTime.Now);
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
            lock (DataStore.Sync)
            {
                var o = GetOrder(orderId);
                if (o != null && o.Status == "ORDERED") o.Status = "SENT";
            }
        }

        /// <summary>Records the result and moves the order to RESULTS_READY. Returns null if the order already has a result.</summary>
        public static LabResult SubmitResult(LabResult record)
        {
            lock (DataStore.Sync)
            {
                var o = GetOrder(record.OrderId);
                if (o == null || o.Status == "RESULTS_READY" || DataStore.LabResults.Any(r => r.OrderId == record.OrderId)) return null;
                record.ResultId = DataStore.NextId("RES");
                record.LabPartnerId = o.LabPartnerId;
                record.UploadedAt = DateTime.Now;
                record.Status = "PENDING_REVIEW";
                DataStore.LabResults.Add(record);
                o.Status = "RESULTS_READY";
                return record;
            }
        }

        public static List<LabResult> ListPendingReview(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.LabResults.Where(r => r.HospitalId == hospitalId && r.Status == "PENDING_REVIEW").OrderByDescending(r => r.UploadedAt).ToList();
        }

        public static List<LabResult> ListForPatient(string patientId)
        {
            lock (DataStore.Sync) return DataStore.LabResults.Where(r => r.PatientId == patientId).OrderByDescending(r => r.UploadedAt).ToList();
        }

        public static LabResult GetResult(string resultId)
        {
            lock (DataStore.Sync) return DataStore.LabResults.FirstOrDefault(r => r.ResultId == resultId);
        }

        public static void MarkReviewed(string resultId, string notes)
        {
            lock (DataStore.Sync)
            {
                var r = GetResult(resultId);
                if (r == null) return;
                r.Status = "REVIEWED";
                r.ReviewedAt = DateTime.Now;
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
            var name = Path.GetFileName(fileName);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var att = new LabResultAttachment
            {
                AttachmentId = DataStore.NextId("ATT"), ResultId = result.ResultId, OrderId = result.OrderId, HospitalId = result.HospitalId,
                LabPartnerId = result.LabPartnerId, FileName = name, ContentType = FileTypes[ext], SizeBytes = length,
                UploadedByUserId = userId, UploadedAt = DateTime.Now,
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
            lock (DataStore.Sync) return DataStore.LabResultAttachments.Where(a => a.ResultId == resultId).OrderBy(a => a.UploadedAt).ToList();
        }

        public static int CountAttachmentsForOrder(string orderId)
        {
            lock (DataStore.Sync) return DataStore.LabResultAttachments.Count(a => a.OrderId == orderId);
        }

        public static LabResultAttachment GetAttachment(string attachmentId)
        {
            lock (DataStore.Sync) return DataStore.LabResultAttachments.FirstOrDefault(a => a.AttachmentId == attachmentId);
        }

        public static string StoredPath(LabResultAttachment att)
        {
            return Path.Combine(StorageRoot, att.HospitalId, att.StoredFileName);
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
    }
}
