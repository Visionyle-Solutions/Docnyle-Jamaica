using System;
using System.Collections.Generic;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.reports
{
    /// <summary>Lists the appointments behind a dashboard tile or pie slice, using the same filter as the counts.</summary>
    public partial class dashboard_list : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Dashboard List"; } }

        private static readonly Dictionary<string, string> KindTitles = new Dictionary<string, string>
        {
            { "consultations", "Consultations" }, { "appointments", "Appointments" }, { "cancelled", "Cancelled Consultations" },
        };

        private static readonly Dictionary<string, string> PeriodTitles = new Dictionary<string, string>
        {
            { "today", "Today" }, { "week", "This Week" }, { "month", "This Month" },
        };

        /// <summary>Doctors see only their own rows, so the doctor column is shown to other roles only.</summary>
        protected bool ShowDoctor { get { return !SeesOwnPatientsOnly; } }

        private string Kind { get { return Request.QueryString["kind"]; } }
        private string Period { get { return Request.QueryString["period"]; } }
        private string Status { get { return Request.QueryString["status"]; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            DateTime from, to;
            if (!ReportService.IsDashboardKind(Kind) || !ReportService.PeriodRange(Period, DateTime.Today, out from, out to)
                || (Status != null && !SchedulingService.StatusLabels.ContainsKey(Status)))
            {
                Go(Urls.Dashboard);
                return;
            }
            if (!IsPostBack) Bind();
        }

        private void Bind()
        {
            string kind = Kind, period = Period, status = Status;
            DateTime from, to;
            ReportService.PeriodRange(period, DateTime.Today, out from, out to);
            var rows = ReportService.DashboardList(HospitalId, SeesOwnPatientsOnly ? UserId : null, kind, period, status, DateTime.Today);
            RowsRepeater.DataSource = rows;
            RowsRepeater.DataBind();
            EmptyRow.Visible = rows.Count == 0;

            TitleText.Text = HttpUtility.HtmlEncode(KindTitles[kind] + " · " + PeriodTitles[period] + (status != null ? " · " + StatusLabel(status) : ""));
            SubtitleText.Text = HttpUtility.HtmlEncode((from == to ? FmtDate(from) : FmtDate(from) + " – " + FmtDate(to)) + " · " + rows.Count + (rows.Count == 1 ? " visit" : " visits")
                + (SeesOwnPatientsOnly ? " · your patients" : " · all doctors"));
        }

        protected void RowsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "CheckIn") return;
            var appt = SchedulingService.GetAppointment((string)e.CommandArgument);
            /* same checks as the button: scheduling permission, this hospital, (for a doctor) their own patient, booked for today */
            var allowed = appt != null && Can("scheduling") && appt.HospitalId == HospitalId && appt.SlotStart.Date == DateTime.Today
                && (!SeesOwnPatientsOnly || appt.DoctorId == UserId);
            var updated = allowed ? SchedulingService.UpdateStatus(appt.AppointmentId, "CHECKED_IN", UserId) : null;
            if (updated != null && updated.Status == "CHECKED_IN")
            {
                Audit("UPDATE", "Appointment", updated.AppointmentId, "Checked in");
                ToastSuccess("Patient checked in");
            }
            else ToastWarning("This appointment can no longer be checked in");
            /* Post/Redirect/Get, so the dashboard numbers and this list both reflect the change on refresh */
            Go(Request.RawUrl);
        }

        protected bool CanCheckIn(AppointmentView a)
        {
            return Can("scheduling") && a.Status == "BOOKED" && a.SlotStart.Date == DateTime.Today;
        }

        protected bool CanConsult(AppointmentView a)
        {
            return HasRole("DOCTOR") && Can("consultation") && a.DoctorId == UserId && a.SlotStart.Date == DateTime.Today
                && (a.Status == "CHECKED_IN" || a.Status == "IN_CONSULT");
        }
    }
}
