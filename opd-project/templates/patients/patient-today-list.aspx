<%@ Page Title="Today's Patients" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="patient-today-list.aspx.cs" Inherits="OpdProject.templates.patients.patient_today_list" %>
<%-- Today's appointment queue, not a flat alphabetical roster. A doctor sees their own queue; the
     front desk (receptionist, admin, nurse) sees every doctor's appointments for the day and checks
     patients in. Visit lifecycle: BOOKED → CHECKED_IN → IN_CONSULT → COMPLETED. --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container titlecase-headings">
  <div class="card mb-3">
    <div style="display:flex; gap:12px; align-items:flex-end; flex-wrap:wrap;">
      <div>
        <div style="font-weight:700;">Appointment Summary</div>
        <div class="text-xs text-muted"><asp:Literal ID="SubtitleText" runat="server" /></div>
      </div>
      <asp:PlaceHolder ID="DoctorFilterHolder" runat="server">
        <div class="form-group" style="margin:0;">
          <label class="form-label" for="<%= DoctorFilter.ClientID %>">Doctor</label>
          <asp:DropDownList ID="DoctorFilter" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="DoctorFilter_Changed" />
        </div>
      </asp:PlaceHolder>
      <div class="form-group" style="margin:0;">
        <label class="form-label" for="<%= StatusFilter.ClientID %>">Status</label>
        <asp:DropDownList ID="StatusFilter" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="StatusFilter_Changed" />
      </div>
      <div style="flex:1;"></div>
      <div class="text-sm text-secondary" style="display:flex; gap:12px; flex-wrap:wrap;">
        <span><span class="badge badge--info"><%: StatusLabel("BOOKED") %></span> <asp:Literal ID="CountBooked" runat="server" /></span>
        <span><span class="badge badge--warning"><%: StatusLabel("CHECKED_IN") %></span> <asp:Literal ID="CountCheckedIn" runat="server" /></span>
        <span><span class="badge badge--warning"><%: StatusLabel("IN_CONSULT") %></span> <asp:Literal ID="CountInConsult" runat="server" /></span>
        <span><span class="badge badge--success"><%: StatusLabel("COMPLETED") %></span> <asp:Literal ID="CountCompleted" runat="server" /></span>
      </div>
    </div>
  </div>
  <div class="card">
    <table class="data-table">
      <thead>
        <tr>
          <th scope="col">S.No</th>
          <th scope="col">Time</th>
          <% if (ShowDoctor) { %><th scope="col">Doctor</th><% } %>
          <th scope="col">Patient Name</th>
          <th scope="col"><%: PatientIdLabel %></th>
          <th scope="col">Type</th>
          <th scope="col">Status</th>
          <th scope="col">Actions</th>
        </tr>
      </thead>
      <tbody>
        <asp:Repeater ID="AppointmentsRepeater" runat="server" ItemType="OpdProject.Models.AppointmentView" OnItemCommand="AppointmentsRepeater_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <tr>
              <td><%# Container.ItemIndex + 1 %></td>
              <td><%#: FmtTime(Item.SlotStart) %></td>
              <asp:PlaceHolder runat="server" Visible="<%# ShowDoctor %>"><td><%#: Item.DoctorName %><div class="text-xs text-muted"><%#: Item.DoctorRoom %></div></td></asp:PlaceHolder>
              <td><%#: Item.PatientName %></td>
              <td><%#: Item.PatientMrn %></td>
              <td><%#: AppointmentTypeName(Item.Type) %></td>
              <td><span class="badge <%#: StatusBadge(Item.Status) %>"><%#: StatusLabel(Item.Status) %></span><div class="text-xs text-muted"><%#: StatusTime(Item) %></div></td>
              <td>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--success" CommandName="CheckIn" CommandArgument="<%# Item.AppointmentId %>" Visible="<%# CanCheckIn(Item) %>">
                  <i class="ti ti-user-check" aria-hidden="true"></i> Check In
                </asp:LinkButton>
                <asp:HyperLink runat="server" CssClass="btn btn--xs btn--primary" NavigateUrl="<%# Urls.Consultation(Item.PatientId) %>" Visible="<%# CanConsult(Item) %>">
                  <i class="ti ti-stethoscope" aria-hidden="true"></i> <%#: Item.Status == "IN_CONSULT" ? "Continue Consultation" : "Start Consultation" %>
                </asp:HyperLink>
                <span class="text-xs text-muted"><%#: WaitingNote(Item) %></span>
                <a class="btn btn--xs" href="<%#: Urls.Allergies(Item.PatientId) %>"><i class="ti ti-clipboard-heart" aria-hidden="true"></i> Health Record</a>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="EmptyRow" runat="server">
          <tr class="empty-row"><td colspan="8"><%: StatusFilter.SelectedValue == "" ? "No appointments scheduled for today." : "No appointments with this status today." %></td></tr>
        </asp:PlaceHolder>
      </tbody>
    </table>
  </div>
</div>
</asp:Content>
