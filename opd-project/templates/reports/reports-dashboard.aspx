<%@ Page Title="Reports Dashboard" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="reports-dashboard.aspx.cs" Inherits="OpdProject.templates.reports.reports_dashboard" %>
<%-- Five reports over a date range: consultation volume, diagnosis frequency, prescribing, appointment
     status / no-show rate, revenue by doctor. Route: /reports --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <asp:Panel ID="FilterCard" runat="server" CssClass="card mb-3" DefaultButton="RefreshButton">
    <div style="display:flex; gap:12px; align-items:flex-end; flex-wrap:wrap;">
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= FromDate.ClientID %>">From</label><asp:TextBox ID="FromDate" runat="server" CssClass="form-control" TextMode="Date" /></div>
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= ToDate.ClientID %>">To</label><asp:TextBox ID="ToDate" runat="server" CssClass="form-control" TextMode="Date" />
        <asp:CompareValidator runat="server" ControlToValidate="ToDate" ControlToCompare="FromDate" Operator="GreaterThanEqual" Type="Date" Text="'To' must be on or after 'From'" CssClass="field-error" Display="Dynamic" /></div>
      <asp:LinkButton ID="RefreshButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="RefreshButton_Click"><i class="ti ti-refresh" aria-hidden="true"></i> Refresh</asp:LinkButton>
      <div class="text-xs text-muted"><asp:Literal ID="RangeText" runat="server" /></div>
    </div>
  </asp:Panel>
  <div style="display:grid; grid-template-columns:1fr 1fr; gap:16px;">
    <div class="card"><asp:Literal ID="VolumeHtml" runat="server" /></div>
    <div class="card"><asp:Literal ID="DiagnosisHtml" runat="server" /></div>
    <div class="card"><asp:Literal ID="RxHtml" runat="server" /></div>
    <div class="card"><asp:Literal ID="NoShowHtml" runat="server" /></div>
    <div class="card" style="grid-column:span 2;"><asp:Literal ID="RevenueHtml" runat="server" /></div>
  </div>
</div>
</asp:Content>
