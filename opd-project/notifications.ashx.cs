using System.Linq;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.SessionState;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject
{
    /// <summary>
    /// Notification bell endpoint for the signed-in user (site.js polls it):
    ///   GET                    → { unread, items[] } as JSON
    ///   GET ?open={id}         → marks that notification read and redirects to what it points at
    ///   POST action=readall    → marks everything read, returns the JSON (requires X-Requested-With, so other sites can't trigger it)
    /// </summary>
    public class notifications : IHttpHandler, IRequiresSessionState
    {
        public bool IsReusable { get { return true; } }

        public void ProcessRequest(HttpContext context)
        {
            var res = context.Response;
            res.Cache.SetCacheability(HttpCacheability.NoCache);
            res.Cache.SetNoStore();
            var user = AppSession.CurrentUser;
            var open = context.Request.QueryString["open"];

            if (user == null)
            {
                if (open != null) { res.Redirect(Urls.Login, false); return; }
                res.StatusCode = 401;
                return;
            }

            if (open != null)
            {
                var n = UserNotificationService.MarkRead(open, user.UserId);
                var url = n != null && n.Url != null && n.Url.StartsWith("/") && !n.Url.StartsWith("//") ? n.Url : Navigation.LandingFor(user.Role);
                res.Redirect(url, false);
                return;
            }

            if (context.Request.HttpMethod == "POST")
            {
                if (context.Request.Headers["X-Requested-With"] != "fetch") { res.StatusCode = 400; return; }
                if (context.Request.Form["action"] == "readall") UserNotificationService.MarkAllRead(user.UserId);
            }

            var items = UserNotificationService.ListRecent(user.UserId, 15).Select(n => new
            {
                id = n.NotificationId, title = n.Title, message = n.Message, critical = n.IsCritical,
                read = n.ReadAt != null, ago = UserNotificationService.Ago(n.CreatedAt),
            }).ToList();
            res.ContentType = "application/json";
            res.Write(new JavaScriptSerializer().Serialize(new { unread = UserNotificationService.UnreadCount(user.UserId), items = items }));
        }
    }
}
