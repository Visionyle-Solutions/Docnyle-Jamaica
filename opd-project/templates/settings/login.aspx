<%@ Page Title="Sign in" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="login.aspx.cs" Inherits="OpdProject.templates.settings.login" %>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div style="display:flex; justify-content:center; align-items:center; min-height:80vh;">
  <asp:Panel ID="LoginCard" runat="server" CssClass="card" style="width:400px; padding:32px;" DefaultButton="SignInButton">
    <div style="text-align:center; margin-bottom:20px;">
      <img src="<%: ResolveUrl("~/assets/Medcity.png") %>" alt="Consult360" style="max-height:90px; margin-bottom:8px;" />
      <div style="font-size:16px; font-weight:600; margin:8px 0 2px;"><asp:Literal ID="HospitalNameText" runat="server" /></div>
      <div class="text-xs text-muted"><asp:Literal ID="HospitalAddressText" runat="server" /></div>
      <div class="text-sm text-secondary" style="margin-top:12px;">Sign in to continue</div>
    </div>
    <asp:Panel ID="ErrorPanel" runat="server" CssClass="validation-summary" Visible="false">
      <asp:Literal ID="ErrorText" runat="server" />
    </asp:Panel>
    <div class="form-group">
      <label class="form-label" for="<%= EmailInput.ClientID %>">Email</label>
      <asp:TextBox ID="EmailInput" runat="server" CssClass="form-control" TextMode="Email" placeholder="doctor@clinic.com" Text="drsharma@sunrise.in" />
      <asp:RequiredFieldValidator runat="server" ControlToValidate="EmailInput" ErrorMessage="Enter your email" CssClass="field-error" Display="Dynamic" />
    </div>
    <div class="form-group">
      <label class="form-label" for="<%= PasswordInput.ClientID %>">Password</label>
      <asp:TextBox ID="PasswordInput" runat="server" CssClass="form-control" TextMode="Password" placeholder="Password" />
      <asp:RequiredFieldValidator runat="server" ControlToValidate="PasswordInput" ErrorMessage="Enter your password" CssClass="field-error" Display="Dynamic" />
    </div>
    <asp:Button ID="SignInButton" runat="server" Text="Sign in" CssClass="btn btn--primary mt-3" style="width:100%; justify-content:center;" OnClick="SignInButton_Click" />
    <div class="text-xs text-muted mt-2" style="text-align:center; line-height:1.4;">By signing in you confirm you are the account holder.<br/>Credential sharing violates clinic policy and data protection regulations.</div>
    <div class="text-xs text-muted mt-3" style="border-top:1px solid var(--border-default); padding-top:10px;">
      <strong>Demo accounts</strong> (password <code>demo123</code> unless noted):
      <div style="display:grid; grid-template-columns:auto 1fr; gap:2px 8px; margin-top:4px;">
        <span>Doctor</span><span>drsharma@sunrise.in</span>
        <span>Reception</span><span>priya@sunrise.in</span>
        <span>Nurse</span><span>nurse@sunrise.in</span>
        <span>Billing</span><span>billing@sunrise.in</span>
        <span>Admin</span><span>admin@sunrise.in · <code>admin123</code></span>
        <span>Lab partner</span><span>lab@sunrisediag.in · <code>lab123</code></span>
      </div>
      <div class="mt-2"><a href="<%: ResolveUrl("~/portal/register") %>">Patient self-registration</a> · <a href="<%: ResolveUrl("~/portal/book") %>">Book an appointment</a></div>
    </div>
  </asp:Panel>
</div>
</asp:Content>
