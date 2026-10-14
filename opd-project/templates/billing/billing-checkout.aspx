<%@ Page Title="Billing Checkout" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="billing-checkout.aspx.cs" Inherits="OpdProject.templates.billing.billing_checkout" MaintainScrollPositionOnPostback="true" %>

<%@ Register TagPrefix="uc" TagName="ErrorState" Src="~/templates/shared/error-state.ascx" %>
<%-- Itemised invoice → discount/tax → coverage & N-way bill split → payment → receipt.
     Route: /billing/checkout/{consultationId} --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<asp:PlaceHolder ID="NotFound" runat="server" Visible="false"><uc:ErrorState runat="server" Message="Consultation not found." /></asp:PlaceHolder>
<asp:PlaceHolder ID="Workspace" runat="server">
<div class="page-container titlecase-headings">
  <div class="card mb-3">
    <div style="display:flex; justify-content:space-between; align-items:center;">
      <div>
        <div class="text-sm text-secondary">Patient</div>
        <div style="font-weight:600; font-size:16px;"><asp:Literal ID="PatientName" runat="server" /></div>
        <div class="text-xs text-muted"><asp:Literal ID="PatientMeta" runat="server" /></div>
      </div>
      <div style="text-align:right;">
        <div class="text-sm text-secondary">Consultation</div>
        <div><asp:Literal ID="ConsultationInfo" runat="server" /></div>
        <a class="text-xs" href="<%: Urls.To("billing/invoices") %>">← All Invoices</a>
      </div>
    </div>
  </div>

  <div style="display:flex; gap:16px; align-items:flex-start;">
    <div style="flex:1; min-width:0;">
      <%-- Invoice --%>
      <div class="card">
        <div class="text-sm text-secondary mb-2" style="font-weight:600;">Invoice</div>
        <asp:PlaceHolder ID="NoInvoiceHolder" runat="server">
          <div class="text-sm text-secondary mb-2">No invoice generated for this consultation.</div>
          <asp:LinkButton ID="GenerateButton" runat="server" CssClass="btn btn--primary" OnClick="GenerateButton_Click"><i class="ti ti-file-invoice" aria-hidden="true"></i> Generate Invoice</asp:LinkButton>
        </asp:PlaceHolder>
        <asp:PlaceHolder ID="InvoiceHolder" runat="server">
          <asp:Literal ID="ChargeNote" runat="server" />
          <table class="data-table" style="font-size:12px;">
            <thead><tr><th>#</th><th>Description</th><th>Category</th><th style="text-align:right;">Qty</th><th style="text-align:right;">Unit</th><th style="text-align:right;">Disc%</th><th style="text-align:right;">Tax%</th><th style="text-align:right;">Total</th><th></th></tr></thead>
            <tbody>
              <asp:Repeater ID="LinesRepeater" runat="server" ItemType="OpdProject.Models.InvoiceLineItem" OnItemCommand="LinesRepeater_ItemCommand">
                <ItemTemplate>
                  <tr>
                    <td><%# Container.ItemIndex + 1 %></td><td><%#: Item.Description %></td><td class="text-xs"><%#: OpdProject.Web.BasePage.TitleCase(Item.Category) %></td>
                    <td style="text-align:right;"><%# Item.Qty %></td><td style="text-align:right;"><%#: Money(Item.UnitAmount, Inv.Currency) %></td>
                    <td style="text-align:right;"><%#: Item.DiscountPercent > 0 ? Item.DiscountPercent + "%" : "-" %></td>
                    <td style="text-align:right;"><%#: Item.TaxPercent > 0 ? Item.TaxPercent + "%" : "-" %></td>
                    <td style="text-align:right; font-weight:600;"><%#: Money(Item.LineTotal, Inv.Currency) %></td>
                    <td><asp:LinkButton runat="server" CssClass="btn btn--xs btn--danger" CommandName="Remove" CommandArgument="<%# Item.LineItemId %>" Visible="<%# Editable %>" ToolTip="Remove"
                      OnClientClick="return confirm('Remove this charge from the invoice?');"><i class="ti ti-trash"></i></asp:LinkButton></td>
                  </tr>
                </ItemTemplate>
              </asp:Repeater>
              <asp:PlaceHolder ID="NoLines" runat="server"><tr class="empty-row"><td colspan="9">No line items. Click "Add Charge" to add services.</td></tr></asp:PlaceHolder>
            </tbody>
            <tfoot><asp:Literal ID="TotalsFooter" runat="server" /></tfoot>
          </table>
          <div class="text-xs text-muted mt-2">Invoice: <asp:Literal ID="InvoiceNumber" runat="server" /> · Status: <span class="badge <%: StatusBadge(Inv != null ? Inv.Status : "") %>"><%: Inv != null ? OpdProject.Web.BasePage.TitleCase(Inv.Status) : "" %></span></div>
          <asp:PlaceHolder ID="EditActions" runat="server">
            <div style="display:flex; gap:8px; margin-top:12px; flex-wrap:wrap;">
              <asp:LinkButton ID="AddChargeButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="AddChargeButton_Click" CausesValidation="false"><i class="ti ti-plus"></i> Add Charge</asp:LinkButton>
              <asp:LinkButton ID="CustomChargeButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="CustomChargeButton_Click" CausesValidation="false"><i class="ti ti-pencil"></i> Custom Charge</asp:LinkButton>
              <div style="flex:1;"></div>
              <asp:LinkButton ID="CancelInvoiceButton" runat="server" CssClass="btn btn--sm btn--ghost text-danger" OnClick="CancelInvoiceButton_Click" CausesValidation="false"><i class="ti ti-x"></i> Cancel Invoice</asp:LinkButton>
              <asp:LinkButton ID="CapturePaymentButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="CapturePaymentButton_Click" CausesValidation="false"><i class="ti ti-cash"></i> Capture Payment</asp:LinkButton>
            </div>
          </asp:PlaceHolder>
          <asp:HyperLink ID="PrintLink" runat="server" CssClass="btn btn--sm btn--ghost mt-2" Target="_blank" style="width:100%; justify-content:center;"><i class="ti ti-printer"></i> Print Invoice</asp:HyperLink>
          <asp:Literal ID="CreditNoteText" runat="server" />
        </asp:PlaceHolder>
      </div>

      <%-- Coverage & N-way bill split --%>
      <asp:Panel ID="CoverageCard" runat="server" CssClass="card mt-3">
        <div style="display:flex; align-items:center; gap:8px;" class="mb-2">
          <div class="text-sm text-secondary" style="font-weight:600;">Coverage &amp; Bill Split</div>
          <div style="flex:1;"></div>
          <asp:LinkButton ID="AddCoverageButton" runat="server" CssClass="btn btn--xs btn--ghost" OnClick="AddCoverageButton_Click" CausesValidation="false"><i class="ti ti-shield-plus" aria-hidden="true"></i> Add Coverage</asp:LinkButton>
        </div>
        <asp:Literal ID="SplitKpis" runat="server" />
        <asp:Repeater ID="CoverageRepeater" runat="server" ItemType="OpdProject.Models.PatientCoverage" OnItemCommand="CoverageRepeater_ItemCommand" OnItemDataBound="CoverageRepeater_ItemDataBound">
          <ItemTemplate>
            <div style="border:1px solid var(--border-default); <%# LineFor(Item) != null ? "border-left:4px solid var(--color-success);" : "" %> border-radius:var(--radius-md); padding:10px 12px; margin-bottom:8px;">
              <div style="display:flex; align-items:center; gap:8px;">
                <strong class="text-sm"><%#: CoverageLabel(Item) %></strong>
                <span class="badge <%# Expired(Item) || Item.Status != "ACTIVE" ? "badge--warning" : "badge--success" %>" style="font-size:10px;"><%#: Expired(Item) ? "Expired" : OpdProject.Web.BasePage.TitleCase(Item.Status) %></span>
                <div style="flex:1;"></div>
                <asp:Literal ID="CoverageState" runat="server" />
              </div>
              <div class="text-xs text-muted" style="margin-top:2px;"><%#: CoverageIdent(Item) %></div>
              <asp:Literal ID="LiabilityText" runat="server" />
              <asp:Panel ID="VerifyForm" runat="server" style="margin-top:8px;">
                <div class="form-grid" style="grid-template-columns:repeat(auto-fit, minmax(120px, 1fr));">
                  <div class="form-group"><label class="form-label">Eligibility</label>
                    <asp:DropDownList ID="VStatus" runat="server" CssClass="form-select"><asp:ListItem Value="VERIFIED">Verified</asp:ListItem><asp:ListItem Value="PENDING">Pending</asp:ListItem><asp:ListItem Value="REJECTED">Rejected</asp:ListItem></asp:DropDownList></div>
                  <div class="form-group"><label class="form-label">Auth No.</label><asp:TextBox ID="VAuth" runat="server" CssClass="form-control" placeholder="e.g. A778899" /></div>
                  <div class="form-group"><label class="form-label">Approved As</label>
                    <asp:DropDownList ID="VType" runat="server" CssClass="form-select"><asp:ListItem Value="PERCENT">% Of Charge</asp:ListItem><asp:ListItem Value="AMOUNT">Fixed Amount</asp:ListItem></asp:DropDownList></div>
                  <div class="form-group"><label class="form-label">Value</label><asp:TextBox ID="VValue" runat="server" CssClass="form-control" TextMode="Number" step="0.01" placeholder="70 or 350" /></div>
                </div>
                <div class="form-group"><asp:TextBox ID="VNotes" runat="server" CssClass="form-control" placeholder="Notes (Optional)" /></div>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--primary" CommandName="Apply" CommandArgument="<%# Item.CoverageId %>" CausesValidation="false"><i class="ti ti-shield-check" aria-hidden="true"></i> Verify &amp; Apply</asp:LinkButton>
              </asp:Panel>
              <div style="display:flex; gap:10px; margin-top:6px; flex-wrap:wrap;">
                <asp:LinkButton ID="EditLineButton" runat="server" CssClass="btn btn--xs btn--ghost" CommandName="EditLine" CommandArgument="<%# Item.CoverageId %>" CausesValidation="false"><i class="ti ti-pencil" aria-hidden="true"></i> Edit Amount</asp:LinkButton>
                <asp:LinkButton ID="RemoveLineButton" runat="server" CssClass="btn btn--xs btn--ghost text-danger" CommandName="RemoveLine" CommandArgument="<%# Item.CoverageId %>" CausesValidation="false"
                  OnClientClick="return confirm('Remove this coverage from the invoice? The patient share goes back up by its amount.');">Remove</asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="History" CommandArgument="<%# Item.CoverageId %>" CausesValidation="false"><i class="ti ti-history" aria-hidden="true"></i> History</asp:LinkButton>
                <asp:LinkButton ID="DeactivateButton" runat="server" CssClass="btn btn--xs btn--ghost text-danger" CommandName="Deactivate" CommandArgument="<%# Item.CoverageId %>" CausesValidation="false"
                  OnClientClick="return confirm('Deactivate this coverage for the patient? It will no longer be offered at billing.');">Deactivate</asp:LinkButton>
              </div>
            </div>
          </ItemTemplate>
        </asp:Repeater>
        <asp:Literal ID="CoverageFooter" runat="server" />
      </asp:Panel>

      <%-- Payments --%>
      <asp:Panel ID="PaymentCard" runat="server" CssClass="card mt-3">
        <div class="text-sm text-secondary mb-2" style="font-weight:600;">Payments</div>
        <asp:Repeater ID="PaymentsRepeater" runat="server" ItemType="OpdProject.Models.Payment" OnItemCommand="PaymentsRepeater_ItemCommand">
          <ItemTemplate>
            <div style="display:flex; justify-content:space-between; padding:6px 0; border-bottom:1px solid var(--border-default); <%# Item.IsVoided ? "opacity:0.45; text-decoration:line-through;" : "" %>">
              <div><div class="text-sm"><strong><%#: OpdProject.Web.BasePage.TitleCase(Item.PaymentMethod) %></strong> · <%#: OpdProject.Web.BasePage.TitleCase(Item.ComponentType) %></div>
                <div class="text-xs text-muted"><%#: Item.TransactionNumber %><%#: string.IsNullOrEmpty(Item.Reference) ? "" : " · Ref: " + Item.Reference %> · <%#: FmtDateTime(Item.PaidAt) %><%#: Item.IsVoided ? " · Voided: " + Item.VoidReason : "" %></div></div>
              <div style="display:flex; align-items:center; gap:8px;">
                <div class="text-sm" style="font-weight:600;"><%#: Money(Item.Amount, Inv.Currency) %></div>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--danger" CommandName="Void" CommandArgument="<%# Item.TransactionId %>" Visible='<%# !Item.IsVoided && Inv.Status != "CANCELLED" %>' ToolTip="Void payment" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton>
              </div>
            </div>
          </ItemTemplate>
        </asp:Repeater>
      </asp:Panel>
    </div>

    <div style="width:300px; flex-shrink:0;">
      <asp:Panel ID="ReceiptCard" runat="server" CssClass="card">
        <div class="text-sm text-secondary mb-2" style="font-weight:600;">Receipt</div>
        <asp:Literal ID="ReceiptHtml" runat="server" />
      </asp:Panel>
    </div>
  </div>
