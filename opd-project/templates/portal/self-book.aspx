<%@ Page Title="Book an Appointment" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="self-book.aspx.cs" Inherits="OpdProject.templates.portal.self_book" MaintainScrollPositionOnPostback="true" %>
<%-- Public online booking: identify → doctor → visit type/date → free slot → intake questions → confirm.
     Route: /portal/book (?h=HOSP-002 for another hospital) --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container--narrow" style="padding:24px 16px;">
  <div class="card mb-3" style="text-align:center;">
      <asp:Literal ID="LogoHtml" runat="server" />
    <h3 style="margin:4px 0;"><%: PortalHospital.Name %></h3>
    <div class="text-xs text-muted"><%: PortalHospital.LegalAddress %> · <%: PortalHospital.ContactNumber %></div>
    <div class="text-sm text-secondary mt-2">Book an appointment online</div>
  </div>

  <asp:Panel ID="BookingPanel" runat="server">
    <div class="card mb-3">
      <div class="text-sm" style="font-weight:600; margin-bottom:8px;">1. Who is the appointment for?</div>
      <div class="form-grid">
        <div class="form-group"><label class="form-label" for="<%= PatientMrn.ClientID %>"><%: PatientIdLabelText %></label><asp:TextBox ID="PatientMrn" runat="server" CssClass="form-control" placeholder="MRN-XXXXX" /></div>
        <div class="form-group"><label class="form-label" for="<%= PatientPhone.ClientID %>">Phone Number</label><asp:TextBox ID="PatientPhone" runat="server" CssClass="form-control" placeholder="+91-9000000000" /></div>
      </div>
      <div class="text-xs text-muted">New here? <a href="<%: RegisterUrl %>">Register first</a>.</div>
    </div>

    <div class="card mb-3">
      <div class="text-sm" style="font-weight:600; margin-bottom:8px;">2. Choose a doctor</div>
      <asp:RadioButtonList ID="DoctorList" runat="server" RepeatLayout="Flow" RepeatDirection="Vertical" CssClass="text-sm" AutoPostBack="true" OnSelectedIndexChanged="Options_Changed" />
    </div>

    <div class="card mb-3">
      <div class="text-sm" style="font-weight:600; margin-bottom:8px;">3. Visit type, date and time</div>
      <div style="display:flex; gap:8px; flex-wrap:wrap; margin-bottom:8px;">
        <asp:DropDownList ID="VisitType" runat="server" CssClass="form-select" style="width:240px;" AutoPostBack="true" OnSelectedIndexChanged="Options_Changed" aria-label="Visit type" />
        <asp:TextBox ID="VisitDate" runat="server" CssClass="form-control" TextMode="Date" style="width:200px;" AutoPostBack="true" OnTextChanged="Options_Changed" aria-label="Date" />
      </div>
      <asp:RadioButtonList ID="SlotList" runat="server" RepeatLayout="Flow" RepeatDirection="Horizontal" CssClass="inline-list text-sm" />
      <asp:Literal ID="SlotMessage" runat="server" />
    </div>

    <asp:Panel ID="IntakeCard" runat="server" CssClass="card mb-3">
      <div class="text-sm" style="font-weight:600; margin-bottom:8px;">4. Pre-visit questions (optional)</div>
      <asp:Repeater ID="QuestionsRepeater" runat="server" ItemType="OpdProject.Models.IntakeQuestion" OnItemDataBound="QuestionsRepeater_ItemDataBound">
        <ItemTemplate>
          <div class="form-group"><label class="form-label"><%#: Item.Label %></label>
            <asp:HiddenField ID="QuestionId" runat="server" Value="<%# Item.QuestionId %>" />
            <asp:TextBox ID="AnswerText" runat="server" CssClass="form-control" />
            <asp:DropDownList ID="AnswerChoice" runat="server" CssClass="form-select" Visible="false" />
          </div>
        </ItemTemplate>
      </asp:Repeater>
    </asp:Panel>

    <asp:CustomValidator ID="BookingValidator" runat="server" OnServerValidate="BookingValidator_ServerValidate" CssClass="validation-summary" Display="Dynamic" />
    <asp:Button ID="BookButton" runat="server" Text="Book appointment" CssClass="btn btn--primary" style="width:100%; justify-content:center;" OnClick="BookButton_Click" />
  </asp:Panel>

  <asp:Panel ID="ConfirmPanel" runat="server" CssClass="card" Visible="false" style="text-align:center; padding:32px;">
    <div style="font-size:48px;">📅</div>
    <h2 style="margin:16px 0 8px;">Appointment Booked!</h2>
    <asp:Literal ID="ConfirmText" runat="server" />
    <p class="text-xs text-muted mt-2">Please arrive 10 minutes early and check in at reception.</p>
  </asp:Panel>
</div>
</asp:Content>
