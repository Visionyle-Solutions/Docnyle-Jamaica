using System;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>
    /// Services &amp; procedures done in the visit, picked from the Service Charge Master (consultation-fee items are left out:
    /// sign-off bills the fee itself). The doctor never sees prices; sign-off adds each pick to the invoice.
    /// </summary>
    public partial class consultation_services : ConsultationSection
    {
        protected override string SectionKey { get { return "services"; } }

        protected override void ApplyFormConfig()
        {
            NotesHolder.Visible = FieldOn("notes");
            SkipServicesButton.Visible = FieldOn("skip");
        }

        public override void Bind()
        {
            FormPanel.Visible = !Host.IsLocked;
            ServicePick.Items.Clear();
            ServicePick.Items.Add(new ListItem("— Select —", ""));
            foreach (var s in BillingService.ListServiceCharges(HospitalId).Where(s => s.Category != "CONSULTATION").OrderBy(s => s.ServiceName))
                ServicePick.Items.Add(new ListItem(s.ServiceName + " (" + BasePage.TitleCase(s.Category) + ")", s.ServiceChargeId));
            NoServicesHint.Visible = ServicePick.Items.Count == 1;
            PickerHolder.Visible = ServicePick.Items.Count > 1;
            ServiceQty.Text = "1";

            var items = ConsultationService.ListServiceItems(ConsultationId);
            ServicesRepeater.DataSource = items;
            ServicesRepeater.DataBind();
            BillingNote.Visible = items.Count > 0;
            var invoice = Host.IsLocked ? BillingService.GetInvoiceForConsultation(ConsultationId) : null;
            BillingNoteText.Text = invoice != null ? "Billed on invoice " + invoice.InvoiceNumber + "."
                : "Added to the invoice at sign-off; billing sets the final amount.";
        }

        protected void AddServiceButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("services");
            if (!EnsureNotLocked()) return;
            Page.Validate("Services");
            if (!Page.IsValid) return;
            var svc = BillingService.GetServiceCharge(ServicePick.SelectedValue);
            if (svc == null || svc.HospitalId != HospitalId || !svc.IsActive || svc.Category == "CONSULTATION") return;
            int qty;
            if (!int.TryParse(ServiceQty.Text, out qty)) qty = 1;
            var row = ConsultationService.AddServiceItem(Visit, svc, qty, FieldOn("notes") ? ServiceNotes.Text.Trim() : "", Host.DoctorId);
            Audit("CREATE", "ConsultationService", ConsultationService.ServiceItemLabel(row));
            ServiceNotes.Text = "";
            AppPage.ToastSuccess("Service added", ConsultationService.ServiceItemLabel(row));
            Host.RefreshAll();
        }

        protected void ServicesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "Remove" || !EnsureNotLocked()) return;
            var row = ConsultationService.ListServiceItems(ConsultationId).FirstOrDefault(s => s.ConsultationServiceId == (string)e.CommandArgument);
            if (row == null) return;
            ConsultationService.RemoveServiceItem(ConsultationId, row.ConsultationServiceId);
            Audit("DELETE", "ConsultationService", ConsultationService.ServiceItemLabel(row));
            AppPage.ToastSuccess("Service removed", row.ServiceName);
            Host.ActivateStep("services");
            Host.RefreshAll();
        }

        protected void SkipServicesButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked()) return;
            ConsultationService.SkipStep(ConsultationId, "services");
            AppPage.ToastSuccess("Marked as not needed");
            Host.ActivateStep("referral");
            Host.RefreshAll();
        }
    }
}