</div>

        <%-- Add service charge --%>
        <asp:Panel ID="AddChargeModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="ConfirmAddChargeButton">
            <div class="modal-frame" role="dialog" aria-modal="true">
                <div class="modal-header"><strong>Add Service Charge</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
                <div class="modal-body">
                    <div class="form-grid">
                        <div class="form-group" style="grid-column: 1/-1;">
                            <label class="form-label" for="<%= ServicePick.ClientID %>">Service *</label><asp:DropDownList ID="ServicePick" runat="server" CssClass="form-select" /></div>
                        <div class="form-group">
                            <label class="form-label" for="<%= ServiceQty.ClientID %>">Qty</label><asp:TextBox ID="ServiceQty" runat="server" CssClass="form-control" TextMode="Number" Text="1" />
                            <asp:RangeValidator runat="server" ControlToValidate="ServiceQty" ValidationGroup="AddCharge" Type="Integer" MinimumValue="1" MaximumValue="99" Text="1–99" CssClass="field-error" Display="Dynamic" />
                        </div>
                        <div class="form-group">
                            <label class="form-label" for="<%= ServiceDiscount.ClientID %>">Discount %</label><asp:TextBox ID="ServiceDiscount" runat="server" CssClass="form-control" TextMode="Number" Text="0" step="0.5" />
                            <asp:RangeValidator runat="server" ControlToValidate="ServiceDiscount" ValidationGroup="AddCharge" Type="Double" MinimumValue="0" MaximumValue="100" Text="0–100" CssClass="field-error" Display="Dynamic" />
                        </div>
                    </div>
                </div>
                <div class="modal-footer">
                    <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Cancel</asp:LinkButton>
                    <asp:LinkButton ID="ConfirmAddChargeButton" runat="server" CssClass="btn btn--primary" ValidationGroup="AddCharge" OnClick="ConfirmAddChargeButton_Click">Add</asp:LinkButton>
                </div>
            </div>
        </asp:Panel>

