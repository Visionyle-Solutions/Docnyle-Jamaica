<%@ Page Title="Coverage & Insurance" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="patient-coverage.aspx.cs" Inherits="OpdProject.templates.patients.patient_coverage" %>
<%@ Register TagPrefix="uc" TagName="PatientStrip" Src="~/templates/shared/patient-identifier-strip.ascx" %>
<%@ Register TagPrefix="uc" TagName="CoverageForm" Src="~/templates/billing/coverage-form.ascx" %>
<%-- A patient's Coverage & Insurance (private insurance and/or the govt scheme, e.g. NHF). Route: /patients/{patientId}/coverage.
     New registrations land here first (?new=1), then continue to the Health Record walkthrough. Coverage saved here is the
     same record billing checkout offers under "Coverage & Bill Split". --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <uc:PatientStrip ID="Strip" runat="server" />

  <asp:PlaceHolder ID="NewPatientBanner" runat="server" Visible="false">
    <div class="alert-banner alert-banner--info mb-3">
      <div><strong>Patient registered.</strong> Record their private insurance and/or <%: SchemeLabel %> now — billing will offer it automatically.
        Patients without cover can skip this step; they are billed as Self-Pay.</div>
    </div>
  </asp:PlaceHolder>

  <div class="card mb-3">
    <div class="reg-section-title" style="border-top:0; padding-top:0; margin-top:0;">Coverage on file</div>
    <asp:PlaceHolder ID="NoCoverage" runat="server">
      <div class="text-sm text-muted">None — the patient is <strong>Self-Pay</strong>.</div>
    </asp:PlaceHolder>
    <asp:Repeater ID="CoverageRepeater" runat="server" ItemType="OpdProject.Models.PatientCoverage" OnItemCommand="CoverageRepeater_ItemCommand">
      <HeaderTemplate><table class="data-table"><thead><tr><th>Coverage</th><th>Details</th><th>Status</th><th></th></tr></thead><tbody></HeaderTemplate>
      <ItemTemplate>
        <tr>
          <td><strong class="text-sm"><%#: CoverageLabel(Item) %></strong></td>
          <td class="text-xs"><%#: CoverageIdent(Item) %></td>
          <td><span class="badge <%# Item.Status == "ACTIVE" ? "badge--success" : "badge--info" %>"><%#: TitleCase(Item.Status) %></span></td>
          <td style="white-space:nowrap;">
            <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost text-danger" CommandName="Deactivate" CommandArgument="<%# Item.CoverageId %>" CausesValidation="false"
              OnClientClick="return confirm('Deactivate this coverage? It will no longer be offered at billing.');">Deactivate</asp:LinkButton>
          </td>
        </tr>
      </ItemTemplate>
      <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>
  </div>

  <asp:Panel ID="AddPanel" runat="server" CssClass="card mb-3" DefaultButton="SaveCoverageButton">
    <div class="reg-section-title" style="border-top:0; padding-top:0; margin-top:0;">Add coverage</div>
    <asp:ValidationSummary runat="server" ValidationGroup="Coverage" CssClass="validation-summary" />
    <uc:CoverageForm ID="CoverageFields" runat="server" />
    <div style="display:flex; gap:8px; margin-top:8px;">
      <asp:LinkButton ID="SaveCoverageButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Coverage" OnClick="SaveCoverageButton_Click">
        <i class="ti ti-shield-plus" aria-hidden="true"></i> Save coverage
      </asp:LinkButton>
    </div>
  </asp:Panel>

  <div style="display:flex; gap:8px; justify-content:flex-end;">
    <asp:HyperLink ID="BackLink" runat="server" CssClass="btn btn--ghost" />
    <asp:HyperLink ID="ContinueLink" runat="server" CssClass="btn btn--primary" />
  </div>
</div>
</asp:Content>
