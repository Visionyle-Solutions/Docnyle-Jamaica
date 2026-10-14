using System;
using System.Collections.Generic;

namespace OpdProject.Models
{
    /// <summary>
    /// Consultation form configuration for one hospital (cfg.ConsultationFormConfig): which sections, fields and
    /// option values the Consultation 360 page shows, and the order of the sections. Array position is the order.
    /// The configuration page edits a working draft; only the published copy drives the consultation.
    /// Built-in parts come from the catalogue; custom sections, fields and options are added by the hospital.
    /// </summary>
    public class ConsultFormConfig
    {
        public string HospitalId { get; set; }
        public int Version { get; set; }
        public DateTime? PublishedAt { get; set; }
        public string PublishedBy { get; set; }
        public List<ConsultFormSection> Sections { get; set; }
        /// <summary>Per hospital vital: whether it is recorded, and the units it can be recorded in.</summary>
        public List<VitalUnitSetting> VitalUnits { get; set; }
    }

    /// <summary>
    /// One vital's setup: whether the consultation and Nurse Vitals capture it, and the units it can be recorded in.
    /// Values are always stored in the base (canonical) unit — °F, mmHg, cm, kg, … — so ranges, BMI and the clinical
    /// engines are unaffected; other units convert on entry and display.
    /// </summary>
    public class VitalUnitSetting
    {
        public string VitalCode { get; set; }
        public string VitalLabel { get; set; }
        /// <summary>Captured in the consultation and on Nurse Vitals (applied to the hospital's vitals list on publish).</summary>
        public bool Enabled { get; set; }
        /// <summary>Added by the hospital (Consultation Setup → Vitals).</summary>
        public bool IsCustomVital { get; set; }
        /// <summary>Added in the draft and not published yet: the vital is created in the hospital's vitals list on publish.</summary>
        public bool IsPending { get; set; }
        /// <summary>Valid range of a pending custom vital, in its unit (either bound optional).</summary>
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public List<VitalUnit> Units { get; set; }
    }

    /// <summary>One unit of a vital: base value = entered value × Factor + Offset. One enabled unit is the default.</summary>
    public class VitalUnit
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public double Factor { get; set; }
        public double Offset { get; set; }
        /// <summary>Decimal places shown when a stored value is displayed in this unit.</summary>
        public int Decimals { get; set; }
        /// <summary>Feet-and-inches (entered as 5'7").</summary>
        public bool IsFeetInches { get; set; }
        public bool IsBase { get; set; }
        public bool Enabled { get; set; }
        public bool IsDefault { get; set; }
        /// <summary>Added by the hospital with its own conversion factor (deletable).</summary>
        public bool IsCustom { get; set; }
    }

    /// <summary>One consultation card (Vitals, Complaints, … or a custom card). Required sections are needed for sign-off and cannot be disabled.</summary>
    public class ConsultFormSection
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public bool Enabled { get; set; }
        /// <summary>Added by the hospital (editable, deletable); its card holds only custom fields.</summary>
        public bool IsCustom { get; set; }
        public List<ConsultFormField> Fields { get; set; }
    }

    /// <summary>A field, column or tool inside a section. Required fields cannot be disabled.</summary>
    public class ConsultFormField
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public bool Enabled { get; set; }
        /// <summary>Added by the hospital; captured per consultation (cln.ConsultationCustomValue).</summary>
        public bool IsCustom { get; set; }
        /// <summary>Custom fields only: TEXT, TEXTAREA, NUMBER, DATE, YESNO, SELECT, MULTI.</summary>
        public string InputType { get; set; }
        /// <summary>Custom fields only: must have a value before the consultation can be signed off.</summary>
        public bool RequiredAtSignOff { get; set; }
        /// <summary>Built-in choice fields whose list the hospital may extend with its own options.</summary>
        public bool AllowCustomOptions { get; set; }
        /// <summary>Allowed options of a choice field (empty for free entry / tools).</summary>
        public List<ConsultFormValue> Values { get; set; }

        /// <summary>True when the hospital can add options to this field.</summary>
        public bool AcceptsOptions { get { return AllowCustomOptions || InputType == "SELECT" || InputType == "MULTI"; } }
    }

    /// <summary>One option of a choice field (e.g. Laterality → Left). Required options cannot be disabled.</summary>
    public class ConsultFormValue
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public bool Required { get; set; }
        public bool Enabled { get; set; }
        public bool IsCustom { get; set; }
    }

    /// <summary>
    /// A custom field's value for one consultation (multiple-choice keys are joined with "|"). The labels and the
    /// display text are captured when the value is saved, so prints of the visit don't change if the setup does.
    /// </summary>
    public class ConsultationCustomValue
    {
        public string ConsultationId { get; set; }
        public string SectionKey { get; set; }
        public string FieldKey { get; set; }
        public string Value { get; set; }
        public string SectionLabel { get; set; }
        public string FieldLabel { get; set; }
        public string InputType { get; set; }
        /// <summary>Value as read by a person (option labels, Yes/No).</summary>
        public string DisplayValue { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; }
    }
}
