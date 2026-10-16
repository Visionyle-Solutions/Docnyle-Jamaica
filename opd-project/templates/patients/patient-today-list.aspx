<%@ Page Title="Today's Patients" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="patient-today-list.aspx.cs" Inherits="OpdProject.templates.patients.patient_today_list" %>
<%-- Appointment summary: one day's appointment queue (today by default, ?date=yyyy-MM-dd for another day); dashboard
     tiles open it with ?kind=appointments|consultations|cancelled [&status=…] preselected. A day's queue, not a flat alphabetical roster. A doctor sees their own queue; the
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
        <label class="form-label" for="<%= DateFilter.ClientID %>">Date</label>
        <asp:TextBox ID="DateFilter" runat="server" CssClass="form-control" TextMode="Date" AutoPostBack="true" OnTextChanged="DateFilter_Changed" />
      </div>
      <div class="form-group" style="margin:0;">
        <label class="form-label" for="<%= KindFilter.ClientID %>">Show</label>
        <asp:DropDownList ID="KindFilter" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="KindFilter_Changed" />
      </div>
      <div class="form-group" style="margin:0;">
        <label class="form-label" for="<%= StatusFilter.ClientID %>">Status</label>
        <asp:DropDownList ID="StatusFilter" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="StatusFilter_Changed" />
      </div>
      <div style="flex:1;"></div>
      <div class="text-sm text-secondary" style="display:flex; gap:12px; flex-wrap:wrap;">
        <span><span class="badge badge--info"><asp:Literal ID="CountBooked" runat="server" /> <%: StatusLabel("BOOKED") %> </span></span>
        <span><span class="badge badge--warning"><asp:Literal ID="CountCheckedIn" runat="server" /> <%: StatusLabel("CHECKED_IN") %> </span></span>
        <span><span class="badge badge--warning"><asp:Literal ID="CountInConsult" runat="server" /> <%: StatusLabel("IN_CONSULT") %> </span> </span>
        <span><span class="badge badge--success"><asp:Literal ID="CountCompleted" runat="server" /> <%: StatusLabel("COMPLETED") %> </span> </span>
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
              <td><%# RowOffset + Container.ItemIndex + 1 %></td>
              <td><%#: EventTime(Item) %></td>
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
          <tr class="empty-row"><td colspan="9"><%: StatusFilter.SelectedValue == "" ? "No " + KindFilter.SelectedItem.Text.ToLower() + " " + DayText + "." : "No " + KindFilter.SelectedItem.Text.ToLower() + " with this status " + DayText + "." %></td></tr>
        </asp:PlaceHolder>
      </tbody>
    </table>
    <%-- pager: page size on the left, Prev/Next centred, record range on the right --%>
    <div style="display:grid; grid-template-columns:1fr auto 1fr; gap:12px; align-items:center; margin-top:12px;">
      <div class="text-sm text-secondary" style="display:flex; gap:8px; align-items:center;">
        <label class="form-label" for="<%= PageSizeFilter.ClientID %>" style="margin:0;">Show</label>
        <asp:DropDownList ID="PageSizeFilter" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="PageSizeFilter_Changed" style="width:auto;" />
        <span>records per page</span>
      </div>
      <div style="display:flex; gap:8px; align-items:center; justify-content:center;">
        <asp:LinkButton ID="PrevPageButton" runat="server" CssClass="btn btn--xs" OnClick="PrevPage_Click" aria-label="Previous page"><i class="ti ti-chevron-left" aria-hidden="true"></i> Prev</asp:LinkButton>
        <asp:LinkButton ID="NextPageButton" runat="server" CssClass="btn btn--xs" OnClick="NextPage_Click" aria-label="Next page">Next <i class="ti ti-chevron-right" aria-hidden="true"></i></asp:LinkButton>
      </div>
      <div class="text-sm text-secondary" style="text-align:right;"><asp:Literal ID="PageInfo" runat="server" /></div>
    </div>
  </div>
</div>
</asp:Content>
