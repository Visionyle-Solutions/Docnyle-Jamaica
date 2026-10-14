using System;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>Sign-in: verifies credentials server-side, starts the session, audits the login and routes by role.</summary>
    public partial class login : BasePage
    {
        protected override bool AllowAnonymous { get { return true; } }
        public override string PageTitle { get { return "Sign in"; } }

        protected void Page_Init(object sender, EventArgs e)
        {
            ((SiteMaster)Master).HideChrome = true;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack && CurrentUser != null)
                Go(Navigation.LandingFor(CurrentUser.Role));
            HospitalNameText.Text = Server.HtmlEncode(Hospital.Name);
            HospitalAddressText.Text = Server.HtmlEncode(Hospital.LegalAddress);
        }

        protected void SignInButton_Click(object sender, EventArgs e)
        {
            if (!IsValid) return;
            var user = UserService.Authenticate(EmailInput.Text, PasswordInput.Text);
            if (user == null)
            {
                ErrorText.Text = "Invalid credentials or inactive account.";
                ErrorPanel.Visible = true;
                return;
            }
            AppSession.SignIn(user);
            AuditService.Record(user.HospitalId, user.UserId, "LOGIN", "Session", Session.SessionID, "Login from " + Request.Browser.Browser + " " + Request.Browser.Version);
            ToastSuccess("Welcome, " + user.FullName);

            var returnUrl = Request.QueryString["returnUrl"];
            bool local = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith("/") && !returnUrl.StartsWith("//");
            Go(local ? returnUrl : Navigation.LandingFor(user.Role));
        }
    }
}
