<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="patient-allergies-body.ascx.cs" Inherits="OpdProject.templates.patients.patient_allergies_body" %>
<%-- Allergies table region of the Patient Allergies page --%>
<div style="display:flex; justify-content:flex-end; margin-bottom:6px;">
  <asp:CheckBox ID="ShowInactive" runat="server" Text=" Show Inactive" CssClass="text-xs text-muted" AutoPostBack="true" OnCheckedChanged="ShowInactive_CheckedChanged" />
</div>
<table class="data-table">
  <thead>
    <tr>
      <th scope="col">Allergy Type</th>
      <th scope="col">Allergy Name</th>
      <th scope="col">Reaction Type</th>
      <th scope="col">Reaction</th>
      <th scope="col">Severity</th>
      <th scope="col">Allergy Status</th>
      <th scope="col">Is Treated</th>
      <th scope="col">Actions</th>
    </tr>
  </thead>
  <tbody>
    <asp:Repeater ID="ItemsRepeater" runat="server" ItemType="OpdProject.Models.PatientAllergy" OnItemCommand="ItemsRepeater_ItemCommand" ClientIDMode="Predictable">
      <ItemTemplate>
        <tr>
          <td><%#: OpdProject.Web.BasePage.TitleCase(Item.AllergyType) %></td>
          <td><%#: Item.AllergyName %></td>
          <td><%#: Item.ReactionType %></td>
          <td><%#: Item.ReactionDescription %></td>
          <td><span class="badge <%#: Item.Severity == "SEVERE" ? "badge--danger" : Item.Severity == "MODERATE" ? "badge--warning" : "badge--info" %>"><%#: OpdProject.Web.BasePage.TitleCase(Item.Severity) %></span></td>
          <td><%#: OpdProject.Web.BasePage.TitleCase(Item.Status) %></td>
          <td><%#: Item.IsTreated ? "Yes" : "No" %></td>
          <td style="white-space:nowrap;">
            <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="EditAllergy" CommandArgument="<%# Item.AllergyId %>" CausesValidation="false" ToolTip='<%# "Edit allergy " + Item.AllergyName %>'>
              <i class="ti ti-edit" aria-hidden="true"></i>
            </asp:LinkButton>
            <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Deactivate" CommandArgument="<%# Item.AllergyId %>" CausesValidation="false" Visible='<%# Item.Status == "ACTIVE" %>'
              ToolTip='<%# "Deactivate allergy " + Item.AllergyName %>' OnClientClick="return confirm('Mark this allergy as inactive? This does not delete the record.');">
              <i class="ti ti-trash" aria-hidden="true"></i>
            </asp:LinkButton>
          </td>
        </tr>
      </ItemTemplate>
    </asp:Repeater>
    <asp:PlaceHolder ID="EmptyRow" runat="server">
      <tr class="empty-row"><td colspan="8">No allergies found.</td></tr>
    </asp:PlaceHolder>
  </tbody>
</table>
