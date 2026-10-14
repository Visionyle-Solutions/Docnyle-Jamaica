<%@ Page Title="Drug Formulary" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="formulary-management.aspx.cs" Inherits="OpdProject.templates.settings.formulary_management" %>
<%-- Drug formulary master data. Route: /settings/formulary --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <asp:Panel ID="SearchCard" runat="server" CssClass="card mb-3" DefaultButton="SearchButton">
    <div style="display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
      <asp:TextBox ID="SearchInput" runat="server" CssClass="form-control" style="flex:1; min-width:200px;" placeholder="Search by brand, generic, or class..." />
      <asp:CheckBox ID="ShowInactive" runat="server" Text=" Include inactive" CssClass="text-xs" AutoPostBack="true" OnCheckedChanged="SearchButton_Click" />
      <asp:LinkButton ID="SearchButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="SearchButton_Click" CausesValidation="false"><i class="ti ti-search" aria-hidden="true"></i> Search</asp:LinkButton>
      <asp:LinkButton ID="AddDrugButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="AddDrugButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add drug</asp:LinkButton>
    </div>
  </asp:Panel>
  <div class="card">
    <table class="data-table">
      <thead><tr><th>Brand</th><th>Generic</th><th>Form</th><th>Class</th><th>Default dosage</th><th>Active</th><th>Actions</th></tr></thead>
      <tbody>
        <asp:Repeater ID="DrugsRepeater" runat="server" ItemType="OpdProject.Models.Drug" OnItemCommand="DrugsRepeater_ItemCommand">
          <ItemTemplate>
            <tr>
              <td><strong><%#: Item.BrandName %></strong><%#: Item.IsControlledSubstance ? " ⚠" : "" %></td>
              <td><%#: Item.GenericName %></td>
              <td><%#: Item.DrugForm %></td>
              <td style="color:var(--color-danger); font-weight:600; font-size:12px;"><%#: Item.TherapeuticClass %></td>
              <td class="text-xs"><%#: Item.DefaultDosage %> · <%#: Item.DefaultFrequency %> · <%#: Item.DefaultDurationDays %>d</td>
              <td><span class="badge <%# Item.IsActive ? "badge--success" : "badge--danger" %>"><%# Item.IsActive ? "Active" : "Inactive" %></span></td>
              <td style="white-space:nowrap;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="EditDrug" CommandArgument="<%# Item.DrugId %>" CausesValidation="false" ToolTip="Edit"><i class="ti ti-edit"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Toggle" CommandArgument="<%# Item.DrugId %>" CausesValidation="false"
                  style='<%# "color:" + (Item.IsActive ? "var(--color-danger)" : "var(--color-success)") %>'><%# Item.IsActive ? "Deactivate" : "Activate" %></asp:LinkButton>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="EmptyRow" runat="server"><tr class="empty-row"><td colspan="7">No drugs found.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>
  </div>
</div>

<asp:Panel ID="DrugModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="SaveDrugButton">
  <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true">
    <div class="modal-header"><strong><asp:Literal ID="DrugModalTitle" runat="server" /></strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModal_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="EditingDrugId" runat="server" />
      <asp:ValidationSummary runat="server" ValidationGroup="Drug" CssClass="validation-summary" />
      <div class="form-grid">
        <div class="form-group"><label class="form-label" for="<%= FdBrand.ClientID %>">Brand name *</label><asp:TextBox ID="FdBrand" runat="server" CssClass="form-control" MaxLength="60" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="FdBrand" ValidationGroup="Drug" ErrorMessage="Brand name is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:CustomValidator runat="server" ControlToValidate="FdBrand" ValidationGroup="Drug" OnServerValidate="BrandUnique_ServerValidate" ErrorMessage="A drug with this brand name already exists" Text="Already exists" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FdGeneric.ClientID %>">Generic name *</label><asp:TextBox ID="FdGeneric" runat="server" CssClass="form-control" MaxLength="100" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="FdGeneric" ValidationGroup="Drug" ErrorMessage="Generic name is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FdComposition.ClientID %>">Composition</label><asp:TextBox ID="FdComposition" runat="server" CssClass="form-control" MaxLength="120" /></div>
        <div class="form-group"><label class="form-label" for="<%= FdClass.ClientID %>">Therapeutic class *</label><asp:TextBox ID="FdClass" runat="server" CssClass="form-control" MaxLength="60" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="FdClass" ValidationGroup="Drug" ErrorMessage="Therapeutic class is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FdGenericClass.ClientID %>">Generic class</label><asp:TextBox ID="FdGenericClass" runat="server" CssClass="form-control" MaxLength="60" placeholder="Used for therapeutic-duplication checks" /></div>
        <div class="form-group"><label class="form-label" for="<%= FdForm.ClientID %>">Form *</label>
          <asp:DropDownList ID="FdForm" runat="server" CssClass="form-select"><asp:ListItem>Tablet</asp:ListItem><asp:ListItem>Capsule</asp:ListItem><asp:ListItem>Syrup</asp:ListItem><asp:ListItem>Injection</asp:ListItem><asp:ListItem>Drops</asp:ListItem><asp:ListItem>Nasal Drops</asp:ListItem><asp:ListItem>Topical</asp:ListItem><asp:ListItem>Topical Paint</asp:ListItem><asp:ListItem>Inhaler</asp:ListItem><asp:ListItem>Sachet</asp:ListItem></asp:DropDownList></div>
        <div class="form-group"><label class="form-label" for="<%= FdDosage.ClientID %>">Default dosage</label><asp:TextBox ID="FdDosage" runat="server" CssClass="form-control" placeholder="e.g. 500mg" /></div>
        <div class="form-group"><label class="form-label" for="<%= FdFrequency.ClientID %>">Default frequency</label><asp:TextBox ID="FdFrequency" runat="server" CssClass="form-control" placeholder="e.g. 1-0-1" />
          <asp:RegularExpressionValidator runat="server" ControlToValidate="FdFrequency" ValidationGroup="Drug" ValidationExpression="^\d(-\d){2,3}$" ErrorMessage="Frequency must look like 1-0-1" Text="Format 1-0-1" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FdDuration.ClientID %>">Default duration (days)</label><asp:TextBox ID="FdDuration" runat="server" CssClass="form-control" TextMode="Number" Text="5" />
          <asp:RangeValidator runat="server" ControlToValidate="FdDuration" ValidationGroup="Drug" Type="Integer" MinimumValue="1" MaximumValue="365" ErrorMessage="Duration must be 1–365 days" Text="1–365" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= FdInstructions.ClientID %>">Instructions</label><asp:TextBox ID="FdInstructions" runat="server" CssClass="form-control" placeholder="e.g. After food" /></div>
        <div class="form-group"><asp:CheckBox ID="FdControlled" runat="server" Text=" Controlled substance (warning when prescribing)" /></div>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModal_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="SaveDrugButton" runat="server" CssClass="btn btn--success" ValidationGroup="Drug" OnClick="SaveDrugButton_Click">Save</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:Content>
