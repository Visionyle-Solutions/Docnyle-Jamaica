<%@ Page Title="Consultation Setup" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="consultation-config.aspx.cs" Inherits="OpdProject.templates.settings.consultation_config" MaintainScrollPositionOnPostback="true" %>
<%-- Consultation Setup: enable / disable the Consultation 360 sections, their fields and the options of choice
     fields, order the sections (drag, or the arrows), add the hospital's own sections, fields and options, and set
     the units each vital can be recorded in (with unit-system presets).
     Edits are a draft; Publish makes them live for every doctor in the hospital.
     Columns: sections · fields · options / field details · live preview. Route: /settings/consultation --%>
<asp:Content ContentPlaceHolderID="MainContent" runat="server">
<div class="ccfg-page">
  <div class="ccfg-head">
    <div class="ccfg-title">
      <a class="btn btn--sm" href="<%: OpdProject.Web.Urls.To("settings") %>" data-action="go-back" title="Back"><i class="ti ti-arrow-left" aria-hidden="true"></i> Back</a>
      <div>
        <div style="font-size:var(--text-lg); font-weight:600;">Consultation Setup</div>
        <div class="text-xs text-muted">Choose which sections, fields and options the Consultation page shows, add your own, and set the order of the sections. Changes are saved as a draft — doctors see them only after you publish.</div>
        <div class="text-xs text-muted mt-1"><asp:Literal ID="LiveText" runat="server" /></div>
      </div>
    </div>
    <div class="ccfg-actions">
      <asp:Label ID="DraftBadge" runat="server" CssClass="ccfg-badge" />
      <asp:LinkButton ID="RestoreDefaultsButton" runat="server" CssClass="btn btn--sm btn--ghost" OnClick="RestoreDefaultsButton_Click" CausesValidation="false"
        OnClientClick="return confirm('Put the built-in sections, fields and options back to the default form (everything shown, default order)? Your custom sections, fields and options are kept. You can still discard the draft before publishing.');"><i class="ti ti-restore" aria-hidden="true"></i> Restore defaults</asp:LinkButton>
      <asp:LinkButton ID="DiscardButton" runat="server" CssClass="btn btn--sm" OnClick="DiscardButton_Click" CausesValidation="false"><i class="ti ti-arrow-back-up" aria-hidden="true"></i> Discard draft</asp:LinkButton>
      <asp:LinkButton ID="OpenPublishButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="OpenPublishButton_Click" CausesValidation="false"><i class="ti ti-rocket" aria-hidden="true"></i> Publish</asp:LinkButton>
    </div>
  </div>

  <div class="ccfg-body">
    <%-- Sections --%>
    <div class="ccfg-col">
      <div class="ccfg-col-title">Sections · drag to reorder</div>
      <div id="ccfgSections">
        <asp:Repeater ID="SectionsRepeater" runat="server" ItemType="OpdProject.Models.ConsultFormSection" OnItemCommand="SectionsRepeater_ItemCommand">
          <ItemTemplate>
            <div class="ccfg-row<%# Item.Key == SelectedSection ? " ccfg-row--sel" : "" %><%# Item.Enabled ? "" : " ccfg-row--off" %>" draggable="true" data-key="<%#: Item.Key %>">
              <span class="ccfg-drag" title="Drag to reorder" aria-hidden="true">⠿</span>
              <asp:LinkButton runat="server" CssClass='<%# "ccfg-check" + (Item.Enabled ? " ccfg-check--on" : "") %>' CommandName="Toggle" CommandArgument="<%# Item.Key %>" Visible="<%# !Item.Required %>"
                ToolTip='<%# (Item.Enabled ? "Hide " : "Show ") + Item.Label %>' CausesValidation="false"><%# Item.Enabled ? "<i class=\"ti ti-check\" aria-hidden=\"true\"></i>" : "" %></asp:LinkButton>
              <asp:PlaceHolder runat="server" Visible="<%# Item.Required %>"><span class="ccfg-lock" title="Required for sign-off — always shown"><i class="ti ti-lock" aria-hidden="true"></i></span></asp:PlaceHolder>
              <asp:LinkButton runat="server" CssClass="ccfg-main" CommandName="Select" CommandArgument="<%# Item.Key %>" CausesValidation="false" ToolTip='<%# "Configure " + Item.Label %>'>
                <span class="ccfg-name"><i class="ti <%#: Item.Icon %>" aria-hidden="true"></i> <%#: Item.Label %><%# Item.IsCustom ? " <span class=\"ccfg-tag\">Custom</span>" : "" %></span>
                <span class="ccfg-sub"><%#: SectionSub(Item) %></span>
              </asp:LinkButton>
              <span class="ccfg-move">
                <asp:LinkButton runat="server" CommandName="Up" CommandArgument="<%# Item.Key %>" CausesValidation="false" Visible="<%# Container.ItemIndex > 0 %>" ToolTip='<%# "Move " + Item.Label + " up" %>'><i class="ti ti-chevron-up" aria-hidden="true"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CommandName="Down" CommandArgument="<%# Item.Key %>" CausesValidation="false" Visible="<%# Container.ItemIndex < SectionCount - 1 %>" ToolTip='<%# "Move " + Item.Label + " down" %>'><i class="ti ti-chevron-down" aria-hidden="true"></i></asp:LinkButton>
              </span>
            </div>
          </ItemTemplate>
        </asp:Repeater>
      </div>
      <asp:LinkButton ID="AddSectionButton" runat="server" CssClass="ccfg-add" OnClick="AddSectionButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add custom section</asp:LinkButton>
      <asp:HiddenField ID="SectionOrderField" runat="server" />
      <asp:LinkButton ID="ReorderButton" runat="server" OnClick="ReorderButton_Click" CausesValidation="false" style="display:none;">reorder</asp:LinkButton>
    </div>

    <%-- Fields of the selected section --%>
    <div class="ccfg-col">
      <div class="ccfg-col-title">Fields</div>
      <div class="ccfg-col-head ccfg-col-head--actions">
        <span><asp:Literal ID="SectionTitle" runat="server" /></span>
        <asp:PlaceHolder ID="SectionActions" runat="server">
          <span class="ccfg-item-actions">
            <asp:LinkButton ID="EditSectionButton" runat="server" CssClass="btn btn--xs btn--ghost" OnClick="EditSectionButton_Click" CausesValidation="false" ToolTip="Edit this section"><i class="ti ti-pencil" aria-hidden="true"></i> Edit</asp:LinkButton>
            <asp:LinkButton ID="DeleteSectionButton" runat="server" CssClass="btn btn--xs btn--ghost ccfg-danger" OnClick="DeleteSectionButton_Click" CausesValidation="false" ToolTip="Delete this section"
              OnClientClick="return confirm('Delete this custom section and its fields from the draft? Values already recorded in past consultations are kept.');"><i class="ti ti-trash" aria-hidden="true"></i> Delete</asp:LinkButton>
          </span>
        </asp:PlaceHolder>
      </div>
      <div class="ccfg-col-desc"><asp:Literal ID="SectionDescription" runat="server" /></div>
      <asp:PlaceHolder ID="SectionOffNote" runat="server"><div class="ccfg-note"><i class="ti ti-eye-off" aria-hidden="true"></i> This section is hidden — none of its fields show in the consultation.</div></asp:PlaceHolder>
      <asp:PlaceHolder ID="FieldOrderNote" runat="server"><div class="ccfg-col-desc"><i class="ti ti-arrows-sort" aria-hidden="true"></i> <asp:Literal ID="FieldOrderText" runat="server" /></div></asp:PlaceHolder>
      <div id="ccfgFields">
      <asp:Repeater ID="FieldsRepeater" runat="server" ItemType="OpdProject.Models.ConsultFormField" OnItemCommand="FieldsRepeater_ItemCommand">
        <ItemTemplate>
          <div class="ccfg-row<%# Item.Key == SelectedField ? " ccfg-row--sel" : "" %><%# Item.Enabled ? "" : " ccfg-row--off" %>"<%# IsMovable(Item) ? " draggable=\"true\" data-key=\"" + HttpUtility.HtmlAttributeEncode(Item.Key) + "\"" : "" %>>
            <%# IsMovable(Item) ? "<span class=\"ccfg-drag\" title=\"Drag to reorder\" aria-hidden=\"true\">⠿</span>" : "" %>
            <asp:LinkButton runat="server" CssClass='<%# "ccfg-check" + (Item.Enabled ? " ccfg-check--on" : "") %>' CommandName="Toggle" CommandArgument="<%# Item.Key %>" Visible="<%# !Item.Required %>"
              ToolTip='<%# (Item.Enabled ? "Hide " : "Show ") + Item.Label %>' CausesValidation="false"><%# Item.Enabled ? "<i class=\"ti ti-check\" aria-hidden=\"true\"></i>" : "" %></asp:LinkButton>
            <asp:PlaceHolder runat="server" Visible="<%# Item.Required %>"><span class="ccfg-lock" title="Required — always shown"><i class="ti ti-lock" aria-hidden="true"></i></span></asp:PlaceHolder>
            <asp:LinkButton runat="server" CssClass="ccfg-main" CommandName="Select" CommandArgument="<%# Item.Key %>" CausesValidation="false" ToolTip='<%# (Item.IsCustom || Item.AcceptsOptions || Item.Values.Count > 0 ? "Open " : "") + Item.Label %>'>
              <span class="ccfg-name"><%#: Item.Label %><%# Item.IsCustom ? " <span class=\"ccfg-tag\">Custom</span>" : "" %></span>
              <span class="ccfg-sub"><%#: FieldSub(Item) %></span>
            </asp:LinkButton>
            <asp:PlaceHolder runat="server" Visible="<%# IsMovable(Item) %>">
              <span class="ccfg-move">
                <asp:LinkButton runat="server" CommandName="Up" CommandArgument="<%# Item.Key %>" CausesValidation="false" Visible="<%# !IsFirstMovable(Item) %>" ToolTip='<%# "Move " + Item.Label + " up" %>'><i class="ti ti-chevron-up" aria-hidden="true"></i></asp:LinkButton>
                <asp:LinkButton runat="server" CommandName="Down" CommandArgument="<%# Item.Key %>" CausesValidation="false" Visible="<%# !IsLastMovable(Item) %>" ToolTip='<%# "Move " + Item.Label + " down" %>'><i class="ti ti-chevron-down" aria-hidden="true"></i></asp:LinkButton>
              </span>
            </asp:PlaceHolder>
            <%# Item.IsCustom || Item.AcceptsOptions || Item.Values.Count > 0 ? "<span class=\"ccfg-arrow\" aria-hidden=\"true\">›</span>" : "" %>
          </div>
        </ItemTemplate>
      </asp:Repeater>
      </div>
      <asp:HiddenField ID="FieldOrderField" runat="server" />
      <asp:LinkButton ID="FieldReorderButton" runat="server" OnClick="FieldReorderButton_Click" CausesValidation="false" style="display:none;">reorder fields</asp:LinkButton>
      <asp:PlaceHolder ID="NoFieldsNote" runat="server"><div class="ccfg-empty"><asp:Literal ID="NoFieldsText" runat="server" /></div></asp:PlaceHolder>
      <asp:LinkButton ID="AddFieldButton" runat="server" CssClass="ccfg-add" OnClick="AddFieldButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add field</asp:LinkButton>

      <%-- Vitals only: unit system presets and the units of each vital --%>
      <asp:PlaceHolder ID="UnitsHolder" runat="server">
        <div class="ccfg-subtitle ccfg-subtitle--gap">Unit system for this practice</div>
        <div class="ccfg-col-desc">Set the default unit of every vital in one tap, then fine-tune any vital below.</div>
        <div class="ccfg-presets">
          <asp:Repeater ID="PresetsRepeater" runat="server" OnItemCommand="PresetsRepeater_ItemCommand">
            <ItemTemplate>
              <asp:LinkButton runat="server" CssClass="btn btn--xs" CommandName="Preset" CommandArgument='<%# Eval("Item1") %>' CausesValidation="false"><%#: Eval("Item2") %> <span class="text-muted">· <%#: Eval("Item3") %></span></asp:LinkButton>
            </ItemTemplate>
          </asp:Repeater>
        </div>
        <div class="ccfg-subtitle ccfg-subtitle--gap">Vitals captured · units</div>
        <div class="ccfg-col-desc">Tick the vitals the consultation and Nurse Vitals record; select one to set its units. Add your own vitals below.</div>
        <asp:Repeater ID="VitalUnitsRepeater" runat="server" ItemType="OpdProject.Models.VitalUnitSetting" OnItemCommand="VitalUnitsRepeater_ItemCommand">
          <ItemTemplate>
            <div class="ccfg-row<%# Item.VitalCode == SelectedVital ? " ccfg-row--sel" : "" %><%# Item.Enabled ? "" : " ccfg-row--off" %>">
              <asp:LinkButton runat="server" CssClass='<%# "ccfg-check" + (Item.Enabled ? " ccfg-check--on" : "") %>' CommandName="Toggle" CommandArgument="<%# Item.VitalCode %>" CausesValidation="false"
                ToolTip='<%# (Item.Enabled ? "Stop recording " : "Record ") + Item.VitalLabel %>'><%# Item.Enabled ? "<i class=\"ti ti-check\" aria-hidden=\"true\"></i>" : "" %></asp:LinkButton>
              <asp:LinkButton runat="server" CssClass="ccfg-main" CommandName="Select" CommandArgument="<%# Item.VitalCode %>" CausesValidation="false" ToolTip='<%# "Units for " + Item.VitalLabel %>'>
                <span class="ccfg-name"><%#: Item.VitalLabel %><%# Item.IsPending ? " <span class=\"ccfg-tag ccfg-tag--new\">New</span>" : Item.IsCustomVital ? " <span class=\"ccfg-tag\">Custom</span>" : "" %></span>
                <span class="ccfg-sub"><%#: UnitsSub(Item) %></span>
              </asp:LinkButton>
              <asp:LinkButton runat="server" CssClass="ccfg-chip-del" CommandName="Remove" CommandArgument="<%# Item.VitalCode %>" CausesValidation="false" Visible="<%# Item.IsPending %>"
                ToolTip='<%# "Remove " + Item.VitalLabel + " from the draft" %>'><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton>
              <span class="ccfg-arrow" aria-hidden="true">›</span>
            </div>
          </ItemTemplate>
        </asp:Repeater>
        <asp:LinkButton ID="AddVitalButton" runat="server" CssClass="ccfg-add" OnClick="AddVitalButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add custom vital</asp:LinkButton>
      </asp:PlaceHolder>
    </div>

    <%-- Options / details of the selected field --%>
    <div class="ccfg-col">
      <div class="ccfg-col-title">Options &amp; details</div>
      <asp:PlaceHolder ID="FieldHolder" runat="server">
        <div class="ccfg-col-head ccfg-col-head--actions">
          <span><asp:Literal ID="FieldTitle" runat="server" /></span>
          <asp:PlaceHolder ID="FieldActions" runat="server">
            <span class="ccfg-item-actions">
              <asp:LinkButton ID="EditFieldButton" runat="server" CssClass="btn btn--xs btn--ghost" OnClick="EditFieldButton_Click" CausesValidation="false" ToolTip="Edit this field"><i class="ti ti-pencil" aria-hidden="true"></i> Edit</asp:LinkButton>
              <asp:LinkButton ID="DeleteFieldButton" runat="server" CssClass="btn btn--xs btn--ghost ccfg-danger" OnClick="DeleteFieldButton_Click" CausesValidation="false" ToolTip="Delete this field"
                OnClientClick="return confirm('Delete this custom field from the draft? Values already recorded in past consultations are kept.');"><i class="ti ti-trash" aria-hidden="true"></i> Delete</asp:LinkButton>
            </span>
          </asp:PlaceHolder>
        </div>
        <div class="ccfg-col-desc"><asp:Literal ID="FieldDetails" runat="server" /></div>

        <asp:PlaceHolder ID="ValuesHolder" runat="server">
          <div class="ccfg-subtitle">Allowed options</div>
          <div class="ccfg-chips">
            <asp:Repeater ID="ValuesRepeater" runat="server" ItemType="OpdProject.Models.ConsultFormValue" OnItemCommand="ValuesRepeater_ItemCommand">
              <ItemTemplate>
                <span class="ccfg-chipwrap">
                  <asp:LinkButton runat="server" CssClass='<%# "ccfg-chip" + (Item.Enabled ? " ccfg-chip--on" : "") %>' CommandName="Toggle" CommandArgument="<%# Item.Key %>" CausesValidation="false" Visible="<%# !Item.Required %>"
                    ToolTip='<%# (Item.Enabled ? "Remove " : "Offer ") + Item.Label %>'><%# Item.Enabled ? "<i class=\"ti ti-check\" aria-hidden=\"true\"></i> " : "" %><%#: Item.Label %></asp:LinkButton>
                  <asp:PlaceHolder runat="server" Visible="<%# Item.Required %>"><span class="ccfg-chip ccfg-chip--on ccfg-chip--locked" title="Required — always offered"><i class="ti ti-lock" aria-hidden="true"></i> <%#: Item.Label %></span></asp:PlaceHolder>
                  <asp:LinkButton runat="server" CssClass="ccfg-chip-del" CommandName="Delete" CommandArgument="<%# Item.Key %>" CausesValidation="false" Visible="<%# Item.IsCustom %>"
                    ToolTip='<%# "Delete option " + Item.Label %>'><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton>
                </span>
              </ItemTemplate>
            </asp:Repeater>
          </div>
          <asp:PlaceHolder ID="NoOptionsNote" runat="server"><div class="ccfg-note"><i class="ti ti-alert-triangle" aria-hidden="true"></i> No options yet — add at least one before publishing.</div></asp:PlaceHolder>
          <asp:Panel ID="AddOptionPanel" runat="server" CssClass="ccfg-addopt" DefaultButton="AddOptionButton">
            <asp:TextBox ID="NewOptionInput" runat="server" CssClass="form-control" MaxLength="60" placeholder="New option" aria-label="New option" />
            <asp:LinkButton ID="AddOptionButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="AddOptionButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add</asp:LinkButton>
          </asp:Panel>
          <div class="ccfg-hint">Unticked options are removed from the field's list in the consultation. At least one option stays on. Custom options can be deleted (×).</div>
        </asp:PlaceHolder>
      </asp:PlaceHolder>
      <asp:PlaceHolder ID="NoValuesNote" runat="server"><div class="ccfg-empty"><asp:Literal ID="NoValuesText" runat="server" /></div></asp:PlaceHolder>

      <asp:PlaceHolder ID="VitalUnitsDetail" runat="server">
        <div class="ccfg-col-head"><asp:Literal ID="VitalUnitsTitle" runat="server" /></div>
        <div class="ccfg-col-desc"><asp:Literal ID="VitalUnitsDesc" runat="server" /></div>
        <asp:PlaceHolder ID="UnitSwitchOffNote" runat="server"><div class="ccfg-note"><i class="ti ti-info-circle" aria-hidden="true"></i> The unit switch is off — vitals are entered in their default unit only.</div></asp:PlaceHolder>
        <asp:Repeater ID="UnitsRepeater" runat="server" ItemType="OpdProject.Models.VitalUnit" OnItemCommand="UnitsRepeater_ItemCommand">
          <ItemTemplate>
            <div class="ccfg-urow<%# Item.Enabled ? "" : " ccfg-urow--off" %>">
              <asp:LinkButton runat="server" CssClass='<%# "ccfg-check" + (Item.Enabled ? " ccfg-check--on" : "") %>' CommandName="Toggle" CommandArgument="<%# Item.Key %>" CausesValidation="false"
                ToolTip='<%# (Item.Enabled ? "Turn off unit " : "Turn on unit ") + Item.Label %>'><%# Item.Enabled ? "<i class=\"ti ti-check\" aria-hidden=\"true\"></i>" : "" %></asp:LinkButton>
              <span class="ccfg-ulabel"><%#: UnitName(Item) %><%# Item.IsBase ? " <span class=\"ccfg-tag ccfg-tag--muted\" title=\"Values are stored in this unit\">Stored</span>" : "" %><%# Item.IsCustom ? " <span class=\"ccfg-tag\">Custom</span>" : "" %>
                <span class="ccfg-sub"><%#: UnitConversion(Item) %></span></span>
              <asp:PlaceHolder runat="server" Visible="<%# Item.IsDefault %>"><span class="ccfg-chip ccfg-chip--on ccfg-chip--locked ccfg-chip--sm">Default</span></asp:PlaceHolder>
              <asp:LinkButton runat="server" CssClass="btn btn--xs" CommandName="Default" CommandArgument="<%# Item.Key %>" CausesValidation="false" Visible="<%# Item.Enabled && !Item.IsDefault %>"
                ToolTip='<%# "Make " + Item.Label + " the default unit" %>'>Make default</asp:LinkButton>
              <asp:LinkButton runat="server" CssClass="ccfg-chip-del" CommandName="Remove" CommandArgument="<%# Item.Key %>" CausesValidation="false" Visible="<%# Item.IsCustom %>"
                ToolTip='<%# "Remove unit " + Item.Label %>'><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton>
            </div>
          </ItemTemplate>
        </asp:Repeater>
        <asp:PlaceHolder ID="StdUnitsHolder" runat="server">
          <div class="ccfg-subtitle ccfg-subtitle--gap">Common units — tap to add</div>
          <div class="ccfg-presets">
            <asp:Repeater ID="StdUnitsRepeater" runat="server" ItemType="OpdProject.Models.VitalUnit" OnItemCommand="UnitsRepeater_ItemCommand">
              <ItemTemplate><asp:LinkButton runat="server" CssClass="btn btn--xs" CommandName="AddStd" CommandArgument="<%# Item.Key %>" CausesValidation="false" ToolTip='<%# "Add unit " + Item.Label %>'><i class="ti ti-plus" aria-hidden="true"></i> <%#: Item.Label %></asp:LinkButton></ItemTemplate>
            </asp:Repeater>
          </div>
        </asp:PlaceHolder>
        <div class="ccfg-subtitle ccfg-subtitle--gap">Add your own unit</div>
        <asp:Panel ID="AddUnitPanel" runat="server" CssClass="ccfg-addunit" DefaultButton="AddUnitButton">
          <asp:TextBox ID="NewUnitLabel" runat="server" CssClass="form-control" MaxLength="16" placeholder="Unit, e.g. st" aria-label="New unit" />
          <span class="text-xs">1 unit =</span>
          <asp:TextBox ID="NewUnitFactor" runat="server" CssClass="form-control" placeholder="6.35029" aria-label="How many stored units one new unit equals" />
          <span class="text-xs"><asp:Literal ID="BaseUnitText" runat="server" /></span>
          <asp:LinkButton ID="AddUnitButton" runat="server" CssClass="btn btn--sm btn--primary" OnClick="AddUnitButton_Click" CausesValidation="false"><i class="ti ti-plus" aria-hidden="true"></i> Add</asp:LinkButton>
        </asp:Panel>
        <div class="ccfg-hint">One enabled unit is always the default; at least one unit must stay on. Values are stored in the <em>Stored</em> unit, so ranges, BMI and history stay correct whichever unit is used.</div>
      </asp:PlaceHolder>
    </div>

    <%-- Live preview of the draft --%>
    <aside class="ccfg-col ccfg-col--preview" aria-label="Preview">
      <div class="ccfg-col-title"><i class="ti ti-eye" aria-hidden="true"></i> Preview (draft)</div>
      <div class="ccfg-col-desc">How the consultation looks with this draft, in order.</div>
      <asp:Literal ID="PreviewHtml" runat="server" />
    </aside>
  </div>

  <%-- Add / edit a custom section or field --%>
  <asp:Panel ID="EditorModal" runat="server" CssClass="modal-overlay" Visible="false" DefaultButton="SaveEditorButton">
    <div class="modal-frame" role="dialog" aria-modal="true" aria-labelledby="ccfgEditorTitle">
      <div class="modal-header">
        <strong id="ccfgEditorTitle"><asp:Literal ID="EditorTitle" runat="server" /></strong>
        <asp:LinkButton ID="CloseEditorButton" runat="server" CssClass="btn btn--ghost btn--xs" OnClick="CancelEditorButton_Click" CausesValidation="false" ToolTip="Close dialog"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton>
      </div>
      <div class="modal-body">
        <asp:PlaceHolder ID="EditorErrorHolder" runat="server" Visible="false"><div class="c360-alert c360-alert--danger" style="margin-bottom:10px;"><div class="c360-alert-text"><asp:Literal ID="EditorError" runat="server" /></div></div></asp:PlaceHolder>
        <div class="form-group">
          <label class="form-label" for="<%= NameInput.ClientID %>">Name <span class="required">*</span></label>
          <asp:TextBox ID="NameInput" runat="server" CssClass="form-control" MaxLength="60" />
          <asp:RequiredFieldValidator runat="server" ControlToValidate="NameInput" ValidationGroup="Editor" Text="Enter a name" CssClass="field-error" Display="Dynamic" />
        </div>
        <asp:PlaceHolder ID="DescriptionHolder" runat="server">
        <div class="form-group">
          <label class="form-label" for="<%= DescriptionInput.ClientID %>"><asp:Literal ID="DescriptionLabel" runat="server" /></label>
          <asp:TextBox ID="DescriptionInput" runat="server" CssClass="form-control" MaxLength="160" />
        </div>
        </asp:PlaceHolder>
        <asp:PlaceHolder ID="VitalInputs" runat="server">
          <div class="form-group">
            <label class="form-label" for="<%= VitalUnitInput.ClientID %>">Unit</label>
            <asp:TextBox ID="VitalUnitInput" runat="server" CssClass="form-control" MaxLength="20" placeholder="e.g. mg/dL" style="max-width:200px;" />
            <div class="text-xs text-muted mt-1">Values are stored in this unit. You can add other units (with a conversion) after saving.</div>
          </div>
          <div class="form-grid">
            <div class="form-group">
              <label class="form-label" for="<%= VitalMinInput.ClientID %>">Minimum valid value</label>
              <asp:TextBox ID="VitalMinInput" runat="server" CssClass="form-control" placeholder="e.g. 20" />
            </div>
            <div class="form-group">
              <label class="form-label" for="<%= VitalMaxInput.ClientID %>">Maximum valid value</label>
              <asp:TextBox ID="VitalMaxInput" runat="server" CssClass="form-control" placeholder="e.g. 600" />
            </div>
          </div>
          <p class="text-xs text-muted" style="margin:0;">Values outside the range are rejected; leave it empty to accept any number. The vital is created when you publish.</p>
        </asp:PlaceHolder>
        <asp:PlaceHolder ID="SectionInputs" runat="server">
          <div class="form-group">
            <label class="form-label" for="<%= IconInput.ClientID %>">Icon</label>
            <asp:DropDownList ID="IconInput" runat="server" CssClass="form-select" style="max-width:240px;" />
          </div>
          <p class="text-xs text-muted" style="margin:0;">After saving, add fields to the section in the Fields column.</p>
        </asp:PlaceHolder>
        <asp:PlaceHolder ID="FieldInputs" runat="server">
          <div class="form-group">
            <label class="form-label" for="<%= TypeInput.ClientID %>">Kind of field</label>
            <asp:DropDownList ID="TypeInput" runat="server" CssClass="form-select" style="max-width:280px;" />
          </div>
          <div class="form-group"><asp:CheckBox ID="RequiredInput" runat="server" Text=" Required before sign-off" /></div>
          <asp:PlaceHolder ID="OptionsInputs" runat="server">
            <div class="form-group" id="ccfgOptionsRow">
              <label class="form-label" for="<%= OptionsInput.ClientID %>">Options <span class="text-muted">(one per line)</span></label>
              <asp:TextBox ID="OptionsInput" runat="server" CssClass="form-control" TextMode="MultiLine" Rows="4" />
              <div class="text-xs text-muted mt-1">For single / multiple choice. You can add, switch off or delete options later.</div>
            </div>
          </asp:PlaceHolder>
        </asp:PlaceHolder>
      </div>
      <div class="modal-footer">
        <asp:LinkButton ID="CancelEditorButton" runat="server" CssClass="btn" OnClick="CancelEditorButton_Click" CausesValidation="false">Cancel</asp:LinkButton>
        <asp:LinkButton ID="SaveEditorButton" runat="server" CssClass="btn btn--primary" ValidationGroup="Editor" OnClick="SaveEditorButton_Click">Save to draft</asp:LinkButton>
      </div>
    </div>
  </asp:Panel>

  <%-- Publish dialog --%>
  <asp:Panel ID="PublishModal" runat="server" CssClass="modal-overlay" Visible="false">
    <div class="modal-frame" role="dialog" aria-modal="true" aria-label="Publish consultation setup">
      <div class="modal-header">
        <strong><i class="ti ti-rocket" aria-hidden="true"></i> Publish consultation setup</strong>
        <asp:LinkButton ID="ClosePublishButton" runat="server" CssClass="btn btn--ghost btn--xs" OnClick="ClosePublishButton_Click" CausesValidation="false" ToolTip="Close dialog"><i class="ti ti-x" aria-hidden="true"></i></asp:LinkButton>
      </div>
      <div class="modal-body">
        <asp:PlaceHolder ID="ProblemsHolder" runat="server">
          <div class="c360-alert c360-alert--danger" style="margin-bottom:10px;">
            <div><strong>Fix these before publishing:</strong>
              <ul class="ccfg-diff" style="margin-bottom:0;"><asp:Repeater ID="ProblemsRepeater" runat="server" ItemType="System.String"><ItemTemplate><li><%#: Item %></li></ItemTemplate></asp:Repeater></ul>
            </div>
          </div>
        </asp:PlaceHolder>
        <p class="text-sm"><asp:Literal ID="PublishIntro" runat="server" /></p>
        <ul class="ccfg-diff">
          <asp:Repeater ID="ChangesRepeater" runat="server" ItemType="System.String"><ItemTemplate><li><%#: Item %></li></ItemTemplate></asp:Repeater>
        </ul>
        <p class="text-xs text-muted">Consultations already signed are not changed. Open consultations pick up the new setup on their next page load.</p>
      </div>
      <div class="modal-footer">
        <asp:LinkButton ID="CancelPublishButton" runat="server" CssClass="btn" OnClick="ClosePublishButton_Click" CausesValidation="false">Cancel</asp:LinkButton>
        <asp:LinkButton ID="ConfirmPublishButton" runat="server" CssClass="btn btn--primary" OnClick="ConfirmPublishButton_Click" CausesValidation="false">Publish &amp; make live</asp:LinkButton>
      </div>
    </div>
  </asp:Panel>
