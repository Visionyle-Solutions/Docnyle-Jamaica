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

        private static string H(string s) { return HttpUtility.HtmlEncode(s ?? ""); }
        private static string N(decimal d) { return d.ToString("N2", CultureInfo.InvariantCulture); }

        protected void Page_Load(object sender, EventArgs e)
        {
            var inv = BillingService.GetInvoice(RouteValue("invoiceId"));
            if (inv == null || inv.HospitalId != HospitalId) { InvoiceHtml.Text = "<p>Invoice not found.</p>"; return; }
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
            sb.Append("<h3>" + (inv.Status == "PAID" ? (Hospital.Country == "IN" ? "Tax Invoice / Receipt" : "Invoice / Receipt") : inv.Status == "CANCELLED" ? "Invoice (Cancelled)" : "Invoice") + "</h3>");
            sb.Append("<table class=\"no-border\"><tr><td style=\"width:50%;\"><strong>Invoice #:</strong> " + H(inv.InvoiceNumber) + "<br/><strong>Date:</strong> " + H(FmtDate(inv.CreatedAt))
                + "<br/><strong>Doctor:</strong> " + H(UserService.UserName(inv.DoctorId)) + "</td><td><strong>Patient:</strong> " + H(patient.FullName) + "<br/><strong>" + H(PatientIdLabel) + ":</strong> " + H(patient.Mrn)
                + (string.IsNullOrEmpty(patient.Phone) ? "" : "<br/><strong>Phone:</strong> " + H(patient.Phone)) + "</td></tr></table>");
            sb.Append("<table><thead><tr><th>#</th><th>Description</th><th>Category</th><th>Qty</th><th>Unit Price</th><th>Discount</th><th>Tax</th><th>Total</th></tr></thead><tbody>");
            int i = 0;
            foreach (var it in items)
                sb.Append("<tr><td>" + (++i) + "</td><td>" + H(it.Description) + "</td><td>" + H(TitleCase(it.Category)) + "</td><td style=\"text-align:right;\">" + it.Qty + "</td><td style=\"text-align:right;\">" + N(it.UnitAmount)
                    + "</td><td style=\"text-align:right;\">" + (it.DiscountAmount > 0 ? "-" + N(it.DiscountAmount) : "-") + "</td><td style=\"text-align:right;\">" + (it.TaxAmount > 0 ? N(it.TaxAmount) : "-") + "</td><td style=\"text-align:right;font-weight:bold;\">" + N(it.LineTotal) + "</td></tr>");
            sb.Append("</tbody><tfoot>");
            if (inv.DiscountAmount > 0) sb.Append("<tr><td colspan=\"7\" style=\"text-align:right;\">Discount</td><td style=\"text-align:right;\">-" + N(inv.DiscountAmount) + "</td></tr>");
            if (inv.TaxAmount > 0) sb.Append("<tr><td colspan=\"7\" style=\"text-align:right;\">Tax</td><td style=\"text-align:right;\">" + N(inv.TaxAmount) + "</td></tr>");
            sb.Append("<tr><td colspan=\"7\" style=\"text-align:right;\">Grand Total</td><td style=\"text-align:right;\">" + H(inv.Currency) + " " + N(inv.TotalAmount) + "</td></tr></tfoot></table>");
            if (covered.Count > 0)
            {
                sb.Append("<h4>Bill Split</h4><table><thead><tr><th>Liability</th><th>Payer</th><th>Authorization</th><th style=\"text-align:right;\">Amount</th></tr></thead><tbody>");
                foreach (var l in covered)
                    sb.Append("<tr><td>" + H(l.CoverageType == "GOVT_SCHEME" ? scheme.Label : "Insurance") + "</td><td>" + H(l.PayerName) + "</td><td>" + H(string.IsNullOrEmpty(l.AuthorizationNumber) ? "—" : l.AuthorizationNumber) + "</td><td style=\"text-align:right;\">" + N(l.Liability) + "</td></tr>");
                sb.Append("<tr><td>Patient</td><td colspan=\"2\"></td><td style=\"text-align:right;\">" + N(split.PatientLiability) + "</td></tr></tbody></table>");
            }
            if (payments.Count > 0)
            {
                sb.Append("<h4>Payments</h4><table><thead><tr><th>Txn #</th><th>Method</th><th>Component</th><th>Date</th><th style=\"text-align:right;\">Amount</th></tr></thead><tbody>");
                foreach (var p in payments)
                    sb.Append("<tr><td>" + H(p.TransactionNumber) + "</td><td>" + H(TitleCase(p.PaymentMethod)) + "</td><td>" + H(TitleCase(p.ComponentType)) + "</td><td>" + H(FmtDate(p.PaidAt)) + "</td><td style=\"text-align:right;\">" + N(p.Amount) + "</td></tr>");
                sb.Append("</tbody><tfoot><tr><td colspan=\"4\" style=\"text-align:right;\">Total Paid</td><td style=\"text-align:right;\">" + H(inv.Currency) + " " + N(inv.PaidAmount) + "</td></tr></tfoot></table>");
            }
            sb.Append(receipt != null
                ? "<div style=\"text-align:center;margin-top:16px;padding:12px;border:2px solid #333;\"><div style=\"font-size:16px;font-weight:bold;color:green;\">" + (covered.Count > 0 ? "Patient Share Paid" : "Paid In Full") + "</div><div>Receipt #: " + H(receipt.ReceiptNumber) + " · Date: " + H(FmtDate(receipt.GeneratedAt)) + "</div>"
                  + (split.PayerOutstanding > 0 ? "<div>Receivable From Payer(s): " + H(inv.Currency) + " " + N(split.PayerOutstanding) + "</div>" : "") + "</div>"
                : "<div style=\"margin-top:12px;\"><strong>Patient Due: " + H(inv.Currency) + " " + N(split.PatientDue) + "</strong></div>");
            sb.Append("<div style=\"margin-top:30px;font-size:10px;color:#888;\">Printed by " + H(CurrentUser.FullName) + " on " + H(FmtDateTime(DateTime.Now)) + "</div>");
            InvoiceHtml.Text = sb.ToString();
            Page.Title = "Invoice " + inv.InvoiceNumber;
            Audit("PRINT", "Invoice", inv.InvoiceId, inv.InvoiceNumber);
        }
    }
}
