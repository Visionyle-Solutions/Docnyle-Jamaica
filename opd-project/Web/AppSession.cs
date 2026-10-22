using System.Collections.Generic;
using System.Web;
using OpdProject.Models;
using OpdProject.Services;

namespace OpdProject.Web
{
    /// <summary>A toast notification queued on the server and rendered by Site.Master.</summary>
    public class ToastMessage
    {
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// The signed-in user's context, kept in ASP.NET session state (replaces the old
    /// client-side SessionManager/AppContext). Session timeout is configured in Web.config.
    /// </summary>
    public static class AppSession
    {
        private const string UserKey = "Consult360.UserId";
        private const string ToastKey = "Consult360.Toasts";

        private static HttpContext Ctx { get { return HttpContext.Current; } }

        private const string ApiUserKey = "Consult360.ApiUser";
        private const string TokenKey = "Consult360.ServiceToken";

        /// <summary>
        /// The signed-in user. In-memory mode re-reads it each request so a disabled account loses access immediately;
        /// API mode returns the user stored at sign-in after a CheckSessionValid (cached 60 s), so a disabled account
        /// is cut off once its sec.UserSession is revoked.
        /// </summary>
        public static AppUser CurrentUser
        {
            get
            {
                if (Ctx == null || Ctx.Session == null) return null;
                if (DataSource.IsApi("Users"))
                {
                    var stored = ApiUser;
                    if (stored == null) return null;
                    if (!UserService.IsSessionValid()) { SignOut(); return null; }
                    return stored;
                }
                var id = Ctx.Session[UserKey] as string;
                if (id == null) return null;
                var user = UserService.GetUser(id);
                return user != null && user.IsActive ? user : null;
            }
        }

        /// <summary>The user returned by AuthenticateUser (API mode), without re-checking the session.</summary>
        public static AppUser ApiUser
        {
            get { return Ctx != null && Ctx.Session != null ? Ctx.Session[ApiUserKey] as AppUser : null; }
        }

        /// <summary>The token returned by CreateUserSession, sent as the SessionToken header on every later API call.</summary>
        public static string ServiceToken
        {
            get { return Ctx != null && Ctx.Session != null ? Ctx.Session[TokenKey] as string : null; }
        }

        /* ── The caller's HospitalId / UserId / hashValue sent with every signed-in API call (CommonHelper) ── */

        /// <summary>HospitalId of the API session user; an Unauthenticated fault when there is no API session.</summary>
        public static string ApiHospitalId { get { return RequireApiUser().HospitalId; } }

        /// <summary>UserId of the API session user; an Unauthenticated fault when there is no API session.</summary>
        public static string ApiUserId { get { return RequireApiUser().UserId; } }

        /// <summary>hashValue = SHA-256(HospitalId + UserId).</summary>
        public static string ApiHashValue
        {
            get { var u = RequireApiUser(); return Helpers.CommonHelper.GetSHA265(u.HospitalId + u.UserId); }
        }

        private static AppUser RequireApiUser()
        {
            var u = ApiUser;
            if (u == null || string.IsNullOrEmpty(ServiceToken)) throw new ApiException(ApiFaultKind.Unauthenticated, null, "INVALID SESSION!");
            return u;
        }

        public static bool IsSignedIn { get { return CurrentUser != null; } }

        /// <summary>The browser's timezone (IANA id) from the c360tz cookie set by Site.Master, or null.</summary>
        public static string BrowserTimeZone
        {
            get
            {
                var c = Ctx != null ? Ctx.Request.Cookies["c360tz"] : null;
                return c != null && !string.IsNullOrEmpty(c.Value) ? HttpUtility.UrlDecode(c.Value) : null;
            }
        }

        public static void SignIn(AppUser user, string serviceToken = null)
        {
            Ctx.Session[UserKey] = user.UserId;
            if (serviceToken != null)
            {
                Ctx.Session[ApiUserKey] = user;
                Ctx.Session[TokenKey] = serviceToken;
            }
        }

        public static void SignOut()
        {
            if (Ctx == null || Ctx.Session == null) return;
            Ctx.Session.Remove(UserKey);
            Ctx.Session.Remove(ApiUserKey);
            Ctx.Session.Remove(TokenKey);
        }

        public static bool Can(string feature)
        {
            var u = CurrentUser;
            return UserService.HasPermission(u, feature);
        }

        /// <summary>Queues a toast. It survives a redirect because it lives in session until rendered.</summary>
        public static void Toast(string kind, string title, string message)
        {
            if (Ctx == null || Ctx.Session == null) return;
            var list = Ctx.Session[ToastKey] as List<ToastMessage>;
            if (list == null)
            {
                list = new List<ToastMessage>();
                Ctx.Session[ToastKey] = list;
            }
            // The same failure reported by several calls on one page is shown once.
            if (list.Exists(t => t.Kind == kind && t.Title == title && t.Message == message)) return;
            list.Add(new ToastMessage { Kind = kind, Title = title, Message = message });
        }

        /// <summary>Returns and clears the queued toasts.</summary>
        public static List<ToastMessage> TakeToasts()
        {
            if (Ctx == null || Ctx.Session == null) return new List<ToastMessage>();
            var list = Ctx.Session[ToastKey] as List<ToastMessage> ?? new List<ToastMessage>();
            Ctx.Session.Remove(ToastKey);
            return list;
        }
    }
}
