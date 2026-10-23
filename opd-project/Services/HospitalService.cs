using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>Hospital profile, key/value settings, display formats, statutory registrations and clinical configuration lists.</summary>
    public static class HospitalService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        private static bool Api { get { return DataSource.IsApi("Hospital"); } }

        /// <summary>True when there is a signed-in API session to call the service with.</summary>
        private static bool HasApiSession { get { return AppSession.ApiUser != null && !string.IsNullOrEmpty(AppSession.ServiceToken); } }

        /// <summary>
        /// API mode: the caller's hospital (GetHospital, cached 5 minutes) — the service never takes a hospital id from the
        /// client, so <paramref name="hospitalId"/> is ignored. With no session (login, patient portal) it is the portal
        /// hospital's display profile from GetPortalBookingOptions.
        /// </summary>
        public static Hospital GetHospital(string hospitalId)
        {
            if (Api)
            {
                if (!HasApiSession) return PortalHospital();
                return HospitalCache.Get("hospital", () => ToModel(cnHelperObj.GetHospital(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Hospital));
            }
            lock (DataStore.Sync) return DataStore.Hospitals.FirstOrDefault(h => h.HospitalId == hospitalId);
        }

        private static Hospital ToModel(CFG_HospitalInfo h)
        {
            if (h == null) return null;
            return new Hospital
            {
                HospitalId = h.HospitalId, Name = h.Name, LegalAddress = h.Address, PrimaryEmail = h.Email, ContactNumber = h.Phone,
                Country = h.Country, Currency = h.Currency, Timezone = h.Timezone, PatientIdLabel = h.PatientIdLabel,
                NationalHealthIdLabel = h.NationalHealthIdLabel, NationalHealthIdMandatory = h.IsNationalHealthIdMandatory,
                GrowthChartStandard = h.GrowthChartStandard, DefaultRxValidityDays = h.DefaultRxValidityDays, LicenseNumber = h.LicenseNumber,
                DefaultTempUnit = h.DefaultTempUnit, RxPrefix = h.RxPrefix, ReceiptPrefix = h.ReceiptPrefix, InvoicePrefix = h.InvoicePrefix,
                LabChargePerTest = h.LabChargePerTest, LogoBase64 = h.LogoBase64,
            };
        }

        /// <summary>
        /// The portal hospital (PortalHospitalId in the service's configuration) for pages without a session, built from
        /// GetPortalBookingOptions (name, patient-id label, country, time zone). Cached application-wide for 5 minutes.
        /// Null when the service cannot be reached.
        /// </summary>
        public static Hospital PortalHospital()
        {
            const string key = "portal-hospital";
            var hit = System.Runtime.Caching.MemoryCache.Default.Get(key) as Hospital;
            if (hit != null) return hit;
            try
            {
                var o = SchedulingService.PortalOptions();
                if (o == null) return null;
                hit = new Hospital
                {
                    HospitalId = System.Configuration.ConfigurationManager.AppSettings["PortalHospitalId"], Name = o.Name,
                    PatientIdLabel = o.PatientIdLabel, Country = o.Country, Timezone = o.TimeZoneId,
                };
                System.Runtime.Caching.MemoryCache.Default.Set(key, hit, DateTimeOffset.UtcNow.AddMinutes(5));
                return hit;
            }
            catch (ApiException ex)
            {
                System.Diagnostics.Trace.TraceWarning("GetPortalBookingOptions failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Applies changes to the hospital profile. API mode: the changed profile is sent with UpdateHospital
        /// (Timezone must be one of ListTimeZones); the logo is sent only when it changed.
        /// </summary>
        public static void UpdateHospital(string hospitalId, Action<Hospital> apply)
        {
            if (Api)
            {
                var h = ToModel(cnHelperObj.GetHospital(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Hospital);
                var oldLogo = h.LogoBase64;
                apply(h);
                bool logoChanged = oldLogo != h.LogoBase64;
                cnHelperObj.UpdateHospital(new CFG_UpdateHospitalReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    Name = h.Name, LegalAddress = h.LegalAddress, ContactNumber = h.ContactNumber, PrimaryEmail = h.PrimaryEmail,
                    Country = h.Country, Currency = h.Currency, Timezone = h.Timezone, LicenseNumber = h.LicenseNumber,
                    PatientIdLabel = h.PatientIdLabel, NationalHealthIdLabel = h.NationalHealthIdLabel,
                    NationalHealthIdMandatory = h.NationalHealthIdMandatory, GrowthChartStandard = h.GrowthChartStandard,
                    DefaultTempUnit = h.DefaultTempUnit, DefaultRxValidityDays = h.DefaultRxValidityDays, RxPrefix = h.RxPrefix,
                    ReceiptPrefix = h.ReceiptPrefix, InvoicePrefix = h.InvoicePrefix, LabChargePerTest = h.LabChargePerTest,
                    LogoBase64 = logoChanged && !string.IsNullOrEmpty(h.LogoBase64) ? h.LogoBase64 : null,
                    RemoveLogo = logoChanged && string.IsNullOrEmpty(h.LogoBase64) ? true : (bool?)null,
                }).EnsureSuccess();
                HospitalCache.Clear("hospital");
                return;
            }
            lock (DataStore.Sync)
            {
                var h = DataStore.Hospitals.FirstOrDefault(x => x.HospitalId == hospitalId);
                if (h != null) apply(h);
            }
        }

        public static string GetSetting(string hospitalId, string key)
        {
            if (Api)
            {
                string v;
                return GetSettings(hospitalId).TryGetValue(key, out v) ? v : null;
            }
            lock (DataStore.Sync)
            {
                var r = DataStore.HospitalSettings.FirstOrDefault(x => x.HospitalId == hospitalId && x.SettingKey == key);
                return r != null ? r.Value : null;
            }
        }

        public static Dictionary<string, string> GetSettings(string hospitalId)
        {
            if (Api)
            {
                // No session (login, portal): the hospital's display formats are not readable, so the defaults apply.
                if (!HasApiSession) return new Dictionary<string, string>();
                return new Dictionary<string, string>(HospitalCache.Get("settings", () => ToDictionary(cnHelperObj.GetHospitalSettings(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Settings)));
            }
            lock (DataStore.Sync)
                return DataStore.HospitalSettings.Where(x => x.HospitalId == hospitalId).ToDictionary(x => x.SettingKey, x => x.Value);
        }

        /// <summary>Saves the given keys. MRN_SEQ is system-managed and never overwritten from the UI.</summary>
        public static void SaveSettings(string hospitalId, IDictionary<string, string> values)
        {
            if (Api)
            {
                // SaveHospitalSettings re-renders every doctor's name when DOCTOR_NAME_TEMPLATE changes.
                cnHelperObj.SaveHospitalSettings(new CFG_SaveHospitalSettingsReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    Values = values.Where(kv => kv.Key != "MRN_SEQ").Select(kv => new KeyValueInfo { Key = kv.Key, Value = kv.Value }).ToList(),
                }).EnsureSuccess();
                HospitalCache.Clear("settings");
                if (values.ContainsKey("DOCTOR_NAME_TEMPLATE")) { HospitalCache.Clear("doctors"); HospitalCache.Clear("users"); }
                return;
            }
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

        private static Dictionary<string, string> ToDictionary(IEnumerable<KeyValueInfo> rows)
        {
            var d = new Dictionary<string, string>();
            foreach (var kv in rows ?? Enumerable.Empty<KeyValueInfo>()) if (kv.Key != null) d[kv.Key] = kv.Value;
            return d;
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
                default: return value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
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
            if (Api)
                return (cnHelperObj.ListCompliances(AppSession.ApiHospitalId, AppSession.ApiUserId, country, AppSession.ApiHashValue).EnsureSuccess().Compliances ?? new List<CFG_ComplianceInfo>()).Select(c => new Compliance
                {
                    ComplianceId = c.ComplianceId, ComplianceName = c.ComplianceName, Country = c.Country, IsMandatory = c.IsMandatory,
                    DisplayLabel = c.DisplayLabel, PrintLabel = c.PrintLabel, SortOrder = c.SortOrder, IsActive = true,
                }).OrderBy(c => c.SortOrder).ToList();
            lock (DataStore.Sync)
                return DataStore.Compliances.Where(c => c.IsActive && c.Country == country).OrderBy(c => c.SortOrder).ToList();
        }

        public static HospitalCompliance GetHospitalCompliance(string hospitalId, string complianceId)
        {
            if (Api) return ApiHospitalCompliances().Select(ToModel).FirstOrDefault(hc => hc.ComplianceId == complianceId);
            lock (DataStore.Sync)
                return DataStore.HospitalCompliances.FirstOrDefault(hc => hc.HospitalId == hospitalId && hc.ComplianceId == complianceId && hc.IsActive);
        }

        private static List<CFG_HospitalComplianceInfo> ApiHospitalCompliances()
        {
            return HospitalCache.Get("hospital-compliances", () => cnHelperObj.ListHospitalCompliances(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().HospitalCompliances ?? new List<CFG_HospitalComplianceInfo>());
        }

        private static HospitalCompliance ToModel(CFG_HospitalComplianceInfo c)
        {
            return new HospitalCompliance
            {
                HospitalComplianceId = c.HospitalComplianceId, ComplianceId = c.ComplianceId, RegistrationNumber = c.RegistrationNumber,
                IssuedDate = DocNyle.ParseLocal(c.IssuedOn), ExpiryDate = DocNyle.ParseLocal(c.ExpiresOn), IsActive = true,
            };
        }

        public static void SaveHospitalCompliance(string hospitalId, string complianceId, string number, DateTime? issued, DateTime? expiry)
        {
            if (Api)
            {
                cnHelperObj.SaveHospitalCompliance(new CFG_SaveHospitalComplianceReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    ComplianceId = complianceId, RegistrationNumber = number, IssuedDate = DocNyle.Date(issued), ExpiryDate = DocNyle.Date(expiry),
                }).EnsureSuccess();
                HospitalCache.Clear("hospital-compliances");
                return;
            }
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
            if (Api)
                return string.Join(" · ", ApiHospitalCompliances().Where(c => !string.IsNullOrEmpty(c.RegistrationNumber))
                    .Select(c => (string.IsNullOrEmpty(c.PrintLabel) ? c.ComplianceName : c.PrintLabel) + ": " + c.RegistrationNumber));
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
            if (Api)
            {
                var rows = HospitalCache.Get("vitals", () => cnHelperObj.ListVitalsConfig(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Vitals ?? new List<CFG_VitalDefinitionInfo>());
                return rows.Where(v => !enabledOnly || v.IsEnabled).OrderBy(v => v.SortOrder).Select(v => new VitalConfig
                {
                    VitalDefinitionId = v.VitalDefinitionId, VitalCode = v.VitalCode, DisplayLabel = v.DisplayLabel, AllowedUnits = v.AllowedUnits,
                    IsEnabled = v.IsEnabled, SortOrder = v.SortOrder, IsCustom = (v.VitalCode ?? "").StartsWith("C_"),
                    MinValue = (double?)v.MinValue, MaxValue = (double?)v.MaxValue,
                }).ToList();
            }
            lock (DataStore.Sync)
                return DataStore.VitalsConfig.Where(v => v.HospitalId == hospitalId && (!enabledOnly || v.IsEnabled)).OrderBy(v => v.SortOrder).ToList();
        }

        /// <summary>
        /// A code for a new hospital-defined vital, derived from its label (e.g. "Blood glucose" → "C_BLOOD_GLUCOSE") and
        /// unique among the hospital's vitals and <paramref name="alsoTaken"/> (codes reserved by unpublished vitals).
        /// </summary>
        public static string NewCustomVitalCode(string hospitalId, string label, IEnumerable<string> alsoTaken)
        {
            var existing = Api ? ListVitalsConfig(hospitalId, false).Select(v => v.VitalCode).ToList() : null;
            lock (DataStore.Sync)
            {
                var taken = new HashSet<string>((existing ?? DataStore.VitalsConfig.Where(v => v.HospitalId == hospitalId).Select(v => v.VitalCode)).Concat(alsoTaken ?? new string[0]));
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
            if (Api)
            {
                var existing = ListVitalsConfig(hospitalId, false).FirstOrDefault(v => v.VitalCode == code);
                if (existing != null) return existing;
                cnHelperObj.CreateVitalConfig(new CFG_CreateVitalConfigReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    VitalCode = code, DisplayLabel = label, AllowedUnits = unit, MinValue = (decimal?)min, MaxValue = (decimal?)max,
                }).EnsureSuccess();
                if (!enabled) cnHelperObj.UpdateVitalEnabled(new CFG_UpdateVitalEnabledReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, VitalCode = code, IsEnabled = false }).EnsureSuccess();
                HospitalCache.Clear("vitals");
                return ListVitalsConfig(hospitalId, false).FirstOrDefault(x => x.VitalCode == code)
                    ?? new VitalConfig { VitalCode = code, DisplayLabel = label, AllowedUnits = unit, IsEnabled = enabled, IsCustom = true, MinValue = min, MaxValue = max };
            }
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
            if (Api)
            {
                var rows = activeOnly ? HospitalCache.Get("mh-items", () => cnHelperObj.ListMedicalHistoryItems(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Items)
                                      : HospitalCache.Get("mh-config", () => cnHelperObj.ListMedicalHistoryConfig(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Items);
                return (rows ?? new List<CFG_MedicalHistoryItemInfo>()).Select(i => new MedicalHistoryItem
                {
                    ItemId = i.MedicalHistoryItemId, ItemName = i.ItemName, IsQuickPick = i.IsQuickPick, SortOrder = i.SortOrder,
                    IsActive = activeOnly || i.IsActive,
                }).OrderBy(i => i.SortOrder).ToList();
            }
            lock (DataStore.Sync)
                return DataStore.MedicalHistoryItems.Where(i => i.HospitalId == hospitalId && (!activeOnly || i.IsActive)).OrderBy(i => i.SortOrder).ToList();
        }

        public static void AddMedicalHistoryItem(string hospitalId, string name)
        {
            if (Api)
            {
                if (ListMedicalHistoryItems(hospitalId, false).Any(i => string.Equals(i.ItemName, name, StringComparison.OrdinalIgnoreCase))) return;
                cnHelperObj.CreateMedicalHistoryConfigItem(new CFG_CreateMedicalHistoryConfigItemReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ItemName = name }).EnsureSuccess();
                HospitalCache.ClearPrefix("mh-");
                return;
            }
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
            if (Api)
            {
                // UpdateMedicalHistoryItemStatus is keyed by the item's name.
                var item = ListMedicalHistoryItems(null, false).FirstOrDefault(i => i.ItemId == itemId);
                if (item == null || item.IsActive == active) return;
                cnHelperObj.UpdateMedicalHistoryItemStatus(new CFG_UpdateMedicalHistoryItemStatusReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, ItemName = item.ItemName, IsActive = active }).EnsureSuccess();
                HospitalCache.ClearPrefix("mh-");
                return;
            }
            lock (DataStore.Sync)
            {
                var i = DataStore.MedicalHistoryItems.FirstOrDefault(x => x.ItemId == itemId);
                if (i != null) i.IsActive = active;
            }
        }

        public static List<MedicalEventItem> ListMedicalEventItems(string hospitalId)
        {
            if (Api)
                return HospitalCache.Get("me-items", () => cnHelperObj.ListMedicalEventItems(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().EventTypes ?? new List<CFG_MedicalEventTypeInfo>())
                    .Select(i => new MedicalEventItem { EventId = i.MedicalEventTypeId, EventName = i.EventTypeName, SortOrder = i.SortOrder, IsActive = true })
                    .OrderBy(i => i.SortOrder).ToList();
            lock (DataStore.Sync)
                return DataStore.MedicalEventItems.Where(i => i.HospitalId == hospitalId && i.IsActive).OrderBy(i => i.SortOrder).ToList();
        }

        public static List<Specialty> ListSpecialties(string hospitalId, bool activeOnly)
        {
            if (Api)
            {
                var rows = activeOnly ? HospitalCache.Get("specialties", () => cnHelperObj.ListSpecialties(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Specialties)
                                      : HospitalCache.Get("specialty-config", () => cnHelperObj.ListSpecialtyConfig(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Specialties);
                return (rows ?? new List<CFG_SpecialtyInfo>())
                    .Select(x => new Specialty { SpecialtyId = x.SpecialtyId, SpecialtyName = x.SpecialtyName, IsActive = activeOnly || x.IsActive }).ToList();
            }
            lock (DataStore.Sync)
                return DataStore.Specialties.Where(s => s.HospitalId == hospitalId && (!activeOnly || s.IsActive)).ToList();
        }

        public static string SpecialtyName(string specialtyId)
        {
            if (Api)
            {
                var s = ListSpecialties(null, true).FirstOrDefault(x => x.SpecialtyId == specialtyId);
                return s != null ? s.SpecialtyName : specialtyId;
            }
            lock (DataStore.Sync)
            {
                var s = DataStore.Specialties.FirstOrDefault(x => x.SpecialtyId == specialtyId);
                return s != null ? s.SpecialtyName : specialtyId;
            }
        }

        public static Specialty CreateSpecialty(string hospitalId, string name)
        {
            if (Api)
            {
                var x = cnHelperObj.CreateSpecialty(new CFG_CreateSpecialtyReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, SpecialtyName = name }).EnsureSuccess().Specialty;
                HospitalCache.ClearPrefix("specialt");
                return new Specialty { SpecialtyId = x.SpecialtyId, SpecialtyName = x.SpecialtyName, IsActive = x.IsActive };
            }
            lock (DataStore.Sync)
            {
                var row = new Specialty { SpecialtyId = DataStore.NextId("SPEC"), HospitalId = hospitalId, SpecialtyName = name, IsActive = true };
                DataStore.Specialties.Add(row);
                return row;
            }
        }

        public static void ToggleSpecialty(string specialtyId)
        {
            if (Api)
            {
                cnHelperObj.ToggleSpecialty(new CFG_ToggleSpecialtyReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, SpecialtyId = specialtyId }).EnsureSuccess();
                HospitalCache.ClearPrefix("specialt");
                return;
            }
            lock (DataStore.Sync)
            {
                var s = DataStore.Specialties.FirstOrDefault(x => x.SpecialtyId == specialtyId);
                if (s != null) s.IsActive = !s.IsActive;
            }
        }

        public static List<PaymentMethod> ListPaymentMethods(string hospitalId)
        {
            if (Api)
                return HospitalCache.Get("payment-methods", () => cnHelperObj.ListPaymentMethods(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().PaymentMethods ?? new List<CFG_PaymentMethodInfo>())
                    .Where(m => m.IsActive).Select(m => new PaymentMethod { PaymentMethodId = m.PaymentMethodId, MethodName = m.MethodName, IsActive = m.IsActive }).ToList();
            lock (DataStore.Sync) return DataStore.PaymentMethods.Where(m => m.HospitalId == hospitalId && m.IsActive).ToList();
        }

        /// <summary>
        /// Time zones a hospital may use (IANA id → label): ListTimeZones in API mode (each row's Windows id is registered
        /// with TimeZoneService), else TimeZoneService.HospitalZones.
        /// </summary>
        public static Dictionary<string, string> ListTimeZones()
        {
            if (!Api) return TimeZoneService.HospitalZones;
            var rows = HospitalCache.Get("time-zones", () => cnHelperObj.ListTimeZones(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().TimeZones ?? new List<CFG_TimeZoneInfo>());
            foreach (var z in rows) TimeZoneService.Register(z.TimeZoneId, z.WindowsTimeZoneId);
            return rows.Where(z => !string.IsNullOrEmpty(z.TimeZoneId)).GroupBy(z => z.TimeZoneId)
                .ToDictionary(g => g.Key, g => string.IsNullOrEmpty(g.First().DisplayName) ? g.Key : g.First().DisplayName);
        }
    }
}
