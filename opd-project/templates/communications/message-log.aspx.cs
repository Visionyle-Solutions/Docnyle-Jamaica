using System;
using System.Linq;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.communications
{
    /// <summary>Outgoing message log with delivery status counts and a status filter.</summary>
    public partial class message_log : BasePage
    {
        protected override string RequiredPermission { get { return "scheduling"; } }
        public override string PageTitle { get { return "Communications"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) Bind();
        }

        protected void StatusFilter_Changed(object sender, EventArgs e) { Bind(); }

        private void Bind()
        {
            var all = NotificationService.ListLog(HospitalId);
            var stats = NotificationService.Stats(all);
            SentCount.Text = stats.Sent.ToString();
            SimulatedCount.Text = stats.Simulated.ToString();
            TotalCount.Text = stats.Total.ToString();
            var rows = all.Where(m => StatusFilter.SelectedValue == "" || m.Status == StatusFilter.SelectedValue).ToList();
            LogRepeater.DataSource = rows;
            LogRepeater.DataBind();
            LogRepeater.Visible = rows.Count > 0;
            EmptyHolder.Visible = rows.Count == 0;
        }
    }
}
