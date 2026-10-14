<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="invoice-print.aspx.cs" Inherits="OpdProject.templates.billing.invoice_print" %>
<%-- Printable invoice / receipt. Route: /billing/print/{invoiceId} --%>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
  <meta charset="UTF-8" />
  <title>Invoice</title>
  <style>
    body { font-family: Arial, sans-serif; margin: 20px; font-size: 12px; }
    table { width: 100%; border-collapse: collapse; margin: 12px 0; }
    th, td { border: 1px solid #ccc; padding: 6px 8px; }
    th { background: #f5f5f5; text-align: left; }
    tfoot td { font-weight: bold; }
    .no-border td { border: none; }
    .toolbar { margin-bottom: 12px; }
    @media print { .toolbar { display: none; } }
  </style>
</head>
<body>
  <div class="toolbar"><button type="button" onclick="window.print();">Print</button> <button type="button" onclick="window.close();">Close</button></div>
  <asp:Literal ID="InvoiceHtml" runat="server" />
</body>
</html>
