using System;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>
    /// Consultation Setup. Edits the hospital's draft consultation configuration — sections on/off and order, fields
    /// on/off, options on/off, and the hospital's own (custom) sections, fields and options. Publish makes the draft
    /// the live configuration read by Consultation 360.
    /// </summary>
    public partial class consultation_config : BasePage
    {
        protected override string RequiredPermission { get { return "admin_consultation"; } }
        public override string PageTitle { get { return "Consultation Setup"; } }

        private ConsultFormConfig _draft;

        protected string SelectedSection
        {
            get { return ViewState["Section"] as string; }
            set { ViewState["Section"] = value; }
        }

        protected string SelectedField
        {
            get { return ViewState["Field"] as string; }
            set { ViewState["Field"] = value; }
        }

        /// <summary>Vital whose units are open in the details column (Vitals section only).</summary>
        protected string SelectedVital
        {
            get { return ViewState["Vital"] as string; }
            set { ViewState["Vital"] = value; }
        }

        private VitalUnitSetting VitalSetting { get { return Draft.VitalUnits.FirstOrDefault(v => v.VitalCode == SelectedVital); } }

        /// <summary>What the editor dialog is doing: addSection, editSection, addField or editField.</summary>
        private string EditorMode
        {
            get { return ViewState["EditorMode"] as string; }
            set { ViewState["EditorMode"] = value; }
        }

        protected int SectionCount { get { return _draft != null ? _draft.Sections.Count : 0; } }

        private ConsultFormConfig Draft { get { return _draft ?? (_draft = ConsultationConfigService.GetDraft(HospitalId)); } }

        private ConsultFormSection Section { get { return Draft.Sections.FirstOrDefault(s => s.Key == SelectedSection); } }

        private ConsultFormField Field
        {
            get
            {
                var s = Section;
                return s != null ? s.Fields.FirstOrDefault(f => f.Key == SelectedField) : null;
            }
        }

        /// <summary>Consultation Setup runs full-screen: no app sidebar or top bar.</summary>
        protected void Page_Init(object sender, EventArgs e)
        {
            ((SiteMaster)Master).HideChrome = true;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            IconInput.Items.AddRange(ConsultationConfigService.SectionIcons.Select(i => new ListItem(i[1], i[0])).ToArray());
            TypeInput.Items.AddRange(ConsultationConfigService.InputTypes.Select(t => new ListItem(t[1], t[0])).ToArray());
        }

        /* ── Sections ── */

        protected void SectionsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var key = (string)e.CommandArgument;
            switch (e.CommandName)
            {
                case "Select":
                    if (SelectedSection != key) SelectedField = SelectedVital = null;
                    SelectedSection = key;
                    break;
                case "Toggle":
                    Refused(ConsultationConfigService.ToggleSection(HospitalId, key));
                    if (SelectedSection != key) SelectedVital = null;
                    if (SelectedSection != key) SelectedField = null;
                    SelectedSection = key;
                    break;
                case "Up": ConsultationConfigService.MoveSection(HospitalId, key, -1); break;
                case "Down": ConsultationConfigService.MoveSection(HospitalId, key, 1); break;
            }
        }

        protected void ReorderButton_Click(object sender, EventArgs e)
        {
            var keys = (SectionOrderField.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToList();
            if (keys.Count > 0) ConsultationConfigService.ReorderSections(HospitalId, keys);
            SectionOrderField.Value = "";
        }

        /* ── Fields & options ── */

        protected void FieldReorderButton_Click(object sender, EventArgs e)
        {
            var keys = (FieldOrderField.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToList();
            if (keys.Count > 0 && SelectedSection != null) ConsultationConfigService.ReorderFields(HospitalId, SelectedSection, keys);
            FieldOrderField.Value = "";
        }

        protected void FieldsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var key = (string)e.CommandArgument;
            switch (e.CommandName)
            {
                case "Toggle": Refused(ConsultationConfigService.ToggleField(HospitalId, SelectedSection, key)); break;
                case "Up": ConsultationConfigService.MoveField(HospitalId, SelectedSection, key, -1); break;
                case "Down": ConsultationConfigService.MoveField(HospitalId, SelectedSection, key, 1); break;
            }
            SelectedField = key;
            SelectedVital = null;
        }

        /* ── Vital units ── */

        protected void PresetsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            bool changed;
            var key = (string)e.CommandArgument;
            if (Refused(ConsultationConfigService.ApplyUnitPreset(HospitalId, key, out changed))) return;
            var preset = VitalUnitService.Presets.First(p => p.Item1 == key);
            if (changed) ToastSuccess(preset.Item2 + " defaults applied (" + preset.Item3 + ")", "Publish to make them live.");
            else ToastInfo(preset.Item2 + " units were already set");
        }

        protected void VitalUnitsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var code = (string)e.CommandArgument;
            if (e.CommandName == "Remove")
            {
                if (!Refused(ConsultationConfigService.RemoveCustomVital(HospitalId, code)) && SelectedVital == code) SelectedVital = null;
                return;
            }
            if (e.CommandName == "Toggle") Refused(ConsultationConfigService.ToggleVital(HospitalId, code));
            else if (e.CommandName != "Select") return;
            SelectedVital = code;
            SelectedField = null;
        }

        protected void UnitsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var key = (string)e.CommandArgument;
            switch (e.CommandName)
            {
                case "Toggle": Refused(ConsultationConfigService.ToggleUnit(HospitalId, SelectedVital, key)); break;
                case "Default": Refused(ConsultationConfigService.MakeDefaultUnit(HospitalId, SelectedVital, key)); break;
                case "Remove": Refused(ConsultationConfigService.RemoveUnit(HospitalId, SelectedVital, key)); break;
                case "AddStd": Refused(ConsultationConfigService.AddStandardUnit(HospitalId, SelectedVital, key)); break;
            }
        }

        protected void AddVitalButton_Click(object sender, EventArgs e)
        {
            OpenEditor("addVital", "", "", null, null, false);
        }

        /// <summary>A number box: empty → null; "invalid" when it holds something that is not a number.</summary>
        private static double? ParseOptional(string text, out bool invalid)
        {
            text = (text ?? "").Trim();
            invalid = false;
            if (text.Length == 0) return null;
            double d;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            invalid = true;
            return null;
        }

        protected void AddUnitButton_Click(object sender, EventArgs e)
        {
            double factor;
            var ok = double.TryParse((NewUnitFactor.Text ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out factor);
            if (Refused(ConsultationConfigService.AddCustomUnit(HospitalId, SelectedVital, NewUnitLabel.Text, ok ? factor : 0))) return;
            NewUnitLabel.Text = NewUnitFactor.Text = "";
        }

        /// <summary>"°C default · °F" — the default unit first, then the other units switched on ("Not recorded" when off).</summary>
        protected string UnitsSub(VitalUnitSetting s)
        {
            if (!s.Enabled) return "Not recorded";
            var def = s.Units.FirstOrDefault(u => u.IsDefault && u.Enabled);
            var others = s.Units.Where(u => u.Enabled && u != def).Select(UnitName);
            return (def != null ? UnitName(def) + " default" : "") + (others.Any() ? " · " + string.Join(", ", others) : "");
        }

        private static string RangeText(VitalUnitSetting v)
        {
            Func<double, string> n = d => d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return v.MinValue.HasValue && v.MaxValue.HasValue ? n(v.MinValue.Value) + "–" + n(v.MaxValue.Value)
                : v.MinValue.HasValue ? "≥ " + n(v.MinValue.Value) : "≤ " + n(v.MaxValue.Value);
        }

        protected static string UnitName(VitalUnit u) { return string.IsNullOrEmpty(u.Label) ? "(no unit)" : u.Label; }

        /// <summary>How the unit converts to the stored unit, e.g. "1 lb = 0.4536 kg".</summary>
        protected string UnitConversion(VitalUnit u)
        {
            if (u.IsBase) return "Values are stored in this unit";
            var s = VitalSetting;
            var baseUnit = s != null ? s.Units.FirstOrDefault(x => x.IsBase) : null;
            var baseLabel = baseUnit != null ? UnitName(baseUnit) : "";
            if (u.IsFeetInches) return "Entered as 5'7\" · converted to " + baseLabel;
            if (u.Offset != 0) return "Converted to " + baseLabel;
            return "1 " + u.Label + " = " + u.Factor.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " " + baseLabel;
        }

        protected void ValuesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var key = (string)e.CommandArgument;
            if (e.CommandName == "Toggle") Refused(ConsultationConfigService.ToggleValue(HospitalId, SelectedSection, SelectedField, key));
            else if (e.CommandName == "Delete") Refused(ConsultationConfigService.DeleteOption(HospitalId, SelectedSection, SelectedField, key));
        }

        protected void AddOptionButton_Click(object sender, EventArgs e)
        {
            var label = NewOptionInput.Text;
            if (Refused(ConsultationConfigService.AddOption(HospitalId, SelectedSection, SelectedField, label))) return;
            NewOptionInput.Text = "";
        }

        /// <summary>Shows why an edit was refused; true when it was.</summary>
        private bool Refused(string reason)
        {
            if (reason == null) return false;
            ToastWarning("Not changed", reason);
            return true;
        }

        /* ── Custom section / field editor ── */

        protected void AddSectionButton_Click(object sender, EventArgs e)
        {
            OpenEditor("addSection", "", "", ConsultationConfigService.SectionIcons[0][0], null, false);
        }

        protected void EditSectionButton_Click(object sender, EventArgs e)
        {
            var s = Section;
            if (s == null || !s.IsCustom) return;
            OpenEditor("editSection", s.Label, s.Description, s.Icon, null, false);
        }

        protected void DeleteSectionButton_Click(object sender, EventArgs e)
        {
            var s = Section;
            if (s == null || Refused(ConsultationConfigService.DeleteSection(HospitalId, s.Key))) return;
            SelectedSection = SelectedField = null;
            ToastInfo("Section deleted from the draft", s.Label);
        }

        protected void AddFieldButton_Click(object sender, EventArgs e)
        {
            if (Section == null || !ConsultationConfigService.AllowsCustomFields(Section.Key)) return;
            OpenEditor("addField", "", "", null, "TEXT", false);
        }

        protected void EditFieldButton_Click(object sender, EventArgs e)
        {
            var f = Field;
            if (f == null || !f.IsCustom) return;
            OpenEditor("editField", f.Label, f.Description, null, f.InputType, f.RequiredAtSignOff);
        }

        protected void DeleteFieldButton_Click(object sender, EventArgs e)
        {
            var f = Field;
            if (f == null || Refused(ConsultationConfigService.DeleteField(HospitalId, SelectedSection, f.Key))) return;
            SelectedField = null;
            ToastInfo("Field deleted from the draft", f.Label);
        }

        private void OpenEditor(string mode, string label, string description, string icon, string inputType, bool required)
        {
            EditorMode = mode;
            var isVital = mode == "addVital";
            var isSection = mode.EndsWith("Section");
            var sectionLabel = Section != null ? Section.Label : "";
            EditorTitle.Text = mode == "addSection" ? "Add a custom section"
                : mode == "editSection" ? "Edit section"
                : mode == "addField" ? "Add a field to " + HttpUtility.HtmlEncode(sectionLabel)
                : isVital ? "Add a custom vital"
                : "Edit field";
            DescriptionLabel.Text = isSection ? "Description (shown on the card)" : "Help text (shown under the field)";
            NameInput.Text = label;
            DescriptionInput.Text = description;
            SectionInputs.Visible = isSection;
            FieldInputs.Visible = !isSection && !isVital;
            DescriptionHolder.Visible = !isVital;
            VitalInputs.Visible = isVital;
            VitalUnitInput.Text = VitalMinInput.Text = VitalMaxInput.Text = "";
            OptionsInputs.Visible = mode == "addField";
            if (isSection && IconInput.Items.FindByValue(icon ?? "") != null) IconInput.SelectedValue = icon;
            if (!isSection && TypeInput.Items.FindByValue(inputType ?? "") != null) TypeInput.SelectedValue = inputType;
            RequiredInput.Checked = required;
            OptionsInput.Text = "";
            EditorErrorHolder.Visible = false;
            EditorModal.Visible = true;
        }

        protected void SaveEditorButton_Click(object sender, EventArgs e)
        {
            Validate("Editor");
            if (!IsValid) return;
            string error, key;
            var label = NameInput.Text;
            var description = DescriptionInput.Text;
            switch (EditorMode)
            {
                case "addSection":
                    error = ConsultationConfigService.AddSection(HospitalId, label, description, IconInput.SelectedValue, out key);
                    if (error == null) { SelectedSection = key; SelectedField = null; }
                    break;
                case "editSection":
                    error = ConsultationConfigService.UpdateSection(HospitalId, SelectedSection, label, description, IconInput.SelectedValue);
                    break;
                case "addField":
                    var options = (OptionsInput.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    error = ConsultationConfigService.AddField(HospitalId, SelectedSection, label, description, TypeInput.SelectedValue, RequiredInput.Checked, options, out key);
                    if (error == null) SelectedField = key;
                    break;
                case "editField":
                    error = ConsultationConfigService.UpdateField(HospitalId, SelectedSection, SelectedField, label, description, TypeInput.SelectedValue, RequiredInput.Checked);
                    break;
                case "addVital":
                    bool badMin, badMax;
                    var min = ParseOptional(VitalMinInput.Text, out badMin);
                    var max = ParseOptional(VitalMaxInput.Text, out badMax);
                    if (badMin || badMax) { error = "Enter the valid range as numbers, or leave it empty."; break; }
                    error = ConsultationConfigService.AddCustomVital(HospitalId, label, VitalUnitInput.Text, min, max, out key);
                    if (error == null) { SelectedVital = key; SelectedField = null; }
                    break;
                default:
                    error = "Nothing to save.";
                    break;
            }
            if (error != null)
            {
                EditorError.Text = HttpUtility.HtmlEncode(error);
                EditorErrorHolder.Visible = true;
                return;
            }
            EditorModal.Visible = false;
            EditorMode = null;
            ToastSuccess("Saved to the draft", "Publish to make it live.");
        }

        protected void CancelEditorButton_Click(object sender, EventArgs e)
        {
            EditorModal.Visible = false;
            EditorMode = null;
        }

        /* ── Draft / publish ── */

        protected void DiscardButton_Click(object sender, EventArgs e)
        {
            ConsultationConfigService.DiscardDraft(HospitalId);
            ToastInfo("Draft discarded", "Back to the live setup.");
        }

        protected void RestoreDefaultsButton_Click(object sender, EventArgs e)
        {
            ConsultationConfigService.RestoreDefaults(HospitalId);
            ToastInfo("Built-in parts reset to the default form", "Custom sections, fields and options were kept. Publish to make it live.");
        }

        protected void OpenPublishButton_Click(object sender, EventArgs e)
        {
            var changes = ConsultationConfigService.ListDraftChanges(HospitalId);
            if (changes.Count == 0)
            {
                ToastInfo("Nothing to publish", "The draft matches the live setup.");
                return;
            }
            var problems = ConsultationConfigService.ListPublishProblems(HospitalId);
            ProblemsHolder.Visible = problems.Count > 0;
            ProblemsRepeater.DataSource = problems;
            ProblemsRepeater.DataBind();
            ConfirmPublishButton.Enabled = problems.Count == 0;
            var live = ConsultationConfigService.GetPublished(HospitalId);
            PublishIntro.Text = "You are about to apply <strong>" + changes.Count + " change" + (changes.Count == 1 ? "" : "s")
                + "</strong>. The consultation setup moves from version " + live.Version + " to <strong>version " + (live.Version + 1) + "</strong>.";
            ChangesRepeater.DataSource = changes;
            ChangesRepeater.DataBind();
            PublishModal.Visible = true;
        }

        protected void ClosePublishButton_Click(object sender, EventArgs e) { PublishModal.Visible = false; }

        protected void ConfirmPublishButton_Click(object sender, EventArgs e)
        {
            var changes = ConsultationConfigService.ListDraftChanges(HospitalId);
            var version = ConsultationConfigService.Publish(HospitalId, UserId);
            PublishModal.Visible = false;
            if (version < 0)
            {
                ToastWarning("Not published", string.Join(" ", ConsultationConfigService.ListPublishProblems(HospitalId)));
                return;
            }
            if (version == 0)
            {
                ToastInfo("Nothing to publish", "The draft matches the live setup.");
                return;
            }
            Audit("PUBLISH", "ConsultationConfig", "v" + version, string.Join("; ", changes));
            ToastSuccess("Consultation setup published", "Version " + version + " is now live for every doctor.");
        }

        /* ── Rendering ── */

        protected string SectionSub(ConsultFormSection s)
        {
            if (s.Required) return "Required · always shown";
            if (!s.Enabled) return "Hidden";
            if (s.Fields.Count == 0) return s.IsCustom ? "No fields yet" : "Shown";
            return s.Fields.Count(f => f.Enabled) + " of " + s.Fields.Count + " fields shown";
        }

        protected string FieldSub(ConsultFormField f)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (f.IsCustom) parts.Add(ConsultationConfigService.InputTypeLabel(f.InputType) + (f.RequiredAtSignOff ? " · required at sign-off" : ""));
            else if (!string.IsNullOrEmpty(f.Description)) parts.Add(f.Description);
            if (f.AcceptsOptions || f.Values.Count > 0) parts.Add(f.Values.Count(v => v.Enabled) + " of " + f.Values.Count + " options");
            return string.Join(" · ", parts);
        }

        protected bool IsMovable(ConsultFormField f) { return ConsultationConfigService.IsMovableField(Section, f); }

        protected bool IsFirstMovable(ConsultFormField f)
        {
            var first = Section.Fields.FirstOrDefault(IsMovable);
            return first != null && first.Key == f.Key;
        }

        protected bool IsLastMovable(ConsultFormField f)
        {
            var last = Section.Fields.LastOrDefault(IsMovable);
            return last != null && last.Key == f.Key;
        }

        /// <summary>One line under the field list explaining what can be reordered in this section.</summary>
        private string FieldOrderHint(ConsultFormSection s)
        {
            int movable = s.Fields.Count(IsMovable);
            if (movable < 2) return null;
            if (s.Key == "context") return "Drag or use the arrows to set the order of these rows in the consultation.";
            if (s.IsCustom) return "Drag or use the arrows to set the order of the fields on this card.";
            return "Custom fields can be dragged into any order; they appear after the built-in fields on this card, which stay in place.";
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            _draft = ConsultationConfigService.GetDraft(HospitalId);
            if (Section == null) { SelectedSection = _draft.Sections[0].Key; SelectedField = null; }
            if (SelectedField != null && Field == null) SelectedField = null;

            SectionsRepeater.DataSource = _draft.Sections;
            SectionsRepeater.DataBind();

            /* Fields column */
            var section = Section;
            SectionTitle.Text = "<i class=\"ti " + HttpUtility.HtmlAttributeEncode(section.Icon) + "\" aria-hidden=\"true\"></i> " + HttpUtility.HtmlEncode(section.Label)
                + (section.IsCustom ? " <span class=\"ccfg-tag\">Custom</span>" : "");
            SectionActions.Visible = section.IsCustom;
            SectionDescription.Text = HttpUtility.HtmlEncode(section.Description);
            SectionOffNote.Visible = !section.Enabled;
            FieldsRepeater.DataSource = section.Fields;
            FieldsRepeater.DataBind();
            var hint = FieldOrderHint(section);
            FieldOrderNote.Visible = hint != null;
            FieldOrderText.Text = HttpUtility.HtmlEncode(hint ?? "");
            var canAddFields = ConsultationConfigService.AllowsCustomFields(section.Key);
            NoFieldsNote.Visible = section.Fields.Count == 0;
            NoFieldsText.Text = section.IsCustom ? "No fields yet — add the first one below." : "This section has no configurable fields — it can only be shown or hidden.";
            AddFieldButton.Visible = canAddFields;
            AddFieldButton.Text = "<i class=\"ti ti-plus\" aria-hidden=\"true\"></i> Add " + (section.IsCustom ? "field" : "custom field to " + HttpUtility.HtmlEncode(section.Label));

            /* Vitals: unit system presets + units per vital */
            UnitsHolder.Visible = section.Key == "vitals";
            if (section.Key != "vitals") SelectedVital = null;
            if (UnitsHolder.Visible)
            {
                PresetsRepeater.DataSource = VitalUnitService.Presets;
                PresetsRepeater.DataBind();
                VitalUnitsRepeater.DataSource = _draft.VitalUnits;
                VitalUnitsRepeater.DataBind();
            }
            var vital = VitalSetting;
            VitalUnitsDetail.Visible = vital != null;
            if (vital != null)
            {
                SelectedField = null;
                var baseUnit = vital.Units.FirstOrDefault(u => u.IsBase) ?? vital.Units[0];
                VitalUnitsTitle.Text = "Units for " + HttpUtility.HtmlEncode(vital.VitalLabel);
                VitalUnitsDesc.Text = (vital.IsPending ? "Added in this draft — it is created when you publish"
                        + (vital.MinValue.HasValue || vital.MaxValue.HasValue ? " (valid range " + HttpUtility.HtmlEncode(RangeText(vital)) + ")" : "") + ". " : "")
                    + (vital.Enabled ? "Choose which units this vital can be recorded in, and its default."
                        : "This vital is not recorded — tick it in the Vitals list to capture it. Its units are kept for when it is.");
                var unitSwitch = section.Fields.FirstOrDefault(f => f.Key == "unitSwitch");
                UnitSwitchOffNote.Visible = unitSwitch != null && !unitSwitch.Enabled;
                UnitsRepeater.DataSource = vital.Units;
                UnitsRepeater.DataBind();
                var std = VitalUnitService.AvailableStandardUnits(vital);
                StdUnitsHolder.Visible = std.Count > 0;
                StdUnitsRepeater.DataSource = std;
                StdUnitsRepeater.DataBind();
                BaseUnitText.Text = HttpUtility.HtmlEncode(UnitName(baseUnit));
            }

            /* Options & details column */
            var field = Field;
            var hasDetails = field != null && (field.IsCustom || field.AcceptsOptions || field.Values.Count > 0);
            FieldHolder.Visible = hasDetails;
            NoValuesNote.Visible = !hasDetails && vital == null;
            NoValuesText.Text = field == null ? "Select a field (marked ›) to see its options and details" + (section.Key == "vitals" ? ", or a vital to set its units." : ".")
                : HttpUtility.HtmlEncode(field.Label) + " has no option list — it is simply shown or hidden.";
            if (hasDetails)
            {
                FieldTitle.Text = HttpUtility.HtmlEncode(field.Label) + (field.IsCustom ? " <span class=\"ccfg-tag\">Custom</span>" : "");
                FieldActions.Visible = field.IsCustom;
                FieldDetails.Text = field.IsCustom
                    ? HttpUtility.HtmlEncode(ConsultationConfigService.InputTypeLabel(field.InputType)) + (field.RequiredAtSignOff ? " · <strong>required before sign-off</strong>" : "")
                      + (string.IsNullOrEmpty(field.Description) ? "" : "<br />" + HttpUtility.HtmlEncode(field.Description))
                    : HttpUtility.HtmlEncode(field.Description);
                ValuesHolder.Visible = field.AcceptsOptions || field.Values.Count > 0;
                ValuesRepeater.DataSource = field.Values;
                ValuesRepeater.DataBind();
                NoOptionsNote.Visible = field.AcceptsOptions && field.IsCustom && field.Values.Count(v => v.Enabled) == 0;
                AddOptionPanel.Visible = field.AcceptsOptions;
            }

            /* Draft state */
            var changes = ConsultationConfigService.ListDraftChanges(HospitalId);
            DraftBadge.Text = changes.Count == 0 ? "All changes published" : changes.Count + " unpublished change" + (changes.Count == 1 ? "" : "s");
            DraftBadge.CssClass = "ccfg-badge" + (changes.Count == 0 ? " ccfg-badge--clean" : "");
            DiscardButton.Enabled = OpenPublishButton.Enabled = changes.Count > 0;
            var live = ConsultationConfigService.GetPublished(HospitalId);
            LiveText.Text = live.PublishedAt.HasValue
                ? "Live: version " + live.Version + " · published " + HttpUtility.HtmlEncode(FmtDateTime(live.PublishedAt.Value)) + " by " + HttpUtility.HtmlEncode(UserService.UserName(live.PublishedBy))
                : "Live: version " + live.Version + " · default setup";
            PreviewHtml.Text = RenderPreview(_draft);
        }

        /// <summary>Stepper chips and one mini card per shown section, listing its shown fields and options.</summary>
        private static string RenderPreview(ConsultFormConfig draft)
        {
            var shown = draft.Sections.Where(s => s.Enabled).ToList();
            var sb = new StringBuilder("<div class=\"ccfg-prev-steps\">");
            foreach (var s in shown) sb.Append("<span class=\"ccfg-prev-step\">").Append(HttpUtility.HtmlEncode(s.Label)).Append("</span>");
            sb.Append("</div>");
            foreach (var s in shown)
            {
                sb.Append("<div class=\"ccfg-prev-card\"><div class=\"ccfg-prev-title\"><i class=\"ti ").Append(HttpUtility.HtmlAttributeEncode(s.Icon))
                    .Append("\" aria-hidden=\"true\"></i> ").Append(HttpUtility.HtmlEncode(s.Label))
                    .Append(s.Required ? " <span class=\"badge badge--warning\" style=\"font-size:9px;\">Required</span>" : "")
                    .Append(s.IsCustom ? " <span class=\"ccfg-tag\">Custom</span>" : "").Append("</div>");
                var fields = s.Fields.Where(f => f.Enabled).ToList();
                foreach (var f in fields)
                {
                    sb.Append("<div class=\"ccfg-prev-field\">").Append(HttpUtility.HtmlEncode(f.Label));
                    if (f.IsCustom)
                        sb.Append(f.RequiredAtSignOff ? " <span class=\"required\">*</span>" : "")
                          .Append(" <span class=\"text-muted\">· ").Append(HttpUtility.HtmlEncode(ConsultationConfigService.InputTypeLabel(f.InputType))).Append("</span>");
                    foreach (var v in f.Values.Where(v => v.Enabled)) sb.Append(" <span class=\"ccfg-prev-opt\">").Append(HttpUtility.HtmlEncode(v.Label)).Append("</span>");
                    sb.Append("</div>");
                }
                if (s.Key == "vitals" && draft.VitalUnits != null && draft.VitalUnits.Any(vu => vu.Enabled))
                    sb.Append("<div class=\"ccfg-prev-field\">Vitals: ").Append(string.Join(" ", draft.VitalUnits.Where(vu => vu.Enabled).Select(vu =>
                    {
                        var d = vu.Units.FirstOrDefault(u => u.IsDefault && u.Enabled) ?? vu.Units[0];
                        var many = vu.Units.Count(u => u.Enabled) > 1;
                        return "<span class=\"ccfg-prev-opt\" title=\"" + HttpUtility.HtmlAttributeEncode(string.Join(", ", vu.Units.Where(u => u.Enabled).Select(u => u.Label))) + "\">"
                            + HttpUtility.HtmlEncode(vu.VitalLabel + " " + d.Label) + (many ? " ▾" : "") + "</span>";
                    }))).Append("</div>");
                if (fields.Count == 0) sb.Append("<div class=\"ccfg-prev-field text-muted\">").Append(s.IsCustom ? "No fields yet" : s.Fields.Count > 0 ? "Core fields only" : "").Append("</div>");
                sb.Append("</div>");
            }
            if (shown.Count == 0) sb.Append("<div class=\"ccfg-empty\">No sections shown.</div>");
            return sb.ToString();
        }
    }
}
