<%@ Page Title="Dashboard" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="home-dashboard.aspx.cs" Inherits="OpdProject.templates.reports.home_dashboard" %>
<%-- Home dashboard: consultations, appointments and cancelled appointments for today / this week / this month,
     and today's visits by status (pie). Doctors see their own numbers; other roles see the whole hospital.
     Route: /dashboard --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container page-container--wide">
  <h1 class="dash-welcome"><asp:Literal ID="WelcomeText" runat="server" /></h1>
  <div class="dash-grid">
    <div class="card dash-card"><asp:Literal ID="ConsultationsHtml" runat="server" /></div>
    <div class="card dash-card"><asp:Literal ID="AppointmentsHtml" runat="server" /></div>
    <div class="card dash-card"><asp:Literal ID="CancelledHtml" runat="server" /></div>
    <div class="card dash-card dash-card--chart"><asp:Literal ID="StatusChartHtml" runat="server" /></div>
  </div>
</div>
</asp:Content>
