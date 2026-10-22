using System;
using System.Configuration;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>
    /// Sign-in: verifies credentials server-side (API mode: AuthenticateUser + CreateUserSession), starts the session,
    /// audits the login and routes by role.
    /// </summary>
    public partial class login : BasePage
    {
        protected override bool AllowAnonymous { get { return true; } }
        public override string PageTitle { get { return "Sign in"; } }

        /// <summary>The demo account list is shown only where appSettings ShowDemoLogins = true (dev/test).</summary>
        protected bool ShowDemoLogins
        {
            get { return string.Equals(ConfigurationManager.AppSettings["ShowDemoLogins"], "true", StringComparison.OrdinalIgnoreCase); }
        }

        protected void Page_Init(object sender, EventArgs e)
        {
            ((SiteMaster)Master).HideChrome = true;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack && CurrentUser != null)
                Go(Navigation.LandingFor(CurrentUser));
            DemoLoginsPanel.Visible = ShowDemoLogins;
            if (!IsPostBack && ShowDemoLogins && !DataSource.IsApi("Users")) EmailInput.Text = "drsharma@sunrise.in";
            var h = Hospital;
            HospitalNameText.Text = Server.HtmlEncode(h != null ? h.Name : "Consult360");
            HospitalAddressText.Text = Server.HtmlEncode(h != null ? h.LegalAddress : "");
        }

        protected void SignInButton_Click(object sender, EventArgs e)
        {
            if (!IsValid) return;
            string token;
            Models.AppUser user;
            try
            {
                user = UserService.Authenticate(EmailInput.Text, PasswordInput.Text, out token);
            }
            catch (ApiException ex)
            {
                // Lockout ("Too many attempts — try again at …"), service unreachable, or another refusal.
                // An unreachable service was already shown as a toast by CommonHelper.
                ErrorText.Text = Server.HtmlEncode(ex.UserMessage);
                ErrorPanel.Visible = !ex.AlreadyShown;
                return;
            }
            if (user == null)
            {
                ErrorText.Text = "Invalid credentials or inactive account.";
                ErrorPanel.Visible = true;
                return;
            }
            AppSession.SignIn(user, token);
            // AuthenticateUser audits LOGIN itself; only the in-memory store needs the page to record it.
            if (token == null)
                AuditService.Record(user.HospitalId, user.UserId, "LOGIN", "Session", Session.SessionID, "Login from " + Request.Browser.Browser + " " + Request.Browser.Version);
            ToastSuccess("Welcome, " + user.FullName);

            var returnUrl = Request.QueryString["returnUrl"];
            bool local = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith("/") && !returnUrl.StartsWith("//");
            Go(local ? returnUrl : Navigation.LandingFor(user));
        }
    }
}
