<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-services.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_services" %>
<%-- Services & procedures done in the visit, picked from Settings → Billing & Numbers → Service Charge Master.
     No prices here: sign-off adds each one to the invoice at the master's amount and tax; billing can still adjust it at checkout. --%>
<div class="consult-section" id="sectionServices" data-section="services">
  <div class="consult-section-body">
    <asp:Panel ID="FormPanel" runat="server" DefaultButton="AddServiceButton">
      <asp:PlaceHolder ID="NoServicesHint" runat="server" Visible="false">
        <div class="text-xs text-muted mb-2"><i class="ti ti-info-circle" aria-hidden="true"></i> No billable services are set up yet — an admin adds them in Settings → Billing &amp; Numbers → Service Charge Master.</div>
      </asp:PlaceHolder>
      <asp:PlaceHolder ID="PickerHolder" runat="server">
      <div class="form-grid">
        <div class="form-group">
          <label class="form-label" for="<%= ServicePick.ClientID %>">Service / Procedure <span class="required">*</span></label>
          <asp:DropDownList ID="ServicePick" runat="server" CssClass="form-select" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="ServicePick" ValidationGroup="Services" Text="Choose a service" CssClass="field-error" Display="Dynamic" />
        </div>
        <div class="form-group">
          <label class="form-label" for="<%= ServiceQty.ClientID %>">Quantity</label>
          <asp:TextBox ID="ServiceQty" runat="server" CssClass="form-control" TextMode="Number" min="1" max="99" Text="1" />
          <asp:RangeValidator runat="server" ControlToValidate="ServiceQty" ValidationGroup="Services" Type="Integer" MinimumValue="1" MaximumValue="99" Text="Enter 1–99" CssClass="field-error" Display="Dynamic" />
        </div>
        <asp:PlaceHolder ID="NotesHolder" runat="server">
        <div class="form-group form-group--full">
          <label class="form-label" for="<%= ServiceNotes.ClientID %>">Notes (Optional)</label>
          <asp:TextBox ID="ServiceNotes" runat="server" CssClass="form-control" MaxLength="120" placeholder="e.g. Left forearm, IM injection" />
        </div>
        </asp:PlaceHolder>
      </div>
      <asp:LinkButton ID="AddServiceButton" runat="server" CssClass="btn btn--sm btn--success" ValidationGroup="Services" OnClick="AddServiceButton_Click">Add Service</asp:LinkButton>
      </asp:PlaceHolder>
      <asp:LinkButton ID="SkipServicesButton" runat="server" CssClass="btn btn--xs btn--ghost ml-2" CausesValidation="false" OnClick="SkipServicesButton_Click">Not Needed</asp:LinkButton>
    </asp:Panel>
    <div class="mt-2">
      <asp:Repeater ID="ServicesRepeater" runat="server" ItemType="OpdProject.Models.ConsultationServiceItem" OnItemCommand="ServicesRepeater_ItemCommand">
        <ItemTemplate>
          <div class="zone-a-item" style="display:flex; align-items:center; gap:8px;">
            <span style="flex:1;"><i class="ti ti-first-aid-kit" aria-hidden="true"></i> <strong><%#: Item.ServiceName %></strong><%#: Item.Qty > 1 ? " ×" + Item.Qty : "" %><%#: string.IsNullOrEmpty(Item.Notes) ? "" : " — " + Item.Notes %>
              <span class="badge badge--info" style="font-size:9px;"><%#: Item.Category %></span></span>
            <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Remove" CommandArgument="<%# Item.ConsultationServiceId %>" CausesValidation="false"
              Visible="<%# !Host.IsLocked %>" ToolTip='<%# "Remove " + Item.ServiceName %>'><i class="ti ti-trash" aria-hidden="true"></i></asp:LinkButton>
          </div>
        </ItemTemplate>
      </asp:Repeater>
      <asp:PlaceHolder ID="BillingNote" runat="server" Visible="false">
        <div class="text-xs text-muted mt-1"><i class="ti ti-receipt" aria-hidden="true"></i> <asp:Literal ID="BillingNoteText" runat="server" Mode="Encode" /></div>
      </asp:PlaceHolder>
    </div>
  </div>
</div>
