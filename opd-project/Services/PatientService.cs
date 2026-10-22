using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Hosting;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>Patient registration/search, allergies, medical/family history and insurance coverage.</summary>
    public static partial class PatientService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        public static Patient Get(string patientId)
        {
            if (Api) return ApiGet(patientId);
            lock (DataStore.Sync) return DataStore.Patients.FirstOrDefault(p => p.PatientId == patientId);
        }

        /// <summary>Search by name, MRN, phone or national health id. Fewer than 2 characters returns every patient.</summary>
        public static List<Patient> Search(string hospitalId, string term)
        {
            if (Api)
                return (cnHelperObj.SearchPatients(AppSession.ApiHospitalId, AppSession.ApiUserId, (term ?? "").Trim(), AppSession.ApiHashValue).EnsureSuccess().Patients ?? new List<PAT_PatientSummaryInfo>())
                    .Where(p => p.IsActive).Select(ToModel).OrderBy(p => p.FullName).ToList();
            lock (DataStore.Sync)
            {
                var all = DataStore.Patients.Where(p => p.HospitalId == hospitalId);
                var t = (term ?? "").Trim().ToLowerInvariant();
                if (t.Length < 2) return all.OrderBy(p => p.FullName).ToList();
                return all.Where(p =>
                        (p.FullName ?? "").ToLowerInvariant().Contains(t) ||
                        (p.Mrn ?? "").ToLowerInvariant().Contains(t) ||
                        (!string.IsNullOrEmpty(p.Phone) && p.Phone.Contains(t)) ||
                        (!string.IsNullOrEmpty(p.NationalHealthId) && p.NationalHealthId.ToLowerInvariant().Contains(t)))
                    .OrderBy(p => p.FullName).ToList();
            }
        }

        /// <summary>Registers a patient and issues the next MRN from the hospital's MRN format.</summary>
        public static Patient Create(Patient record)
        {
            if (Api) return ApiCreate(record);
            lock (DataStore.Sync)
            {
                record.PatientId = DataStore.NextId("PAT");
                record.Mrn = HospitalService.IssueMrnUnlocked(record.HospitalId);
                DataStore.Patients.Add(record);
                return record;
            }
        }

        /// <summary>Copies the editable demographic fields onto the stored patient.</summary>
        public static Patient Update(string patientId, Patient changes)
        {
            if (Api) return ApiUpdate(patientId, changes);
            lock (DataStore.Sync)
            {
                var p = DataStore.Patients.FirstOrDefault(x => x.PatientId == patientId);
                if (p == null) return null;
                p.FullName = changes.FullName; p.Gender = changes.Gender; p.Dob = changes.Dob; p.Phone = changes.Phone;
                p.Email = changes.Email; p.Address = changes.Address; p.NationalHealthId = changes.NationalHealthId;
                p.Occupation = changes.Occupation;
                p.BloodGroup = changes.BloodGroup; p.EmergencyContactName = changes.EmergencyContactName;
                p.EmergencyContactPhone = changes.EmergencyContactPhone;
                p.Prefix = changes.Prefix; p.FirstName = changes.FirstName; p.MiddleName = changes.MiddleName; p.LastName = changes.LastName;
                p.Trn = changes.Trn; p.CountryCode = changes.CountryCode; p.Mobile = changes.Mobile; p.PreferredLanguage = changes.PreferredLanguage;
                p.AddrLine1 = changes.AddrLine1; p.AddrLine2 = changes.AddrLine2; p.City = changes.City; p.Region = changes.Region; p.Country = changes.Country;
                p.MaritalStatus = changes.MaritalStatus; p.ReferralSource = changes.ReferralSource; p.PreferredDoctorId = changes.PreferredDoctorId;
                return p;
            }
        }

        /// <summary>
        /// Duplicate guard for new registrations: another patient in the hospital with the same
        /// national health id / TRN, or the same phone number (digits compared, trailing-match so
        /// "+1-876-555-0101" and "8765550101" collide). Returns the clash, or null.
        /// </summary>
        public static PatientDuplicate FindDuplicate(Patient fields)
        {
            Func<string, string> digits = v => new string((v ?? "").Where(char.IsDigit).ToArray());
            Func<string, string> norm = v => (v ?? "").Replace(" ", "").Replace("-", "").ToLowerInvariant();
            if (Api) return ApiFindDuplicate(fields);
            lock (DataStore.Sync)
            {
                var others = DataStore.Patients.Where(p => p.HospitalId == fields.HospitalId && p.PatientId != fields.PatientId).ToList();
                if (norm(fields.NationalHealthId).Length > 0)
                {
                    var hit = others.FirstOrDefault(p => norm(p.NationalHealthId) == norm(fields.NationalHealthId));
                    if (hit != null) return new PatientDuplicate { Field = "NationalHealthId", Value = fields.NationalHealthId, Patient = hit };
                }
                if (norm(fields.Trn).Length > 0)
                {
                    var hit = others.FirstOrDefault(p => norm(p.Trn) == norm(fields.Trn));
                    if (hit != null) return new PatientDuplicate { Field = "Trn", Value = fields.Trn, Patient = hit };
                }
                var mob = digits(fields.Mobile);
                if (mob.Length >= 7)
                {
                    var hit = others.FirstOrDefault(p =>
                    {
                        var d = digits(string.IsNullOrEmpty(p.Mobile) ? p.Phone : p.Mobile);
                        return d.Length >= 7 && (d == mob || d.EndsWith(mob) || mob.EndsWith(d));
                    });
                    if (hit != null) return new PatientDuplicate { Field = "Mobile", Value = fields.Mobile, Patient = hit };
                }
                return null;
            }
        }

        /* ── Allergies ── */

        public static List<PatientAllergy> ListAllergies(string patientId, bool activeOnly)
        {
            if (Api) return ApiAllergies(patientId).Where(a => !activeOnly || a.Status == "ACTIVE").ToList();
            lock (DataStore.Sync)
                return DataStore.Allergies.Where(a => a.PatientId == patientId && (!activeOnly || a.Status == "ACTIVE")).ToList();
        }

        /// <summary>API mode reads the allergy from the patient's list, so <paramref name="patientId"/> is required there.</summary>
        public static PatientAllergy GetAllergy(string allergyId, string patientId = null)
        {
            if (Api) return string.IsNullOrEmpty(patientId) ? null : ApiAllergies(patientId).FirstOrDefault(a => a.AllergyId == allergyId);
            lock (DataStore.Sync) return DataStore.Allergies.FirstOrDefault(a => a.AllergyId == allergyId);
        }

        public static PatientAllergy SaveAllergy(PatientAllergy record)
        {
            if (Api) return ApiSaveAllergy(record);
            lock (DataStore.Sync)
            {
                var existing = string.IsNullOrEmpty(record.AllergyId) ? null : DataStore.Allergies.FirstOrDefault(a => a.AllergyId == record.AllergyId);
                if (existing == null)
                {
                    record.AllergyId = DataStore.NextId("ALG");
                    if (string.IsNullOrEmpty(record.Status)) record.Status = "ACTIVE";
                    DataStore.Allergies.Add(record);
                    return record;
                }
                existing.AllergyType = record.AllergyType; existing.AllergyName = record.AllergyName; existing.ReactionType = record.ReactionType;
                existing.ReactionDescription = record.ReactionDescription; existing.Severity = record.Severity; existing.Status = record.Status;
                existing.IsTreated = record.IsTreated; existing.IsFamilyHistory = record.IsFamilyHistory; existing.SinceWhen = record.SinceWhen;
                existing.Notes = record.Notes;
                return existing;
            }
        }

        /// <summary>Soft delete — sets the allergy INACTIVE, never removes the record.</summary>
        public static void DeactivateAllergy(string allergyId, string patientId = null)
        {
            if (Api)
            {
                cnHelperObj.DeleteAllergy(new PAT_DeleteAllergyReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, AllergyId = allergyId, PatientId = patientId }).EnsureSuccess();
                if (patientId != null) RequestCache.Remove("allergies:" + patientId);
                return;
            }
            lock (DataStore.Sync)
            {
                var a = DataStore.Allergies.FirstOrDefault(x => x.AllergyId == allergyId);
                if (a != null) a.Status = "INACTIVE";
            }
        }

        /* ── Medical history / events / family ── */

        public static List<PatientMedicalHistory> ListMedicalHistory(string patientId)
        {
            if (Api) return ApiMedicalHistory(patientId);
            lock (DataStore.Sync) return DataStore.PatientMedicalHistory.Where(h => h.PatientId == patientId).ToList();
        }

        /// <summary>Checklist upsert by condition name (null month/year allowed).</summary>
        public static void SaveMedicalHistory(string patientId, string conditionName, int? month, int? year, string notes, string userId)
        {
            if (Api)
            {
                var current = ApiMedicalHistory(patientId).FirstOrDefault(x => x.ConditionName == conditionName);
                ApiSaveMedicalHistory(patientId, conditionName, month, year, current != null ? current.Status : "ACTIVE", notes ?? (current != null ? current.Notes : null));
                return;
            }
            lock (DataStore.Sync)
            {
                var h = DataStore.PatientMedicalHistory.FirstOrDefault(x => x.PatientId == patientId && x.ConditionName == conditionName);
                if (h == null)
                {
                    var item = DataStore.MedicalHistoryItems.FirstOrDefault(i => i.ItemName == conditionName);
                    h = new PatientMedicalHistory
                    {
                        PatientMedHistoryId = DataStore.NextId("PMH"), PatientId = patientId, ItemId = item != null ? item.ItemId : null,
                        ConditionName = conditionName, Status = "ACTIVE", RecordedByUserId = userId, RecordedAt = DateTime.UtcNow
                    };
                    DataStore.PatientMedicalHistory.Add(h);
                }
                h.OnsetMonth = month;
                h.OnsetYear = year;
                if (notes != null) h.Notes = notes;
            }
        }

        public static void DeleteMedicalHistory(string patientId, string conditionName)
        {
            if (Api)
            {
                if (ApiMedicalHistory(patientId).All(h => h.ConditionName != conditionName)) return;
                cnHelperObj.DeletePatientMedicalHistory(new PAT_DeletePatientMedicalHistoryReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PatientId = patientId, ConditionName = conditionName }).EnsureSuccess();
                RequestCache.Remove("medhist:" + patientId);
                return;
            }
            lock (DataStore.Sync) DataStore.PatientMedicalHistory.RemoveAll(h => h.PatientId == patientId && h.ConditionName == conditionName);
        }

        /// <summary>API mode reads the row from the patient's list, so <paramref name="patientId"/> is required there.</summary>
        public static PatientMedicalHistory GetMedicalHistory(string id, string patientId = null)
        {
            if (Api) return string.IsNullOrEmpty(patientId) ? null : ApiMedicalHistory(patientId).FirstOrDefault(h => h.PatientMedHistoryId == id);
            lock (DataStore.Sync) return DataStore.PatientMedicalHistory.FirstOrDefault(h => h.PatientMedHistoryId == id);
        }

        /// <summary>Add/edit from the patient record. Editing goes by id; adding a condition the patient already has updates that row.</summary>
        public static PatientMedicalHistory SaveMedicalHistoryRecord(PatientMedicalHistory record)
        {
            if (Api)
            {
                var list = ApiMedicalHistory(record.PatientId);
                var existing = !string.IsNullOrEmpty(record.PatientMedHistoryId)
                    ? list.FirstOrDefault(x => x.PatientMedHistoryId == record.PatientMedHistoryId)
                    : list.FirstOrDefault(x => string.Equals(x.ConditionName, record.ConditionName, StringComparison.OrdinalIgnoreCase));
                if (existing != null && !string.Equals(existing.ConditionName, record.ConditionName, StringComparison.OrdinalIgnoreCase))
                {
                    // Renamed: UpdatePatientMedicalHistory is keyed by the condition name, so the old row is replaced.
                    cnHelperObj.DeletePatientMedicalHistory(new PAT_DeletePatientMedicalHistoryReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PatientId = record.PatientId, ConditionName = existing.ConditionName }).EnsureSuccess();
                    existing = null;
                }
                if (existing != null)
                    cnHelperObj.UpdatePatientMedicalHistory(new PAT_UpdatePatientMedicalHistoryReq
                    {
                        HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                        PatientId = record.PatientId, ConditionName = existing.ConditionName, OnsetMonth = record.OnsetMonth, OnsetYear = record.OnsetYear,
                        Status = record.Status, Notes = record.Notes ?? "",
                    }).EnsureSuccess();
                else
                    cnHelperObj.CreatePatientMedicalHistory(new PAT_CreatePatientMedicalHistoryReq
                    {
                        HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                        PatientId = record.PatientId, ItemId = HistoryItemId(record.ConditionName), ConditionName = record.ConditionName,
                        OnsetMonth = record.OnsetMonth, OnsetYear = record.OnsetYear, Status = record.Status, Notes = record.Notes,
                    }).EnsureSuccess();
                RequestCache.Remove("medhist:" + record.PatientId);
                return ApiMedicalHistory(record.PatientId).FirstOrDefault(x => string.Equals(x.ConditionName, record.ConditionName, StringComparison.OrdinalIgnoreCase)) ?? record;
            }
            lock (DataStore.Sync)
            {
                var h = !string.IsNullOrEmpty(record.PatientMedHistoryId)
                    ? DataStore.PatientMedicalHistory.FirstOrDefault(x => x.PatientMedHistoryId == record.PatientMedHistoryId)
                    : DataStore.PatientMedicalHistory.FirstOrDefault(x => x.PatientId == record.PatientId && string.Equals(x.ConditionName, record.ConditionName, StringComparison.OrdinalIgnoreCase));
                if (h == null)
                {
                    var item = DataStore.MedicalHistoryItems.FirstOrDefault(i => i.ItemName == record.ConditionName);
                    h = new PatientMedicalHistory
                    {
                        PatientMedHistoryId = DataStore.NextId("PMH"), PatientId = record.PatientId, ItemId = item != null ? item.ItemId : null,
                        RecordedByUserId = record.RecordedByUserId, RecordedAt = DateTime.UtcNow
                    };
                    DataStore.PatientMedicalHistory.Add(h);
                }
                h.ConditionName = record.ConditionName;
                h.OnsetMonth = record.OnsetMonth;
                h.OnsetYear = record.OnsetYear;
                h.Status = record.Status;
                h.Notes = record.Notes;
                return h;
            }
        }

        /// <summary>DeletePatientMedicalHistory is keyed by patient + condition name.</summary>
        public static void DeleteMedicalHistoryRecord(PatientMedicalHistory record)
        {
            if (Api)
            {
                cnHelperObj.DeletePatientMedicalHistory(new PAT_DeletePatientMedicalHistoryReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PatientId = record.PatientId, ConditionName = record.ConditionName }).EnsureSuccess();
                RequestCache.Remove("medhist:" + record.PatientId);
                return;
            }
            lock (DataStore.Sync) DataStore.PatientMedicalHistory.RemoveAll(h => h.PatientMedHistoryId == record.PatientMedHistoryId);
        }

        public static PatientMedicalEvent GetMedicalEvent(string id)
        {
            if (Api) return ToModel(DocNyle.OrNull(() => cnHelperObj.GetPatientMedicalEvent(AppSession.ApiHospitalId, AppSession.ApiUserId, id, AppSession.ApiHashValue).EnsureSuccess())?.MedicalEvent, null);
            lock (DataStore.Sync) return DataStore.PatientMedicalEvents.FirstOrDefault(e => e.PatientMedEventId == id);
        }

        /// <summary>Add/edit a surgery or medical event; the name may come from the configured list or be typed in.</summary>
        public static PatientMedicalEvent SaveMedicalEventRecord(PatientMedicalEvent record)
        {
            if (Api)
            {
                PAT_MedicalEventInfo saved;
                if (string.IsNullOrEmpty(record.PatientMedEventId))
                {
                    var item = HospitalService.ListMedicalEventItems(null).FirstOrDefault(i => string.Equals(i.EventName, record.EventName, StringComparison.OrdinalIgnoreCase));
                    saved = cnHelperObj.CreatePatientMedicalEvent(new PAT_CreatePatientMedicalEventReq
                    {
                        HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                        PatientId = record.PatientId, EventId = item != null ? item.EventId : null, EventName = record.EventName,
                        EventMonth = record.EventMonth, EventYear = record.EventYear, Notes = record.Notes,
                    }).EnsureSuccess().MedicalEvent;
                }
                else
                    saved = cnHelperObj.UpdatePatientMedicalEvent(new PAT_UpdatePatientMedicalEventReq
                    {
                        HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                        PatientMedEventId = record.PatientMedEventId, EventName = record.EventName, EventMonth = record.EventMonth,
                        EventYear = record.EventYear, Notes = record.Notes ?? "",
                    }).EnsureSuccess().MedicalEvent;
                RequestCache.Remove("medevents:" + record.PatientId);
                return ToModel(saved, record.PatientId) ?? record;
            }
            lock (DataStore.Sync)
            {
                var ev = string.IsNullOrEmpty(record.PatientMedEventId) ? null : DataStore.PatientMedicalEvents.FirstOrDefault(e => e.PatientMedEventId == record.PatientMedEventId);
                if (ev == null)
                {
                    ev = new PatientMedicalEvent { PatientMedEventId = DataStore.NextId("PME"), PatientId = record.PatientId, RecordedByUserId = record.RecordedByUserId, RecordedAt = DateTime.UtcNow };
                    DataStore.PatientMedicalEvents.Add(ev);
                }
                var item = DataStore.MedicalEventItems.FirstOrDefault(i => string.Equals(i.EventName, record.EventName, StringComparison.OrdinalIgnoreCase));
                ev.EventId = item != null ? item.EventId : null;
                ev.EventName = record.EventName;
                ev.EventMonth = record.EventMonth;
                ev.EventYear = record.EventYear;
                ev.Notes = record.Notes;
                return ev;
            }
        }

        public static void DeleteMedicalEvent(string id)
        {
            if (Api) { cnHelperObj.DeletePatientMedicalEvent(new PAT_DeletePatientMedicalEventReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PatientMedEventId = id }).EnsureSuccess(); RequestCache.RemovePrefix("medevents:"); return; }
            lock (DataStore.Sync) DataStore.PatientMedicalEvents.RemoveAll(e => e.PatientMedEventId == id);
        }

        public static List<PatientMedicalEvent> ListMedicalEvents(string patientId)
        {
            if (Api) return ApiMedicalEvents(patientId);
            lock (DataStore.Sync) return DataStore.PatientMedicalEvents.Where(e => e.PatientId == patientId).ToList();
        }

        public static void AddMedicalEvent(string patientId, string eventId, int? year, string notes, string userId)
        {
            if (Api)
            {
                var item = HospitalService.ListMedicalEventItems(null).FirstOrDefault(i => i.EventId == eventId);
                if (item == null || ApiMedicalEvents(patientId).Any(e => e.EventId == eventId)) return;
                cnHelperObj.CreatePatientMedicalEvent(new PAT_CreatePatientMedicalEventReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PatientId = patientId, EventId = eventId, EventName = item.EventName, EventYear = year, Notes = notes }).EnsureSuccess();
                RequestCache.Remove("medevents:" + patientId);
                return;
            }
            lock (DataStore.Sync)
            {
                var item = DataStore.MedicalEventItems.FirstOrDefault(i => i.EventId == eventId);
                if (item == null || DataStore.PatientMedicalEvents.Any(e => e.PatientId == patientId && e.EventId == eventId)) return;
                DataStore.PatientMedicalEvents.Add(new PatientMedicalEvent
                {
                    PatientMedEventId = DataStore.NextId("PME"), PatientId = patientId, EventId = eventId, EventName = item.EventName,
                    EventYear = year, Notes = notes, RecordedByUserId = userId, RecordedAt = DateTime.UtcNow
                });
            }
        }

        public static List<PatientFamilyHistory> ListFamilyHistory(string patientId)
        {
            if (Api) return (cnHelperObj.ListPatientFamilyHistory(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().FamilyHistory ?? new List<PAT_FamilyHistoryInfo>()).Select(ToModel).ToList();
            lock (DataStore.Sync) return DataStore.FamilyHistory.Where(f => f.PatientId == patientId).ToList();
        }

        public static void AddFamilyHistory(string patientId, string relationship, string condition, string notes)
        {
            if (Api)
            {
                cnHelperObj.CreatePatientFamilyHistory(new PAT_CreatePatientFamilyHistoryReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PatientId = patientId, RelationshipType = relationship, ConditionName = condition, Notes = notes }).EnsureSuccess();
                return;
            }
            lock (DataStore.Sync)
                DataStore.FamilyHistory.Add(new PatientFamilyHistory
                {
                    FamilyHistoryId = DataStore.NextId("PFH"), PatientId = patientId, RelationshipType = relationship,
                    ConditionName = condition, Notes = notes, RecordedAt = DateTime.UtcNow
                });
        }

        public static PatientFamilyHistory GetFamilyHistory(string id)
        {
            if (Api) return ToModel(DocNyle.OrNull(() => cnHelperObj.GetPatientFamilyHistory(AppSession.ApiHospitalId, AppSession.ApiUserId, id, AppSession.ApiHashValue).EnsureSuccess())?.FamilyHistory);
            lock (DataStore.Sync) return DataStore.FamilyHistory.FirstOrDefault(f => f.FamilyHistoryId == id);
        }

        public static void UpdateFamilyHistory(string id, string relationship, string condition, string notes)
        {
            if (Api)
            {
                cnHelperObj.UpdatePatientFamilyHistory(new PAT_UpdatePatientFamilyHistoryReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, FamilyHistoryId = id, RelationshipType = relationship, ConditionName = condition, Notes = notes ?? "" }).EnsureSuccess();
                return;
            }
            lock (DataStore.Sync)
            {
                var f = DataStore.FamilyHistory.FirstOrDefault(x => x.FamilyHistoryId == id);
                if (f == null) return;
                f.RelationshipType = relationship; f.ConditionName = condition; f.Notes = notes;
            }
        }

        public static void DeleteFamilyHistory(string id)
        {
            if (Api) { cnHelperObj.DeletePatientFamilyHistory(new PAT_DeletePatientFamilyHistoryReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, FamilyHistoryId = id }).EnsureSuccess(); return; }
            lock (DataStore.Sync) DataStore.FamilyHistory.RemoveAll(f => f.FamilyHistoryId == id);
        }

        /* ── Signed consent form (Consent for Sharing Health Information) ── */

        /// <summary>Printed on the form and stored with each signed copy, so a later wording change is traceable.</summary>
        public const string ConsentFormVersion = "JM-SHARE-1.0";

        private static string ConsentStorageRoot { get { return HostingEnvironment.MapPath("~/App_Data/consent-forms"); } }

        /// <summary>Signed copies for the patient, newest first (the first one is the current consent).</summary>
        public static List<PatientConsentDocument> ListConsentDocuments(string patientId)
        {
            if (Api)
                return (cnHelperObj.ListPatientConsentDocuments(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().ConsentDocuments ?? new List<PAT_ConsentDocumentInfo>())
                    .Select(ToModel).OrderByDescending(d => d.UploadedAt).ToList();
            lock (DataStore.Sync) return DataStore.ConsentDocuments.Where(d => d.PatientId == patientId).OrderByDescending(d => d.UploadedAt).ToList();
        }

        public static PatientConsentDocument GetConsentDocument(string documentId)
        {
            if (Api) return ToModel(DocNyle.OrNull(() => cnHelperObj.GetPatientConsentDocument(AppSession.ApiHospitalId, AppSession.ApiUserId, documentId, AppSession.ApiHashValue).EnsureSuccess())?.ConsentDocument);
            lock (DataStore.Sync) return DataStore.ConsentDocuments.FirstOrDefault(d => d.ConsentDocumentId == documentId);
        }

        /// <summary>Stores a file already checked with LabService.CheckFile(…, allowDicom: false) under App_Data (never web-served).</summary>
        public static PatientConsentDocument SaveConsentDocument(Patient patient, string fileName, long length, Stream content, string userId)
        {
            var name = Path.GetFileName(fileName);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (Api)
            {
                // The service stores the file (CreatePatientConsentDocument, base64 content).
                content.Position = 0;
                using (var ms = new MemoryStream())
                {
                    content.CopyTo(ms);
                    return ToModel(cnHelperObj.CreatePatientConsentDocument(new PAT_CreatePatientConsentDocumentReq
                    {
                        HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                        PatientId = patient.PatientId, FileName = name, ContentBase64 = Convert.ToBase64String(ms.ToArray()), FormVersion = ConsentFormVersion,
                    }).EnsureSuccess().ConsentDocument);
                }
            }
            var doc = new PatientConsentDocument
            {
                ConsentDocumentId = DataStore.NextId("PCD"), PatientId = patient.PatientId, HospitalId = patient.HospitalId,
                FormVersion = ConsentFormVersion, FileName = name, ContentType = LabService.ContentTypeFor(ext), SizeBytes = length,
                StoredFileName = Guid.NewGuid().ToString("N") + ext, UploadedByUserId = userId, UploadedAt = DateTime.UtcNow,
            };
            var dir = Path.Combine(ConsentStorageRoot, patient.HospitalId);
            Directory.CreateDirectory(dir);
            content.Position = 0;
            using (var fs = new FileStream(Path.Combine(dir, doc.StoredFileName), FileMode.CreateNew, FileAccess.Write))
                content.CopyTo(fs);
            lock (DataStore.Sync) DataStore.ConsentDocuments.Add(doc);
            return doc;
        }

        /// <summary>The signed file's bytes: GetPatientConsentDocumentContent in API mode, else the copy under App_Data. Null when missing.</summary>
        public static byte[] GetConsentDocumentContent(PatientConsentDocument doc)
        {
            if (Api)
            {
                var file = DocNyle.OrNull(() => cnHelperObj.GetPatientConsentDocumentContent(AppSession.ApiHospitalId, AppSession.ApiUserId, doc.ConsentDocumentId, AppSession.ApiHashValue).EnsureSuccess());
                return file != null && file.File != null && !string.IsNullOrEmpty(file.File.ContentBase64) ? Convert.FromBase64String(file.File.ContentBase64) : null;
            }
            var path = Path.Combine(ConsentStorageRoot, doc.HospitalId, doc.StoredFileName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>Social history lives on the patient row (also editable at registration, except pack-years/activity/diet).</summary>
        public static void UpdateSocialHistory(string patientId, string smoking, int? packYears, string alcohol, string occupation, string activity, string diet)
        {
            if (Api) { ApiUpdateSocial(patientId, smoking, packYears, alcohol, occupation, activity, diet); return; }
            lock (DataStore.Sync)
            {
                var p = DataStore.Patients.FirstOrDefault(x => x.PatientId == patientId);
                if (p == null) return;
                p.SmokingStatus = smoking; p.SmokingPackYears = packYears; p.AlcoholUse = alcohol;
                p.Occupation = occupation; p.PhysicalActivity = activity; p.Diet = diet;
            }
        }

        /* ── Coverage (insurance / govt scheme) ── */

        public static GovtSchemeProfile GetGovtSchemeProfile(string country)
        {
            switch (country)
            {
                case "IN": return new GovtSchemeProfile { Code = "GOVT", Label = "Govt Scheme (CGHS/Aarogyasri)", CardLabel = "Scheme Card Number" };
                case "JM": return new GovtSchemeProfile { Code = "NHF", Label = "NHF", CardLabel = "NHF Card Number" };
                case "US": return new GovtSchemeProfile { Code = "MCR", Label = "Medicare / Medicaid", CardLabel = "Program ID" };
                default: return new GovtSchemeProfile { Code = "GOVT", Label = "Government Scheme", CardLabel = "Scheme Card Number" };
            }
        }

        public static List<Payer> ListPayers(string country, string category)
        {
            if (Api)
                return RequestCache.Get("payers:" + country + ":" + category, () =>
                    (cnHelperObj.ListPayers(AppSession.ApiHospitalId, AppSession.ApiUserId, country, category, AppSession.ApiHashValue).EnsureSuccess().Payers ?? new List<PAT_PayerInfo>()).Where(p => p.IsActive).Select(ToModel).ToList());
            lock (DataStore.Sync)
                return DataStore.Payers.Where(p => p.IsActive && p.Country == country && (category == null || p.Category == category)).ToList();
        }

        /// <summary>API mode: GetPayer needs admin_hospital, so other users get the payer from the active payer list (or the name a coverage row carried).</summary>
        public static Payer GetPayer(string payerId)
        {
            if (Api)
            {
                if (string.IsNullOrEmpty(payerId)) return null;
                var hit = ListPayers(null, null).FirstOrDefault(p => p.PayerId == payerId);
                if (hit != null) return hit;
                if (AppSession.CurrentUser != null && UserService.HasPermission(AppSession.CurrentUser, "admin_hospital"))
                    return ToModel(DocNyle.OrNull(() => cnHelperObj.GetPayer(AppSession.ApiHospitalId, AppSession.ApiUserId, payerId, AppSession.ApiHashValue).EnsureSuccess())?.Payer);
                var name = RequestCache.Get<string>("payer-name:" + payerId, () => null);
                return name != null ? new Payer { PayerId = payerId, Name = name, IsActive = false } : null;
            }
            lock (DataStore.Sync) return DataStore.Payers.FirstOrDefault(p => p.PayerId == payerId);
        }

        /// <summary>Every payer of the category for the country, inactive ones included (Settings → Billing &amp; Numbers).</summary>
        /// <summary>API mode: ListAllPayers lists the caller hospital's country.</summary>
        public static List<Payer> ListAllPayers(string country, string category)
        {
            if (Api) return (cnHelperObj.ListAllPayers(AppSession.ApiHospitalId, AppSession.ApiUserId, category, AppSession.ApiHashValue).EnsureSuccess().Payers ?? new List<PAT_PayerInfo>()).Select(ToModel).OrderBy(p => p.Name).ToList();
            lock (DataStore.Sync)
                return DataStore.Payers.Where(p => p.Country == country && p.Category == category).OrderBy(p => p.Name).ToList();
        }

        /// <summary>
        /// Adds a payer for the country (admin settings). Names are unique per country and category, ignoring case and
        /// extra spaces; returns null for an active duplicate and reactivates an inactive one.
        /// </summary>
        public static Payer AddPayer(string name, string category, string country)
        {
            name = string.Join(" ", (name ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (Api)
            {
                // SavePayer reactivates an inactive payer of the same name; an active duplicate is fault 50210 → null.
                try { return ToModel(cnHelperObj.SavePayer(new PAT_SavePayerReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, Name = name, Category = category }).EnsureSuccess().Payer); }
                catch (ApiException ex) when (ex.ErrorNumber == 50210) { return null; }
            }
            lock (DataStore.Sync)
            {
                var existing = DataStore.Payers.FirstOrDefault(p => p.Country == country && p.Category == category
                    && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (existing.IsActive) return null;
                    existing.IsActive = true;
                    return existing;
                }
                var payer = new Payer { PayerId = DataStore.NextId("PAYER"), Name = name, Category = category, Country = country, IsActive = true };
                DataStore.Payers.Add(payer);
                return payer;
            }
        }

        /// <summary>An inactive payer is no longer offered for new coverage; coverage already on file keeps it.</summary>
        public static void TogglePayer(string payerId)
        {
            if (Api) { cnHelperObj.UpdatePayerActive(new PAT_UpdatePayerActiveReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PayerId = payerId, IsActive = null }).EnsureSuccess(); return; }
            lock (DataStore.Sync)
            {
                var p = DataStore.Payers.FirstOrDefault(x => x.PayerId == payerId);
                if (p != null) p.IsActive = !p.IsActive;
            }
        }
        public static List<PatientCoverage> ListCoverages(string patientId)
        {
            if (Api) return ApiCoverages(patientId).Where(c => c.IsActive).Select(RememberPayer).OrderBy(c => c.Priority).ToList();
            lock (DataStore.Sync)
                return DataStore.Coverages.Where(c => c.PatientId == patientId && c.IsActive).OrderBy(c => c.Priority).ToList();
        }

        /// <summary>
        /// Active coverages the hospital can bill: only those whose payer belongs to the hospital's country
        /// (a Jamaican clinic offers Sagicor / Guardian / NHF, never CGHS or an Indian insurer).
        /// </summary>
        public static List<PatientCoverage> ListCoverages(string patientId, string country)
        {
            if (Api)
            {
                // Payers are configured per hospital country; only a different country needs the payer filter.
                var all = ListCoverages(patientId);
                var hospital = HospitalService.GetHospital(null);
                if (hospital == null || string.IsNullOrEmpty(country) || country == hospital.Country) return all;
                var ids = new HashSet<string>(ListPayers(country, null).Select(p => p.PayerId));
                return all.Where(c => ids.Contains(c.PayerId)).ToList();
            }
            lock (DataStore.Sync)
                return DataStore.Coverages.Where(c => c.PatientId == patientId && c.IsActive)
                    .Where(c => DataStore.Payers.Any(p => p.PayerId == c.PayerId && p.Country == country))
                    .OrderBy(c => c.Priority).ToList();
        }

        /// <summary>API mode reads the coverage from the patient's list, so <paramref name="patientId"/> is required there.</summary>
        public static PatientCoverage GetCoverage(string coverageId, string patientId = null)
        {
            if (Api) return string.IsNullOrEmpty(patientId) ? null : ApiCoverages(patientId).Where(c => c.CoverageId == coverageId).Select(RememberPayer).FirstOrDefault();
            lock (DataStore.Sync) return DataStore.Coverages.FirstOrDefault(c => c.CoverageId == coverageId);
        }

        public static PatientCoverage AddCoverage(PatientCoverage record)
        {
            if (Api)
            {
                var saved = cnHelperObj.SavePatientCoverage(new PAT_SavePatientCoverageReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    PatientId = record.PatientId, CoverageType = record.CoverageType, PayerId = record.PayerId, PlanName = record.PlanName,
                    MemberId = record.MemberId, PolicyNumber = record.PolicyNumber, GroupNumber = record.GroupNumber, PolicyHolderName = record.PolicyHolderName,
                    Relationship = record.Relationship, ClaimMode = record.ClaimMode, GovtCardNumber = record.GovtCardNumber,
                    EffectiveDate = DocNyle.Date(record.EffectiveDate), ExpiryDate = DocNyle.Date(record.ExpiryDate),
                    Status = string.IsNullOrEmpty(record.Status) ? "ACTIVE" : record.Status,
                }).EnsureSuccess().Coverage;
                RequestCache.Remove("coverages:" + record.PatientId);
                return saved != null ? RememberPayer(saved) : record;
            }
            lock (DataStore.Sync)
            {
                record.CoverageId = DataStore.NextId("COV");
                record.IsActive = true;
                if (string.IsNullOrEmpty(record.Status)) record.Status = "ACTIVE";
                record.Priority = DataStore.Coverages.Count(c => c.PatientId == record.PatientId) + 1;
                DataStore.Coverages.Add(record);
                return record;
            }
        }

        public static void DeactivateCoverage(string coverageId, string patientId = null)
        {
            if (Api)
            {
                cnHelperObj.DeactivatePatientCoverage(new PAT_DeactivatePatientCoverageReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, CoverageId = coverageId, PatientId = patientId }).EnsureSuccess();
                if (patientId != null) RequestCache.Remove("coverages:" + patientId);
                return;
            }
            lock (DataStore.Sync)
            {
                var c = DataStore.Coverages.FirstOrDefault(x => x.CoverageId == coverageId);
                if (c != null) c.IsActive = false;
            }
        }

        /// <summary>
        /// "Sagicor Life · Executive Plus" for private cover; for a govt scheme the country's scheme label ("NHF"),
        /// plus the scheme name only when the country has more than one scheme (e.g. CGHS vs Aarogyasri in India).
        /// </summary>
        public static string CoverageLabel(PatientCoverage c, string country)
        {
            var payer = GetPayer(c.PayerId);
            if (c.CoverageType == "GOVT_SCHEME")
                return GetGovtSchemeProfile(country).Label + (payer != null && ListPayers(country, "GOVT_SCHEME").Count > 1 ? " · " + payer.Name : "");
            return (payer != null ? payer.Name : "Private insurance") + (string.IsNullOrEmpty(c.PlanName) ? "" : " · " + c.PlanName);
        }

        /// <summary>Maps a coverage row and remembers its payer name for <see cref="GetPayer"/>.</summary>
        private static PatientCoverage RememberPayer(PAT_CoverageInfo c)
        {
            if (!string.IsNullOrEmpty(c.PayerId) && c.PayerName != null) RequestCache.Get("payer-name:" + c.PayerId, () => c.PayerName);
            return ToModel(c);
        }

        private static bool Api { get { return DataSource.IsApi("Patients"); } }

        private static string CallerHospital { get { return AppSession.ApiUser != null ? AppSession.ApiUser.HospitalId : null; } }

        /* ── Patients ── */

        internal static Patient ToModel(PAT_PatientInfo p)
        {
            if (p == null) return null;
            return new Patient
            {
                PatientId = p.PatientId, HospitalId = CallerHospital, Mrn = p.Mrn, FullName = p.FullName, Dob = DocNyle.ParseLocal(p.Dob),
                Gender = p.Gender, Phone = p.Phone, Email = p.Email, Address = p.Address, NationalHealthId = p.NationalHealthId,
                SmokingStatus = p.SmokingStatus, SmokingPackYears = p.SmokingPackYears, AlcoholUse = p.AlcoholUse, Occupation = p.Occupation,
                PhysicalActivity = p.PhysicalActivity, Diet = p.Diet, BloodGroup = p.BloodGroup, EmergencyContactName = p.EmergencyContactName,
                EmergencyContactPhone = p.EmergencyContactPhone, InsuranceProvider = p.InsuranceProvider, InsurancePolicyNo = p.InsurancePolicyNo,
                RegisteredVia = p.RegisteredVia, Prefix = p.Prefix, FirstName = p.FirstName, MiddleName = p.MiddleName, LastName = p.LastName,
                Trn = p.Trn, CountryCode = p.CountryCode, Mobile = p.Mobile, PreferredLanguage = p.PreferredLanguage, AddrLine1 = p.AddrLine1,
                AddrLine2 = p.AddrLine2, City = p.City, Region = p.Region, Country = p.Country, MaritalStatus = p.MaritalStatus,
                ReferralSource = p.ReferralSource, PreferredDoctorId = p.PreferredDoctorId,
            };
        }

        internal static Patient ToModel(PAT_PatientSummaryInfo p)
        {
            return new Patient
            {
                PatientId = p.PatientId, HospitalId = CallerHospital, Mrn = p.Mrn, FullName = p.FullName, Dob = DocNyle.ParseLocal(p.Dob),
                Gender = p.Gender, Phone = p.Phone, Email = p.Email,
            };
        }

        /// <summary>GetPatient (audited READ by the service), cached for the request; NotFound → null.</summary>
        private static Patient ApiGet(string patientId)
        {
            if (string.IsNullOrEmpty(patientId)) return null;
            return RequestCache.Get("patient:" + patientId, () => ToModel(DocNyle.OrNull(() => cnHelperObj.GetPatient(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess())?.Patient));
        }

        private static Patient ApiCreate(Patient p)
        {
            var created = cnHelperObj.CreatePatient(new PAT_CreatePatientReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                FullName = p.FullName, Gender = p.Gender, Dob = DocNyle.Date(p.Dob), Phone = p.Phone, Email = p.Email, Address = p.Address,
                NationalHealthId = p.NationalHealthId, InsuranceProvider = p.InsuranceProvider, InsurancePolicyNo = p.InsurancePolicyNo,
                EmergencyContactName = p.EmergencyContactName, EmergencyContactPhone = p.EmergencyContactPhone, Prefix = p.Prefix,
                FirstName = p.FirstName, MiddleName = p.MiddleName, LastName = p.LastName, Trn = p.Trn, CountryCode = p.CountryCode,
                Mobile = p.Mobile, PreferredLanguage = p.PreferredLanguage, AddrLine1 = p.AddrLine1, AddrLine2 = p.AddrLine2, City = p.City,
                Region = p.Region, Country = p.Country, MaritalStatus = p.MaritalStatus, ReferralSource = p.ReferralSource,
                PreferredDoctorId = p.PreferredDoctorId, BloodGroup = p.BloodGroup, Occupation = p.Occupation,
            }).EnsureSuccess().Patient;
            // The service generates the id and MRN; the page gets the stored record back.
            p.PatientId = created.PatientId;
            p.Mrn = created.Mrn;
            p.HospitalId = CallerHospital;
            if (!string.IsNullOrEmpty(p.SmokingStatus) || !string.IsNullOrEmpty(p.AlcoholUse))
                ApiUpdateSocial(p.PatientId, p.SmokingStatus, p.SmokingPackYears, p.AlcoholUse, p.Occupation, p.PhysicalActivity, p.Diet);
            return p;
        }

        /// <summary>UpdatePatient: "" clears an optional text field, so every editable field is sent ("" for blank).</summary>
        private static Patient ApiUpdate(string patientId, Patient c)
        {
            Func<string, string> v = s => s ?? "";
            var updated = cnHelperObj.UpdatePatient(new PAT_UpdatePatientReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                PatientId = patientId, FullName = c.FullName, Gender = c.Gender, Dob = DocNyle.Date(c.Dob), Phone = v(c.Phone), Email = v(c.Email),
                Address = v(c.Address), NationalHealthId = v(c.NationalHealthId), EmergencyContactName = v(c.EmergencyContactName),
                EmergencyContactPhone = v(c.EmergencyContactPhone), Prefix = v(c.Prefix), FirstName = v(c.FirstName), MiddleName = v(c.MiddleName),
                LastName = v(c.LastName), Trn = v(c.Trn), CountryCode = v(c.CountryCode), Mobile = v(c.Mobile), PreferredLanguage = v(c.PreferredLanguage),
                AddrLine1 = v(c.AddrLine1), AddrLine2 = v(c.AddrLine2), City = v(c.City), Region = v(c.Region), Country = v(c.Country),
                MaritalStatus = string.IsNullOrEmpty(c.MaritalStatus) ? null : c.MaritalStatus, ReferralSource = v(c.ReferralSource),
                PreferredDoctorId = string.IsNullOrEmpty(c.PreferredDoctorId) ? null : c.PreferredDoctorId, BloodGroup = v(c.BloodGroup),
                Occupation = v(c.Occupation),
            }).EnsureSuccess().Patient;
            RequestCache.Remove("patient:" + patientId);
            return ToModel(updated) ?? ApiGet(patientId);
        }

        private static PatientDuplicate ApiFindDuplicate(Patient fields)
        {
            var d = cnHelperObj.CheckDuplicatePatient(AppSession.ApiHospitalId, AppSession.ApiUserId, fields.PatientId, NullIfBlank(fields.NationalHealthId), NullIfBlank(fields.Trn), NullIfBlank(fields.Mobile), AppSession.ApiHashValue).EnsureSuccess().Duplicate;
            if (d == null) return null;
            return new PatientDuplicate { Field = d.Field, Value = d.Value, Patient = new Patient { PatientId = d.PatientId, Mrn = d.Mrn, FullName = d.FullName, HospitalId = CallerHospital } };
        }

        private static void ApiUpdateSocial(string patientId, string smoking, int? packYears, string alcohol, string occupation, string activity, string diet)
        {
            cnHelperObj.UpdatePatientSocialHistory(new PAT_UpdatePatientSocialHistoryReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                PatientId = patientId, SmokingStatus = NullIfBlank(smoking), SmokingPackYears = packYears, AlcoholUse = NullIfBlank(alcohol),
                Occupation = NullIfBlank(occupation), PhysicalActivity = NullIfBlank(activity), Diet = NullIfBlank(diet),
            }).EnsureSuccess();
            RequestCache.Remove("patient:" + patientId);
        }

        private static string NullIfBlank(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }

        /* ── Allergies ── */

        private static List<PatientAllergy> ApiAllergies(string patientId)
        {
            return RequestCache.Get("allergies:" + patientId, () =>
                (cnHelperObj.ListAllergies(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().Allergies ?? new List<PAT_AllergyInfo>()).Select(a => new PatientAllergy
                {
                    AllergyId = a.AllergyId, PatientId = a.PatientId ?? patientId, AllergyType = a.AllergyType, AllergyName = a.AllergyName,
                    ReactionType = a.ReactionType, ReactionDescription = a.ReactionDescription, Severity = a.Severity, Status = a.Status,
                    IsTreated = a.IsTreated, IsFamilyHistory = a.IsFamilyHistory, SinceWhen = DocNyle.ParseLocal(a.SinceWhen), Notes = a.Notes,
                }).ToList());
        }

        private static PatientAllergy ApiSaveAllergy(PatientAllergy r)
        {
            PAT_AllergyInfo saved;
            if (string.IsNullOrEmpty(r.AllergyId))
                saved = cnHelperObj.CreateAllergy(new PAT_CreateAllergyReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    PatientId = r.PatientId, AllergyType = r.AllergyType, AllergyName = r.AllergyName, ReactionType = r.ReactionType,
                    ReactionDescription = r.ReactionDescription, Severity = r.Severity, IsTreated = r.IsTreated, IsFamilyHistory = r.IsFamilyHistory,
                    SinceWhen = DocNyle.Date(r.SinceWhen), Notes = r.Notes,
                }).EnsureSuccess().Allergy;
            else
                saved = cnHelperObj.UpdateAllergy(new PAT_UpdateAllergyReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    AllergyId = r.AllergyId, PatientId = r.PatientId, ReactionType = r.ReactionType, ReactionDescription = r.ReactionDescription,
                    Severity = r.Severity, Status = string.IsNullOrEmpty(r.Status) ? null : r.Status, IsTreated = r.IsTreated,
                    SinceWhen = DocNyle.Date(r.SinceWhen), Notes = r.Notes,
                }).EnsureSuccess().Allergy;
            RequestCache.Remove("allergies:" + r.PatientId);
            if (saved != null) r.AllergyId = saved.AllergyId;
            return r;
        }

        /* ── Medical history / events / family ── */

        private static List<PatientMedicalHistory> ApiMedicalHistory(string patientId)
        {
            return RequestCache.Get("medhist:" + patientId, () =>
                (cnHelperObj.ListPatientMedicalHistory(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().MedicalHistory ?? new List<PAT_MedicalHistoryInfo>()).Select(h => new PatientMedicalHistory
                {
                    PatientMedHistoryId = h.PatientMedHistoryId, PatientId = h.PatientId ?? patientId, ItemId = h.ItemId, ConditionName = h.ConditionName,
                    OnsetMonth = h.OnsetMonth, OnsetYear = h.OnsetYear, Status = h.Status, Notes = h.Notes, RecordedByUserId = h.RecordedByUserId,
                    RecordedAt = DocNyle.InstantOrMin(h.RecordedAt),
                }).ToList());
        }

        private static string HistoryItemId(string conditionName)
        {
            var item = HospitalService.ListMedicalHistoryItems(null, false).FirstOrDefault(i => string.Equals(i.ItemName, conditionName, StringComparison.OrdinalIgnoreCase));
            return item != null ? item.ItemId : null;
        }

        /// <summary>Upsert by condition name (SavePatientMedicalHistory).</summary>
        private static void ApiSaveMedicalHistory(string patientId, string conditionName, int? month, int? year, string status, string notes)
        {
            cnHelperObj.SavePatientMedicalHistory(new PAT_SavePatientMedicalHistoryReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                PatientId = patientId, ItemId = HistoryItemId(conditionName), ConditionName = conditionName, OnsetMonth = month, OnsetYear = year,
                Status = status, Notes = notes,
            }).EnsureSuccess();
            RequestCache.Remove("medhist:" + patientId);
        }

        private static PatientMedicalEvent ToModel(PAT_MedicalEventInfo e, string patientId)
        {
            if (e == null) return null;
            return new PatientMedicalEvent
            {
                PatientMedEventId = e.PatientMedEventId, PatientId = e.PatientId ?? patientId, EventId = e.EventId, EventName = e.EventName,
                EventMonth = e.EventMonth, EventYear = e.EventYear, Notes = e.Notes, RecordedByUserId = e.RecordedByUserId,
                RecordedAt = DocNyle.InstantOrMin(e.RecordedAt),
            };
        }

        private static List<PatientMedicalEvent> ApiMedicalEvents(string patientId)
        {
            return RequestCache.Get("medevents:" + patientId, () =>
                (cnHelperObj.ListPatientMedicalEvents(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, AppSession.ApiHashValue).EnsureSuccess().MedicalEvents ?? new List<PAT_MedicalEventInfo>()).Select(e => ToModel(e, patientId)).ToList());
        }

        private static PatientFamilyHistory ToModel(PAT_FamilyHistoryInfo f)
        {
            if (f == null) return null;
            return new PatientFamilyHistory
            {
                FamilyHistoryId = f.FamilyHistoryId, PatientId = f.PatientId, RelationshipType = f.RelationshipType, ConditionName = f.ConditionName,
                Notes = f.Notes, RecordedAt = DocNyle.InstantOrMin(f.RecordedAt),
            };
        }

        /* ── Consent documents ── */

        private static PatientConsentDocument ToModel(PAT_ConsentDocumentInfo d)
        {
            if (d == null) return null;
            return new PatientConsentDocument
            {
                ConsentDocumentId = d.ConsentDocumentId, PatientId = d.PatientId, HospitalId = CallerHospital, FormVersion = d.FormVersion,
                FileName = d.FileName, ContentType = d.ContentType, SizeBytes = d.SizeBytes, UploadedByUserId = d.UploadedByUserId,
                UploadedAt = DocNyle.InstantOrMin(d.UploadedAt),
            };
        }

        /* ── Payers & coverage ── */

        private static Payer ToModel(PAT_PayerInfo p)
        {
            if (p == null) return null;
            return new Payer { PayerId = p.PayerId, Name = p.Name, Category = p.Category, Country = p.Country, IsActive = p.IsActive };
        }

        private static PatientCoverage ToModel(PAT_CoverageInfo c)
        {
            return new PatientCoverage
            {
                CoverageId = c.CoverageId, HospitalId = CallerHospital, PatientId = c.PatientId, CoverageType = c.CoverageType, PayerId = c.PayerId,
                PlanName = c.PlanName, MemberId = c.MemberId, PolicyNumber = c.PolicyNumber, GroupNumber = c.GroupNumber,
                PolicyHolderName = c.PolicyHolderName, Relationship = c.Relationship, ClaimMode = c.ClaimMode,
                EffectiveDate = DocNyle.ParseLocal(c.EffectiveDate), ExpiryDate = DocNyle.ParseLocal(c.ExpiryDate), Status = c.Status,
                GovtCardNumber = c.GovtCardNumber, Priority = c.Priority, IsActive = c.IsActive,
            };
        }

        /// <summary>The patient's coverages (ListPatientCoverages, active only), cached for the request; also remembers each row's payer name.</summary>
        private static List<PAT_CoverageInfo> ApiCoverages(string patientId)
        {
            return RequestCache.Get("coverages:" + patientId, () => cnHelperObj.ListPatientCoverages(AppSession.ApiHospitalId, AppSession.ApiUserId, patientId, "false", AppSession.ApiHashValue).EnsureSuccess().Coverages ?? new List<PAT_CoverageInfo>());
        }
    }
}