</div>
</asp:Content>
<asp:Content ContentPlaceHolderID="ScriptContent" runat="server">
  <script>
    /* Drag a section (or a movable field) row onto another to reorder; the new order is posted back
       (the arrows do the same without a mouse). Rows without data-key are fixed and can't be dragged. */
    function ccfgDragList(listId, fieldId, postback) {
      var list = document.getElementById(listId);
      var field = document.getElementById(fieldId);
      if (!list || !field) return;
      var dragged = null;
      function rows() { return Array.prototype.slice.call(list.querySelectorAll('.ccfg-row[data-key]')); }
      function clearOver() { rows().forEach(function (r) { r.classList.remove('ccfg-row--over'); }); }
      list.addEventListener('dragstart', function (e) {
        var row = e.target.closest('.ccfg-row[data-key]');
        if (!row) return;
        dragged = row;
        row.classList.add('ccfg-row--dragging');
        e.dataTransfer.effectAllowed = 'move';
        try { e.dataTransfer.setData('text/plain', row.getAttribute('data-key')); } catch (x) { }
      });
      list.addEventListener('dragover', function (e) {
        var row = e.target.closest('.ccfg-row[data-key]');
        if (!row || !dragged) return;
        e.preventDefault();
        clearOver();
        if (row !== dragged) row.classList.add('ccfg-row--over');
      });
      list.addEventListener('drop', function (e) {
        var row = e.target.closest('.ccfg-row[data-key]');
        if (!row || !dragged || row === dragged) return;
        e.preventDefault();
        var rect = row.getBoundingClientRect();
        list.insertBefore(dragged, (e.clientY - rect.top) > rect.height / 2 ? row.nextSibling : row);
        field.value = rows().map(function (r) { return r.getAttribute('data-key'); }).join(',');
        postback();
      });
      list.addEventListener('dragend', function () {
        clearOver();
        if (dragged) dragged.classList.remove('ccfg-row--dragging');
        dragged = null;
      });
    }
    ccfgDragList('ccfgSections', '<%= SectionOrderField.ClientID %>', function () { <%= ClientScript.GetPostBackEventReference(ReorderButton, "") %>; });
    ccfgDragList('ccfgFields', '<%= FieldOrderField.ClientID %>', function () { <%= ClientScript.GetPostBackEventReference(FieldReorderButton, "") %>; });

    /* The columns scroll on their own (the page itself doesn't), so keep each column's scroll position across postbacks. */
    (function () {
      var key = 'ccfgColScroll', cols = document.querySelectorAll('.ccfg-col');
      try {
        var saved = <%= IsPostBack ? "true" : "false" %> ? JSON.parse(sessionStorage.getItem(key) || '[]') : [];
        cols.forEach(function (c, i) { if (saved[i]) c.scrollTop = saved[i]; });
        sessionStorage.removeItem(key);
      } catch (x) { }
      window.addEventListener('beforeunload', function () {
        try { sessionStorage.setItem(key, JSON.stringify(Array.prototype.map.call(cols, function (c) { return c.scrollTop; }))); } catch (x) { }
      });
    })();

    /* Field editor: the options box only matters for single / multiple choice */
    (function () {
      var type = document.getElementById('<%= TypeInput.ClientID %>'), row = document.getElementById('ccfgOptionsRow');
      if (!type || !row) return;
      function sync() { row.style.display = (type.value === 'SELECT' || type.value === 'MULTI') ? '' : 'none'; }
      type.addEventListener('change', sync);
      sync();
    })();
  </script>
</asp:Content>
