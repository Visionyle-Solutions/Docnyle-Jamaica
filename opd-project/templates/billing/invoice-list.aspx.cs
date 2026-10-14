using System;
using System.Linq;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.billing
{
    /// <summary>All invoices of the hospital, newest first, filterable by status and searchable by number/patient.</summary>
    public partial class invoice_list : BasePage
    {
        protected override string RequiredPermission { get { return "billing"; } }
        public override string PageTitle { get { return "Invoices"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) Bind();
        }

        protected void Filter_Changed(object sender, EventArgs e) { Bind(); }

        protected string PatientName(string patientId)
        {
            var p = PatientService.Get(patientId);
            return p != null ? p.FullName : "";
        }

        private void Bind()
        {
            var term = SearchInput.Text.Trim().ToLowerInvariant();
            var rows = BillingService.ListInvoices(HospitalId)
                .Where(i => StatusFilter.SelectedValue == "" || i.Status == StatusFilter.SelectedValue)
                .Where(i => term.Length == 0 || i.InvoiceNumber.ToLowerInvariant().Contains(term) || PatientName(i.PatientId).ToLowerInvariant().Contains(term))
                .ToList();
            InvoicesRepeater.DataSource = rows;
            InvoicesRepeater.DataBind();
            EmptyRow.Visible = rows.Count == 0;
        }
    }
}
