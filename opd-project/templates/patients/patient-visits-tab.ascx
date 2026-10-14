<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="patient-visits-tab.ascx.cs" Inherits="OpdProject.templates.patients.patient_visits_tab" %>
<%-- Health record → Visits (doctors and nurses only): every signed consultation, newest first, with its
     diagnoses, complaints, medicines, tests, follow-up and post-lock addenda. Read-only. --%>
<div class="text-xs text-muted" style="margin-bottom:8px;"><i class="ti ti-lock" aria-hidden="true"></i>
  Signed consultations, newest first. Visible to doctors and nurses only. Corrections are made as addenda from the consultation page.</div>
<asp:Repeater ID="VisitsRepeater" runat="server" ItemType="OpdProject.Web.VisitRecord">
  <ItemTemplate>
    <div class="card record-visit">
      <div class="record-visit-head">
        <div>
          <strong><%#: Item.DateLabel %></strong> <span class="text-muted">· <%#: Item.DoctorName %></span>
          <div class="record-visit-dx"><%#: Item.PrimaryDiagnosis %></div>
        </div>
        <div class="record-visit-actions"><%# Item.ActionsHtml %></div>
      </div>
      <%# Item.DetailHtml %>
    </div>
  </ItemTemplate>
</asp:Repeater>
<asp:PlaceHolder ID="NoVisits" runat="server"><div class="card text-muted" style="text-align:center;">No signed visits yet.</div></asp:PlaceHolder>
