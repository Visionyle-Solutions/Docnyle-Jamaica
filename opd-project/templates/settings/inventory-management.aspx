<%@ Page Title="Clinic Inventory" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="inventory-management.aspx.cs" Inherits="OpdProject.templates.settings.inventory_management" %>
<%-- Clinic stock: items used in the visit (injections, medications, consumables), batches with expiry, receipts and adjustments.
     Each item is linked to the services it is used with ("Used with"); in the consultation's Services & Procedures step,
     picking one of those services offers the item. Stock is deducted earliest-expiry first at sign-off.
     Route: /settings/inventory --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <asp:Panel ID="SearchCard" runat="server" CssClass="card mb-3" DefaultButton="SearchButton">
    <div style="display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
      <asp:TextBox ID="SearchInput" runat="server" CssClass="form-control" style="flex:1; min-width:200px;" placeholder="Search by name or code..." />
      <asp:CheckBox ID="ShowInactive" runat="server" Text=" Include inactive" CssClass="text-xs" AutoPostBack="true" OnCheckedChanged="SearchButton_Click" />
      <asp:LinkButton ID="SearchButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="SearchButton_Click" CausesValidation="false"><i class="ti ti-search" aria-hidden="true"></i> Search</asp:LinkButton>
      <asp:LinkButton ID="ReceiveStockButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="ReceiveStockButton_Click" CausesValidation="false"><i class="ti ti-truck-delivery" aria-hidden="true"></i> Receive stock</asp:LinkButton>
      <asp:LinkButton ID="AddItemButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="AddItemButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add item</asp:LinkButton>
    </div>
  </asp:Panel>

  <div class="mb-3" style="display:grid; grid-template-columns:repeat(auto-fit, minmax(160px, 1fr)); gap:8px;">
    <div class="metric-card"><div class="metric-label">Active items</div><div class="metric-value"><asp:Literal ID="ItemCount" runat="server" /></div></div>
    <div class="metric-card"><div class="metric-label">Low or out of stock</div><div class="metric-value"><asp:Literal ID="LowCount" runat="server" /></div></div>
    <div class="metric-card"><div class="metric-label">Expiring within <%: OpdProject.Models.InventoryRules.ExpiryWarningDays %> days</div><div class="metric-value"><asp:Literal ID="ExpiringCount" runat="server" /></div></div>
    <div class="metric-card"><div class="metric-label">Expired units to write off</div><div class="metric-value"><asp:Literal ID="ExpiredUnits" runat="server" /></div></div>
  </div>

  <div class="card mb-3" style="overflow-x:auto;">
    <table class="data-table">
      <thead><tr><th>Code</th><th>Item</th><th>Category</th><th style="text-align:right;">Price / unit</th><th style="text-align:right;">Available</th><th>Next expiry</th><th>Status</th><th>Actions</th></tr></thead>
      <tbody>
        <asp:Repeater ID="ItemsRepeater" runat="server" ItemType="OpdProject.Models.StockSummary" OnItemCommand="ItemsRepeater_ItemCommand">
          <ItemTemplate>
            <tr>
              <td class="text-xs"><%#: Item.Item.ItemCode %></td>
              <td><strong><%#: Item.Item.ItemName %></strong>
                <div class="text-xs text-muted"><%#: UsedWith(Item.Item) %></div></td>
              <td class="text-xs"><%#: TitleCase(Item.Item.Category) %></td>
              <td style="text-align:right;"><%#: Money(Item.Item.UnitPrice) %><span class="text-xs text-muted"> / <%#: Item.Item.Unit %></span></td>
              <td style="text-align:right;"><strong><%#: Item.Available %></strong> <span class="text-xs text-muted"><%#: Item.Item.Unit %></span></td>
              <td class="text-xs"><%#: Item.NextExpiry.HasValue ? Item.NextExpiry.Value.ToString("dd MMM yyyy") : "—" %></td>
              <td style="white-space:nowrap;">
                <%# !Item.Item.IsActive ? "<span class=\"badge badge--danger\">Inactive</span>"
                    : Item.Available == 0 ? "<span class=\"badge badge--danger\">Out of stock</span>"
                    : Item.IsLow ? "<span class=\"badge badge--warning\">Low</span>" : "<span class=\"badge badge--success\">OK</span>" %>
                <%# Item.ExpiresSoon ? "<span class=\"badge badge--warning\">Expiring</span>" : "" %>
                <%# Item.Expired > 0 ? "<span class=\"badge badge--danger\">" + Item.Expired + " expired</span>" : "" %>
              </td>
              <td style="white-space:nowrap;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Stock" CommandArgument="<%# Item.Item.InventoryItemId %>" CausesValidation="false" ToolTip="Batches and history"><i class="ti ti-stack-2"></i> Stock</asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Receive" CommandArgument="<%# Item.Item.InventoryItemId %>" CausesValidation="false" ToolTip="Receive stock"><i class="ti ti-truck-delivery"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="EditItem" CommandArgument="<%# Item.Item.InventoryItemId %>" CausesValidation="false" ToolTip="Edit"><i class="ti ti-edit"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Toggle" CommandArgument="<%# Item.Item.InventoryItemId %>" CausesValidation="false"
                  style='<%# "color:" + (Item.Item.IsActive ? "var(--color-danger)" : "var(--color-success)") %>'><%# Item.Item.IsActive ? "Deactivate" : "Activate" %></asp:LinkButton>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="EmptyRow" runat="server"><tr class="empty-row"><td colspan="8">No inventory items found.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>
  </div>

  <asp:Panel ID="StockPanel" runat="server" CssClass="card" Visible="false">
    <asp:HiddenField ID="SelectedItemId" runat="server" />
    <div style="display:flex; align-items:center; gap:8px; margin-bottom:8px;">
      <strong style="flex:1;"><i class="ti ti-stack-2" aria-hidden="true"></i> <asp:Literal ID="StockTitle" runat="server" Mode="Encode" /></strong>
      <asp:CheckBox ID="ShowEmptyBatches" runat="server" Text=" Show used-up batches" CssClass="text-xs" AutoPostBack="true" OnCheckedChanged="ShowEmptyBatches_CheckedChanged" />
      <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" OnClick="CloseStockPanel_Click" CausesValidation="false" ToolTip="Close"><i class="ti ti-x"></i></asp:LinkButton>
    </div>
    <div style="overflow-x:auto;">
    <table class="data-table" style="font-size:12px;">
      <thead><tr><th>Batch</th><th>Expiry</th><th style="text-align:right;">Received</th><th style="text-align:right;">On hand</th><th style="text-align:right;">Cost / unit</th><th>Supplier</th><th>Received on</th><th></th></tr></thead>
      <tbody>
        <asp:Repeater ID="BatchesRepeater" runat="server" ItemType="OpdProject.Models.StockBatch" OnItemCommand="BatchesRepeater_ItemCommand">
          <ItemTemplate>
            <tr>
              <td><strong><%#: Item.BatchNo %></strong></td>
              <td><%#: Item.ExpiryDate.ToString("dd MMM yyyy") %>
                <%# Item.ExpiryDate.Date < Today ? " <span class=\"badge badge--danger\">Expired</span>"
                    : Item.ExpiryDate.Date <= Today.AddDays(OpdProject.Models.InventoryRules.ExpiryWarningDays) ? " <span class=\"badge badge--warning\">Expiring</span>" : "" %></td>
              <td style="text-align:right;"><%#: Item.QtyReceived %></td>
              <td style="text-align:right;"><strong><%#: Item.QtyOnHand %></strong></td>
              <td style="text-align:right;"><%#: Money(Item.UnitCost) %></td>
              <td><%#: Item.Supplier %></td>
              <td class="text-xs"><%#: FmtDate(Item.ReceivedAt) %></td>
              <td><asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Adjust" CommandArgument="<%# Item.StockBatchId %>" CausesValidation="false"><i class="ti ti-adjustments"></i> Adjust</asp:LinkButton></td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoBatchesRow" runat="server"><tr class="empty-row"><td colspan="8">No stock on hand — use Receive stock.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>
    </div>
    <div class="settings-section-header mt-3">Recent movements</div>
    <div style="overflow-x:auto;">
    <table class="data-table" style="font-size:12px;">
      <thead><tr><th>When</th><th>Type</th><th>Batch</th><th style="text-align:right;">Qty</th><th>Reason</th><th>By</th></tr></thead>
      <tbody>
        <asp:Repeater ID="MovementsRepeater" runat="server" ItemType="OpdProject.Models.StockMovement">
          <ItemTemplate>
            <tr>
              <td class="text-xs"><%#: FmtDateTime(Item.CreatedAt) %></td>
              <td><span class="badge <%# Item.MovementType == "RECEIPT" ? "badge--success" : Item.MovementType == "CONSUMPTION" ? "badge--info" : "badge--warning" %>"><%#: TitleCase(Item.MovementType) %></span></td>
              <td class="text-xs"><%#: BatchNo(Item.StockBatchId) %></td>
              <td style="text-align:right; font-weight:600; color:<%# Item.Qty < 0 ? "var(--color-danger)" : "var(--color-success)" %>;"><%#: Item.Qty > 0 ? "+" + Item.Qty : Item.Qty.ToString() %></td>
              <td><%#: Item.Reason %></td>
              <td class="text-xs"><%#: OpdProject.Services.UserService.UserName(Item.UserId) %></td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoMovementsRow" runat="server"><tr class="empty-row"><td colspan="6">No movements yet.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>
    </div>
  </asp:Panel>
