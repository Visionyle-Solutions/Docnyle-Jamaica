using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>
    /// Vitals capture for the hospital's enabled vitals. Each vital is entered in any unit the consultation setup
    /// enables and stored in its base unit (°F, mmHg, cm, kg, …); values are range-checked on the server
    /// (plus systolic &gt; diastolic) and BMI is derived on save. Previous values and ranges show in the default unit.
    /// </summary>
    public partial class consultation_vitals : ConsultationSection
    {
        private ConsultationVitals _current, _previous;
        private List<string> _highlight = new List<string>();

        protected override string SectionKey { get { return "vitals"; } }

        /// <summary>Vitals to emphasise (from the branched-flow engine).</summary>
        public List<string> HighlightVitals { set { _highlight = value ?? new List<string>(); } }

        public override void Bind()
        {
            _current = ConsultationService.GetVitals(ConsultationId);
            var last = ConsultationService.ListPastVisits(Visit.PatientId).FirstOrDefault(v => v.ConsultationId != ConsultationId);
            _previous = last != null ? ConsultationService.GetVitals(last.ConsultationId) : null;
            VitalsRepeater.DataSource = HospitalService.ListVitalsConfig(HospitalId, false).Where(v => Host.FormConfig.VitalOn(v.VitalCode)).ToList();
            VitalsRepeater.DataBind();
            CalcText.Text = Calculations(_current);
            RecordedText.Text = _current != null ? "Last Saved " + AppPage.FmtDateTime(_current.RecordedAt) : "";
            SaveVitalsButton.Visible = !Host.IsLocked;
        }

        protected void VitalsRepeater_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var cfg = e.Item.DataItem as VitalConfig;
            if (cfg == null) return;
            var input = (TextBox)e.Item.FindControl("VitalInput");
            var value = ConsultationService.GetVital(_current, cfg.VitalCode);
            var unit = Host.FormConfig.DefaultUnit(cfg.VitalCode);
            VitalUnitUi.Bind((DropDownList)e.Item.FindControl("UnitInput"), (Literal)e.Item.FindControl("UnitLabel"), Host.FormConfig.EntryUnits(cfg.VitalCode));
            ((DropDownList)e.Item.FindControl("UnitInput")).Enabled = !Host.IsLocked;
            input.Text = value.HasValue ? VitalUnitService.Format(unit, value.Value) : "";
            input.ReadOnly = Host.IsLocked;
            input.Attributes["data-vital-code"] = cfg.VitalCode;
            if (cfg.VitalCode == "PAIN_SCALE")
            {
                /* Slider bounds follow the unit (0–10, or 0–5 on the faces scale); base bounds let the unit switch rescale it */
                input.TextMode = TextBoxMode.Range;
                input.Attributes["min"] = VitalUnitService.Format(unit, 0);
                input.Attributes["max"] = VitalUnitService.Format(unit, 10);
                input.Attributes["data-min-base"] = "0";
                input.Attributes["data-max-base"] = "10";
                input.Text = VitalUnitService.Format(unit, value ?? 0);
                input.Attributes["oninput"] = "this.title=this.value;";
                input.Attributes["title"] = input.Text;
                input.Style["width"] = "110px";
            }
            if (_highlight.Contains(cfg.VitalCode)) input.Style["box-shadow"] = "0 0 0 2px var(--color-primary)";
        }

        /// <summary>Last visit's value in the default unit, e.g. "71 kg".</summary>
        protected string PreviousValue(string code)
        {
            var v = ConsultationService.GetVital(_previous, code);
            return v.HasValue ? VitalUnitService.FormatWithUnit(Host.FormConfig.DefaultUnit(code), v.Value) : "—";
        }

        /// <summary>Valid range in the default unit.</summary>
        protected string RangeLabel(string code)
        {
            return VitalUnitService.RangeText(Host.FormConfig.DefaultUnit(code), ConsultationService.RangeFor(HospitalId, code));
        }

        private static string Calculations(ConsultationVitals v)
        {
            var parts = new List<string>();
            if (v != null && v.Bmi.HasValue) parts.Add("<strong>BMI:</strong> " + v.Bmi + " (" + SoapComposer.BmiClass(v.Bmi.Value) + ")");
            if (v != null && v.HeightCm.HasValue && v.WeightKg.HasValue)
                parts.Add("<strong>BSA:</strong> " + Math.Round(Math.Sqrt(v.HeightCm.Value * v.WeightKg.Value / 3600), 2) + " m²");
            if (v != null && v.BpSystolic.HasValue && v.BpDiastolic.HasValue)
                parts.Add("<strong>MAP:</strong> " + Math.Round(v.BpDiastolic.Value + (v.BpSystolic.Value - v.BpDiastolic.Value) / 3) + " mmHg");
            return parts.Count > 0 ? string.Join(" · ", parts) : "BMI: — · BSA: — · MAP: —";
        }

        protected override void ApplyFormConfig()
        {
            VitalsPanel.CssClass = "vitals-table" + (FieldOn("previous") ? "" : " vitals-table--no-prev") + (FieldOn("range") ? "" : " vitals-table--no-range");
            CalcHolder.Visible = FieldOn("calculated");
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            foreach (RepeaterItem item in VitalsRepeater.Items)
            {
                var code = ((HiddenField)item.FindControl("VitalCode")).Value;
                VitalUnitUi.Render((DropDownList)item.FindControl("UnitInput"), (TextBox)item.FindControl("VitalInput"),
                    Host.FormConfig.UnitsFor(code).Units, Host.FormConfig.DefaultUnit(code));
            }
            if (!FieldOn("calculated")) return;
            /* Live BMI/BSA/MAP preview while typing (supplementary JS; the saved values are computed on the server) */
            Func<string, string> idOf = code =>
            {
                var item = VitalsRepeater.Items.Cast<RepeaterItem>().FirstOrDefault(i => ((HiddenField)i.FindControl("VitalCode")).Value == code);
                return item != null ? item.FindControl("VitalInput").ClientID : "";
            };
            var script = string.Format("Consult360.liveVitals({{height:'{0}',weight:'{1}',sys:'{2}',dia:'{3}'}}, 'vitalsCalcDisplay');",
                idOf("HEIGHT"), idOf("WEIGHT"), idOf("BP_SYS"), idOf("BP_DIA"));
            Page.ClientScript.RegisterStartupScript(GetType(), "liveVitals", script, true);
        }

        protected void SaveVitalsButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked()) return;
            var values = new Dictionary<string, double?>();
            var errors = new Dictionary<string, string>();
            foreach (RepeaterItem item in VitalsRepeater.Items)
            {
                var code = ((HiddenField)item.FindControl("VitalCode")).Value;
                var raw = ((TextBox)item.FindControl("VitalInput")).Text.Trim();
                if (raw.Length == 0) { values[code] = null; continue; }
                /* Typed in the picked unit (or the default) → converted to the stored base unit */
                var unit = VitalUnitUi.Selected((DropDownList)item.FindControl("UnitInput"), Host.FormConfig.UnitsFor(code).Units, Host.FormConfig.DefaultUnit(code));
                double parsed;
                if (unit == null) { errors[code] = "That unit is no longer available — choose another"; continue; }
                if (!VitalUnitService.TryParse(unit, raw, out parsed))
                {
                    errors[code] = unit.IsFeetInches ? "Enter feet and inches, e.g. 5'7\"" : "Enter a number";
                    continue;
                }
                var range = ConsultationService.RangeFor(HospitalId, code);
                if (range != null && (parsed < range.Min || parsed > range.Max))
                {
                    errors[code] = "Must be " + VitalUnitService.RangeText(unit, range);
                    continue;
                }
                values[code] = parsed;
            }
            foreach (var kv in ConsultationService.ValidateVitals(values, HospitalId)) if (!errors.ContainsKey(kv.Key)) errors[kv.Key] = kv.Value;

            foreach (RepeaterItem item in VitalsRepeater.Items)
            {
                var code = ((HiddenField)item.FindControl("VitalCode")).Value;
                ((Literal)item.FindControl("VitalError")).Text = errors.ContainsKey(code) ? Server.HtmlEncode(errors[code]) : "";
            }
            if (errors.Count > 0)
            {
                AppPage.ToastWarning("Please fix the highlighted errors");
                Host.ActivateStep("vitals");
                return;
            }
            ConsultationService.SaveVitals(ConsultationId, values, Host.DoctorId);
            Audit("UPDATE", "ConsultationVitals");
            AppPage.ToastSuccess("Vitals saved");
            Host.RefreshAll();
        }
    }
}
