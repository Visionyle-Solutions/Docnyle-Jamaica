<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="patient-history-tab.ascx.cs" Inherits="OpdProject.templates.patients.patient_history_tab" %>
<%-- Health record → Medical History: chronic conditions and surgeries/medical events, with an add/edit form. --%>
<div class="split-layout">
  <div class="split-main">
    <div class="record-section-title"><span>Chronic conditions</span></div>
    <table class="data-table">
      <thead>
        <tr><th scope="col">Condition</th><th scope="col">Since</th><th scope="col">Status</th><th scope="col">Notes</th><th scope="col">Recorded</th><th scope="col">Actions</th></tr>
      </thead>
      <tbody>
        <asp:Repeater ID="ConditionsRepeater" runat="server" ItemType="OpdProject.Models.PatientMedicalHistory" OnItemCommand="ConditionsRepeater_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <tr>
              <td><strong><%#: ConditionLabel(Item) %></strong></td>
              <td><%#: SinceLabel(Item.OnsetMonth, Item.OnsetYear) %></td>
              <td><span class="badge <%#: StatusBadge(Item.Status) %>"><%#: Item.Status ?? "ACTIVE" %></span></td>
              <td><%#: Item.ConditionName == "OTHER" || string.IsNullOrEmpty(Item.Notes) ? "—" : Item.Notes %></td>
              <td class="text-xs text-muted"><%#: RecordedLabel(Item.RecordedByUserId, Item.RecordedAt) %></td>
              <td style="white-space:nowrap;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Edit" CommandArgument="<%# Item.PatientMedHistoryId %>" CausesValidation="false" ToolTip='<%# "Edit " + ConditionLabel(Item) %>'><i class="ti ti-edit" aria-hidden="true"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Delete" CommandArgument="<%# Item.PatientMedHistoryId %>" CausesValidation="false" ToolTip='<%# "Remove " + ConditionLabel(Item) %>'
                  OnClientClick="return confirm('Remove this condition from the patient\'s medical history?');"><i class="ti ti-trash" aria-hidden="true"></i></asp:LinkButton>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoConditions" runat="server"><tr class="empty-row"><td colspan="6">No chronic conditions recorded.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>

    <div class="record-section-title"><span>Surgeries &amp; medical events</span></div>
    <table class="data-table">
      <thead>
        <tr><th scope="col">Event</th><th scope="col">When</th><th scope="col">Notes</th><th scope="col">Recorded</th><th scope="col">Actions</th></tr>
      </thead>
      <tbody>
        <asp:Repeater ID="EventsRepeater" runat="server" ItemType="OpdProject.Models.PatientMedicalEvent" OnItemCommand="EventsRepeater_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <tr>
              <td><strong><%#: Item.EventName %></strong></td>
              <td><%#: SinceLabel(Item.EventMonth, Item.EventYear) %></td>
              <td><%#: string.IsNullOrEmpty(Item.Notes) ? "—" : Item.Notes %></td>
              <td class="text-xs text-muted"><%#: RecordedLabel(Item.RecordedByUserId, Item.RecordedAt) %></td>
              <td style="white-space:nowrap;">
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Edit" CommandArgument="<%# Item.PatientMedEventId %>" CausesValidation="false" ToolTip='<%# "Edit " + Item.EventName %>'><i class="ti ti-edit" aria-hidden="true"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Delete" CommandArgument="<%# Item.PatientMedEventId %>" CausesValidation="false" ToolTip='<%# "Remove " + Item.EventName %>'
                  OnClientClick="return confirm('Remove this event from the patient\'s medical history?');"><i class="ti ti-trash" aria-hidden="true"></i></asp:LinkButton>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoEvents" runat="server"><tr class="empty-row"><td colspan="5">No surgeries or medical events recorded.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>
  </div>

  <div class="split-panel">
    <asp:Panel ID="FormPanel" runat="server" CssClass="card form-grid" DefaultButton="SaveButton">
      <asp:HiddenField ID="EditingId" runat="server" />
      <asp:HiddenField ID="EditingKind" runat="server" />
      <div class="form-group form-group--full" style="font-weight:700;"><asp:Literal ID="FormTitle" runat="server" Text="Add to medical history" /></div>
      <div class="form-group form-group--full">
        <span class="form-label">Record type <span class="required">*</span></span>
        <asp:RadioButtonList ID="RecordType" runat="server" RepeatLayout="Flow" RepeatDirection="Horizontal" CssClass="form-radio-group" AutoPostBack="true" OnSelectedIndexChanged="RecordType_SelectedIndexChanged">
          <asp:ListItem Value="CONDITION" Selected="True"> Chronic Condition</asp:ListItem>
          <asp:ListItem Value="EVENT"> Surgery / Event</asp:ListItem>
        </asp:RadioButtonList>
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= NamePick.ClientID %>"><asp:Literal ID="NameLabel" runat="server" Text="Condition" /> <span class="required">*</span></label>
        <asp:DropDownList ID="NamePick" runat="server" CssClass="form-select" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= NameOther.ClientID %>">Or type a name</label>
        <asp:TextBox ID="NameOther" runat="server" CssClass="form-control" MaxLength="100" placeholder="Not in the list" />
        <asp:CustomValidator ID="NameValidator" runat="server" ValidationGroup="MedHistory" OnServerValidate="NameValidator_ServerValidate" Text="Pick from the list or type a name" CssClass="field-error" Display="Dynamic" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= SinceMonth.ClientID %>">Since (month)</label>
        <asp:DropDownList ID="SinceMonth" runat="server" CssClass="form-select" />
      </div>
      <div class="form-group">
        <label class="form-label" for="<%= SinceYear.ClientID %>">Since (year)</label>
        <asp:DropDownList ID="SinceYear" runat="server" CssClass="form-select" />
      </div>
      <asp:PlaceHolder ID="StatusHolder" runat="server">
        <div class="form-group">
          <label class="form-label" for="<%= StatusInput.ClientID %>">Status</label>
          <asp:DropDownList ID="StatusInput" runat="server" CssClass="form-select">
            <asp:ListItem Value="ACTIVE">Active</asp:ListItem>
            <asp:ListItem Value="CONTROLLED">Controlled</asp:ListItem>
            <asp:ListItem Value="RESOLVED">Resolved</asp:ListItem>
          </asp:DropDownList>
        </div>
      </asp:PlaceHolder>
      <div class="form-group">
        <label class="form-label" for="<%= NotesInput.ClientID %>">Notes</label>
        <asp:TextBox ID="NotesInput" runat="server" CssClass="form-control" MaxLength="250" placeholder="e.g. Type 2, on insulin" />
      </div>
      <div class="form-group form-group--full" style="display:flex; gap:8px;">
        <asp:Button ID="SaveButton" runat="server" Text="Add" CssClass="btn btn--success" ValidationGroup="MedHistory" OnClick="SaveButton_Click" />
        <asp:Button ID="ResetButton" runat="server" Text="Reset" CssClass="btn btn--danger" CausesValidation="false" OnClick="ResetButton_Click" />
      </div>
    </asp:Panel>
  </div>
</div>
