<%@ Page Title="Service unavailable" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="service-error.aspx.cs" Inherits="OpdProject.templates.shared.service_error" %>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card" style="text-align:center; padding:40px;">
    <i class="ti ti-cloud-off" style="font-size:48px; color:var(--color-danger);" aria-hidden="true"></i>
    <h3 style="margin:12px 0 8px;">This page could not be loaded</h3>
    <p class="text-secondary">The data service did not answer or refused the request. Nothing on this page was changed.</p>
    <p class="text-xs text-muted">If this keeps happening, contact your administrator.</p>
    <a class="btn btn--primary mt-2" href="<%: RetryUrl %>"><i class="ti ti-refresh" aria-hidden="true"></i> Try again</a>
    <a class="btn btn--ghost mt-2" href="<%: HomeUrl %>"><i class="ti ti-home" aria-hidden="true"></i> Go to my start page</a>
  </div>
</div>
</asp:Content>
