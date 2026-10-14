<%@ Page Title="Lab Results Inbox" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="lab-results.aspx.cs" Inherits="OpdProject.templates.lab.lab_results" %>
<%-- Doctor's lab result inbox: results pending review, critical/abnormal highlighted. Route: /lab-results --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card mb-3">
    <div style="display:flex; justify-content:space-between; align-items:center;">
      <div class="text-sm text-secondary">Results pending your review. Critical and abnormal values are highlighted.</div>
      <asp:LinkButton ID="RefreshButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="RefreshButton_Click" CausesValidation="false"><i class="ti ti-refresh" aria-hidden="true"></i> Refresh</asp:LinkButton>
    </div>
  </div>
  <div class="card">
    <asp:Repeater ID="ResultsRepeater" runat="server" ItemType="OpdProject.Models.LabResult" OnItemCommand="ResultsRepeater_ItemCommand">
      <ItemTemplate>
        <div id="<%#: Item.ResultId %>" class="lab-result-row" style="display:flex; gap:12px; padding:12px; border-bottom:1px solid var(--border-default); background:<%# Item.Interpretation == "CRITICAL" ? "#fef2f2" : (Item.Interpretation ?? "").StartsWith("ABNORMAL") ? "#fffbeb" : "transparent" %>;">
          <div style="width:4px; min-height:60px; border-radius:2px; background:<%# Item.Interpretation == "CRITICAL" ? "var(--color-danger)" : (Item.Interpretation ?? "").StartsWith("ABNORMAL") ? "#f59e0b" : "var(--color-success)" %>;"></div>
          <div style="flex:1;">
            <div style="display:flex; justify-content:space-between;">
              <div class="text-sm" style="font-weight:600;"><%#: Item.TestName %></div>
              <span class="badge <%# Item.Interpretation == "CRITICAL" ? "badge--danger" : (Item.Interpretation ?? "").StartsWith("ABNORMAL") ? "badge--warning" : "badge--success" %>" style="font-size:10px;"><%#: (Item.Interpretation ?? "NORMAL").Replace("_", " ") %></span>
            </div>
            <div class="text-xs text-muted"><%#: PatientLabel(Item.PatientId) %></div>
            <asp:PlaceHolder runat="server" Visible="<%# !string.IsNullOrEmpty(Item.ResultSummary) %>"><div class="text-sm mt-1"><%#: Item.ResultSummary %></div></asp:PlaceHolder>
            <asp:PlaceHolder runat="server" Visible="<%# !string.IsNullOrEmpty(Item.ResultValue) %>"><div class="text-xs mt-1"><strong>Value:</strong> <%#: Item.ResultValue %> <%#: string.IsNullOrEmpty(Item.ReferenceRange) ? "" : "(Ref: " + Item.ReferenceRange + ")" %></div></asp:PlaceHolder>
            <asp:PlaceHolder runat="server" Visible="<%# !string.IsNullOrEmpty(Item.Notes) %>"><div class="text-xs text-muted mt-1"><%#: Item.Notes %></div></asp:PlaceHolder>
            <div class="text-xs text-muted mt-1">Uploaded: <%#: FmtDateTime(Item.UploadedAt) %> · by <%#: UploadedBy(Item) %><%#: string.IsNullOrEmpty(Item.Technician) ? "" : " · Tech: " + Item.Technician %></div>
            <asp:Repeater runat="server" ItemType="OpdProject.Models.LabResultAttachment" DataSource="<%# LabService.ListAttachments(Item.ResultId) %>">
              <HeaderTemplate><div style="display:flex; flex-wrap:wrap; gap:6px; margin-top:6px;"></HeaderTemplate>
              <ItemTemplate><a class="btn btn--xs btn--ghost" href="<%#: Urls.LabReportFile(Item.AttachmentId) %>" target="_blank" rel="noopener"><i class="ti ti-paperclip" aria-hidden="true"></i> <%#: Item.FileName %> <span class="text-muted">(<%#: LabService.FormatSize(Item.SizeBytes) %>)</span></a></ItemTemplate>
              <FooterTemplate></div></FooterTemplate>
            </asp:Repeater>
          </div>
          <div style="display:flex; flex-direction:column; gap:4px;">
            <asp:LinkButton runat="server" CssClass="btn btn--sm btn--success" CommandName="Review" CommandArgument="<%# Item.ResultId %>" CausesValidation="false"><i class="ti ti-check" aria-hidden="true"></i> Reviewed</asp:LinkButton>
            <asp:HyperLink runat="server" CssClass="btn btn--xs btn--ghost" NavigateUrl="<%# Urls.Consultation(Item.PatientId) %>" Visible="<%# CanConsult %>"><i class="ti ti-stethoscope" aria-hidden="true"></i> Open</asp:HyperLink>
          </div>
        </div>
      </ItemTemplate>
    </asp:Repeater>
    <asp:PlaceHolder ID="NoResults" runat="server"><div class="zone-a-empty" style="padding:20px; text-align:center;">No pending lab results to review.</div></asp:PlaceHolder>
  </div>
</div>

<asp:Panel ID="ReviewModal" runat="server" CssClass="modal-overlay" Visible="false">
  <div class="modal-frame" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Mark Reviewed — <asp:Literal ID="ReviewTitle" runat="server" /></strong>
      <asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModal_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="ReviewResultId" runat="server" />
      <div class="form-group"><label class="form-label" for="<%= ReviewNotes.ClientID %>">Review notes</label>
        <asp:TextBox ID="ReviewNotes" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:60px;" placeholder="Any clinical notes on this result..." /></div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModal_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="ConfirmReviewButton" runat="server" CssClass="btn btn--success" OnClick="ConfirmReviewButton_Click" CausesValidation="false">Confirm reviewed</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:Content>
