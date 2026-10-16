using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>Hospital profile, key/value settings, display formats, statutory registrations and clinical configuration lists.</summary>
    public static class HospitalService
    {
        public static Hospital GetHospital(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.Hospitals.FirstOrDefault(h => h.HospitalId == hospitalId);
        }

        /// <summary>Applies changes to the hospital profile under the store lock.</summary>
        public static void UpdateHospital(string hospitalId, Action<Hospital> apply)
        {
            lock (DataStore.Sync)
            {
                var h = DataStore.Hospitals.FirstOrDefault(x => x.HospitalId == hospitalId);
                if (h != null) apply(h);
            }
        }

        public static string GetSetting(string hospitalId, string key)
        {
            lock (DataStore.Sync)
            {
                var r = DataStore.HospitalSettings.FirstOrDefault(x => x.HospitalId == hospitalId && x.SettingKey == key);
                return r != null ? r.Value : null;
            }
        }

        public static Dictionary<string, string> GetSettings(string hospitalId)
        {
            lock (DataStore.Sync)
                return DataStore.HospitalSettings.Where(x => x.HospitalId == hospitalId).ToDictionary(x => x.SettingKey, x => x.Value);
        }

        /// <summary>Saves the given keys. MRN_SEQ is system-managed and never overwritten from the UI.</summary>
        public static void SaveSettings(string hospitalId, IDictionary<string, string> values)
        {
            lock (DataStore.Sync)
            {
                foreach (var kv in values)
                {
                    if (kv.Key == "MRN_SEQ") continue;
                    SetSettingUnlocked(hospitalId, kv.Key, kv.Value);
                }
                if (values.ContainsKey("DOCTOR_NAME_TEMPLATE")) SchedulingService.RefreshDoctorNames(hospitalId);
            }
        }

        internal static void SetSettingUnlocked(string hospitalId, string key, string value)
        {
            var r = DataStore.HospitalSettings.FirstOrDefault(x => x.HospitalId == hospitalId && x.SettingKey == key);
            if (r != null) r.Value = value;
            else DataStore.HospitalSettings.Add(new HospitalSetting { HospitalId = hospitalId, SettingKey = key, Value = value });
        }

        /* ── Display formats (Settings → Hospital & Practice → Formats) ── */

        /// <summary>
        /// A UTC instant (DateTimeKind.Utc — a system event such as CreatedAt or PaidAt) is shown in the viewer's own
        /// timezone. Anything else (a calendar date, or a clinic wall-clock time such as AppointmentView.SlotStart) is
        /// shown as it is.
        /// </summary>
        private static DateTime ForDisplay(string hospitalId, DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? TimeZoneService.ToZone(value, TimeZoneService.ViewerZoneId(hospitalId)) : value;
        }

        public static string FormatDate(string hospitalId, DateTime? value)
        {
            if (!value.HasValue) return "";
            value = ForDisplay(hospitalId, value.Value);
            switch (GetSetting(hospitalId, "DATE_FORMAT"))
            {
                case "DD/MM/YYYY": return value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                case "MM/DD/YYYY": return value.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
                default: return value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        public static string FormatTime(string hospitalId, DateTime? value)
        {
            if (!value.HasValue) return "";
            value = ForDisplay(hospitalId, value.Value);
            return GetSetting(hospitalId, "TIME_FORMAT") == "24h"
                ? value.Value.ToString("HH:mm", CultureInfo.InvariantCulture)
                : value.Value.ToString("h:mm tt", CultureInfo.InvariantCulture);
        }

        /// <summary>"HH:mm" text → hospital time format.</summary>
        public static string FormatClock(string hospitalId, string hhmm)
        {
            DateTime t;
            return DateTime.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? FormatTime(hospitalId, t) : hhmm;
        }

        /// <summary>MRN format tokens: {prefix} {year} {seq:N} (zero-padded) {seq}.</summary>
        public static string FormatMrn(string format, int seq, string prefix)
        {
            var s = string.IsNullOrEmpty(format) ? "{prefix}-{seq:5}" : format;
            s = s.Replace("{prefix}", string.IsNullOrEmpty(prefix) ? "MRN" : prefix).Replace("{year}", DateTime.UtcNow.Year.ToString());
            s = Regex.Replace(s, @"\{seq:(\d+)\}", m => seq.ToString().PadLeft(int.Parse(m.Groups[1].Value), '0'));
            return s.Replace("{seq}", seq.ToString());
        }

        /// <summary>Issues the next MRN for a hospital and advances MRN_SEQ.</summary>
        internal static string IssueMrnUnlocked(string hospitalId)
        {
            int seq;
            int.TryParse(GetSetting(hospitalId, "MRN_SEQ"), out seq);
            seq++;
            SetSettingUnlocked(hospitalId, "MRN_SEQ", seq.ToString());
            return FormatMrn(GetSetting(hospitalId, "MRN_FORMAT"), seq, GetSetting(hospitalId, "MRN_PREFIX"));
        }

        /* ── Statutory registrations ── */

        public static List<Compliance> ListCompliances(string country)
        {
            lock (DataStore.Sync)
                return DataStore.Compliances.Where(c => c.IsActive && c.Country == country).OrderBy(c => c.SortOrder).ToList();
        }

        public static HospitalCompliance GetHospitalCompliance(string hospitalId, string complianceId)
        {
            lock (DataStore.Sync)
                return DataStore.HospitalCompliances.FirstOrDefault(hc => hc.HospitalId == hospitalId && hc.ComplianceId == complianceId && hc.IsActive);
        }

        public static void SaveHospitalCompliance(string hospitalId, string complianceId, string number, DateTime? issued, DateTime? expiry)
        {
            lock (DataStore.Sync)
            {
                var hc = DataStore.HospitalCompliances.FirstOrDefault(x => x.HospitalId == hospitalId && x.ComplianceId == complianceId);
                if (hc == null)
                {
                    hc = new HospitalCompliance { HospitalComplianceId = DataStore.NextId("HC"), HospitalId = hospitalId, ComplianceId = complianceId };
                    DataStore.HospitalCompliances.Add(hc);
                }
                hc.RegistrationNumber = number;
                hc.IssuedDate = issued;
                hc.ExpiryDate = expiry;
                hc.IsActive = true;
            }
        }

        /// <summary>"GSTIN: 29AAB… · CEA Reg.: KA/…" line used on printed documents.</summary>
        public static string ComplianceLine(string hospitalId)
        {
            lock (DataStore.Sync)
            {
                var parts = DataStore.HospitalCompliances.Where(hc => hc.HospitalId == hospitalId && hc.IsActive)
                    .Select(hc => new { hc, m = DataStore.Compliances.FirstOrDefault(c => c.ComplianceId == hc.ComplianceId) })
                    .Where(x => x.m != null).Select(x => x.m.PrintLabel + ": " + x.hc.RegistrationNumber);
                return string.Join(" · ", parts);
            }
        }

        /* ── Clinical configuration ── */

        public static List<VitalConfig> ListVitalsConfig(string hospitalId, bool enabledOnly)
        {
            lock (DataStore.Sync)
                return DataStore.VitalsConfig.Where(v => v.HospitalId == hospitalId && (!enabledOnly || v.IsEnabled)).OrderBy(v => v.SortOrder).ToList();
        }

        /// <summary>
        /// A code for a new hospital-defined vital, derived from its label (e.g. "Blood glucose" → "C_BLOOD_GLUCOSE") and
        /// unique among the hospital's vitals and <paramref name="alsoTaken"/> (codes reserved by unpublished vitals).
        /// </summary>
        public static string NewCustomVitalCode(string hospitalId, string label, IEnumerable<string> alsoTaken)
        {
            lock (DataStore.Sync)
            {
                var taken = new HashSet<string>(DataStore.VitalsConfig.Where(v => v.HospitalId == hospitalId).Select(v => v.VitalCode).Concat(alsoTaken ?? new string[0]));
                var stem = "C_" + System.Text.RegularExpressions.Regex.Replace(label.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
                if (stem.Length > 30) stem = stem.Substring(0, 30);
                if (stem == "C_") stem = "C_VITAL";
                var code = stem;
                for (int n = 2; taken.Contains(code); n++) code = stem + "_" + n;
                return code;
            }
        }

        /// <summary>Creates a hospital-defined vital, shown after the existing ones (Consultation Setup publishes it).</summary>
        public static VitalConfig CreateCustomVital(string hospitalId, string code, string label, string unit, double? min, double? max, bool enabled)
        {
            lock (DataStore.Sync)
            {
                var existing = DataStore.VitalsConfig.Where(v => v.HospitalId == hospitalId).ToList();
                var cfg = existing.FirstOrDefault(v => v.VitalCode == code);
                if (cfg != null) return cfg;
                cfg = new VitalConfig
                {
                    VitalDefinitionId = DataStore.NextId("VCF"), HospitalId = hospitalId, VitalCode = code, DisplayLabel = label,
                    AllowedUnits = unit, IsEnabled = enabled, SortOrder = existing.Count == 0 ? 1 : existing.Max(v => v.SortOrder) + 1,
                    IsCustom = true, MinValue = min, MaxValue = max,
                };
                DataStore.VitalsConfig.Add(cfg);
                return cfg;
            }
        }

        public static List<MedicalHistoryItem> ListMedicalHistoryItems(string hospitalId, bool activeOnly)
        {
            lock (DataStore.Sync)
                return DataStore.MedicalHistoryItems.Where(i => i.HospitalId == hospitalId && (!activeOnly || i.IsActive)).OrderBy(i => i.SortOrder).ToList();
        }

        public static void AddMedicalHistoryItem(string hospitalId, string name)
        {
            lock (DataStore.Sync)
            {
                if (DataStore.MedicalHistoryItems.Any(i => i.HospitalId == hospitalId && string.Equals(i.ItemName, name, StringComparison.OrdinalIgnoreCase))) return;
                DataStore.MedicalHistoryItems.Add(new MedicalHistoryItem
                {
                    ItemId = DataStore.NextId("MHI"), HospitalId = hospitalId, ItemName = name, IsQuickPick = true,
                    SortOrder = DataStore.MedicalHistoryItems.Count(i => i.HospitalId == hospitalId) + 1, IsActive = true
                });
            }
        }

        public static void SetMedicalHistoryItemActive(string itemId, bool active)
        {
            lock (DataStore.Sync)
            {
                var i = DataStore.MedicalHistoryItems.FirstOrDefault(x => x.ItemId == itemId);
                if (i != null) i.IsActive = active;
            }
        }

        public static List<MedicalEventItem> ListMedicalEventItems(string hospitalId)
        {
            lock (DataStore.Sync)
                return DataStore.MedicalEventItems.Where(i => i.HospitalId == hospitalId && i.IsActive).OrderBy(i => i.SortOrder).ToList();
        }

        public static List<Specialty> ListSpecialties(string hospitalId, bool activeOnly)
        {
            lock (DataStore.Sync)
                return DataStore.Specialties.Where(s => s.HospitalId == hospitalId && (!activeOnly || s.IsActive)).ToList();
        }

        public static string SpecialtyName(string specialtyId)
        {
            lock (DataStore.Sync)
            {
                var s = DataStore.Specialties.FirstOrDefault(x => x.SpecialtyId == specialtyId);
                return s != null ? s.SpecialtyName : specialtyId;
            }
        }

        public static Specialty CreateSpecialty(string hospitalId, string name)
        {
            lock (DataStore.Sync)
            {
                var row = new Specialty { SpecialtyId = DataStore.NextId("SPEC"), HospitalId = hospitalId, SpecialtyName = name, IsActive = true };
                DataStore.Specialties.Add(row);
                return row;
            }
        }

        public static void ToggleSpecialty(string specialtyId)
        {
            lock (DataStore.Sync)
            {
                var s = DataStore.Specialties.FirstOrDefault(x => x.SpecialtyId == specialtyId);
                if (s != null) s.IsActive = !s.IsActive;
            }
        }

        public static List<PaymentMethod> ListPaymentMethods(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.PaymentMethods.Where(m => m.HospitalId == hospitalId && m.IsActive).ToList();
        }
    }
}
