using System;

namespace OpdProject.Models
{
    public class Patient
    {
        public string PatientId { get; set; }
        public string HospitalId { get; set; }
        public string Mrn { get; set; }
        public string FullName { get; set; }
        public DateTime? Dob { get; set; }
        public string Gender { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string NationalHealthId { get; set; }
        public string SmokingStatus { get; set; }
        public int? SmokingPackYears { get; set; }
        public string AlcoholUse { get; set; }
        public string Occupation { get; set; }
        public string PhysicalActivity { get; set; }
        public string Diet { get; set; }
        public string BloodGroup { get; set; }
        public string EmergencyContactName { get; set; }
        public string EmergencyContactPhone { get; set; }
        public string InsuranceProvider { get; set; }
        public string InsurancePolicyNo { get; set; }
        public string RegisteredVia { get; set; }

        /* Structured registration fields. FullName, Phone and Address stay the composed
           values the rest of the app reads; these keep the parts the registration form edits. */
        public string Prefix { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public string Trn { get; set; }
        public string CountryCode { get; set; }
        public string Mobile { get; set; }
        public string PreferredLanguage { get; set; }
        public string AddrLine1 { get; set; }
        public string AddrLine2 { get; set; }
        public string City { get; set; }
        public string Region { get; set; }
        public string Country { get; set; }
        public string MaritalStatus { get; set; }
        public string ReferralSource { get; set; }
        public string PreferredDoctorId { get; set; }

        public int? AgeYears
        {
            get
            {
                if (!Dob.HasValue) return null;
                var today = DateTime.Today;
                int age = today.Year - Dob.Value.Year;
                if (Dob.Value.Date > today.AddYears(-age)) age--;
                return age;
            }
        }
    }

    /// <summary>A registration clash found by PatientService.FindDuplicate.</summary>
    public class PatientDuplicate
    {
        /// <summary>"NationalHealthId", "Trn" or "Mobile".</summary>
        public string Field { get; set; }
        public string Value { get; set; }
        public Patient Patient { get; set; }
    }

    public class PatientAllergy
    {
        public string AllergyId { get; set; }
        public string PatientId { get; set; }
        public string AllergyType { get; set; }
        public string AllergyName { get; set; }
        public string ReactionType { get; set; }
        public string ReactionDescription { get; set; }
        public string Severity { get; set; }
        public string Status { get; set; }
        public bool IsTreated { get; set; }
        public bool IsFamilyHistory { get; set; }
        public DateTime? SinceWhen { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>Quick-pick chronic condition (MedicalHistoryItemConfig).</summary>
    public class MedicalHistoryItem
    {
        public string ItemId { get; set; }
        public string HospitalId { get; set; }
        public string ItemName { get; set; }
        public bool IsQuickPick { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class PatientMedicalHistory
    {
        public string PatientMedHistoryId { get; set; }
        public string PatientId { get; set; }
        public string ItemId { get; set; }
        public string ConditionName { get; set; }
        public int? OnsetMonth { get; set; }
        public int? OnsetYear { get; set; }
        /// <summary>ACTIVE, CONTROLLED or RESOLVED (null on older records = ACTIVE).</summary>
        public string Status { get; set; }
        public string Notes { get; set; }
        public string RecordedByUserId { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    /// <summary>Quick-pick medical event (MedicalEventConfig).</summary>
    public class MedicalEventItem
    {
        public string EventId { get; set; }
        public string HospitalId { get; set; }
        public string EventName { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class PatientMedicalEvent
    {
        public string PatientMedEventId { get; set; }
        public string PatientId { get; set; }
        public string EventId { get; set; }
        public string EventName { get; set; }
        public int? EventMonth { get; set; }
        public int? EventYear { get; set; }
        public string Notes { get; set; }
        public string RecordedByUserId { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    public class PatientFamilyHistory
    {
        public string FamilyHistoryId { get; set; }
        public string PatientId { get; set; }
        public string RelationshipType { get; set; }
        public string ConditionName { get; set; }
        public string Notes { get; set; }
        public DateTime RecordedAt { get; set; }
    }
}
