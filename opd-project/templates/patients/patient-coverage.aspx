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
    <div class="reg-section-title" style="border-top:0; padding-top:0; margin-top:0; display:flex; justify-content:space-between; align-items:center; gap:8px;">
      <span>Add coverage</span>
      <%-- Admins maintain the insurer list in Settings → Billing & Numbers --%>
      <asp:HyperLink ID="ManagePayersLink" runat="server" CssClass="btn btn--xs btn--ghost" Visible="false"><i class="ti ti-building-plus" aria-hidden="true"></i> Manage insurance companies</asp:HyperLink>
    </div>
    <asp:ValidationSummary runat="server" ValidationGroup="Coverage" CssClass="validation-summary" />
    <uc:CoverageForm ID="CoverageFields" runat="server" />
    <div style="display:flex; gap:8px; margin-top:8px;">
      <asp:LinkButton ID="SaveCoverageButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Coverage" OnClick="SaveCoverageButton_Click">
        <i class="ti ti-shield-plus" aria-hidden="true"></i> Save coverage
      </asp:LinkButton>
    </div>
  </asp:Panel>

  <%-- Consent for Sharing Health Information: print (pre-filled), patient signs on paper, upload the scanned copy --%>
  <div id="consent" class="card mb-3" style="scroll-margin-top:16px;">
    <div class="reg-section-title" style="border-top:0; padding-top:0; margin-top:0; display:flex; justify-content:space-between; align-items:center; gap:8px;">
      <span>Consent for sharing health information</span>
      <asp:Literal ID="ConsentBadge" runat="server" />
    </div>
    <p class="text-sm text-muted" style="margin-top:0;">
      Print the consent form — it is pre-filled with the hospital, physician and patient details — have the patient (or their representative) sign it with a witness,
      then scan it and upload the signed copy here.
    </p>
    <asp:PlaceHolder ID="NoDoctorHint" runat="server" Visible="false">
      <div class="text-xs text-muted mb-3"><i class="ti ti-info-circle" aria-hidden="true"></i> No preferred physician on file, so the physician line on the form will be blank.
        <asp:HyperLink ID="SetDoctorLink" runat="server">Set one in patient details</asp:HyperLink>.</div>
    </asp:PlaceHolder>

    <asp:Repeater ID="ConsentRepeater" runat="server" ItemType="OpdProject.Models.PatientConsentDocument">
      <HeaderTemplate><table class="data-table mb-3"><thead><tr><th>Signed copy</th><th>Uploaded</th><th>By</th><th>Form</th></tr></thead><tbody></HeaderTemplate>
      <ItemTemplate>
        <tr>
          <td><a href="<%#: OpdProject.Web.Urls.PatientConsentFile(Item.PatientId, Item.ConsentDocumentId) %>" target="_blank" rel="noopener"><i class="ti ti-file-certificate" aria-hidden="true"></i> <%#: Item.FileName %></a>
            <span class="text-xs text-muted">(<%#: OpdProject.Services.LabService.FormatSize(Item.SizeBytes) %>)</span><%# Container.ItemIndex == 0 ? " <span class=\"badge badge--success\">Current</span>" : "" %></td>
          <td class="text-xs"><%#: FmtDateTime(Item.UploadedAt) %></td>
          <td class="text-xs"><%#: OpdProject.Services.UserService.UserName(Item.UploadedByUserId) %></td>
          <td class="text-xs"><%#: Item.FormVersion %></td>
        </tr>
      </ItemTemplate>
      <FooterTemplate></tbody></table></FooterTemplate>
    </asp:Repeater>

    <div style="display:flex; flex-wrap:wrap; gap:8px; align-items:center;">
      <asp:HyperLink ID="PrintConsentLink" runat="server" CssClass="btn btn--ghost" Target="_blank"><i class="ti ti-printer" aria-hidden="true"></i> Print consent form</asp:HyperLink>
      <asp:FileUpload ID="ConsentFile" runat="server" CssClass="form-control" style="max-width:320px;" accept=".pdf,.jpg,.jpeg,.png" />
      <asp:LinkButton ID="UploadConsentButton" runat="server" CssClass="btn btn--primary" CausesValidation="false" OnClick="UploadConsentButton_Click">
        <i class="ti ti-upload" aria-hidden="true"></i> Upload signed copy
      </asp:LinkButton>
    </div>
    <div class="text-xs text-muted mt-1">PDF, JPG or PNG · up to <%: OpdProject.Services.LabService.MaxFileBytes / (1024 * 1024) %> MB</div>
    <asp:PlaceHolder ID="ConsentErrorPanel" runat="server" Visible="false">
      <div class="field-error mt-1"><asp:Literal ID="ConsentError" runat="server" Mode="Encode" /></div>
    </asp:PlaceHolder>
  </div>

  <div style="display:flex; gap:8px; justify-content:flex-end;">
    <asp:HyperLink ID="BackLink" runat="server" CssClass="btn btn--ghost" />
    <asp:HyperLink ID="ContinueLink" runat="server" CssClass="btn btn--primary" />
  </div>
</div>
</asp:Content>
