using System;
using System.Collections.Generic;
using System.Linq;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.scheduling
{
    /// <summary>Waiting-room token display: now consulting, checked-in queue with estimated wait, upcoming.</summary>
    public partial class queue_display : BasePage
    {
        protected const int AvgConsultMinutes = 15;

        protected override string RequiredPermission { get { return "scheduling"; } }
        public override string PageTitle { get { return "Queue Display"; } }

        protected List<AppointmentView> Waiting { get; private set; }
        protected List<AppointmentView> Upcoming { get; private set; }
        protected List<AppointmentView> Completed { get; private set; }
        protected bool IsTvMode { get { return Request.QueryString["tv"] == "1"; } }
        protected string TvModeUrl { get { return Request.Url.AbsolutePath + (IsTvMode ? "" : "?tv=1"); } }

        protected void Page_Init(object sender, EventArgs e)
        {
            ((SiteMaster)Master).HideChrome = IsTvMode;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            var all = SchedulingService.ListAppointments(HospitalId, null, DateTime.Today).Where(a => a.Status != "CANCELLED").ToList();
            Waiting = all.Where(a => a.Status == "CHECKED_IN").ToList();
            Upcoming = all.Where(a => a.Status == "BOOKED").ToList();
            Completed = all.Where(a => a.Status == "COMPLETED").ToList();
            NowRepeater.DataSource = all.Where(a => a.Status == "IN_CONSULT").ToList();
            NowRepeater.DataBind();
            WaitingRepeater.DataSource = Waiting;
            WaitingRepeater.DataBind();
            WaitingRepeater.Visible = Waiting.Count > 0;
            NoWaiting.Visible = Waiting.Count == 0;
            UpcomingRepeater.DataSource = Upcoming;
            UpcomingRepeater.DataBind();
            NoUpcoming.Visible = Upcoming.Count == 0;
        }

        protected string WaitBadge(int index)
        {
            var minutes = (index + 1) * AvgConsultMinutes;
            return minutes <= 15 ? "badge--success" : minutes <= 30 ? "badge--warning" : "badge--danger";
        }
    }
}
