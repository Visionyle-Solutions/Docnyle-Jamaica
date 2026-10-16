using System;
using System.Collections.Generic;
using System.Linq;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// Itemised invoicing, discounts/tax, payments, receipts, credit notes and the N-way bill split:
    /// Charge = Σ(verified insurance / govt-scheme liabilities) + patient share.
    /// </summary>
    public static class BillingService
    {
        private static decimal R2(decimal v) { return Math.Round(v, 2, MidpointRounding.AwayFromZero); }

        /* ── Consultation charge ── */

        /// <summary>
        /// Consultation fee: appointment type (chargeable? fee category, duration) → doctor charge model
        /// (FLAT fee schedule, or UNIT = ceil(duration ÷ unit) × rate) → free follow-up when a PAID consultation
        /// with the same doctor falls within the free-follow-up window.
        /// </summary>
        public static ConsultationCharge GetConsultationCharge(string consultationId)
        {
            var c = ConsultationService.Get(consultationId);
            if (c == null) return null;
            var hid = c.HospitalId;
            var date = c.VisitDate.Date;
            var appt = SchedulingService.AppointmentForConsultation(c);
            var type = appt != null ? SchedulingService.GetAppointmentType(hid, appt.Type) : null;
            var category = type != null ? type.FeeCategory : (appt != null && appt.Type == "FOLLOWUP" ? "FOLLOWUP" : "NEW");
            var typeName = type != null ? type.Name : (category == "FOLLOWUP" ? "Follow-Up" : "New Consultation");
            int duration = appt != null ? (int)(appt.SlotEnd - appt.SlotStart).TotalMinutes : SchedulingService.SlotMinutes(hid, c.DoctorId);
            var result = new ConsultationCharge
            {
                ConsultationType = category, AppointmentTypeName = typeName, DurationMinutes = duration, ChargeModel = "FLAT",
                FeeAmount = 0, Description = "Consultation – " + typeName, Basis = "", Note = ""
            };
            if (type != null && !type.IsChargeable)
            {
                result.Basis = "Non-chargeable appointment type";
                result.Note = typeName + " is set as non-chargeable.";
                return result;
            }
            var model = SchedulingService.ResolveSetting(hid, c.DoctorId, "CHARGE_MODEL").Value ?? "FLAT";
            if (model == "UNIT")
            {
                var ch = SchedulingService.GetDoctorCharge(hid, c.DoctorId);
                int units = Math.Max(1, (int)Math.Ceiling((double)(duration > 0 ? duration : ch.UnitMinutes) / ch.UnitMinutes));
                result.FeeAmount = units * ch.UnitRate;
                result.Basis = units + " × " + ch.UnitMinutes + "-min unit @ " + ch.UnitRate;
            }
            else
            {
                result.FeeAmount = SchedulingService.FlatFee(c.DoctorId, category, date);
                result.Basis = "Flat fee (" + (category == "FOLLOWUP" ? "follow-up" : "new consultation") + ")";
            }
            result.ChargeModel = model;

            bool allowed = SchedulingService.ResolveSetting(hid, c.DoctorId, "FREE_FOLLOWUP_ALLOWED").Value == "true";
            int windowDays;
            int.TryParse(SchedulingService.ResolveSetting(hid, c.DoctorId, "FREE_FOLLOWUP_DAYS").Value, out windowDays);
            DateTime? anchor;
            lock (DataStore.Sync)
            {
                anchor = DataStore.Invoices
                    .Where(i => i.PatientId == c.PatientId && i.DoctorId == c.DoctorId && i.Status != "CANCELLED" && i.ConsultationId != c.ConsultationId && !i.IsFreeFollowup)
                    .Where(i => DataStore.LineItems.Any(l => l.InvoiceId == i.InvoiceId && l.Category == "CONSULTATION" && l.UnitAmount > 0))
                    .Select(i =>
                    {
                        var pc = DataStore.Consultations.FirstOrDefault(x => x.ConsultationId == i.ConsultationId);
                        return pc != null ? pc.VisitDate.Date : i.CreatedAt.Date;
                    })
                    .Where(d => d <= date).OrderBy(d => d).Select(d => (DateTime?)d).LastOrDefault();
            }
            if (allowed && anchor.HasValue)
            {
                int days = (int)(date - anchor.Value).TotalDays;
                if (days >= 0 && days <= windowDays)
                {
                    result.FeeAmount = 0;
                    result.IsFreeFollowup = true;
                    result.Description += " (free follow-up)";
                    result.Note = "Free follow-up — within " + windowDays + " days of the " + HospitalService.FormatDate(hid, anchor.Value) + " consultation (day " + days + ").";
                    return result;
                }
            }
            result.Note = !allowed ? "Chargeable." : anchor.HasValue
                ? "Chargeable — outside the " + windowDays + "-day free-follow-up window."
                : "Chargeable — no earlier paid consultation with this doctor.";
            return result;
        }

        /* ── Invoices ── */

        public static Invoice GetInvoice(string invoiceId)
        {
            lock (DataStore.Sync) return DataStore.Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId);
        }

        public static Invoice GetInvoiceForConsultation(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Invoices.FirstOrDefault(i => i.ConsultationId == consultationId && i.Status != "CANCELLED");
        }

        public static Invoice GetLatestInvoiceForConsultation(string consultationId)
        {
            lock (DataStore.Sync) return DataStore.Invoices.Where(i => i.ConsultationId == consultationId).OrderByDescending(i => i.CreatedAt).FirstOrDefault();
        }

        public static List<Invoice> ListInvoices(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.Invoices.Where(i => i.HospitalId == hospitalId).OrderByDescending(i => i.CreatedAt).ToList();
        }

        /// <summary>
        /// Creates the visit's invoice: the consultation fee line (kept at 0 for free follow-ups so the receipt shows why),
        /// then one line per service / procedure the doctor recorded, at the Service Charge Master's current amount and tax.
        /// Lab and imaging orders are NOT billed — the clinic has no in-house diagnostics; the patient takes the
        /// requisition to an outside lab / imaging centre and pays there. Returns the existing invoice if one exists.
        /// </summary>
        public static Invoice GenerateInvoiceForConsultation(string consultationId)
        {
            var existing = GetInvoiceForConsultation(consultationId);
            if (existing != null) return existing;
            var c = ConsultationService.Get(consultationId);
            if (c == null) return null;
            var hospital = HospitalService.GetHospital(c.HospitalId);
            var charge = GetConsultationCharge(consultationId);
            Invoice inv;
            lock (DataStore.Sync)
            {
                inv = new Invoice
                {
                    InvoiceId = DataStore.NextId("INV"),
                    InvoiceNumber = (hospital.InvoicePrefix ?? "INV") + "-" + (DataStore.Invoices.Count + 1001).ToString("000000"),
                    HospitalId = c.HospitalId, ConsultationId = consultationId, PatientId = c.PatientId, DoctorId = c.DoctorId,
                    ConsultationType = charge.ConsultationType, IsFreeFollowup = charge.IsFreeFollowup, ChargeNote = charge.Note,
                    Currency = hospital.Currency ?? "INR", Status = "PENDING", CreatedAt = DateTime.Now
                };
                DataStore.Invoices.Add(inv);
            }
            AddLineItem(inv.InvoiceId, null, charge.Description, "CONSULTATION", 1, charge.FeeAmount, 0, 0);
            foreach (var item in ConsultationService.ListServiceItems(consultationId))
            {
                var svc = GetServiceCharge(item.ServiceChargeId);
                if (svc == null) continue;
                AddLineItem(inv.InvoiceId, svc.ServiceChargeId, svc.ServiceName + (string.IsNullOrEmpty(item.Notes) ? "" : " — " + item.Notes),
                    svc.Category, item.Qty, svc.DefaultAmount, 0, svc.TaxPercent);
            }
            return RecalculateTotals(inv.InvoiceId);
        }

        /// <summary>Cancels the invoice and issues a credit note for its total.</summary>
        public static CreditNote CancelInvoice(string invoiceId, string reason, string userId)
        {
            lock (DataStore.Sync)
            {
                var inv = GetInvoice(invoiceId);
                if (inv == null || inv.Status == "CANCELLED") return null;
                inv.Status = "CANCELLED";
                inv.ModifiedAt = DateTime.Now;
                var cn = new CreditNote
                {
                    CreditNoteId = DataStore.NextId("CN"), CreditNoteNumber = "CN-" + (DataStore.CreditNotes.Count + 1001).ToString("000000"),
                    HospitalId = inv.HospitalId, InvoiceId = invoiceId, Amount = inv.TotalAmount, Reason = reason, IssuedBy = userId, IssuedAt = DateTime.Now
                };
                DataStore.CreditNotes.Add(cn);
                return cn;
            }
        }

        public static List<CreditNote> ListCreditNotes(string invoiceId)
        {
            lock (DataStore.Sync) return DataStore.CreditNotes.Where(c => c.InvoiceId == invoiceId).ToList();
        }

        /* ── Line items ── */

        public static List<ServiceCharge> ListServiceCharges(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.ServiceCharges.Where(s => s.HospitalId == hospitalId && s.IsActive).ToList();
        }

        public static ServiceCharge AddServiceCharge(string hospitalId, string code, string name, string category, decimal amount, decimal taxPercent)
        {
            lock (DataStore.Sync)
            {
                var row = new ServiceCharge { ServiceChargeId = DataStore.NextId("SVC"), HospitalId = hospitalId, ServiceCode = code, ServiceName = name, Category = category, DefaultAmount = amount, TaxPercent = taxPercent, IsActive = true };
                DataStore.ServiceCharges.Add(row);
                return row;
            }
        }

        public static void ToggleServiceCharge(string id)
        {
            lock (DataStore.Sync)
            {
                var s = DataStore.ServiceCharges.FirstOrDefault(x => x.ServiceChargeId == id);
                if (s != null) s.IsActive = !s.IsActive;
            }
        }

        public static List<ServiceCharge> ListAllServiceCharges(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.ServiceCharges.Where(s => s.HospitalId == hospitalId).OrderBy(s => s.Category).ThenBy(s => s.ServiceName).ToList();
        }

        public static ServiceCharge GetServiceCharge(string id)
        {
            lock (DataStore.Sync) return DataStore.ServiceCharges.FirstOrDefault(s => s.ServiceChargeId == id);
        }

        public static List<InvoiceLineItem> ListLineItems(string invoiceId)
        {
            lock (DataStore.Sync) return DataStore.LineItems.Where(l => l.InvoiceId == invoiceId).OrderBy(l => l.SortOrder).ToList();
        }

        /// <summary>Adds a line: subtotal − discount% then + tax%, rounded to 2 decimals.</summary>
        public static InvoiceLineItem AddLineItem(string invoiceId, string serviceChargeId, string description, string category, int qty, decimal unit, decimal discountPercent, decimal taxPercent)
        {
            lock (DataStore.Sync)
            {
                if (qty < 1) qty = 1;
                var subtotal = qty * unit;
                var disc = R2(subtotal * discountPercent / 100m);
                var tax = R2((subtotal - disc) * taxPercent / 100m);
                var row = new InvoiceLineItem
                {
                    LineItemId = DataStore.NextId("LI"), InvoiceId = invoiceId, ServiceChargeId = serviceChargeId, Description = description,
                    Category = category, Qty = qty, UnitAmount = unit, DiscountPercent = discountPercent, DiscountAmount = disc,
                    TaxPercent = taxPercent, TaxAmount = tax, LineTotal = R2(subtotal - disc + tax),
                    SortOrder = DataStore.LineItems.Count(l => l.InvoiceId == invoiceId) + 1
                };
                DataStore.LineItems.Add(row);
                return row;
            }
        }

        public static void RemoveLineItem(string lineItemId)
        {
            lock (DataStore.Sync) DataStore.LineItems.RemoveAll(l => l.LineItemId == lineItemId);
        }

        /// <summary>Recomputes the invoice totals from its line items, then re-derives the coverage split and status.</summary>
        public static Invoice RecalculateTotals(string invoiceId)
        {
            lock (DataStore.Sync)
            {
                var inv = GetInvoice(invoiceId);
                if (inv == null) return null;
                var items = DataStore.LineItems.Where(l => l.InvoiceId == invoiceId).ToList();
                inv.SubTotal = R2(items.Sum(l => l.Qty * l.UnitAmount));
                inv.DiscountAmount = R2(items.Sum(l => l.DiscountAmount));
                inv.TaxAmount = R2(items.Sum(l => l.TaxAmount));
                inv.TotalAmount = R2(items.Sum(l => l.LineTotal));
                inv.ModifiedAt = DateTime.Now;
                return RecomputeSplit(inv);
            }
        }

        /* ── Coverage & split ── */

        private static decimal CoverageLiability(decimal charge, string approvedType, decimal approvedValue, decimal alreadyAllocated)
        {
            var raw = approvedType == "PERCENT" ? R2(charge * approvedValue / 100m) : approvedValue;
            return Math.Max(0, Math.Min(raw, R2(charge - alreadyAllocated)));
        }

        /// <summary>
        /// Re-derives an invoice's split + status from its line totals, coverage lines and payments. Percent coverages follow
        /// the current charge; fixed amounts are re-capped. The invoice is PAID once the patient's share is settled —
        /// verified coverage is receivable from the payer.
        /// </summary>
        private static Invoice RecomputeSplit(Invoice inv)
        {
            var charge = inv.TotalAmount;
            decimal allocated = 0;
            foreach (var l in DataStore.CoverageLines.Where(x => x.InvoiceId == inv.InvoiceId && x.Status == "VERIFIED").OrderBy(x => x.Priority))
            {
                l.Liability = l.ApprovedType == "PERCENT"
                    ? CoverageLiability(charge, "PERCENT", l.ApprovedValue, allocated)
                    : CoverageLiability(charge, "AMOUNT", l.IsManualAmount ? l.Liability : l.ApprovedValue, allocated);
                allocated += l.Liability;
            }
            var live = DataStore.Payments.Where(p => p.InvoiceId == inv.InvoiceId && !p.IsVoided).ToList();
            foreach (var l in DataStore.CoverageLines.Where(x => x.InvoiceId == inv.InvoiceId))
                l.SettledAmount = R2(live.Where(p => p.CoverageLineId == l.CoverageLineId).Sum(p => p.Amount));
            inv.CoveredAmount = R2(allocated);
            inv.PatientLiability = R2(Math.Max(0, charge - allocated));
            inv.PatientPaidAmount = R2(live.Where(p => (p.ComponentType ?? "PATIENT") == "PATIENT").Sum(p => p.Amount));
            inv.PaidAmount = R2(live.Sum(p => p.Amount));
            if (inv.Status != "CANCELLED")
            {
                bool hasLines = DataStore.LineItems.Any(l => l.InvoiceId == inv.InvoiceId);
                if (charge == 0 && hasLines) inv.Status = "PAID";
                else if (charge > 0 && (inv.PaidAmount >= charge || (inv.PatientPaidAmount >= inv.PatientLiability && (allocated > 0 || inv.PatientPaidAmount > 0)))) inv.Status = "PAID";
                else if (allocated > 0 || inv.PaidAmount > 0) inv.Status = "PARTIAL";
                else inv.Status = "PENDING";
            }
            return inv;
        }

        public static BillingSplit GetBillingSplit(string invoiceId)
        {
            lock (DataStore.Sync)
            {
                var inv = GetInvoice(invoiceId);
                if (inv == null) return null;
                RecomputeSplit(inv);
                var lines = DataStore.CoverageLines.Where(l => l.InvoiceId == invoiceId).OrderBy(l => l.Priority).ToList();
                Func<string, decimal> sum = t => R2(lines.Where(l => l.CoverageType == t && l.Status == "VERIFIED").Sum(l => l.Liability));
                return new BillingSplit
                {
                    InvoiceId = invoiceId, Currency = inv.Currency, GrossAmount = inv.TotalAmount,
                    InsuranceLiability = sum("PRIVATE_INSURANCE"), GovtLiability = sum("GOVT_SCHEME"),
                    CoveredAmount = inv.CoveredAmount, PatientLiability = inv.PatientLiability, PatientPaid = inv.PatientPaidAmount,
                    PatientDue = R2(Math.Max(0, inv.PatientLiability - inv.PatientPaidAmount)),
                    PatientOverpaid = R2(Math.Max(0, inv.PatientPaidAmount - inv.PatientLiability)),
                    PayerOutstanding = R2(lines.Sum(l => Math.Max(0, l.Liability - l.SettledAmount))),
                    Lines = lines,
                };
            }
        }

        public static InvoiceCoverageLine GetCoverageLine(string invoiceId, string coverageId)
        {
            lock (DataStore.Sync) return DataStore.CoverageLines.FirstOrDefault(l => l.InvoiceId == invoiceId && l.CoverageId == coverageId);
        }

        public static List<EligibilityVerification> ListVerifications(string coverageId)
        {
            lock (DataStore.Sync) return DataStore.Verifications.Where(v => v.CoverageId == coverageId).OrderByDescending(v => v.VerifiedAt).ToList();
        }

        /// <summary>
        /// Records the payer's eligibility response for this invoice. VERIFIED applies (or updates) a liability line;
        /// PENDING/REJECTED removes it — unless the payer has already settled against it (returns false).
        /// </summary>
        public static bool ApplyCoverage(string invoiceId, string coverageId, string status, string authNo, string approvedType, decimal approvedValue, string notes, string userId)
        {
            lock (DataStore.Sync)
            {
                var inv = GetInvoice(invoiceId);
                var cov = PatientService.GetCoverage(coverageId);
                if (inv == null || cov == null || inv.Status == "CANCELLED") return false;
                approvedType = approvedType == "AMOUNT" ? "AMOUNT" : "PERCENT";
                approvedValue = Math.Max(0, approvedValue);
                DataStore.Verifications.Add(new EligibilityVerification
                {
                    VerificationId = DataStore.NextId("ELV"), CoverageId = coverageId, InvoiceId = invoiceId, Status = status, AuthorizationNumber = authNo,
                    ApprovedType = approvedType, ApprovedValue = approvedValue, Notes = notes, VerifiedBy = userId, VerifiedAt = DateTime.Now
                });
                var existing = GetCoverageLine(invoiceId, coverageId);
                if (status == "VERIFIED")
                {
                    var payer = PatientService.GetPayer(cov.PayerId);
                    var line = existing ?? new InvoiceCoverageLine { CoverageLineId = DataStore.NextId("ICL"), InvoiceId = invoiceId, CoverageId = coverageId };
                    line.CoverageType = cov.CoverageType; line.PayerId = cov.PayerId; line.PayerName = payer != null ? payer.Name : "Payer";
                    line.Priority = cov.Priority; line.AuthorizationNumber = authNo; line.ApprovedType = approvedType; line.ApprovedValue = approvedValue;
                    line.IsManualAmount = false; line.Status = "VERIFIED";
                    if (existing == null) DataStore.CoverageLines.Add(line);
                }
                else if (existing != null)
                {
                    if (existing.SettledAmount > 0) return false;
                    DataStore.CoverageLines.Remove(existing);
                }
                RecomputeSplit(inv);
                return true;
            }
        }

        /// <summary>Overrides an applied liability amount (capped so coverages never exceed the charge).</summary>
        public static void EditCoverageLine(string coverageLineId, decimal amount)
        {
            lock (DataStore.Sync)
            {
                var line = DataStore.CoverageLines.FirstOrDefault(l => l.CoverageLineId == coverageLineId);
                if (line == null) return;
                line.Liability = Math.Max(0, amount);
                line.IsManualAmount = true;
                RecomputeSplit(GetInvoice(line.InvoiceId));
            }
        }

        /// <summary>Not allowed once the payer has settled against the line.</summary>
        public static bool RemoveCoverageLine(string coverageLineId)
        {
            lock (DataStore.Sync)
            {
                var line = DataStore.CoverageLines.FirstOrDefault(l => l.CoverageLineId == coverageLineId);
                if (line == null || line.SettledAmount > 0) return false;
                DataStore.CoverageLines.Remove(line);
                RecomputeSplit(GetInvoice(line.InvoiceId));
                return true;
            }
        }

        /* ── Payments & receipts ── */

        public static List<Payment> ListPayments(string invoiceId)
        {
            lock (DataStore.Sync) return DataStore.Payments.Where(p => p.InvoiceId == invoiceId).OrderBy(p => p.PaidAt).ToList();
        }

        public static List<Payment> ListHospitalPayments(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.Payments.Where(p => p.HospitalId == hospitalId).ToList();
        }

        public static Payment RecordPayment(string invoiceId, string coverageLineId, string method, decimal amount, string reference, string userId)
        {
            lock (DataStore.Sync)
            {
                var inv = GetInvoice(invoiceId);
                var row = new Payment
                {
                    TransactionId = DataStore.NextId("PAY"), TransactionNumber = "TXN-" + (DataStore.Payments.Count + 1001).ToString("000000"),
                    InvoiceId = invoiceId, HospitalId = inv.HospitalId, ComponentType = coverageLineId == null ? "PATIENT" : "INSURANCE",
                    CoverageLineId = coverageLineId, PaymentMethod = method, Amount = amount, Reference = reference, ReceivedBy = userId, PaidAt = DateTime.Now
                };
                DataStore.Payments.Add(row);
                RecomputeSplit(inv);
                if (inv.Status == "PAID") EnsureReceipt(inv);
                return row;
            }
        }

        /// <summary>Payments are never deleted, only voided with a reason.</summary>
        public static bool VoidPayment(string transactionId, string reason, string userId)
        {
            lock (DataStore.Sync)
            {
                var p = DataStore.Payments.FirstOrDefault(x => x.TransactionId == transactionId);
                if (p == null || p.IsVoided) return false;
                p.IsVoided = true; p.VoidedAt = DateTime.Now; p.VoidedBy = userId; p.VoidReason = reason;
                RecomputeSplit(GetInvoice(p.InvoiceId));
                return true;
            }
        }

        public static Receipt GetReceipt(string invoiceId)
        {
            lock (DataStore.Sync) return DataStore.Receipts.FirstOrDefault(r => r.InvoiceId == invoiceId);
        }

        private static Receipt EnsureReceipt(Invoice inv)
        {
            var r = GetReceipt(inv.InvoiceId);
            if (r != null) return r;
            var hospital = HospitalService.GetHospital(inv.HospitalId);
            r = new Receipt
            {
                ReceiptId = DataStore.NextId("RCP"), ReceiptNumber = (hospital.ReceiptPrefix ?? "RCP") + "-" + (DataStore.Receipts.Count + 1001).ToString("000000"),
                InvoiceId = inv.InvoiceId, HospitalId = inv.HospitalId, PatientId = inv.PatientId, TotalAmount = inv.TotalAmount, Currency = inv.Currency, GeneratedAt = DateTime.Now
            };
            DataStore.Receipts.Add(r);
            return r;
        }
    }
}
