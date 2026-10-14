<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="specialty-config.ascx.cs" Inherits="OpdProject.templates.settings.specialty_config" %>
<%-- Settings → Clinical Config → Specialties --%>
<div class="settings-section mt-3">
  <div class="settings-section-header">Specialties</div>
  <asp:Panel ID="AddPanel" runat="server" DefaultButton="AddSpecialtyButton" style="display:flex; gap:8px; margin-bottom:10px;">
    <asp:TextBox ID="NewSpecialtyName" runat="server" CssClass="form-control" style="max-width:300px;" placeholder="New specialty name" MaxLength="60" />
    <asp:LinkButton ID="AddSpecialtyButton" runat="server" CssClass="btn btn--sm btn--primary" ValidationGroup="Specialty" OnClick="AddSpecialtyButton_Click"><i class="ti ti-plus" aria-hidden="true"></i> Add</asp:LinkButton>
    <asp:RequiredFieldValidator runat="server" ControlToValidate="NewSpecialtyName" ValidationGroup="Specialty" Text="Enter a name" CssClass="field-error" Display="Dynamic" />
  </asp:Panel>
  <table class="data-table" style="font-size:12px;">
    <thead><tr><th>Specialty</th><th>Status</th><th>Actions</th></tr></thead>
    <tbody>
      <asp:Repeater ID="SpecialtiesRepeater" runat="server" ItemType="OpdProject.Models.Specialty" OnItemCommand="SpecialtiesRepeater_ItemCommand">
        <ItemTemplate>
          <tr>
            <td><%#: Item.SpecialtyName %></td>
            <td><span class="badge <%# Item.IsActive ? "badge--success" : "badge--danger" %>"><%# Item.IsActive ? "Active" : "Inactive" %></span></td>
            <td><asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Toggle" CommandArgument="<%# Item.SpecialtyId %>" CausesValidation="false"
              style='<%# "color:" + (Item.IsActive ? "var(--color-danger)" : "var(--color-success)") %>'><%# Item.IsActive ? "Deactivate" : "Activate" %></asp:LinkButton></td>
          </tr>
        </ItemTemplate>
      </asp:Repeater>
    </tbody>
  </table>
</div>
