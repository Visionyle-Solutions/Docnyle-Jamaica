<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="patient-allergies-form.ascx.cs" Inherits="OpdProject.templates.patients.patient_allergies_form" %>
<%-- Allergy add/edit form region. Two fields per row (components.css .form-grid). --%>
<asp:Panel ID="FormPanel" runat="server" CssClass="card form-grid" DefaultButton="SaveButton">
  <div class="form-group form-group--full" style="font-weight:700;"><asp:Literal ID="FormTitle" runat="server" Text="Add Allergy" /></div>
  <div class="form-group">
    <label class="form-label" for="<%= AllergyTypeInput.ClientID %>">Allergy Type <span class="required">*</span></label>
    <asp:DropDownList ID="AllergyTypeInput" runat="server" CssClass="form-select">
      <asp:ListItem Value="DRUG">Drug Allergy</asp:ListItem>
      <asp:ListItem Value="FOOD">Food Allergy</asp:ListItem>
      <asp:ListItem Value="ENVIRONMENTAL">Environmental Allergy</asp:ListItem>
      <asp:ListItem Value="OTHER">Other</asp:ListItem>
    </asp:DropDownList>
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= AllergyNameInput.ClientID %>">Allergy Name <span class="required">*</span></label>
    <asp:TextBox ID="AllergyNameInput" runat="server" CssClass="form-control" placeholder="Enter Allergy Name" MaxLength="100" />
    <asp:RequiredFieldValidator runat="server" ControlToValidate="AllergyNameInput" ValidationGroup="Allergy" Text="Allergy name is required" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= ReactionTypeInput.ClientID %>">Reaction Type <span class="required">*</span></label>
    <asp:DropDownList ID="ReactionTypeInput" runat="server" CssClass="form-select">
      <asp:ListItem>Allergy</asp:ListItem>
      <asp:ListItem>Intolerance</asp:ListItem>
    </asp:DropDownList>
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= ReactionInput.ClientID %>">Reaction <span class="required">*</span></label>
    <asp:TextBox ID="ReactionInput" runat="server" CssClass="form-control" placeholder="Enter Reaction Name" MaxLength="200" />
    <asp:RequiredFieldValidator runat="server" ControlToValidate="ReactionInput" ValidationGroup="Allergy" Text="Reaction is required" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= SeverityInput.ClientID %>">Severity <span class="required">*</span></label>
    <asp:DropDownList ID="SeverityInput" runat="server" CssClass="form-select">
      <asp:ListItem Value="MILD">Mild</asp:ListItem>
      <asp:ListItem Value="MODERATE">Moderate</asp:ListItem>
      <asp:ListItem Value="SEVERE">Severe</asp:ListItem>
    </asp:DropDownList>
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= StatusInput.ClientID %>">Status <span class="required">*</span></label>
    <asp:DropDownList ID="StatusInput" runat="server" CssClass="form-select">
      <asp:ListItem Value="ACTIVE">Active</asp:ListItem>
      <asp:ListItem Value="INACTIVE">Inactive</asp:ListItem>
    </asp:DropDownList>
  </div>
  <div class="form-group">
    <label class="form-label">Is Treated</label>
    <asp:RadioButtonList ID="IsTreatedInput" runat="server" RepeatLayout="Flow" RepeatDirection="Horizontal" CssClass="form-radio-group">
      <asp:ListItem Value="true"> Yes</asp:ListItem>
      <asp:ListItem Value="false" Selected="True"> No</asp:ListItem>
    </asp:RadioButtonList>
  </div>
  <div class="form-group">
    <label class="form-label">Is Family History</label>
    <asp:RadioButtonList ID="IsFamilyInput" runat="server" RepeatLayout="Flow" RepeatDirection="Horizontal" CssClass="form-radio-group">
      <asp:ListItem Value="true"> Yes</asp:ListItem>
      <asp:ListItem Value="false" Selected="True"> No</asp:ListItem>
    </asp:RadioButtonList>
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= SinceWhenInput.ClientID %>">Since When <span class="required">*</span></label>
    <asp:TextBox ID="SinceWhenInput" runat="server" CssClass="form-control" TextMode="Date" />
    <asp:RequiredFieldValidator runat="server" ControlToValidate="SinceWhenInput" ValidationGroup="Allergy" Text="Since-when is required" CssClass="field-error" Display="Dynamic" />
    <asp:CustomValidator runat="server" ControlToValidate="SinceWhenInput" ValidationGroup="Allergy" OnServerValidate="SinceWhen_ServerValidate" Text="Cannot be in the future" CssClass="field-error" Display="Dynamic" />
  </div>
  <div class="form-group">
    <label class="form-label" for="<%= NotesInput.ClientID %>">Notes</label>
    <asp:TextBox ID="NotesInput" runat="server" CssClass="form-control" placeholder="Enter Notes" MaxLength="250" />
  </div>
  <asp:HiddenField ID="EditingId" runat="server" />
  <div class="form-group form-group--full" style="display:flex; gap:8px;">
    <asp:Button ID="SaveButton" runat="server" Text="Add" CssClass="btn btn--success" ValidationGroup="Allergy" OnClick="SaveButton_Click" />
    <asp:Button ID="ResetButton" runat="server" Text="Reset" CssClass="btn btn--danger" CausesValidation="false" OnClick="ResetButton_Click" />
  </div>
</asp:Panel>