<%-- Custom charge --%>
<asp:Panel ID="CustomChargeModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="ConfirmCustomChargeButton">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Custom Charge</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <div class="form-grid">
        <div class="form-group" style="grid-column:1/-1;"><label class="form-label" for="<%= CustomDesc.ClientID %>">Description *</label><asp:TextBox ID="CustomDesc" runat="server" CssClass="form-control" placeholder="e.g. Splint, Dressing Material" MaxLength="120" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="CustomDesc" ValidationGroup="Custom" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= CustomCategory.ClientID %>">Category</label>
          <asp:DropDownList ID="CustomCategory" runat="server" CssClass="form-select"><asp:ListItem Value="PROCEDURE">Procedure</asp:ListItem><asp:ListItem Value="SUPPLY">Supply</asp:ListItem><asp:ListItem Value="INJECTION">Injection</asp:ListItem><asp:ListItem Value="OTHER">Other</asp:ListItem></asp:DropDownList></div>
        <div class="form-group"><label class="form-label" for="<%= CustomAmount.ClientID %>">Amount *</label><asp:TextBox ID="CustomAmount" runat="server" CssClass="form-control" TextMode="Number" step="0.01" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="CustomAmount" ValidationGroup="Custom" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:RangeValidator runat="server" ControlToValidate="CustomAmount" ValidationGroup="Custom" Type="Double" MinimumValue="0.01" MaximumValue="10000000" Text="Must be greater than 0" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= CustomDiscount.ClientID %>">Discount %</label><asp:TextBox ID="CustomDiscount" runat="server" CssClass="form-control" TextMode="Number" Text="0" />
          <asp:RangeValidator runat="server" ControlToValidate="CustomDiscount" ValidationGroup="Custom" Type="Double" MinimumValue="0" MaximumValue="100" Text="0–100" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= CustomTax.ClientID %>">Tax %</label><asp:TextBox ID="CustomTax" runat="server" CssClass="form-control" TextMode="Number" Text="0" />
          <asp:RangeValidator runat="server" ControlToValidate="CustomTax" ValidationGroup="Custom" Type="Double" MinimumValue="0" MaximumValue="100" Text="0–100" CssClass="field-error" Display="Dynamic" /></div>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="ConfirmCustomChargeButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Custom" OnClick="ConfirmCustomChargeButton_Click">Add</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<%-- Cancel invoice --%>
