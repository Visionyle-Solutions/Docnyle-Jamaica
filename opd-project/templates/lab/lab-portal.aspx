<%@ Page Title="Lab Partner Portal" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="lab-portal.aspx.cs" Inherits="OpdProject.templates.lab.lab_portal" %>
<%-- External lab / imaging partner: claim orders from the patient's requisition, upload results and report files. Route: /lab-portal --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card mb-3">
    <div style="display:flex; justify-content:space-between; align-items:center;">
      <div>
        <div class="text-sm" style="font-weight:600;"><%: Hospital.Name %></div>
        <div class="text-xs text-muted">Welcome, <%: CurrentUser.FullName %><%: IsPartner && LabService.LabPartnerName(CurrentUser.LabPartnerId) != CurrentUser.FullName ? " (" + LabService.LabPartnerName(CurrentUser.LabPartnerId) + ")" : "" %> — <%: IsPartner ? "receive patient requisitions and upload results below" : "all lab and imaging orders for this clinic" %></div>
      </div>
      <asp:LinkButton ID="RefreshButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="RefreshButton_Click" CausesValidation="false"><i class="ti ti-refresh" aria-hidden="true"></i> Refresh</asp:LinkButton>
    </div>
  </div>

  <asp:Panel ID="ClaimPanel" runat="server" CssClass="card mb-3" DefaultButton="FindButton">
    <div class="text-sm text-secondary mb-2" style="font-weight:600; text-transform:uppercase;">Receive a requisition</div>
    <div class="text-xs text-muted mb-2">Enter the patient's mobile number to see their open lab and imaging orders, then receive the ones you will do. They are then added to your list.</div>
    <div style="display:flex; gap:12px; align-items:flex-end; flex-wrap:wrap;">
      <div class="form-group" style="margin:0;"><label class="form-label" for="<%= ClaimMobile.ClientID %>">Patient Mobile Number <span class="required">*</span></label>
        <asp:TextBox ID="ClaimMobile" runat="server" CssClass="form-control" TextMode="Phone" MaxLength="20" placeholder="e.g., 9111111115" autocomplete="off" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="ClaimMobile" ValidationGroup="Claim" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
      <asp:LinkButton ID="FindButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Claim" OnClick="FindButton_Click"><i class="ti ti-search" aria-hidden="true"></i> Find Orders</asp:LinkButton>
    </div>
    <asp:PlaceHolder ID="ClaimErrorPanel" runat="server" Visible="false"><div class="field-error mt-2"><asp:Literal ID="ClaimError" runat="server" /></div></asp:PlaceHolder>

    <asp:PlaceHolder ID="FoundPanel" runat="server" Visible="false">
      <div style="display:flex; justify-content:space-between; align-items:center; margin-top:12px;">
        <div class="text-sm" style="font-weight:600;"><asp:Literal ID="FoundTitle" runat="server" /></div>
        <asp:LinkButton ID="ReceiveAllButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="ReceiveAllButton_Click" CausesValidation="false"><i class="ti ti-file-import" aria-hidden="true"></i> Receive All</asp:LinkButton>
      </div>
      <asp:Repeater ID="FoundRepeater" runat="server" ItemType="OpdProject.Models.InvestigationOrder" OnItemCommand="FoundRepeater_ItemCommand">
        <ItemTemplate>
          <div style="display:flex; align-items:center; gap:12px; padding:10px 0; border-bottom:1px solid var(--border-default);">
            <div style="flex:1;">
              <div class="text-sm" style="font-weight:600;"><%#: Item.TestName %> <span class="text-xs text-muted" style="font-weight:400;">· <%#: Item.OrderId %></span></div>
              <div class="text-xs text-muted"><%#: PatientLabel(Item.PatientId) %> · <%#: Item.TestType %> · Ordered: <%#: FmtDateTime(Item.OrderedAt) %></div>
            </div>
            <asp:LinkButton runat="server" CssClass="btn btn--sm" CommandName="Claim" CommandArgument="<%# Item.OrderId %>" CausesValidation="false"><i class="ti ti-file-import" aria-hidden="true"></i> Receive</asp:LinkButton>
          </div>
        </ItemTemplate>
      </asp:Repeater>
    </asp:PlaceHolder>
  </asp:Panel>

  <div class="card">
    <div class="text-sm text-secondary mb-2" style="font-weight:600; text-transform:uppercase;"><%: IsPartner ? "Your Orders" : "Pending Orders" %></div>
    <asp:Repeater ID="OrdersRepeater" runat="server" ItemType="OpdProject.Models.InvestigationOrder" OnItemCommand="OrdersRepeater_ItemCommand">
      <ItemTemplate>
        <div style="display:flex; align-items:center; gap:12px; padding:12px; border-bottom:1px solid var(--border-default);">
          <div style="width:4px; height:50px; border-radius:2px; background:<%# Item.Status == "RESULTS_READY" ? "var(--color-success)" : "var(--color-primary)" %>;"></div>
          <div style="flex:1;">
            <div class="text-sm" style="font-weight:600;"><%#: Item.TestName %> <span class="text-xs text-muted" style="font-weight:400;">· <%#: Item.OrderId %></span></div>
            <div class="text-xs text-muted"><%#: PatientLabel(Item.PatientId) %> · <%#: Item.TestType %> · Dr: <%#: DoctorName(Item.ConsultationId) %></div>
            <div class="text-xs text-muted">Ordered: <%#: FmtDateTime(Item.OrderedAt) %></div>
          </div>
          <span class="badge <%# Item.Status == "RESULTS_READY" ? "badge--success" : Item.Status == "SENT" ? "badge--warning" : "badge--info" %>" style="font-size:10px;"><%#: Item.Status %></span>
          <asp:LinkButton runat="server" CssClass="btn btn--sm btn--ghost" CommandName="Receive" CommandArgument="<%# Item.OrderId %>" Visible='<%# Item.Status == "ORDERED" %>' CausesValidation="false"><i class="ti ti-inbox" aria-hidden="true"></i> Mark received</asp:LinkButton>
          <asp:LinkButton runat="server" CssClass="btn btn--sm btn--primary" CommandName="Upload" CommandArgument="<%# Item.OrderId %>" Visible='<%# Item.Status != "RESULTS_READY" %>' CausesValidation="false"><i class="ti ti-upload" aria-hidden="true"></i> Upload result</asp:LinkButton>
          <asp:PlaceHolder runat="server" Visible='<%# Item.Status == "RESULTS_READY" %>'><span class="text-xs text-success">✓ Uploaded<%#: FileCountLabel(Item.OrderId) %></span></asp:PlaceHolder>
        </div>
      </ItemTemplate>
    </asp:Repeater>
    <asp:PlaceHolder ID="NoOrders" runat="server"><div class="zone-a-empty" style="padding:20px; text-align:center;"><%: IsPartner ? "No orders yet — receive a patient's requisition above." : "No pending lab orders." %></div></asp:PlaceHolder>
  </div>
