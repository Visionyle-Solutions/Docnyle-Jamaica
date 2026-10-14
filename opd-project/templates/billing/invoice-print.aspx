<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="invoice-print.aspx.cs" Inherits="OpdProject.templates.billing.invoice_print" %>
<%-- Printable invoice / receipt. Route: /billing/print/{invoiceId} --%>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
  <meta charset="UTF-8" />
  <title>Invoice</title>
  <link rel="stylesheet" href="~/assets/icons/tabler-icons.min.css" runat="server" />
  <style>
    body { font-family: Arial, sans-serif; margin: 20px; font-size: 12px; padding-bottom: 70px; }
    table { width: 100%; border-collapse: collapse; margin: 12px 0; }
    th, td { border: 1px solid #ccc; padding: 6px 8px; }
    th { background: #f5f5f5; text-align: left; }
    tfoot td { font-weight: bold; }
    .no-border td { border: none; }

    /* Print / Close — fixed to the bottom-right corner of the window, hidden when printing */
    .toolbar { position: fixed; right: 24px; bottom: 24px; display: flex; gap: 10px; z-index: 10; }
    .toolbar button {
      display: inline-flex; align-items: center; gap: 6px;
      height: 38px; padding: 0 18px; border-radius: 8px; border: 1px solid transparent;
      font: 600 13px Arial, sans-serif; color: #fff; cursor: pointer;
      box-shadow: 0 2px 6px rgba(0,0,0,0.18);
    }
    .toolbar .btn-print { background: #1a6b9a; border-color: #1a6b9a; }
    .toolbar .btn-print:hover { background: #114a6b; border-color: #114a6b; }
    .toolbar .btn-close { background: #64748b; border-color: #64748b; }
    .toolbar .btn-close:hover { background: #475569; border-color: #475569; }

    @media print { .toolbar { display: none; } body { padding-bottom: 0; } }
  </style>
</head>
<body>
  <asp:Literal ID="InvoiceHtml" runat="server" />
  <div class="toolbar">
    <button type="button" class="btn-print" onclick="window.print();"><i class="ti ti-printer" aria-hidden="true"></i> Print</button>
    <button type="button" class="btn-close" onclick="window.close();"><i class="ti ti-x" aria-hidden="true"></i> Close</button>
  </div>
</body>
</html>