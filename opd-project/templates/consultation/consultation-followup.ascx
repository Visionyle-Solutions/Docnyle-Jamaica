<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-followup.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_followup" %>
<div class="consult-section" id="sectionFollowup" data-section="followup">
  <div class="consult-section-body">
    <asp:Panel ID="FormPanel" runat="server" DefaultButton="SaveFollowupButton">
      <div class="form-grid">
        <div class="form-group">
          <label class="form-label" for="<%= FollowupDate.ClientID %>">Follow-up date</label>
          <asp:TextBox ID="FollowupDate" runat="server" CssClass="form-control" TextMode="Date" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="FollowupDate" ValidationGroup="Followup" Text="Please select a follow-up date" CssClass="field-error" Display="Dynamic" />
          <asp:CustomValidator runat="server" ControlToValidate="FollowupDate" ValidationGroup="Followup" OnServerValidate="FollowupDate_ServerValidate" Text="Must be after today" CssClass="field-error" Display="Dynamic" />
        </div>
        <asp:PlaceHolder ID="NotesHolder" runat="server">
        <div class="form-group">
          <label class="form-label" for="<%= FollowupNotes.ClientID %>">Instructions</label>
          <asp:TextBox ID="FollowupNotes" runat="server" CssClass="form-control" placeholder="e.g. Review with lab reports" MaxLength="250" />
        </div>
        </asp:PlaceHolder>
      </div>
      <asp:CheckBox ID="AutoBook" runat="server" Checked="true" Text=" Book a follow-up appointment at the first free slot on that date" CssClass="text-xs" />
      <div class="mt-2">
        <asp:LinkButton ID="SaveFollowupButton" runat="server" CssClass="btn btn--sm btn--success" ValidationGroup="Followup" OnClick="SaveFollowupButton_Click">Save</asp:LinkButton>
        <asp:LinkButton ID="SkipFollowupButton" runat="server" CssClass="btn btn--xs btn--ghost ml-2" CausesValidation="false" OnClick="SkipFollowupButton_Click">Not needed</asp:LinkButton>
      </div>
    </asp:Panel>
    <asp:Literal ID="SavedText" runat="server" />
  </div>
</div>
