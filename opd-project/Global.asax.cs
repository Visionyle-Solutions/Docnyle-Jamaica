using System;
using System.Web;
using System.Web.Routing;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject
{
    public class Global : HttpApplication
    {
        protected void Application_Start(object sender, EventArgs e)
        {
            /* In-memory demo data (no database) — lives until the application restarts */
            DataStore.Seed();
            RouteConfig.Register(RouteTable.Routes);
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            // Keep the original URLs working: any *.html page that was converted to
            // *.aspx (e.g. /index.html -> /index.aspx) is permanently redirected.
            string path = Request.AppRelativeCurrentExecutionFilePath;
            if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                string aspx = path.Substring(0, path.Length - 5) + ".aspx";
                if (System.IO.File.Exists(Server.MapPath(aspx)))
                {
                    string target = VirtualPathUtility.ToAbsolute(aspx) + Request.Url.Query;
                    Response.RedirectPermanent(target, true);
                }
            }
        }
    }
}
