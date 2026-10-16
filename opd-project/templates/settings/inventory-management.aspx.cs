using System;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>Clinic inventory: items, stock receipts by batch and expiry, adjustments, and each item's batches and movement history.</summary>
    public partial class inventory_management : BasePage
    {
        protected override string RequiredPermission { get { return "admin_inventory"; } }
        public override string PageTitle { get { return "Clinic Inventory"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            foreach (var c in InventoryService.Categories) FiCategory.Items.Add(new ListItem(TitleCase(c), c));
            foreach (var s in BillingService.ListServiceCharges(HospitalId).Where(s => s.Category != "CONSULTATION").OrderBy(s => s.ServiceName))
                FiServices.Items.Add(new ListItem(s.ServiceName, s.ServiceChargeId));
            NoServicesNote.Visible = FiServices.Items.Count == 0;
            Bind();
        }

        private void Bind()
        {
            var all = InventoryService.Summaries(HospitalId, true);
            var active = all.Where(s => s.Item.IsActive).ToList();
            ItemCount.Text = active.Count.ToString();
            LowCount.Text = active.Count(s => s.IsLow).ToString();
            ExpiringCount.Text = active.Count(s => s.ExpiresSoon).ToString();
            ExpiredUnits.Text = all.Sum(s => s.Expired).ToString();

            var term = SearchInput.Text.Trim();
            var shown = all.Where(s => (ShowInactive.Checked || s.Item.IsActive)
                    && (term.Length == 0 || s.Item.ItemName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                        || s.Item.ItemCode.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            ItemsRepeater.DataSource = shown;
            ItemsRepeater.DataBind();
            EmptyRow.Visible = shown.Count == 0;
            BindStockPanel();
        }

        private void BindStockPanel()
        {
            var item = string.IsNullOrEmpty(SelectedItemId.Value) ? null : InventoryService.GetItem(SelectedItemId.Value);
            StockPanel.Visible = item != null && item.HospitalId == HospitalId;
            if (!StockPanel.Visible) return;
            StockTitle.Text = item.ItemName + " (" + item.ItemCode + ") — stock in " + item.Unit;
            var batches = InventoryService.ListBatches(item.InventoryItemId, ShowEmptyBatches.Checked);
            BatchesRepeater.DataSource = batches;
            BatchesRepeater.DataBind();
            NoBatchesRow.Visible = batches.Count == 0;
            var moves = InventoryService.ListMovements(item.InventoryItemId, 50);
            MovementsRepeater.DataSource = moves;
            MovementsRepeater.DataBind();
            NoMovementsRow.Visible = moves.Count == 0;
        }

        /// <summary>"Used with: Injection Administration, Nebulization", or a hint when the item is linked to nothing.</summary>
        protected string UsedWith(InventoryItem item)
        {
            var names = item.ServiceChargeIds.Select(BillingService.GetServiceCharge).Where(s => s != null).Select(s => s.ServiceName).ToList();
            return names.Count > 0 ? "Used with: " + string.Join(", ", names) : "Not linked to a service — not offered in consultations";
        }

        protected string BatchNo(string stockBatchId)
        {
            var b = InventoryService.GetBatch(stockBatchId);
            return b != null ? b.BatchNo : "";
        }

        protected void SearchButton_Click(object sender, EventArgs e) { Bind(); }
        protected void ShowEmptyBatches_CheckedChanged(object sender, EventArgs e) { BindStockPanel(); }
        protected void CloseStockPanel_Click(object sender, EventArgs e) { SelectedItemId.Value = ""; StockPanel.Visible = false; }

        protected void CloseModal_Click(object sender, EventArgs e)
        {
            ItemModal.Visible = ReceiveModal.Visible = AdjustModal.Visible = false;
        }

        /* ── Items ── */

        protected void AddItemButton_Click(object sender, EventArgs e)
        {
            EditingItemId.Value = "";
            ItemModalTitle.Text = "Add Inventory Item";
            FiName.Text = FiCode.Text = FiUnit.Text = FiPrice.Text = "";
            FiTax.Text = "0";
            FiReorder.Text = "10";
            FiCategory.SelectedIndex = 0;
            foreach (ListItem li in FiServices.Items) li.Selected = false;
            ItemModal.Visible = true;
        }

        protected void ItemsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var item = InventoryService.GetItem((string)e.CommandArgument);
            if (item == null || item.HospitalId != HospitalId) return;
            switch (e.CommandName)
            {
                case "Stock":
                    SelectedItemId.Value = item.InventoryItemId;
                    BindStockPanel();
                    break;
                case "Receive":
                    OpenReceive(item.InventoryItemId);
                    break;
                case "Toggle":
                    InventoryService.ToggleItem(item.InventoryItemId);
                    Audit("UPDATE", "InventoryItem", item.InventoryItemId, item.ItemName + (item.IsActive ? " activated" : " deactivated"));
                    ToastSuccess("Item status updated", item.ItemName + " is now " + (item.IsActive ? "active" : "inactive"));
                    Bind();
                    break;
                case "EditItem":
                    EditingItemId.Value = item.InventoryItemId;
                    ItemModalTitle.Text = "Edit: " + item.ItemName;
                    FiName.Text = item.ItemName; FiCode.Text = item.ItemCode; FiUnit.Text = item.Unit;
                    FiPrice.Text = item.UnitPrice.ToString("0.##"); FiTax.Text = item.TaxPercent.ToString("0.##"); FiReorder.Text = item.ReorderLevel.ToString();
                    FiCategory.SelectedValue = item.Category;
                    foreach (ListItem li in FiServices.Items) li.Selected = item.ServiceChargeIds.Contains(li.Value);
                    ItemModal.Visible = true;
                    break;
            }
        }

        protected void CodeUnique_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = !InventoryService.ItemCodeExists(HospitalId, args.Value.Trim(), string.IsNullOrEmpty(EditingItemId.Value) ? null : EditingItemId.Value);
        }

        protected void SaveItemButton_Click(object sender, EventArgs e)
        {
            Validate("Item");
            if (!IsValid) { ItemModal.Visible = true; return; }
            var isNew = string.IsNullOrEmpty(EditingItemId.Value);
            var saved = InventoryService.SaveItem(new InventoryItem
            {
                InventoryItemId = isNew ? null : EditingItemId.Value, HospitalId = HospitalId,
                ItemName = FiName.Text.Trim(), ItemCode = FiCode.Text.Trim().ToUpperInvariant(), Category = FiCategory.SelectedValue,
                Unit = FiUnit.Text.Trim().ToLowerInvariant(), UnitPrice = ParseMoney(FiPrice.Text), TaxPercent = ParseMoney(FiTax.Text),
                ReorderLevel = ParseInt(FiReorder.Text, 0),
                ServiceChargeIds = FiServices.Items.Cast<ListItem>().Where(li => li.Selected).Select(li => li.Value).ToList()
            });
            Audit(isNew ? "CREATE" : "UPDATE", "InventoryItem", saved.InventoryItemId, saved.ItemName);
            ToastSuccess(saved.ItemName + (isNew ? " added — use Receive stock to record its first batch" : " updated"));
            ItemModal.Visible = false;
            Bind();
        }

        /* ── Receive ── */

        protected void ReceiveStockButton_Click(object sender, EventArgs e) { OpenReceive(SelectedItemId.Value); }

        private void OpenReceive(string inventoryItemId)
        {
            RcItem.Items.Clear();
            RcItem.Items.Add(new ListItem("— Select —", ""));
            foreach (var i in InventoryService.ListItems(HospitalId, false))
                RcItem.Items.Add(new ListItem(i.ItemName + " (" + i.ItemCode + ", per " + i.Unit + ")", i.InventoryItemId));
            if (!string.IsNullOrEmpty(inventoryItemId) && RcItem.Items.FindByValue(inventoryItemId) != null) RcItem.SelectedValue = inventoryItemId;
            RcBatch.Text = RcExpiry.Text = RcQty.Text = RcSupplier.Text = "";
            RcCost.Text = "0";
            ReceiveModal.Visible = true;
        }

        protected void Expiry_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var d = ParseDate(args.Value);
            args.IsValid = d.HasValue && d.Value.Date >= Today;
        }

        protected void SaveReceiptButton_Click(object sender, EventArgs e)
        {
            Validate("Receive");
            if (!IsValid) { ReceiveModal.Visible = true; return; }
            string error;
            var batch = InventoryService.ReceiveStock(HospitalId, RcItem.SelectedValue, RcBatch.Text.Trim().ToUpperInvariant(), ParseDate(RcExpiry.Text).Value,
                ParseInt(RcQty.Text, 0), ParseMoney(RcCost.Text), RcSupplier.Text.Trim(), UserId, out error);
            if (batch == null)
            {
                ReceiveValidator.IsValid = false;
                ReceiveValidator.Text = Server.HtmlEncode(error);
                ReceiveModal.Visible = true;
                return;
            }
            var item = InventoryService.GetItem(batch.InventoryItemId);
            Audit("CREATE", "StockReceipt", batch.StockBatchId, item.ItemName + " batch " + batch.BatchNo + " +" + RcQty.Text);
            ToastSuccess("Stock received", RcQty.Text + " " + item.Unit + " of " + item.ItemName + " (batch " + batch.BatchNo + ")");
            ReceiveModal.Visible = false;
            SelectedItemId.Value = item.InventoryItemId;
            Bind();
        }

        /* ── Adjust ── */

        protected void BatchesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var batch = InventoryService.GetBatch((string)e.CommandArgument);
            if (e.CommandName != "Adjust" || batch == null || batch.HospitalId != HospitalId) return;
            var item = InventoryService.GetItem(batch.InventoryItemId);
            AdjustBatchId.Value = batch.StockBatchId;
            AdjustTitle.Text = "Adjust " + item.ItemName + " — batch " + batch.BatchNo;
            AdjustOnHand.Text = batch.QtyOnHand + " " + Server.HtmlEncode(item.Unit);
            AdQty.Text = batch.ExpiryDate.Date < Today ? (-batch.QtyOnHand).ToString() : "";
            AdReason.SelectedIndex = batch.ExpiryDate.Date < Today ? 0 : 2;
            AdNote.Text = "";
            AdjustModal.Visible = true;
        }

        protected void AdjustQty_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var batch = InventoryService.GetBatch(AdjustBatchId.Value);
            int delta;
            if (batch == null || !int.TryParse(args.Value.Trim(), out delta) || delta == 0) { args.IsValid = false; AdjustValidator.Text = "Enter a non-zero whole number"; return; }
            args.IsValid = batch.QtyOnHand + delta >= 0;
            AdjustValidator.Text = "Only " + batch.QtyOnHand + " on hand";
        }

        protected void SaveAdjustButton_Click(object sender, EventArgs e)
        {
            Validate("Adjust");
            if (!IsValid) { AdjustModal.Visible = true; return; }
            var batch = InventoryService.GetBatch(AdjustBatchId.Value);
            var delta = ParseInt(AdQty.Text, 0);
            var reason = AdReason.SelectedValue + (AdNote.Text.Trim().Length > 0 ? " — " + AdNote.Text.Trim() : "");
            if (batch == null || !InventoryService.AdjustStock(batch.StockBatchId, delta, reason, UserId))
            {
                AdjustValidator.IsValid = false;
                AdjustModal.Visible = true;
                return;
            }
            var item = InventoryService.GetItem(batch.InventoryItemId);
            Audit("UPDATE", "StockAdjustment", batch.StockBatchId, item.ItemName + " batch " + batch.BatchNo + " " + (delta > 0 ? "+" : "") + delta + ": " + reason);
            ToastSuccess("Stock adjusted", item.ItemName + " batch " + batch.BatchNo + ": " + (delta > 0 ? "+" : "") + delta);
            AdjustModal.Visible = false;
            Bind();
        }
    }
}
