<%@ Page Title="Patient Registration" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="patient-registration.aspx.cs" Inherits="OpdProject.templates.patients.patient_registration" %>
<%@ Register TagPrefix="uc" TagName="PatientForm" Src="~/templates/patients/patient-form.ascx" %>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card reg-form">
    <div style="font-weight:700; margin-bottom:12px;"><asp:Literal ID="HeadingText" runat="server" /> <asp:Literal ID="MrnText" runat="server" /></div>
    <asp:ValidationSummary runat="server" CssClass="validation-summary" HeaderText="Please fix the following:" DisplayMode="BulletList" />

    <uc:PatientForm ID="PatientFields" runat="server" />

    <asp:PlaceHolder ID="DuplicatePanel" runat="server" Visible="false">
      <div class="alert-banner alert-banner--warning" style="margin-top:12px;">
        <div><strong>Patient already exists.</strong> <asp:Literal ID="DuplicateText" runat="server" /></div>
      </div>
    </asp:PlaceHolder>

    <div style="display:flex; gap:8px; margin-top:16px;">
      <asp:LinkButton ID="SaveButton" runat="server" CssClass="btn btn--primary" OnClick="SaveButton_Click">
        <i class="ti ti-device-floppy" aria-hidden="true"></i> <asp:Literal ID="SaveLabel" runat="server" />
      </asp:LinkButton>
      <a class="btn btn--ghost" href="<%: Urls.PatientSearch %>">Cancel</a>
    </div>
  </div>
</div>
</asp:Content>
