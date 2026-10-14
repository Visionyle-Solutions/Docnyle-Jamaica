<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-diagnosis-card.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_diagnosis_card" %>
<%-- Diagnosis · ICD-10: search → pick → add clinical reasoning (one record) → list.
     Replaces the former icd10-results / icd10-pending / icd10-diagnoses-list fragments. --%>
<div class="consult-section" id="sectionDiagnosis" data-section="diagnosis">
  <div class="consult-section-body">
    <asp:PlaceHolder ID="SearchHolder" runat="server">
      <asp:Panel ID="SearchPanel" runat="server" CssClass="form-group" DefaultButton="SearchButton" style="display:flex; gap:8px;">
        <asp:TextBox ID="Icd10SearchInput" runat="server" CssClass="form-control" placeholder="Search By Code Or Term (e.g. 'high bp', 'fever', 'sugar')" autocomplete="off" />
        <asp:LinkButton ID="SearchButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="SearchButton_Click"><i class="ti ti-search" aria-hidden="true"></i></asp:LinkButton>
      </asp:Panel>
      <div style="display:flex; flex-direction:column; gap:4px; margin-bottom:8px;">
        <asp:Repeater ID="ResultsRepeater" runat="server" ItemType="OpdProject.Models.Icd10Entry" OnItemCommand="ResultsRepeater_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <asp:LinkButton runat="server" CssClass="text-sm" CommandName="Pick" CommandArgument="<%# Item.Icd10Code %>"
              style="display:flex; justify-content:space-between; align-items:center; padding:8px 10px; border:1px solid var(--border-default); border-radius:var(--radius-md); color:inherit; text-decoration:none;">
              <span><strong><%#: Item.Icd10Code %></strong> — <%#: Item.Description %></span>
              <i class="ti ti-plus" aria-hidden="true"></i>
            </asp:LinkButton>
          </ItemTemplate>
        </asp:Repeater>
        <asp:Literal ID="SearchHint" runat="server" />
      </div>
      <asp:Panel ID="PendingPanel" runat="server" CssClass="card" style="background:var(--surface-page); margin-bottom:8px;" Visible="false">
        <div class="text-sm text-bold mb-2"><asp:Literal ID="PendingTitle" runat="server" /></div>
        <asp:HiddenField ID="PendingCode" runat="server" />
        <asp:HiddenField ID="PendingDescription" runat="server" />
        <asp:PlaceHolder ID="NotesHolder" runat="server">
        <div class="form-group mb-2">
          <label class="form-label" for="<%= ClinicalNotesInput.ClientID %>">Clinical Notes</label>
          <asp:TextBox ID="ClinicalNotesInput" runat="server" CssClass="form-control" TextMode="MultiLine" placeholder="Doctor's Diagnostic Reasoning" />
        </div>
        </asp:PlaceHolder>
        <div style="display:flex; gap:8px;">
          <asp:DropDownList ID="LateralityInput" runat="server" CssClass="form-select">
            <asp:ListItem Value="">Laterality: N/A</asp:ListItem>
            <asp:ListItem Value="LEFT">Left</asp:ListItem>
            <asp:ListItem Value="RIGHT">Right</asp:ListItem>
            <asp:ListItem Value="BILATERAL">Bilateral</asp:ListItem>
          </asp:DropDownList>
          <asp:DropDownList ID="AcuteChronicInput" runat="server" CssClass="form-select">
            <asp:ListItem Value="">Acute/Chronic: N/A</asp:ListItem>
            <asp:ListItem Value="ACUTE">Acute</asp:ListItem>
            <asp:ListItem Value="CHRONIC">Chronic</asp:ListItem>
          </asp:DropDownList>
        </div>
        <div style="display:flex; gap:8px; margin-top:8px;">
          <asp:LinkButton ID="ConfirmDiagnosisButton" runat="server" CssClass="btn btn--sm btn--success" OnClick="ConfirmDiagnosisButton_Click">Add Diagnosis</asp:LinkButton>
          <asp:LinkButton ID="CancelDiagnosisButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="CancelDiagnosisButton_Click">Cancel</asp:LinkButton>
        </div>
      </asp:Panel>
    </asp:PlaceHolder>
    <div class="text-xs text-muted mt-2 mb-1">Diagnoses Added</div>
    <asp:Repeater ID="DiagnosesRepeater" runat="server" ItemType="OpdProject.Models.ConsultationDiagnosis" OnItemCommand="DiagnosesRepeater_ItemCommand" ClientIDMode="Predictable">
      <ItemTemplate>
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:6px; padding:6px 8px; background:var(--surface-page); border-radius:var(--radius-md);">
          <div>
            <span class="badge badge--info"><%#: Item.Icd10Code %> · <%#: Item.Icd10Description %><%#: Item.IsPrimary ? " (Primary)" : "" %></span>
            <asp:PlaceHolder runat="server" Visible="<%# !string.IsNullOrEmpty(Item.AcuteChronic) %>"><span class="badge badge--warning" style="margin-left:4px;"><%#: Item.AcuteChronic %></span></asp:PlaceHolder>
            <asp:PlaceHolder runat="server" Visible="<%# !string.IsNullOrEmpty(Item.Laterality) %>"><span class="badge badge--info" style="margin-left:4px;"><%#: Item.Laterality %></span></asp:PlaceHolder>
            <asp:PlaceHolder runat="server" Visible="<%# !string.IsNullOrEmpty(Item.ClinicalNotes) %>"><div class="text-xs text-secondary mt-1" style="padding-left:4px;"><%#: Item.ClinicalNotes %></div></asp:PlaceHolder>
          </div>
          <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Remove" CommandArgument="<%# Item.DiagnosisId %>" Visible="<%# !Host.IsLocked %>" ToolTip='<%# "Remove " + Item.Icd10Code %>'>
            <i class="ti ti-x" aria-hidden="true"></i>
          </asp:LinkButton>
        </div>
      </ItemTemplate>
    </asp:Repeater>
    <asp:PlaceHolder ID="NoDiagnoses" runat="server"><div class="zone-a-empty">No diagnoses added yet.</div></asp:PlaceHolder>
  </div>
</div>
