using System;
using System.Globalization;
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
        protected string Role { get { return CurrentUser != null ? CurrentUser.Role : null; } }

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
            if (CurrentUser != null && !UserService.HasPermission(CurrentUser.Role, RequiredPermission))
                Response.Redirect(Urls.To("access-denied") + "?page=" + HttpUtility.UrlEncode(PageTitle), true);
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            Title = PageTitle + " · Consult360";
        }

        protected bool Can(string feature) { return CurrentUser != null && UserService.HasPermission(CurrentUser.Role, feature); }

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

        public string Money(decimal amount)
        {
            return (Hospital.Currency ?? "") + " " + amount.ToString("N2", CultureInfo.InvariantCulture);
        }

        public string Money(decimal amount, string currency)
        {
            return (currency ?? Hospital.Currency ?? "") + " " + amount.ToString("N2", CultureInfo.InvariantCulture);
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
