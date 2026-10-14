using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>
    /// Vital-sign units: the standard units each built-in vital can be recorded in (with their conversion to the
    /// stored base unit), the hospital's default unit set, unit-system presets, and parsing / formatting values.
    /// </summary>
    public static class VitalUnitService
    {
        private static VitalUnit U(string key, string label, double factor, double offset = 0, int decimals = 1, bool feetInches = false)
        {
            return new VitalUnit { Key = key, Label = label, Factor = factor, Offset = offset, Decimals = decimals, IsFeetInches = feetInches };
        }

        /// <summary>Standard units per built-in vital; the first is the base (stored) unit.</summary>
        private static readonly Dictionary<string, Func<List<VitalUnit>>> Standard = new Dictionary<string, Func<List<VitalUnit>>>
        {
            { "TEMP", () => new List<VitalUnit> { U("F", "°F", 1), U("C", "°C", 1.8, 32) } },
            { "BP_SYS", () => new List<VitalUnit> { U("MMHG", "mmHg", 1, 0, 0), U("KPA", "kPa", 7.50062) } },
            { "BP_DIA", () => new List<VitalUnit> { U("MMHG", "mmHg", 1, 0, 0), U("KPA", "kPa", 7.50062) } },
            { "PULSE", () => new List<VitalUnit> { U("BPM", "bpm", 1, 0, 0) } },
            { "SPO2", () => new List<VitalUnit> { U("PCT", "%", 1, 0, 0) } },
            { "HEIGHT", () => new List<VitalUnit> { U("CM", "cm", 1), U("IN", "in", 2.54), U("FT", "ft", 30.48, 0, 2), U("M", "m", 100, 0, 2), U("FTIN", "ft-in", 1, 0, 1, true) } },
            { "WEIGHT", () => new List<VitalUnit> { U("KG", "kg", 1), U("LB", "lb", 0.45359237), U("G", "g", 0.001, 0, 0) } },
            { "RESP_RATE", () => new List<VitalUnit> { U("BRPM", "breaths/min", 1, 0, 0) } },
            { "PAIN_SCALE", () => new List<VitalUnit> { U("P10", "0–10", 1, 0, 0), U("P5", "0–5 faces", 2, 0, 0) } },
            { "WAIST_CIRC", () => new List<VitalUnit> { U("CM", "cm", 1), U("IN", "in", 2.54) } },
            { "HEAD_CIRC", () => new List<VitalUnit> { U("CM", "cm", 1), U("IN", "in", 2.54) } },
        };

        /// <summary>Units switched on in a new setup (the rest are offered as "tap to add").</summary>
        private static readonly Dictionary<string, string[]> Seeded = new Dictionary<string, string[]>
        {
            { "TEMP", new[] { "F", "C" } }, { "HEIGHT", new[] { "CM", "IN", "FT" } }, { "WEIGHT", new[] { "KG", "LB" } },
            { "WAIST_CIRC", new[] { "CM", "IN" } }, { "HEAD_CIRC", new[] { "CM", "IN" } },
        };

        /// <summary>Unit-system presets: (key, label, note, vital → unit key).</summary>
        public static readonly List<Tuple<string, string, string, Dictionary<string, string>>> Presets = new List<Tuple<string, string, string, Dictionary<string, string>>>
        {
            Tuple.Create("metric", "Metric", "°C · kg · cm", new Dictionary<string, string> { { "TEMP", "C" }, { "HEIGHT", "CM" }, { "WEIGHT", "KG" }, { "WAIST_CIRC", "CM" }, { "HEAD_CIRC", "CM" } }),
            Tuple.Create("mixed", "Common clinic mix", "°F · kg · cm", new Dictionary<string, string> { { "TEMP", "F" }, { "HEIGHT", "CM" }, { "WEIGHT", "KG" }, { "WAIST_CIRC", "CM" }, { "HEAD_CIRC", "CM" } }),
            Tuple.Create("us", "US customary", "°F · lb · ft-in", new Dictionary<string, string> { { "TEMP", "F" }, { "HEIGHT", "FTIN" }, { "WEIGHT", "LB" }, { "WAIST_CIRC", "IN" }, { "HEAD_CIRC", "IN" } }),
        };

        /// <summary>Standard units of a vital that are not yet in its list (offered as one-tap additions).</summary>
        public static List<VitalUnit> AvailableStandardUnits(VitalUnitSetting s)
        {
            Func<List<VitalUnit>> make;
            if (!Standard.TryGetValue(s.VitalCode, out make)) return new List<VitalUnit>();
            return make().Where(u => !s.Units.Any(x => x.Key == u.Key || string.Equals(x.Label, u.Label, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        public static VitalUnit StandardUnit(string vitalCode, string unitKey)
        {
            Func<List<VitalUnit>> make;
            return Standard.TryGetValue(vitalCode, out make) ? make().FirstOrDefault(u => u.Key == unitKey) : null;
        }

        /// <summary>Built-in vitals switched off in a new setup (the rest, and every custom vital, are on).</summary>
        private static readonly string[] OffByDefault = { "WAIST_CIRC", "HEAD_CIRC" };

        /// <summary>Whether a vital is captured in the default form.</summary>
        public static bool OnByDefault(string vitalCode) { return !OffByDefault.Contains(vitalCode); }

        /// <summary>
        /// The default setup for one hospital vital: <paramref name="enabled"/> as given, default units
        /// (temperature follows the hospital's default unit).
        /// </summary>
        public static VitalUnitSetting DefaultSetting(VitalConfig vital, string defaultTempUnit, bool enabled)
        {
            Func<List<VitalUnit>> make;
            List<VitalUnit> units;
            if (Standard.TryGetValue(vital.VitalCode, out make))
            {
                string[] seeded;
                var keep = Seeded.TryGetValue(vital.VitalCode, out seeded) ? seeded : null;
                units = make().Select((u, i) => { u.IsBase = i == 0; return u; }).Where(u => u.IsBase || (keep != null && keep.Contains(u.Key))).ToList();
            }
            else
                /* A custom vital (Settings → Clinical): its own unit is the base */
                units = new List<VitalUnit> { new VitalUnit { Key = "BASE", Label = vital.AllowedUnits ?? "", Factor = 1, Decimals = 2, IsBase = true } };
            foreach (var u in units) u.Enabled = true;
            var preferred = vital.VitalCode == "TEMP" && defaultTempUnit == "C" ? "C" : units[0].Key;
            foreach (var u in units) u.IsDefault = u.Key == preferred;
            return new VitalUnitSetting { VitalCode = vital.VitalCode, VitalLabel = vital.DisplayLabel, Enabled = enabled, IsCustomVital = vital.IsCustom, Units = units };
        }

        public static VitalUnitSetting CloneSetting(VitalUnitSetting s)
        {
            return new VitalUnitSetting
            {
                VitalCode = s.VitalCode, VitalLabel = s.VitalLabel, Enabled = s.Enabled, IsCustomVital = s.IsCustomVital,
                IsPending = s.IsPending, MinValue = s.MinValue, MaxValue = s.MaxValue,
                Units = s.Units.Select(CloneUnit).ToList(),
            };
        }

        public static VitalUnit CloneUnit(VitalUnit u)
        {
            return new VitalUnit
            {
                Key = u.Key, Label = u.Label, Factor = u.Factor, Offset = u.Offset, Decimals = u.Decimals, IsFeetInches = u.IsFeetInches,
                IsBase = u.IsBase, Enabled = u.Enabled, IsDefault = u.IsDefault, IsCustom = u.IsCustom,
            };
        }

        /* ── Conversion ── */

        public static double ToBase(VitalUnit u, double value) { return Math.Round(value * u.Factor + u.Offset, 2); }

        public static double FromBase(VitalUnit u, double baseValue) { return (baseValue - u.Offset) / u.Factor; }

        /// <summary>Parses a value typed in the unit (feet-and-inches accepts 5'7", 5' 7, 5 ft 7 in, 5-7, 5 7 or 5). Returns the base value.</summary>
        public static bool TryParse(VitalUnit u, string raw, out double baseValue)
        {
            baseValue = 0;
            raw = (raw ?? "").Trim();
            double v;
            if (u.IsFeetInches)
            {
                var m = Regex.Match(raw, @"^\s*(\d+(?:\.\d+)?)\s*(?:'|ft|feet)?\s*(?:[-\s]\s*)?(?:(\d+(?:\.\d+)?)\s*(?:""|in|inches)?)?\s*$", RegexOptions.IgnoreCase);
                if (!m.Success) return false;
                var feet = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var inches = m.Groups[2].Success ? double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                if (inches >= 12) return false;
                baseValue = Math.Round((feet * 12 + inches) * 2.54, 2);
                return true;
            }
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            baseValue = ToBase(u, v);
            return true;
        }

        /// <summary>A stored (base) value written in the unit, without the unit label.</summary>
        public static string Format(VitalUnit u, double baseValue)
        {
            if (u.IsFeetInches)
            {
                var totalInches = Math.Round(baseValue / 2.54 * 2, MidpointRounding.AwayFromZero) / 2;   /* nearest half inch */
                var feet = (int)Math.Floor(totalInches / 12);
                var inches = totalInches - feet * 12;
                return feet + "'" + inches.ToString("0.#", CultureInfo.InvariantCulture) + "\"";
            }
            var shown = Math.Round(FromBase(u, baseValue), u.Decimals, MidpointRounding.AwayFromZero);
            return shown.ToString("0." + new string('#', Math.Max(u.Decimals, 0)), CultureInfo.InvariantCulture).TrimEnd('.');
        }

        /// <summary>A stored value with its unit label, e.g. "37.2 °C" or "5'7"".</summary>
        public static string FormatWithUnit(VitalUnit u, double baseValue)
        {
            var text = Format(u, baseValue);
            return u.IsFeetInches || string.IsNullOrEmpty(u.Label) ? text : text + (u.Label == "%" ? "" : " ") + u.Label;
        }

        /// <summary>A validation range written in the unit, e.g. "32.2–43.3 °C".</summary>
        public static string RangeText(VitalUnit u, VitalRange r)
        {
            if (r == null) return "";
            if (u.IsBase) return r.Label;
            Func<double, string> f = x => Format(u, x);
            var bounded = r.Min > double.MinValue / 2 && r.Max < double.MaxValue / 2;
            var text = bounded ? f(r.Min) + "–" + f(r.Max) : r.Min > double.MinValue / 2 ? "≥ " + f(r.Min) : "≤ " + f(r.Max);
            return u.IsFeetInches ? text : text + " " + u.Label;
        }
    }
}
