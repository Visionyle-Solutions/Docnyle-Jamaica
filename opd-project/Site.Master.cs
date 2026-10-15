using System;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject
{
    /// <summary>
    /// Application shell (replaces index.html + layout.js): permission-filtered sidebar, top bar with the
    /// hospital name and signed-in user, the page content area and the server-queued toast stack.
    /// </summary>
    public partial class SiteMaster : System.Web.UI.MasterPage
    {
        /// <summary>Pages such as the login screen, patient portal and queue display render without sidebar/top bar.</summary>
        public bool HideChrome { get; set; }

        protected string CurrentPath { get { return Request.Url.AbsolutePath; } }

        protected string ShellCss { get { return HideChrome || !AppSession.IsSignedIn ? "no-chrome" : ""; } }

        /// <summary>Static asset URL with a last-modified version stamp, so browsers re-fetch a CSS/JS file after it changes.</summary>
        public string Asset(string virtualPath)
        {
            var file = Server.MapPath(virtualPath);
            var version = System.IO.File.Exists(file) ? System.IO.File.GetLastWriteTimeUtc(file).Ticks.ToString("x") : "0";
            return ResolveUrl(virtualPath) + "?v=" + version;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            var user = AppSession.CurrentUser;
            ChromeSidebar.Visible = user != null && !HideChrome;
            bool bell = user != null && UserService.HasPermission(user, "consultation");
            if (bell && HideChrome) FloatingBell.Text = BellHtml(user.UserId, true);
            if (!ChromeSidebar.Visible) return;
            if (bell) TopbarBell.Text = BellHtml(user.UserId, false);

            var hospital = HospitalService.GetHospital(user.HospitalId);
            HospitalName.Text = HttpUtility.HtmlEncode(hospital != null ? hospital.Name : "");
            if (hospital != null && !string.IsNullOrEmpty(hospital.LogoBase64))
                HospitalLogo.Text = "<img src=\"" + HttpUtility.HtmlAttributeEncode(hospital.LogoBase64) + "\" alt=\"\" style=\"max-height:28px; margin-right:10px;\" />";
            UserBadge.Text = HttpUtility.HtmlEncode(user.FullName + " · " + user.RolesLabel) + " · ";

            NavSections.DataSource = Navigation.ForUser(user, CurrentPath);
            NavSections.DataBind();
        }

        /// <summary>Notification bell; site.js fills the list and polls for new notifications.</summary>
        private string BellHtml(string userId, bool floating)
        {
            int unread = UserNotificationService.UnreadCount(userId);
            return "<div class=\"notif" + (floating ? " notif--floating" : "") + "\" data-notif data-endpoint=\"" + HttpUtility.HtmlAttributeEncode(ResolveUrl("~/notifications.ashx")) + "\">"
                + "<button type=\"button\" class=\"notif-bell\" data-action=\"toggle-notif\" aria-haspopup=\"true\" aria-expanded=\"false\" aria-label=\"Notifications\" title=\"Notifications\">"
                + "<i class=\"ti ti-bell\" aria-hidden=\"true\"></i><span class=\"notif-count\" data-notif-count" + (unread == 0 ? " hidden" : "") + ">" + (unread > 9 ? "9+" : unread.ToString()) + "</span></button>"
                + "<div class=\"notif-panel\" data-notif-panel hidden role=\"dialog\" aria-label=\"Notifications\">"
                + "<div class=\"notif-panel-head\"><strong>Notifications</strong><button type=\"button\" class=\"btn btn--xs btn--ghost\" data-action=\"notif-read-all\">Mark all read</button></div>"
                + "<div class=\"notif-list\" data-notif-list><div class=\"notif-empty\">Loading…</div></div>"
                + "<a class=\"notif-panel-foot\" href=\"" + HttpUtility.HtmlAttributeEncode(Urls.To("lab-results")) + "\">Open Lab Results inbox</a>"
                + "</div></div>";
        }

        protected void NavSections_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var section = e.Item.DataItem as NavSection;
            if (section == null) return;
            var items = (Repeater)e.Item.FindControl("NavItems");
            items.DataSource = section.Items;
            items.DataBind();
        }

        protected void SignOutButton_Click(object sender, EventArgs e)
        {
            var user = AppSession.CurrentUser;
            if (user != null) AuditService.Record(user.HospitalId, user.UserId, "LOGOUT", "Session", Session.SessionID, "Signed out");
            AppSession.SignOut();
            Response.Redirect(Urls.Login, true);
        }

        /// <summary>Toasts are bound last so messages queued by this request's event handlers are included.</summary>
        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            Toasts.DataSource = AppSession.TakeToasts();
            Toasts.DataBind();
        }
    }
}
