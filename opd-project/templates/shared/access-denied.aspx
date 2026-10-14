<%@ Page Title="Access denied" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="access-denied.aspx.cs" Inherits="OpdProject.templates.shared.access_denied" %>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card" style="text-align:center; padding:40px;">
    <i class="ti ti-shield-lock" style="font-size:48px; color:var(--color-danger);"></i>
    <h3 style="margin:12px 0 8px;">Access Denied</h3>
    <p class="text-secondary">Your role (<%: Role %>) does not have permission to access <%: RequestedPage %>.</p>
    <p class="text-xs text-muted">Contact your administrator to request access.</p>
    <a class="btn btn--primary mt-2" href="<%: Navigation.LandingFor(Role) %>"><i class="ti ti-home" aria-hidden="true"></i> Go to my start page</a>
  </div>
</div>
</asp:Content>
