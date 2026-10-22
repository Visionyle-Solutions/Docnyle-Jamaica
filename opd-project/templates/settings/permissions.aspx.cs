using System;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.Web
{
    public class PermissionCell
    {
        public string Role { get; set; }
        public string Feature { get; set; }
        public bool Allowed { get; set; }
    }
}

namespace OpdProject.templates.settings
{
    /// <summary>Toggle role/feature cells of the permission matrix (BasePage enforces it on every request).</summary>
    public partial class permissions : BasePage
    {
        protected override string RequiredPermission { get { return "admin_users"; } }
        public override string PageTitle { get { return "Permission Matrix"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) Bind();
        }

        private void Bind()
        {
            RolesHeader.DataSource = UserService.ListRoles();
            RolesHeader.DataBind();
            MatrixRepeater.DataSource = UserService.GetPermissionMatrix();
            MatrixRepeater.DataBind();
        }

        protected void MatrixRepeater_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var row = e.Item.DataItem as PermissionRow;
            if (row == null) return;
            var cells = (Repeater)e.Item.FindControl("CellsRepeater");
            cells.DataSource = UserService.ListRoles().Select(r => new PermissionCell { Role = r, Feature = row.Feature, Allowed = row.Access.ContainsKey(r) && row.Access[r] }).ToList();
            cells.DataBind();
        }

        protected void CellsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var parts = ((string)e.CommandArgument).Split('|');
            var allowed = UserService.TogglePermission(parts[0], parts[1]);
            Audit("UPDATE", "Permission", parts[0] + ":" + parts[1], allowed ? "Granted" : "Revoked");
            ToastSuccess(parts[1] + " → " + parts[0] + (allowed ? " granted" : " revoked"));
            Bind();
        }
    }
}
