<%@ Page Title="Queue Display" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="queue-display.aspx.cs" Inherits="OpdProject.templates.scheduling.queue_display" %>
<%-- Token / queue display (TV-ready). Server-rendered; the page refreshes itself every 30 seconds.
     ?tv=1 hides the sidebar and top bar for a waiting-room screen. Route: /queue-display --%>
<asp:Content ContentPlaceHolderID="HeadContent" runat="server">
  <meta http-equiv="refresh" content="30" />
</asp:Content>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div style="padding:20px; background:var(--surface-page); min-height:90vh;">
  <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:16px; flex-wrap:wrap; gap:8px;">
    <div>
      <h2 style="margin:0; font-size:24px;"><%: Hospital.Name %></h2>
      <div class="text-muted"><%: FmtDate(Today) %> · <%: FmtTime(HospitalNow) %></div>
    </div>
    <div style="display:flex; gap:12px; align-items:center;">
      <div class="badge badge--success" style="font-size:14px; padding:8px 16px;"><%: Completed.Count %> seen</div>
      <div class="badge badge--info" style="font-size:14px; padding:8px 16px;"><%: Waiting.Count %> waiting</div>
      <div class="badge" style="font-size:14px; padding:8px 16px;"><%: Upcoming.Count %> upcoming</div>
      <a class="btn btn--xs btn--ghost no-print" href="<%: TvModeUrl %>"><i class="ti ti-device-tv" aria-hidden="true"></i> <%: IsTvMode ? "Exit TV mode" : "TV mode" %></a>
    </div>
  </div>

  <asp:Repeater ID="NowRepeater" runat="server" ItemType="OpdProject.Models.AppointmentView">
    <ItemTemplate>
      <div class="card mb-3" style="border-left:4px solid var(--color-success); background:#f0fdf4;">
        <div class="text-xs text-muted" style="text-transform:uppercase; font-weight:600;">Now Consulting</div>
        <div style="font-size:20px; font-weight:700; margin:4px 0;"><%#: Item.PatientName %></div>
        <div class="text-sm text-muted"><%#: Item.DoctorName %><%#: string.IsNullOrEmpty(Item.DoctorRoom) ? "" : " · " + Item.DoctorRoom %> · <%#: EventTime(Item) %></div>
      </div>
    </ItemTemplate>
  </asp:Repeater>

  <div class="card mb-3">
    <div class="text-xs text-muted mb-2" style="text-transform:uppercase; font-weight:600;">Waiting (<%: Waiting.Count %>)</div>
    <asp:PlaceHolder ID="NoWaiting" runat="server"><div class="text-muted" style="padding:12px;">No patients waiting.</div></asp:PlaceHolder>
    <asp:Repeater ID="WaitingRepeater" runat="server" ItemType="OpdProject.Models.AppointmentView">
      <HeaderTemplate>
        <table style="width:100%; border-collapse:collapse; font-size:16px;">
          <thead><tr style="border-bottom:2px solid var(--border-default);"><th style="padding:8px; text-align:left;">Token</th><th style="text-align:left;">Patient</th><th style="text-align:left;">Doctor</th><th style="text-align:left;">Time</th><th style="text-align:center;">Est. Wait</th></tr></thead><tbody>
      </HeaderTemplate>
      <ItemTemplate>
        <tr style="border-bottom:1px solid var(--border-subtle,#f1f5f9); <%# Container.ItemIndex == 0 ? "background:#eff6ff; font-weight:600;" : "" %>">
          <td style="padding:10px 8px;"><span style="display:inline-flex; width:36px; height:36px; border-radius:50%; background:var(--color-primary); color:#fff; align-items:center; justify-content:center; font-weight:700;"><%# Container.ItemIndex + 1 %></span></td>
          <td><%#: Item.PatientName %></td>
          <td class="text-muted"><%#: Item.DoctorName %></td>
          <td class="text-muted"><%#: EventTime(Item) %></td>
          <td style="text-align:center;"><span class="badge <%# WaitBadge(Container.ItemIndex) %>" style="font-size:13px;">~<%# (Container.ItemIndex + 1) * AvgConsultMinutes %> min</span></td>
        </tr>
      </ItemTemplate>
      <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>
  </div>

  <div class="card">
    <div class="text-xs text-muted mb-2" style="text-transform:uppercase; font-weight:600;">Upcoming Today (<%: Upcoming.Count %>)</div>
    <asp:Repeater ID="UpcomingRepeater" runat="server" ItemType="OpdProject.Models.AppointmentView">
      <ItemTemplate>
        <div style="display:flex; justify-content:space-between; padding:6px 0; border-bottom:1px solid var(--border-subtle,#f1f5f9); font-size:14px;">
          <span><%#: Item.PatientName %></span><span class="text-muted"><%#: Item.DoctorName %> · <%#: EventTime(Item) %></span>
        </div>
      </ItemTemplate>
    </asp:Repeater>
    <asp:PlaceHolder ID="NoUpcoming" runat="server"><div class="text-muted">No more appointments.</div></asp:PlaceHolder>
  </div>
  <div class="text-xs text-muted mt-3" style="text-align:center;">Auto-refreshes every 30 seconds</div>
</div>
</asp:Content>
