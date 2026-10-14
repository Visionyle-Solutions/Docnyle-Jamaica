using System;
using System.Collections.Generic;

namespace OpdProject.Models
{
    /// <summary>Hospitals table (multi-tenant root). Country-specific behaviour is driven by these settings.</summary>
    public class Hospital
    {
        public string HospitalId { get; set; }
        public string Name { get; set; }
        public string LegalAddress { get; set; }
        public string PrimaryEmail { get; set; }
        public string ContactNumber { get; set; }
        public string Country { get; set; }
        public string Currency { get; set; }
        public string Timezone { get; set; }
        public string PatientIdLabel { get; set; }
        public string NationalHealthIdLabel { get; set; }
        public bool NationalHealthIdMandatory { get; set; }
        public string GrowthChartStandard { get; set; }
        public int DefaultRxValidityDays { get; set; }
        public string LicenseNumber { get; set; }
        public string DefaultTempUnit { get; set; }
        public string RxPrefix { get; set; }
        public string ReceiptPrefix { get; set; }
        public string InvoicePrefix { get; set; }
        public decimal LabChargePerTest { get; set; }
        public string LogoBase64 { get; set; }
    }

    /// <summary>In-app notification for a staff user (sec.UserNotification), shown under the top-bar bell.</summary>
    public class UserNotification
    {
        public string NotificationId { get; set; }
        public string HospitalId { get; set; }
        public string UserId { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        /// <summary>App-absolute URL opened when the notification is clicked.</summary>
        public string Url { get; set; }
        public bool IsCritical { get; set; }
        /// <summary>What the notification is about (e.g. a ResultId), so it can be cleared once that is dealt with.</summary>
        public string EntityId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    /// <summary>External lab / imaging centre (cfg.LabPartner). Several LAB_PARTNER user accounts can belong to one partner.</summary>
    public class LabPartner
    {
        public string LabPartnerId { get; set; }
        public string HospitalId { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>ApplicationUsers table. Roles: ADMIN, DOCTOR, RECEPTIONIST, BILLING, NURSE, LAB_PARTNER.</summary>
    public class AppUser
    {
        public string UserId { get; set; }
        public string HospitalId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Password { get; set; }
        public string Role { get; set; }
        public string SpecialtyId { get; set; }
        public string LicenseNumber { get; set; }
        public string Prefix { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public string Qualifications { get; set; }
        public string DisplayNameOverride { get; set; }
        public string Room { get; set; }
        public string Languages { get; set; }
        public string Bio { get; set; }
        public string LabPartnerId { get; set; }
        public bool IsActive { get; set; }
    }

    public class Specialty
    {
        public string SpecialtyId { get; set; }
        public string HospitalId { get; set; }
        public string SpecialtyName { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Key/value hospital configuration (HospitalSetting table).</summary>
    public class HospitalSetting
    {
        public string HospitalId { get; set; }
        public string SettingKey { get; set; }
        public string Value { get; set; }
    }

    /// <summary>Per-doctor override of an overridable hospital setting (DoctorSetting table).</summary>
    public class DoctorSetting
    {
        public string HospitalId { get; set; }
        public string DoctorId { get; set; }
        public string SettingKey { get; set; }
        public string Value { get; set; }
    }

    /// <summary>A resolved setting value and where it came from (DOCTOR, HOSPITAL or NONE).</summary>
    public class ResolvedSetting
    {
        public string Value { get; set; }
        public string Source { get; set; }
    }

    public class AuditLogEntry
    {
        public string AuditLogId { get; set; }
        public string HospitalId { get; set; }
        public string UserId { get; set; }
        public string Action { get; set; }
        public string EntityType { get; set; }
        public string EntityId { get; set; }
        public string Details { get; set; }
        public DateTime Timestamp { get; set; }
        public string IpAddress { get; set; }
        public string UserAgent { get; set; }
    }

    public class Compliance
    {
        public string ComplianceId { get; set; }
        public string ComplianceName { get; set; }
        public string Country { get; set; }
        public bool IsMandatory { get; set; }
        public string DisplayLabel { get; set; }
        public string PrintLabel { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class HospitalCompliance
    {
        public string HospitalComplianceId { get; set; }
        public string HospitalId { get; set; }
        public string ComplianceId { get; set; }
        public string RegistrationNumber { get; set; }
        public DateTime? IssuedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsActive { get; set; }
    }

    public class ChannelConfig
    {
        public string Provider { get; set; }
        public string ApiKey { get; set; }
        public string PhoneNumberId { get; set; }
        public string BusinessAccountId { get; set; }
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; }
        public string FromEmail { get; set; }
        public string FromName { get; set; }
        public bool Enabled { get; set; }
    }

    public class MessageTemplate
    {
        public string TemplateId { get; set; }
        public string Name { get; set; }
        public string Trigger { get; set; }
        public string Channel { get; set; }
        public bool IsEnabled { get; set; }
        public string Text { get; set; }
    }

    public class QueuedMessage
    {
        public string MessageId { get; set; }
        public string HospitalId { get; set; }
        public string PatientId { get; set; }
        public string PatientName { get; set; }
        public string PatientPhone { get; set; }
        public string Channel { get; set; }
        public string TemplateName { get; set; }
        public string MessageText { get; set; }
        public string Status { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }

    /// <summary>One row of the permission matrix grid: a feature and its access per role.</summary>
    public class PermissionRow
    {
        public string Feature { get; set; }
        public string Label { get; set; }
        public Dictionary<string, bool> Access { get; set; }
    }
}
