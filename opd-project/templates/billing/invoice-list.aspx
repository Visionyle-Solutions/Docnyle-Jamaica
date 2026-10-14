<%@ Page Title="Invoices" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="invoice-list.aspx.cs" Inherits="OpdProject.templates.billing.invoice_list" %>
<%-- Invoice list with status filter and search. Route: /billing/invoices --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <asp:Panel ID="FilterCard" runat="server" CssClass="card mb-3" DefaultButton="SearchButton">
    <div style="display:flex; gap:8px; align-items:flex-end; flex-wrap:wrap;">
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= StatusFilter.ClientID %>">Status</label>
        <asp:DropDownList ID="StatusFilter" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="Filter_Changed">
          <asp:ListItem Value="">All</asp:ListItem><asp:ListItem>PENDING</asp:ListItem><asp:ListItem>PARTIAL</asp:ListItem><asp:ListItem>PAID</asp:ListItem><asp:ListItem>CANCELLED</asp:ListItem>
        </asp:DropDownList></div>
      <div class="form-group" style="margin:0; flex:1;"><label class="form-label" for="<%= SearchInput.ClientID %>">Search</label>
        <asp:TextBox ID="SearchInput" runat="server" CssClass="form-control" placeholder="Invoice # or patient name" /></div>
      <asp:LinkButton ID="SearchButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="Filter_Changed"><i class="ti ti-search" aria-hidden="true"></i> Search</asp:LinkButton>
    </div>
  </asp:Panel>
  <div class="card">
    <table class="data-table">
      <thead><tr><th>Invoice #</th><th>Patient</th><th>Type</th><th style="text-align:right;">Total</th><th style="text-align:right;">Covered</th><th style="text-align:right;">Paid</th><th>Status</th><th>Date</th><th>Actions</th></tr></thead>
      <tbody>
        <asp:Repeater ID="InvoicesRepeater" runat="server" ItemType="OpdProject.Models.Invoice">
          <ItemTemplate>
            <tr>
              <td><%#: Item.InvoiceNumber %></td>
              <td><%#: PatientName(Item.PatientId) %></td>
              <td><%#: Item.ConsultationType %><%#: Item.IsFreeFollowup ? " (free)" : "" %></td>
              <td style="text-align:right;"><%#: Money(Item.TotalAmount, Item.Currency) %></td>
              <td style="text-align:right;"><%#: Item.CoveredAmount > 0 ? Money(Item.CoveredAmount, Item.Currency) : "-" %></td>
              <td style="text-align:right;"><%#: Money(Item.PaidAmount, Item.Currency) %></td>
              <td><span class="badge <%#: StatusBadge(Item.Status) %>"><%#: Item.Status %></span></td>
              <td><%#: FmtDate(Item.CreatedAt) %></td>
              <td><a class="btn btn--xs btn--primary" href="<%#: Urls.Checkout(Item.ConsultationId) %>">Open</a></td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="EmptyRow" runat="server"><tr class="empty-row"><td colspan="9">No invoices yet. Invoices are generated when a doctor signs off a consultation.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>
  </div>
</div>
</asp:Content>