<asp:Panel ID="CancelModal" runat="server" CssClass="modal-overlay" Visible="false">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Cancel Invoice</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <div class="form-group"><label class="form-label" for="<%= CancelReason.ClientID %>">Reason For Cancellation *</label>
        <asp:TextBox ID="CancelReason" runat="server" CssClass="form-control" TextMode="MultiLine" Rows="3" placeholder="e.g. Patient no-show, fee waived by doctor, wrong patient billed" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="CancelReason" ValidationGroup="Cancel" Text="Enter a cancellation reason" CssClass="field-error" Display="Dynamic" /></div>
      <p class="text-xs text-muted">A credit note is issued for the invoice total.</p>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Keep Invoice</asp:LinkButton>
      <asp:LinkButton ID="ConfirmCancelButton" runat="server" CssClass="btn btn--danger" ValidationGroup="Cancel" OnClick="ConfirmCancelButton_Click">Cancel Invoice</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<%-- Capture payment --%>
<asp:Panel ID="PaymentModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="ConfirmPaymentButton">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Capture Payment</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <div class="hint-banner mb-3"><i class="ti ti-info-circle"></i> <asp:Literal ID="PaymentHint" runat="server" /></div>
      <div class="form-grid">
        <div class="form-group" style="grid-column:1/-1;"><label class="form-label" for="<%= PayComponent.ClientID %>">Paid By</label>
          <asp:DropDownList ID="PayComponent" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="PayComponent_Changed" /></div>
        <div class="form-group"><label class="form-label" for="<%= PayMethod.ClientID %>">Payment Method *</label><asp:DropDownList ID="PayMethod" runat="server" CssClass="form-select" /></div>
        <div class="form-group"><label class="form-label" for="<%= PayAmount.ClientID %>">Amount *</label><asp:TextBox ID="PayAmount" runat="server" CssClass="form-control" TextMode="Number" step="0.01" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="PayAmount" ValidationGroup="Pay" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:CustomValidator ID="PayAmountValidator" runat="server" ControlToValidate="PayAmount" ValidationGroup="Pay" OnServerValidate="PayAmountValidator_ServerValidate" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group" style="grid-column:1/-1;"><label class="form-label" for="<%= PayReference.ClientID %>">Reference</label><asp:TextBox ID="PayReference" runat="server" CssClass="form-control" placeholder="Txn Ref / Card / Cheque / Claim Settlement No." /></div>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="ConfirmPaymentButton" runat="server" CssClass="btn btn--success" ValidationGroup="Pay" OnClick="ConfirmPaymentButton_Click">Record Payment</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<%-- Void payment --%>
