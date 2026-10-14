<%@ Page Title="Phrase Library" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="phrase-management.aspx.cs" Inherits="OpdProject.templates.settings.phrase_management" %>
<%-- Phrase Library = SOAP "/" macros (hospital-wide, specialty, doctor-specific; optional condition link)
     and clinical conditions. Route: /settings/phrases --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="page-container">
  <div class="card">
    <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:12px; gap:8px; flex-wrap:wrap;">
      <div style="display:flex; gap:4px; flex-wrap:wrap;">
        <asp:DropDownList ID="ScopeFilter" runat="server" CssClass="form-select" style="width:150px;" AutoPostBack="true" OnSelectedIndexChanged="Filter_Changed">
          <asp:ListItem Value="ALL">All scopes</asp:ListItem><asp:ListItem Value="MINE">My phrases</asp:ListItem><asp:ListItem Value="DOCTOR">Doctor-specific</asp:ListItem>
          <asp:ListItem Value="SPECIALTY">Specialty</asp:ListItem><asp:ListItem Value="GLOBAL">Hospital-wide</asp:ListItem>
        </asp:DropDownList>
        <asp:DropDownList ID="SectionFilter" runat="server" CssClass="form-select" style="width:130px;" AutoPostBack="true" OnSelectedIndexChanged="Filter_Changed">
          <asp:ListItem Value="ALL">All sections</asp:ListItem><asp:ListItem Value="S">Subjective</asp:ListItem><asp:ListItem Value="O">Objective</asp:ListItem>
          <asp:ListItem Value="P">Plan</asp:ListItem><asp:ListItem Value="ANY">Any section</asp:ListItem>
        </asp:DropDownList>
        <asp:DropDownList ID="ConditionFilter" runat="server" CssClass="form-select" style="width:180px;" AutoPostBack="true" OnSelectedIndexChanged="Filter_Changed" />
      </div>
      <asp:LinkButton ID="AddPhraseButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="AddPhraseButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> New phrase</asp:LinkButton>
    </div>
    <div class="text-xs text-muted mb-2">Type the trigger (e.g. <code>/htn</code>) in a SOAP note — it is expanded when the note is saved. A doctor-specific phrase replaces a specialty or hospital-wide phrase with the same trigger for that doctor. Condition-linked phrases are only offered when the consultation has a matching ICD-10 diagnosis.</div>
    <table class="data-table">
      <thead><tr><th scope="col">Trigger</th><th scope="col">Phrase text</th><th scope="col">Section</th><th scope="col">Scope</th><th scope="col">Condition</th><th scope="col">Actions</th></tr></thead>
      <tbody>
        <asp:Repeater ID="PhrasesRepeater" runat="server" ItemType="OpdProject.Models.Phrase" OnItemCommand="PhrasesRepeater_ItemCommand">
          <ItemTemplate>
            <tr>
              <td><code><%#: Item.TriggerKeyword %></code></td>
              <td><%#: Item.PhraseText %></td>
              <td><%#: SectionLabel(Item.Section) %></td>
              <td><%# ScopeBadge(Item) %></td>
              <td class="text-xs"><%#: ConditionName(Item.ConditionId) %></td>
              <td style="white-space:nowrap;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="EditPhrase" CommandArgument="<%# Item.PhraseId %>" CausesValidation="false" Visible="<%# CanEdit(Item) %>" ToolTip="Edit phrase"><i class="ti ti-edit"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="DeletePhrase" CommandArgument="<%# Item.PhraseId %>" CausesValidation="false" Visible='<%# Item.Scope == "DOCTOR" && CanEdit(Item) %>'
                  style="color:var(--color-danger);" ToolTip="Delete phrase" OnClientClick="return confirm('Remove this phrase from the library?');"><i class="ti ti-trash"></i></asp:LinkButton>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="EmptyRow" runat="server"><tr class="empty-row"><td colspan="6">No phrases found.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>
  </div>

  <div class="card mt-3">
    <div class="text-sm" style="font-weight:600; margin-bottom:4px;">Conditions</div>
    <div class="text-xs text-muted mb-2">Group phrases by condition. A condition matches a consultation when a diagnosis ICD-10 code starts with one of its prefixes (e.g. <code>I10</code> matches I10, <code>E11</code> matches E11.9).</div>
    <div style="display:flex; flex-wrap:wrap; gap:6px; margin-bottom:10px;">
      <asp:Repeater ID="ConditionsRepeater" runat="server" ItemType="OpdProject.Models.ClinicalCondition" OnItemCommand="ConditionsRepeater_ItemCommand">
        <ItemTemplate>
          <span class="badge badge--info" style="display:inline-flex; align-items:center; gap:4px; font-size:12px;"><%#: Item.Name %>
            <span class="text-xs" style="opacity:.75;"><%#: string.IsNullOrEmpty(Item.Icd10Prefixes) ? "no ICD-10 link" : Item.Icd10Prefixes %></span>
            <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Remove" CommandArgument="<%# Item.ConditionId %>" CausesValidation="false" Visible="<%# IsAdmin %>"
              style="height:18px; padding:0 4px;" ToolTip="Remove condition" OnClientClick="return confirm('Remove this condition? Linked phrases stay but are no longer offered until relinked.');">×</asp:LinkButton></span>
        </ItemTemplate>
      </asp:Repeater>
    </div>
    <asp:Panel ID="AddConditionPanel" runat="server" DefaultButton="AddConditionButton" style="display:flex; gap:6px; flex-wrap:wrap; align-items:flex-start;">
      <asp:TextBox ID="NewConditionName" runat="server" CssClass="form-control" style="width:200px;" placeholder="Condition name" MaxLength="60" />
      <asp:TextBox ID="NewConditionPrefixes" runat="server" CssClass="form-control" style="width:220px;" placeholder="ICD-10 prefixes, e.g. I10,I11" MaxLength="80" />
      <asp:LinkButton ID="AddConditionButton" runat="server" CssClass="btn btn--sm" ValidationGroup="Condition" OnClick="AddConditionButton_Click"><i class="ti ti-plus" aria-hidden="true"></i> Add condition</asp:LinkButton>
      <asp:RequiredFieldValidator runat="server" ControlToValidate="NewConditionName" ValidationGroup="Condition" Text="Enter a condition name" CssClass="field-error" Display="Dynamic" />
      <asp:RegularExpressionValidator runat="server" ControlToValidate="NewConditionPrefixes" ValidationGroup="Condition" ValidationExpression="^\s*[A-Za-z][0-9A-Za-z.]*(\s*,\s*[A-Za-z][0-9A-Za-z.]*)*\s*$" Text="Comma-separated ICD-10 prefixes" CssClass="field-error" Display="Dynamic" />
    </asp:Panel>
  </div>
