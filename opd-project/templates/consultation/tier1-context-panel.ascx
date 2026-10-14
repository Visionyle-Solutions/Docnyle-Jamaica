<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="tier1-context-panel.ascx.cs" Inherits="OpdProject.templates.consultation.tier1_context_panel" %>
<%-- Tier 1 — patient context panel. Collapsible as a unit; each storyboard row expands its detail on the server. --%>
<div class="tier1-panel" id="tier1Panel">
  <div class="tier1-header" onclick="document.getElementById('tier1Panel').classList.toggle('tier1--collapsed');" style="cursor:pointer;">
    <span class="tier1-label">Patient Context</span>
    <span class="tier1-chevron" id="tier1Chevron">▼</span>
  </div>
  <div class="tier1-body" id="tier1Body">
    <asp:Repeater ID="RowsRepeater" runat="server" ItemType="OpdProject.Web.ContextRow" OnItemCommand="RowsRepeater_ItemCommand" ClientIDMode="Predictable">
      <ItemTemplate>
        <asp:LinkButton runat="server" CssClass='<%# "tier1-row" + (Item.Key == Expanded ? " tier1-row--expanded" : "") %>' CommandName="Expand" CommandArgument="<%# Item.Key %>" CausesValidation="false" style="color:inherit; text-decoration:none;">
          <span class="tier1-row-label"><%#: Item.Label %></span>
          <span class="tier1-row-summary"><%#: Item.Summary %></span>
          <span class="tier1-row-chevron">›</span>
        </asp:LinkButton>
      </ItemTemplate>
    </asp:Repeater>

    <asp:Panel ID="DetailPanel" runat="server" CssClass="tier1-detail tier1-detail--open" Visible="false">
      <%-- Visit history --%>
      <asp:PlaceHolder ID="VisitHistoryDetail" runat="server" Visible="false">
        <asp:Repeater ID="VisitsRepeater" runat="server" ItemType="OpdProject.Models.PastVisit" OnItemCommand="VisitsRepeater_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <asp:LinkButton runat="server" CssClass="zone-a-item" CommandName="ViewVisit" CommandArgument="<%# Item.ConsultationId %>" CausesValidation="false" style="display:block; color:inherit; text-decoration:none;">
              <%#: AppPage.FmtDate(Item.VisitDate) %> · <%#: Item.DoctorName %> · <%#: Item.ChiefComplaint %> · <em><%#: Item.PrimaryDiagnosis %></em>
            </asp:LinkButton>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoVisits" runat="server"><div class="zone-a-empty">No previous visits</div></asp:PlaceHolder>
        <asp:Panel ID="VisitDetail" runat="server" Visible="false" CssClass="card mt-2" style="padding:10px;">
          <asp:Literal ID="VisitDetailText" runat="server" />
        </asp:Panel>
      </asp:PlaceHolder>

      <%-- Medical history checklist --%>
      <asp:PlaceHolder ID="MedicalHistoryDetail" runat="server" Visible="false">
        <div class="text-xs text-muted mb-2" style="font-style:italic;">Mandatory past history of any chronic illness. If yes, since (month/year)</div>
        <asp:Repeater ID="HistoryRepeater" runat="server" ItemType="OpdProject.Models.MedicalHistoryItem" OnItemDataBound="HistoryRepeater_ItemDataBound" ClientIDMode="Predictable">
          <ItemTemplate>
            <div class="medhist-row" style="display:flex; align-items:center; gap:8px; padding:4px 0; border-bottom:1px solid var(--border-subtle, #f1f5f9);">
              <span style="width:20px; font-size:11px; color:var(--text-muted);"><%# Container.ItemIndex + 1 %>.</span>
              <asp:CheckBox ID="HasCondition" runat="server" />
              <span style="flex:1; font-size:12px;"><%#: Item.ItemName %></span>
              <asp:HiddenField ID="ConditionName" runat="server" Value="<%# Item.ItemName %>" />
              <asp:DropDownList ID="SinceMonth" runat="server" CssClass="form-select" style="width:64px; font-size:10px; padding:2px 4px; height:26px;" />
              <asp:DropDownList ID="SinceYear" runat="server" CssClass="form-select" style="width:78px; font-size:10px; padding:2px 4px; height:26px;" />
            </div>
          </ItemTemplate>
        </asp:Repeater>
        <div style="margin-top:8px;">
          <div class="text-xs text-muted mb-1">Any Other Ailment — Give Details:</div>
          <asp:TextBox ID="OtherHistory" runat="server" CssClass="form-control" style="font-size:12px;" placeholder="Other Conditions Not Listed Above..." />
        </div>
        <asp:LinkButton ID="SaveHistoryButton" runat="server" CssClass="btn btn--xs btn--primary mt-2" OnClick="SaveHistoryButton_Click" CausesValidation="false"><i class="ti ti-device-floppy" aria-hidden="true"></i> Save Medical History</asp:LinkButton>
      </asp:PlaceHolder>

      <%-- Recent labs --%>
      <asp:PlaceHolder ID="RecentLabsDetail" runat="server" Visible="false">
        <asp:Repeater ID="LabsRepeater" runat="server" ItemType="OpdProject.Models.LabResult">
          <ItemTemplate>
            <div class="zone-a-item" style="display:flex; justify-content:space-between;">
              <span><%#: Item.TestName %> <span class="text-xs text-muted"><%#: AppPage.FmtDate(Item.UploadedAt) %> · <%#: Item.Status == "REVIEWED" ? "reviewed" : "pending review" %></span>
                <asp:Repeater runat="server" ItemType="OpdProject.Models.LabResultAttachment" DataSource="<%# LabService.ListAttachments(Item.ResultId) %>">
                  <ItemTemplate><a class="text-xs" style="display:block;" href="<%#: Urls.LabReportFile(Item.AttachmentId) %>" target="_blank" rel="noopener"><i class="ti ti-paperclip" aria-hidden="true"></i> <%#: Item.FileName %></a></ItemTemplate>
                </asp:Repeater></span>
              <span style="font-size:10px; font-weight:600; color:<%# Item.Interpretation == "CRITICAL" ? "var(--color-danger)" : (Item.Interpretation ?? "").StartsWith("ABNORMAL") ? "#f59e0b" : "var(--color-success)" %>;"><%#: string.IsNullOrEmpty(Item.ResultValue) ? (Item.Interpretation ?? "").Replace("_", " ") : Item.ResultValue %></span>
            </div>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoLabs" runat="server"><div class="zone-a-empty">No lab results</div></asp:PlaceHolder>
      </asp:PlaceHolder>

      <%-- Current medications --%>
      <asp:PlaceHolder ID="CurrentMedsDetail" runat="server" Visible="false">
        <asp:Repeater ID="MedsRepeater" runat="server" ItemType="OpdProject.Models.Prescription">
          <ItemTemplate><div class="zone-a-item"><%#: Item.MedicineName %> <%#: Item.Strength %> · <%#: Item.TimesPerDay %>x/Day · since <%#: AppPage.FmtDate(Item.PrescribedDate) %></div></ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoMeds" runat="server"><div class="zone-a-empty">None</div></asp:PlaceHolder>
      </asp:PlaceHolder>

      <%-- Medical events --%>
      <asp:PlaceHolder ID="MedicalEventsDetail" runat="server" Visible="false">
        <asp:Repeater ID="EventsRepeater" runat="server" ItemType="OpdProject.Models.PatientMedicalEvent">
          <ItemTemplate><div class="zone-a-item"><%#: Item.EventName %><%#: Item.EventYear.HasValue ? " (" + Item.EventYear + ")" : "" %><%#: string.IsNullOrEmpty(Item.Notes) ? "" : " — " + Item.Notes %></div></ItemTemplate>
        </asp:Repeater>
        <div class="text-xs text-muted mt-2 mb-1">Quick Add (This Year):</div>
        <div style="display:flex; flex-wrap:wrap; gap:4px;">
          <asp:Repeater ID="EventPicksRepeater" runat="server" ItemType="OpdProject.Models.MedicalEventItem" OnItemCommand="EventPicksRepeater_ItemCommand" ClientIDMode="Predictable">
            <ItemTemplate><asp:LinkButton runat="server" CssClass="chip" CommandName="AddEvent" CommandArgument="<%# Item.EventId %>" CausesValidation="false"><%#: Item.EventName %></asp:LinkButton></ItemTemplate>
          </asp:Repeater>
        </div>
      </asp:PlaceHolder>

      <%-- Intake & consent --%>
      <asp:PlaceHolder ID="IntakeConsentDetail" runat="server" Visible="false">
        <div class="text-xs text-muted" style="font-weight:600; margin-bottom:4px;">Pre-Visit Intake</div>
        <asp:Literal ID="IntakeText" runat="server" />
        <asp:Repeater ID="IntakeRepeater" runat="server" ItemType="OpdProject.Models.IntakeAnswer">
          <ItemTemplate><div class="zone-a-item"><strong><%#: Item.Label %>:</strong> <%#: string.IsNullOrEmpty(Item.Answer) ? "—" : Item.Answer %></div></ItemTemplate>
        </asp:Repeater>
        <div class="text-xs text-muted" style="font-weight:600; margin:10px 0 4px;">Consent</div>
        <asp:Literal ID="ConsentText" runat="server" />
        <asp:Panel ID="SignConsentPanel" runat="server" DefaultButton="SignConsentButton" style="display:flex; gap:6px; align-items:center; margin-top:6px; flex-wrap:wrap;">
          <asp:TextBox ID="ConsentSignName" runat="server" CssClass="form-control" style="font-size:12px; max-width:240px;" aria-label="Patient or guardian name" />
          <asp:LinkButton ID="SignConsentButton" runat="server" CssClass="btn btn--xs btn--primary" OnClick="SignConsentButton_Click" CausesValidation="false"><i class="ti ti-signature" aria-hidden="true"></i> Sign Consent</asp:LinkButton>
          <div class="text-xs text-muted" style="width:100%;">Patient / Guardian Name</div>
        </asp:Panel>
      </asp:PlaceHolder>

      <%-- Family & social --%>
      <asp:PlaceHolder ID="FamilySocialDetail" runat="server" Visible="false">
        <asp:Repeater ID="FamilyRepeater" runat="server" ItemType="OpdProject.Models.PatientFamilyHistory">
          <ItemTemplate><div class="zone-a-item"><strong><%#: Item.RelationshipType %>:</strong> <%#: Item.ConditionName %><%#: string.IsNullOrEmpty(Item.Notes) ? "" : " — " + Item.Notes %></div></ItemTemplate>
        </asp:Repeater>
        <asp:Literal ID="SocialText" runat="server" />
        <asp:Panel ID="AddFamilyPanel" runat="server" DefaultButton="AddFamilyButton" style="display:flex; gap:6px; align-items:center; margin-top:8px; flex-wrap:wrap;">
          <asp:DropDownList ID="FamilyRelation" runat="server" CssClass="form-select" style="width:120px; font-size:12px;">
            <asp:ListItem Value="FATHER">Father</asp:ListItem><asp:ListItem Value="MOTHER">Mother</asp:ListItem><asp:ListItem Value="SIBLING">Sibling</asp:ListItem>
            <asp:ListItem Value="SPOUSE">Spouse</asp:ListItem><asp:ListItem Value="CHILD">Child</asp:ListItem><asp:ListItem Value="GRANDPARENT">Grandparent</asp:ListItem>
          </asp:DropDownList>
          <asp:TextBox ID="FamilyCondition" runat="server" CssClass="form-control" style="max-width:200px; font-size:12px;" placeholder="Condition" />
          <asp:TextBox ID="FamilyNotes" runat="server" CssClass="form-control" style="max-width:200px; font-size:12px;" placeholder="Notes (Optional)" />
          <asp:LinkButton ID="AddFamilyButton" runat="server" CssClass="btn btn--xs btn--primary" OnClick="AddFamilyButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add Relative</asp:LinkButton>
        </asp:Panel>
      </asp:PlaceHolder>
    </asp:Panel>
  </div>
</div>
