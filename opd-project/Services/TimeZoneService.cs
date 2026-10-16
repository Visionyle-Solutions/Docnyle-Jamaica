using System;
using System.Collections.Generic;
using System.Linq;

namespace OpdProject.Services
{
    /// <summary>
    /// Time handling rules (same as docs/schema.sql):
    ///  - Every point in time is held as UTC (DateTimeKind.Utc) — CreatedAt, PaidAt, SignedAt, Appointment.SlotStart …
    ///  - System events are shown in the viewer's own timezone (detected from the browser, see <see cref="ViewerZoneProvider"/>);
    ///    HospitalService.FormatDate / FormatTime do this automatically for any DateTimeKind.Utc value.
    ///  - Business events (appointments, visits) carry the clinic's TimeZoneId and are shown only in that zone, for every viewer.
    ///  - Calendar dates (expiry, leave, follow-up, "today") are dates in the hospital's timezone, DateTimeKind.Unspecified.
    /// Zones are IANA ids (America/Jamaica); .NET Framework on Windows needs the Windows id, mapped here.
    /// </summary>
    public static class TimeZoneService
    {
        public const string DefaultZoneId = "America/Jamaica";

        /// <summary>IANA id → Windows id. The hospital choices match cfg.TimeZone; the rest cover common viewer (browser) zones.</summary>
        private static readonly Dictionary<string, string> IanaToWindows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "America/Jamaica", "SA Pacific Standard Time" }, { "America/New_York", "Eastern Standard Time" },
            { "America/Chicago", "Central Standard Time" }, { "America/Denver", "Mountain Standard Time" },
            { "America/Phoenix", "US Mountain Standard Time" }, { "America/Los_Angeles", "Pacific Standard Time" },
            { "America/Anchorage", "Alaskan Standard Time" }, { "Pacific/Honolulu", "Hawaiian Standard Time" },
            { "America/Toronto", "Eastern Standard Time" }, { "America/Vancouver", "Pacific Standard Time" },
            { "America/Detroit", "Eastern Standard Time" }, { "America/Indiana/Indianapolis", "US Eastern Standard Time" },
            { "America/Panama", "SA Pacific Standard Time" }, { "America/Bogota", "SA Pacific Standard Time" },
            { "America/Cayman", "SA Pacific Standard Time" }, { "America/Port-au-Prince", "Haiti Standard Time" },
            { "America/Havana", "Cuba Standard Time" }, { "America/Nassau", "Eastern Standard Time" },
            { "America/Santo_Domingo", "SA Western Standard Time" }, { "America/Puerto_Rico", "SA Western Standard Time" },
            { "America/Port_of_Spain", "SA Western Standard Time" }, { "America/Barbados", "SA Western Standard Time" },
            { "America/Mexico_City", "Central Standard Time (Mexico)" }, { "America/Sao_Paulo", "E. South America Standard Time" },
            { "Europe/London", "GMT Standard Time" }, { "Europe/Dublin", "GMT Standard Time" }, { "Europe/Paris", "Romance Standard Time" },
            { "Europe/Berlin", "W. Europe Standard Time" }, { "Europe/Madrid", "Romance Standard Time" },
            { "Asia/Kolkata", "India Standard Time" }, { "Asia/Calcutta", "India Standard Time" }, { "Asia/Dubai", "Arabian Standard Time" },
            { "Asia/Singapore", "Singapore Standard Time" }, { "Asia/Tokyo", "Tokyo Standard Time" },
            { "Australia/Sydney", "AUS Eastern Standard Time" }, { "Africa/Lagos", "W. Central Africa Standard Time" },
            { "UTC", "UTC" }, { "Etc/UTC", "UTC" },
        };

        /// <summary>Zones offered as a hospital's timezone (same rows as cfg.TimeZone), IANA id → label.</summary>
        public static readonly Dictionary<string, string> HospitalZones = new Dictionary<string, string>
        {
            { "America/Jamaica", "Jamaica (UTC−05:00, no daylight saving)" }, { "America/New_York", "US Eastern (New York)" },
            { "America/Chicago", "US Central (Chicago)" }, { "America/Denver", "US Mountain (Denver)" },
            { "America/Los_Angeles", "US Pacific (Los Angeles)" }, { "America/Toronto", "Canada Eastern (Toronto)" },
            { "Europe/London", "United Kingdom (London)" }, { "Asia/Kolkata", "India (UTC+05:30)" }, { "UTC", "UTC" },
        };

        private static readonly Dictionary<string, TimeZoneInfo> Cache = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set by the web layer (Global.asax): the signed-in viewer's browser zone (IANA), or null when unknown.</summary>
        public static Func<string> ViewerZoneProvider { get; set; }

        public static bool IsKnown(string ianaId) { return !string.IsNullOrEmpty(ianaId) && IanaToWindows.ContainsKey(ianaId); }

        public static TimeZoneInfo Find(string ianaId)
        {
            if (!IsKnown(ianaId)) ianaId = DefaultZoneId;
            lock (Cache)
            {
                TimeZoneInfo tz;
                if (!Cache.TryGetValue(ianaId, out tz))
                {
                    try { tz = TimeZoneInfo.FindSystemTimeZoneById(IanaToWindows[ianaId]); }
                    catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; }
                    Cache[ianaId] = tz;
                }
                return tz;
            }
        }

        /* ── Hospital zone ── */

        public static string HospitalZoneId(string hospitalId)
        {
            var h = string.IsNullOrEmpty(hospitalId) ? null : HospitalService.GetHospital(hospitalId);
            return h != null && IsKnown(h.Timezone) ? h.Timezone : DefaultZoneId;
        }

        /// <summary>The hospital's current calendar date.</summary>
        public static DateTime Today(string hospitalId) { return Now(hospitalId).Date; }

        /// <summary>The hospital's current wall-clock time (DateTimeKind.Unspecified).</summary>
        public static DateTime Now(string hospitalId) { return ToZone(DateTime.UtcNow, HospitalZoneId(hospitalId)); }

        /* ── Viewer zone ── */

        /// <summary>The viewer's browser zone when known, else the hospital's.</summary>
        public static string ViewerZoneId(string hospitalId)
        {
            var viewer = ViewerZoneProvider != null ? SafeViewerZone() : null;
            return IsKnown(viewer) ? viewer : HospitalZoneId(hospitalId);
        }

        private static string SafeViewerZone()
        {
            try { return ViewerZoneProvider(); } catch { return null; }
        }

        /* ── Conversion ── */

        /// <summary>UTC instant → wall-clock time in the zone (DateTimeKind.Unspecified). Unspecified input is taken as UTC.</summary>
        public static DateTime ToZone(DateTime utc, string ianaId)
        {
            var u = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(u, Find(ianaId)), DateTimeKind.Unspecified);
        }

        /// <summary>Wall-clock time in the zone → UTC instant (DateTimeKind.Utc). A time skipped by a daylight-saving jump moves forward an hour.</summary>
        public static DateTime ToUtc(DateTime local, string ianaId)
        {
            var tz = Find(ianaId);
            var l = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (tz.IsInvalidTime(l)) l = l.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(l, tz);
        }

        /// <summary>The hospital-local calendar date of a UTC instant.</summary>
        public static DateTime LocalDate(DateTime utc, string ianaId) { return ToZone(utc, ianaId).Date; }

        /// <summary>True when the two zones show different wall-clock times at this instant.</summary>
        public static bool DiffersAt(DateTime utc, string zoneA, string zoneB)
        {
            return Find(zoneA).GetUtcOffset(utc) != Find(zoneB).GetUtcOffset(utc);
        }

        /// <summary>"Jamaica time", "New York time", "UTC".</summary>
        public static string ShortName(string ianaId)
        {
            if (string.IsNullOrEmpty(ianaId)) ianaId = DefaultZoneId;
            if (ianaId.EndsWith("UTC", StringComparison.OrdinalIgnoreCase)) return "UTC";
            return ianaId.Split('/').Last().Replace('_', ' ') + " time";
        }
    }
}
