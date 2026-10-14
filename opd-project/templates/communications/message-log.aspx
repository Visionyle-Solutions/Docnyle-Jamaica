<%@ Page Title="Communications" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="message-log.aspx.cs" Inherits="OpdProject.templates.communications.message_log" %>
<%-- Communications center — outgoing WhatsApp / e-mail message log. Route: /communications --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card mb-3">
    <div style="display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; gap:8px;">
      <div>
        <div class="text-sm" style="font-weight:600;">Communications Center</div>
        <div class="text-xs text-muted">All outgoing messages — WhatsApp &amp; Email. Without a configured provider, messages are SIMULATED.</div>
      </div>
      <div style="display:flex; gap:8px; align-items:center;">
        <asp:DropDownList ID="StatusFilter" runat="server" CssClass="form-select" style="width:140px;" AutoPostBack="true" OnSelectedIndexChanged="StatusFilter_Changed">
          <asp:ListItem Value="">All statuses</asp:ListItem><asp:ListItem>SENT</asp:ListItem><asp:ListItem>SIMULATED</asp:ListItem><asp:ListItem>QUEUED</asp:ListItem><asp:ListItem>FAILED</asp:ListItem>
        </asp:DropDownList>
        <span class="badge badge--success"><asp:Literal ID="SentCount" runat="server" /> sent</span>
        <span class="badge badge--warning"><asp:Literal ID="SimulatedCount" runat="server" /> simulated</span>
        <span class="badge badge--info"><asp:Literal ID="TotalCount" runat="server" /> total</span>
      </div>
    </div>
  </div>
  <div class="card">
    <asp:Repeater ID="LogRepeater" runat="server" ItemType="OpdProject.Models.QueuedMessage">
      <HeaderTemplate><table class="data-table" style="font-size:11px;"><thead><tr><th>Time</th><th>Ch.</th><th>Patient</th><th>Phone</th><th>Template</th><th>Message</th><th>Status</th></tr></thead><tbody></HeaderTemplate>
      <ItemTemplate>
        <tr>
          <td class="text-xs"><%#: FmtDateTime(Item.QueuedAt) %></td>
          <td><%# Item.Channel == "EMAIL" ? "<i class=\"ti ti-mail\" style=\"color:var(--color-primary);\"></i>" : "<i class=\"ti ti-brand-whatsapp\" style=\"color:#25D366;\"></i>" %></td>
          <td><%#: Item.PatientName %></td><td class="text-xs"><%#: Item.PatientPhone %></td><td class="text-xs"><%#: Item.TemplateName %></td>
          <td style="max-width:320px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;" title="<%#: Item.MessageText %>"><%#: Item.MessageText %></td>
          <td><span class="badge <%#: StatusBadge(Item.Status) %>" style="font-size:9px;"><%#: Item.Status %></span></td>
        </tr>
      </ItemTemplate>
      <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>
    <asp:PlaceHolder ID="EmptyHolder" runat="server"><div class="zone-a-empty" style="padding:20px; text-align:center;">No messages sent yet. Use "Send reminders" from Appointments, or sign off a consultation / upload a lab result.</div></asp:PlaceHolder>
  </div>
</div>
</asp:Content>