</div>

<asp:Panel ID="ItemModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="SaveItemButton">
  <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true">
    <div class="modal-header"><strong><asp:Literal ID="ItemModalTitle" runat="server" Mode="Encode" /></strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModal_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="EditingItemId" runat="server" />
      <asp:ValidationSummary runat="server" ValidationGroup="Item" CssClass="validation-summary" />
      <div class="form-grid">
        <div class="form-group"><label class="form-label" for="<%= FiName.ClientID %>">Item name *</label><asp:TextBox ID="FiName" runat="server" CssClass="form-control" MaxLength="100" placeholder="e.g. Diclofenac 75mg/3ml" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="FiName" ValidationGroup="Item" ErrorMessage="Item name is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FiCode.ClientID %>">Item code *</label><asp:TextBox ID="FiCode" runat="server" CssClass="form-control" MaxLength="30" placeholder="e.g. INJ-DICLO" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="FiCode" ValidationGroup="Item" ErrorMessage="Item code is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:CustomValidator runat="server" ControlToValidate="FiCode" ValidationGroup="Item" OnServerValidate="CodeUnique_ServerValidate" ErrorMessage="Another item already uses this code" Text="Already used" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FiCategory.ClientID %>">Category *</label><asp:DropDownList ID="FiCategory" runat="server" CssClass="form-select" /></div>
        <div class="form-group"><label class="form-label" for="<%= FiUnit.ClientID %>">Unit *</label><asp:TextBox ID="FiUnit" runat="server" CssClass="form-control" MaxLength="20" placeholder="vial, ampoule, tablet, piece" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="FiUnit" ValidationGroup="Item" ErrorMessage="Unit is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FiPrice.ClientID %>">Selling price per unit *</label><asp:TextBox ID="FiPrice" runat="server" CssClass="form-control" TextMode="Number" step="0.01" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="FiPrice" ValidationGroup="Item" ErrorMessage="Price is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:RangeValidator runat="server" ControlToValidate="FiPrice" ValidationGroup="Item" Type="Double" MinimumValue="0" MaximumValue="10000000" ErrorMessage="Price must be 0 or more" Text="0 or more" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FiTax.ClientID %>">Tax %</label><asp:TextBox ID="FiTax" runat="server" CssClass="form-control" TextMode="Number" step="0.01" Text="0" />
          <asp:RangeValidator runat="server" ControlToValidate="FiTax" ValidationGroup="Item" Type="Double" MinimumValue="0" MaximumValue="100" ErrorMessage="Tax must be 0–100" Text="0–100" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FiReorder.ClientID %>">Reorder level</label><asp:TextBox ID="FiReorder" runat="server" CssClass="form-control" TextMode="Number" Text="10" />
          <asp:RangeValidator runat="server" ControlToValidate="FiReorder" ValidationGroup="Item" Type="Integer" MinimumValue="0" MaximumValue="100000" ErrorMessage="Reorder level must be 0 or more" Text="0 or more" CssClass="field-error" Display="Dynamic" />
          <div class="text-xs text-muted">Flagged as low stock at or below this quantity.</div></div>
        <div class="form-group form-group--full"><label class="form-label">Used with services</label>
          <asp:CheckBoxList ID="FiServices" runat="server" RepeatLayout="Flow" RepeatDirection="Horizontal" CssClass="text-xs" style="display:flex; flex-wrap:wrap; gap:4px 16px;" />
          <div class="text-xs text-muted">The item is offered in Services &amp; Procedures when one of these is picked.</div>
          <asp:PlaceHolder ID="NoServicesNote" runat="server" Visible="false"><div class="text-xs text-muted">No services set up yet — add them in Settings → Billing &amp; Numbers → Service Charge Master.</div></asp:PlaceHolder></div>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModal_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="SaveItemButton" runat="server" CssClass="btn btn--success" ValidationGroup="Item" OnClick="SaveItemButton_Click">Save</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<asp:Panel ID="ReceiveModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="SaveReceiptButton">
  <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Receive Stock</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModal_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:ValidationSummary runat="server" ValidationGroup="Receive" CssClass="validation-summary" />
      <div class="form-grid">
        <div class="form-group form-group--full"><label class="form-label" for="<%= RcItem.ClientID %>">Item *</label><asp:DropDownList ID="RcItem" runat="server" CssClass="form-select" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="RcItem" ValidationGroup="Receive" ErrorMessage="Choose an item" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= RcBatch.ClientID %>">Batch number *</label><asp:TextBox ID="RcBatch" runat="server" CssClass="form-control" MaxLength="40" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="RcBatch" ValidationGroup="Receive" ErrorMessage="Batch number is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= RcExpiry.ClientID %>">Expiry date *</label><asp:TextBox ID="RcExpiry" runat="server" CssClass="form-control" TextMode="Date" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="RcExpiry" ValidationGroup="Receive" ErrorMessage="Expiry date is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:CustomValidator ID="ExpiryValidator" runat="server" ControlToValidate="RcExpiry" ValidationGroup="Receive" OnServerValidate="Expiry_ServerValidate" ErrorMessage="Expiry date must be today or later" Text="Already expired" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= RcQty.ClientID %>">Quantity received *</label><asp:TextBox ID="RcQty" runat="server" CssClass="form-control" TextMode="Number" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="RcQty" ValidationGroup="Receive" ErrorMessage="Quantity is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:RangeValidator runat="server" ControlToValidate="RcQty" ValidationGroup="Receive" Type="Integer" MinimumValue="1" MaximumValue="100000" ErrorMessage="Quantity must be 1 or more" Text="1 or more" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= RcCost.ClientID %>">Cost per unit</label><asp:TextBox ID="RcCost" runat="server" CssClass="form-control" TextMode="Number" step="0.01" Text="0" />
          <asp:RangeValidator runat="server" ControlToValidate="RcCost" ValidationGroup="Receive" Type="Double" MinimumValue="0" MaximumValue="10000000" ErrorMessage="Cost must be 0 or more" Text="0 or more" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= RcSupplier.ClientID %>">Supplier</label><asp:TextBox ID="RcSupplier" runat="server" CssClass="form-control" MaxLength="100" /></div>
      </div>
      <asp:CustomValidator ID="ReceiveValidator" runat="server" ValidationGroup="Receive" CssClass="field-error" Display="Dynamic" EnableClientScript="false" />
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModal_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="SaveReceiptButton" runat="server" CssClass="btn btn--success" ValidationGroup="Receive" OnClick="SaveReceiptButton_Click">Receive</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<asp:Panel ID="AdjustModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="SaveAdjustButton">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong><asp:Literal ID="AdjustTitle" runat="server" Mode="Encode" /></strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModal_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="AdjustBatchId" runat="server" />
      <div class="text-xs text-muted mb-2">On hand: <strong><asp:Literal ID="AdjustOnHand" runat="server" /></strong>. Enter a negative number to remove stock (damaged, expired, count short) or a positive one to add it back.</div>
      <div class="form-grid">
        <div class="form-group"><label class="form-label" for="<%= AdQty.ClientID %>">Change in quantity *</label><asp:TextBox ID="AdQty" runat="server" CssClass="form-control" TextMode="Number" placeholder="e.g. -2" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="AdQty" ValidationGroup="Adjust" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:CustomValidator ID="AdjustValidator" runat="server" ControlToValidate="AdQty" ValidationGroup="Adjust" OnServerValidate="AdjustQty_ServerValidate" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= AdReason.ClientID %>">Reason *</label>
          <asp:DropDownList ID="AdReason" runat="server" CssClass="form-select">
            <asp:ListItem>Expired — written off</asp:ListItem><asp:ListItem>Damaged / broken</asp:ListItem><asp:ListItem>Physical count correction</asp:ListItem>
            <asp:ListItem>Returned to supplier</asp:ListItem><asp:ListItem>Other</asp:ListItem>
          </asp:DropDownList></div>
        <div class="form-group form-group--full"><label class="form-label" for="<%= AdNote.ClientID %>">Note</label><asp:TextBox ID="AdNote" runat="server" CssClass="form-control" MaxLength="120" /></div>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModal_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="SaveAdjustButton" runat="server" CssClass="btn btn--success" ValidationGroup="Adjust" OnClick="SaveAdjustButton_Click">Save adjustment</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:Content>
