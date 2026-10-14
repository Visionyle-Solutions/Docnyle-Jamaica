<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-confidential-notes.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_confidential_notes" %>
<div class="consult-section" id="sectionNotes" data-section="notes">
  <div class="consult-section-body">
    <div class="hint-banner" style="background:var(--color-info-bg); border-left-color:var(--color-info);">
      <i class="ti ti-shield-lock" aria-hidden="true"></i>
      This is confidential and visible only to you. It will never appear in prints, exports, or audit records.
    </div>
    <asp:TextBox ID="ConfidentialNoteText" runat="server" CssClass="form-control mt-2" TextMode="MultiLine" style="min-height:70px;" placeholder="Private notes for your own reference — patient concerns, clinical hunches, follow-up reminders..." />
    <asp:LinkButton ID="SaveNoteButton" runat="server" CssClass="btn btn--sm btn--primary mt-2" OnClick="SaveNoteButton_Click" CausesValidation="false">
      <i class="ti ti-device-floppy" aria-hidden="true"></i> Save Note
    </asp:LinkButton>
    <span class="text-xs text-muted"><asp:Literal ID="SavedAt" runat="server" /></span>
  </div>
</div>