<asp:Panel ID="VoidModal" runat="server" CssClass="modal-overlay" Visible="false">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Void Payment</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="VoidTransactionId" runat="server" />
      <div class="form-group"><label class="form-label" for="<%= VoidReason.ClientID %>">Reason For Voiding *</label>
        <asp:TextBox ID="VoidReason" runat="server" CssClass="form-control" TextMode="MultiLine" Rows="2" placeholder="e.g. Wrong Amount, Wrong Method, Duplicate Entry" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="VoidReason" ValidationGroup="Void" Text="Enter a reason" CssClass="field-error" Display="Dynamic" /></div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Keep</asp:LinkButton>
      <asp:LinkButton ID="ConfirmVoidButton" runat="server" CssClass="btn btn--danger" ValidationGroup="Void" OnClick="ConfirmVoidButton_Click">Void Payment</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<%-- Edit coverage amount --%>
<asp:Panel ID="EditLineModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="ConfirmEditLineButton">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Edit Coverage Amount</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="EditLineId" runat="server" />
      <div class="form-group"><label class="form-label" for="<%= EditLineAmount.ClientID %>">Approved Liability Amount</label>
        <asp:TextBox ID="EditLineAmount" runat="server" CssClass="form-control" TextMode="Number" step="0.01" />
        <asp:RangeValidator runat="server" ControlToValidate="EditLineAmount" ValidationGroup="EditLine" Type="Double" MinimumValue="0" MaximumValue="10000000" Text="Enter 0 or more" CssClass="field-error" Display="Dynamic" />
        <div class="text-xs text-muted mt-1">Capped so all coverages together never exceed the invoice total.</div></div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="ConfirmEditLineButton" runat="server" CssClass="btn btn--primary" ValidationGroup="EditLine" OnClick="ConfirmEditLineButton_Click">Save</asp:LinkButton>
    </div>
  </div>
