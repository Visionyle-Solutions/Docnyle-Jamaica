<%@ Page Title="Data Export" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="data-export.aspx.cs" Inherits="OpdProject.templates.settings.data_export" %>
<%-- CSV export of the in-memory tables (each export is audited). Route: /data-export --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card">
    <div class="text-sm text-secondary mb-3">Export data tables as CSV files. Each table downloads as a separate CSV; every export is recorded in the audit log.</div>
    <asp:Repeater ID="TablesRepeater" runat="server" ItemType="OpdProject.Services.ExportTable" OnItemCommand="TablesRepeater_ItemCommand">
      <ItemTemplate>
        <div style="display:flex; justify-content:space-between; align-items:center; padding:12px 0; border-bottom:1px solid var(--border-default);">
          <div><div class="text-sm" style="font-weight:600;"><%#: Item.Label %></div><div class="text-xs text-muted"><%# Item.Count %> records</div></div>
          <asp:LinkButton runat="server" CssClass="btn btn--sm btn--primary" CommandName="Export" CommandArgument="<%# Item.Key %>" Enabled="<%# Item.Count > 0 %>"><i class="ti ti-download" aria-hidden="true"></i> Export CSV</asp:LinkButton>
        </div>
      </ItemTemplate>
    </asp:Repeater>
  </div>
</div>
</asp:Content>
