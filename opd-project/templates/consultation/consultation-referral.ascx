<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-referral.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_referral" %>
<div class="consult-section" id="sectionReferral" data-section="referral">
  <div class="consult-section-body">
    <asp:Panel ID="FormPanel" runat="server" DefaultButton="AddReferralButton">
      <div class="form-grid">
        <div class="form-group">
          <label class="form-label" for="<%= ReferralSpecialty.ClientID %>">Refer To Specialty <span class="required">*</span></label>
          <asp:TextBox ID="ReferralSpecialty" runat="server" CssClass="form-control" placeholder="e.g. Cardiology, Neurology" MaxLength="80" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="ReferralSpecialty" ValidationGroup="Referral" Text="Specialty is required" CssClass="field-error" Display="Dynamic" />
        </div>
        <asp:PlaceHolder ID="DoctorNameHolder" runat="server">
        <div class="form-group">
          <label class="form-label" for="<%= ReferralDoctorName.ClientID %>">Doctor Name (Optional)</label>
          <asp:TextBox ID="ReferralDoctorName" runat="server" CssClass="form-control" placeholder="Specific Doctor If Known" MaxLength="80" />
        </div>
        </asp:PlaceHolder>
        <asp:PlaceHolder ID="UrgencyHolder" runat="server">
        <div class="form-group">
          <label class="form-label" for="<%= ReferralUrgency.ClientID %>">Urgency</label>
          <asp:DropDownList ID="ReferralUrgency" runat="server" CssClass="form-select">
            <asp:ListItem Value="ROUTINE">Routine</asp:ListItem>
            <asp:ListItem Value="URGENT">Urgent</asp:ListItem>
          </asp:DropDownList>
        </div>
        </asp:PlaceHolder>
        <div class="form-group">
          <label class="form-label" for="<%= ReferralReason.ClientID %>">Reason <span class="required">*</span></label>
          <asp:TextBox ID="ReferralReason" runat="server" CssClass="form-control" placeholder="Reason For Referral" MaxLength="250" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="ReferralReason" ValidationGroup="Referral" Text="Reason is required" CssClass="field-error" Display="Dynamic" />
        </div>
      </div>
      <asp:LinkButton ID="AddReferralButton" runat="server" CssClass="btn btn--sm btn--success" ValidationGroup="Referral" OnClick="AddReferralButton_Click">Add Referral</asp:LinkButton>
      <asp:LinkButton ID="SkipReferralButton" runat="server" CssClass="btn btn--xs btn--ghost ml-2" CausesValidation="false" OnClick="SkipReferralButton_Click">Not Needed</asp:LinkButton>
    </asp:Panel>
    <div class="mt-2">
      <asp:Repeater ID="ReferralsRepeater" runat="server" ItemType="OpdProject.Models.Referral">
        <ItemTemplate>
          <div class="zone-a-item">→ <strong><%#: Item.Specialty %></strong><%#: string.IsNullOrEmpty(Item.DoctorName) ? "" : " (Dr. " + Item.DoctorName + ")" %>: <%#: Item.Reason %>
            <span class="badge <%# Item.Urgency == "URGENT" ? "badge--danger" : "badge--info" %>" style="font-size:9px;"><%#: Item.Urgency %></span></div>
        </ItemTemplate>
      </asp:Repeater>
    </div>
  </div>
</div>
