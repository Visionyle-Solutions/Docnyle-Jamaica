using System;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.UI;
using OpdProject.Models;
using OpdProject.Services;

namespace OpdProject.Web
{
    /// <summary>
    /// Base class for every application page. Enforces sign-in and the role permission matrix
    /// (the server-side replacement for router.js's session and permission guards), and offers
    /// the shared context and formatting helpers pages need.
    /// </summary>
    public class BasePage : Page
    {
        /// <summary>Permission-matrix feature required to open the page (null = any signed-in user).</summary>
        protected virtual string RequiredPermission { get { return null; } }

        /// <summary>Public pages (login, patient portal) skip the sign-in check.</summary>
        protected virtual bool AllowAnonymous { get { return false; } }

        /// <summary>Page heading shown in the browser title.</summary>
        public virtual string PageTitle { get { return "Consult360"; } }

        protected AppUser CurrentUser { get; private set; }
        protected string UserId { get { return CurrentUser != null ? CurrentUser.UserId : null; } }
        /// <summary>True when the signed-in user holds this role (a user may hold several).</summary>
        protected bool HasRole(string role) { return CurrentUser != null && CurrentUser.HasRole(role); }
        /// <summary>Doctor-only users see just their own patients/appointments; see AppUser.SeesOwnPatientsOnly.</summary>
        protected bool SeesOwnPatientsOnly { get { return CurrentUser != null && CurrentUser.SeesOwnPatientsOnly; } }

        /// <summary>One coloured badge per role, for user lists.</summary>
        protected static string RoleBadges(AppUser u, string fontSize)
        {
            return string.Join(" ", u.Roles.Select(r => "<span class=\"badge " + (r == "ADMIN" ? "badge--danger" : r == "DOCTOR" ? "badge--success" : "badge--info")
                + "\" style=\"font-size:" + fontSize + ";\">" + HttpUtility.HtmlEncode(r) + "</span>"));
        }

        /// <summary>Signed-in user's hospital; for anonymous pages, the first (demo) hospital.</summary>
        protected string HospitalId { get { return CurrentUser != null ? CurrentUser.HospitalId : "HOSP-001"; } }

        private Hospital _hospital;
        protected Hospital Hospital { get { return _hospital ?? (_hospital = HospitalService.GetHospital(HospitalId)); } }

        protected override void OnPreInit(EventArgs e)
        {
            base.OnPreInit(e);
            CurrentUser = AppSession.CurrentUser;
            /* endResponse: true — nothing on a guarded page may run for an unauthorised request */
            if (CurrentUser == null && !AllowAnonymous)
                Response.Redirect(Urls.Login + "?returnUrl=" + HttpUtility.UrlEncode(Request.RawUrl), true);
            if (CurrentUser != null && !UserService.HasPermission(CurrentUser, RequiredPermission))
                Response.Redirect(Urls.To("access-denied") + "?page=" + HttpUtility.UrlEncode(PageTitle), true);
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            Title = PageTitle + " · Consult360";
        }

        protected bool Can(string feature) { return UserService.HasPermission(CurrentUser, feature); }

        /* ── Route / query values ── */

        /// <summary>Friendly-route value (e.g. {patientId}), falling back to the query string of the same name.</summary>
        protected string RouteValue(string name)
        {
            object v;
            if (RouteData != null && RouteData.Values.TryGetValue(name, out v) && v != null) return v.ToString();
            return Request.QueryString[name];
        }

        /* ── Toasts ── */

        public void ToastSuccess(string title, string message = null) { AppSession.Toast("success", title, message); }
        public void ToastWarning(string title, string message = null) { AppSession.Toast("warning", title, message); }
        public void ToastDanger(string title, string message = null) { AppSession.Toast("danger", title, message); }
        public void ToastInfo(string title, string message = null) { AppSession.Toast("info", title, message); }

        /// <summary>Post/Redirect/Get: redirect to the current URL so a refresh doesn't resubmit.</summary>
        protected void ReloadPage(string query = null)
        {
            Go(Request.Url.AbsolutePath + (string.IsNullOrEmpty(query) ? "" : "?" + query));
        }

        /// <summary>Redirects and ends the request (queued toasts are rendered by the next page).</summary>
        protected void Go(string url)
        {
            Response.Redirect(url, true);
        }

        protected void Audit(string action, string entityType, string entityId, string details = null)
        {
            AuditService.Record(HospitalId, UserId, action, entityType, entityId, details);
        }

        /* ── Formatting helpers (follow Settings → Hospital & Practice → Formats) ── */

        public string FmtDate(object value)
        {
            if (value == null) return "";
            return HospitalService.FormatDate(HospitalId, (DateTime)value);
        }

        public string FmtTime(object value)
        {
            if (value == null) return "";
            return HospitalService.FormatTime(HospitalId, (DateTime)value);
        }

        public string FmtDateTime(object value)
        {
            if (value == null) return "";
            return FmtDate(value) + " " + FmtTime(value);
        }

        /* ── Time zones (see TimeZoneService) ── */

        /// <summary>The hospital's current calendar date — use instead of DateTime.Today.</summary>
        public DateTime Today { get { return TimeZoneService.Today(HospitalId); } }

        /// <summary>The hospital's current wall-clock time.</summary>
        public DateTime HospitalNow { get { return TimeZoneService.Now(HospitalId); } }

