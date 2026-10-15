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

        /// <summary>The signed-in user, re-read from the store each request so a disabled account loses access immediately.</summary>
        public static AppUser CurrentUser
        {
            get
            {
                if (Ctx == null || Ctx.Session == null) return null;
                var id = Ctx.Session[UserKey] as string;
                if (id == null) return null;
                var user = UserService.GetUser(id);
                return user != null && user.IsActive ? user : null;
            }
        }

        public static bool IsSignedIn { get { return CurrentUser != null; } }

        public static void SignIn(AppUser user)
        {
            Ctx.Session[UserKey] = user.UserId;
        }

        public static void SignOut()
        {
            if (Ctx.Session != null) Ctx.Session.Remove(UserKey);
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
