using System;
using System.Linq;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.Web
{
    public class MethodTotal
    {
        public string Method { get; set; }
        public int Count { get; set; }
        public decimal Amount { get; set; }
        public int Percent { get; set; }
    }
}

namespace OpdProject.templates.billing
{
    /// <summary>Day-end closure: billed vs collected (non-voided payments), method breakdown, consultations by type.</summary>
    public partial class day_end : BasePage
    {
        protected override string RequiredPermission { get { return "billing"; } }
        public override string PageTitle { get { return "Day-End Reconciliation"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            DayDate.Text = IsoDate(DateTime.Today);
            Bind();
        }

        protected void DayDate_Changed(object sender, EventArgs e) { Bind(); }

        protected string PatientName(string id)
        {
            var p = PatientService.Get(id);
            return p != null ? p.FullName : "";
        }

        private void Bind()
        {
            var date = ParseDate(DayDate.Text) ?? DateTime.Today;
            var invoices = BillingService.ListInvoices(HospitalId).Where(i => i.CreatedAt.Date == date && i.Status != "CANCELLED").ToList();
            var payments = BillingService.ListHospitalPayments(HospitalId).Where(p => p.PaidAt.Date == date && !p.IsVoided).ToList();
            decimal billed = invoices.Sum(i => i.TotalAmount), collected = payments.Sum(p => p.Amount);
            BilledText.Text = Server.HtmlEncode(Money(billed));
            CollectedText.Text = Server.HtmlEncode(Money(collected));
            var outstanding = invoices.Sum(i => i.TotalAmount - i.PaidAmount);
            OutstandingText.Text = "<span style=\"color:" + (outstanding > 0 ? "var(--color-danger)" : "var(--color-success)") + ";\">" + Server.HtmlEncode(Money(outstanding)) + "</span>";

            var consults = ConsultationService.ListForDate(HospitalId, date);
            int newCount = 0, followCount = 0;
            foreach (var c in consults)
            {
                var appt = SchedulingService.AppointmentForConsultation(c);
                if (appt != null && appt.Type == "FOLLOWUP") followCount++; else newCount++;
            }
            ConsultCount.Text = consults.Count.ToString();
            ConsultSplit.Text = newCount + " new · " + followCount + " follow-up";

            var methods = payments.GroupBy(p => p.PaymentMethod).Select(g => new MethodTotal
            {
                Method = g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount),
                Percent = collected > 0 ? (int)Math.Round(g.Sum(p => p.Amount) * 100 / collected) : 0,
            }).OrderByDescending(m => m.Amount).ToList();
            MethodsRepeater.DataSource = methods;
            MethodsRepeater.DataBind();
            MethodsRepeater.Visible = methods.Count > 0;
            NoPayments.Visible = methods.Count == 0;

            InvoicesRepeater.DataSource = invoices;
            InvoicesRepeater.DataBind();
            InvoicesRepeater.Visible = invoices.Count > 0;
            NoInvoices.Visible = invoices.Count == 0;
            InvoiceCount.Text = invoices.Count.ToString();
        }
    }
}