</asp:Panel>

<%-- Eligibility history --%>
<asp:Panel ID="HistoryModal" runat="server" CssClass="modal-overlay" Visible="false">
  <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true">
    <div class="modal-header"><strong><asp:Literal ID="HistoryTitle" runat="server" /></strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <table class="data-table" style="font-size:12px;"><thead><tr><th>Date</th><th>Status</th><th>Auth</th><th>Approved</th><th>Notes</th></tr></thead><tbody>
        <asp:Repeater ID="HistoryRepeater" runat="server" ItemType="OpdProject.Models.EligibilityVerification">
          <ItemTemplate><tr><td><%#: FmtDateTime(Item.VerifiedAt) %></td><td><span class="badge <%# Item.Status == "VERIFIED" ? "badge--success" : Item.Status == "REJECTED" ? "badge--danger" : "badge--warning" %>" style="font-size:10px;"><%#: OpdProject.Web.BasePage.TitleCase(Item.Status) %></span></td>
            <td><%#: string.IsNullOrEmpty(Item.AuthorizationNumber) ? "—" : Item.AuthorizationNumber %></td><td><%#: Item.ApprovedType == "PERCENT" ? Item.ApprovedValue + "%" : Money(Item.ApprovedValue, Inv.Currency) %></td><td class="text-xs"><%#: Item.Notes %></td></tr></ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoHistory" runat="server"><tr class="empty-row"><td colspan="5">No eligibility checks recorded yet.</td></tr></asp:PlaceHolder>
      </tbody></table>
    </div>
    <div class="modal-footer"><asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Close</asp:LinkButton></div>
  </div>
</asp:Panel>

