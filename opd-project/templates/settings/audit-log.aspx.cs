using System;
using System.Linq;
using System.Text;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>Audit log viewer (date/action/entity filters) with CSV export of the filtered rows.</summary>
    public partial class audit_log : BasePage
    {
        protected override string RequiredPermission { get { return "audit_log"; } }
        public override string PageTitle { get { return "Audit Log"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            ToDate.Text = IsoDate(DateTime.Today);
            FromDate.Text = IsoDate(DateTime.Today.AddDays(-30));
            EntityFilter.Items.Add(new System.Web.UI.WebControls.ListItem("All", ""));
            foreach (var t in AuditService.List(HospitalId, null, null, null, null).Select(a => a.EntityType).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t))
                EntityFilter.Items.Add(t);
            Bind();
        }

        protected void RefreshButton_Click(object sender, EventArgs e)
        {
            if (IsValid) Bind();
        }

        private System.Collections.Generic.List<Models.AuditLogEntry> Rows()
        {
            return AuditService.List(HospitalId, ActionFilter.SelectedValue, EntityFilter.SelectedValue, ParseDate(FromDate.Text), ParseDate(ToDate.Text));
        }

        private void Bind()
        {
            var rows = Rows();
            LogRepeater.DataSource = rows;
            LogRepeater.DataBind();
            LogRepeater.Visible = rows.Count > 0;
            EmptyHolder.Visible = rows.Count == 0;
            CountText.Text = rows.Count + " entries";
        }

        protected string ActionBadge(string action)
        {
            switch (action)
            {
                case "CREATE": case "SIGN_OFF": case "LOGIN": return "badge--success";
                case "DELETE": case "OVERRIDE_SAFETY": return "badge--danger";
                case "ACKNOWLEDGE_RECON": case "ACKNOWLEDGE_CONTRADICTION": return "badge--warning";
                default: return "badge--info";
            }
        }

        protected void ExportButton_Click(object sender, EventArgs e)
        {
            var rows = Rows();
            if (rows.Count == 0) { ToastWarning("No audit data to export"); Bind(); return; }
            var sb = new StringBuilder("Timestamp,User,Action,EntityType,EntityId,Details,IpAddress\r\n");
            Func<string, string> q = s => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            foreach (var a in rows)
                sb.Append(string.Join(",", q(a.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")), q(UserService.UserName(a.UserId)), q(a.Action), q(a.EntityType), q(a.EntityId), q(a.Details), q(a.IpAddress)) + "\r\n");
            Audit("EXPORT", "DataExport", "auditLogs", "Exported " + rows.Count + " audit entries to CSV");
            Response.Clear();
            Response.ContentType = "text/csv";
            Response.ContentEncoding = Encoding.UTF8;
            Response.AddHeader("Content-Disposition", "attachment; filename=audit-logs-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv");
            Response.BinaryWrite(Encoding.UTF8.GetPreamble());
            Response.Write(sb.ToString());
            Response.End();
        }
    }
}
