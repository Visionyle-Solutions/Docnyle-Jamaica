<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="patient-identifier-strip.ascx.cs" Inherits="OpdProject.templates.shared.patient_identifier_strip" %>
<%-- Persistent patient identifier banner shown on every patient-scoped page.
     Compact: no hospital name (already in the top bar), tight padding. --%>
<asp:PlaceHolder ID="StripHolder" runat="server">
<div class="patient-strip">
  <div class="patient-strip-name"><%: Patient.FullName %><asp:Literal ID="BadgeText" runat="server" /></div>
  <div class="patient-strip-meta">
    <span><span class="label">Gender:</span> <%: Patient.Gender %></span>
    <span><span class="label">DOB:</span> <%: DobDisplay %> (<%: Patient.AgeYears.HasValue ? Patient.AgeYears.Value.ToString() : "—" %>y)</span>
    <span><span class="label"><%: HealthIdLabel %>:</span> <%: string.IsNullOrEmpty(Patient.NationalHealthId) ? "Not recorded" : Patient.NationalHealthId %></span>
    <span><span class="label"><%: PatientIdLabel %>:</span> <%: Patient.Mrn %></span>
    <span><span class="label">Phone:</span> <%: Patient.Phone %></span>
  </div>
</div>
</asp:PlaceHolder>
