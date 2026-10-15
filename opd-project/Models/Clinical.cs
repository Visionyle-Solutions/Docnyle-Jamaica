using System;
using System.Collections.Generic;

namespace OpdProject.Models
{
    /// <summary>Consultations table. Status: IN_PROGRESS → LOCKED (signed off).</summary>
    public class Consultation
    {
        public string ConsultationId { get; set; }
        public string HospitalId { get; set; }
        public string PatientId { get; set; }
        public string DoctorId { get; set; }
        public string AppointmentId { get; set; }
        public DateTime VisitDate { get; set; }
        public string Status { get; set; }
        public bool IsLocked { get; set; }
        public string SubjectiveText { get; set; }
        public string ObjectiveText { get; set; }
        public string PlanText { get; set; }
        public string SubjectivePersonal { get; set; }
        public string ObjectivePersonal { get; set; }
        public string PlanPersonal { get; set; }
        public DateTime? FollowupDate { get; set; }
        public string FollowupNotes { get; set; }
        public List<string> SkippedSteps { get; set; }

        public Consultation() { SkippedSteps = new List<string>(); }
    }

    public class VitalConfig
    {
        public string VitalDefinitionId { get; set; }
        public string HospitalId { get; set; }
        public string VitalCode { get; set; }
        public string DisplayLabel { get; set; }
        public string AllowedUnits { get; set; }
        public bool IsEnabled { get; set; }
        public int SortOrder { get; set; }
        public bool IsPediatricOnly { get; set; }
        /// <summary>Added by the hospital in Settings (built-in vitals have fixed ranges in ConsultationService.VitalRanges).</summary>
        public bool IsCustom { get; set; }
        /// <summary>Valid range for a custom vital (either bound optional).</summary>
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
    }

    /// <summary>ConsultationVitals — one row per consultation.</summary>
    public class ConsultationVitals
    {
        public string VitalId { get; set; }
        public string ConsultationId { get; set; }
        public double? TempValue { get; set; }
        public double? BpSystolic { get; set; }
        public double? BpDiastolic { get; set; }
        public double? PulseBpm { get; set; }
        public double? SpO2 { get; set; }
        public double? HeightCm { get; set; }
        public double? WeightKg { get; set; }
        public double? RespRate { get; set; }
        public double? PainScale { get; set; }
        public double? WaistCircCm { get; set; }
        public double? HeadCircCm { get; set; }
        public double? Bmi { get; set; }
        /// <summary>Values of the hospital's custom vitals, keyed by vital code.</summary>
        public Dictionary<string, double?> CustomValues { get; set; } = new Dictionary<string, double?>();
        public DateTime RecordedAt { get; set; }
        public string RecordedBy { get; set; }
    }

