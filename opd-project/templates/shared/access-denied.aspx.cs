using OpdProject.Web;

namespace OpdProject.templates.shared
{
    /// <summary>Shown when the signed-in role lacks the permission a page requires (replaces the router's inline guard).</summary>
    public partial class access_denied : BasePage
    {
        public override string PageTitle { get { return "Access denied"; } }

        protected string RequestedPage
        {
            get { return string.IsNullOrEmpty(Request.QueryString["page"]) ? "this page" : "\"" + Request.QueryString["page"] + "\""; }
        }
    }
}
