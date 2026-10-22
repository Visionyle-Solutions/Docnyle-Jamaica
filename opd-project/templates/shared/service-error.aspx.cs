using OpdProject.Web;

namespace OpdProject.templates.shared
{
    /// <summary>Shown when a page could not load its data from the API (service unreachable or the request refused).</summary>
    public partial class service_error : BasePage
    {
        public override string PageTitle { get { return "Service unavailable"; } }

        protected override bool AllowAnonymous { get { return true; } }

        /// <summary>The page that failed (local URLs only).</summary>
        protected string RetryUrl
        {
            get
            {
                var r = Request.QueryString["returnUrl"];
                return !string.IsNullOrEmpty(r) && r.StartsWith("/") && !r.StartsWith("//") ? r : HomeUrl;
            }
        }

        protected string HomeUrl { get { return CurrentUser != null ? Navigation.LandingFor(CurrentUser) : Urls.Login; } }
    }
}
