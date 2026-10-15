<%@ Page Title="Patient Registration" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="self-register.aspx.cs" Inherits="OpdProject.templates.portal.self_register" %>
<%@ Register TagPrefix="uc" TagName="PatientForm" Src="~/templates/patients/patient-form.ascx" %>
<%-- Public patient self-registration (no sign-in). Route: /portal/register (?h=HOSP-002 for another hospital).
     Uses the same patient-form fields as staff registration. --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div style="max-width:960px; margin:0 auto; padding:24px 16px;">
  <div class="card mb-3" style="text-align:center;">
    <asp:Literal ID="LogoHtml" runat="server" />
    <h3 style="margin:4px 0;"><%: PortalHospital.Name %></h3>
    <div class="text-xs text-muted"><%: PortalHospital.LegalAddress %> · <%: PortalHospital.ContactNumber %></div>
    <div class="text-sm text-secondary mt-2">New patient registration</div>
  </div>
  <asp:Panel ID="FormPanel" runat="server" CssClass="card reg-form" DefaultButton="SubmitButton">
    <asp:ValidationSummary runat="server" CssClass="validation-summary" HeaderText="Please fix the following:" />

    <uc:PatientForm ID="PatientFields" runat="server" />

    <asp:PlaceHolder ID="DuplicatePanel" runat="server" Visible="false">
      <div class="alert-banner alert-banner--warning" style="margin-top:12px;">
        <div><strong>You may already be registered.</strong> <asp:Literal ID="DuplicateText" runat="server" /></div>
      </div>
    </asp:PlaceHolder>

    <div style="margin-top:16px;">
      <asp:CheckBox ID="ConsentCheck" runat="server" Text=" I confirm these details are correct and consent to the clinic storing them for my care." CssClass="text-xs" />
      <asp:CustomValidator runat="server" OnServerValidate="Consent_ServerValidate" ErrorMessage="Please confirm the declaration" Text="Please confirm" CssClass="field-error" Display="Dynamic" />
    </div>
    <asp:Button ID="SubmitButton" runat="server" Text="Register" CssClass="btn btn--primary mt-3" style="width:100%; justify-content:center;" OnClick="SubmitButton_Click" />
  </asp:Panel>
  <asp:Panel ID="SuccessPanel" runat="server" CssClass="card" Visible="false" style="text-align:center; padding:32px;">
    <div style="font-size:48px;">✅</div>
    <h2 style="margin:16px 0 8px;">Registration Successful</h2>
    <p class="text-secondary">Your <%: PatientIdLabelText %> is</p>
    <div style="font-size:24px; font-weight:700; color:var(--color-primary);"><asp:Literal ID="NewMrn" runat="server" /></div>
    <p class="text-xs text-muted mt-2">Keep this number — use it (or your phone number) to book appointments online.</p>
    <a class="btn btn--primary mt-2" href="<%: BookUrl %>"><i class="ti ti-calendar-plus" aria-hidden="true"></i> Book an appointment</a>
  </asp:Panel>
</div>
</asp:Content>