    public class SymptomCatalogItem
    {
        public string SymptomCatalogId { get; set; }
        public string HospitalId { get; set; }
        public string SymptomCode { get; set; }
        public string DisplayLabel { get; set; }
        public bool IsQuickPick { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Tri-state complaint: UNSET → YES → NO → UNSET.</summary>
    public class ConsultationSymptom
    {
        public string SymptomId { get; set; }
        public string ConsultationId { get; set; }
        public string SymptomCode { get; set; }
        public string State { get; set; }
    }

    public class Icd10Entry
    {
        public string Icd10CatalogId { get; set; }
        public string Icd10Code { get; set; }
        public string Description { get; set; }
        public string SynonymTerms { get; set; }
    }

    public class ConsultationDiagnosis
    {
        public string DiagnosisId { get; set; }
        public string ConsultationId { get; set; }
        public string Icd10Code { get; set; }
        public string Icd10Description { get; set; }
        public string ClinicalNotes { get; set; }
        public string Laterality { get; set; }
        public string AcuteChronic { get; set; }
        public bool IsPrimary { get; set; }
        public int SequenceOrder { get; set; }
    }

    public class Drug
    {
        public string DrugId { get; set; }
        public string HospitalId { get; set; }
        public string BrandName { get; set; }
        public string GenericName { get; set; }
        public string Composition { get; set; }
        public string TherapeuticClass { get; set; }
        public string GenericClass { get; set; }
        public string DrugForm { get; set; }
        public string DefaultRoute { get; set; }
        public string DefaultDosage { get; set; }
        public string DefaultFrequency { get; set; }
        public int? DefaultDurationDays { get; set; }
        public string DefaultInstructions { get; set; }
        public bool IsControlledSubstance { get; set; }
        public bool IsActive { get; set; }
    }

    public class Prescription
    {
        public string PrescriptionId { get; set; }
        public string ConsultationId { get; set; }
        public string PatientId { get; set; }
        public string Category { get; set; }
        public string MedicineName { get; set; }
        public string Strength { get; set; }
        public string DrugForm { get; set; }
        public string DeliveryRoute { get; set; }
        public string TargetArea { get; set; }
        public string TakeInUnit { get; set; }
        public double TakeInQty { get; set; }
        public int TimesPerDay { get; set; }
        public int DurationValue { get; set; }
        public string DurationUnit { get; set; }
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public bool TimingBf { get; set; }
        public bool TimingAn { get; set; }
        public bool TimingNi { get; set; }
        public bool TimingEm { get; set; }
        public bool Prn { get; set; }
        public string Instructions { get; set; }
        public string FrequencyMode { get; set; }
        public string FrequencyPeriod { get; set; }
        public string DynamicInstruction { get; set; }
        public string GenericClass { get; set; }
        public string TherapeuticClass { get; set; }
        public int ValidityDays { get; set; }
        public DateTime? ValidUntil { get; set; }
        public double TotalDispenseQty { get; set; }
        public string Status { get; set; }
        public DateTime PrescribedDate { get; set; }
        public string RepeatedFromConsultationId { get; set; }
        /// <summary>null = prescribed in a consultation here; "OUTSIDE" = medicine from elsewhere, recorded on the patient record.</summary>
        public string Source { get; set; }
        /// <summary>Who prescribed an outside medicine (e.g. "Cardiologist, City Hospital", "Self").</summary>
        public string SourceDetail { get; set; }
        public bool IsOutside { get { return Source == "OUTSIDE"; } }

        public string TimingLabel
        {
            get
            {
                var parts = new List<string>();
                if (TimingEm) parts.Add("EM");
                if (TimingBf) parts.Add("BF");
                if (TimingAn) parts.Add("AN");
                if (TimingNi) parts.Add("NI");
                if (Prn) parts.Add("PRN");
                return string.Join(", ", parts);
            }
        }
    }

    public class LabTest
    {
        public string TestId { get; set; }
        public string HospitalId { get; set; }
        public string TestName { get; set; }
        public string TestType { get; set; }
        public string Category { get; set; }
        public List<string> SpecialtyIds { get; set; }
        public bool IsActive { get; set; }
    }

    public class DoctorLabFavorite
    {
        public string FavoriteId { get; set; }
        public string DoctorId { get; set; }
        public string TestId { get; set; }
    }

    /// <summary>Lab/imaging order. Status: ORDERED → SENT → RESULTS_READY.</summary>
    public class InvestigationOrder
    {
        public string OrderId { get; set; }
        public string HospitalId { get; set; }
        public string ConsultationId { get; set; }
        public string PatientId { get; set; }
        public string TestId { get; set; }
        public string TestName { get; set; }
        public string TestType { get; set; }
        public string Status { get; set; }
        public DateTime OrderedAt { get; set; }
        /// <summary>Lab partner handling the order; null until a lab claims it from the patient's requisition.</summary>
        public string LabPartnerId { get; set; }
    }

    /// <summary>Result uploaded by a lab partner. Status: PENDING_REVIEW → REVIEWED.</summary>
    public class LabResult
    {
        public string ResultId { get; set; }
        public string OrderId { get; set; }
        public string HospitalId { get; set; }
        public string PatientId { get; set; }
        public string ConsultationId { get; set; }
        public string TestName { get; set; }
        public string ResultSummary { get; set; }
        public string ResultValue { get; set; }
        public string ReferenceRange { get; set; }
        public string Interpretation { get; set; }
        public string Technician { get; set; }
        public string Notes { get; set; }
        public string UploadedByUserId { get; set; }
        public string LabPartnerId { get; set; }
        public DateTime UploadedAt { get; set; }
        public string Status { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string ReviewNotes { get; set; }
    }

    /// <summary>Report file (PDF / image / DICOM) attached to a lab result. The file itself is stored under App_Data.</summary>
    public class LabResultAttachment
    {
        public string AttachmentId { get; set; }
        public string ResultId { get; set; }
        public string OrderId { get; set; }
        public string HospitalId { get; set; }
        public string LabPartnerId { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public long SizeBytes { get; set; }
        public string StoredFileName { get; set; }
        public string UploadedByUserId { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public class Referral
    {
        public string ReferralId { get; set; }
        public string ConsultationId { get; set; }
        public string PatientId { get; set; }
        public string Specialty { get; set; }
        public string DoctorName { get; set; }
        public string Urgency { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// A service or procedure done during the visit (picked from the hospital's Service Charge Master). Holds no price:
    /// sign-off turns each row into an invoice line at the master's amount and tax.
    /// </summary>
    public class ConsultationServiceItem
    {
        public string ConsultationServiceId { get; set; }
        public string ConsultationId { get; set; }
        public string PatientId { get; set; }
        public string ServiceChargeId { get; set; }
        public string ServiceName { get; set; }
        public string Category { get; set; }
        public int Qty { get; set; }
        public string Notes { get; set; }
        public string AddedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Append-only post-lock correction.</summary>
    public class Addendum
    {
        public string AddendumId { get; set; }
        public string ConsultationId { get; set; }
        public string DoctorId { get; set; }
        public string Category { get; set; }
        public string AddendumText { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Author-only note; never shown to anyone but the writing doctor.</summary>
    public class ConfidentialNote
    {
        public string ConfidentialNoteId { get; set; }
        public string ConsultationId { get; set; }
        public string DoctorId { get; set; }
        public string NoteText { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ConsultationAttestation
    {
        public string AttestationId { get; set; }
        public string ConsultationId { get; set; }
        public string DoctorId { get; set; }
        public DateTime SignedAt { get; set; }
        public string SignatureHash { get; set; }
    }

    /// <summary>SOAP phrase / "/" macro. Scope: GLOBAL, SPECIALTY or DOCTOR. Section: S, O, P or ANY.</summary>
    public class Phrase
    {
        public string PhraseId { get; set; }
        public string Scope { get; set; }
        public string SpecialtyId { get; set; }
        public string DoctorId { get; set; }
        public string ConditionId { get; set; }
        public string Section { get; set; }
        public string TriggerKeyword { get; set; }
        public string PhraseText { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Groups phrases by condition; matches a consultation by ICD-10 prefix.</summary>
    public class ClinicalCondition
    {
        public string ConditionId { get; set; }
        public string HospitalId { get; set; }
        public string Name { get; set; }
        public string Icd10Prefixes { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>One previous (signed) visit, summarised for the context panel.</summary>
    public class PastVisit
    {
        public string ConsultationId { get; set; }
        public DateTime VisitDate { get; set; }
        public string DoctorName { get; set; }
        public string ChiefComplaint { get; set; }
        public string PrimaryDiagnosis { get; set; }
        public string FollowupNotes { get; set; }
    }

    /// <summary>Alert raised by the contradiction engine or the Rx reconciler.</summary>
    public class ClinicalAlert
    {
        public string Severity { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
    }

    public class SoapDraft
    {
        public string Subjective { get; set; }
        public string Objective { get; set; }
        public string Assessment { get; set; }
        public string Plan { get; set; }
    }

    /// <summary>Drawing-pad sketch saved with a consultation (JPEG data URL).</summary>
    public class ConsultationSketch
    {
        public string SketchId { get; set; }
        public string ConsultationId { get; set; }
        public string PatientId { get; set; }
        public string DoctorId { get; set; }
        public string Title { get; set; }
        public string ImageData { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
