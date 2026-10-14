using System;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Appointment summary — today's queue by default, or any dashboard tile's period/kind via ?kind=&period=[&status=]. DOCTOR → own appointments; front desk (receptionist, admin, nurse) → every
    /// doctor's appointments with a doctor filter and a server-side Check-in action.
    /// </summary>
    public partial class patient_today_list : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Today's Patients"; } }

        protected bool ShowDoctor { get { return Role != "DOCTOR"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            DoctorFilterHolder.Visible = ShowDoctor;
            if (!IsPostBack)
            {
                if (ShowDoctor)
                {
                    DoctorFilter.Items.Add(new ListItem("All Doctors", ""));
                    foreach (var d in UserService.ListActiveDoctors(HospitalId)) DoctorFilter.Items.Add(new ListItem(d.FullName, d.UserId));
                }
                PeriodFilter.Items.Add(new ListItem("Today", "today"));
                PeriodFilter.Items.Add(new ListItem("This Week", "week"));
                PeriodFilter.Items.Add(new ListItem("This Month", "month"));
                KindFilter.Items.Add(new ListItem("Appointments", "appointments"));
                KindFilter.Items.Add(new ListItem("Consultations", "consultations"));
                KindFilter.Items.Add(new ListItem("Cancelled Consultations", "cancelled"));
                StatusFilter.Items.Add(new ListItem("All Statuses", ""));
                foreach (var s in new[] { "BOOKED", "CHECKED_IN", "IN_CONSULT", "COMPLETED", "NO_SHOW" }) StatusFilter.Items.Add(new ListItem(StatusLabel(s), s));

                /* dashboard tiles and pie slices link here with ?kind=&period=[&status=]; unknown values fall back to the defaults */
                Select(KindFilter, Request.QueryString["kind"]);
                Select(PeriodFilter, Request.QueryString["period"]);
                Select(StatusFilter, Request.QueryString["status"]);
                BindQueue();
            }
        }

        private static void Select(DropDownList list, string value)
        {
            if (value != null && list.Items.FindByValue(value) != null) list.SelectedValue = value;
        }

        protected bool IsToday { get { return PeriodFilter.SelectedValue == "today"; } }

        private void BindQueue()
        {
            DateTime from, to;
            ReportService.PeriodRange(PeriodFilter.SelectedValue, DateTime.Today, out from, out to);
            SubtitleText.Text = Server.HtmlEncode((ShowDoctor ? "All Doctors" : CurrentUser.FullName) + " · "
                + (from == to ? from.ToString("dddd, MMM d") : FmtDate(from) + " – " + FmtDate(to)));

            /* same source as the dashboard counts, so the list matches the tile that was clicked */
            var rows = ReportService.DashboardList(HospitalId, ShowDoctor ? null : UserId, KindFilter.SelectedValue, PeriodFilter.SelectedValue, null, DateTime.Today);
            if (ShowDoctor && DoctorFilter.SelectedValue != "") rows = rows.Where(a => a.DoctorId == DoctorFilter.SelectedValue).ToList();

            CountBooked.Text = rows.Count(a => a.Status == "BOOKED").ToString();
            CountCheckedIn.Text = rows.Count(a => a.Status == "CHECKED_IN").ToString();
            CountInConsult.Text = rows.Count(a => a.Status == "IN_CONSULT").ToString();
            CountCompleted.Text = rows.Count(a => a.Status == "COMPLETED").ToString();

            /* counts above stay the period's totals; the status filter only narrows the list */
            if (StatusFilter.SelectedValue != "") rows = rows.Where(a => a.Status == StatusFilter.SelectedValue).ToList();
            AppointmentsRepeater.DataSource = rows;
            AppointmentsRepeater.DataBind();
            EmptyRow.Visible = rows.Count == 0;
        }

        protected void DoctorFilter_Changed(object sender, EventArgs e) { BindQueue(); }

        protected void StatusFilter_Changed(object sender, EventArgs e) { BindQueue(); }

        protected void PeriodFilter_Changed(object sender, EventArgs e) { BindQueue(); }

        protected void KindFilter_Changed(object sender, EventArgs e) { BindQueue(); }

        protected void AppointmentsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "CheckIn") return;
            var appt = SchedulingService.GetAppointment((string)e.CommandArgument);
            /* same checks as the button: scheduling permission, this hospital, (for a doctor) their own patient, booked for today */
            var allowed = appt != null && Can("scheduling") && appt.HospitalId == HospitalId && appt.SlotStart.Date == DateTime.Today
                && (Role != "DOCTOR" || appt.DoctorId == UserId);
            var updated = allowed ? SchedulingService.UpdateStatus(appt.AppointmentId, "CHECKED_IN", UserId) : null;
            if (updated != null && updated.Status == "CHECKED_IN")
            {
                Audit("UPDATE", "Appointment", updated.AppointmentId, "Checked in");
                ToastSuccess("Patient checked in");
            }
            else ToastWarning("This appointment can no longer be checked in");
            BindQueue();
        }

        /* The consultation is opened by the appointment's own doctor, once the patient is checked in */
        protected bool CanConsult(AppointmentView a)
        {
            return Role == "DOCTOR" && Can("consultation") && a.DoctorId == UserId && a.SlotStart.Date == DateTime.Today && (a.Status == "CHECKED_IN" || a.Status == "IN_CONSULT");
        }

        protected bool CanCheckIn(AppointmentView a) { return Can("scheduling") && a.Status == "BOOKED" && a.SlotStart.Date == DateTime.Today; }

        protected string WaitingNote(AppointmentView a)
        {
            if (CanConsult(a)) return "";
            return a.Status == "CHECKED_IN" ? "Waiting For Doctor" : a.Status == "IN_CONSULT" ? "With Doctor" : "";
        }

        protected string StatusTime(AppointmentView a)
        {
            var ap = a.Appointment;
            var stamp = a.Status == "CHECKED_IN" ? ap.CheckedInAt : a.Status == "IN_CONSULT" ? ap.ConsultStartedAt : a.Status == "COMPLETED" ? ap.CompletedAt : null;
            return stamp.HasValue ? FmtTime(stamp.Value) : "";
        }
    }
}
