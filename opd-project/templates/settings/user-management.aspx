<%@ Page Title="User Management" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="user-management.aspx.cs" Inherits="OpdProject.templates.settings.user_management" %>
<%-- RBAC user management. Route: /settings/users --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card mb-3">
    <div style="display:flex; justify-content:space-between; align-items:center;">
      <div class="text-sm text-secondary">Manage users, roles, and access. Doctor profiles and timetables are managed in <a href="<%: Urls.To("settings/scheduling") %>?tab=doctors">Schedule Settings</a>.</div>
      <asp:LinkButton ID="AddUserButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="AddUserButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add user</asp:LinkButton>
    </div>
  </div>
  <div class="card">
    <table class="data-table">
      <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Specialty / Lab</th><th>Active</th><th>Actions</th></tr></thead>
      <tbody>
        <asp:Repeater ID="UsersRepeater" runat="server" ItemType="OpdProject.Models.AppUser" OnItemCommand="UsersRepeater_ItemCommand">
          <ItemTemplate>
            <tr>
              <td><%#: Item.FullName %></td>
              <td class="text-xs"><%#: Item.Email %></td>
              <td><span class="badge <%# Item.Role == "ADMIN" ? "badge--danger" : Item.Role == "DOCTOR" ? "badge--success" : "badge--info" %>" style="font-size:10px;"><%#: Item.Role %></span></td>
              <td class="text-xs"><%#: SpecialtyOrLab(Item) %></td>
              <td><span class="badge <%# Item.IsActive ? "badge--success" : "badge--danger" %>"><%# Item.IsActive ? "Active" : "Inactive" %></span></td>
              <td style="white-space:nowrap;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="EditUser" CommandArgument="<%# Item.UserId %>" CausesValidation="false" ToolTip="Edit"><i class="ti ti-edit"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Toggle" CommandArgument="<%# Item.UserId %>" CausesValidation="false" Visible="<%# Item.UserId != CurrentUserId %>"
                  style='<%# "color:" + (Item.IsActive ? "var(--color-danger)" : "var(--color-success)") %>'><%# Item.IsActive ? "Disable" : "Enable" %></asp:LinkButton>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
      </tbody>
    </table>
  </div>
</div>

<asp:Panel ID="UserModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="SaveUserButton">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong><asp:Literal ID="UserModalTitle" runat="server" /></strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModal_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="EditingUserId" runat="server" />
      <asp:ValidationSummary runat="server" ValidationGroup="User" CssClass="validation-summary" />
      <div class="form-grid">
        <div class="form-group"><label class="form-label" for="<%= UserName.ClientID %>">Full name *</label><asp:TextBox ID="UserName" runat="server" CssClass="form-control" MaxLength="80" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="UserName" ValidationGroup="User" ErrorMessage="Full name is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= UserEmail.ClientID %>">Email *</label><asp:TextBox ID="UserEmail" runat="server" CssClass="form-control" TextMode="Email" MaxLength="120" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="UserEmail" ValidationGroup="User" ErrorMessage="Email is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          <asp:RegularExpressionValidator runat="server" ControlToValidate="UserEmail" ValidationGroup="User" ValidationExpression="^[^@\s]+@[^@\s]+\.[^@\s]+$" ErrorMessage="Enter a valid email" Text="Invalid email" CssClass="field-error" Display="Dynamic" />
          <asp:CustomValidator ID="EmailUniqueValidator" runat="server" ControlToValidate="UserEmail" ValidationGroup="User" OnServerValidate="EmailUniqueValidator_ServerValidate" ErrorMessage="Another user already has this email" Text="Already in use" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= UserRole.ClientID %>">Role *</label><asp:DropDownList ID="UserRole" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="UserRole_SelectedIndexChanged" /></div>
        <asp:PlaceHolder ID="SpecialtyHolder" runat="server">
          <div class="form-group"><label class="form-label" for="<%= UserSpecialty.ClientID %>">Specialty</label><asp:DropDownList ID="UserSpecialty" runat="server" CssClass="form-select" /></div>
        </asp:PlaceHolder>
        <asp:PlaceHolder ID="LabPartnerHolder" runat="server" Visible="false">
          <div class="form-group"><label class="form-label" for="<%= UserLabPartner.ClientID %>">Lab / imaging centre *</label>
            <asp:DropDownList ID="UserLabPartner" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="UserLabPartner_SelectedIndexChanged" />
            <asp:CustomValidator ID="LabPartnerValidator" runat="server" ValidationGroup="User" OnServerValidate="LabPartnerValidator_ServerValidate" ErrorMessage="Choose the lab / imaging centre this account belongs to" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
          <asp:PlaceHolder ID="NewLabPartnerHolder" runat="server" Visible="false">
            <div class="form-group"><label class="form-label" for="<%= NewLabPartnerName.ClientID %>">New lab / imaging centre name *</label>
              <asp:TextBox ID="NewLabPartnerName" runat="server" CssClass="form-control" MaxLength="100" placeholder="e.g., Apollo Diagnostics" /></div>
          </asp:PlaceHolder>
          <div class="form-group form-group--full"><div class="hint-banner"><i class="ti ti-info-circle"></i> Lab partner accounts can only open the Lab Portal, and only see orders their lab has received from a patient's requisition.</div></div>
        </asp:PlaceHolder>
        <asp:PlaceHolder ID="PasswordHolder" runat="server">
          <div class="form-group"><label class="form-label" for="<%= UserPassword.ClientID %>">Initial password *</label><asp:TextBox ID="UserPassword" runat="server" CssClass="form-control" TextMode="Password" />
            <asp:RequiredFieldValidator ID="PasswordRequired" runat="server" ControlToValidate="UserPassword" ValidationGroup="User" ErrorMessage="Password is required" Text="Required" CssClass="field-error" Display="Dynamic" />
            <asp:RegularExpressionValidator runat="server" ControlToValidate="UserPassword" ValidationGroup="User" ValidationExpression="^.{6,}$" ErrorMessage="Password must be at least 6 characters" Text="Min. 6 characters" CssClass="field-error" Display="Dynamic" /></div>
        </asp:PlaceHolder>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModal_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="SaveUserButton" runat="server" CssClass="btn btn--success" ValidationGroup="User" OnClick="SaveUserButton_Click">Save</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:Content>
