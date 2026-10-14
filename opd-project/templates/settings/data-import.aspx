<%@ Page Title="CSV Data Import" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="data-import.aspx.cs" Inherits="OpdProject.templates.settings.data_import" %>
<%-- CSV patient import: upload → server-side parse + row validation → preview → import valid rows. Route: /data-import --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card mb-3">
    <div class="text-sm text-secondary mb-3">Upload a CSV file to bulk-register patients. The first row must be column headers. Required columns: <code>FullName</code>, <code>Gender</code> (MALE/FEMALE/OTHER), <code>Dob</code> (yyyy-MM-dd), <code>Phone</code>. Optional: <code>Email</code>, <code>Address</code>, <code>NationalHealthId</code>.</div>
    <div style="display:flex; gap:12px; align-items:flex-end; flex-wrap:wrap;">
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= TargetTable.ClientID %>">Target table</label>
        <asp:DropDownList ID="TargetTable" runat="server" CssClass="form-select"><asp:ListItem Value="patients">Patients</asp:ListItem></asp:DropDownList></div>
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= CsvFile.ClientID %>">CSV file</label><asp:FileUpload ID="CsvFile" runat="server" CssClass="form-control" accept=".csv,.txt" /></div>
      <asp:LinkButton ID="ValidateButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="ValidateButton_Click"><i class="ti ti-file-check" aria-hidden="true"></i> Upload &amp; validate</asp:LinkButton>
      <asp:CustomValidator ID="FileValidator" runat="server" OnServerValidate="FileValidator_ServerValidate" CssClass="field-error" Display="Dynamic" />
    </div>
  </div>
  <div class="card">
    <asp:PlaceHolder ID="NoFile" runat="server"><div class="zone-a-empty">Select a CSV file to preview and validate.</div></asp:PlaceHolder>
    <asp:PlaceHolder ID="PreviewHolder" runat="server" Visible="false">
      <div class="text-sm mb-2"><asp:Literal ID="FileSummary" runat="server" /></div>
      <div class="text-sm mb-2"><span class="badge badge--success"><asp:Literal ID="ValidCount" runat="server" /> valid</span> <span class="badge badge--danger"><asp:Literal ID="ErrorCount" runat="server" /> errors</span></div>
      <asp:Literal ID="ErrorList" runat="server" />
      <div style="max-height:220px; overflow:auto;"><asp:Literal ID="PreviewTable" runat="server" /></div>
      <asp:LinkButton ID="ImportButton" runat="server" CssClass="btn btn--success mt-3" style="width:100%; justify-content:center;" OnClick="ImportButton_Click" CausesValidation="false"
        OnClientClick="return confirm('Import the valid rows as new patients?');"><i class="ti ti-upload" aria-hidden="true"></i> Import valid rows</asp:LinkButton>
    </asp:PlaceHolder>
  </div>
</div>
</asp:Content>
