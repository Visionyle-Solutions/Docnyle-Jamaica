<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="patient-family-social-tab.ascx.cs" Inherits="OpdProject.templates.patients.patient_family_social_tab" %>
<%-- Health record → Family & Social: family history (list + add/edit form) and the social history card. --%>
<div class="split-layout">
  <div class="split-main">
    <div class="record-section-title"><span>Family History</span></div>
    <table class="data-table">
      <thead>
        <tr><th scope="col">Relationship</th><th scope="col">Condition</th><th scope="col">Notes</th><th scope="col">Actions</th></tr>
      </thead>
      <tbody>
        <asp:Repeater ID="FamilyRepeater" runat="server" ItemType="OpdProject.Models.PatientFamilyHistory" OnItemCommand="FamilyRepeater_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <tr>
              <td><%#: OpdProject.Web.BasePage.TitleCase(Item.RelationshipType) %></td>
              <td><strong><%#: Item.ConditionName %></strong></td>
              <td><%#: string.IsNullOrEmpty(Item.Notes) ? "—" : Item.Notes %></td>
              <td style="white-space:nowrap;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Edit" CommandArgument="<%# Item.FamilyHistoryId %>" CausesValidation="false" ToolTip='<%# "Edit " + Item.RelationshipType + ": " + Item.ConditionName %>'><i class="ti ti-edit" aria-hidden="true"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Delete" CommandArgument="<%# Item.FamilyHistoryId %>" CausesValidation="false" ToolTip='<%# "Remove " + Item.RelationshipType + ": " + Item.ConditionName %>'
                  OnClientClick="return confirm('Remove this family history entry?');"><i class="ti ti-trash" aria-hidden="true"></i></asp:LinkButton>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoFamily" runat="server"><tr class="empty-row"><td colspan="4">No family history recorded.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>

    <asp:Panel ID="SocialPanel" runat="server" DefaultButton="SaveSocialButton">
      <div class="record-section-title">
        <span>Social History</span>
        <asp:LinkButton ID="SaveSocialButton" runat="server" CssClass="btn btn--xs btn--primary" ValidationGroup="Social" OnClick="SaveSocialButton_Click"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save Social History</asp:LinkButton>
      </div>
      <div class="card record-social-grid">
        <div class="form-group">
          <label class="form-label" for="<%= SmokingInput.ClientID %>">Smoking Status</label>
          <asp:DropDownList ID="SmokingInput" runat="server" CssClass="form-select">
            <asp:ListItem Value="">—</asp:ListItem><asp:ListItem Value="NEVER">Never</asp:ListItem>
            <asp:ListItem Value="FORMER">Former</asp:ListItem><asp:ListItem Value="CURRENT">Current</asp:ListItem>
          </asp:DropDownList>
        </div>
        <div class="form-group">
          <label class="form-label" for="<%= PackYearsInput.ClientID %>">Pack/Years</label>
          <asp:TextBox ID="PackYearsInput" runat="server" CssClass="form-control" TextMode="Number" min="0" max="200" placeholder="Former / Current Smokers" />
          <asp:RangeValidator runat="server" ControlToValidate="PackYearsInput" ValidationGroup="Social" Type="Integer" MinimumValue="0" MaximumValue="200" Text="Enter 0–200" CssClass="field-error" Display="Dynamic" />
        </div>
        <div class="form-group">
          <label class="form-label" for="<%= AlcoholInput.ClientID %>">Alcohol Use</label>
          <asp:DropDownList ID="AlcoholInput" runat="server" CssClass="form-select">
            <asp:ListItem Value="">—</asp:ListItem><asp:ListItem Value="NONE">None</asp:ListItem>
            <asp:ListItem Value="OCCASIONAL">Occasional</asp:ListItem><asp:ListItem Value="REGULAR">Regular</asp:ListItem>
          </asp:DropDownList>
        </div>
        <div class="form-group">
          <label class="form-label" for="<%= OccupationInput.ClientID %>">Occupation</label>
          <asp:TextBox ID="OccupationInput" runat="server" CssClass="form-control" MaxLength="80" placeholder="Occupation" />
        </div>
        <div class="form-group">
          <label class="form-label" for="<%= ActivityInput.ClientID %>">Physical Activity</label>
          <asp:DropDownList ID="ActivityInput" runat="server" CssClass="form-select">
            <asp:ListItem Value="">—</asp:ListItem><asp:ListItem Value="SEDENTARY">Sedentary</asp:ListItem>
            <asp:ListItem Value="LIGHT">Light</asp:ListItem><asp:ListItem Value="MODERATE">Moderate</asp:ListItem>
            <asp:ListItem Value="ACTIVE">Active</asp:ListItem>
          </asp:DropDownList>
        </div>
        <div class="form-group">
          <label class="form-label" for="<%= DietInput.ClientID %>">Diet</label>
          <asp:DropDownList ID="DietInput" runat="server" CssClass="form-select">
            <asp:ListItem Value="">—</asp:ListItem><asp:ListItem Value="VEGETARIAN">Vegetarian</asp:ListItem>
            <asp:ListItem Value="EGGETARIAN">Eggetarian</asp:ListItem><asp:ListItem Value="NON_VEGETARIAN">Non-Vegetarian</asp:ListItem>
            <asp:ListItem Value="VEGAN">Vegan</asp:ListItem>
          </asp:DropDownList>
        </div>
      </div>
    </asp:Panel>
  </div>

  <div class="split-panel">
    <asp:Panel ID="FormPanel" runat="server" CssClass="card form-grid" DefaultButton="SaveButton">
      <asp:HiddenField ID="EditingId" runat="server" />
      <div class="form-group form-group--full" style="font-weight:700;"><asp:Literal ID="FormTitle" runat="server" Text="Add Family History" /></div>
      <div class="form-group">
        <label class="form-label" for="<%= RelationInput.ClientID %>">Relationship <span class="required">*</span></label>
        <asp:DropDownList ID="RelationInput" runat="server" CssClass="form-select">
          <asp:ListItem Value="FATHER">Father</asp:ListItem><asp:ListItem Value="MOTHER">Mother</asp:ListItem><asp:ListItem Value="SIBLING">Sibling</asp:ListItem>
          <asp:ListItem Value="SPOUSE">Spouse</asp:ListItem><asp:ListItem Value="CHILD">Child</asp:ListItem><asp:ListItem Value="GRANDPARENT">Grandparent</asp:ListItem>
        </asp:DropDownList>
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= ConditionInput.ClientID %>">Condition <span class="required">*</span></label>
        <asp:TextBox ID="ConditionInput" runat="server" CssClass="form-control" MaxLength="100" placeholder="e.g. Hypertension" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="ConditionInput" ValidationGroup="Family" Text="Condition is required" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group form-group--full">
        <label class="form-label" for="<%= NotesInput.ClientID %>">Notes</label>
        <asp:TextBox ID="NotesInput" runat="server" CssClass="form-control" MaxLength="250" placeholder="e.g. Diagnosed at 50" />
      </div>
      <div class="form-group form-group--full" style="display:flex; gap:8px;">
        <asp:Button ID="SaveButton" runat="server" Text="Add" CssClass="btn btn--success" ValidationGroup="Family" OnClick="SaveButton_Click" />
        <asp:Button ID="ResetButton" runat="server" Text="Reset" CssClass="btn btn--danger" CausesValidation="false" OnClick="ResetButton_Click" />
      </div>
    </asp:Panel>
  </div>
</div>
