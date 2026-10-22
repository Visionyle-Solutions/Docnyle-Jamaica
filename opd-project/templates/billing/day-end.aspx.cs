using System;
using System.Collections.Generic;
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
            DayDate.Text = IsoDate(Today);
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
            var date = ParseDate(DayDate.Text) ?? Today;
            // The day is the hospital's calendar day; invoices and payments are UTC instants.
            var zone = TimeZoneService.HospitalZoneId(HospitalId);
            var invoices = BillingService.ListInvoices(HospitalId).Where(i => TimeZoneService.LocalDate(i.CreatedAt, zone) == date && i.Status != "CANCELLED").ToList();
            var payments = BillingService.ListHospitalPayments(HospitalId).Where(p => TimeZoneService.LocalDate(p.PaidAt, zone) == date && !p.IsVoided).ToList();
            decimal billed = invoices.Sum(i => i.TotalAmount), collected = payments.Sum(p => p.Amount);
            var outstanding = invoices.Sum(i => i.TotalAmount - i.PaidAmount);
            List<MethodTotal> methods = null;
            var summary = DataSource.IsApi("Billing") ? DayEndService.GetSummary(date) : null;
            if (summary != null)
            {
                // GetDayEndSummary: the service's own totals and payment breakdown for the hospital day.
                billed = summary.TotalBilled; collected = summary.TotalCollected; outstanding = summary.TotalOutstanding;
                var total = collected;
                methods = summary.ByComponent.GroupBy(c => c.PaymentMethod).Select(g => new MethodTotal
                {
                    Method = g.Key, Count = g.Sum(c => c.PaymentCount), Amount = g.Sum(c => c.TotalAmount),
                    Percent = total > 0 ? (int)Math.Round(g.Sum(c => c.TotalAmount) * 100 / total) : 0,
                }).OrderByDescending(m => m.Amount).ToList();
            }
            BilledText.Text = Server.HtmlEncode(Money(billed));
            CollectedText.Text = Server.HtmlEncode(Money(collected));
            OutstandingText.Text = "<span style=\"color:" + (outstanding > 0 ? "var(--color-danger)" : "var(--color-success)") + ";\">" + Server.HtmlEncode(Money(outstanding)) + "</span>";

            int newCount = 0, followCount = 0;
            List<Models.Consultation> consults;
            if (DataSource.IsApi("Consultation"))
            {
                // Each visit's appointment needs the scheduling feature, so the split follows each visit's invoice type.
                try { consults = ConsultationService.ListForDate(HospitalId, date); }
                catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden) { consults = new List<Models.Consultation>(); }
                var typeOf = invoices.Where(i => i.ConsultationId != null).GroupBy(i => i.ConsultationId).ToDictionary(g => g.Key, g => g.First().ConsultationType);
                foreach (var c in consults)
                    if (typeOf.ContainsKey(c.ConsultationId) && typeOf[c.ConsultationId] == "FOLLOWUP") followCount++; else newCount++;
            }
            else
            {
                consults = ConsultationService.ListForDate(HospitalId, date);
                foreach (var c in consults)
                {
                    var appt = SchedulingService.AppointmentForConsultation(c);
                    if (appt != null && appt.Type == "FOLLOWUP") followCount++; else newCount++;
                }
            }
            ConsultCount.Text = consults.Count.ToString();
            ConsultSplit.Text = newCount + " New · " + followCount + " Follow-Up";

            methods = methods ?? payments.GroupBy(p => p.PaymentMethod).Select(g => new MethodTotal
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
