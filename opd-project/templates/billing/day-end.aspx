<%@ Page Title="Day-End Reconciliation" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="day-end.aspx.cs" Inherits="OpdProject.templates.billing.day_end" %>
<%-- Day-end cash reconciliation for a date. Route: /billing/day-end --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card mb-3">
    <div style="display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; gap:8px;">
      <div>
        <div class="text-sm" style="font-weight:600;">Day-End Cash Reconciliation</div>
        <div class="text-xs text-muted">Daily closure report</div>
      </div>
      <asp:TextBox ID="DayDate" runat="server" CssClass="form-control no-print" TextMode="Date" style="width:170px;" AutoPostBack="true" OnTextChanged="DayDate_Changed" />
    </div>
  </div>

  <div style="display:grid; grid-template-columns:repeat(4,1fr); gap:12px; margin-bottom:16px;">
    <div class="card" style="text-align:center; padding:16px;"><div class="text-xs text-muted">Total Billed</div><div style="font-size:20px; font-weight:700;"><asp:Literal ID="BilledText" runat="server" /></div></div>
    <div class="card" style="text-align:center; padding:16px;"><div class="text-xs text-muted">Total Collected</div><div style="font-size:20px; font-weight:700; color:var(--color-success);"><asp:Literal ID="CollectedText" runat="server" /></div></div>
    <div class="card" style="text-align:center; padding:16px;"><div class="text-xs text-muted">Outstanding</div><div style="font-size:20px; font-weight:700;"><asp:Literal ID="OutstandingText" runat="server" /></div></div>
    <div class="card" style="text-align:center; padding:16px;"><div class="text-xs text-muted">Consultations</div><div style="font-size:20px; font-weight:700;"><asp:Literal ID="ConsultCount" runat="server" /></div><div class="text-xs text-muted"><asp:Literal ID="ConsultSplit" runat="server" /></div></div>
  </div>

  <div class="card mb-3">
    <div class="text-sm" style="font-weight:600; margin-bottom:8px;">Collection by Payment Method</div>
    <asp:PlaceHolder ID="NoPayments" runat="server"><div class="text-muted">No payments collected on this date.</div></asp:PlaceHolder>
    <asp:Repeater ID="MethodsRepeater" runat="server" ItemType="OpdProject.Web.MethodTotal">
      <HeaderTemplate><table class="data-table" style="font-size:13px;"><thead><tr><th>Method</th><th style="text-align:right;">Transactions</th><th style="text-align:right;">Amount</th><th style="text-align:right;">% of Total</th></tr></thead><tbody></HeaderTemplate>
      <ItemTemplate><tr><td style="font-weight:600;"><%#: Item.Method.Replace("_", " ") %></td><td style="text-align:right;"><%# Item.Count %></td><td style="text-align:right;"><%#: Money(Item.Amount) %></td><td style="text-align:right;"><%# Item.Percent %>%</td></tr></ItemTemplate>
      <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>
  </div>

  <div class="card">
    <div class="text-sm" style="font-weight:600; margin-bottom:8px;">Invoices (<asp:Literal ID="InvoiceCount" runat="server" />)</div>
    <asp:PlaceHolder ID="NoInvoices" runat="server"><div class="text-muted">No invoices on this date.</div></asp:PlaceHolder>
    <asp:Repeater ID="InvoicesRepeater" runat="server" ItemType="OpdProject.Models.Invoice">
      <HeaderTemplate><table class="data-table" style="font-size:11px;"><thead><tr><th>Invoice</th><th>Patient</th><th>Type</th><th style="text-align:right;">Amount</th><th style="text-align:right;">Paid</th><th>Status</th></tr></thead><tbody></HeaderTemplate>
      <ItemTemplate><tr><td class="text-xs"><%#: Item.InvoiceNumber %></td><td><%#: PatientName(Item.PatientId) %></td><td class="text-xs"><%#: Item.ConsultationType %></td>
        <td style="text-align:right;"><%#: Money(Item.TotalAmount, Item.Currency) %></td><td style="text-align:right;"><%#: Money(Item.PaidAmount, Item.Currency) %></td>
        <td><span class="badge <%#: StatusBadge(Item.Status) %>" style="font-size:9px;"><%#: Item.Status %></span></td></tr></ItemTemplate>
      <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>
  </div>

  <button class="btn btn--sm btn--ghost mt-3 no-print" type="button" style="width:100%; justify-content:center;" onclick="window.print();"><i class="ti ti-printer"></i> Print reconciliation report</button>
</div>
</asp:Content>
