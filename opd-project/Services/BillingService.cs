using System.Collections.Generic;
using System.Linq;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>
    /// Itemised invoicing, discounts/tax, payments, receipts, credit notes and the N-way bill split:
    /// Charge = Σ(verified insurance / govt-scheme liabilities) + patient share.
    /// </summary>
    public static partial class BillingService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        private static decimal R2(decimal v) { return Math.Round(v, 2, MidpointRounding.AwayFromZero); }

        /* ── Consultation charge ── */

        /// <summary>
        /// Consultation fee: appointment type (chargeable? fee category, duration) → doctor charge model
        /// (FLAT fee schedule, or UNIT = ceil(duration ÷ unit) × rate) → free follow-up when a PAID consultation
        /// with the same doctor falls within the free-follow-up window.
        /// </summary>
        public static ConsultationCharge GetConsultationCharge(string consultationId)
        {
            if (Api) return ToModel(DocNyle.OrNull(() => cnHelperObj.GetConsultationCharge(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess())?.ConsultationCharge);
            var c = ConsultationService.Get(consultationId);
            if (c == null) return null;
            var hid = c.HospitalId;
            var date = c.VisitLocalDate;
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
                        return pc != null ? pc.VisitLocalDate : TimeZoneService.LocalDate(i.CreatedAt, TimeZoneService.HospitalZoneId(i.HospitalId));
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
            if (Api) return ApiGetInvoice(invoiceId);
            lock (DataStore.Sync) return DataStore.Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId);
        }

        public static Invoice GetInvoiceForConsultation(string consultationId)
        {
            if (Api) return RequestCache.Get("invoice-for:" + consultationId, () => ToModel(DocNyle.OrNull(() => cnHelperObj.GetInvoiceForConsultation(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess())?.Invoice));
            lock (DataStore.Sync) return DataStore.Invoices.FirstOrDefault(i => i.ConsultationId == consultationId && i.Status != "CANCELLED");
        }

        /// <summary>API mode: GetInvoiceForConsultation (cancelled invoices are not returned).</summary>
        public static Invoice GetLatestInvoiceForConsultation(string consultationId)
        {
            if (Api) return GetInvoiceForConsultation(consultationId);
            lock (DataStore.Sync) return DataStore.Invoices.Where(i => i.ConsultationId == consultationId).OrderByDescending(i => i.CreatedAt).FirstOrDefault();
        }

        public static List<Invoice> ListInvoices(string hospitalId)
        {
            if (Api) return (cnHelperObj.ListInvoices(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Invoices ?? new List<BIL_InvoiceInfo>()).Select(ToModel).OrderByDescending(i => i.CreatedAt).ToList();
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
            if (Api) { RequestCache.Remove("invoice-for:" + consultationId); return ApiGenerateInvoice(consultationId); }
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
                    Currency = hospital.Currency ?? "INR", Status = "PENDING", CreatedAt = DateTime.UtcNow
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
                // The stocked item used for the service is billed on its own line, at its own quantity and price.
                var stock = item.InventoryItemId != null ? InventoryService.GetItem(item.InventoryItemId) : null;
                if (stock != null)
                    AddLineItem(inv.InvoiceId, null, stock.ItemName + " (for " + svc.ServiceName + ")", stock.Category,
                        item.ItemQty ?? 1, stock.UnitPrice, 0, stock.TaxPercent, stock.InventoryItemId);
            }
            return RecalculateTotals(inv.InvoiceId);
        }

        /// <summary>Cancels the invoice and issues a credit note for its total.</summary>
        public static CreditNote CancelInvoice(string invoiceId, string reason, string userId)
        {
            if (Api)
            {
                // 50121 (Conflict): already cancelled → null, like the in-memory store.
                BIL_CreditNoteInfo cn;
                try { cn = cnHelperObj.CancelInvoice(new BIL_CancelInvoiceReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, InvoiceId = invoiceId, Reason = reason }).EnsureSuccess().CreditNote; }
                catch (ApiException ex) when (ex.ErrorNumber == 50121) { return null; }
                Forget(invoiceId);
                return cn == null ? null : new CreditNote
                {
                    CreditNoteId = cn.CreditNoteId, CreditNoteNumber = cn.CreditNoteNumber, HospitalId = CallerHospital, InvoiceId = cn.InvoiceId,
                    Amount = cn.Amount, Reason = cn.Reason, IssuedBy = cn.IssuedBy, IssuedAt = DocNyle.InstantOrMin(cn.IssuedAt),
                };
            }
            lock (DataStore.Sync)
            {
                var inv = GetInvoice(invoiceId);
                if (inv == null || inv.Status == "CANCELLED") return null;
                inv.Status = "CANCELLED";
                inv.ModifiedAt = DateTime.UtcNow;
                var cn = new CreditNote
                {
                    CreditNoteId = DataStore.NextId("CN"), CreditNoteNumber = "CN-" + (DataStore.CreditNotes.Count + 1001).ToString("000000"),
                    HospitalId = inv.HospitalId, InvoiceId = invoiceId, Amount = inv.TotalAmount, Reason = reason, IssuedBy = userId, IssuedAt = DateTime.UtcNow
                };
                DataStore.CreditNotes.Add(cn);
                return cn;
            }
        }

        public static List<CreditNote> ListCreditNotes(string invoiceId)
        {
            if (Api)
                return (cnHelperObj.ListCreditNotes(AppSession.ApiHospitalId, AppSession.ApiUserId, invoiceId, AppSession.ApiHashValue).EnsureSuccess().CreditNotes ?? new List<BIL_CreditNoteInfo>()).Select(cn => new CreditNote
                {
                    CreditNoteId = cn.CreditNoteId, CreditNoteNumber = cn.CreditNoteNumber, HospitalId = CallerHospital, InvoiceId = cn.InvoiceId,
                    Amount = cn.Amount, Reason = cn.Reason, IssuedBy = cn.IssuedBy, IssuedAt = DocNyle.InstantOrMin(cn.IssuedAt),
                }).ToList();
            lock (DataStore.Sync) return DataStore.CreditNotes.Where(c => c.InvoiceId == invoiceId).ToList();
        }

        /* ── Line items ── */

        public static List<ServiceCharge> ListServiceCharges(string hospitalId)
        {
            if (Api) return HospitalCache.Get("service-charges", () => cnHelperObj.ListServiceCharges(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().ServiceCharges ?? new List<BIL_ServiceChargeInfo>()).Select(ToModel).Where(s => s.IsActive).ToList();
            lock (DataStore.Sync) return DataStore.ServiceCharges.Where(s => s.HospitalId == hospitalId && s.IsActive).ToList();
        }

        public static ServiceCharge AddServiceCharge(string hospitalId, string code, string name, string category, decimal amount, decimal taxPercent)
        {
            if (Api)
            {
                var s = cnHelperObj.CreateServiceCharge(new BIL_CreateServiceChargeReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ServiceCode = code, ServiceName = name, Category = category, DefaultAmount = amount, TaxPercent = taxPercent }).EnsureSuccess().ServiceCharge;
                HospitalCache.Clear("service-charges");
                return ToModel(s);
            }
            lock (DataStore.Sync)
            {
                var row = new ServiceCharge { ServiceChargeId = DataStore.NextId("SVC"), HospitalId = hospitalId, ServiceCode = code, ServiceName = name, Category = category, DefaultAmount = amount, TaxPercent = taxPercent, IsActive = true };
                DataStore.ServiceCharges.Add(row);
                return row;
            }
        }

        public static void ToggleServiceCharge(string id)
        {
            if (Api)
            {
                cnHelperObj.UpdateServiceChargeActive(new BIL_UpdateServiceChargeActiveReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ServiceChargeId = id, IsActive = null }).EnsureSuccess();
                HospitalCache.Clear("service-charges");
                return;
            }
            lock (DataStore.Sync)
            {
                var s = DataStore.ServiceCharges.FirstOrDefault(x => x.ServiceChargeId == id);
                if (s != null) s.IsActive = !s.IsActive;
            }
        }

        public static List<ServiceCharge> ListAllServiceCharges(string hospitalId)
        {
            if (Api) return (cnHelperObj.ListAllServiceCharges(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().ServiceCharges ?? new List<BIL_ServiceChargeInfo>()).Select(ToModel).OrderBy(s => s.Category).ThenBy(s => s.ServiceName).ToList();
            lock (DataStore.Sync) return DataStore.ServiceCharges.Where(s => s.HospitalId == hospitalId).OrderBy(s => s.Category).ThenBy(s => s.ServiceName).ToList();
        }

        public static ServiceCharge GetServiceCharge(string id)
        {
            if (Api)
            {
                if (string.IsNullOrEmpty(id)) return null;
                return RequestCache.Get("service-charge:" + id, () => ToModel(DocNyle.OrNull(() => cnHelperObj.GetServiceCharge(AppSession.ApiHospitalId, AppSession.ApiUserId, id, AppSession.ApiHashValue).EnsureSuccess())?.ServiceCharge));
            }
            lock (DataStore.Sync) return DataStore.ServiceCharges.FirstOrDefault(s => s.ServiceChargeId == id);
        }

        public static List<InvoiceLineItem> ListLineItems(string invoiceId)
        {
            if (Api)
                return RequestCache.Get("lines:" + invoiceId, () =>
                    (cnHelperObj.ListInvoiceLineItems(AppSession.ApiHospitalId, AppSession.ApiUserId, invoiceId, AppSession.ApiHashValue).EnsureSuccess().LineItems ?? new List<BIL_LineItemInfo>()).Select(ToModel).OrderBy(l => l.SortOrder).ToList());
            lock (DataStore.Sync) return DataStore.LineItems.Where(l => l.InvoiceId == invoiceId).OrderBy(l => l.SortOrder).ToList();
        }

        /// <summary>Adds a line: subtotal − discount% then + tax%, rounded to 2 decimals.</summary>
        public static InvoiceLineItem AddLineItem(string invoiceId, string serviceChargeId, string description, string category, int qty, decimal unit, decimal discountPercent, decimal taxPercent,
            string inventoryItemId = null)
        {
            if (Api)
            {
                // The server computes discount, tax and line total; totals change with RecalculateTotals.
                var l = cnHelperObj.CreateInvoiceLineItem(new BIL_CreateInvoiceLineItemReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    InvoiceId = invoiceId, ServiceChargeId = serviceChargeId, InventoryItemId = inventoryItemId, Description = description, Category = category,
                    Qty = Math.Max(1, qty), UnitAmount = unit, DiscountPercent = discountPercent, TaxPercent = taxPercent,
                }).EnsureSuccess().LineItem;
                Forget(invoiceId);
                return l != null ? ToModel(l) : null;
            }
            lock (DataStore.Sync)
            {
                if (qty < 1) qty = 1;
                var subtotal = qty * unit;
                var disc = R2(subtotal * discountPercent / 100m);
                var tax = R2((subtotal - disc) * taxPercent / 100m);
                var row = new InvoiceLineItem
                {
                    LineItemId = DataStore.NextId("LI"), InvoiceId = invoiceId, ServiceChargeId = serviceChargeId, InventoryItemId = inventoryItemId, Description = description,
                    Category = category, Qty = qty, UnitAmount = unit, DiscountPercent = discountPercent, DiscountAmount = disc,
                    TaxPercent = taxPercent, TaxAmount = tax, LineTotal = R2(subtotal - disc + tax),
                    SortOrder = DataStore.LineItems.Count(l => l.InvoiceId == invoiceId) + 1
                };
                DataStore.LineItems.Add(row);
                return row;
            }
        }

        public static void RemoveLineItem(string lineItemId, string invoiceId = null)
        {
            if (Api)
            {
                cnHelperObj.DeleteInvoiceLineItem(new BIL_DeleteInvoiceLineItemReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, LineItemId = lineItemId, InvoiceId = invoiceId }).EnsureSuccess();
                if (invoiceId != null) Forget(invoiceId);
                return;
            }
            lock (DataStore.Sync) DataStore.LineItems.RemoveAll(l => l.LineItemId == lineItemId);
        }

        /// <summary>Recomputes the invoice totals from its line items, then re-derives the coverage split and status.</summary>
        public static Invoice RecalculateTotals(string invoiceId)
        {
            if (Api)
            {
                var inv = ToModel(cnHelperObj.RecalculateInvoiceTotals(new BIL_InvoiceIdReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, InvoiceId = invoiceId }).EnsureSuccess().Invoice);
                Forget(invoiceId);
                return inv;
            }
            lock (DataStore.Sync)
            {
                var inv = GetInvoice(invoiceId);
                if (inv == null) return null;
                var items = DataStore.LineItems.Where(l => l.InvoiceId == invoiceId).ToList();
                inv.SubTotal = R2(items.Sum(l => l.Qty * l.UnitAmount));
                inv.DiscountAmount = R2(items.Sum(l => l.DiscountAmount));
                inv.TaxAmount = R2(items.Sum(l => l.TaxAmount));
                inv.TotalAmount = R2(items.Sum(l => l.LineTotal));
                inv.ModifiedAt = DateTime.UtcNow;
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
            if (Api) return ApiSplit(invoiceId);
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
            if (Api)
            {
                var split = ApiSplit(invoiceId);
                return split != null ? split.Lines.FirstOrDefault(l => l.CoverageId == coverageId) : null;
            }
            lock (DataStore.Sync) return DataStore.CoverageLines.FirstOrDefault(l => l.InvoiceId == invoiceId && l.CoverageId == coverageId);
        }

        public static List<EligibilityVerification> ListVerifications(string coverageId)
        {
            if (Api)
                return (cnHelperObj.ListEligibilityVerifications(AppSession.ApiHospitalId, AppSession.ApiUserId, coverageId, AppSession.ApiHashValue).EnsureSuccess().Verifications ?? new List<BIL_EligibilityVerificationInfo>()).Select(v => new EligibilityVerification
                {
                    VerificationId = v.VerificationId, CoverageId = v.CoverageId, InvoiceId = v.InvoiceId, Status = v.Status, AuthorizationNumber = v.AuthorizationNumber,
                    ApprovedType = v.ApprovedType, ApprovedValue = v.ApprovedValue, Notes = v.Notes, VerifiedBy = v.VerifiedBy, VerifiedAt = DocNyle.InstantOrMin(v.VerifiedAt),
                }).OrderByDescending(v => v.VerifiedAt).ToList();
            lock (DataStore.Sync) return DataStore.Verifications.Where(v => v.CoverageId == coverageId).OrderByDescending(v => v.VerifiedAt).ToList();
        }

        /// <summary>
        /// Records the payer's eligibility response for this invoice. VERIFIED applies (or updates) a liability line;
        /// PENDING/REJECTED removes it — unless the payer has already settled against it (returns false).
        /// </summary>
        public static bool ApplyCoverage(string invoiceId, string coverageId, string status, string authNo, string approvedType, decimal approvedValue, string notes, string userId)
        {
            if (Api)
            {
                // 50121 cancelled invoice, 50122 payer already settled against the line (Conflict) → false.
                try
                {
                    cnHelperObj.ApplyCoverageToInvoice(new BIL_ApplyCoverageToInvoiceReq
                    {
                        HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                        InvoiceId = invoiceId, CoverageId = coverageId, Status = status, AuthorizationNumber = authNo,
                        ApprovedType = approvedType == "AMOUNT" ? "AMOUNT" : "PERCENT", ApprovedValue = Math.Max(0, approvedValue), Notes = notes,
                    }).EnsureSuccess();
                }
                catch (ApiException ex) when (ex.Kind == ApiFaultKind.Conflict || ex.Kind == ApiFaultKind.NotFound) { return false; }
                Forget(invoiceId);
                return true;
            }
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
                    ApprovedType = approvedType, ApprovedValue = approvedValue, Notes = notes, VerifiedBy = userId, VerifiedAt = DateTime.UtcNow
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
            if (Api)
            {
                cnHelperObj.EditInvoiceCoverageLine(new BIL_EditInvoiceCoverageLineReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, CoverageLineId = coverageLineId, Liability = Math.Max(0, amount) }).EnsureSuccess();
                RequestCache.RemovePrefix("split:");
                RequestCache.RemovePrefix("invoice:");
                return;
            }
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
            if (Api)
            {
                try { cnHelperObj.RemoveInvoiceCoverageLine(new BIL_RemoveInvoiceCoverageLineReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, CoverageLineId = coverageLineId }).EnsureSuccess(); }
                catch (ApiException ex) when (ex.Kind == ApiFaultKind.Conflict) { return false; }
                RequestCache.RemovePrefix("split:");
                RequestCache.RemovePrefix("invoice:");
                return true;
            }
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
            if (Api) return (cnHelperObj.ListPaymentsForInvoice(AppSession.ApiHospitalId, AppSession.ApiUserId, invoiceId, AppSession.ApiHashValue).EnsureSuccess().Payments ?? new List<BIL_PaymentInfo>()).Select(ToModel).OrderBy(p => p.PaidAt).ToList();
            lock (DataStore.Sync) return DataStore.Payments.Where(p => p.InvoiceId == invoiceId).OrderBy(p => p.PaidAt).ToList();
        }

        public static List<Payment> ListHospitalPayments(string hospitalId)
        {
            if (Api) return (cnHelperObj.ListPayments(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Payments ?? new List<BIL_PaymentInfo>()).Select(ToModel).ToList();
            lock (DataStore.Sync) return DataStore.Payments.Where(p => p.HospitalId == hospitalId).ToList();
        }

        public static Payment RecordPayment(string invoiceId, string coverageLineId, string method, decimal amount, string reference, string userId)
        {
            if (Api) return ApiRecordPayment(invoiceId, coverageLineId, method, amount, reference);
            lock (DataStore.Sync)
            {
                var inv = GetInvoice(invoiceId);
                var row = new Payment
                {
                    TransactionId = DataStore.NextId("PAY"), TransactionNumber = "TXN-" + (DataStore.Payments.Count + 1001).ToString("000000"),
                    InvoiceId = invoiceId, HospitalId = inv.HospitalId, ComponentType = coverageLineId == null ? "PATIENT" : "INSURANCE",
                    CoverageLineId = coverageLineId, PaymentMethod = method, Amount = amount, Reference = reference, ReceivedBy = userId, PaidAt = DateTime.UtcNow
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
            if (Api)
            {
                try { cnHelperObj.UpdatePaymentVoid(new BIL_UpdatePaymentVoidReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, PaymentId = transactionId, VoidReason = reason }).EnsureSuccess(); }
                catch (ApiException ex) when (ex.Kind == ApiFaultKind.Conflict || ex.Kind == ApiFaultKind.NotFound) { return false; }
                RequestCache.RemovePrefix("invoice:");
                RequestCache.RemovePrefix("split:");
                return true;
            }
            lock (DataStore.Sync)
            {
                var p = DataStore.Payments.FirstOrDefault(x => x.TransactionId == transactionId);
                if (p == null || p.IsVoided) return false;
                p.IsVoided = true; p.VoidedAt = DateTime.UtcNow; p.VoidedBy = userId; p.VoidReason = reason;
                RecomputeSplit(GetInvoice(p.InvoiceId));
                return true;
            }
        }

        public static Receipt GetReceipt(string invoiceId)
        {
            if (Api)
            {
                var rc = DocNyle.OrNull(() => cnHelperObj.GetReceiptForInvoice(AppSession.ApiHospitalId, AppSession.ApiUserId, invoiceId, AppSession.ApiHashValue).EnsureSuccess())?.Receipt;
                return rc == null ? null : new Receipt
                {
                    ReceiptId = rc.ReceiptId, ReceiptNumber = rc.ReceiptNumber, InvoiceId = rc.InvoiceId, HospitalId = CallerHospital, PatientId = rc.PatientId,
                    TotalAmount = rc.TotalAmount, Currency = rc.Currency, GeneratedAt = DocNyle.InstantOrMin(rc.GeneratedAt),
                };
            }
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
                InvoiceId = inv.InvoiceId, HospitalId = inv.HospitalId, PatientId = inv.PatientId, TotalAmount = inv.TotalAmount, Currency = inv.Currency, GeneratedAt = DateTime.UtcNow
            };
            DataStore.Receipts.Add(r);
            return r;
        }

        private static bool Api { get { return DataSource.IsApi("Billing"); } }

        private static string CallerHospital { get { return AppSession.ApiUser != null ? AppSession.ApiUser.HospitalId : null; } }

        private static ConsultationCharge ToModel(BIL_ConsultationChargeInfo c)
        {
            if (c == null) return null;
            return new ConsultationCharge
            {
                ConsultationType = c.ConsultationType, AppointmentTypeName = c.AppointmentTypeName, DurationMinutes = c.DurationMinutes,
                ChargeModel = c.ChargeModel, FeeAmount = c.FeeAmount, IsFreeFollowup = c.IsFreeFollowup, Basis = c.Basis ?? "", Note = c.Note ?? "",
                Description = c.Description,
            };
        }

        private static Invoice ToModel(BIL_InvoiceInfo i)
        {
            if (i == null) return null;
            return new Invoice
            {
                InvoiceId = i.InvoiceId, InvoiceNumber = i.InvoiceNumber, HospitalId = CallerHospital, ConsultationId = i.ConsultationId,
                PatientId = i.PatientId, DoctorId = i.DoctorId, ConsultationType = i.ConsultationType, IsFreeFollowup = i.IsFreeFollowup,
                ChargeNote = i.ChargeNote, Currency = i.Currency, SubTotal = i.SubTotal, DiscountAmount = i.DiscountAmount, TaxAmount = i.TaxAmount,
                TotalAmount = i.TotalAmount, CoveredAmount = i.CoveredAmount, PatientLiability = i.PatientLiability, PatientPaidAmount = i.PatientPaidAmount,
                PaidAmount = i.PaidAmount, Status = i.Status, CreatedAt = DocNyle.InstantOrMin(i.CreatedAt),
            };
        }

        private static ServiceCharge ToModel(BIL_ServiceChargeInfo s)
        {
            if (s == null) return null;
            return new ServiceCharge
            {
                ServiceChargeId = s.ServiceChargeId, HospitalId = CallerHospital, ServiceCode = s.ServiceCode, ServiceName = s.ServiceName,
                Category = s.Category, DefaultAmount = s.DefaultAmount, TaxPercent = s.TaxPercent, IsActive = s.IsActive,
            };
        }

        private static InvoiceLineItem ToModel(BIL_LineItemInfo l)
        {
            return new InvoiceLineItem
            {
                LineItemId = l.LineItemId, InvoiceId = l.InvoiceId, ServiceChargeId = l.ServiceChargeId, InventoryItemId = l.InventoryItemId,
                Description = l.Description, Category = l.Category, Qty = l.Qty, UnitAmount = l.UnitAmount, DiscountPercent = l.DiscountPercent,
                DiscountAmount = l.DiscountAmount, TaxPercent = l.TaxPercent, TaxAmount = l.TaxAmount, LineTotal = l.LineTotal, SortOrder = l.SortOrder,
            };
        }

        private static Payment ToModel(BIL_PaymentInfo p)
        {
            return new Payment
            {
                TransactionId = p.TransactionId, TransactionNumber = p.TransactionNumber, InvoiceId = p.InvoiceId, HospitalId = CallerHospital,
                ComponentType = p.ComponentType, PaymentMethod = p.PaymentMethod, Amount = p.Amount, Reference = p.Reference,
                PaidAt = DocNyle.InstantOrMin(p.PaidAt), IsVoided = p.IsVoided, VoidReason = p.VoidReason,
            };
        }

        private static BillingSplit ToModel(BIL_BillingSplitInfo s)
        {
            if (s == null) return null;
            return new BillingSplit
            {
                InvoiceId = s.InvoiceId, Currency = s.Currency, GrossAmount = s.GrossAmount, InsuranceLiability = s.InsuranceLiability,
                GovtLiability = s.GovtLiability, CoveredAmount = s.CoveredAmount, PatientLiability = s.PatientLiability, PatientPaid = s.PatientPaid,
                PatientDue = s.PatientDue, PatientOverpaid = s.PatientOverpaid, PayerOutstanding = s.PayerOutstanding,
                Lines = (s.Lines ?? new List<BIL_CoverageLineInfo>()).Select(l => new InvoiceCoverageLine
                {
                    CoverageLineId = l.CoverageLineId, InvoiceId = s.InvoiceId, CoverageId = l.CoverageId, CoverageType = l.CoverageType, PayerId = l.PayerId,
                    PayerName = l.PayerName, Priority = l.Priority, AuthorizationNumber = l.AuthorizationNumber, ApprovedType = l.ApprovedType,
                    ApprovedValue = l.ApprovedValue, IsManualAmount = l.IsManualAmount, Liability = l.Liability, SettledAmount = l.SettledAmount, Status = l.Status,
                }).OrderBy(l => l.Priority).ToList(),
            };
        }

        private static Invoice ApiGetInvoice(string invoiceId)
        {
            if (string.IsNullOrEmpty(invoiceId)) return null;
            return RequestCache.Get("invoice:" + invoiceId, () => ToModel(DocNyle.OrNull(() => cnHelperObj.GetInvoice(AppSession.ApiHospitalId, AppSession.ApiUserId, invoiceId, AppSession.ApiHashValue).EnsureSuccess())?.Invoice));
        }

        private static void Forget(string invoiceId)
        {
            RequestCache.Remove("invoice:" + invoiceId);
            RequestCache.Remove("split:" + invoiceId);
            RequestCache.Remove("lines:" + invoiceId);
        }

        private static BillingSplit ApiSplit(string invoiceId)
        {
            return RequestCache.Get("split:" + invoiceId, () => ToModel(DocNyle.OrNull(() => cnHelperObj.GetInvoiceBillingSplit(AppSession.ApiHospitalId, AppSession.ApiUserId, invoiceId, AppSession.ApiHashValue).EnsureSuccess())?.BillingSplit));
        }

        /// <summary>
        /// CreateInvoice + one CreateInvoiceLineItem per line (consultation fee, each recorded service, each stocked item used)
        /// + RecalculateInvoiceTotals. The stocked item's price needs admin_inventory (GetInventoryItem); without it the item
        /// line is left out and the user is told.
        /// </summary>
        private static Invoice ApiGenerateInvoice(string consultationId)
        {
            var existing = ToModel(cnHelperObj.GetInvoiceForConsultation(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().Invoice);
            if (existing != null) return existing;
            var c = ConsultationService.Get(consultationId);
            if (c == null) return null;
            var charge = ToModel(cnHelperObj.GetConsultationCharge(AppSession.ApiHospitalId, AppSession.ApiUserId, consultationId, AppSession.ApiHashValue).EnsureSuccess().ConsultationCharge) ?? new ConsultationCharge { ConsultationType = "NEW", Description = "Consultation", Note = "" };
            var inv = ToModel(cnHelperObj.CreateInvoice(new BIL_CreateInvoiceReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                ConsultationId = consultationId, PatientId = c.PatientId, DoctorId = c.DoctorId, ConsultationType = charge.ConsultationType,
                IsFreeFollowup = charge.IsFreeFollowup, ChargeNote = charge.Note,
            }).EnsureSuccess().Invoice);
            Action<string, string, string, int, decimal, decimal, string> add = (svcId, desc, cat, qty, unit, tax, itemId) =>
                cnHelperObj.CreateInvoiceLineItem(new BIL_CreateInvoiceLineItemReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    InvoiceId = inv.InvoiceId, ServiceChargeId = svcId, Description = desc, Category = cat, Qty = Math.Max(1, qty), UnitAmount = unit,
                    DiscountPercent = 0, TaxPercent = tax, InventoryItemId = itemId,
                }).EnsureSuccess();
            add(null, charge.Description, "CONSULTATION", 1, charge.FeeAmount, 0, null);
            var skipped = new List<string>();
            foreach (var item in ConsultationService.ListServiceItems(consultationId))
            {
                var svc = GetServiceCharge(item.ServiceChargeId);
                if (svc == null) continue;
                add(svc.ServiceChargeId, svc.ServiceName + (string.IsNullOrEmpty(item.Notes) ? "" : " — " + item.Notes), svc.Category, item.Qty, svc.DefaultAmount, svc.TaxPercent, null);
                if (item.InventoryItemId == null) continue;
                InventoryItem stock = null;
                try { stock = InventoryService.GetItem(item.InventoryItemId); }
                catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden) { }
                if (stock != null)
                    add(null, stock.ItemName + " (for " + svc.ServiceName + ")", stock.Category, item.ItemQty ?? 1, stock.UnitPrice, stock.TaxPercent, stock.InventoryItemId);
                else skipped.Add(item.ItemName);
            }
            if (skipped.Count > 0)
                AppSession.Toast("warning", "Stock items not priced", string.Join(", ", skipped) + " — add them to the invoice manually (inventory prices need Clinic Inventory access).");
            return ToModel(cnHelperObj.RecalculateInvoiceTotals(new BIL_InvoiceIdReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, InvoiceId = inv.InvoiceId }).EnsureSuccess().Invoice) ?? inv;
        }

        /// <summary>CreatePayment recalculates the invoice; once it is PAID a receipt is issued (CreateReceipt) if there is none yet.</summary>
        private static Payment ApiRecordPayment(string invoiceId, string coverageLineId, string method, decimal amount, string reference)
        {
            var p = cnHelperObj.CreatePayment(new BIL_CreatePaymentReq
            {
                HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                InvoiceId = invoiceId, ComponentType = coverageLineId == null ? "PATIENT" : "INSURANCE", CoverageLineId = coverageLineId,
                PaymentMethod = method, Amount = amount, Reference = reference,
            }).EnsureSuccess().Payment;
            Forget(invoiceId);
            var inv = ApiGetInvoice(invoiceId);
            if (inv != null && inv.Status == "PAID" && DocNyle.OrNull(() => cnHelperObj.GetReceiptForInvoice(AppSession.ApiHospitalId, AppSession.ApiUserId, invoiceId, AppSession.ApiHashValue).EnsureSuccess())?.Receipt == null)
                cnHelperObj.CreateReceipt(new BIL_CreateReceiptReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, InvoiceId = invoiceId, PatientId = inv.PatientId, TotalAmount = inv.TotalAmount, Currency = inv.Currency }).EnsureSuccess();
            var payment = p != null ? ToModel(p) : new Payment { InvoiceId = invoiceId, Amount = amount, PaymentMethod = method, Reference = reference, PaidAt = DateTime.UtcNow };
            payment.CoverageLineId = coverageLineId;
            return payment;
        }
    }

    /// <summary>Day-end reconciliation totals (GetDayEndSummary).</summary>
    public static class DayEndService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        public class Summary
        {
            public decimal TotalBilled { get; set; }
            public decimal TotalCollected { get; set; }
            public decimal TotalOutstanding { get; set; }
            public int InvoiceCount { get; set; }
            public List<RPT_PaymentComponentInfo> ByComponent { get; set; }
        }

        public static Summary GetSummary(DateTime date)
        {
            var s = cnHelperObj.GetDayEndSummary(AppSession.ApiHospitalId, AppSession.ApiUserId, DocNyle.Date(date), AppSession.ApiHashValue).EnsureSuccess().Summary;
            if (s == null) return null;
            return new Summary
            {
                TotalBilled = s.TotalBilled, TotalCollected = s.TotalCollected, TotalOutstanding = s.TotalOutstanding, InvoiceCount = s.InvoiceCount,
                ByComponent = s.ByComponent ?? new List<RPT_PaymentComponentInfo>(),
            };
        }
    }
}
