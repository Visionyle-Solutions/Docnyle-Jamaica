using System;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Today's appointment queue. DOCTOR → own appointments; front desk (receptionist, admin, nurse) → every
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
            SubtitleText.Text = Server.HtmlEncode((ShowDoctor ? "All doctors" : CurrentUser.FullName) + " · " + DateTime.Today.ToString("dddd, MMM d"));
            if (!IsPostBack)
            {
                if (ShowDoctor)
                {
                    DoctorFilter.Items.Add(new ListItem("All doctors", ""));
                    foreach (var d in UserService.ListActiveDoctors(HospitalId)) DoctorFilter.Items.Add(new ListItem(d.FullName, d.UserId));
                }
                BindQueue();
            }
        }

        private void BindQueue()
        {
            var rows = SchedulingService.ListAppointments(HospitalId, ShowDoctor ? null : UserId, DateTime.Today)
                .Where(a => a.Status != "CANCELLED").ToList();
            if (ShowDoctor && DoctorFilter.SelectedValue != "") rows = rows.Where(a => a.DoctorId == DoctorFilter.SelectedValue).ToList();

            CountBooked.Text = rows.Count(a => a.Status == "BOOKED").ToString();
            CountCheckedIn.Text = rows.Count(a => a.Status == "CHECKED_IN").ToString();
            CountInConsult.Text = rows.Count(a => a.Status == "IN_CONSULT").ToString();
            CountCompleted.Text = rows.Count(a => a.Status == "COMPLETED").ToString();

            AppointmentsRepeater.DataSource = rows;
            AppointmentsRepeater.DataBind();
            EmptyRow.Visible = rows.Count == 0;
        }

        protected void DoctorFilter_Changed(object sender, EventArgs e) { BindQueue(); }

        protected void AppointmentsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "CheckIn") return;
            var updated = SchedulingService.UpdateStatus((string)e.CommandArgument, "CHECKED_IN", UserId);
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
            return Role == "DOCTOR" && Can("consultation") && a.DoctorId == UserId && (a.Status == "CHECKED_IN" || a.Status == "IN_CONSULT");
        }

        protected bool CanCheckIn(AppointmentView a) { return Can("scheduling") && a.Status == "BOOKED"; }

        protected string WaitingNote(AppointmentView a)
        {
            if (CanConsult(a)) return "";
            return a.Status == "CHECKED_IN" ? "Waiting for doctor" : a.Status == "IN_CONSULT" ? "With doctor" : "";
        }

        protected string StatusTime(AppointmentView a)
        {
            var ap = a.Appointment;
            var stamp = a.Status == "CHECKED_IN" ? ap.CheckedInAt : a.Status == "IN_CONSULT" ? ap.ConsultStartedAt : a.Status == "COMPLETED" ? ap.CompletedAt : null;
            return stamp.HasValue ? FmtTime(stamp.Value) : "";
        }
    }
}
