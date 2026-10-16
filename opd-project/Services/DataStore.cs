using System;
using System.Collections.Generic;
using System.Linq;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// Centralized in-memory data store — the temporary stand-in for a database.
    /// Every list mirrors one table in docs/schema.sql and is seeded with the same
    /// demo data the original js/data.js shipped with. Data lives for the lifetime of
    /// the application domain (an IIS Express restart re-seeds it).
    /// All reads and writes go through the service classes, which lock on <see cref="Sync"/>.
    /// </summary>
    public static class DataStore
    {
        public static readonly object Sync = new object();

        private static int _seq = 1000;

        /// <summary>Next unique id with the given prefix, e.g. NextId("PAT") → PAT-1001.</summary>
        public static string NextId(string prefix)
        {
            lock (Sync)
            {
                _seq++;
                return prefix + "-" + _seq;
            }
        }

        public static List<Hospital> Hospitals = new List<Hospital>();
        public static List<AppUser> Users = new List<AppUser>();
        public static List<LabPartner> LabPartners = new List<LabPartner>();
        public static List<UserNotification> UserNotifications = new List<UserNotification>();
        public static Dictionary<string, Dictionary<string, bool>> Permissions = new Dictionary<string, Dictionary<string, bool>>();
        public static List<Specialty> Specialties = new List<Specialty>();
        public static List<Patient> Patients = new List<Patient>();
        public static List<PatientAllergy> Allergies = new List<PatientAllergy>();
        public static List<MedicalHistoryItem> MedicalHistoryItems = new List<MedicalHistoryItem>();
        public static List<PatientMedicalHistory> PatientMedicalHistory = new List<PatientMedicalHistory>();
        public static List<MedicalEventItem> MedicalEventItems = new List<MedicalEventItem>();
        public static List<PatientMedicalEvent> PatientMedicalEvents = new List<PatientMedicalEvent>();
        public static List<PatientFamilyHistory> FamilyHistory = new List<PatientFamilyHistory>();
        public static List<PatientConsentDocument> ConsentDocuments = new List<PatientConsentDocument>();
        public static List<Consultation> Consultations = new List<Consultation>();
        public static List<VitalConfig> VitalsConfig = new List<VitalConfig>();
        public static List<ConsultationVitals> Vitals = new List<ConsultationVitals>();
        public static List<SymptomCatalogItem> SymptomCatalog = new List<SymptomCatalogItem>();
        public static List<ConsultationSymptom> Symptoms = new List<ConsultationSymptom>();
        public static List<Icd10Entry> Icd10Catalog = new List<Icd10Entry>();
        public static List<ConsultationDiagnosis> Diagnoses = new List<ConsultationDiagnosis>();
        public static List<DoctorSlotConfig> SlotConfig = new List<DoctorSlotConfig>();
        public static List<HospitalSetting> HospitalSettings = new List<HospitalSetting>();
        public static List<DoctorSetting> DoctorSettings = new List<DoctorSetting>();
        public static List<DoctorLeave> Leave = new List<DoctorLeave>();
        public static List<AppointmentType> AppointmentTypes = new List<AppointmentType>();
        public static List<DoctorSpecialty> DoctorSpecialties = new List<DoctorSpecialty>();
        public static List<IntakeQuestion> IntakeQuestions = new List<IntakeQuestion>();
        public static List<IntakeResponse> IntakeResponses = new List<IntakeResponse>();
        public static List<ConsentForm> ConsentForms = new List<ConsentForm>();
        public static List<ConsentRecord> ConsentRecords = new List<ConsentRecord>();
        public static List<ClinicalCondition> Conditions = new List<ClinicalCondition>();
        public static List<Appointment> Appointments = new List<Appointment>();
        public static List<Drug> Formulary = new List<Drug>();
        public static List<Prescription> Prescriptions = new List<Prescription>();
        public static List<LabTest> LabTests = new List<LabTest>();
        public static List<DoctorLabFavorite> LabFavorites = new List<DoctorLabFavorite>();
        public static List<InvestigationOrder> Orders = new List<InvestigationOrder>();
        public static List<LabResult> LabResults = new List<LabResult>();
        public static List<LabResultAttachment> LabResultAttachments = new List<LabResultAttachment>();
        public static List<Referral> Referrals = new List<Referral>();
        public static List<ConsultationServiceItem> ConsultationServiceItems = new List<ConsultationServiceItem>();
        public static List<Addendum> Addenda = new List<Addendum>();
        public static List<ConsultationSketch> Sketches = new List<ConsultationSketch>();
        public static List<ConfidentialNote> ConfidentialNotes = new List<ConfidentialNote>();
        public static List<ConsultationAttestation> Attestations = new List<ConsultationAttestation>();
        public static List<Phrase> Phrases = new List<Phrase>();
        public static List<Compliance> Compliances = new List<Compliance>();
        public static List<HospitalCompliance> HospitalCompliances = new List<HospitalCompliance>();
        public static ChannelConfig WhatsAppConfig = new ChannelConfig();
        public static ChannelConfig EmailConfig = new ChannelConfig();
        public static List<MessageTemplate> MessageTemplates = new List<MessageTemplate>();
        public static List<QueuedMessage> Messages = new List<QueuedMessage>();
        public static List<DoctorFee> Fees = new List<DoctorFee>();
        public static List<PaymentMethod> PaymentMethods = new List<PaymentMethod>();
        public static List<AuditLogEntry> AuditLogs = new List<AuditLogEntry>();
        public static List<ServiceCharge> ServiceCharges = new List<ServiceCharge>();
        public static List<InventoryItem> InventoryItems = new List<InventoryItem>();
        public static List<StockBatch> StockBatches = new List<StockBatch>();
        public static List<StockMovement> StockMovements = new List<StockMovement>();
        public static List<Invoice> Invoices = new List<Invoice>();
        public static List<InvoiceLineItem> LineItems = new List<InvoiceLineItem>();
        public static List<CreditNote> CreditNotes = new List<CreditNote>();
        public static List<Payment> Payments = new List<Payment>();
        public static List<Receipt> Receipts = new List<Receipt>();
        public static List<Payer> Payers = new List<Payer>();
        public static List<PatientCoverage> Coverages = new List<PatientCoverage>();
        public static List<EligibilityVerification> Verifications = new List<EligibilityVerification>();
        public static List<InvoiceCoverageLine> CoverageLines = new List<InvoiceCoverageLine>();
        /// <summary>Consultation form configuration per hospital: the live copy and the working draft (cfg.ConsultationFormConfig).</summary>
        public static Dictionary<string, ConsultFormConfig> ConsultFormPublished = new Dictionary<string, ConsultFormConfig>();
        public static Dictionary<string, ConsultFormConfig> ConsultFormDrafts = new Dictionary<string, ConsultFormConfig>();
        /// <summary>Values captured in custom consultation fields.</summary>
        public static List<ConsultationCustomValue> CustomFieldValues = new List<ConsultationCustomValue>();

        private static bool _seeded;

        /// <summary>Loads the demo data. Called once from Global.Application_Start.</summary>
        public static void Seed()
        {
            lock (Sync)
            {
                if (_seeded) return;
                SeedAdministration();
                SeedPatients();
                SeedClinicalCatalogs();
                SeedScheduling();
                SeedBilling();
                SeedInventory();
                SeedMessaging();
                foreach (var h in Hospitals) SchedulingService.RefreshDoctorNames(h.HospitalId);
                _seeded = true;
            }
        }

        private static DateTime D(string iso) { return DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture); }

        private static void SeedAdministration()
        {
            Hospitals.Add(new Hospital
            {
                HospitalId = "HOSP-001", Name = "Medcity Orthopaedic & Medical Centre",
                LegalAddress = "12 Market Street, Montego Bay, St. James, Jamaica", PrimaryEmail = "admin@sunriseclinic.example",
                ContactNumber = "+1-876-555-0123", Country = "JM", Currency = "JMD", Timezone = "America/Jamaica",
                PatientIdLabel = "MRN", NationalHealthIdLabel = "National Health ID", NationalHealthIdMandatory = false,
                GrowthChartStandard = "WHO", DefaultRxValidityDays = 30, DefaultTempUnit = "F",
                RxPrefix = "RX", ReceiptPrefix = "RCP", InvoicePrefix = "INV", LabChargePerTest = 150
            });
            Hospitals.Add(new Hospital
            {
                HospitalId = "HOSP-002", Name = "Kingston Family Health Centre",
                LegalAddress = "45 Half Way Tree Rd, Kingston, Jamaica", PrimaryEmail = "admin@kingstonfhc.example",
                ContactNumber = "+1-876-555-0100", Country = "JM", Currency = "JMD", Timezone = "America/Jamaica",
                PatientIdLabel = "Patient ID", NationalHealthIdLabel = "National ID", NationalHealthIdMandatory = true,
                GrowthChartStandard = "CDC", DefaultRxValidityDays = 30, DefaultTempUnit = "F",
                RxPrefix = "RX", ReceiptPrefix = "RCP", InvoicePrefix = "INV", LabChargePerTest = 150
            });

            string[] features = { "consultation", "patients", "scheduling", "billing", "reports", "admin_hospital", "admin_scheduling",
                "admin_vitals", "admin_specialty", "admin_formulary", "admin_phrases", "admin_users", "audit_log", "data_export",
                "data_import", "print", "lab_portal", "admin_consultation", "admin_inventory" };
            var grants = new Dictionary<string, string[]>
            {
                { "ADMIN", features },
                { "DOCTOR", new[] { "consultation", "patients", "scheduling", "reports", "admin_scheduling", "admin_phrases", "admin_consultation", "print" } },
                { "RECEPTIONIST", new[] { "patients", "scheduling", "billing" } },
                { "BILLING", new[] { "patients", "billing", "reports", "print" } },
                { "NURSE", new[] { "patients", "scheduling" } },
                { "LAB_PARTNER", new[] { "lab_portal" } },
            };
            foreach (var role in grants)
            {
                var row = new Dictionary<string, bool>();
                foreach (var f in features) row[f] = role.Value.Contains(f);
                Permissions[role.Key] = row;
            }

            LabPartners.Add(new LabPartner { LabPartnerId = "LAB-001", HospitalId = "HOSP-001", Name = "Sunrise Diagnostics Lab", IsActive = true });
            LabPartners.Add(new LabPartner { LabPartnerId = "LAB-002", HospitalId = "HOSP-001", Name = "City Imaging Center", IsActive = true });

            Users.AddRange(new[]
            {
                Doctor("USR-001", "HOSP-001", "Dr. A. Sharma", "drsharma@sunrise.in", "+1-876-555-0001", "SPEC-GEN", "MCI-4432", "A.", "Sharma", "MBBS, MD (General Medicine)", "OPD-1", "English, Telugu, Hindi", "Consultant physician — general medicine."),
                new AppUser { UserId = "USR-002", HospitalId = "HOSP-001", FullName = "Priya Nair", Email = "priya@sunrise.in", Phone = "+1-876-555-0002", Password = "demo123", Role = "RECEPTIONIST", IsActive = true },
                Doctor("USR-003", "HOSP-002", "Dr. K. Bailey", "k.bailey@kingstonfhc.example", "+1-876-5550101", "SPEC-GEN", "JMC-1187", "K.", "Bailey", "MBBS, DM (Family Medicine)", "Room 2", "English", "Family physician."),
                new AppUser { UserId = "USR-004", HospitalId = "HOSP-001", FullName = "Admin User", Email = "admin@sunrise.in", Phone = "", Password = "admin123", Role = "ADMIN", IsActive = true },
                new AppUser { UserId = "USR-005", HospitalId = "HOSP-001", FullName = "Billing Staff", Email = "billing@sunrise.in", Phone = "", Password = "demo123", Role = "BILLING", IsActive = true },
                new AppUser { UserId = "USR-006", HospitalId = "HOSP-001", FullName = "Sunrise Diagnostics Lab", Email = "lab@sunrisediag.in", Phone = "+1-876-555-3333", Password = "lab123", Role = "LAB_PARTNER", LabPartnerId = "LAB-001", IsActive = true },
                new AppUser { UserId = "USR-007", HospitalId = "HOSP-001", FullName = "City Imaging Center", Email = "imaging@cityimaging.in", Phone = "+1-876-555-4444", Password = "lab123", Role = "LAB_PARTNER", LabPartnerId = "LAB-002", IsActive = true },
                Doctor("USR-008", "HOSP-001", "Dr. Ravi Teja Varma", "ravi.varma@sunrise.in", "+1-876-555-0008", "SPEC-GEN", "APMC-58213", "Ravi Teja", "Varma", "MBBS", "OPD-2", "English, Telugu", "General physician."),
                Doctor("USR-009", "HOSP-001", "Dr. Lakshmi Prasanna", "lakshmi.prasanna@sunrise.in", "+1-876-555-0009", "SPEC-GEN", "APMC-61047", "Lakshmi", "Prasanna", "MBBS", "OPD-3", "English, Telugu, Tamil", "General physician."),
                Doctor("USR-010", "HOSP-001", "Dr. Suresh Reddy", "suresh.reddy@sunrise.in", "+1-876-555-0010", "SPEC-CARD", "APMC-40982", "Suresh", "Reddy", "MBBS, MD, DM (Cardiology)", "Cardiology OPD", "English, Telugu, Hindi", "Visiting consultant cardiologist."),
                Doctor("USR-011", "HOSP-001", "Dr. Kavitha Rao", "kavitha.rao@sunrise.in", "+1-876-555-0011", "SPEC-PED", "APMC-47315", "Kavitha", "Rao", "MBBS, MD (Pediatrics)", "Child Care OPD", "English, Telugu, Kannada", "Visiting consultant pediatrician."),
                new AppUser { UserId = "USR-012", HospitalId = "HOSP-001", FullName = "Nurse Anitha", Email = "nurse@sunrise.in", Phone = "", Password = "demo123", Role = "NURSE", IsActive = true },
            });

            Specialties.Add(new Specialty { SpecialtyId = "SPEC-GEN", HospitalId = "HOSP-001", SpecialtyName = "General Medicine", IsActive = true });
            Specialties.Add(new Specialty { SpecialtyId = "SPEC-CARD", HospitalId = "HOSP-001", SpecialtyName = "Cardiology", IsActive = true });
            Specialties.Add(new Specialty { SpecialtyId = "SPEC-PED", HospitalId = "HOSP-001", SpecialtyName = "Pediatrics", IsActive = true });

            Compliances.AddRange(new[]
            {
                new Compliance { ComplianceId = "CMP-001", ComplianceName = "GSTN", Country = "IN", IsMandatory = true, DisplayLabel = "GST Number", PrintLabel = "GSTIN", SortOrder = 1, IsActive = true },
                new Compliance { ComplianceId = "CMP-002", ComplianceName = "ROHINI_ID", Country = "IN", IsMandatory = false, DisplayLabel = "ROHINI ID", PrintLabel = "ROHINI", SortOrder = 2, IsActive = true },
                new Compliance { ComplianceId = "CMP-003", ComplianceName = "CEA_REG", Country = "IN", IsMandatory = false, DisplayLabel = "Clinical Establishment Registration", PrintLabel = "CEA Reg.", SortOrder = 3, IsActive = true },
                new Compliance { ComplianceId = "CMP-004", ComplianceName = "ABDM_REG", Country = "IN", IsMandatory = false, DisplayLabel = "ABDM Registration", PrintLabel = "ABDM", SortOrder = 4, IsActive = true },
                new Compliance { ComplianceId = "CMP-005", ComplianceName = "MOH_LICENSE", Country = "JM", IsMandatory = true, DisplayLabel = "MOH License Number", PrintLabel = "MOH Lic.", SortOrder = 1, IsActive = true },
                new Compliance { ComplianceId = "CMP-006", ComplianceName = "TRN", Country = "JM", IsMandatory = true, DisplayLabel = "Tax Registration Number", PrintLabel = "TRN", SortOrder = 2, IsActive = true },
            });
            HospitalCompliances.Add(new HospitalCompliance { HospitalComplianceId = "HC-001", HospitalId = "HOSP-001", ComplianceId = "CMP-001", RegistrationNumber = "29AABCS1429B1ZX", IssuedDate = D("2020-01-01"), IsActive = true });
            HospitalCompliances.Add(new HospitalCompliance { HospitalComplianceId = "HC-002", HospitalId = "HOSP-001", ComplianceId = "CMP-003", RegistrationNumber = "KA/BLR/2024/0847", IssuedDate = D("2024-03-15"), ExpiryDate = D("2029-03-14"), IsActive = true });

            var defaults = new Dictionary<string, string>
            {
                { "APPT_SLOT_MINS", "15" }, { "APPT_BUFFER_MINS", "0" }, { "FREE_FOLLOWUP_ALLOWED", "true" }, { "FREE_FOLLOWUP_DAYS", "7" },
                { "DATE_FORMAT", "DD/MM/YYYY" }, { "TIME_FORMAT", "12h" }, { "MRN_PREFIX", "MRN" }, { "MRN_FORMAT", "{prefix}-{seq:5}" },
                { "DOCTOR_NAME_TEMPLATE", "{prefix} {first} {last}" },
            };
            var mrnSeq = new Dictionary<string, string> { { "HOSP-001", "90825" }, { "HOSP-002", "10000" } };
            foreach (var hid in new[] { "HOSP-001", "HOSP-002" })
            {
                foreach (var kv in defaults) HospitalSettings.Add(new HospitalSetting { HospitalId = hid, SettingKey = kv.Key, Value = kv.Value });
                HospitalSettings.Add(new HospitalSetting { HospitalId = hid, SettingKey = "MRN_SEQ", Value = mrnSeq[hid] });
            }
            DoctorSettings.Add(new DoctorSetting { HospitalId = "HOSP-001", DoctorId = "USR-010", SettingKey = "APPT_SLOT_MINS", Value = "20" });
            DoctorSettings.Add(new DoctorSetting { HospitalId = "HOSP-001", DoctorId = "USR-010", SettingKey = "FREE_FOLLOWUP_DAYS", Value = "14" });
            DoctorSettings.Add(new DoctorSetting { HospitalId = "HOSP-001", DoctorId = "USR-011", SettingKey = "APPT_SLOT_MINS", Value = "20" });
        }

        private static AppUser Doctor(string id, string hid, string fullName, string email, string phone, string spec, string license,
            string first, string last, string quals, string room, string langs, string bio)
        {
            return new AppUser
            {
                UserId = id, HospitalId = hid, FullName = fullName, Email = email, Phone = phone, Password = "demo123", Role = "DOCTOR",
                SpecialtyId = spec, LicenseNumber = license, Prefix = "Dr.", FirstName = first, MiddleName = "", LastName = last,
                Qualifications = quals, DisplayNameOverride = "", Room = room, Languages = langs, Bio = bio, IsActive = true
            };
        }

        private static void SeedPatients()
        {
            Patients.AddRange(new[]
            {
                new Patient { PatientId = "PAT-001", HospitalId = "HOSP-001", Mrn = "MRN-90821", FullName = "John Doe", Dob = D("1992-03-14"), Gender = "MALE", Phone = "+1-876-555-1111", Email = "", Address = "", NationalHealthId = "" },
                new Patient { PatientId = "PAT-002", HospitalId = "HOSP-001", Mrn = "MRN-90822", FullName = "Priya Reddy", Dob = D("1988-07-22"), Gender = "FEMALE", Phone = "+1-876-555-1112", Email = "", Address = "", NationalHealthId = "NHID-1122-3344" },
                new Patient { PatientId = "PAT-003", HospitalId = "HOSP-001", Mrn = "MRN-90823", FullName = "Ramesh Kumar", Dob = D("1965-11-02"), Gender = "MALE", Phone = "+1-876-555-1113", Email = "", Address = "", NationalHealthId = "" },
                new Patient { PatientId = "PAT-004", HospitalId = "HOSP-001", Mrn = "MRN-90824", FullName = "Ananya Iyer", Dob = D("2001-01-30"), Gender = "FEMALE", Phone = "+1-876-555-1114", Email = "", Address = "", NationalHealthId = "" },
                new Patient { PatientId = "PAT-005", HospitalId = "HOSP-001", Mrn = "MRN-90825", FullName = "Vikram Singh", Dob = D("1979-05-18"), Gender = "MALE", Phone = "+1-876-555-1115", Email = "", Address = "", NationalHealthId = "", SmokingStatus = "CURRENT", SmokingPackYears = 10, AlcoholUse = "OCCASIONAL", Occupation = "Business owner" },
            });

            Allergies.Add(new PatientAllergy { AllergyId = "ALG-001", PatientId = "PAT-001", AllergyType = "DRUG", AllergyName = "Penicillin", ReactionType = "Allergy", ReactionDescription = "Skin rash, hives", Severity = "SEVERE", Status = "ACTIVE", SinceWhen = D("2018-06-01"), Notes = "" });
            Allergies.Add(new PatientAllergy { AllergyId = "ALG-002", PatientId = "PAT-003", AllergyType = "FOOD", AllergyName = "Shellfish", ReactionType = "Allergy", ReactionDescription = "Swelling, breathing difficulty", Severity = "MODERATE", Status = "ACTIVE", SinceWhen = D("2010-01-01"), Notes = "" });

            string[] history = { "Diabetes", "Heart Disease", "Hypertension", "Hyperlipidemias", "Osteoarthritis", "Asthma / COPD / Bronchitis", "Cancer",
                "Alcohol or Drug Abuse", "HIV / STD / Related Ailments", "Hypothyroidism", "CKD", "Epilepsy", "Depression / Anxiety", "Rheumatoid Arthritis" };
            for (int i = 0; i < history.Length; i++)
                MedicalHistoryItems.Add(new MedicalHistoryItem { ItemId = "MHI-" + (i + 1).ToString("000"), HospitalId = "HOSP-001", ItemName = history[i], IsQuickPick = true, SortOrder = i + 1, IsActive = true });

            PatientMedicalHistory.Add(new PatientMedicalHistory { PatientMedHistoryId = "PMH-001", PatientId = "PAT-001", ItemId = "MHI-002", ConditionName = "Heart Disease", OnsetMonth = 1, OnsetYear = 2020, Notes = "", RecordedByUserId = "USR-001", RecordedAt = D("2024-01-15T10:00:00") });
            PatientMedicalHistory.Add(new PatientMedicalHistory { PatientMedHistoryId = "PMH-002", PatientId = "PAT-003", ItemId = "MHI-001", ConditionName = "Diabetes", OnsetMonth = 3, OnsetYear = 2018, Notes = "Type 2", RecordedByUserId = "USR-001", RecordedAt = D("2024-06-10T10:00:00") });
            PatientMedicalHistory.Add(new PatientMedicalHistory { PatientMedHistoryId = "PMH-003", PatientId = "PAT-003", ItemId = "MHI-002", ConditionName = "Heart Disease", OnsetYear = 2015, Notes = "", RecordedByUserId = "USR-001", RecordedAt = D("2024-06-10T10:00:00") });
            PatientMedicalHistory.Add(new PatientMedicalHistory { PatientMedHistoryId = "PMH-004", PatientId = "PAT-003", ItemId = "MHI-003", ConditionName = "Hypertension", OnsetMonth = 6, OnsetYear = 2019, Notes = "Triple vessel disease", RecordedByUserId = "USR-001", RecordedAt = D("2024-06-10T10:00:00") });

            string[] events = { "CABG", "Angioplasty / Stent", "Appendectomy", "Cholecystectomy", "Joint Replacement", "C-Section", "Hysterectomy", "Stroke", "Myocardial Infarction", "Fracture (specify)" };
            for (int i = 0; i < events.Length; i++)
                MedicalEventItems.Add(new MedicalEventItem { EventId = "MEV-" + (i + 1).ToString("000"), HospitalId = "HOSP-001", EventName = events[i], SortOrder = i + 1, IsActive = true });
            PatientMedicalEvents.Add(new PatientMedicalEvent { PatientMedEventId = "PME-001", PatientId = "PAT-003", EventId = "MEV-001", EventName = "CABG", EventMonth = 6, EventYear = 2020, Notes = "Triple vessel CABG", RecordedByUserId = "USR-001", RecordedAt = D("2024-06-10T10:00:00") });

            FamilyHistory.Add(new PatientFamilyHistory { FamilyHistoryId = "PFH-001", PatientId = "PAT-001", RelationshipType = "FATHER", ConditionName = "Hypertension", Notes = "", RecordedAt = D("2024-01-15T10:00:00") });
            FamilyHistory.Add(new PatientFamilyHistory { FamilyHistoryId = "PFH-002", PatientId = "PAT-003", RelationshipType = "FATHER", ConditionName = "CAD", Notes = "MI at age 55", RecordedAt = D("2024-06-10T10:00:00") });
            FamilyHistory.Add(new PatientFamilyHistory { FamilyHistoryId = "PFH-003", PatientId = "PAT-003", RelationshipType = "MOTHER", ConditionName = "Diabetes", Notes = "", RecordedAt = D("2024-06-10T10:00:00") });

            Payers.AddRange(new[]
            {
                new Payer { PayerId = "PAYER-001", Name = "Star Health Insurance", Category = "PRIVATE", Country = "IN", IsActive = true },
                new Payer { PayerId = "PAYER-002", Name = "HDFC Ergo", Category = "PRIVATE", Country = "IN", IsActive = true },
                new Payer { PayerId = "PAYER-003", Name = "ICICI Lombard", Category = "PRIVATE", Country = "IN", IsActive = true },
                new Payer { PayerId = "PAYER-004", Name = "CGHS (Central Govt)", Category = "GOVT_SCHEME", Country = "IN", IsActive = true },
                new Payer { PayerId = "PAYER-005", Name = "Dr. YSR Aarogyasri", Category = "GOVT_SCHEME", Country = "IN", IsActive = true },
                new Payer { PayerId = "PAYER-006", Name = "Sagicor Life", Category = "PRIVATE", Country = "JM", IsActive = true },
                new Payer { PayerId = "PAYER-007", Name = "Guardian Life", Category = "PRIVATE", Country = "JM", IsActive = true },
                new Payer { PayerId = "PAYER-008", Name = "Medecus", Category = "PRIVATE", Country = "JM", IsActive = true },
                new Payer { PayerId = "PAYER-009", Name = "Canopy Insurance", Category = "PRIVATE", Country = "JM", IsActive = true },
                new Payer { PayerId = "PAYER-010", Name = "National Health Fund (NHF)", Category = "GOVT_SCHEME", Country = "JM", IsActive = true },
            });
            /* Sunrise is a Jamaican clinic: private insurance only, private + NHF, NHF only — everyone else is Self-Pay */
            Coverages.Add(new PatientCoverage { CoverageId = "COV-001", HospitalId = "HOSP-001", PatientId = "PAT-001", CoverageType = "PRIVATE_INSURANCE", PayerId = "PAYER-006", PlanName = "Executive Plus", MemberId = "SAG543210", PolicyNumber = "P123456", GroupNumber = "GR7788", PolicyHolderName = "John Doe", Relationship = "Self", ClaimMode = "CASHLESS", EffectiveDate = D("2026-01-01"), ExpiryDate = D("2026-12-31"), Status = "ACTIVE", GovtCardNumber = "", Priority = 1, IsActive = true });
            Coverages.Add(new PatientCoverage { CoverageId = "COV-002", HospitalId = "HOSP-001", PatientId = "PAT-003", CoverageType = "PRIVATE_INSURANCE", PayerId = "PAYER-007", PlanName = "Health Starter", MemberId = "GDN778812", PolicyNumber = "G556677", GroupNumber = "GRP-4521", PolicyHolderName = "Ramesh Kumar", Relationship = "Self", ClaimMode = "CASHLESS", EffectiveDate = D("2026-01-01"), ExpiryDate = D("2026-12-31"), Status = "ACTIVE", GovtCardNumber = "", Priority = 1, IsActive = true });
            Coverages.Add(new PatientCoverage { CoverageId = "COV-003", HospitalId = "HOSP-001", PatientId = "PAT-003", CoverageType = "GOVT_SCHEME", PayerId = "PAYER-010", PlanName = "NHF", PolicyHolderName = "Ramesh Kumar", Relationship = "Self", ClaimMode = "", EffectiveDate = D("2025-06-01"), ExpiryDate = D("2027-05-31"), Status = "ACTIVE", GovtCardNumber = "NHF-99887766", Priority = 2, IsActive = true });
            Coverages.Add(new PatientCoverage { CoverageId = "COV-004", HospitalId = "HOSP-001", PatientId = "PAT-002", CoverageType = "GOVT_SCHEME", PayerId = "PAYER-010", PlanName = "NHF", PolicyHolderName = "Priya Reddy", Relationship = "Self", ClaimMode = "", EffectiveDate = D("2025-01-01"), ExpiryDate = D("2027-12-31"), Status = "ACTIVE", GovtCardNumber = "NHF-11223344", Priority = 1, IsActive = true });
        }

        private static void SeedClinicalCatalogs()
        {
            var vitals = new[]
            {
                new[] { "TEMP", "Temp", "F" }, new[] { "BP_SYS", "BP systolic", "mmHg" }, new[] { "BP_DIA", "BP diastolic", "mmHg" },
                new[] { "PULSE", "Pulse", "bpm" }, new[] { "SPO2", "SpO2", "%" }, new[] { "HEIGHT", "Height", "cm" }, new[] { "WEIGHT", "Weight", "kg" },
                new[] { "RESP_RATE", "Respiratory Rate", "breaths/min" }, new[] { "PAIN_SCALE", "Pain Scale", "0-10" },
                new[] { "WAIST_CIRC", "Waist Circumference", "cm" }, new[] { "HEAD_CIRC", "Head Circumference", "cm" },
            };
            for (int i = 0; i < vitals.Length; i++)
                VitalsConfig.Add(new VitalConfig
                {
                    VitalDefinitionId = "VTC-" + (i + 1).ToString("000"), HospitalId = "HOSP-001", VitalCode = vitals[i][0], DisplayLabel = vitals[i][1],
                    AllowedUnits = vitals[i][2], IsEnabled = i < 9, SortOrder = i + 1, IsPediatricOnly = vitals[i][0] == "HEAD_CIRC"
                });

            var symptoms = new[]
            {
                new[] { "FEVER", "Fever", "Y" }, new[] { "COUGH", "Cough", "Y" }, new[] { "HEADACHE", "Headache", "Y" }, new[] { "RIGORS", "Rigors / chills", "Y" },
                new[] { "NAUSEA", "Nausea", "Y" }, new[] { "FATIGUE", "Fatigue", "Y" }, new[] { "VOMITING", "Vomiting", "Y" }, new[] { "CHEST_PAIN", "Chest pain", "Y" },
                new[] { "BREATHLESSNESS", "Breathlessness", "N" }, new[] { "DIARRHEA", "Diarrhea", "N" }, new[] { "ABDOMINAL_PAIN", "Abdominal pain", "N" },
                new[] { "JOINT_PAIN", "Joint pain", "N" }, new[] { "BACK_PAIN", "Back pain", "N" }, new[] { "DIZZINESS", "Dizziness", "N" },
                new[] { "WEIGHT_LOSS", "Weight loss", "N" }, new[] { "SKIN_RASH", "Skin rash", "N" }, new[] { "SWELLING", "Swelling", "N" },
                new[] { "BURNING_URINE", "Burning urination", "N" },
            };
            for (int i = 0; i < symptoms.Length; i++)
                SymptomCatalog.Add(new SymptomCatalogItem { SymptomCatalogId = "SYM-" + (i + 1).ToString("000"), HospitalId = "HOSP-001", SymptomCode = symptoms[i][0], DisplayLabel = symptoms[i][1], IsQuickPick = symptoms[i][2] == "Y", IsActive = true });

            Icd10Catalog.AddRange(new[]
            {
                new Icd10Entry { Icd10CatalogId = "ICD-001", Icd10Code = "R50.9", Description = "Fever, unspecified", SynonymTerms = "fever,pyrexia,high temperature" },
                new Icd10Entry { Icd10CatalogId = "ICD-002", Icd10Code = "J00", Description = "Acute nasopharyngitis (common cold)", SynonymTerms = "cold,cough,runny nose,common cold" },
                new Icd10Entry { Icd10CatalogId = "ICD-003", Icd10Code = "I10", Description = "Essential (primary) hypertension", SynonymTerms = "high bp,hypertension,bp" },
                new Icd10Entry { Icd10CatalogId = "ICD-004", Icd10Code = "E11.9", Description = "Type 2 diabetes mellitus without complications", SynonymTerms = "diabetes,sugar,high sugar,type 2" },
                new Icd10Entry { Icd10CatalogId = "ICD-005", Icd10Code = "A90", Description = "Dengue fever", SynonymTerms = "dengue" },
                new Icd10Entry { Icd10CatalogId = "ICD-006", Icd10Code = "G43.9", Description = "Migraine, unspecified", SynonymTerms = "migraine,headache" },
            });

            Conditions.AddRange(new[]
            {
                new ClinicalCondition { ConditionId = "COND-001", HospitalId = "HOSP-001", Name = "Hypertension", Icd10Prefixes = "I10,I11,I12,I13,I15", IsActive = true },
                new ClinicalCondition { ConditionId = "COND-002", HospitalId = "HOSP-001", Name = "Diabetes Mellitus", Icd10Prefixes = "E10,E11,E13,E14", IsActive = true },
                new ClinicalCondition { ConditionId = "COND-003", HospitalId = "HOSP-001", Name = "Chest Pain / Cardiac", Icd10Prefixes = "R07,I20,I21,I25", IsActive = true },
                new ClinicalCondition { ConditionId = "COND-004", HospitalId = "HOSP-001", Name = "Fever / Infection", Icd10Prefixes = "R50,A90,J00,J06", IsActive = true },
                new ClinicalCondition { ConditionId = "COND-005", HospitalId = "HOSP-001", Name = "Migraine / Headache", Icd10Prefixes = "G43,G44,R51", IsActive = true },
            });

            Formulary.AddRange(new[]
            {
                NewDrug("DRUG-001", "Crocin", "Paracetamol", "Paracetamol 500mg", "Analgesic / Antipyretic", "Paracetamol", "Tablet", "Oral", "500mg", "1-1-1", 3, "After food"),
                NewDrug("DRUG-002", "Augmentin", "Amoxicillin + Clavulanate", "Amoxicillin 500mg + Clavulanate 125mg", "Antibiotic", "Penicillin combination", "Tablet", "Oral", "625mg", "1-0-1", 5, "After food"),
                NewDrug("DRUG-003", "Amlodac", "Amlodipine", "Amlodipine 5mg", "Antihypertensive", "Calcium channel blocker", "Tablet", "Oral", "5mg", "1-0-0", 30, "Morning, before food"),
                NewDrug("DRUG-004", "Glycomet", "Metformin", "Metformin 500mg", "Antidiabetic", "Biguanide", "Tablet", "Oral", "500mg", "1-0-1", 30, "After food"),
                NewDrug("DRUG-005", "Cetzine", "Cetirizine", "Cetirizine 10mg", "Antihistamine", "Cetirizine", "Tablet", "Oral", "10mg", "0-0-1", 5, "At night"),
                NewDrug("DRUG-006", "Omez", "Omeprazole", "Omeprazole 20mg", "Proton Pump Inhibitor", "PPI", "Capsule", "Oral", "20mg", "1-0-0", 14, "Before breakfast, empty stomach"),
                NewDrug("DRUG-007", "Ascoril-D", "Dextromethorphan + Phenylephrine + Chlorpheniramine", "DM+PE+CPM", "Cough Suppressant", "Antitussive combination", "Syrup", "Oral", "5ml", "1-1-1", 5, "After food"),
                NewDrug("DRUG-008", "Otrivin", "Xylometazoline", "Xylometazoline 0.1%", "Nasal Decongestant", "Sympathomimetic", "Nasal Drops", "Nasal", "2 drops each nostril", "1-0-1", 5, "Not more than 5 days"),
                NewDrug("DRUG-009", "Monocef", "Ceftriaxone", "Ceftriaxone 1g", "Antibiotic (Cephalosporin)", "3rd gen Cephalosporin", "Injection", "IV/IM", "1g", "1-0-1", 5, "To be administered by healthcare professional"),
                NewDrug("DRUG-010", "Candid Mouth Paint", "Clotrimazole", "Clotrimazole 1%", "Antifungal", "Azole antifungal", "Topical Paint", "Topical", "Apply thin layer", "1-1-1", 7, "Apply to affected area"),
            });

            var tests = new[]
            {
                new[] { "Complete Blood Count (CBC)", "LAB", "Hematology", "SPEC-GEN,SPEC-CARD,SPEC-PED" }, new[] { "HbA1c", "LAB", "Biochemistry", "SPEC-GEN" },
                new[] { "Fasting Blood Sugar (FBS)", "LAB", "Biochemistry", "SPEC-GEN" }, new[] { "Post Prandial Blood Sugar (PPBS)", "LAB", "Biochemistry", "SPEC-GEN" },
                new[] { "Lipid Profile", "LAB", "Biochemistry", "SPEC-GEN,SPEC-CARD" }, new[] { "Liver Function Test (LFT)", "LAB", "Biochemistry", "SPEC-GEN" },
                new[] { "Renal Function Test (RFT/KFT)", "LAB", "Biochemistry", "SPEC-GEN" }, new[] { "Thyroid Profile (TSH, T3, T4)", "LAB", "Endocrinology", "SPEC-GEN" },
                new[] { "Urine Routine & Microscopy", "LAB", "Microbiology", "SPEC-GEN" }, new[] { "Dengue NS1 Antigen", "LAB", "Serology", "SPEC-GEN" },
                new[] { "Malarial Smear (MP/MF)", "LAB", "Parasitology", "SPEC-GEN" }, new[] { "ECG (12-lead)", "LAB", "Cardiology", "SPEC-GEN,SPEC-CARD" },
                new[] { "Chest X-ray (PA view)", "IMAGING", "Radiology", "SPEC-GEN,SPEC-CARD" }, new[] { "Ultrasound Abdomen", "IMAGING", "Radiology", "SPEC-GEN" },
                new[] { "Echocardiography (2D Echo)", "IMAGING", "Cardiology", "SPEC-CARD" }, new[] { "ESR", "LAB", "Hematology", "SPEC-GEN" },
                new[] { "CRP (C-Reactive Protein)", "LAB", "Biochemistry", "SPEC-GEN" }, new[] { "Blood Culture & Sensitivity", "LAB", "Microbiology", "SPEC-GEN" },
            };
            for (int i = 0; i < tests.Length; i++)
                LabTests.Add(new LabTest { TestId = "LT-" + (i + 1).ToString("000"), HospitalId = "HOSP-001", TestName = tests[i][0], TestType = tests[i][1], Category = tests[i][2], SpecialtyIds = tests[i][3].Split(',').ToList(), IsActive = true });
            foreach (var t in new[] { "LT-001", "LT-003", "LT-005", "LT-010", "LT-013" })
                LabFavorites.Add(new DoctorLabFavorite { FavoriteId = NextId("DLF"), DoctorId = "USR-001", TestId = t });

            Phrases.AddRange(new[]
            {
                NewPhrase("PH-001", "SPECIALTY", "SPEC-GEN", null, null, "S", "/fever", "Fever since ___ days, moderate grade, associated with chills and body ache.", 1),
                NewPhrase("PH-002", "SPECIALTY", "SPEC-GEN", null, null, "S", "/norelief", "No relief with previous medication.", 2),
                NewPhrase("PH-003", "SPECIALTY", "SPEC-GEN", null, null, "S", "/cough", "Dry cough since ___ days, non-productive, no hemoptysis.", 3),
                NewPhrase("PH-004", "SPECIALTY", "SPEC-GEN", null, null, "S", "/appetite", "Appetite reduced. No significant weight loss.", 4),
                NewPhrase("PH-005", "SPECIALTY", "SPEC-GEN", null, null, "S", "/denies", "Denies vomiting, loose stools, or urinary complaints.", 5),
                NewPhrase("PH-010", "SPECIALTY", "SPEC-GEN", null, null, "O", "/gen", "Patient is conscious, oriented, afebrile at time of examination.", 1),
                NewPhrase("PH-011", "SPECIALTY", "SPEC-GEN", null, null, "O", "/cvs", "CVS: S1S2 normal, no murmurs, no gallop.", 2),
                NewPhrase("PH-012", "SPECIALTY", "SPEC-GEN", null, null, "O", "/rs", "RS: Bilateral air entry equal, no added sounds.", 3),
                NewPhrase("PH-013", "SPECIALTY", "SPEC-GEN", null, null, "O", "/abd", "Abdomen: Soft, non-tender, no organomegaly.", 4),
                NewPhrase("PH-014", "SPECIALTY", "SPEC-GEN", null, null, "O", "/nad", "No abnormality detected on general examination.", 5),
                NewPhrase("PH-020", "SPECIALTY", "SPEC-GEN", null, null, "P", "/rest", "Advised rest, adequate hydration, and light diet.", 1),
                NewPhrase("PH-021", "SPECIALTY", "SPEC-GEN", null, null, "P", "/review", "Review after ___ days or earlier if symptoms worsen.", 2),
                NewPhrase("PH-022", "SPECIALTY", "SPEC-GEN", null, null, "P", "/lifestyle", "Advised lifestyle modification: salt restriction, regular exercise, weight management.", 3),
                NewPhrase("PH-023", "SPECIALTY", "SPEC-GEN", null, null, "P", "/emergency", "Advised to visit ER immediately if high-grade fever, breathlessness, or chest pain.", 4),
                NewPhrase("PH-101", "GLOBAL", null, null, null, "ANY", "/followup", "Advised to review after 2 weeks or earlier if symptoms worsen.", 1),
                NewPhrase("PH-102", "GLOBAL", null, null, "COND-001", "ANY", "/htn", "Blood pressure elevated. Counselled on salt restriction and adherence.", 2),
                NewPhrase("PH-103", "GLOBAL", null, null, "COND-002", "ANY", "/dm", "Reinforced diabetic diet, foot care, and regular glucose monitoring.", 3),
                NewPhrase("PH-104", "DOCTOR", null, "USR-010", "COND-003", "ANY", "/acs", "ACS ruled out by serial ECG and troponin.", 4),
                NewPhrase("PH-105", "DOCTOR", null, "USR-010", null, "O", "/cvs", "S1S2 heard, no murmurs. No pedal oedema. JVP not raised.", 5),
            });
        }

        private static Drug NewDrug(string id, string brand, string generic, string comp, string tclass, string gclass, string form, string route,
            string dosage, string freq, int days, string instr)
        {
            return new Drug
            {
                DrugId = id, BrandName = brand, GenericName = generic, Composition = comp, TherapeuticClass = tclass, GenericClass = gclass,
                DrugForm = form, DefaultRoute = route, DefaultDosage = dosage, DefaultFrequency = freq, DefaultDurationDays = days,
                DefaultInstructions = instr, IsActive = true
            };
        }

        private static Phrase NewPhrase(string id, string scope, string spec, string doctor, string condition, string section, string trigger, string text, int order)
        {
            return new Phrase
            {
                PhraseId = id, Scope = scope, SpecialtyId = spec, DoctorId = doctor, ConditionId = condition, Section = section,
                TriggerKeyword = trigger, PhraseText = text, SortOrder = order, IsActive = true
            };
        }

        private static void SeedScheduling()
        {
            string[] monFri = { "MON", "TUE", "WED", "THU", "FRI" };
            string[] monSat = { "MON", "TUE", "WED", "THU", "FRI", "SAT" };
            int n = 0;
            Action<string, string, string[], string, string> add = (doctorId, hid, days, start, end) =>
            {
                foreach (var d in days)
                    SlotConfig.Add(new DoctorSlotConfig { SlotConfigId = "DSC-" + (++n).ToString("000"), DoctorId = doctorId, HospitalId = hid, DayOfWeek = d, StartTime = start, EndTime = end, IsActive = true });
            };
            add("USR-001", "HOSP-001", monSat, "09:00", "13:00"); add("USR-001", "HOSP-001", new[] { "MON", "WED", "FRI" }, "17:00", "19:00");
            add("USR-008", "HOSP-001", monFri, "09:00", "13:00"); add("USR-008", "HOSP-001", monFri, "14:00", "17:00");
            add("USR-009", "HOSP-001", monSat, "10:00", "14:00"); add("USR-009", "HOSP-001", new[] { "TUE", "THU" }, "16:00", "19:00");
            add("USR-010", "HOSP-001", new[] { "TUE", "THU", "SAT" }, "10:00", "14:00");
            add("USR-011", "HOSP-001", new[] { "MON", "WED", "FRI" }, "15:00", "18:00");
            add("USR-003", "HOSP-002", monFri, "08:00", "12:00"); add("USR-003", "HOSP-002", monFri, "13:00", "16:00");

            Leave.Add(new DoctorLeave { LeaveId = "LV-001", HospitalId = "HOSP-001", DoctorId = "USR-010", LeaveDate = TimeZoneService.Today("HOSP-001").AddDays(7), Reason = "Cardiology conference" });

            int t = 0;
            foreach (var hid in new[] { "HOSP-001", "HOSP-002" })
            {
                var types = new[]
                {
                    new AppointmentType { Code = "NEW", Name = "New Consultation", DurationMinutes = null, Color = "#0f766e", IsChargeable = true, FeeCategory = "NEW" },
                    new AppointmentType { Code = "FOLLOWUP", Name = "Follow-Up", DurationMinutes = null, Color = "#4f46e5", IsChargeable = true, FeeCategory = "FOLLOWUP" },
                    new AppointmentType { Code = "PROC", Name = "Procedure / Minor OT", DurationMinutes = 45, Color = "#b45309", IsChargeable = true, FeeCategory = "NEW" },
                    new AppointmentType { Code = "TELE", Name = "Teleconsultation", DurationMinutes = 20, Color = "#15803d", IsChargeable = true, FeeCategory = "FOLLOWUP" },
                };
                for (int i = 0; i < types.Length; i++)
                {
                    types[i].AppointmentTypeId = "APT-TYPE-" + (++t).ToString("000");
                    types[i].HospitalId = hid;
                    types[i].SortOrder = i + 1;
                    types[i].IsActive = true;
                    AppointmentTypes.Add(types[i]);
                }
            }

            foreach (var ds in new[] { "USR-001:SPEC-GEN", "USR-003:SPEC-GEN", "USR-008:SPEC-GEN", "USR-009:SPEC-GEN", "USR-010:SPEC-CARD", "USR-011:SPEC-PED" })
            {
                var p = ds.Split(':');
                DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = p[0], SpecialtyId = p[1] });
            }

            int q = 0;
            foreach (var hid in new[] { "HOSP-001", "HOSP-002" })
            {
                var questions = new[]
                {
                    new[] { "Reason for today’s visit", "TEXT", "" }, new[] { "Known allergies", "TEXT", "" }, new[] { "Current medications", "TEXT", "" },
                    new[] { "Do you smoke?", "CHOICE", "No,Occasionally,Daily" }, new[] { "Pain level (0–10)", "NUMBER", "" },
                };
                for (int i = 0; i < questions.Length; i++)
                    IntakeQuestions.Add(new IntakeQuestion { QuestionId = "IQ-" + (++q).ToString("000"), HospitalId = hid, Label = questions[i][0], QuestionType = questions[i][1], Options = questions[i][2], SortOrder = i + 1, IsActive = true });
            }
            ConsentForms.Add(new ConsentForm { ConsentFormId = "CF-001", HospitalId = "HOSP-001", Title = "Consent to examination & treatment", Version = 1, IsActive = true, BodyText = ConsentText });
            ConsentForms.Add(new ConsentForm { ConsentFormId = "CF-002", HospitalId = "HOSP-002", Title = "Consent to examination & treatment", Version = 1, IsActive = true, BodyText = ConsentText });

            // Times below are the clinic's wall clock; converted to UTC + TimeZoneId after the list.
            var today = TimeZoneService.Today("HOSP-001");
            Appointments.AddRange(new[]
            {
                new Appointment { AppointmentId = "APT-001", HospitalId = "HOSP-001", PatientId = "PAT-001", DoctorId = "USR-001", SlotStart = today.AddHours(9), SlotEnd = today.AddHours(9).AddMinutes(15), Type = "FOLLOWUP", Status = "COMPLETED", Notes = "" },
                new Appointment { AppointmentId = "APT-002", HospitalId = "HOSP-001", PatientId = "PAT-002", DoctorId = "USR-001", SlotStart = today.AddHours(9).AddMinutes(15), SlotEnd = today.AddHours(9).AddMinutes(30), Type = "NEW", Status = "IN_CONSULT", Notes = "" },
                new Appointment { AppointmentId = "APT-003", HospitalId = "HOSP-001", PatientId = "PAT-003", DoctorId = "USR-001", SlotStart = today.AddHours(9).AddMinutes(30), SlotEnd = today.AddHours(9).AddMinutes(45), Type = "FOLLOWUP", Status = "CHECKED_IN", Notes = "" },
                new Appointment { AppointmentId = "APT-004", HospitalId = "HOSP-001", PatientId = "PAT-004", DoctorId = "USR-001", SlotStart = today.AddHours(10), SlotEnd = today.AddHours(10).AddMinutes(15), Type = "NEW", Status = "BOOKED", Notes = "" },
                new Appointment { AppointmentId = "APT-005", HospitalId = "HOSP-001", PatientId = "PAT-005", DoctorId = "USR-001", SlotStart = today.AddHours(10).AddMinutes(15), SlotEnd = today.AddHours(10).AddMinutes(30), Type = "FOLLOWUP", Status = "BOOKED", Notes = "" },
            });
            foreach (var a in Appointments)
            {
                a.TimeZoneId = TimeZoneService.HospitalZoneId(a.HospitalId);
                a.AppointmentDate = a.SlotStart.Date;
                a.SlotStart = TimeZoneService.ToUtc(a.SlotStart, a.TimeZoneId);
                a.SlotEnd = TimeZoneService.ToUtc(a.SlotEnd, a.TimeZoneId);
            }
        }

        private const string ConsentText = "I consent to examination, investigations and treatment advised by the treating physician, and acknowledge that no guarantee of outcome has been given. I consent to the processing of my health data for care and billing.";

        private static void SeedBilling()
        {
            var fees = new[]
            {
                new object[] { "USR-001", 500m, 300m }, new object[] { "USR-008", 400m, 250m }, new object[] { "USR-009", 400m, 250m },
                new object[] { "USR-010", 800m, 500m }, new object[] { "USR-011", 600m, 400m },
            };
            int f = 0;
            foreach (var row in fees)
            {
                Fees.Add(new DoctorFee { FeeScheduleId = "FEE-" + (++f).ToString("000"), HospitalId = "HOSP-001", DoctorId = (string)row[0], ConsultationType = "NEW", FeeAmount = (decimal)row[1], Currency = "INR", EffectiveFrom = D("2026-01-01") });
                Fees.Add(new DoctorFee { FeeScheduleId = "FEE-" + (++f).ToString("000"), HospitalId = "HOSP-001", DoctorId = (string)row[0], ConsultationType = "FOLLOWUP", FeeAmount = (decimal)row[2], Currency = "INR", EffectiveFrom = D("2026-01-01") });
            }

            PaymentMethods.AddRange(new[]
            {
                new PaymentMethod { PaymentMethodId = "PM-001", HospitalId = "HOSP-001", MethodName = "CASH", IsActive = true },
                new PaymentMethod { PaymentMethodId = "PM-002", HospitalId = "HOSP-001", MethodName = "CREDIT_CARD", IsActive = true },
                new PaymentMethod { PaymentMethodId = "PM-003", HospitalId = "HOSP-001", MethodName = "DEBIT_CARD", IsActive = true },
                new PaymentMethod { PaymentMethodId = "PM-004", HospitalId = "HOSP-002", MethodName = "CASH", IsActive = true },
                new PaymentMethod { PaymentMethodId = "PM-005", HospitalId = "HOSP-002", MethodName = "CREDIT_CARD", IsActive = true },
                new PaymentMethod { PaymentMethodId = "PM-006", HospitalId = "HOSP-002", MethodName = "INSURANCE_DIRECT", IsActive = true },
            });

            var services = new[]
            {
                /* No lab / imaging charges: the clinic has no in-house diagnostics (tests are done and paid for outside) */
                new object[] { "CON-NEW", "Consultation – New Patient", "CONSULTATION", 500m }, new object[] { "CON-FUP", "Consultation – Follow-Up", "CONSULTATION", 300m },
                new object[] { "PRC-DRESS", "Wound Dressing", "PROCEDURE", 150m },
                new object[] { "PRC-INJ", "Injection Administration", "INJECTION", 100m }, new object[] { "PRC-NEB", "Nebulization", "PROCEDURE", 200m },
            };
            for (int i = 0; i < services.Length; i++)
                ServiceCharges.Add(new ServiceCharge
                {
                    ServiceChargeId = "SVC-" + (i + 1).ToString("000"), HospitalId = "HOSP-001", ServiceCode = (string)services[i][0],
                    ServiceName = (string)services[i][1], Category = (string)services[i][2], DefaultAmount = (decimal)services[i][3], TaxPercent = 0, IsActive = true
                });
        }

        /// <summary>
        /// Clinic stock for HOSP-001, each item linked to the services it is used with (SVC-003 Wound Dressing,
        /// SVC-004 Injection Administration, SVC-005 Nebulization). Dates are relative to today so the
        /// low-stock / expiring / expired flags always show.
        /// </summary>
        private static void SeedInventory()
        {
            var items = new[]
            {
                new object[] { "INJ-CEF1G", "Monocef 1g (Ceftriaxone)", "INJECTION", "vial", 120m, 12m, 10, "SVC-004" },
                new object[] { "INJ-DICLO", "Diclofenac 75mg/3ml", "INJECTION", "ampoule", 25m, 12m, 10, "SVC-004" },
                new object[] { "INJ-ONDAN", "Ondansetron 4mg/2ml", "INJECTION", "ampoule", 30m, 12m, 10, "SVC-004" },
                new object[] { "INJ-DEXA", "Dexamethasone 8mg/2ml", "INJECTION", "vial", 20m, 12m, 5, "SVC-004" },
                new object[] { "INJ-TT", "Tetanus Toxoid 0.5ml", "INJECTION", "vial", 45m, 5m, 5, "SVC-004" },
                new object[] { "MED-SALB", "Salbutamol Respules 2.5mg", "MEDICATION", "respule", 18m, 12m, 10, "SVC-005" },
                new object[] { "CON-SYR5", "Disposable Syringe 5ml", "CONSUMABLE", "piece", 8m, 12m, 50, "SVC-004" },
                new object[] { "CON-DRESS", "Sterile Dressing Pad 10x10cm", "CONSUMABLE", "piece", 15m, 12m, 20, "SVC-003" },
            };
            foreach (var r in items)
                InventoryItems.Add(new InventoryItem
                {
                    InventoryItemId = NextId("ITM"), HospitalId = "HOSP-001", ItemCode = (string)r[0], ItemName = (string)r[1], Category = (string)r[2],
                    Unit = (string)r[3], UnitPrice = (decimal)r[4], TaxPercent = (decimal)r[5], ReorderLevel = (int)r[6], IsActive = true,
                    ServiceChargeIds = new List<string> { (string)r[7] }
                });

            Action<int, string, int, int> batch = (itemIndex, batchNo, expiresInDays, qty) =>
            {
                var b = new StockBatch
                {
                    StockBatchId = NextId("BAT"), HospitalId = "HOSP-001", InventoryItemId = InventoryItems[itemIndex].InventoryItemId,
                    BatchNo = batchNo, ExpiryDate = TimeZoneService.Today("HOSP-001").AddDays(expiresInDays), QtyReceived = qty, QtyOnHand = qty,
                    UnitCost = InventoryItems[itemIndex].UnitPrice * 0.6m, Supplier = "MedLine Distributors", ReceivedByUserId = "USR-004",
                    ReceivedAt = DateTime.UtcNow.AddDays(-20)
                };
                StockBatches.Add(b);
                StockMovements.Add(new StockMovement
                {
                    StockMovementId = NextId("STM"), HospitalId = b.HospitalId, InventoryItemId = b.InventoryItemId, StockBatchId = b.StockBatchId,
                    MovementType = "RECEIPT", Qty = qty, Reason = "Received from " + b.Supplier, UserId = b.ReceivedByUserId, CreatedAt = b.ReceivedAt
                });
            };
            batch(0, "CF24A11", 20, 6);     // expiring soon, used first
            batch(0, "CF25B03", 300, 30);
            batch(1, "DC25C17", 240, 40);
            batch(2, "ON25A02", 180, 4);    // below reorder level
            batch(3, "DX24K09", -10, 8);    // expired, awaiting write-off
            batch(3, "DX25D21", 400, 25);
            batch(4, "TT25B14", 150, 12);
            batch(5, "SB25C30", 200, 60);
            batch(6, "SY25A01", 700, 400);
            batch(7, "DP25B08", 500, 80);
        }

        private static void SeedMessaging()
        {
            WhatsAppConfig = new ChannelConfig { Provider = "META", ApiKey = "", PhoneNumberId = "", BusinessAccountId = "", Enabled = false };
            EmailConfig = new ChannelConfig { Provider = "SENDGRID", SmtpHost = "", SmtpPort = 587, ApiKey = "", FromEmail = "", FromName = "", Enabled = false };
            MessageTemplates.AddRange(new[]
            {
                new MessageTemplate { TemplateId = "TPL-001", Name = "Appointment Reminder", Trigger = "APPOINTMENT_REMINDER", Channel = "BOTH", IsEnabled = true, Text = "Dear {PatientName}, this is a reminder for your appointment with {DoctorName} at {Time} today at {HospitalName}. Please arrive 10 minutes early." },
                new MessageTemplate { TemplateId = "TPL-002", Name = "No-Show Follow-Up", Trigger = "NO_SHOW", Channel = "BOTH", IsEnabled = true, Text = "Dear {PatientName}, we noticed you missed your appointment on {Date} with {DoctorName} at {HospitalName}. Please call {Phone} to reschedule." },
                new MessageTemplate { TemplateId = "TPL-003", Name = "Lab Results Ready", Trigger = "LAB_RESULTS_READY", Channel = "BOTH", IsEnabled = true, Text = "Dear {PatientName}, your lab results for {TestName} are ready. Please visit {HospitalName} or contact {DoctorName} for review." },
                new MessageTemplate { TemplateId = "TPL-004", Name = "Follow-Up Reminder", Trigger = "FOLLOWUP_REMINDER", Channel = "BOTH", IsEnabled = true, Text = "Dear {PatientName}, your follow-up with {DoctorName} is tomorrow at {HospitalName}. Please confirm." },
                new MessageTemplate { TemplateId = "TPL-005", Name = "Prescription Ready", Trigger = "PRESCRIPTION_READY", Channel = "WHATSAPP", IsEnabled = true, Text = "Dear {PatientName}, your prescription from {DoctorName} at {HospitalName} is ready." },
            });
        }
    }
}
