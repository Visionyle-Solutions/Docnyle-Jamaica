using System;
using System.Collections.Generic;

namespace OpdProject.Models
{
    /// <summary>Effective-dated doctor fee (DoctorFeeSchedule).</summary>
    public class DoctorFee
    {
        public string FeeScheduleId { get; set; }
        public string HospitalId { get; set; }
        public string DoctorId { get; set; }
        public string ConsultationType { get; set; }
        public decimal FeeAmount { get; set; }
        public string Currency { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
    }

    public class PaymentMethod
    {
        public string PaymentMethodId { get; set; }
        public string HospitalId { get; set; }
        public string MethodName { get; set; }
        public bool IsActive { get; set; }
    }

    public class ServiceCharge
    {
        public string ServiceChargeId { get; set; }
        public string HospitalId { get; set; }
        public string ServiceCode { get; set; }
        public string ServiceName { get; set; }
        public string Category { get; set; }
        public decimal DefaultAmount { get; set; }
        public decimal TaxPercent { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Invoice. Status: PENDING → PARTIAL → PAID (or CANCELLED with a credit note).</summary>
    public class Invoice
    {
        public string InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string HospitalId { get; set; }
        public string ConsultationId { get; set; }
        public string PatientId { get; set; }
        public string DoctorId { get; set; }
        public string ConsultationType { get; set; }
        public bool IsFreeFollowup { get; set; }
        public string ChargeNote { get; set; }
        public string Currency { get; set; }
        public decimal SubTotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal CoveredAmount { get; set; }
        public decimal PatientLiability { get; set; }
        public decimal PatientPaidAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
    }

    public class InvoiceLineItem
    {
        public string LineItemId { get; set; }
        public string InvoiceId { get; set; }
        public string ServiceChargeId { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public int Qty { get; set; }
        public decimal UnitAmount { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxPercent { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotal { get; set; }
        public int SortOrder { get; set; }
    }

    public class CreditNote
    {
        public string CreditNoteId { get; set; }
        public string CreditNoteNumber { get; set; }
        public string HospitalId { get; set; }
        public string InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        public string IssuedBy { get; set; }
        public DateTime IssuedAt { get; set; }
    }

    /// <summary>Payment against an invoice. ComponentType PATIENT, or INSURANCE for a payer settlement.</summary>
    public class Payment
    {
        public string TransactionId { get; set; }
        public string TransactionNumber { get; set; }
        public string InvoiceId { get; set; }
        public string HospitalId { get; set; }
        public string ComponentType { get; set; }
        public string CoverageLineId { get; set; }
        public string PaymentMethod { get; set; }
        public decimal Amount { get; set; }
        public string Reference { get; set; }
        public string ReceivedBy { get; set; }
        public DateTime PaidAt { get; set; }
        public bool IsVoided { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string VoidedBy { get; set; }
        public string VoidReason { get; set; }
    }

    public class Receipt
    {
        public string ReceiptId { get; set; }
        public string ReceiptNumber { get; set; }
        public string InvoiceId { get; set; }
        public string HospitalId { get; set; }
        public string PatientId { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>Insurer (PRIVATE) or government scheme (GOVT_SCHEME).</summary>
    public class Payer
    {
        public string PayerId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Country { get; set; }
        public bool IsActive { get; set; }
    }

    public class GovtSchemeProfile
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public string CardLabel { get; set; }
    }

    /// <summary>Patient coverage. CoverageType PRIVATE_INSURANCE or GOVT_SCHEME; no row = Self-Pay.</summary>
    public class PatientCoverage
    {
        public string CoverageId { get; set; }
        public string HospitalId { get; set; }
        public string PatientId { get; set; }
        public string CoverageType { get; set; }
        public string PayerId { get; set; }
        public string PlanName { get; set; }
        public string MemberId { get; set; }
        public string PolicyNumber { get; set; }
        public string GroupNumber { get; set; }
        public string PolicyHolderName { get; set; }
        public string Relationship { get; set; }
        public string ClaimMode { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Status { get; set; }
        public string GovtCardNumber { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
    }

    public class EligibilityVerification
    {
        public string VerificationId { get; set; }
        public string CoverageId { get; set; }
        public string InvoiceId { get; set; }
        public string Status { get; set; }
        public string AuthorizationNumber { get; set; }
        public string ApprovedType { get; set; }
        public decimal ApprovedValue { get; set; }
        public string Notes { get; set; }
        public string VerifiedBy { get; set; }
        public DateTime VerifiedAt { get; set; }
    }

    /// <summary>One verified coverage applied to an invoice; its Liability is what the payer owes.</summary>
    public class InvoiceCoverageLine
    {
        public string CoverageLineId { get; set; }
        public string InvoiceId { get; set; }
        public string CoverageId { get; set; }
        public string CoverageType { get; set; }
        public string PayerId { get; set; }
        public string PayerName { get; set; }
        public int Priority { get; set; }
        public string AuthorizationNumber { get; set; }
        public string ApprovedType { get; set; }
        public decimal ApprovedValue { get; set; }
        public bool IsManualAmount { get; set; }
        public decimal Liability { get; set; }
        public decimal SettledAmount { get; set; }
        public string Status { get; set; }
    }

    /// <summary>Charge = Σ(coverage liabilities) + patient share.</summary>
    public class BillingSplit
    {
        public string InvoiceId { get; set; }
        public string Currency { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal InsuranceLiability { get; set; }
        public decimal GovtLiability { get; set; }
        public decimal CoveredAmount { get; set; }
        public decimal PatientLiability { get; set; }
        public decimal PatientPaid { get; set; }
        public decimal PatientDue { get; set; }
        public decimal PatientOverpaid { get; set; }
        public decimal PayerOutstanding { get; set; }
        public List<InvoiceCoverageLine> Lines { get; set; }
    }

    /// <summary>Consultation fee computed from appointment type, charge model and free follow-up policy.</summary>
    public class ConsultationCharge
    {
        public string ConsultationType { get; set; }
        public string AppointmentTypeName { get; set; }
        public int DurationMinutes { get; set; }
        public string ChargeModel { get; set; }
        public decimal FeeAmount { get; set; }
        public bool IsFreeFollowup { get; set; }
        public string Basis { get; set; }
        public string Note { get; set; }
        public string Description { get; set; }
    }
}
