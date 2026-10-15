using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Hosting;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>Patient registration/search, allergies, medical/family history and insurance coverage.</summary>
    public static class PatientService
    {
        public static Patient Get(string patientId)
        {
            lock (DataStore.Sync) return DataStore.Patients.FirstOrDefault(p => p.PatientId == patientId);
        }

        /// <summary>Search by name, MRN, phone or national health id. Fewer than 2 characters returns every patient.</summary>
        public static List<Patient> Search(string hospitalId, string term)
        {
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
            lock (DataStore.Sync)
                return DataStore.Allergies.Where(a => a.PatientId == patientId && (!activeOnly || a.Status == "ACTIVE")).ToList();
        }

        public static PatientAllergy GetAllergy(string allergyId)
        {
            lock (DataStore.Sync) return DataStore.Allergies.FirstOrDefault(a => a.AllergyId == allergyId);
        }

        public static PatientAllergy SaveAllergy(PatientAllergy record)
        {
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
        public static void DeactivateAllergy(string allergyId)
        {
            lock (DataStore.Sync)
            {
                var a = DataStore.Allergies.FirstOrDefault(x => x.AllergyId == allergyId);
                if (a != null) a.Status = "INACTIVE";
            }
        }

        /* ── Medical history / events / family ── */

        public static List<PatientMedicalHistory> ListMedicalHistory(string patientId)
        {
            lock (DataStore.Sync) return DataStore.PatientMedicalHistory.Where(h => h.PatientId == patientId).ToList();
        }

        /// <summary>Checklist upsert by condition name (null month/year allowed).</summary>
        public static void SaveMedicalHistory(string patientId, string conditionName, int? month, int? year, string notes, string userId)
        {
            lock (DataStore.Sync)
            {
                var h = DataStore.PatientMedicalHistory.FirstOrDefault(x => x.PatientId == patientId && x.ConditionName == conditionName);
                if (h == null)
                {
                    var item = DataStore.MedicalHistoryItems.FirstOrDefault(i => i.ItemName == conditionName);
                    h = new PatientMedicalHistory
                    {
                        PatientMedHistoryId = DataStore.NextId("PMH"), PatientId = patientId, ItemId = item != null ? item.ItemId : null,
                        ConditionName = conditionName, Status = "ACTIVE", RecordedByUserId = userId, RecordedAt = DateTime.Now
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
            lock (DataStore.Sync) DataStore.PatientMedicalHistory.RemoveAll(h => h.PatientId == patientId && h.ConditionName == conditionName);
        }

        public static PatientMedicalHistory GetMedicalHistory(string id)
        {
            lock (DataStore.Sync) return DataStore.PatientMedicalHistory.FirstOrDefault(h => h.PatientMedHistoryId == id);
        }

        /// <summary>Add/edit from the patient record. Editing goes by id; adding a condition the patient already has updates that row.</summary>
        public static PatientMedicalHistory SaveMedicalHistoryRecord(PatientMedicalHistory record)
        {
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
                        RecordedByUserId = record.RecordedByUserId, RecordedAt = DateTime.Now
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

        public static void DeleteMedicalHistoryRecord(string id)
        {
            lock (DataStore.Sync) DataStore.PatientMedicalHistory.RemoveAll(h => h.PatientMedHistoryId == id);
        }

        public static PatientMedicalEvent GetMedicalEvent(string id)
        {
            lock (DataStore.Sync) return DataStore.PatientMedicalEvents.FirstOrDefault(e => e.PatientMedEventId == id);
        }

        /// <summary>Add/edit a surgery or medical event; the name may come from the configured list or be typed in.</summary>
        public static PatientMedicalEvent SaveMedicalEventRecord(PatientMedicalEvent record)
        {
            lock (DataStore.Sync)
            {
                var ev = string.IsNullOrEmpty(record.PatientMedEventId) ? null : DataStore.PatientMedicalEvents.FirstOrDefault(e => e.PatientMedEventId == record.PatientMedEventId);
                if (ev == null)
                {
                    ev = new PatientMedicalEvent { PatientMedEventId = DataStore.NextId("PME"), PatientId = record.PatientId, RecordedByUserId = record.RecordedByUserId, RecordedAt = DateTime.Now };
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
            lock (DataStore.Sync) DataStore.PatientMedicalEvents.RemoveAll(e => e.PatientMedEventId == id);
        }

        public static List<PatientMedicalEvent> ListMedicalEvents(string patientId)
        {
            lock (DataStore.Sync) return DataStore.PatientMedicalEvents.Where(e => e.PatientId == patientId).ToList();
        }

        public static void AddMedicalEvent(string patientId, string eventId, int? year, string notes, string userId)
        {
            lock (DataStore.Sync)
            {
                var item = DataStore.MedicalEventItems.FirstOrDefault(i => i.EventId == eventId);
                if (item == null || DataStore.PatientMedicalEvents.Any(e => e.PatientId == patientId && e.EventId == eventId)) return;
                DataStore.PatientMedicalEvents.Add(new PatientMedicalEvent
                {
                    PatientMedEventId = DataStore.NextId("PME"), PatientId = patientId, EventId = eventId, EventName = item.EventName,
                    EventYear = year, Notes = notes, RecordedByUserId = userId, RecordedAt = DateTime.Now
                });
            }
        }

        public static List<PatientFamilyHistory> ListFamilyHistory(string patientId)
        {
            lock (DataStore.Sync) return DataStore.FamilyHistory.Where(f => f.PatientId == patientId).ToList();
        }

        public static void AddFamilyHistory(string patientId, string relationship, string condition, string notes)
        {
            lock (DataStore.Sync)
                DataStore.FamilyHistory.Add(new PatientFamilyHistory
                {
                    FamilyHistoryId = DataStore.NextId("PFH"), PatientId = patientId, RelationshipType = relationship,
                    ConditionName = condition, Notes = notes, RecordedAt = DateTime.Now
                });
        }

        public static PatientFamilyHistory GetFamilyHistory(string id)
        {
            lock (DataStore.Sync) return DataStore.FamilyHistory.FirstOrDefault(f => f.FamilyHistoryId == id);
        }

        public static void UpdateFamilyHistory(string id, string relationship, string condition, string notes)
        {
            lock (DataStore.Sync)
            {
                var f = DataStore.FamilyHistory.FirstOrDefault(x => x.FamilyHistoryId == id);
                if (f == null) return;
                f.RelationshipType = relationship; f.ConditionName = condition; f.Notes = notes;
            }
        }

        public static void DeleteFamilyHistory(string id)
        {
            lock (DataStore.Sync) DataStore.FamilyHistory.RemoveAll(f => f.FamilyHistoryId == id);
        }

        /* ── Signed consent form (Consent for Sharing Health Information) ── */

        /// <summary>Printed on the form and stored with each signed copy, so a later wording change is traceable.</summary>
        public const string ConsentFormVersion = "JM-SHARE-1.0";

        private static string ConsentStorageRoot { get { return HostingEnvironment.MapPath("~/App_Data/consent-forms"); } }

        /// <summary>Signed copies for the patient, newest first (the first one is the current consent).</summary>
        public static List<PatientConsentDocument> ListConsentDocuments(string patientId)
        {
            lock (DataStore.Sync) return DataStore.ConsentDocuments.Where(d => d.PatientId == patientId).OrderByDescending(d => d.UploadedAt).ToList();
        }

        public static PatientConsentDocument GetConsentDocument(string documentId)
        {
            lock (DataStore.Sync) return DataStore.ConsentDocuments.FirstOrDefault(d => d.ConsentDocumentId == documentId);
        }

        /// <summary>Stores a file already checked with LabService.CheckFile(…, allowDicom: false) under App_Data (never web-served).</summary>
        public static PatientConsentDocument SaveConsentDocument(Patient patient, string fileName, long length, Stream content, string userId)
        {
            var name = Path.GetFileName(fileName);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var doc = new PatientConsentDocument
            {
                ConsentDocumentId = DataStore.NextId("PCD"), PatientId = patient.PatientId, HospitalId = patient.HospitalId,
                FormVersion = ConsentFormVersion, FileName = name, ContentType = LabService.ContentTypeFor(ext), SizeBytes = length,
                StoredFileName = Guid.NewGuid().ToString("N") + ext, UploadedByUserId = userId, UploadedAt = DateTime.Now,
            };
            var dir = Path.Combine(ConsentStorageRoot, patient.HospitalId);
            Directory.CreateDirectory(dir);
            content.Position = 0;
            using (var fs = new FileStream(Path.Combine(dir, doc.StoredFileName), FileMode.CreateNew, FileAccess.Write))
                content.CopyTo(fs);
            lock (DataStore.Sync) DataStore.ConsentDocuments.Add(doc);
            return doc;
        }

        public static string ConsentDocumentPath(PatientConsentDocument doc)
        {
            return Path.Combine(ConsentStorageRoot, doc.HospitalId, doc.StoredFileName);
        }

        /// <summary>Social history lives on the patient row (also editable at registration, except pack-years/activity/diet).</summary>
        public static void UpdateSocialHistory(string patientId, string smoking, int? packYears, string alcohol, string occupation, string activity, string diet)
        {
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
            lock (DataStore.Sync)
                return DataStore.Payers.Where(p => p.IsActive && p.Country == country && (category == null || p.Category == category)).ToList();
        }

        public static Payer GetPayer(string payerId)
        {
            lock (DataStore.Sync) return DataStore.Payers.FirstOrDefault(p => p.PayerId == payerId);
        }

        /// <summary>Every payer of the category for the country, inactive ones included (Settings → Billing &amp; Numbers).</summary>
        public static List<Payer> ListAllPayers(string country, string category)
        {
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
            lock (DataStore.Sync)
            {
                var p = DataStore.Payers.FirstOrDefault(x => x.PayerId == payerId);
                if (p != null) p.IsActive = !p.IsActive;
            }
        }
        public static List<PatientCoverage> ListCoverages(string patientId)
        {
            lock (DataStore.Sync)
                return DataStore.Coverages.Where(c => c.PatientId == patientId && c.IsActive).OrderBy(c => c.Priority).ToList();
        }

        /// <summary>
        /// Active coverages the hospital can bill: only those whose payer belongs to the hospital's country
        /// (a Jamaican clinic offers Sagicor / Guardian / NHF, never CGHS or an Indian insurer).
        /// </summary>
        public static List<PatientCoverage> ListCoverages(string patientId, string country)
        {
            lock (DataStore.Sync)
                return DataStore.Coverages.Where(c => c.PatientId == patientId && c.IsActive)
                    .Where(c => DataStore.Payers.Any(p => p.PayerId == c.PayerId && p.Country == country))
                    .OrderBy(c => c.Priority).ToList();
        }

        public static PatientCoverage GetCoverage(string coverageId)
        {
            lock (DataStore.Sync) return DataStore.Coverages.FirstOrDefault(c => c.CoverageId == coverageId);
        }

        public static PatientCoverage AddCoverage(PatientCoverage record)
        {
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

        public static void DeactivateCoverage(string coverageId)
        {
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
    }
}
