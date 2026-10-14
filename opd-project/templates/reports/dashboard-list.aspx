<%@ Page Title="Dashboard List" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="dashboard-list.aspx.cs" Inherits="OpdProject.templates.reports.dashboard_list" %>
<%-- The appointments behind one dashboard number: ?kind=consultations|appointments|cancelled &period=today|week|month
     [&status=CHECKED_IN…]. Same filter as the dashboard counts (ReportService), so the row count matches the tile.
     Route: /dashboard/list --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card mb-3">
    <div style="display:flex; gap:12px; align-items:center; flex-wrap:wrap;">
      <a class="btn btn--sm" href="<%: OpdProject.Web.Urls.Dashboard %>" title="Back to dashboard"><i class="ti ti-arrow-left" aria-hidden="true"></i> Dashboard</a>
      <div>
        <div style="font-weight:700;"><asp:Literal ID="TitleText" runat="server" /></div>
        <div class="text-xs text-muted"><asp:Literal ID="SubtitleText" runat="server" /></div>
      </div>
    </div>
  </div>
  <div class="card">
    <table class="data-table">
      <thead>
        <tr>
          <th scope="col">Date</th>
          <th scope="col">Time</th>
          <% if (ShowDoctor) { %><th scope="col">Doctor</th><% } %>
          <th scope="col">Patient</th>
          <th scope="col"><%: PatientIdLabel %></th>
          <th scope="col">Type</th>
          <th scope="col">Status</th>
          <th scope="col">Actions</th>
        </tr>
      </thead>
      <tbody>
        <asp:Repeater ID="RowsRepeater" runat="server" ItemType="OpdProject.Models.AppointmentView" OnItemCommand="RowsRepeater_ItemCommand">
          <ItemTemplate>
            <tr>
              <td><%#: FmtDate(Item.SlotStart) %></td>
              <td><%#: FmtTime(Item.SlotStart) %></td>
              <asp:PlaceHolder runat="server" Visible="<%# ShowDoctor %>"><td><%#: Item.DoctorName %></td></asp:PlaceHolder>
              <td><%#: Item.PatientName %></td>
              <td><%#: Item.PatientMrn %></td>
              <td><%#: Item.Type %></td>
              <td><span class="badge <%#: StatusBadge(Item.Status) %>"><%#: StatusLabel(Item.Status) %></span></td>
              <td>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--success" CommandName="CheckIn" CommandArgument="<%# Item.AppointmentId %>" Visible="<%# CanCheckIn(Item) %>" CausesValidation="false">
                  <i class="ti ti-user-check" aria-hidden="true"></i> Check in
                </asp:LinkButton>
                <asp:HyperLink runat="server" CssClass="btn btn--xs btn--primary" NavigateUrl="<%# OpdProject.Web.Urls.Consultation(Item.PatientId) %>" Visible="<%# CanConsult(Item) %>">
                  <i class="ti ti-stethoscope" aria-hidden="true"></i> <%#: Item.Status == "IN_CONSULT" ? "Continue consultation" : "Start consultation" %>
                </asp:HyperLink>
                <a class="btn btn--xs" href="<%#: OpdProject.Web.Urls.Allergies(Item.PatientId) %>"><i class="ti ti-clipboard-heart" aria-hidden="true"></i> Health Record</a>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="EmptyRow" runat="server">
          <tr class="empty-row"><td colspan="8">Nothing in this period.</td></tr>
        </asp:PlaceHolder>
      </tbody>
    </table>
  </div>
</div>
</asp:Content>