</div>

<asp:Panel ID="PhraseModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="SavePhraseButton">
  <div class="modal-frame modal-frame--wide" role="dialog" aria-modal="true">
    <div class="modal-header"><strong><asp:Literal ID="PhraseModalTitle" runat="server" /></strong><asp:LinkButton runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CloseModal_Click" CausesValidation="false"><i class="ti ti-x"></i></asp:LinkButton></div>
    <div class="modal-body">
      <asp:HiddenField ID="EditingPhraseId" runat="server" />
      <asp:ValidationSummary runat="server" ValidationGroup="Phrase" CssClass="validation-summary" />
      <div class="form-group"><label class="form-label" for="<%= PhraseTrigger.ClientID %>">Trigger (starts with /)</label><asp:TextBox ID="PhraseTrigger" runat="server" CssClass="form-control" placeholder="/keyword" MaxLength="20" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="PhraseTrigger" ValidationGroup="Phrase" ErrorMessage="Trigger is required" Text="Required" CssClass="field-error" Display="Dynamic" />
        <asp:RegularExpressionValidator runat="server" ControlToValidate="PhraseTrigger" ValidationGroup="Phrase" ValidationExpression="^/?[A-Za-z0-9_]{2,19}$" ErrorMessage="Trigger: letters, digits or _ (2–19 characters)" Text="Invalid trigger" CssClass="field-error" Display="Dynamic" /></div>
      <div class="form-group"><label class="form-label" for="<%= PhraseText.ClientID %>">Phrase text</label><asp:TextBox ID="PhraseText" runat="server" CssClass="form-control" TextMode="MultiLine" style="min-height:80px;" placeholder="The text to insert" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="PhraseText" ValidationGroup="Phrase" ErrorMessage="Phrase text is required" Text="Required" CssClass="field-error" Display="Dynamic" /></div>
      <div class="form-grid">
        <div class="form-group"><label class="form-label" for="<%= PhraseSection.ClientID %>">Section</label>
          <asp:DropDownList ID="PhraseSection" runat="server" CssClass="form-select"><asp:ListItem Value="S">Subjective</asp:ListItem><asp:ListItem Value="O">Objective</asp:ListItem><asp:ListItem Value="P">Plan</asp:ListItem><asp:ListItem Value="ANY">Any</asp:ListItem></asp:DropDownList></div>
        <div class="form-group"><label class="form-label" for="<%= PhraseCondition.ClientID %>">Condition</label><asp:DropDownList ID="PhraseCondition" runat="server" CssClass="form-select" /></div>
        <div class="form-group"><label class="form-label" for="<%= PhraseScope.ClientID %>">Scope</label>
          <asp:DropDownList ID="PhraseScope" runat="server" CssClass="form-select" AutoPostBack="true" OnSelectedIndexChanged="PhraseScope_Changed"><asp:ListItem Value="DOCTOR">Doctor-specific</asp:ListItem><asp:ListItem Value="SPECIALTY">Specialty</asp:ListItem><asp:ListItem Value="GLOBAL">Hospital-wide</asp:ListItem></asp:DropDownList></div>
        <asp:PlaceHolder ID="DoctorHolder" runat="server"><div class="form-group"><label class="form-label" for="<%= PhraseDoctor.ClientID %>">Doctor</label><asp:DropDownList ID="PhraseDoctor" runat="server" CssClass="form-select" /></div></asp:PlaceHolder>
        <asp:PlaceHolder ID="SpecialtyHolder" runat="server" Visible="false"><div class="form-group"><label class="form-label" for="<%= PhraseSpecialty.ClientID %>">Specialty</label><asp:DropDownList ID="PhraseSpecialty" runat="server" CssClass="form-select" /></div></asp:PlaceHolder>
      </div>
      <asp:CustomValidator ID="ScopeValidator" runat="server" ValidationGroup="Phrase" OnServerValidate="ScopeValidator_ServerValidate" ErrorMessage="Only administrators can create specialty or hospital-wide phrases, or phrases for another doctor." Display="None" />
    </div>
    <div class="modal-footer">
      <asp:LinkButton runat="server" CssClass="btn btn--ghost" OnClick="CloseModal_Click" CausesValidation="false">Cancel</asp:LinkButton>
      <asp:LinkButton ID="SavePhraseButton" runat="server" CssClass="btn btn--success" ValidationGroup="Phrase" OnClick="SavePhraseButton_Click">Save</asp:LinkButton>
    </div>
  </div>
</asp:Panel>
</asp:Content>
