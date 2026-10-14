<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="consultation-cpoe-card.ascx.cs" Inherits="OpdProject.templates.consultation.consultation_cpoe_card" %>
<%-- Prescription / CPOE. Search → pick → full order form → server-side drug-allergy safety check and
     medication reconciliation (override/acknowledge with an audited reason) → current medication list.
     Replaces the former rx-results / rx-current-medication fragments. --%>
<div class="consult-section" id="sectionCpoe" data-section="cpoe">
  <asp:PlaceHolder ID="OrderingHolder" runat="server">
    <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:8px;">
      <span class="text-sm" style="font-weight:600;"><i class="ti ti-prescription" aria-hidden="true"></i> Prescriptions</span>
      <div style="display:flex; gap:6px;">
        <asp:LinkButton ID="RepeatPrevButton" runat="server" CssClass="btn btn--xs btn--ghost" ToolTip="Copy prescriptions from your previous visit with this patient" OnClick="RepeatPrevButton_Click" CausesValidation="false"><i class="ti ti-copy" aria-hidden="true"></i> Repeat Prev</asp:LinkButton>
        <asp:LinkButton ID="ManualEntryButton" runat="server" CssClass="btn btn--xs btn--ghost" ToolTip="Enter a drug manually without the formulary" OnClick="ManualEntryButton_Click" CausesValidation="false"><i class="ti ti-edit" aria-hidden="true"></i> Manual</asp:LinkButton>
      </div>
    </div>

    <asp:Panel ID="RxSearchPanel" runat="server" CssClass="form-group mb-2" DefaultButton="RxSearchButton" style="display:flex; gap:8px;">
      <asp:TextBox ID="RxSearchInput" runat="server" CssClass="form-control" placeholder="Search formulary by name, generic, or therapeutic class..." autocomplete="off" />
      <asp:LinkButton ID="RxSearchButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="RxSearchButton_Click" CausesValidation="false"><i class="ti ti-search" aria-hidden="true"></i></asp:LinkButton>
    </asp:Panel>
    <div style="display:flex; flex-direction:column; gap:4px; margin-bottom:8px;">
      <asp:Repeater ID="RxResults" runat="server" ItemType="OpdProject.Models.Drug" OnItemCommand="RxResults_ItemCommand" ClientIDMode="Predictable">
        <ItemTemplate>
          <asp:LinkButton runat="server" CssClass="text-sm" CommandName="Pick" CommandArgument="<%# Item.DrugId %>" CausesValidation="false"
            style="display:flex; justify-content:space-between; align-items:flex-start; padding:8px 10px; border:1px solid var(--border-default); border-radius:var(--radius-md); color:inherit; text-decoration:none;">
            <div>
              <strong><%#: Item.BrandName %></strong> (<%#: Item.GenericName %>) — <%#: Item.DefaultDosage %>
              <div style="font-size:11px; color:var(--color-danger); font-weight:600;"><%#: Item.TherapeuticClass %></div>
              <div style="font-size:11px; color:var(--text-muted);"><%#: Item.DrugForm %> · <%#: Item.DefaultRoute %> · <%#: Item.Composition %></div>
            </div>
            <i class="ti ti-plus" aria-hidden="true"></i>
          </asp:LinkButton>
        </ItemTemplate>
      </asp:Repeater>
      <asp:Literal ID="RxSearchHint" runat="server" />
    </div>

    <%-- Pending prescription form --%>
    <asp:Panel ID="PendingPanel" runat="server" Visible="false">
      <div style="border:1px solid var(--border-default); border-radius:8px; padding:16px; background:#fafcff;">
        <asp:Panel ID="ControlledWarning" runat="server" Visible="false" style="padding:10px; margin-bottom:10px; background:#fff7ed; border-left:4px solid #f59e0b; border-radius:4px;">
          <strong style="color:#f59e0b;">NARCOTIC / CONTROLLED SUBSTANCE</strong><br />Verify patient identity and clinical necessity before prescribing.
        </asp:Panel>
        <asp:ValidationSummary runat="server" ValidationGroup="Rx" CssClass="validation-summary" HeaderText="Please complete the prescription:" />
        <div class="form-grid">
          <div class="form-group">
            <asp:PlaceHolder ID="CategoryHolder" runat="server">
            <label class="form-label" for="<%= RxCategory.ClientID %>">Categories</label>
            <asp:DropDownList ID="RxCategory" runat="server" CssClass="form-select">
              <asp:ListItem Value="PRESCRIPTION">Prescription Medicines</asp:ListItem>
              <asp:ListItem Value="OTC">OTC Medicines</asp:ListItem>
              <asp:ListItem Value="CONTROLLED">Controlled Substances</asp:ListItem>
              <asp:ListItem Value="AYURVEDIC">Ayurvedic / Herbal</asp:ListItem>
            </asp:DropDownList>
            </asp:PlaceHolder>
            <div class="rx-meta-text" style="color:var(--color-danger); font-weight:600; font-size:11px;"><asp:Literal ID="TherapeuticClassText" runat="server" /></div>
          </div>
          <div class="form-group">
            <label class="form-label" for="<%= RxMedicineName.ClientID %>">Medicine Name <span class="required">*</span></label>
            <asp:TextBox ID="RxMedicineName" runat="server" CssClass="form-control" MaxLength="100" />
            <asp:RequiredFieldValidator runat="server" ControlToValidate="RxMedicineName" ValidationGroup="Rx" ErrorMessage="Medicine name is required" Text="Required" CssClass="field-error" Display="Dynamic" />
            <div class="rx-meta-text" style="font-size:11px; color:var(--text-muted);"><asp:Literal ID="CompositionText" runat="server" /></div>
            <asp:HiddenField ID="RxComposition" runat="server" />
            <asp:HiddenField ID="RxGenericClass" runat="server" />
          </div>
        </div>
        <div style="display:flex; gap:8px; flex-wrap:wrap; margin-top:8px;">
          <div class="form-group" style="flex:1; min-width:100px;"><label class="form-label" for="<%= RxStrength.ClientID %>">Strength</label><asp:TextBox ID="RxStrength" runat="server" CssClass="form-control" /></div>
          <div class="form-group" style="flex:1; min-width:80px;"><label class="form-label" for="<%= RxDrugForm.ClientID %>">Forms</label><asp:TextBox ID="RxDrugForm" runat="server" CssClass="form-control" placeholder="Tablet" /></div>
          <div class="form-group" style="flex:1; min-width:90px;"><label class="form-label" for="<%= RxDeliveryRoute.ClientID %>">Delivery Route</label><asp:TextBox ID="RxDeliveryRoute" runat="server" CssClass="form-control" placeholder="Oral" /></div>
          <asp:PlaceHolder ID="TargetAreaHolder" runat="server"><div class="form-group" style="flex:1; min-width:90px;"><label class="form-label" for="<%= RxTargetArea.ClientID %>">Target Area</label><asp:TextBox ID="RxTargetArea" runat="server" CssClass="form-control" /></div></asp:PlaceHolder>
          <div class="form-group" style="flex:1; min-width:80px;">
            <label class="form-label" for="<%= RxTakeInUnit.ClientID %>">Take-In Unit <span class="required">*</span></label>
            <asp:TextBox ID="RxTakeInUnit" runat="server" CssClass="form-control" placeholder="tablet(s)" />
            <asp:RequiredFieldValidator runat="server" ControlToValidate="RxTakeInUnit" ValidationGroup="Rx" ErrorMessage="Take-in unit is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          </div>
        </div>
        <div style="display:flex; gap:8px; flex-wrap:wrap; margin-top:8px; align-items:flex-end;">
          <div class="form-group" style="width:80px;">
            <label class="form-label" for="<%= RxTakeInQty.ClientID %>">Take-In Qty <span class="required">*</span></label>
            <asp:TextBox ID="RxTakeInQty" runat="server" CssClass="form-control" TextMode="Number" step="0.5" Text="1" />
            <asp:RangeValidator runat="server" ControlToValidate="RxTakeInQty" ValidationGroup="Rx" Type="Double" MinimumValue="0.5" MaximumValue="100" ErrorMessage="Take-in quantity must be 0.5–100" Text="0.5–100" CssClass="field-error" Display="Dynamic" />
            <asp:RequiredFieldValidator runat="server" ControlToValidate="RxTakeInQty" ValidationGroup="Rx" ErrorMessage="Take-in quantity is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          </div>
          <div class="form-group" style="width:80px;">
            <label class="form-label" for="<%= RxDurationValue.ClientID %>">Duration <span class="required">*</span></label>
            <asp:TextBox ID="RxDurationValue" runat="server" CssClass="form-control" TextMode="Number" />
            <asp:RequiredFieldValidator runat="server" ControlToValidate="RxDurationValue" ValidationGroup="Rx" ErrorMessage="Duration is required" Text="Required" CssClass="field-error" Display="Dynamic" />
            <asp:RangeValidator runat="server" ControlToValidate="RxDurationValue" ValidationGroup="Rx" Type="Integer" MinimumValue="1" MaximumValue="365" ErrorMessage="Duration must be 1–365" Text="1–365" CssClass="field-error" Display="Dynamic" />
          </div>
          <div class="form-group" style="flex:1;">
            <label class="form-label">&nbsp;</label>
            <div class="chip-row"><%= Chips(RxDurationUnit, "days|day(s)", "weeks|week(s)", "months|month(s)", "hours|hours", "minutes|minutes") %></div>
            <asp:HiddenField ID="RxDurationUnit" runat="server" Value="days" />
          </div>
          <div class="form-group" style="width:140px;">
            <label class="form-label" for="<%= RxPeriodStart.ClientID %>">Period (Start) <span class="required">*</span></label>
            <asp:TextBox ID="RxPeriodStart" runat="server" CssClass="form-control" TextMode="Date" />
            <asp:RequiredFieldValidator runat="server" ControlToValidate="RxPeriodStart" ValidationGroup="Rx" ErrorMessage="Start date is required" Text="Required" CssClass="field-error" Display="Dynamic" />
          </div>
        </div>
        <div style="margin-top:12px; padding:10px; border:1px solid var(--border-subtle,#e2e8f0); border-radius:6px;">
          <label class="form-label" style="color:var(--color-primary); font-weight:600;">Frequency <span class="required">*</span></label>
          <div style="display:flex; gap:12px; align-items:center; flex-wrap:wrap;">
            <asp:RadioButtonList ID="RxFreqType" runat="server" RepeatLayout="Flow" RepeatDirection="Horizontal" CssClass="inline-list">
              <asp:ListItem Value="times" Selected="True">Times</asp:ListItem>
              <asp:ListItem Value="repeat">Repeat</asp:ListItem>
            </asp:RadioButtonList>
            <asp:TextBox ID="RxFreqValue" runat="server" CssClass="form-control" TextMode="Number" Text="3" style="width:64px;" />
            <asp:RangeValidator runat="server" ControlToValidate="RxFreqValue" ValidationGroup="Rx" Type="Integer" MinimumValue="1" MaximumValue="24" ErrorMessage="Frequency must be 1–24" Text="1–24" CssClass="field-error" Display="Dynamic" />
            <span style="font-size:12px;">per</span>
            <div class="chip-row"><%= Chips(RxFreqPeriod, "day|day", "week|week", "month|month") %></div>
            <asp:HiddenField ID="RxFreqPeriod" runat="server" Value="day" />
          </div>
          <asp:PlaceHolder ID="DoseTimingHolder" runat="server">
          <div style="margin-top:8px; display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
            <span style="font-size:12px; color:var(--text-muted);">Dose Timing</span>
            <asp:CheckBox ID="TimingBf" runat="server" Text=" BF" CssClass="rx-timing-label" ToolTip="Before Breakfast / Morning" />
            <asp:CheckBox ID="TimingAn" runat="server" Text=" AN" CssClass="rx-timing-label" ToolTip="Afternoon / After Lunch" />
            <asp:CheckBox ID="TimingNi" runat="server" Text=" NI" CssClass="rx-timing-label" ToolTip="Night / Before Bed" />
            <asp:CheckBox ID="TimingEm" runat="server" Text=" EM" CssClass="rx-timing-label" ToolTip="Early Morning / On waking" />
          </div>
          </asp:PlaceHolder>
        </div>
        <asp:PlaceHolder ID="EventTimingHolder" runat="server">
        <div style="margin-top:8px; padding:10px; border:1px solid var(--border-subtle,#e2e8f0); border-radius:6px;">
          <label class="form-label" style="color:var(--color-primary); font-weight:600;">Timing (relative to event)</label>
          <div style="display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
            <asp:TextBox ID="RxTimingFrom" runat="server" CssClass="form-control" TextMode="Number" style="width:60px;" placeholder="10" />
            <span style="font-size:12px;">to</span>
            <asp:TextBox ID="RxTimingTo" runat="server" CssClass="form-control" TextMode="Number" style="width:60px;" placeholder="20" />
            <span style="font-size:12px;">Unit</span>
            <div class="chip-row"><%= Chips(RxTimingUnit, "hours|hour(s)", "minutes|minutes") %></div>
            <asp:HiddenField ID="RxTimingUnit" runat="server" Value="minutes" />
            <span style="font-size:12px;">Direction</span>
            <div class="chip-row"><%= Chips(RxTimingDir, "after|after", "before|before") %></div>
            <asp:HiddenField ID="RxTimingDir" runat="server" Value="before" />
          </div>
          <div style="margin-top:6px; display:flex; gap:4px; align-items:center;">
            <span style="font-size:12px; color:var(--text-muted);">Event</span>
            <div class="chip-row"><%= Chips(RxEvent, "meals|meals", "lunch|lunch", "dinner|dinner", "bedtime|bedtime", "morning|morning", "breakfast|breakfast") %></div>
            <asp:HiddenField ID="RxEvent" runat="server" Value="meals" />
          </div>
        </div>
        </asp:PlaceHolder>
        <div style="display:flex; gap:12px; align-items:center; margin-top:10px;">
          <asp:CheckBox ID="RxPrn" runat="server" Text=" PRN Needed" style="font-size:12px; font-weight:600;" ToolTip="Pro Re Nata — take as needed, not on a fixed schedule" />
          <asp:PlaceHolder ID="InstructionsHolder" runat="server">
          <div class="form-group" style="flex:1; margin:0;">
            <label class="form-label" for="<%= RxInstructions.ClientID %>">Instructions</label>
            <asp:TextBox ID="RxInstructions" runat="server" CssClass="form-control" placeholder="Special usage instructions (left blank = generated instruction)" />
          </div>
          </asp:PlaceHolder>
        </div>
        <div style="display:flex; justify-content:flex-end; gap:8px; margin-top:12px;">
          <asp:LinkButton ID="ConfirmRxButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Rx" OnClick="ConfirmRxButton_Click">Add</asp:LinkButton>
          <asp:LinkButton ID="ResetRxButton" runat="server" CssClass="btn btn--danger btn--ghost" CausesValidation="false" OnClick="ResetRxButton_Click">Reset</asp:LinkButton>
          <asp:LinkButton ID="CancelRxButton" runat="server" CssClass="btn btn--ghost" CausesValidation="false" OnClick="CancelRxButton_Click">Cancel</asp:LinkButton>
        </div>
      </div>
    </asp:Panel>

    <%-- Safety alerts: drug-allergy conflicts (CRITICAL) and medication reconciliation --%>
    <asp:Panel ID="AlertPanel" runat="server" Visible="false" CssClass="card mt-2" style="border-left:4px solid var(--color-danger);">
      <div style="font-weight:700; color:var(--color-danger); margin-bottom:8px;"><i class="ti ti-alert-octagon" aria-hidden="true"></i> <asp:Literal ID="AlertTitle" runat="server" /></div>
      <asp:Repeater ID="AlertsRepeater" runat="server" ItemType="OpdProject.Models.ClinicalAlert">
        <ItemTemplate>
          <div style="padding:8px 12px; margin-bottom:6px; background:<%# Item.Severity == "CRITICAL" ? "#fef2f2" : Item.Severity == "INFO" ? "#eff6ff" : "#fffbeb" %>; border-left:4px solid <%# Item.Severity == "CRITICAL" ? "var(--color-danger)" : Item.Severity == "INFO" ? "var(--color-primary)" : "#f59e0b" %>; border-radius:4px; font-size:13px;">
            <strong><%#: Item.Severity.Replace("_", " ") %></strong> · <em><%#: Item.Type.Replace("_", " ") %></em><br /><%#: Item.Message %>
          </div>
        </ItemTemplate>
      </asp:Repeater>
      <div class="form-group mt-2">
        <label class="form-label text-xs" for="<%= OverrideReason.ClientID %>">Override reason (required for audit)</label>
        <asp:TextBox ID="OverrideReason" runat="server" CssClass="form-control" placeholder="e.g. Allergy history disputed, clinical judgment..." />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="OverrideReason" ValidationGroup="Override" Text="A reason is required to proceed" CssClass="field-error" Display="Dynamic" />
      </div>
      <p class="text-xs text-muted">This will be permanently recorded in the safety audit trail.</p>
      <div style="display:flex; gap:8px;">
        <asp:LinkButton ID="CancelOverrideButton" runat="server" CssClass="btn btn--sm btn--ghost" CausesValidation="false" OnClick="CancelOverrideButton_Click">Cancel prescription</asp:LinkButton>
        <asp:LinkButton ID="ProceedOverrideButton" runat="server" CssClass="btn btn--sm btn--danger" ValidationGroup="Override" OnClick="ProceedOverrideButton_Click">Override &amp; proceed</asp:LinkButton>
      </div>
    </asp:Panel>

    <%-- Repeat previous prescription --%>
    <asp:Panel ID="RepeatPanel" runat="server" Visible="false" CssClass="card mt-2">
      <div class="text-sm" style="font-weight:600;"><i class="ti ti-copy" aria-hidden="true"></i> Repeat Previous Prescription</div>
      <p class="text-sm text-secondary mb-2"><asp:Literal ID="RepeatIntro" runat="server" /></p>
      <asp:CheckBoxList ID="RepeatList" runat="server" RepeatLayout="Flow" RepeatDirection="Vertical" CssClass="text-sm" />
      <p class="text-xs text-muted mt-2">Uncheck items you don't want to repeat.</p>
      <div style="display:flex; gap:8px;">
        <asp:LinkButton ID="CancelRepeatButton" runat="server" CssClass="btn btn--sm btn--ghost" CausesValidation="false" OnClick="CancelRepeatButton_Click">Cancel</asp:LinkButton>
        <asp:LinkButton ID="ConfirmRepeatButton" runat="server" CssClass="btn btn--sm btn--success" CausesValidation="false" OnClick="ConfirmRepeatButton_Click">Add selected to today</asp:LinkButton>
      </div>
    </asp:Panel>
  </asp:PlaceHolder>

  <%-- Current prescriptions for this visit --%>
  <div class="mt-3">
    <table class="data-table">
      <thead>
        <tr><th scope="col">Medicine</th><th scope="col">Strength</th><th scope="col">Frequency &amp; Timing</th><th scope="col">Duration</th><th scope="col">Start Date</th><th scope="col">Instructions</th><th scope="col">Actions</th></tr>
      </thead>
      <tbody>
        <asp:Repeater ID="MedsRepeater" runat="server" ItemType="OpdProject.Models.Prescription" OnItemCommand="MedsRepeater_ItemCommand" ClientIDMode="Predictable">
          <ItemTemplate>
            <tr>
              <td><%#: Item.MedicineName %></td>
              <td><%#: Item.Strength %></td>
              <td><%#: Item.TimesPerDay %>x/<%#: Item.FrequencyPeriod ?? "day" %><%#: string.IsNullOrEmpty(Item.TimingLabel) ? "" : " (" + Item.TimingLabel + ")" %></td>
              <td><%#: Item.DurationValue %> <%#: Item.DurationUnit %></td>
              <td><%#: AppPage.FmtDate(Item.PeriodStart ?? Item.PrescribedDate) %></td>
              <td><%#: Item.Instructions %></td>
              <td>
                <asp:LinkButton runat="server" CssClass="btn btn--xs btn--ghost" CommandName="Discontinue" CommandArgument="<%# Item.PrescriptionId %>" CausesValidation="false"
                  Visible="<%# !Host.IsLocked %>" OnClientClick="return confirm('Discontinue this medication?');" ToolTip='<%# "Discontinue " + Item.MedicineName %>'>
                  <i class="ti ti-x" aria-hidden="true"></i>
                </asp:LinkButton>
              </td>
            </tr>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="NoMeds" runat="server"><tr class="empty-row"><td colspan="7">No medicines found.</td></tr></asp:PlaceHolder>
      </tbody>
    </table>
    <asp:PlaceHolder ID="DispensingNote" runat="server"><div class="text-xs text-muted mt-1">*** FOR SINGLE DISPENSING ONLY ***</div></asp:PlaceHolder>
  </div>
</div>
