<%@ Page Title="Audit Log" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="audit-log.aspx.cs" Inherits="OpdProject.templates.settings.audit_log" %>
<%-- Append-only audit trail with filters and CSV export. Route: /audit-log --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <asp:Panel ID="FilterCard" runat="server" CssClass="card mb-3" DefaultButton="RefreshButton">
    <div style="display:flex; gap:8px; align-items:flex-end; flex-wrap:wrap;">
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= FromDate.ClientID %>">From</label><asp:TextBox ID="FromDate" runat="server" CssClass="form-control" TextMode="Date" /></div>
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= ToDate.ClientID %>">To</label><asp:TextBox ID="ToDate" runat="server" CssClass="form-control" TextMode="Date" />
        <asp:CompareValidator runat="server" ControlToValidate="ToDate" ControlToCompare="FromDate" Operator="GreaterThanEqual" Type="Date" Text="Must be on/after From" CssClass="field-error" Display="Dynamic" /></div>
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= ActionFilter.ClientID %>">Action</label>
        <asp:DropDownList ID="ActionFilter" runat="server" CssClass="form-select" style="width:150px;">
          <asp:ListItem Value="">All</asp:ListItem><asp:ListItem>LOGIN</asp:ListItem><asp:ListItem>LOGOUT</asp:ListItem><asp:ListItem>CREATE</asp:ListItem><asp:ListItem>UPDATE</asp:ListItem><asp:ListItem>DELETE</asp:ListItem>
          <asp:ListItem>PRINT</asp:ListItem><asp:ListItem>EXPORT</asp:ListItem><asp:ListItem>IMPORT</asp:ListItem><asp:ListItem Value="OVERRIDE_SAFETY">OVERRIDE</asp:ListItem>
          <asp:ListItem Value="ACKNOWLEDGE_RECON">RECON ACK</asp:ListItem><asp:ListItem Value="ACKNOWLEDGE_CONTRADICTION">CONTRA ACK</asp:ListItem><asp:ListItem Value="SIGN_OFF">SIGN OFF</asp:ListItem>
        </asp:DropDownList></div>
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= EntityFilter.ClientID %>">Entity</label><asp:DropDownList ID="EntityFilter" runat="server" CssClass="form-select" style="width:170px;" /></div>
      <asp:LinkButton ID="RefreshButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="RefreshButton_Click"><i class="ti ti-filter" aria-hidden="true"></i> Apply</asp:LinkButton>
      <div style="flex:1;"></div>
      <asp:LinkButton ID="ExportButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="ExportButton_Click" CausesValidation="false"><i class="ti ti-download" aria-hidden="true"></i> Export CSV</asp:LinkButton>
    </div>
  </asp:Panel>
  <div class="card">
    <div class="text-xs text-muted mb-2"><asp:Literal ID="CountText" runat="server" /></div>
    <div style="max-height:560px; overflow-y:auto;">
      <asp:Repeater ID="LogRepeater" runat="server" ItemType="OpdProject.Models.AuditLogEntry">
        <HeaderTemplate><table class="data-table"><thead><tr><th>Timestamp</th><th>User</th><th>Action</th><th>Entity</th><th>Entity ID</th><th>Details</th><th>IP</th></tr></thead><tbody></HeaderTemplate>
        <ItemTemplate>
          <tr>
            <td class="text-xs" style="white-space:nowrap;"><%#: FmtDateTime(Item.Timestamp) %></td>
            <td class="text-xs"><%#: OpdProject.Services.UserService.UserName(Item.UserId) %></td>
            <td><span class="badge <%# ActionBadge(Item.Action) %>" style="font-size:10px;"><%#: Item.Action %></span></td>
            <td class="text-xs"><%#: Item.EntityType %></td>
            <td class="text-xs" style="max-width:110px; overflow:hidden; text-overflow:ellipsis;"><%#: Item.EntityId %></td>
            <td class="text-xs" style="max-width:280px; overflow:hidden; text-overflow:ellipsis;" title="<%#: Item.Details %>"><%#: Item.Details %></td>
            <td class="text-xs"><%#: Item.IpAddress %></td>
          </tr>
        </ItemTemplate>
        <FooterTemplate></tbody></table></FooterTemplate>
      </asp:Repeater>
      <asp:PlaceHolder ID="EmptyHolder" runat="server"><div class="zone-a-empty" style="padding:20px; text-align:center;">No audit entries for this filter.</div></asp:PlaceHolder>
    </div>
  </div>
</div>
</asp:Content>
