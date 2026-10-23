using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Appointment summary — one day's queue (today by default, ?date=yyyy-MM-dd), or a dashboard tile's kind via ?kind=[&status=]. DOCTOR → own appointments; front desk (receptionist, admin, nurse) → every
    /// doctor's appointments with a doctor filter and a server-side Check-in action.
    /// </summary>
    public partial class patient_today_list : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Today's Patients"; } }

        protected bool ShowDoctor { get { return !SeesOwnPatientsOnly; } }

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
                KindFilter.Items.Add(new ListItem("Appointments", "appointments"));
                KindFilter.Items.Add(new ListItem("Consultations", "consultations"));
                KindFilter.Items.Add(new ListItem("Cancelled Consultations", "cancelled"));
                StatusFilter.Items.Add(new ListItem("All Statuses", ""));
                foreach (var s in new[] { "BOOKED", "CHECKED_IN", "IN_CONSULT", "COMPLETED", "NO_SHOW" }) StatusFilter.Items.Add(new ListItem(StatusLabel(s), s));
                foreach (var n in new[] { "10", "25", "50", "100" }) PageSizeFilter.Items.Add(new ListItem(n, n));
                PageSizeFilter.Items.Add(new ListItem("All", "0"));

                Select(KindFilter, Request.QueryString["kind"]);
                Select(StatusFilter, Request.QueryString["status"]);
                DateFilter.Text = ParseDate(Request.QueryString["date"]).ToString("yyyy-MM-dd");
                BindQueue();
            }
        }

        private static void Select(DropDownList list, string value)
        {
            if (value != null && list.Items.FindByValue(value) != null) list.SelectedValue = value;
        }

        /// <summary>"today" or "on 2026-10-03" — for the empty-list message.</summary>
        protected string DayText { get; private set; }

        private new DateTime ParseDate(string value)
        {
            DateTime d;
            return DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : Today;
        }

        private void BindQueue()
        {
            var day = ParseDate(DateFilter.Text);
            DateFilter.Text = day.ToString("yyyy-MM-dd");
            DayText = day == Today ? "today" : "on " + FmtDate(day);
            SubtitleText.Text = Server.HtmlEncode((ShowDoctor ? "All Doctors" : CurrentUser.FullName) + " · " + day.ToString("dddd", CultureInfo.InvariantCulture) + ", " + FmtDate(day));

            var dayRows = DayRows(day);
            CountBooked.Text = dayRows.Count(a => a.Status == "BOOKED").ToString();
            CountCheckedIn.Text = dayRows.Count(a => a.Status == "CHECKED_IN").ToString();
            CountInConsult.Text = dayRows.Count(a => a.Status == "IN_CONSULT").ToString();
            CountCompleted.Text = dayRows.Count(a => a.Status == "COMPLETED").ToString();

            /* counts above stay the period's totals; the status filter only narrows the list */
            var rows = ByStatus(dayRows);

            /* paging: PageSize 0 = all records; the page index is clamped so a shrinking list never shows an empty page */
            var size = PageSize;
            var pages = size == 0 || rows.Count == 0 ? 1 : (rows.Count + size - 1) / size;
            PageIndex = Math.Max(0, Math.Min(PageIndex, pages - 1));
            RowOffset = size == 0 ? 0 : PageIndex * size;
            var pageRows = size == 0 ? rows : rows.Skip(RowOffset).Take(size).ToList();

            AppointmentsRepeater.DataSource = pageRows;
            AppointmentsRepeater.DataBind();
            EmptyRow.Visible = rows.Count == 0;

            PageInfo.Text = rows.Count == 0 ? "" : "Showing " + (RowOffset + 1) + "–" + (RowOffset + pageRows.Count) + " of " + rows.Count;
            PrevPageButton.Enabled = PageIndex > 0;
            NextPageButton.Enabled = PageIndex < pages - 1;
            foreach (var b in new[] { PrevPageButton, NextPageButton }) { if (b.Enabled) b.Style.Remove("opacity"); else b.Style["opacity"] = "0.45"; }
        }

        /// <summary>The day's appointments of the selected kind, narrowed to the doctor filter.</summary>
        private List<AppointmentView> DayRows(DateTime day)
        {
            var rows = ReportService.AppointmentList(HospitalId, ShowDoctor ? null : UserId, KindFilter.SelectedValue, day, day);
            if (ShowDoctor && DoctorFilter.SelectedValue != "") rows = rows.Where(a => a.DoctorId == DoctorFilter.SelectedValue).ToList();
            return rows;
        }

        private List<AppointmentView> ByStatus(List<AppointmentView> rows)
        {
            return StatusFilter.SelectedValue == "" ? rows : rows.Where(a => a.Status == StatusFilter.SelectedValue).ToList();
        }

        /// <summary>Excel / PDF of every row matching the filters (all pages, not just the one shown).</summary>
        protected void Export_Command(object sender, CommandEventArgs e)
        {
            var day = ParseDate(DateFilter.Text);
            var rows = ByStatus(DayRows(day));
            if (rows.Count == 0) { ToastWarning("No " + KindFilter.SelectedItem.Text.ToLower() + " to export"); BindQueue(); return; }

            var table = new TableExport.Table
            {
                Title = "Appointment Summary",
                Subtitle = string.Join(" · ", new[] {
                    ShowDoctor ? DoctorFilter.SelectedItem.Text : CurrentUser.FullName,
                    day.ToString("dddd", CultureInfo.InvariantCulture) + ", " + FmtDate(day),
                    KindFilter.SelectedItem.Text, StatusFilter.SelectedItem.Text }),
            };
            table.Headers.AddRange(new[] { "S.No", "Time" });
            if (ShowDoctor) table.Headers.AddRange(new[] { "Doctor", "Room" });
            table.Headers.AddRange(new[] { "Patient Name", PatientIdLabel, "Type", "Status", "Status Time" });
            var n = 0;
            foreach (var a in rows)
            {
                var cells = new List<object> { ++n, EventTime(a) };
                if (ShowDoctor) cells.AddRange(new object[] { a.DoctorName, a.DoctorRoom });
                cells.AddRange(new object[] { a.PatientName, a.PatientMrn, AppointmentTypeName(a.Type), StatusLabel(a.Status), StatusTime(a) });
                table.Rows.Add(cells.ToArray());
            }
            DownloadTable((string)e.CommandArgument, "appointment-summary-" + day.ToString("yyyy-MM-dd"), table);
        }

        protected int PageSize { get { int n; return int.TryParse(PageSizeFilter.SelectedValue, out n) ? n : 10; } }

        protected int PageIndex
        {
            get { return ViewState["page"] as int? ?? 0; }
            set { ViewState["page"] = value; }
        }

        /// <summary>Records before the current page, so S.No continues across pages.</summary>
        protected int RowOffset { get; private set; }

        /* any filter change starts again at the first page */
        private void Rebind() { PageIndex = 0; BindQueue(); }

        protected void DoctorFilter_Changed(object sender, EventArgs e) { Rebind(); }

        protected void StatusFilter_Changed(object sender, EventArgs e) { Rebind(); }

        protected void DateFilter_Changed(object sender, EventArgs e) { Rebind(); }

        protected void KindFilter_Changed(object sender, EventArgs e) { Rebind(); }

        protected void PageSizeFilter_Changed(object sender, EventArgs e) { Rebind(); }

        protected void PrevPage_Click(object sender, EventArgs e) { PageIndex--; BindQueue(); }

        protected void NextPage_Click(object sender, EventArgs e) { PageIndex++; BindQueue(); }

        protected void AppointmentsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "CheckIn") return;
            var appt = SchedulingService.GetAppointment((string)e.CommandArgument);
            /* same checks as the button: scheduling permission, this hospital, (for a doctor) their own patient, booked for today */
            var allowed = appt != null && Can("scheduling") && appt.HospitalId == HospitalId && appt.AppointmentDate == Today
                && (!SeesOwnPatientsOnly || appt.DoctorId == UserId);
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
            return HasRole("DOCTOR") && Can("consultation") && a.DoctorId == UserId && a.SlotStart.Date == Today && (a.Status == "CHECKED_IN" || a.Status == "IN_CONSULT");
        }

        protected bool CanCheckIn(AppointmentView a) { return Can("scheduling") && a.Status == "BOOKED" && a.SlotStart.Date == Today; }

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
