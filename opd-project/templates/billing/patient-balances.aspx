<%@ Page Title="Outstanding Balances" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="patient-balances.aspx.cs" Inherits="OpdProject.templates.billing.patient_balances" %>
<%-- Patients with an unpaid share across all visits (payer receivables are not patient debt). Route: /billing/balances --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container titlecase-headings">
  <div class="card mb-3">
    <div style="display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; gap:8px;">
      <div>
        <div class="text-sm" style="font-weight:600;">Patient Outstanding Balances</div>
        <div class="text-xs text-muted">Patients with unpaid invoices across all visits</div>
      </div>
      <div class="badge badge--danger" style="font-size:16px; padding:10px 16px;"><asp:Literal ID="TotalText" runat="server" /> Total Outstanding</div>
    </div>
  </div>
  <div class="card">
    <asp:PlaceHolder ID="EmptyHolder" runat="server"><div class="zone-a-empty" style="padding:20px; text-align:center;">No outstanding balances. All patients are paid up!</div></asp:PlaceHolder>
    <asp:Repeater ID="BalancesRepeater" runat="server" ItemType="OpdProject.Web.BalanceRow">
      <HeaderTemplate><table class="data-table" style="font-size:12px;"><thead><tr><th>Patient</th><th>MRN</th><th>Phone Number</th><th>Invoices</th><th style="text-align:right;">Billed</th><th style="text-align:right;">Paid</th><th style="text-align:right;">Outstanding</th><th></th></tr></thead><tbody></HeaderTemplate>
      <ItemTemplate>
        <tr>
          <td style="font-weight:600;"><%#: Item.PatientName %></td><td class="text-xs"><%#: Item.Mrn %></td><td class="text-xs"><%#: Item.Phone %></td>
          <td><%# Item.InvoiceCount %></td>
          <td style="text-align:right;"><%#: Money(Item.Billed) %></td><td style="text-align:right;"><%#: Money(Item.Paid) %></td>
          <td style="text-align:right; font-weight:700; color:var(--color-danger);"><%#: Money(Item.Outstanding) %></td>
          <td><a class="btn btn--xs btn--primary" href="<%#: Urls.Checkout(Item.LatestConsultationId) %>">Collect</a></td>
        </tr>
      </ItemTemplate>
      <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>
  </div>
</div>
</asp:Content>