        /// <summary>The viewer's timezone (browser, else the hospital's).</summary>
        public string ViewerZoneId { get { return TimeZoneService.ViewerZoneId(HospitalId); } }

        /// <summary>
        /// A business event's time (appointment, visit) in the clinic's timezone only, e.g. "9:00 AM" — the same for every
        /// viewer, wherever they are. <paramref name="value"/> is a UTC instant, or a wall-clock time already in <paramref name="zoneId"/>.
        /// </summary>
        public string EventTime(DateTime value, string zoneId)
        {
            zoneId = TimeZoneService.IsKnown(zoneId) ? zoneId : TimeZoneService.HospitalZoneId(HospitalId);
            return FmtTime(value.Kind == DateTimeKind.Utc ? TimeZoneService.ToZone(value, zoneId) : value);
        }

        /// <summary><see cref="EventTime(DateTime, string)"/> for an appointment row.</summary>
        public string EventTime(AppointmentView a) { return a == null ? "" : EventTime(a.Appointment.SlotStart, a.Appointment.TimeZoneId); }

        /// <summary>One-line coverage identifiers: card number for a govt scheme (NHF …), member/policy/group and claim mode for private cover, plus validity.</summary>
        protected string CoverageIdent(PatientCoverage c)
        {
            var scheme = PatientService.GetGovtSchemeProfile(Hospital.Country);
            var ident = c.CoverageType == "GOVT_SCHEME"
                ? scheme.CardLabel + ": " + (string.IsNullOrEmpty(c.GovtCardNumber) ? "—" : c.GovtCardNumber)
                : "Member: " + (string.IsNullOrEmpty(c.MemberId) ? "—" : c.MemberId) + " · Policy: " + (string.IsNullOrEmpty(c.PolicyNumber) ? "—" : c.PolicyNumber)
                  + (string.IsNullOrEmpty(c.GroupNumber) ? "" : " · Group: " + c.GroupNumber) + " · " + TitleCase(c.ClaimMode);
            if (c.EffectiveDate.HasValue || c.ExpiryDate.HasValue) ident += " · Valid " + (c.EffectiveDate.HasValue ? FmtDate(c.EffectiveDate.Value) : "—") + " → " + (c.ExpiryDate.HasValue ? FmtDate(c.ExpiryDate.Value) : "—");
            return ident;
        }

        /// <summary>Stored codes (DRUG, ACTIVE, CHECKED_IN) shown as Title Case ("Checked In").</summary>
        public static string TitleCase(string code)
        {
            return string.IsNullOrEmpty(code) ? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(code.Replace('_', ' ').ToLowerInvariant());
        }

        /// <summary>Currency code → display symbol. Unknown codes are shown as the code itself.</summary>
        public static string CurrencySymbol(string code)
        {
            switch ((code ?? "").Trim().ToUpperInvariant())
            {
                case "JMD": return "J$";
                case "INR": return "₹";
                case "USD": return "$";
                case "GBP": return "£";
                default: return (code ?? "").Trim();
            }
        }

        public string Money(decimal amount)
        {
            return Money(amount, null);
        }

        public string Money(decimal amount, string currency)
        {
            var code = (currency ?? Hospital.Currency ?? "").Trim();
            var symbol = CurrencySymbol(code);
            // symbols attach to the number (J$300.00); a bare code such as AED keeps a space (AED 300.00)
            var prefix = symbol == code && code.Length > 0 ? code + " " : symbol;
            return prefix + amount.ToString("N2", CultureInfo.InvariantCulture);
        }

        public string PatientIdLabel { get { return string.IsNullOrEmpty(Hospital.PatientIdLabel) ? "MRN" : Hospital.PatientIdLabel; } }
        public string HealthIdLabel { get { return string.IsNullOrEmpty(Hospital.NationalHealthIdLabel) ? "National ID" : Hospital.NationalHealthIdLabel; } }

        public static string StatusBadge(string status)
        {
            switch (status)
            {
                case "COMPLETED": case "PAID": case "REVIEWED": case "SENT": case "RESULTS_READY": case "ACTIVE": return "badge--success";
                case "CANCELLED": case "NO_SHOW": case "FAILED": case "INACTIVE": case "CRITICAL": return "badge--danger";
                case "CHECKED_IN": case "IN_CONSULT": case "PARTIAL": case "SIMULATED": case "PENDING_REVIEW": return "badge--warning";
                default: return "badge--info";
            }
        }

        public static string StatusLabel(string status)
        {
            return SchedulingService.StatusLabels.ContainsKey(status ?? "") ? SchedulingService.StatusLabels[status] : status;
        }

        /// <summary>Appointment type name for a code (FOLLOWUP → "Follow-Up").</summary>
        public string AppointmentTypeName(string code)
        {
            var t = SchedulingService.GetAppointmentType(HospitalId, code);
            return t != null ? t.Name : code;
        }

        /// <summary>Parses a yyyy-MM-dd (HTML date input) value.</summary>
        protected static DateTime? ParseDate(string value)
        {
            DateTime d;
            return DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }

        protected static string IsoDate(DateTime? value) { return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : ""; }

        protected static decimal ParseMoney(string value)
        {
            decimal d;
            return decimal.TryParse((value ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        protected static int ParseInt(string value, int fallback)
        {
            int i;
            return int.TryParse((value ?? "").Trim(), out i) ? i : fallback;
        }
    }
}