<%-- Add coverage --%>
<asp:Panel ID="AddCoverageModal" runat="server" CssClass="modal-overlay" Visible="false">
  <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Add Coverage</strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModals_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:ValidationSummary runat="server" ValidationGroup="Coverage" CssClass="validation-summary" />
      <div class="form-group"><label class="form-label" for="<%= CovType.ClientID %>">Coverage Type</label>
        <asp:DropDownList ID="CovType" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="CovType_Changed" /></div>
      <asp:PlaceHolder ID="CovPrivate" runat="server">
        <div class="form-grid">
          <div class="form-group"><label class="form-label" for="<%= CovPayerPrivate.ClientID %>">Insurance Company *</label><asp:DropDownList ID="CovPayerPrivate" runat="server" CssClass="form-select" /></div>
          <div class="form-group"><label class="form-label" for="<%= CovPlan.ClientID %>">Plan Name</label><asp:TextBox ID="CovPlan" runat="server" CssClass="form-control" /></div>
          <div class="form-group"><label class="form-label" for="<%= CovMember.ClientID %>">Member ID *</label><asp:TextBox ID="CovMember" runat="server" CssClass="form-control" /></div>
          <div class="form-group"><label class="form-label" for="<%= CovPolicy.ClientID %>">Policy Number</label><asp:TextBox ID="CovPolicy" runat="server" CssClass="form-control" /></div>
          <div class="form-group"><label class="form-label" for="<%= CovGroup.ClientID %>">Group Number</label><asp:TextBox ID="CovGroup" runat="server" CssClass="form-control" /></div>
          <div class="form-group"><label class="form-label" for="<%= CovHolder.ClientID %>">Policy Holder</label><asp:TextBox ID="CovHolder" runat="server" CssClass="form-control" /></div>
          <div class="form-group"><label class="form-label" for="<%= CovRelation.ClientID %>">Relationship</label>
            <asp:DropDownList ID="CovRelation" runat="server" CssClass="form-select"><asp:ListItem>Self</asp:ListItem><asp:ListItem>Spouse</asp:ListItem><asp:ListItem>Child</asp:ListItem><asp:ListItem>Parent</asp:ListItem><asp:ListItem>Dependent</asp:ListItem></asp:DropDownList></div>
          <div class="form-group"><label class="form-label" for="<%= CovMode.ClientID %>">Claim Mode</label>
            <asp:DropDownList ID="CovMode" runat="server" CssClass="form-select"><asp:ListItem Value="CASHLESS">Cashless</asp:ListItem><asp:ListItem Value="REIMBURSEMENT">Reimbursement</asp:ListItem></asp:DropDownList></div>
        </div>
        <asp:CustomValidator ID="CovPrivateValidator" runat="server" ValidationGroup="Coverage" OnServerValidate="CovPrivateValidator_ServerValidate" ErrorMessage="Enter the member ID or policy number" Display="None" />
      </asp:PlaceHolder>
      <asp:PlaceHolder ID="CovGovt" runat="server" Visible="false">
        <div class="form-grid">
          <div class="form-group"><label class="form-label" for="<%= CovPayerGovt.ClientID %>">Scheme *</label><asp:DropDownList ID="CovPayerGovt" runat="server" CssClass="form-select" /></div>
          <div class="form-group"><label class="form-label" for="<%= CovCard.ClientID %>"><asp:Literal ID="CardLabel" runat="server" /> *</label><asp:TextBox ID="CovCard" runat="server" CssClass="form-control" /></div>
        </div>
        <asp:CustomValidator ID="CovGovtValidator" runat="server" ValidationGroup="Coverage" OnServerValidate="CovGovtValidator_ServerValidate" Display="None" />
      </asp:PlaceHolder>
      <div class="form-grid">
        <div class="form-group"><label class="form-label" for="<%= CovEffective.ClientID %>">Effective Date</label><asp:TextBox ID="CovEffective" runat="server" CssClass="form-control" TextMode="Date" /></div>
        <div class="form-group"><label class="form-label" for="<%= CovExpiry.ClientID %>">Expiry Date</label><asp:TextBox ID="CovExpiry" runat="server" CssClass="form-control" TextMode="Date" />
          <asp:CompareValidator runat="server" ControlToValidate="CovExpiry" ControlToCompare="CovEffective" Operator="GreaterThanEqual" Type="Date" ValidationGroup="Coverage" ErrorMessage="Expiry must be on or after the effective date" Text="Must be after effective date" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= CovStatus.ClientID %>">Status</label>
          <asp:DropDownList ID="CovStatus" runat="server" CssClass="form-select"><asp:ListItem Value="ACTIVE">Active</asp:ListItem><asp:ListItem Value="PENDING">Pending</asp:ListItem><asp:ListItem Value="INACTIVE">Inactive</asp:ListItem><asp:ListItem Value="EXPIRED">Expired</asp:ListItem></asp:DropDownList></div>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModals_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="ConfirmCoverageButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Coverage" OnClick="ConfirmCoverageButton_Click">Add Coverage</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:PlaceHolder>
</asp:Content>
