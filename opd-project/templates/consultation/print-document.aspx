<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="print-document.aspx.cs" Inherits="OpdProject.templates.consultation.print_document" %>
<%-- Printable clinical documents (prescription, consultation summary, lab requisition, referral letter),
     rendered on the server on the hospital letterhead. Route: /print/{document}/{consultationId} --%>
<!DOCTYPE html>
<html lang="en">
<head runat="server">
  <meta charset="UTF-8" />
  <title>Print</title>
  <link rel="stylesheet" href="<%: ResolveUrl("~/assets/icons/tabler-icons.min.css") %>" />
  <link rel="stylesheet" href="<%: ResolveUrl("~/css/tokens.css") %>" />
  <link rel="stylesheet" href="<%: ResolveUrl("~/css/components.css") %>" />
  <link rel="stylesheet" href="<%: ResolveUrl("~/css/print.css") %>" />
  <style>
    body { background: var(--surface-page); margin: 0; padding: 16px; font-family: 'Segoe UI', Arial, sans-serif; }
    .print-toolbar { max-width: 820px; margin: 0 auto 12px; display: flex; gap: 8px; align-items: center; }
    #printContainer.print-preview { max-width: 820px; margin: 0 auto; max-height: none; font-size: 12px; }
    .print-preview .print-header { text-align: center; border-bottom: 2px solid #333; padding-bottom: 8px; margin-bottom: 12px; }
    .print-preview .print-patient-info { display: flex; justify-content: space-between; border: 1px solid #ccc; padding: 6px 10px; margin-bottom: 10px; }
    .print-preview .print-section { margin-bottom: 10px; }
    .print-preview .print-section-label { font-weight: 700; text-transform: uppercase; border-bottom: 1px solid #ccc; padding-bottom: 2px; margin-bottom: 4px; }
    .print-preview .print-footer { margin-top: 20px; border-top: 1px solid #ccc; padding-top: 8px; display: flex; justify-content: space-between; }
    .print-preview .print-signature .sig-line { border-top: 1px solid #333; margin-top: 30px; padding-top: 4px; min-width: 200px; text-align: right; }
    .print-preview .print-validity { font-size: 10px; color: #777; margin-top: 8px; text-align: center; }
    .print-preview .print-custom { margin: 1px 0; }
    .print-preview .print-custom-gap { height: 4px; }
    .print-preview .print-addendum { border-left: 3px solid #f59e0b; padding: 6px 10px; margin-top: 6px; background: #fffbeb; }
  </style>
</head>
<body>
  <div class="print-toolbar no-print">
    <button class="btn btn--primary" type="button" onclick="window.print();"><i class="ti ti-printer" aria-hidden="true"></i> Print</button>
    <button class="btn btn--ghost" type="button" onclick="window.close();">Close</button>
    <span class="text-xs text-muted"><asp:Literal ID="DocumentTitle" runat="server" /></span>
  </div>
  <div id="printContainer" class="print-preview" style="display:block;">
    <asp:Literal ID="DocumentHtml" runat="server" />
  </div>
</body>
</html>