</div>

<asp:Panel ID="UploadModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="SubmitResultButton">
  <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true">
    <div class="modal-header"><strong>Upload Result — <asp:Literal ID="UploadTitle" runat="server" /></strong>
      <asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModal_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="UploadOrderId" runat="server" />
      <div class="hint-banner mb-3"><i class="ti ti-info-circle"></i> Uploading result for: <strong><asp:Literal ID="UploadFor" runat="server" /></strong></div>
      <div class="form-grid">
        <div class="form-group form-group--full"><label class="form-label" for="<%= ReportFiles.ClientID %>">Report files</label>
          <asp:FileUpload ID="ReportFiles" runat="server" CssClass="form-control" AllowMultiple="true" />
          <div class="text-xs text-muted mt-1">PDF, JPG, PNG or DICOM (.dcm) · up to <%: LabService.MaxFilesPerUpload %> files, <%: LabService.MaxFileBytes / (1024 * 1024) %> MB each</div>
          <asp:PlaceHolder ID="FileErrorPanel" runat="server" Visible="false"><div class="field-error"><asp:Literal ID="FileError" runat="server" /></div></asp:PlaceHolder></div>
        <div class="form-group form-group--full"><label class="form-label" for="<%= ResultSummary.ClientID %>">Result summary</label>
          <asp:TextBox ID="ResultSummary" runat="server" CssClass="form-control" TextMode="MultiLine" MaxLength="500" style="min-height:80px;" placeholder="e.g., CBC: Hb 12.5 g/dL, WBC 8200/μL, Platelets 2.1 Lac... (optional when a report file is attached)" />
          <asp:CustomValidator ID="SummaryOrFileValidator" runat="server" ValidationGroup="Result" OnServerValidate="SummaryOrFile_ServerValidate" Text="Enter a result summary or attach at least one report file" CssClass="field-error" Display="Dynamic" /></div>
        <div class="form-group"><label class="form-label" for="<%= ResultValue.ClientID %>">Result value</label><asp:TextBox ID="ResultValue" runat="server" CssClass="form-control" MaxLength="100" placeholder="e.g., 12.5 g/dL" /></div>
        <div class="form-group"><label class="form-label" for="<%= ReferenceRange.ClientID %>">Reference range</label><asp:TextBox ID="ReferenceRange" runat="server" CssClass="form-control" MaxLength="100" placeholder="e.g., 12.0-16.0 g/dL" /></div>
        <div class="form-group"><label class="form-label" for="<%= Interpretation.ClientID %>">Interpretation</label>
          <asp:DropDownList ID="Interpretation" runat="server" CssClass="form-select">
            <asp:ListItem Value="NORMAL">Normal</asp:ListItem><asp:ListItem Value="ABNORMAL_LOW">Abnormal — Low</asp:ListItem>
            <asp:ListItem Value="ABNORMAL_HIGH">Abnormal — High</asp:ListItem><asp:ListItem Value="CRITICAL">Critical</asp:ListItem>
          </asp:DropDownList></div>
        <div class="form-group"><label class="form-label" for="<%= Technician.ClientID %>">Lab technician</label><asp:TextBox ID="Technician" runat="server" CssClass="form-control" MaxLength="100" placeholder="Technician name" /></div>
        <div class="form-group form-group--full"><label class="form-label" for="<%= ResultNotes.ClientID %>">Additional notes</label><asp:TextBox ID="ResultNotes" runat="server" CssClass="form-control" MaxLength="300" placeholder="Any additional observations" /></div>
      </div>
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModal_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="SubmitResultButton" runat="server" CssClass="btn btn--success" ValidationGroup="Result" OnClick="SubmitResultButton_Click">Submit result</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:Content>
