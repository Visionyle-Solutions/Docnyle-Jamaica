using System;
using System.Linq;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.Web
{
    public class BalanceRow
    {
        public string PatientName { get; set; }
        public string Mrn { get; set; }
        public string Phone { get; set; }
        public int InvoiceCount { get; set; }
        public decimal Billed { get; set; }
        public decimal Paid { get; set; }
        public decimal Outstanding { get { return Billed - Paid; } }
        public string LatestConsultationId { get; set; }
    }
}

namespace OpdProject.templates.billing
{
    /// <summary>Per-patient balance = Σ patient share − Σ patient payments over non-cancelled invoices.</summary>
    public partial class patient_balances : BasePage
    {
        protected override string RequiredPermission { get { return "billing"; } }
        public override string PageTitle { get { return "Outstanding Balances"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            var rows = BillingService.ListInvoices(HospitalId).Where(i => i.Status != "CANCELLED")
                .GroupBy(i => i.PatientId)
                .Select(g =>
                {
                    var p = PatientService.Get(g.Key);
                    return new BalanceRow
                    {
                        PatientName = p != null ? p.FullName : "Unknown", Mrn = p != null ? p.Mrn : "", Phone = p != null ? p.Phone : "",
                        InvoiceCount = g.Count(), Billed = g.Sum(i => i.PatientLiability), Paid = g.Sum(i => i.PatientPaidAmount),
                        LatestConsultationId = (g.OrderByDescending(i => i.CreatedAt).FirstOrDefault(i => i.PatientLiability > i.PatientPaidAmount) ?? g.First()).ConsultationId,
                    };
                })
                .Where(r => r.Outstanding > 0).OrderByDescending(r => r.Outstanding).ToList();
            BalancesRepeater.DataSource = rows;
            BalancesRepeater.DataBind();
            BalancesRepeater.Visible = rows.Count > 0;
            EmptyHolder.Visible = rows.Count == 0;
            TotalText.Text = Server.HtmlEncode(Money(rows.Sum(r => r.Outstanding)));
        }
    }
}
