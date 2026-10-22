using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Security;
using Newtonsoft.Json;
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
        private const string UserKey = "DocNyle.UserId";
        private const string ToastKey = "DocNyle.Toasts";

        private static HttpContext Ctx { get { return HttpContext.Current; } }

        private const string ApiUserKey = "DocNyle.ApiUser";
        private const string TokenKey = "DocNyle.ServiceToken";

        /// <summary>
        /// Encrypted, signed copy of the sign-in (machineKey), so a session lost to an app-pool recycle or another
        /// worker process is restored instead of sending the user to the login page. Same idle timeout as the session.
        /// </summary>
        private const string AuthCookie = "c360auth";

        /* What the sign-in cookie carries (AppUser.Role is derived from Roles, so the user is not serialized whole) */
        private class SignInData
        {
            public string Token;
            public string UserId, HospitalId, FullName, Email, Phone, SpecialtyId, LicenseNumber, LabPartnerId, Qualifications;
            public List<string> Roles;
        }

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
                RestoreFromCookie();
                if (DataSource.IsApi("Users"))
                {
                    var stored = ApiUser;
                    if (stored == null) return null;
                    if (!UserService.IsSessionValid())
                    {
                        /* Signing in again (any browser or device) ends the user's other sessions (sec.usp_CreateUserSession) */
                        SignOut();
                        Toast("warning", "Signed out", "This account was signed in somewhere else, or the session expired. Please sign in again.");
                        return null;
                    }
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
            get { RestoreFromCookie(); return Ctx != null && Ctx.Session != null ? Ctx.Session[ApiUserKey] as AppUser : null; }
        }

        /// <summary>The token returned by CreateUserSession, sent as the SessionToken header on every later API call.</summary>
        public static string ServiceToken
        {
            get { RestoreFromCookie(); return Ctx != null && Ctx.Session != null ? Ctx.Session[TokenKey] as string : null; }
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
            WriteAuthCookie();
        }

        public static void SignOut()
        {
            if (Ctx == null || Ctx.Session == null) return;
            Ctx.Session.Remove(UserKey);
            Ctx.Session.Remove(ApiUserKey);
            Ctx.Session.Remove(TokenKey);
            Ctx.Request.Cookies.Remove(AuthCookie);   // nothing later in this request restores it
            Ctx.Response.Cookies.Add(new HttpCookie(AuthCookie, "") { Path = "/", HttpOnly = true, Expires = DateTime.UtcNow.AddYears(-1) });
        }

        /// <summary>
        /// Called on every page request of a signed-in user: re-issues the sign-in cookie so it slides like the session.
        /// Background calls (notifications.ashx) don't, so an idle tab still times out.
        /// </summary>
        public static void KeepAlive()
        {
            if (Ctx != null && Ctx.Session != null && Ctx.Session[UserKey] != null) WriteAuthCookie();
        }

        private static void WriteAuthCookie()
        {
            var userId = Ctx.Session[UserKey] as string;
            if (userId == null) return;
            var u = Ctx.Session[ApiUserKey] as AppUser;
            var data = u == null ? "" : JsonConvert.SerializeObject(new SignInData
            {
                Token = Ctx.Session[TokenKey] as string,
                UserId = u.UserId, HospitalId = u.HospitalId, FullName = u.FullName, Email = u.Email, Phone = u.Phone,
                SpecialtyId = u.SpecialtyId, LicenseNumber = u.LicenseNumber, LabPartnerId = u.LabPartnerId,
                Qualifications = u.Qualifications, Roles = u.Roles,
            });
            var now = DateTime.Now;
            var ticket = new FormsAuthenticationTicket(1, userId, now, now.AddMinutes(Ctx.Session.Timeout), false, data, "/");
            Ctx.Response.Cookies.Set(new HttpCookie(AuthCookie, FormsAuthentication.Encrypt(ticket))
            {
                Path = "/", HttpOnly = true, Secure = Ctx.Request.IsSecureConnection, SameSite = SameSiteMode.Lax,
            });
        }

        /// <summary>Puts the sign-in back into an empty session from a valid, unexpired sign-in cookie.</summary>
        private static void RestoreFromCookie()
        {
            if (Ctx == null || Ctx.Session == null || Ctx.Session[UserKey] != null) return;
            var c = Ctx.Request.Cookies[AuthCookie];
            if (c == null || string.IsNullOrEmpty(c.Value)) return;
            FormsAuthenticationTicket t;
            try { t = FormsAuthentication.Decrypt(c.Value); }
            catch (Exception) { return; }   // tampered, or encrypted with another machineKey
            if (t == null || t.Expired || string.IsNullOrEmpty(t.Name)) return;
            if (!string.IsNullOrEmpty(t.UserData))
            {
                SignInData d;
                try { d = JsonConvert.DeserializeObject<SignInData>(t.UserData); }
                catch (JsonException) { return; }
                if (d == null || string.IsNullOrEmpty(d.Token)) return;
                Ctx.Session[ApiUserKey] = new AppUser
                {
                    UserId = d.UserId, HospitalId = d.HospitalId, FullName = d.FullName, Email = d.Email, Phone = d.Phone,
                    SpecialtyId = d.SpecialtyId, LicenseNumber = d.LicenseNumber, LabPartnerId = d.LabPartnerId,
                    Qualifications = d.Qualifications, Roles = d.Roles, IsActive = true,
                };
                Ctx.Session[TokenKey] = d.Token;
            }
            Ctx.Session[UserKey] = t.Name;
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
