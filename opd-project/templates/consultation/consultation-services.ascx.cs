using System;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>
    /// Services &amp; procedures done in the visit, picked from the Service Charge Master (consultation-fee items are left out:
    /// sign-off bills the fee itself). Picking a service offers the stocked items linked to it in Clinic Inventory; the item is
    /// optional, has its own quantity and can only be picked up to what is still available. The doctor never sees prices;
    /// sign-off bills the service and the item on separate lines and deducts the item's stock.
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
            BindItemPicker();

            var items = ConsultationService.ListServiceItems(ConsultationId);
            ServicesRepeater.DataSource = items;
            ServicesRepeater.DataBind();
            BillingNote.Visible = items.Count > 0;
            var invoice = Host.IsLocked ? BillingService.GetInvoiceForConsultation(ConsultationId) : null;
            BillingNoteText.Text = invoice != null ? "Billed on invoice " + invoice.InvoiceNumber + "."
                : "Added to the invoice at sign-off; billing sets the final amount.";
        }

        /// <summary>Items linked to the selected service, each with what is still available to this visit.</summary>
        private void BindItemPicker()
        {
            ItemPick.Items.Clear();
            ItemQty.Text = "1";
            var linked = string.IsNullOrEmpty(ServicePick.SelectedValue) ? new System.Collections.Generic.List<StockSummary>()
                : InventoryService.ItemsForService(HospitalId, ServicePick.SelectedValue);
            ItemHolder.Visible = linked.Count > 0;
            if (linked.Count == 0) return;
            ItemPick.Items.Add(new ListItem("— None —", ""));
            foreach (var st in linked)
            {
                var left = st.Available - InventoryService.QtyOnConsultation(ConsultationId, st.Item.InventoryItemId);
                var li = new ListItem(st.Item.ItemName + " (" + BasePage.TitleCase(st.Item.Category) + ") — "
                    + (left > 0 ? left + " " + st.Item.Unit + " available" : "out of stock"), st.Item.InventoryItemId);
                if (left <= 0) li.Attributes["disabled"] = "disabled";
                ItemPick.Items.Add(li);
            }
        }

        protected void ServicePick_SelectedIndexChanged(object sender, EventArgs e)
        {
            Host.ActivateStep("services");
            BindItemPicker();
        }

        protected void AddServiceButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("services");
            if (!EnsureNotLocked()) return;
            Page.Validate("Services");
            if (!Page.IsValid) return;
            var svc = BillingService.GetServiceCharge(ServicePick.SelectedValue);
            if (svc == null || svc.HospitalId != HospitalId || !svc.IsActive || svc.Category == "CONSULTATION") return;
            int qty, itemQty;
            if (!int.TryParse(ServiceQty.Text, out qty)) qty = 1;
            if (!int.TryParse(ItemQty.Text, out itemQty)) itemQty = 1;
            var item = PickedItem(svc.ServiceChargeId);
            var row = ConsultationService.AddServiceItem(Visit, svc, qty, FieldOn("notes") ? ServiceNotes.Text.Trim() : "", Host.DoctorId, item, itemQty);
            Audit("CREATE", "ConsultationService", ConsultationService.ServiceItemLabel(row));
            ServiceNotes.Text = "";
            AppPage.ToastSuccess("Service added", ConsultationService.ServiceItemLabel(row));
            Host.RefreshAll();
        }

        /// <summary>The picked item, only if it is active, this hospital's and linked to the service.</summary>
        private InventoryItem PickedItem(string serviceChargeId)
        {
            if (!ItemHolder.Visible || string.IsNullOrEmpty(ItemPick.SelectedValue)) return null;
            var item = InventoryService.GetItem(ItemPick.SelectedValue);
            return item != null && item.HospitalId == HospitalId && item.IsActive && item.ServiceChargeIds.Contains(serviceChargeId) ? item : null;
        }

        /// <summary>The item quantity may not exceed what is available, less what this visit already lists.</summary>
        protected void StockValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = true;
            int qty;
            var item = PickedItem(ServicePick.SelectedValue);
            if (item == null || !int.TryParse(ItemQty.Text, out qty) || qty < 1) return;
            var left = InventoryService.Available(item.InventoryItemId) - InventoryService.QtyOnConsultation(ConsultationId, item.InventoryItemId);
            if (qty <= left) return;
            args.IsValid = false;
            StockValidator.Text = HttpUtility.HtmlEncode(left > 0 ? "Only " + left + " " + item.Unit + " available" : item.ItemName + " is out of stock");
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
