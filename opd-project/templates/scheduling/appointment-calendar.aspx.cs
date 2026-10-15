using System;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.scheduling
{
    /// <summary>
    /// One doctor's diary for a date: check-in / reschedule / cancel, booking with server-computed free slots,
    /// quick-book of a free slot, recurring series, reminders and no-show messages.
    /// </summary>
    public partial class appointment_calendar : BasePage
    {
        protected override string RequiredPermission { get { return "scheduling"; } }
        public override string PageTitle { get { return "Appointment Calendar"; } }

        private string DoctorId { get { return DoctorFilter.SelectedValue; } }
        private DateTime SelectedDate { get { return ParseDate(DateFilter.Text) ?? DateTime.Today; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (Request.QueryString["patientSuggest"] != null) { WritePatientSuggestions(Request.QueryString["patientSuggest"]); return; }
            if (IsPostBack) return;
            var doctors = UserService.ListActiveDoctors(HospitalId);
            foreach (var d in doctors)
                DoctorFilter.Items.Add(new ListItem(d.FullName + (string.IsNullOrEmpty(d.Qualifications) ? "" : " — " + d.Qualifications) + (string.IsNullOrEmpty(d.Room) ? "" : " · " + d.Room), d.UserId));
            if (doctors.Any(d => d.UserId == UserId)) DoctorFilter.SelectedValue = UserId;
            DateFilter.Text = IsoDate(Request.QueryString["date"] != null ? ParseDate(Request.QueryString["date"]) : DateTime.Today);
            foreach (var t in SchedulingService.ListAppointmentTypes(HospitalId, false))
            {
                var label = t.Name + (t.DurationMinutes.HasValue ? " (" + t.DurationMinutes + " min)" : "");
                BookType.Items.Add(new ListItem(label, t.Code));
                RecurType.Items.Add(new ListItem(label, t.Code));
            }
            if (RecurType.Items.FindByValue("FOLLOWUP") != null) RecurType.SelectedValue = "FOLLOWUP";
            Bind();
        }

        protected void Filter_Changed(object sender, EventArgs e) { Bind(); }

        private void Bind()
        {
            if (string.IsNullOrEmpty(DoctorId))
            {
                NoAppointments.Visible = true;
                NoSlots.Text = "<div class=\"zone-a-empty\">No active doctors — add one in Schedule Settings → Doctors</div>";
                return;
            }
            var appts = SchedulingService.ListAppointments(HospitalId, DoctorId, SelectedDate);
            ScheduleRepeater.DataSource = appts;
            ScheduleRepeater.DataBind();
            NoAppointments.Visible = appts.Count == 0;

            var slots = SchedulingService.ListAvailableSlots(DoctorId, HospitalId, SelectedDate, null);
            var sched = SchedulingService.GetDoctorSchedule(DoctorId, HospitalId, SelectedDate);
            var free = slots.Where(s => !s.IsBooked).ToList();
            SlotsRepeater.DataSource = free;
            SlotsRepeater.DataBind();
            NoSlots.Text = free.Count == 0 ? "<div class=\"zone-a-empty\">No Available Slots<br><span class=\"text-xs\">" + HttpUtility.HtmlEncode(SchedulingService.NoSlotReason(sched)) + "</span></div>" : "";
            ScheduleInfo.Text = sched.OnLeave || sched.Windows.Count == 0
                ? "<i class=\"ti ti-calendar-off\" aria-hidden=\"true\"></i> " + HttpUtility.HtmlEncode(SchedulingService.NoSlotReason(sched))
                : "<i class=\"ti ti-clock\" aria-hidden=\"true\"></i> " + string.Join("<br>", sched.Windows.Select(w => HttpUtility.HtmlEncode(HospitalService.FormatClock(HospitalId, w.StartTime) + "–" + HospitalService.FormatClock(HospitalId, w.EndTime))))
                  + "<br>" + sched.SlotMinutes + "-Min Slots" + (sched.BufferMinutes > 0 ? " · " + sched.BufferMinutes + "-Min Buffer" : "");
            SlotSummary.Text = slots.Count(s => s.IsBooked) + " Booked / " + slots.Count + " Total Slots · " + HttpUtility.HtmlEncode(FmtDate(SelectedDate));
        }

        /* ── Display helpers ── */

        protected string StatusColor(string status)
        {
            switch (status)
            {
                case "CHECKED_IN": return "var(--color-warning)";
                case "IN_CONSULT": return "#f59e0b";
                case "COMPLETED": return "var(--color-success)";
                case "CANCELLED": case "NO_SHOW": return "var(--color-danger)";
                default: return "var(--color-primary)";
            }
        }

        protected string TypeColor(string code)
        {
            var t = SchedulingService.GetAppointmentType(HospitalId, code);
            return t != null && !string.IsNullOrEmpty(t.Color) ? t.Color : "#94a3b8";
        }

        protected bool CanConsult(AppointmentView a)
        {
            return HasRole("DOCTOR") && Can("consultation") && a.DoctorId == UserId && (a.Status == "CHECKED_IN" || a.Status == "IN_CONSULT");
        }

        /* ── Row actions ── */

        protected void ScheduleRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var id = (string)e.CommandArgument;
            switch (e.CommandName)
            {
                case "CheckIn":
                    if (SchedulingService.UpdateStatus(id, "CHECKED_IN", UserId) != null) { Audit("UPDATE", "Appointment", id, "Checked in"); ToastSuccess("Patient checked in"); }
                    else ToastWarning("This appointment can no longer be checked in");
                    break;
                case "Cancel":
                    if (SchedulingService.UpdateStatus(id, "CANCELLED", UserId) != null) { Audit("UPDATE", "Appointment", id, "Cancelled"); ToastSuccess("Appointment cancelled"); }
                    else ToastWarning("A consultation has already started — it can no longer be cancelled");
                    break;
                case "Reschedule":
                    var appt = SchedulingService.GetAppointment(id);
                    if (appt == null || appt.HospitalId != HospitalId) break;
                    RescheduleId.Value = id;
                    RescheduleCurrent.Text = HttpUtility.HtmlEncode(FmtDate(appt.SlotStart) + ", " + FmtTime(appt.SlotStart) + " — " + FmtTime(appt.SlotEnd)
                        + " · " + UserService.UserName(appt.DoctorId));
                    RescheduleDate.Text = IsoDate(SelectedDate.AddDays(1));
                    BindRescheduleSlots();
                    RescheduleModal.Visible = true;
                    return;
            }
            Bind();
        }

        /// <summary>Fills "New time" with the free slots on the chosen date (same doctor, same length, not in the past).</summary>
        private void BindRescheduleSlots()
        {
            RescheduleSlot.Items.Clear();
            var appt = SchedulingService.GetAppointment(RescheduleId.Value);
            var date = ParseDate(RescheduleDate.Text);
            if (appt == null || !date.HasValue) { RescheduleSlot.Items.Add(new ListItem("Choose A Date First", "")); return; }
            var free = SchedulingService.ListRescheduleSlots(appt, date.Value);
            if (free.Count == 0)
            {
                var sched = SchedulingService.GetDoctorSchedule(appt.DoctorId, HospitalId, date.Value);
                var reason = date.Value.Date < DateTime.Today ? "Date is in the past"
                    : date.Value.Date == DateTime.Today && sched.Windows.Count > 0 ? "No free times left today"
                    : SchedulingService.NoSlotReason(sched);
                RescheduleSlot.Items.Add(new ListItem("No free slots: " + reason, ""));
                return;
            }
            foreach (var s in free) RescheduleSlot.Items.Add(new ListItem(FmtTime(s.SlotStart) + " — " + FmtTime(s.SlotEnd), s.Value));
        }

        protected void RescheduleDate_Changed(object sender, EventArgs e)
        {
            BindRescheduleSlots();
            RescheduleModal.Visible = true;
        }

        protected void ConfirmRescheduleButton_Click(object sender, EventArgs e)
        {
            Validate("Reschedule");
            if (!IsValid) { RescheduleModal.Visible = true; return; }
            var appt = SchedulingService.GetAppointment(RescheduleId.Value);
            var moved = appt != null && appt.HospitalId == HospitalId
                ? SchedulingService.Reschedule(RescheduleId.Value, ParseSlot(RescheduleSlot.SelectedValue)[0], UserId) : null;
            if (moved == null)
            {
                ToastWarning("That time is no longer available", "Someone may have just booked it — pick another time.");
                BindRescheduleSlots();
                RescheduleModal.Visible = true;
                return;
            }
            Audit("UPDATE", "Appointment", RescheduleId.Value, "Rescheduled to " + moved.SlotStart.ToString("yyyy-MM-dd HH:mm") + " as " + moved.AppointmentId);
            ToastSuccess("Rescheduled to " + FmtDate(moved.SlotStart) + " at " + FmtTime(moved.SlotStart));
            RescheduleModal.Visible = false;
            Bind();
        }

        /* ── Booking ── */

        private void OpenBookModal(string quickSlotValue)
        {
            QuickSlot.Value = quickSlotValue ?? "";
            PatientSearch.Text = BookNotes.Text = PatientPick.Value = "";
            BookDoctor.Items.Clear();
            foreach (ListItem i in DoctorFilter.Items) BookDoctor.Items.Add(new ListItem(i.Text, i.Value));
            BookDoctor.SelectedValue = DoctorId;
            if (BookType.Items.FindByValue("NEW") != null) BookType.SelectedValue = "NEW";
            bool quick = quickSlotValue != null;
            BookDoctorHolder.Visible = BookSlotHolder.Visible = !quick;
            BookTitle.Text = quick ? "Book " + HttpUtility.HtmlEncode(FmtTime(ParseSlot(quickSlotValue)[0]) + " — " + FmtTime(ParseSlot(quickSlotValue)[1]))
                                   : "Book Appointment — " + HttpUtility.HtmlEncode(FmtDate(SelectedDate));
            BindBookSlots();
            BookModal.Visible = true;
        }

        private static DateTime[] ParseSlot(string value)
        {
            var parts = value.Split('|');
            return parts.Select(p => DateTime.ParseExact(p, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)).ToArray();
        }

        private void BindBookSlots()
        {
            BookSlot.Items.Clear();
            if (string.IsNullOrEmpty(BookDoctor.SelectedValue)) return;
            var type = SchedulingService.GetAppointmentType(HospitalId, BookType.SelectedValue);
            var free = SchedulingService.ListAvailableSlots(BookDoctor.SelectedValue, HospitalId, SelectedDate, type != null ? type.DurationMinutes : null).Where(s => !s.IsBooked).ToList();
            if (free.Count == 0)
            {
                var sched = SchedulingService.GetDoctorSchedule(BookDoctor.SelectedValue, HospitalId, SelectedDate);
                BookSlot.Items.Add(new ListItem("No free slots: " + SchedulingService.NoSlotReason(sched), ""));
                return;
            }
            foreach (var s in free) BookSlot.Items.Add(new ListItem(FmtTime(s.SlotStart) + " — " + FmtTime(s.SlotEnd), s.Value));
        }

        protected void BookButton_Click(object sender, EventArgs e)
        {
            if (DoctorFilter.Items.Count == 0) { ToastWarning("No active doctors — add one in Schedule Settings → Doctors"); return; }
            OpenBookModal(null);
        }

        protected void SlotsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName == "QuickBook") OpenBookModal((string)e.CommandArgument);
        }

        protected void BookOptions_Changed(object sender, EventArgs e)
        {
            BindBookSlots();
            BookModal.Visible = true;
        }

        /* ── Patient typeahead ── */

        /// <summary>GET ?patientSuggest=term — JSON list of up to 15 matching patients for the booking dialogs' typeahead.</summary>
        private void WritePatientSuggestions(string term)
        {
            var found = (term ?? "").Trim().Length < 2
                ? new System.Collections.Generic.List<Patient>()
                : PatientService.Search(HospitalId, term).Take(15).ToList();
            var rows = found.Select(p => new
            {
                id = p.PatientId,
                name = p.FullName,
                mrn = p.Mrn,
                phone = p.Phone,
                info = string.Join(" ", new[] { p.Gender, p.Dob.HasValue ? AgeYears(p.Dob.Value) + "y" : null }.Where(s => !string.IsNullOrEmpty(s)))
            });
            Response.Clear();
            Response.ContentType = "application/json";
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Write(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(rows));
            Response.End();
        }

        private static int AgeYears(DateTime dob)
        {
            var today = DateTime.Today;
            int age = today.Year - dob.Year;
            return dob.Date > today.AddYears(-age) ? age - 1 : age;
        }

        /// <summary>The chosen patient (hidden field set by the typeahead) must exist in this hospital.</summary>
        protected void PatientPick_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var id = ((CustomValidator)source).ValidationGroup == "Recurring" ? RecurPatientPick.Value : PatientPick.Value;
            var p = string.IsNullOrEmpty(id) ? null : PatientService.Get(id);
            args.IsValid = p != null && p.HospitalId == HospitalId;
        }

        protected void ConfirmBookButton_Click(object sender, EventArgs e)
        {
            Validate("Book");
            if (!IsValid) { BookModal.Visible = true; return; }
            var typeCode = BookType.SelectedValue;
            var type = SchedulingService.GetAppointmentType(HospitalId, typeCode);
            string doctorId;
            DateTime start, end;
            if (!string.IsNullOrEmpty(QuickSlot.Value))
            {
                doctorId = DoctorId;
                var s = ParseSlot(QuickSlot.Value);
                start = s[0]; end = s[1];
                if (type != null && type.DurationMinutes.HasValue)
                {
                    /* a type with its own duration must fit at this start time */
                    var fit = SchedulingService.ListAvailableSlots(doctorId, HospitalId, start.Date, type.DurationMinutes).FirstOrDefault(x => !x.IsBooked && x.SlotStart == start);
                    if (fit == null)
                    {
                        ToastWarning(type.Name + " needs " + type.DurationMinutes + " min", "That time isn't free for that long. Use \"Book appointment\" to see times that fit.");
                        BookModal.Visible = true;
                        return;
                    }
                    end = fit.SlotEnd;
                }
            }
            else
            {
                doctorId = BookDoctor.SelectedValue;
                var s = ParseSlot(BookSlot.SelectedValue);
                start = s[0]; end = s[1];
            }
            /* re-check on the server: the slot may have been taken since the dialog opened */
            var stillFree = SchedulingService.ListAvailableSlots(doctorId, HospitalId, start.Date, (int)(end - start).TotalMinutes).Any(x => !x.IsBooked && x.SlotStart == start);
            if (!stillFree)
            {
                ToastWarning("That slot was just taken", "Choose another time.");
                BindBookSlots();
                BookModal.Visible = true;
                return;
            }
            var appt = SchedulingService.CreateAppointment(HospitalId, PatientPick.Value, doctorId, start, end, typeCode, BookNotes.Text.Trim());
            Audit("CREATE", "Appointment", appt.AppointmentId, PatientSearch.Text.Trim() + " " + start.ToString("yyyy-MM-dd HH:mm"));
            ToastSuccess("Appointment booked", "with " + UserService.UserName(doctorId) + " at " + FmtTime(start));
            BookModal.Visible = false;
            DoctorFilter.SelectedValue = doctorId;
            Bind();
        }

        /* ── Recurring ── */

        protected void RecurringButton_Click(object sender, EventArgs e)
        {
            RecurPatientSearch.Text = RecurPatientPick.Value = "";
            RecurStart.Text = IsoDate(SelectedDate);
            RecurringModal.Visible = true;
        }

        protected void ConfirmRecurringButton_Click(object sender, EventArgs e)
        {
            Validate("Recurring");
            if (!IsValid) { RecurringModal.Visible = true; return; }
            int count = ParseInt(RecurCount.Text, 4), every = ParseInt(RecurFreq.SelectedValue, 7);
            var booked = SchedulingService.BookRecurring(HospitalId, RecurPatientPick.Value, DoctorId, ParseDate(RecurStart.Text).Value, every, count, RecurType.SelectedValue);
            Audit("CREATE", "Appointment", RecurPatientPick.Value, "Recurring series: " + booked + " of " + count);
            if (booked == count) ToastSuccess(booked + " recurring appointments created");
            else ToastWarning(booked + " of " + count + " recurring appointments created", "Some dates had no free slot.");
            RecurringModal.Visible = false;
            Bind();
        }

        /* ── Notifications ── */

        protected void RemindersButton_Click(object sender, EventArgs e)
        {
            var sent = NotificationService.SendReminders(HospitalId, DoctorId, SelectedDate);
            if (sent == null) ToastWarning("Appointment reminder template is disabled");
            else ToastSuccess(sent + " reminder(s) sent", "See Reports & Comms → Message Log.");
        }

        protected void NoShowButton_Click(object sender, EventArgs e)
        {
            var sent = NotificationService.SendNoShowMessages(HospitalId, DoctorId, SelectedDate, UserId);
            if (sent == null) ToastWarning("No-show template is disabled");
            else ToastSuccess(sent + " no-show message(s) sent");
            Bind();
        }

        protected void CloseModals_Click(object sender, EventArgs e)
        {
            BookModal.Visible = RescheduleModal.Visible = RecurringModal.Visible = false;
        }
    }
}
