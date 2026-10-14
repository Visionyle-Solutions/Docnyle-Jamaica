<%@ Page Title="Health Record" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="patient-allergies.aspx.cs" Inherits="OpdProject.templates.patients.patient_allergies" %>
<%@ Register TagPrefix="uc" TagName="PatientStrip" Src="~/templates/shared/patient-identifier-strip.ascx" %>
<%@ Register TagPrefix="uc" TagName="AllergiesBody" Src="~/templates/patients/patient-allergies-body.ascx" %>
<%@ Register TagPrefix="uc" TagName="AllergiesForm" Src="~/templates/patients/patient-allergies-form.ascx" %>
<%@ Register TagPrefix="uc" TagName="HistoryTab" Src="~/templates/patients/patient-history-tab.ascx" %>
<%@ Register TagPrefix="uc" TagName="MedicationsTab" Src="~/templates/patients/patient-medications-tab.ascx" %>
<%@ Register TagPrefix="uc" TagName="FamilySocialTab" Src="~/templates/patients/patient-family-social-tab.ascx" %>
<%@ Register TagPrefix="uc" TagName="VisitsTab" Src="~/templates/patients/patient-visits-tab.ascx" %>
<%@ Register TagPrefix="uc" TagName="ErrorState" Src="~/templates/shared/error-state.ascx" %>
<%-- Patient Health Record — allergies, medical history, current medications, family & social.
     Each tab is a split-panel list + form. Route: /patients/{patientId}/allergies[?tab=history|meds|family] --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
  <asp:PlaceHolder ID="NotFound" runat="server" Visible="false"><uc:ErrorState runat="server" Message="Patient not found." /></asp:PlaceHolder>
  <asp:PlaceHolder ID="Workspace" runat="server">
    <uc:PatientStrip ID="Strip" runat="server" />
    <div class="page-container titlecase-headings">
      <div style="display:flex; gap:8px; margin-bottom:8px;">
        <a class="btn btn--xs btn--ghost" href="<%: Urls.PatientSearch %>"><i class="ti ti-arrow-left" aria-hidden="true"></i> Patients</a>
        <asp:HyperLink ID="ConsultLink" runat="server" CssClass="btn btn--xs btn--primary"><i class="ti ti-stethoscope" aria-hidden="true"></i> Open consultation</asp:HyperLink>
      </div>

      <%-- Shown right after registration: allergies first, then medical history --%>
      <asp:PlaceHolder ID="OnboardingBanner" runat="server" Visible="false">
        <div class="record-onboarding">
          <div><strong><%: OnboardingTitle %></strong><div class="text-xs"><%: OnboardingText %></div></div>
          <a class="btn btn--sm btn--primary" href="<%: OnboardingNextUrl %>"><%: OnboardingNextLabel %> <i class="ti ti-arrow-right" aria-hidden="true"></i></a>
        </div>
      </asp:PlaceHolder>

      <%-- At-a-glance summary; each tile opens its tab --%>
      <div class="record-summary">
        <a class="record-summary-tile" href="<%: TabUrl("allergies") %>"><span class="k">Allergies</span><span class="v<%: AllergyCount > 0 ? " v--warn" : "" %>"><%: AllergySummary %></span></a>
        <a class="record-summary-tile" href="<%: TabUrl("history") %>"><span class="k">Conditions</span><span class="v"><%: ConditionSummary %></span></a>
        <a class="record-summary-tile" href="<%: TabUrl("meds") %>"><span class="k">Current Medications</span><span class="v"><%: MedsSummary %></span></a>
        <a class="record-summary-tile" href="<%: TabUrl("family") %>"><span class="k">Family &amp; Social</span><span class="v"><%: FamilySummary %></span></a>
      </div>

      <nav class="record-tabs" aria-label="Health record sections">
        <a class="record-tab<%: ActiveTab == "allergies" ? " active" : "" %>" href="<%: TabUrl("allergies") %>"<%= ActiveTab == "allergies" ? " aria-current=\"page\"" : "" %>><i class="ti ti-alert-triangle" aria-hidden="true"></i> Allergies <span class="count<%: AllergyCount > 0 ? " count--alert" : "" %>"><%: AllergyCount %></span></a>
        <a class="record-tab<%: ActiveTab == "history" ? " active" : "" %>" href="<%: TabUrl("history") %>"<%= ActiveTab == "history" ? " aria-current=\"page\"" : "" %>><i class="ti ti-clipboard-heart" aria-hidden="true"></i> Medical History <span class="count"><%: HistoryCount %></span></a>
        <a class="record-tab<%: ActiveTab == "meds" ? " active" : "" %>" href="<%: TabUrl("meds") %>"<%= ActiveTab == "meds" ? " aria-current=\"page\"" : "" %>><i class="ti ti-pill" aria-hidden="true"></i> Current Medications <span class="count"><%: MedsCount %></span></a>
        <a class="record-tab<%: ActiveTab == "family" ? " active" : "" %>" href="<%: TabUrl("family") %>"<%= ActiveTab == "family" ? " aria-current=\"page\"" : "" %>><i class="ti ti-users" aria-hidden="true"></i> Family &amp; Social <span class="count"><%: FamilyCount %></span></a>
        <% if (CanSeeVisits) { %>
        <a class="record-tab<%: ActiveTab == "visits" ? " active" : "" %>" href="<%: TabUrl("visits") %>"<%= ActiveTab == "visits" ? " aria-current=\"page\"" : "" %>><i class="ti ti-stethoscope" aria-hidden="true"></i> Visits <span class="count"><%: VisitsCount %></span></a>
        <% } %>
      </nav>

      <asp:PlaceHolder ID="AllergiesTab" runat="server">
        <div class="split-layout">
          <div class="split-main">
            <div role="region" aria-live="polite" aria-label="Allergies list"><uc:AllergiesBody ID="Body" runat="server" /></div>
          </div>
          <div class="split-panel">
            <uc:AllergiesForm ID="AllergyForm" runat="server" />
          </div>
        </div>
      </asp:PlaceHolder>
      <asp:PlaceHolder ID="HistoryTabHolder" runat="server"><uc:HistoryTab ID="History" runat="server" /></asp:PlaceHolder>
      <asp:PlaceHolder ID="MedsTabHolder" runat="server"><uc:MedicationsTab ID="Medications" runat="server" /></asp:PlaceHolder>
      <asp:PlaceHolder ID="FamilyTabHolder" runat="server"><uc:FamilySocialTab ID="FamilySocial" runat="server" /></asp:PlaceHolder>
      <asp:PlaceHolder ID="VisitsTabHolder" runat="server"><uc:VisitsTab ID="Visits" runat="server" /></asp:PlaceHolder>
    </div>
  </asp:PlaceHolder>
</asp:Content>
