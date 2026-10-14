using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.billing
{
    /// <summary>Server-rendered invoice / tax invoice + receipt with the bill split and payment list.</summary>
    public partial class invoice_print : BasePage
    {
        protected override string RequiredPermission { get { return "billing"; } }
        public override string PageTitle { get { return "Invoice"; } }

        private string _currency;

        private static string H(string s) { return HttpUtility.HtmlEncode(s ?? ""); }

        /// <summary>HTML-encoded amount with the invoice's currency symbol (e.g. J$300.00).</summary>
        private string M(decimal d) { return H(Money(d, _currency)); }

        protected void Page_Load(object sender, EventArgs e)
        {
            var inv = BillingService.GetInvoice(RouteValue("invoiceId"));
            if (inv == null || inv.HospitalId != HospitalId) { InvoiceHtml.Text = "<p>Invoice not found.</p>"; return; }
            _currency = inv.Currency;
            var patient = PatientService.Get(inv.PatientId);
            var items = BillingService.ListLineItems(inv.InvoiceId);
            var payments = BillingService.ListPayments(inv.InvoiceId).Where(p => !p.IsVoided).ToList();
            var split = BillingService.GetBillingSplit(inv.InvoiceId);
            var receipt = inv.Status == "PAID" ? BillingService.GetReceipt(inv.InvoiceId) : null;
            var scheme = PatientService.GetGovtSchemeProfile(Hospital.Country);
            var covered = split.Lines.Where(l => l.Status == "VERIFIED" && l.Liability > 0).ToList();
            var compliance = HospitalService.ComplianceLine(HospitalId);

            var sb = new StringBuilder();
            sb.Append("<div style=\"text-align:center;margin-bottom:16px;\"><h2 style=\"margin:0;\">" + H(Hospital.Name) + "</h2><div>" + H(Hospital.LegalAddress) + "</div>"
                + "<div>Tel: " + H(Hospital.ContactNumber) + "</div>" + (compliance.Length > 0 ? "<div style=\"font-size:10px;\">" + H(compliance) + "</div>" : "") + "</div>");
            /* "Tax invoice" is GST wording (India); elsewhere — e.g. Jamaica, where consultations are typically tax-exempt — a plain invoice / receipt */
            sb.Append("<h3>" + (inv.Status == "PAID" ? (Hospital.Country == "IN" ? "TAX INVOICE / RECEIPT" : "INVOICE / RECEIPT") : inv.Status == "CANCELLED" ? "INVOICE (CANCELLED)" : "INVOICE") + "</h3>");
            sb.Append("<table class=\"no-border\"><tr><td style=\"width:50%;\"><strong>Invoice #:</strong> " + H(inv.InvoiceNumber) + "<br/><strong>Date:</strong> " + H(FmtDate(inv.CreatedAt))
                + "<br/><strong>Doctor:</strong> " + H(UserService.UserName(inv.DoctorId)) + "</td><td><strong>Patient:</strong> " + H(patient.FullName) + "<br/><strong>" + H(PatientIdLabel) + ":</strong> " + H(patient.Mrn)
                + (string.IsNullOrEmpty(patient.Phone) ? "" : "<br/><strong>Phone:</strong> " + H(patient.Phone)) + "</td></tr></table>");
            sb.Append("<table><thead><tr><th>#</th><th>Description</th><th>Category</th><th>Qty</th><th>Unit Price</th><th>Discount</th><th>Tax</th><th>Total</th></tr></thead><tbody>");
            int i = 0;
            foreach (var it in items)
                sb.Append("<tr><td>" + (++i) + "</td><td>" + H(it.Description) + "</td><td>" + H(it.Category) + "</td><td style=\"text-align:right;\">" + it.Qty + "</td><td style=\"text-align:right;\">" + M(it.UnitAmount)
                    + "</td><td style=\"text-align:right;\">" + (it.DiscountAmount > 0 ? "-" + M(it.DiscountAmount) : "-") + "</td><td style=\"text-align:right;\">" + (it.TaxAmount > 0 ? M(it.TaxAmount) : "-") + "</td><td style=\"text-align:right;font-weight:bold;\">" + M(it.LineTotal) + "</td></tr>");
            sb.Append("</tbody><tfoot>");
            if (inv.DiscountAmount > 0) sb.Append("<tr><td colspan=\"7\" style=\"text-align:right;\">Discount</td><td style=\"text-align:right;\">-" + M(inv.DiscountAmount) + "</td></tr>");
            if (inv.TaxAmount > 0) sb.Append("<tr><td colspan=\"7\" style=\"text-align:right;\">Tax</td><td style=\"text-align:right;\">" + M(inv.TaxAmount) + "</td></tr>");
            sb.Append("<tr><td colspan=\"7\" style=\"text-align:right;\">Grand Total</td><td style=\"text-align:right;\">" + M(inv.TotalAmount) + "</td></tr></tfoot></table>");
            if (covered.Count > 0)
            {
                sb.Append("<h4>Bill split</h4><table><thead><tr><th>Liability</th><th>Payer</th><th>Authorization</th><th style=\"text-align:right;\">Amount</th></tr></thead><tbody>");
                foreach (var l in covered)
                    sb.Append("<tr><td>" + H(l.CoverageType == "GOVT_SCHEME" ? scheme.Label : "Insurance") + "</td><td>" + H(l.PayerName) + "</td><td>" + H(string.IsNullOrEmpty(l.AuthorizationNumber) ? "—" : l.AuthorizationNumber) + "</td><td style=\"text-align:right;\">" + M(l.Liability) + "</td></tr>");
                sb.Append("<tr><td>Patient</td><td colspan=\"2\"></td><td style=\"text-align:right;\">" + M(split.PatientLiability) + "</td></tr></tbody></table>");
            }
            if (payments.Count > 0)
            {
                sb.Append("<h4>Payments</h4><table><thead><tr><th>Txn #</th><th>Payment Mode</th><th>Component</th><th>Date</th><th style=\"text-align:right;\">Amount</th></tr></thead><tbody>");
                foreach (var p in payments)
                    sb.Append("<tr><td>" + H(p.TransactionNumber) + "</td><td>" + H(p.PaymentMethod.Replace("_", " ")) + "</td><td>" + H(p.ComponentType) + "</td><td>" + H(FmtDateTime(p.PaidAt)) + "</td><td style=\"text-align:right;\">" + M(p.Amount) + "</td></tr>");
                sb.Append("</tbody><tfoot><tr><td colspan=\"4\" style=\"text-align:right;\">Total Paid</td><td style=\"text-align:right;\">" + M(inv.PaidAmount) + "</td></tr></tfoot></table>");
            }
            sb.Append(receipt != null
                ? "<div style=\"text-align:center;margin-top:16px;padding:12px;border:2px solid #333;\"><div style=\"font-size:16px;font-weight:bold;color:green;\">" + (covered.Count > 0 ? "PATIENT SHARE PAID" : "PAID IN FULL") + "</div><div>Receipt #: " + H(receipt.ReceiptNumber) + " · Date: " + H(FmtDateTime(receipt.GeneratedAt)) + "</div>"
                  + (split.PayerOutstanding > 0 ? "<div>Receivable from payer(s): " + M(split.PayerOutstanding) + "</div>" : "") + "</div>"
                : "<div style=\"margin-top:12px;\"><strong>Patient Due: " + M(split.PatientDue) + "</strong></div>");
            sb.Append("<div style=\"margin-top:30px;font-size:10px;color:#888;\">Printed by " + H(CurrentUser.FullName) + " on " + H(FmtDateTime(DateTime.Now)) + "</div>");
            InvoiceHtml.Text = sb.ToString();
            Page.Title = "Invoice " + inv.InvoiceNumber;
            Audit("PRINT", "Invoice", inv.InvoiceId, inv.InvoiceNumber);
        }
    }
}