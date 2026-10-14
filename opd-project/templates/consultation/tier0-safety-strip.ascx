<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="tier0-safety-strip.ascx.cs" Inherits="OpdProject.templates.consultation.tier0_safety_strip" %>
<%-- Tier 0 — safety alerts (allergies, active medications, continuity), pinned at the top of the
     Clinical Intelligence panel. Never collapses. --%>
<div class="c360-safety" id="tier0Strip">
  <div class="c360-alert c360-alert--<%: AllergyTone %>" id="tier0Allergy" title="Active allergies">
    <i class="ti <%: AllergyIcon %> c360-alert-icon" aria-hidden="true"></i>
    <div><strong>Allergies</strong><div class="c360-alert-text"><%: AllergyText %></div></div>
  </div>
  <div class="c360-alert c360-alert--info" id="tier0Meds" title="Active medications across all visits">
    <i class="ti ti-pill c360-alert-icon" aria-hidden="true"></i>
    <div><strong><%: MedsText %></strong><asp:PlaceHolder ID="MedNamesHolder" runat="server"><div class="c360-alert-text"><%: MedNames %></div></asp:PlaceHolder></div>
  </div>
  <asp:PlaceHolder ID="ContinuityHolder" runat="server">
    <div class="c360-alert c360-alert--note" id="tier0Continuity">
      <i class="ti ti-history c360-alert-icon" aria-hidden="true"></i>
      <div><strong>Last visit</strong><div class="c360-alert-text"><%: ContinuityText %></div></div>
      <button class="c360-alert-dismiss" type="button" title="Dismiss" onclick="this.parentNode.style.display='none';">×</button>
    </div>
  </asp:PlaceHolder>
</div>
