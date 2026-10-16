using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// Consultation form configuration: enable / disable sections, fields and option values, order the sections,
    /// and add the hospital's own (custom) sections, fields and options.
    /// Edits go to a per-hospital working draft; <see cref="Publish"/> applies the draft and bumps the version.
    /// The Consultation 360 page reads only the published copy (<see cref="GetView"/>), so a half-finished draft
    /// never reaches a doctor. Structure (keys, labels, what is required) comes from <see cref="Catalog"/>.
    /// </summary>
    public static class ConsultationConfigService
    {
        /* ── Catalogue: every configurable part of the consultation page ── */

        private static ConsultFormValue V(string key, string label, bool required = false)
        {
            return new ConsultFormValue { Key = key, Label = label, Required = required, Enabled = true };
        }

        private static ConsultFormField F(string key, string label, string description, params ConsultFormValue[] values)
        {
            return new ConsultFormField { Key = key, Label = label, Description = description, Enabled = true, Values = values.ToList() };
        }

        private static ConsultFormField Req(string key, string label, string description)
        {
            var f = F(key, label, description);
            f.Required = true;
            return f;
        }

        /// <summary>A built-in choice field the hospital may extend with its own options.</summary>
        private static ConsultFormField Open(ConsultFormField f)
        {
            f.AllowCustomOptions = true;
            return f;
        }

        private static ConsultFormSection S(string key, string label, string icon, bool required, string description, params ConsultFormField[] fields)
        {
            return new ConsultFormSection { Key = key, Label = label, Icon = icon, Required = required, Enabled = true, Description = description, Fields = fields.ToList() };
        }

        /// <summary>The default form, in the default order (everything enabled).</summary>
        private static List<ConsultFormSection> Catalog()
        {
            return new List<ConsultFormSection>
            {
                S("context", "Patient Context & History", "ti-user-heart", false, "Storyboard of the patient's history above the visit cards.",
                    F("visitHistory", "Visit History", "Previous visits with this hospital"),
                    F("medicalHistory", "Medical History", "Chronic illness checklist"),
                    F("recentLabs", "Recent Labs", "Latest lab results"),
                    F("currentMeds", "Current Medications", "Active prescriptions"),
                    F("medicalEvents", "Medical Events", "Surgeries, MI, stroke …"),
                    F("intakeConsent", "Intake & Consent", "Pre-visit answers and consent signature"),
                    F("familySocial", "Family & Social", "Family history and social habits")),
                S("vitals", "Vitals", "ti-heart-rate-monitor", true, "Required for sign-off. Choose which vitals are recorded, and their units, below; new vitals are added in Settings → Clinical.",
                    F("previous", "Previous visit column", "Values from the last visit"),
                    F("range", "Normal range column", "Reference range per vital"),
                    F("unitSwitch", "Unit switch on each vital", "Record in any enabled unit; off = default unit only"),
                    F("calculated", "Calculated values", "BMI · BSA · MAP")),
                S("complaints", "Complaints", "ti-list-check", true, "Required for sign-off. Tri-state complaint chips.",
                    F("more", "More symptoms list", "Symptoms that are not quick picks"),
                    F("custom", "Add a custom complaint", "Free-text complaint not in the list")),
                S("diagnosis", "Diagnosis", "ti-stethoscope", true, "Required for sign-off. ICD-10 search and confirm.",
                    F("notes", "Clinical notes", "Doctor's diagnostic reasoning"),
                    Open(F("laterality", "Laterality", "Side affected", V("LEFT", "Left"), V("RIGHT", "Right"), V("BILATERAL", "Bilateral"))),
                    Open(F("acuteChronic", "Acute / Chronic", "Course of the condition", V("ACUTE", "Acute"), V("CHRONIC", "Chronic")))),
                S("soap", "Observations", "ti-notes", false, "SOAP notes, phrase chips and sketches.",
                    F("subjective", "Subjective", "Patient's own words"),
                    F("objective", "Objective", "Examination findings"),
                    F("plan", "Plan", "Treatment plan"),
                    F("personal", "“Your own words” fields", "Doctor's personal observation under each note — checked at sign-off"),
                    F("phrases", "Quick phrase chips", "Tap to insert a phrase from the library"),
                    F("voice", "Voice dictation", "Dictate into the notes"),
                    F("autoGenerate", "Auto-generate SOAP", "Draft the note from the visit data"),
                    F("savePhrase", "Save a phrase", "Add to the doctor's phrase library"),
                    F("sketches", "Sketches & drawing pad", "Handwritten notes and diagrams")),
                S("labs", "Labs/Imaging", "ti-microscope", false, "Investigations ordered for an outside lab.",
                    F("favorites", "Frequent tests", "Doctor's quick-pick favourites"),
                    F("suggested", "Suggested for this visit", "From the branched-flow engine"),
                    F("search", "Catalog search", "Search all tests"),
                    F("custom", "Custom test", "Add a test that is not in the catalog", V("LAB", "Lab"), V("IMAGING", "Imaging")),
                    F("skip", "“Not needed” option", "Mark the section as not needed for the visit")),
                S("cpoe", "Prescriptions", "ti-prescription", false, "CPOE with drug-allergy and reconciliation checks (always on).",
                    F("repeat", "Repeat previous", "Copy the last visit's prescriptions"),
                    F("manual", "Manual entry", "Prescribe a drug outside the formulary"),
                    F("category", "Medicine category", "Category on the prescription",
                        V("PRESCRIPTION", "Prescription Medicines"), V("OTC", "OTC Medicines"), V("CONTROLLED", "Controlled Substances", true), V("AYURVEDIC", "Ayurvedic / Herbal")),
                    F("targetArea", "Target area", "Body site for topical drugs"),
                    F("doseTiming", "Dose timing", "BF · AN · NI · EM"),
                    F("eventTiming", "Timing relative to an event", "e.g. 10–20 minutes before meals"),
                    F("prn", "PRN (as needed)", "Take as needed, not on a schedule"),
                    F("instructions", "Instructions", "Special usage instructions")),
                S("services", "Services & Procedures", "ti-first-aid-kit", false, "Procedures done in the visit (dressing, injection …), picked from the Service Charge Master and added to the invoice at sign-off.",
                    F("notes", "Notes", "e.g. site or detail, shown on the invoice line"),
                    F("skip", "“Not needed” option", "Mark the section as not needed for the visit")),
                S("referral", "Referral", "ti-transfer-out", false, "Refer the patient to a specialist.",
                    Req("specialty", "Refer to specialty", "Required for every referral"),
                    Req("reason", "Reason", "Required for every referral"),
                    F("doctorName", "Doctor name", "Specific doctor, if known"),
                    Open(F("urgency", "Urgency", "How soon", V("ROUTINE", "Routine"), V("URGENT", "Urgent"))),
                    F("skip", "“Not needed” option", "Mark the section as not needed for the visit")),
                S("followup", "Next Visit", "ti-calendar-plus", false, "Follow-Up Date and booking.",
                    Req("date", "Follow-Up Date", "Required to save a follow-up"),
                    F("instructions", "Instructions", "e.g. review with lab reports"),
                    F("autoBook", "Auto-book appointment", "Book the first free slot on that date"),
                    F("skip", "“Not needed” option", "Mark the section as not needed for the visit")),
                S("notes", "Confidential Notes", "ti-lock", false, "Private to the doctor — never printed or exported."),
            };
        }

        /// <summary>Sections the consultation page can render (catalogue order).</summary>
        public static List<string> SectionKeys { get { return Catalog().Select(s => s.Key).ToList(); } }


        /* ── Custom parts: input types, icons, where custom fields may go ── */

        /// <summary>Input types of a custom field (code, label), in menu order.</summary>
        public static readonly string[][] InputTypes =
        {
            new[] { "TEXT", "Short text" }, new[] { "TEXTAREA", "Long text" }, new[] { "NUMBER", "Number" }, new[] { "DATE", "Date" },
            new[] { "YESNO", "Yes / No" }, new[] { "SELECT", "Single choice (dropdown)" }, new[] { "MULTI", "Multiple choice (checkboxes)" },
        };

        public static string InputTypeLabel(string code)
        {
            var t = InputTypes.FirstOrDefault(x => x[0] == code);
            return t != null ? t[1] : code;
        }

        /// <summary>Icons offered for a custom section (Tabler class, label).</summary>
        public static readonly string[][] SectionIcons =
        {
            new[] { "ti-clipboard-list", "Clipboard" }, new[] { "ti-checklist", "Checklist" }, new[] { "ti-forms", "Form" },
            new[] { "ti-file-text", "Document" }, new[] { "ti-stethoscope", "Stethoscope" }, new[] { "ti-activity", "Activity" },
            new[] { "ti-heart", "Heart" }, new[] { "ti-lungs", "Lungs" }, new[] { "ti-brain", "Brain" }, new[] { "ti-eye", "Eye" },
            new[] { "ti-ear", "Ear" }, new[] { "ti-dental", "Tooth" }, new[] { "ti-bone", "Bone" }, new[] { "ti-droplet", "Drop" },
            new[] { "ti-baby-carriage", "Child" },
        };

        /// <summary>Custom fields are not offered on the history panel or the doctor-private notes.</summary>
        public static bool AllowsCustomFields(string sectionKey) { return sectionKey != "context" && sectionKey != "notes"; }

        /* ── Store ── */

        private static ConsultFormConfig Clone(ConsultFormConfig c)
        {
            return new ConsultFormConfig
            {
                HospitalId = c.HospitalId, Version = c.Version, PublishedAt = c.PublishedAt, PublishedBy = c.PublishedBy,
                Sections = c.Sections.Select(CloneSection).ToList(),
                VitalUnits = (c.VitalUnits ?? new List<VitalUnitSetting>()).Select(VitalUnitService.CloneSetting).ToList(),
            };
        }

        private static ConsultFormSection CloneSection(ConsultFormSection s)
        {
            return new ConsultFormSection
            {
                Key = s.Key, Label = s.Label, Icon = s.Icon, Description = s.Description, Required = s.Required, Enabled = s.Enabled, IsCustom = s.IsCustom,
                Fields = s.Fields.Select(CloneField).ToList(),
            };
        }

        private static ConsultFormField CloneField(ConsultFormField f)
        {
            return new ConsultFormField
            {
                Key = f.Key, Label = f.Label, Description = f.Description, Required = f.Required, Enabled = f.Enabled, IsCustom = f.IsCustom,
                InputType = f.InputType, RequiredAtSignOff = f.RequiredAtSignOff, AllowCustomOptions = f.AllowCustomOptions,
                Values = f.Values.Select(v => new ConsultFormValue { Key = v.Key, Label = v.Label, Required = v.Required, Enabled = v.Enabled, IsCustom = v.IsCustom }).ToList(),
            };
        }

        private static ConsultFormConfig Defaults(string hospitalId)
        {
            return new ConsultFormConfig { HospitalId = hospitalId, Version = 1, Sections = Catalog(), VitalUnits = new List<VitalUnitSetting>() };
        }

        /// <summary>
        /// Default setup of every hospital vital. <paramref name="factoryOnOff"/>: built-in vitals on/off as in the
        /// default form (custom vitals on); otherwise as the hospital's vitals list has them now.
        /// </summary>
        /* Callers hold DataStore.Sync */
        private static List<VitalUnitSetting> DefaultVitalUnits(string hospitalId, bool factoryOnOff = false)
        {
            var hospital = DataStore.Hospitals.FirstOrDefault(h => h.HospitalId == hospitalId);
            var tempUnit = hospital != null ? hospital.DefaultTempUnit : "F";
            return DataStore.VitalsConfig.Where(v => v.HospitalId == hospitalId).OrderBy(v => v.SortOrder)
                .Select(v => VitalUnitService.DefaultSetting(v, tempUnit, factoryOnOff ? v.IsCustom || VitalUnitService.OnByDefault(v.VitalCode) : v.IsEnabled)).ToList();
        }

        /// <summary>
        /// Gives every hospital vital (including ones added later in Settings → Clinical, which start as the hospital's
        /// vitals list has them) a setup entry, in vital order.
        /// </summary>
        private static ConsultFormConfig WithVitalUnits(ConsultFormConfig c)
        {
            if (c.VitalUnits == null) c.VitalUnits = new List<VitalUnitSetting>();
            var defaults = DefaultVitalUnits(c.HospitalId);
            foreach (var d in defaults)
            {
                var existing = c.VitalUnits.FirstOrDefault(x => x.VitalCode == d.VitalCode);
                if (existing == null) c.VitalUnits.Add(d);
                else { existing.VitalLabel = d.VitalLabel; existing.IsCustomVital = d.IsCustomVital; }
            }
            var order = defaults.Select(d => d.VitalCode).ToList();
            c.VitalUnits = c.VitalUnits.OrderBy(x => order.IndexOf(x.VitalCode) < 0 ? int.MaxValue : order.IndexOf(x.VitalCode)).ToList();
            return c;
        }

        private static ConsultFormConfig PublishedRaw(string hospitalId)
        {
            ConsultFormConfig c;
            if (!DataStore.ConsultFormPublished.TryGetValue(hospitalId, out c))
                DataStore.ConsultFormPublished[hospitalId] = c = Defaults(hospitalId);
            return WithVitalUnits(c);
        }

        private static ConsultFormConfig DraftRaw(string hospitalId)
        {
            ConsultFormConfig c;
            if (!DataStore.ConsultFormDrafts.TryGetValue(hospitalId, out c))
                DataStore.ConsultFormDrafts[hospitalId] = c = Clone(PublishedRaw(hospitalId));
            return WithVitalUnits(c);
        }

        /// <summary>The live configuration (a copy).</summary>
        public static ConsultFormConfig GetPublished(string hospitalId)
        {
            lock (DataStore.Sync) return Clone(PublishedRaw(hospitalId));
        }

        /// <summary>The working draft being edited on the configuration page (a copy).</summary>
        public static ConsultFormConfig GetDraft(string hospitalId)
        {
            lock (DataStore.Sync) return Clone(DraftRaw(hospitalId));
        }

        /// <summary>Read-only lookup over the published configuration, for the consultation page.</summary>
        public static ConsultFormView GetView(string hospitalId)
        {
            return new ConsultFormView(GetPublished(hospitalId));
        }

        /* ── Draft edits: show / hide, order (each returns null on success, or the reason it was refused) ── */

        /* Callers hold DataStore.Sync */
        private static ConsultFormSection DraftSection(string hospitalId, string sectionKey)
        {
            return DraftRaw(hospitalId).Sections.FirstOrDefault(x => x.Key == sectionKey);
        }

        private static ConsultFormField DraftField(string hospitalId, string sectionKey, string fieldKey)
        {
            var s = DraftSection(hospitalId, sectionKey);
            return s != null ? s.Fields.FirstOrDefault(x => x.Key == fieldKey) : null;
        }

        public static string ToggleSection(string hospitalId, string sectionKey)
        {
            lock (DataStore.Sync)
            {
                var s = DraftSection(hospitalId, sectionKey);
                if (s == null) return "Unknown section.";
                if (s.Required) return s.Label + " is required for sign-off and cannot be disabled.";
                s.Enabled = !s.Enabled;
                return null;
            }
        }

        public static string ToggleField(string hospitalId, string sectionKey, string fieldKey)
        {
            lock (DataStore.Sync)
            {
                var f = DraftField(hospitalId, sectionKey, fieldKey);
                if (f == null) return "Unknown field.";
                if (f.Required) return f.Label + " is required and cannot be disabled.";
                f.Enabled = !f.Enabled;
                return null;
            }
        }

        public static string ToggleValue(string hospitalId, string sectionKey, string fieldKey, string valueKey)
        {
            lock (DataStore.Sync)
            {
                var f = DraftField(hospitalId, sectionKey, fieldKey);
                var v = f != null ? f.Values.FirstOrDefault(x => x.Key == valueKey) : null;
                if (v == null) return "Unknown option.";
                if (v.Required) return "“" + v.Label + "” is required and cannot be disabled.";
                if (v.Enabled && f.Values.Count(x => x.Enabled) == 1) return "Keep at least one option — disable the " + f.Label + " field instead.";
                v.Enabled = !v.Enabled;
                return null;
            }
        }

        /// <summary>Moves a section one place up (delta −1) or down (+1).</summary>
        public static void MoveSection(string hospitalId, string sectionKey, int delta)
        {
            lock (DataStore.Sync) Move(DraftRaw(hospitalId).Sections, s => s.Key == sectionKey, delta);
        }

        /// <summary>
        /// Fields whose order the consultation follows: custom fields (shown in order at the foot of their card) and every
        /// row of Patient Context (a list). Other built-in fields are tools, columns or buttons at fixed places in their card.
        /// </summary>
        public static bool IsMovableField(ConsultFormSection section, ConsultFormField field)
        {
            return field.IsCustom || section.Key == "context";
        }

        /// <summary>Moves a movable field one place up or down among the section's movable fields.</summary>
        public static void MoveField(string hospitalId, string sectionKey, string fieldKey, int delta)
        {
            lock (DataStore.Sync)
            {
                var s = DraftSection(hospitalId, sectionKey);
                if (s == null) return;
                var movable = s.Fields.Where(f => IsMovableField(s, f)).ToList();
                var from = movable.FindIndex(f => f.Key == fieldKey);
                var to = from + delta;
                if (from < 0 || to < 0 || to >= movable.Count) return;
                /* swap the two fields' positions in the full list (fixed built-in fields keep theirs) */
                int a = s.Fields.IndexOf(movable[from]), b = s.Fields.IndexOf(movable[to]);
                s.Fields[a] = movable[to];
                s.Fields[b] = movable[from];
            }
        }

        /// <summary>
        /// Applies a full order of a section's movable fields (from drag and drop). The movable fields are laid back into
        /// the positions they occupied, so fixed built-in fields don't move. Unknown keys are ignored; missing ones keep
        /// their relative order at the end.
        /// </summary>
        public static void ReorderFields(string hospitalId, string sectionKey, IList<string> orderedKeys)
        {
            lock (DataStore.Sync)
            {
                var s = DraftSection(hospitalId, sectionKey);
                if (s == null) return;
                var keys = (orderedKeys ?? new List<string>()).ToList();
                var slots = s.Fields.Select((f, i) => new { f, i }).Where(x => IsMovableField(s, x.f)).Select(x => x.i).ToList();
                var ordered = slots.Select(i => s.Fields[i])
                    .Select((f, i) => new { f, i, rank = keys.IndexOf(f.Key) })
                    .OrderBy(x => x.rank < 0 ? int.MaxValue : x.rank).ThenBy(x => x.i)
                    .Select(x => x.f).ToList();
                for (int i = 0; i < slots.Count; i++) s.Fields[slots[i]] = ordered[i];
            }
        }

        private static void Move<T>(List<T> list, Func<T, bool> match, int delta)
        {
            var from = list.FindIndex(x => match(x));
            var to = from + delta;
            if (from < 0 || to < 0 || to >= list.Count) return;
            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
        }

        /// <summary>Applies a full section order (from drag and drop). Unknown keys are ignored; missing ones keep their relative order at the end.</summary>
        public static void ReorderSections(string hospitalId, IList<string> orderedKeys)
        {
            lock (DataStore.Sync)
            {
                var draft = DraftRaw(hospitalId);
                var keys = (orderedKeys ?? new List<string>()).ToList();
                draft.Sections = draft.Sections
                    .Select((s, i) => new { s, i, rank = keys.IndexOf(s.Key) })
                    .OrderBy(x => x.rank < 0 ? int.MaxValue : x.rank).ThenBy(x => x.i)
                    .Select(x => x.s).ToList();
            }
        }

        /* ── Draft edits: custom sections, fields and options ── */

        private static string CheckLabel(ref string label, IEnumerable<string> siblings, string what)
        {
            label = (label ?? "").Trim();
            if (label.Length < 2 || label.Length > 60) return "Enter a " + what + " name of 2–60 characters.";
            var l = label;
            if (siblings.Any(x => string.Equals(x, l, StringComparison.OrdinalIgnoreCase))) return "There is already a " + what + " called “" + label + "”.";
            return null;
        }

        private static string Clip(string text, int max)
        {
            text = (text ?? "").Trim();
            return text.Length > max ? text.Substring(0, max) : text;
        }

        /// <summary>Readable, stable option key from its label (stored on the consultation), unique within the field.</summary>
        private static string OptionKey(ConsultFormField f, string label)
        {
            var sb = new StringBuilder();
            foreach (var ch in label.ToUpperInvariant()) sb.Append(ch < 128 && char.IsLetterOrDigit(ch) ? ch : '_');
            var stem = Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
            if (stem.Length > 30) stem = stem.Substring(0, 30).TrimEnd('_');
            if (stem.Length == 0) stem = "OPT";
            var key = stem;
            for (int n = 2; f.Values.Any(v => v.Key == key); n++) key = stem + "_" + n;
            return key;
        }

        private static bool ValidIcon(string icon) { return SectionIcons.Any(i => i[0] == icon); }

        private static bool ValidType(string type) { return InputTypes.Any(t => t[0] == type); }

        public static string AddSection(string hospitalId, string label, string description, string icon, out string sectionKey)
        {
            sectionKey = null;
            lock (DataStore.Sync)
            {
                var draft = DraftRaw(hospitalId);
                var error = CheckLabel(ref label, draft.Sections.Select(s => s.Label), "section");
                if (error != null) return error;
                sectionKey = DataStore.NextId("cs");
                draft.Sections.Add(new ConsultFormSection
                {
                    Key = sectionKey, Label = label, Description = Clip(description, 160), Icon = ValidIcon(icon) ? icon : SectionIcons[0][0],
                    Enabled = true, IsCustom = true, Fields = new List<ConsultFormField>(),
                });
                return null;
            }
        }

        public static string UpdateSection(string hospitalId, string sectionKey, string label, string description, string icon)
        {
            lock (DataStore.Sync)
            {
                var draft = DraftRaw(hospitalId);
                var s = draft.Sections.FirstOrDefault(x => x.Key == sectionKey);
                if (s == null || !s.IsCustom) return "Only custom sections can be edited.";
                var error = CheckLabel(ref label, draft.Sections.Where(x => x != s).Select(x => x.Label), "section");
                if (error != null) return error;
                s.Label = label;
                s.Description = Clip(description, 160);
                if (ValidIcon(icon)) s.Icon = icon;
                return null;
            }
        }

        public static string DeleteSection(string hospitalId, string sectionKey)
        {
            lock (DataStore.Sync)
            {
                var draft = DraftRaw(hospitalId);
                var s = draft.Sections.FirstOrDefault(x => x.Key == sectionKey);
                if (s == null || !s.IsCustom) return "Only custom sections can be deleted.";
                draft.Sections.Remove(s);
                return null;
            }
        }

        public static string AddField(string hospitalId, string sectionKey, string label, string description, string inputType,
            bool requiredAtSignOff, IEnumerable<string> options, out string fieldKey)
        {
            fieldKey = null;
            lock (DataStore.Sync)
            {
                var s = DraftSection(hospitalId, sectionKey);
                if (s == null) return "Unknown section.";
                if (!AllowsCustomFields(s.Key)) return "Custom fields cannot be added to " + s.Label + ".";
                if (!ValidType(inputType)) return "Choose the kind of field.";
                var error = CheckLabel(ref label, s.Fields.Select(f => f.Label), "field");
                if (error != null) return error;
                var field = new ConsultFormField
                {
                    Key = DataStore.NextId("cf"), Label = label, Description = Clip(description, 160), Enabled = true, IsCustom = true,
                    InputType = inputType, RequiredAtSignOff = requiredAtSignOff, Values = new List<ConsultFormValue>(),
                };
                if (field.AcceptsOptions)
                    foreach (var o in options ?? new string[0])
                    {
                        var opt = Clip(o, 60);
                        if (opt.Length == 0 || field.Values.Any(v => string.Equals(v.Label, opt, StringComparison.OrdinalIgnoreCase))) continue;
                        field.Values.Add(new ConsultFormValue { Key = OptionKey(field, opt), Label = opt, Enabled = true, IsCustom = true });
                    }
                s.Fields.Add(field);
                fieldKey = field.Key;
                return null;
            }
        }

        public static string UpdateField(string hospitalId, string sectionKey, string fieldKey, string label, string description, string inputType, bool requiredAtSignOff)
        {
            lock (DataStore.Sync)
            {
                var s = DraftSection(hospitalId, sectionKey);
                var f = s != null ? s.Fields.FirstOrDefault(x => x.Key == fieldKey) : null;
                if (f == null || !f.IsCustom) return "Only custom fields can be edited.";
                if (!ValidType(inputType)) return "Choose the kind of field.";
                var error = CheckLabel(ref label, s.Fields.Where(x => x != f).Select(x => x.Label), "field");
                if (error != null) return error;
                f.Label = label;
                f.Description = Clip(description, 160);
                f.InputType = inputType;
                f.RequiredAtSignOff = requiredAtSignOff;
                return null;
            }
        }

        public static string DeleteField(string hospitalId, string sectionKey, string fieldKey)
        {
            lock (DataStore.Sync)
            {
                var s = DraftSection(hospitalId, sectionKey);
                var f = s != null ? s.Fields.FirstOrDefault(x => x.Key == fieldKey) : null;
                if (f == null || !f.IsCustom) return "Only custom fields can be deleted.";
                s.Fields.Remove(f);
                return null;
            }
        }

        public static string AddOption(string hospitalId, string sectionKey, string fieldKey, string label)
        {
            lock (DataStore.Sync)
            {
                var f = DraftField(hospitalId, sectionKey, fieldKey);
                if (f == null || !f.AcceptsOptions) return "Options cannot be added to this field.";
                var error = CheckLabel(ref label, f.Values.Select(v => v.Label), "option");
                if (error != null) return error;
                f.Values.Add(new ConsultFormValue { Key = OptionKey(f, label), Label = label, Enabled = true, IsCustom = true });
                return null;
            }
        }

        public static string DeleteOption(string hospitalId, string sectionKey, string fieldKey, string valueKey)
        {
            lock (DataStore.Sync)
            {
                var f = DraftField(hospitalId, sectionKey, fieldKey);
                var v = f != null ? f.Values.FirstOrDefault(x => x.Key == valueKey) : null;
                if (v == null || !v.IsCustom) return "Only custom options can be deleted.";
                f.Values.Remove(v);
                return null;
            }
        }

        /* ── Draft edits: vital units ── */

        /* Callers hold DataStore.Sync */
        private static VitalUnitSetting DraftUnits(string hospitalId, string vitalCode)
        {
            return DraftRaw(hospitalId).VitalUnits.FirstOrDefault(x => x.VitalCode == vitalCode);
        }

        /// <summary>Keeps exactly one enabled default (the first enabled unit when the default was switched off).</summary>
        private static void FixDefault(VitalUnitSetting s)
        {
            var current = s.Units.FirstOrDefault(u => u.IsDefault && u.Enabled) ?? s.Units.FirstOrDefault(u => u.Enabled);
            foreach (var u in s.Units) u.IsDefault = u == current;
        }

        /// <summary>
        /// Adds a hospital-defined vital to the draft (recorded, after the existing vitals). It is created in the hospital's
        /// vitals list when the draft is published. <paramref name="unit"/> becomes its stored unit; values outside
        /// <paramref name="min"/>–<paramref name="max"/> (either optional) are rejected.
        /// </summary>
        public static string AddCustomVital(string hospitalId, string label, string unit, double? min, double? max, out string vitalCode)
        {
            vitalCode = null;
            lock (DataStore.Sync)
            {
                var draft = DraftRaw(hospitalId);
                label = (label ?? "").Trim();
                unit = (unit ?? "").Trim();
                if (label.Length < 2 || label.Length > 40) return "Enter a vital name of 2–40 characters.";
                var l = label;
                if (draft.VitalUnits.Any(v => string.Equals(v.VitalLabel, l, StringComparison.OrdinalIgnoreCase))
                    || DataStore.VitalsConfig.Any(v => v.HospitalId == hospitalId && string.Equals(v.DisplayLabel, l, StringComparison.OrdinalIgnoreCase)))
                    return "There is already a vital called “" + label + "”.";
                if (unit.Length > 20) return "Keep the unit to 20 characters.";
                if (min.HasValue && max.HasValue && max.Value <= min.Value) return "The maximum must be greater than the minimum.";
                vitalCode = HospitalService.NewCustomVitalCode(hospitalId, label, draft.VitalUnits.Select(v => v.VitalCode));
                var hospital = DataStore.Hospitals.FirstOrDefault(h => h.HospitalId == hospitalId);
                var setting = VitalUnitService.DefaultSetting(new VitalConfig { VitalCode = vitalCode, DisplayLabel = label, AllowedUnits = unit, IsCustom = true },
                    hospital != null ? hospital.DefaultTempUnit : "F", true);
                setting.IsPending = true;
                setting.MinValue = min;
                setting.MaxValue = max;
                draft.VitalUnits.Add(setting);
                return null;
            }
        }

        /// <summary>Removes a custom vital that was added in this draft (published vitals can only be switched off).</summary>
        public static string RemoveCustomVital(string hospitalId, string vitalCode)
        {
            lock (DataStore.Sync)
            {
                var draft = DraftRaw(hospitalId);
                var s = draft.VitalUnits.FirstOrDefault(x => x.VitalCode == vitalCode);
                if (s == null || !s.IsPending) return "Only vitals added in this draft can be removed — switch a published vital off instead.";
                if (s.Enabled && draft.VitalUnits.Count(x => x.Enabled) == 1) return "Keep at least one vital — vitals are required for sign-off.";
                draft.VitalUnits.Remove(s);
                return null;
            }
        }

        /// <summary>Switches whether a vital is captured. At least one vital stays on (vitals are required for sign-off).</summary>
        public static string ToggleVital(string hospitalId, string vitalCode)
        {
            lock (DataStore.Sync)
            {
                var all = DraftRaw(hospitalId).VitalUnits;
                var s = all.FirstOrDefault(x => x.VitalCode == vitalCode);
                if (s == null) return "Unknown vital.";
                if (s.Enabled && all.Count(x => x.Enabled) == 1) return "Keep at least one vital — vitals are required for sign-off.";
                s.Enabled = !s.Enabled;
                return null;
            }
        }

        public static string ToggleUnit(string hospitalId, string vitalCode, string unitKey)
        {
            lock (DataStore.Sync)
            {
                var s = DraftUnits(hospitalId, vitalCode);
                var u = s != null ? s.Units.FirstOrDefault(x => x.Key == unitKey) : null;
                if (u == null) return "Unknown unit.";
                if (u.Enabled && s.Units.Count(x => x.Enabled) == 1) return "At least one unit must stay on.";
                u.Enabled = !u.Enabled;
                FixDefault(s);
                return null;
            }
        }

        public static string MakeDefaultUnit(string hospitalId, string vitalCode, string unitKey)
        {
            lock (DataStore.Sync)
            {
                var s = DraftUnits(hospitalId, vitalCode);
                var u = s != null ? s.Units.FirstOrDefault(x => x.Key == unitKey) : null;
                if (u == null) return "Unknown unit.";
                u.Enabled = true;
                foreach (var x in s.Units) x.IsDefault = x == u;
                return null;
            }
        }

        public static string AddStandardUnit(string hospitalId, string vitalCode, string unitKey)
        {
            lock (DataStore.Sync)
            {
                var s = DraftUnits(hospitalId, vitalCode);
                var u = s != null ? VitalUnitService.AvailableStandardUnits(s).FirstOrDefault(x => x.Key == unitKey) : null;
                if (u == null) return "That unit is not available for this vital.";
                u.Enabled = true;
                s.Units.Add(u);
                FixDefault(s);
                return null;
            }
        }

        /// <summary>Adds the hospital's own unit: 1 [label] = <paramref name="factor"/> × the vital's base unit.</summary>
        public static string AddCustomUnit(string hospitalId, string vitalCode, string label, double factor)
        {
            lock (DataStore.Sync)
            {
                var s = DraftUnits(hospitalId, vitalCode);
                if (s == null) return "Unknown vital.";
                label = (label ?? "").Trim();
                if (label.Length < 1 || label.Length > 16) return "Enter a unit of 1–16 characters.";
                if (s.Units.Any(u => string.Equals(u.Label, label, StringComparison.OrdinalIgnoreCase))) return "Unit “" + label + "” already exists.";
                if (VitalUnitService.AvailableStandardUnits(s).Any(u => string.Equals(u.Label, label, StringComparison.OrdinalIgnoreCase)))
                    return "“" + label + "” is a standard unit — add it from Common units instead.";
                if (!(factor > 0) || double.IsInfinity(factor)) return "Enter how many base units one " + label + " equals (a positive number).";
                s.Units.Add(new VitalUnit { Key = DataStore.NextId("U"), Label = label, Factor = factor, Decimals = 2, Enabled = true, IsCustom = true });
                FixDefault(s);
                return null;
            }
        }

        /// <summary>Removes a custom unit (standard units can only be switched off).</summary>
        public static string RemoveUnit(string hospitalId, string vitalCode, string unitKey)
        {
            lock (DataStore.Sync)
            {
                var s = DraftUnits(hospitalId, vitalCode);
                var u = s != null ? s.Units.FirstOrDefault(x => x.Key == unitKey) : null;
                if (u == null || !u.IsCustom) return "Only custom units can be removed — switch a standard unit off instead.";
                if (u.Enabled && s.Units.Count(x => x.Enabled) == 1) return "At least one unit must stay on.";
                s.Units.Remove(u);
                FixDefault(s);
                return null;
            }
        }

        /// <summary>Sets the default unit of every vital the preset covers (adding / enabling the unit when needed).</summary>
        public static string ApplyUnitPreset(string hospitalId, string presetKey, out bool changed)
        {
            changed = false;
            var preset = VitalUnitService.Presets.FirstOrDefault(p => p.Item1 == presetKey);
            if (preset == null) return "Unknown unit system.";
            lock (DataStore.Sync)
                foreach (var s in DraftRaw(hospitalId).VitalUnits)
                {
                    string want;
                    if (!preset.Item4.TryGetValue(s.VitalCode, out want)) continue;
                    var u = s.Units.FirstOrDefault(x => x.Key == want);
                    if (u == null)
                    {
                        u = VitalUnitService.StandardUnit(s.VitalCode, want);
                        if (u == null) continue;
                        s.Units.Add(u);
                        changed = true;
                    }
                    if (!u.Enabled || !u.IsDefault) changed = true;
                    u.Enabled = true;
                    foreach (var x in s.Units) x.IsDefault = x == u;
                }
            return null;
        }

        /* ── Draft state ── */

        /// <summary>Human-readable list of what the draft changes against the live configuration.</summary>
        public static List<string> ListDraftChanges(string hospitalId)
        {
            lock (DataStore.Sync) return Diff(DraftRaw(hospitalId), PublishedRaw(hospitalId));
        }

        /// <summary>True when both lists hold their common keys in the same order.</summary>
        private static bool SameOrder(IList<string> a, IList<string> b)
        {
            return a.Where(b.Contains).SequenceEqual(b.Where(a.Contains));
        }

        private static List<string> Diff(ConsultFormConfig draft, ConsultFormConfig live)
        {
            var lines = new List<string>();
            var draftKeys = draft.Sections.Select(s => s.Key).ToList();
            if (!SameOrder(draftKeys, live.Sections.Select(s => s.Key).ToList())) lines.Add("Reordered the sections");
            foreach (var ls in live.Sections.Where(x => !draftKeys.Contains(x.Key))) lines.Add("Removed section: " + ls.Label);
            foreach (var s in draft.Sections)
            {
                var ls = live.Sections.FirstOrDefault(x => x.Key == s.Key);
                if (ls == null)
                {
                    lines.Add("Added section: " + s.Label + (s.Fields.Count > 0 ? " (" + s.Fields.Count + " field" + (s.Fields.Count == 1 ? "" : "s") + ")" : ""));
                    continue;
                }
                if (s.Label != ls.Label || s.Description != ls.Description || s.Icon != ls.Icon) lines.Add("Edited section: " + s.Label);
                if (s.Enabled != ls.Enabled) lines.Add((s.Enabled ? "Enabled" : "Disabled") + " section: " + s.Label);
                var fieldKeys = s.Fields.Select(f => f.Key).ToList();
                if (!SameOrder(fieldKeys, ls.Fields.Select(f => f.Key).ToList())) lines.Add("Reordered fields in " + s.Label);
                foreach (var lf in ls.Fields.Where(x => !fieldKeys.Contains(x.Key))) lines.Add("Removed field: " + lf.Label + " (" + s.Label + ")");
                foreach (var f in s.Fields)
                {
                    var lf = ls.Fields.FirstOrDefault(x => x.Key == f.Key);
                    if (lf == null) { lines.Add("Added field: " + f.Label + " (" + s.Label + ")"); continue; }
                    if (f.Label != lf.Label || f.Description != lf.Description || f.InputType != lf.InputType || f.RequiredAtSignOff != lf.RequiredAtSignOff)
                        lines.Add("Edited field: " + f.Label + " (" + s.Label + ")");
                    if (f.Enabled != lf.Enabled) lines.Add((f.Enabled ? "Enabled" : "Disabled") + " field: " + f.Label + " (" + s.Label + ")");
                    var valueKeys = f.Values.Select(v => v.Key).ToList();
                    foreach (var lv in lf.Values.Where(x => !valueKeys.Contains(x.Key))) lines.Add("Removed option “" + lv.Label + "” (" + f.Label + ")");
                    foreach (var v in f.Values)
                    {
                        var lv = lf.Values.FirstOrDefault(x => x.Key == v.Key);
                        if (lv == null) lines.Add("Added option “" + v.Label + "” (" + f.Label + ")");
                        else if (v.Enabled != lv.Enabled) lines.Add((v.Enabled ? "Enabled" : "Disabled") + " option “" + v.Label + "” (" + f.Label + ")");
                    }
                }
            }
            foreach (var s in draft.VitalUnits ?? new List<VitalUnitSetting>())
            {
                var ls = (live.VitalUnits ?? new List<VitalUnitSetting>()).FirstOrDefault(x => x.VitalCode == s.VitalCode);
                if (ls == null)
                {
                    var unit = s.Units.FirstOrDefault(u => u.IsBase);
                    lines.Add("Added vital: " + s.VitalLabel + (unit != null && !string.IsNullOrEmpty(unit.Label) ? " (" + unit.Label + ")" : "") + (s.Enabled ? "" : " — switched off"));
                    continue;
                }
                if (s.Enabled != ls.Enabled) lines.Add((s.Enabled ? "Enabled" : "Disabled") + " vital: " + s.VitalLabel);
                var name = "Vitals · " + s.VitalLabel + ": ";
                foreach (var lu in ls.Units.Where(x => !s.Units.Any(u => u.Key == x.Key))) lines.Add(name + "removed unit " + lu.Label);
                foreach (var u in s.Units)
                {
                    var lu = ls.Units.FirstOrDefault(x => x.Key == u.Key);
                    if (lu == null) { lines.Add(name + "added unit " + u.Label); continue; }
                    if (u.Enabled != lu.Enabled) lines.Add(name + "unit " + u.Label + " turned " + (u.Enabled ? "on" : "off"));
                }
                var d = s.Units.FirstOrDefault(u => u.IsDefault);
                var ld = ls.Units.FirstOrDefault(u => u.IsDefault);
                if (d != null && (ld == null || ld.Key != d.Key)) lines.Add(name + "default unit is now " + d.Label);
            }
            return lines;
        }

        /// <summary>What must be fixed before the draft can go live (empty when it can be published).</summary>
        public static List<string> ListPublishProblems(string hospitalId)
        {
            lock (DataStore.Sync) return Problems(DraftRaw(hospitalId));
        }

        private static List<string> Problems(ConsultFormConfig draft)
        {
            var problems = new List<string>();
            foreach (var s in draft.Sections.Where(x => x.Enabled))
            {
                if (s.IsCustom && !s.Fields.Any(f => f.Enabled)) problems.Add("“" + s.Label + "” has no fields shown — add a field or hide the section.");
                foreach (var f in s.Fields.Where(x => x.Enabled && x.IsCustom && (x.InputType == "SELECT" || x.InputType == "MULTI") && !x.Values.Any(v => v.Enabled)))
                    problems.Add("Add at least one option to “" + f.Label + "” (" + s.Label + ").");
            }
            return problems;
        }

        /// <summary>Makes the draft live. Returns the new version, 0 when there was nothing to publish, or −1 when it has problems.</summary>
        public static int Publish(string hospitalId, string userId)
        {
            lock (DataStore.Sync)
            {
                var live = PublishedRaw(hospitalId);
                var draft = DraftRaw(hospitalId);
                if (Problems(draft).Count > 0) return -1;
                if (Diff(draft, live).Count == 0) return 0;
                var next = Clone(draft);
                next.Version = live.Version + 1;
                next.PublishedAt = DateTime.UtcNow;
                next.PublishedBy = userId;
                /* Vitals added in the draft become real hospital vitals (Settings → Clinical lists them) */
                foreach (var s in next.VitalUnits.Where(x => x.IsPending))
                {
                    var unit = s.Units.FirstOrDefault(u => u.IsBase);
                    HospitalService.CreateCustomVital(hospitalId, s.VitalCode, s.VitalLabel, unit != null && unit.Label.Length > 0 ? unit.Label : null, s.MinValue, s.MaxValue, s.Enabled);
                    s.IsPending = false;
                }
                DataStore.ConsultFormPublished[hospitalId] = next;
                DataStore.ConsultFormDrafts[hospitalId] = Clone(next);
                /* Which vitals are captured is kept on the hospital's vitals list too (Settings → Clinical shows it) */
                foreach (var s in next.VitalUnits)
                {
                    var vital = DataStore.VitalsConfig.FirstOrDefault(v => v.HospitalId == hospitalId && v.VitalCode == s.VitalCode);
                    if (vital != null) vital.IsEnabled = s.Enabled;
                }
                return next.Version;
            }
        }

        /// <summary>Throws the draft away — back to the live configuration.</summary>
        public static void DiscardDraft(string hospitalId)
        {
            lock (DataStore.Sync) DataStore.ConsultFormDrafts[hospitalId] = Clone(PublishedRaw(hospitalId));
        }

        /// <summary>
        /// Puts the built-in sections, fields and options back to the default form (all shown, default order).
        /// Custom sections, fields and options are kept as they are; custom sections follow the built-in ones.
        /// </summary>
        public static void RestoreDefaults(string hospitalId)
        {
            lock (DataStore.Sync)
            {
                var draft = DraftRaw(hospitalId);
                var fresh = Catalog();
                foreach (var s in fresh)
                {
                    var old = draft.Sections.FirstOrDefault(x => x.Key == s.Key);
                    if (old == null) continue;
                    s.Fields.AddRange(old.Fields.Where(f => f.IsCustom).Select(CloneField));
                    foreach (var f in s.Fields.Where(x => x.AllowCustomOptions))
                    {
                        var oldField = old.Fields.FirstOrDefault(x => x.Key == f.Key);
                        if (oldField != null) f.Values.AddRange(oldField.Values.Where(v => v.IsCustom).Select(v => new ConsultFormValue { Key = v.Key, Label = v.Label, Enabled = v.Enabled, IsCustom = true }));
                    }
                }
                draft.Sections = fresh.Concat(draft.Sections.Where(x => x.IsCustom).Select(CloneSection)).ToList();

                /* Vitals: built-in ones on/off as in the default form (custom vitals keep theirs); units back to the
                   default set, with custom units kept but switched off */
                var units = DefaultVitalUnits(hospitalId, true);
                foreach (var s in units)
                {
                    var old = draft.VitalUnits.FirstOrDefault(x => x.VitalCode == s.VitalCode);
                    if (old == null) continue;
                    if (s.IsCustomVital) s.Enabled = old.Enabled;
                    s.Units.AddRange(old.Units.Where(u => u.IsCustom).Select(u => { var c = VitalUnitService.CloneUnit(u); c.Enabled = c.IsDefault = false; return c; }));
                }
                /* Custom vitals added in this draft are kept as they are */
                draft.VitalUnits = units.Concat(draft.VitalUnits.Where(x => x.IsPending).Select(VitalUnitService.CloneSetting)).ToList();
            }
        }

        /* ── Values recorded in custom fields (cln.ConsultationCustomValue) ── */

        /// <summary>Custom field values of a consultation, by field key.</summary>
        public static Dictionary<string, string> GetCustomValues(string consultationId)
        {
            lock (DataStore.Sync)
                return DataStore.CustomFieldValues.Where(v => v.ConsultationId == consultationId).ToDictionary(v => v.FieldKey, v => v.Value);
        }

        /// <summary>Display text of a recorded value: option keys → labels, YES/NO → Yes/No.</summary>
        public static string DisplayValue(ConsultFormField f, string value)
        {
            if (string.IsNullOrEmpty(value) || f == null) return value ?? "";
            if (f.InputType == "YESNO") return value == "YES" ? "Yes" : value == "NO" ? "No" : value;
            if (f.InputType == "SELECT" || f.InputType == "MULTI")
                return string.Join(", ", value.Split('|').Select(k => { var v = f.Values.FirstOrDefault(x => x.Key == k); return v != null ? v.Label : k; }));
            return value;
        }

        /// <summary>
        /// Saves a section's custom field values (an empty value clears the field), capturing the section and field
        /// labels and the display text from the live setup.
        /// </summary>
        public static void SaveCustomValues(string hospitalId, string consultationId, string sectionKey, IDictionary<string, string> values, string userId)
        {
            lock (DataStore.Sync)
            {
                var section = PublishedRaw(hospitalId).Sections.FirstOrDefault(s => s.Key == sectionKey);
                foreach (var kv in values)
                {
                    var row = DataStore.CustomFieldValues.FirstOrDefault(v => v.ConsultationId == consultationId && v.FieldKey == kv.Key);
                    if (string.IsNullOrEmpty(kv.Value)) { if (row != null) DataStore.CustomFieldValues.Remove(row); continue; }
                    if (row == null) DataStore.CustomFieldValues.Add(row = new ConsultationCustomValue { ConsultationId = consultationId, SectionKey = sectionKey, FieldKey = kv.Key });
                    var field = section != null ? section.Fields.FirstOrDefault(f => f.Key == kv.Key) : null;
                    row.Value = kv.Value;
                    row.SectionLabel = section != null ? section.Label : row.SectionLabel;
                    row.FieldLabel = field != null ? field.Label : row.FieldLabel;
                    row.InputType = field != null ? field.InputType : row.InputType;
                    row.DisplayValue = field != null ? DisplayValue(field, kv.Value) : kv.Value;
                    row.UpdatedAt = DateTime.UtcNow;
                    row.UpdatedBy = userId;
                }
            }
        }

        /// <summary>
        /// A consultation's recorded custom values (copies), in the live setup's section and field order; values whose
        /// section or field has since been removed follow, still with the labels captured when they were saved.
        /// </summary>
        public static List<ConsultationCustomValue> ListCustomValues(string hospitalId, string consultationId)
        {
            lock (DataStore.Sync)
            {
                var sections = PublishedRaw(hospitalId).Sections;
                return DataStore.CustomFieldValues.Where(v => v.ConsultationId == consultationId && !string.IsNullOrEmpty(v.Value))
                    .Select((v, i) =>
                    {
                        var si = sections.FindIndex(s => s.Key == v.SectionKey);
                        var fi = si < 0 ? -1 : sections[si].Fields.FindIndex(f => f.Key == v.FieldKey);
                        var field = fi < 0 ? null : sections[si].Fields[fi];
                        var copy = new ConsultationCustomValue
                        {
                            ConsultationId = v.ConsultationId, SectionKey = v.SectionKey, FieldKey = v.FieldKey, Value = v.Value,
                            SectionLabel = v.SectionLabel ?? (si < 0 ? "" : sections[si].Label),
                            FieldLabel = v.FieldLabel ?? (field != null ? field.Label : v.FieldKey),
                            InputType = v.InputType ?? (field != null ? field.InputType : "TEXT"),
                            DisplayValue = v.DisplayValue ?? DisplayValue(field, v.Value),
                            UpdatedAt = v.UpdatedAt, UpdatedBy = v.UpdatedBy,
                        };
                        return new { copy, rank = (si < 0 ? 10000 : si) * 1000 + (fi < 0 ? 999 : fi), i };
                    })
                    .OrderBy(x => x.rank).ThenBy(x => x.i).Select(x => x.copy).ToList();
            }
        }
    }

    /// <summary>Lookup over a published configuration. Anything not in the configuration counts as enabled.</summary>
    public class ConsultFormView
    {
        private readonly ConsultFormConfig _config;

        public ConsultFormView(ConsultFormConfig config) { _config = config; }

        public int Version { get { return _config.Version; } }

        /// <summary>Section keys in configured order (enabled and disabled).</summary>
        public List<string> SectionOrder { get { return _config.Sections.Select(s => s.Key).ToList(); } }

        public ConsultFormSection GetSection(string key) { return _config.Sections.FirstOrDefault(s => s.Key == key); }

        /// <summary>Enabled custom sections, in order.</summary>
        public List<ConsultFormSection> CustomSections { get { return _config.Sections.Where(s => s.IsCustom && s.Enabled).ToList(); } }

        private ConsultFormField Field(string section, string field)
        {
            var s = GetSection(section);
            return s != null ? s.Fields.FirstOrDefault(f => f.Key == field) : null;
        }

        public bool SectionOn(string section)
        {
            var s = GetSection(section);
            return s == null || s.Enabled;
        }

        public bool FieldOn(string section, string field)
        {
            if (section == null) return true;
            var f = Field(section, field);
            return SectionOn(section) && (f == null || f.Enabled);
        }

        /// <summary>True when the option is offered. Options a field does not list (e.g. a deleted custom option) are not.</summary>
        public bool ValueOn(string section, string field, string value)
        {
            var f = Field(section, field);
            if (f == null) return true;
            var v = f.Values.FirstOrDefault(x => x.Key == value);
            return v != null && v.Enabled;
        }

        /// <summary>Offered options of a choice field, in order.</summary>
        public List<ConsultFormValue> EnabledOptions(string section, string field)
        {
            var f = Field(section, field);
            return f == null ? new List<ConsultFormValue>() : f.Values.Where(v => v.Enabled).ToList();
        }

        /// <summary>True when the vital is captured (vitals the setup doesn't know yet count as captured).</summary>
        public bool VitalOn(string vitalCode)
        {
            var s = (_config.VitalUnits ?? new List<VitalUnitSetting>()).FirstOrDefault(x => x.VitalCode == vitalCode);
            return s == null || s.Enabled;
        }

        /// <summary>Units of a vital (a single unlabelled base unit when the setup has none for it).</summary>
        public VitalUnitSetting UnitsFor(string vitalCode)
        {
            var s = (_config.VitalUnits ?? new List<VitalUnitSetting>()).FirstOrDefault(x => x.VitalCode == vitalCode);
            return s ?? new VitalUnitSetting
            {
                VitalCode = vitalCode, VitalLabel = vitalCode, Enabled = true,
                Units = new List<VitalUnit> { new VitalUnit { Key = "BASE", Label = "", Factor = 1, Decimals = 2, IsBase = true, Enabled = true, IsDefault = true } },
            };
        }

        /// <summary>The unit a vital is shown and entered in by default.</summary>
        public VitalUnit DefaultUnit(string vitalCode)
        {
            var units = UnitsFor(vitalCode).Units;
            return units.FirstOrDefault(u => u.Enabled && u.IsDefault) ?? units.FirstOrDefault(u => u.Enabled) ?? units[0];
        }

        /// <summary>Units a vital may be entered in (all enabled units with the unit switch on, else the default only).</summary>
        public List<VitalUnit> EntryUnits(string vitalCode)
        {
            var def = DefaultUnit(vitalCode);
            if (!FieldOn("vitals", "unitSwitch")) return new List<VitalUnit> { def };
            return new[] { def }.Concat(UnitsFor(vitalCode).Units.Where(u => u.Enabled && u != def)).ToList();
        }

        /// <summary>Field keys of a section in configured order (enabled and disabled).</summary>
        public List<string> FieldOrder(string section)
        {
            var s = GetSection(section);
            return s == null ? new List<string>() : s.Fields.Select(f => f.Key).ToList();
        }

        /// <summary>Shown custom fields of a shown section, in order.</summary>
        public List<ConsultFormField> CustomFields(string section)
        {
            var s = GetSection(section);
            return s == null || !s.Enabled ? new List<ConsultFormField>() : s.Fields.Where(f => f.IsCustom && f.Enabled).ToList();
        }
    }
}
