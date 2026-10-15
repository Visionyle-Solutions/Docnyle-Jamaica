using System;
using System.Web.UI;
using OpdProject.Web;

namespace OpdProject
{
    /// <summary>
    /// Application entry point (default document). Signed-in users go to their landing page
    /// (lab partners → Lab Portal, everyone else → Today's Patients); others go to the login page.
    /// </summary>
    public partial class index : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            var user = AppSession.CurrentUser;
            Response.Redirect(user != null ? Navigation.LandingFor(user) : Urls.Login, true);
        }
    }
}
