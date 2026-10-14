using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.portal
{
    /// <summary>
    /// Public online booking. Patient is identified by MRN or phone; free slots come from the same slot engine the
    /// front desk uses and are re-checked at submit; intake answers are stored against the appointment.
    /// </summary>
    public partial class self_book : BasePage
    {
        protected override bool AllowAnonymous { get { return true; } }
        public override string PageTitle { get { return "Book an Appointment"; } }

        protected Hospital PortalHospital
        {
            get
            {
                var h = Request.QueryString["h"] != null ? HospitalService.GetHospital(Request.QueryString["h"]) : null;
                return h ?? Hospital;
            }
        }

        private string Hid { get { return PortalHospital.HospitalId; } }
        protected string PatientIdLabelText { get { return string.IsNullOrEmpty(PortalHospital.PatientIdLabel) ? "MRN" : PortalHospital.PatientIdLabel; } }
        protected string RegisterUrl { get { return Urls.To("portal/register") + "?h=" + HttpUtility.UrlEncode(Hid); } }

        private Patient _patient;

        protected void Page_Init(object sender, EventArgs e)
        {
            ((SiteMaster)Master).HideChrome = true;
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            foreach (var d in UserService.ListActiveDoctors(Hid))
            {
                var sched = SchedulingService.GetDoctorSchedule(d.UserId, Hid, DateTime.Today);
                var label = d.FullName + (string.IsNullOrEmpty(d.Qualifications) ? "" : " — " + d.Qualifications)
                    + " · " + string.Join(", ", SchedulingService.DoctorSpecialtyIds(d.UserId).Select(HospitalService.SpecialtyName))
                    + " · " + SchedulingService.DaysSummary(sched.WorkingDays);
                DoctorList.Items.Add(new ListItem(" " + label, d.UserId));
            }
            if (DoctorList.Items.Count > 0) DoctorList.SelectedIndex = 0;
            foreach (var t in SchedulingService.ListAppointmentTypes(Hid, false).Where(t => t.Code != "PROC"))
                VisitType.Items.Add(new ListItem(t.Name + (t.DurationMinutes.HasValue ? " (" + t.DurationMinutes + " min)" : ""), t.Code));
            VisitDate.Text = IsoDate(DateTime.Today);
            VisitDate.Attributes["min"] = IsoDate(DateTime.Today);
            var questions = SchedulingService.ListIntakeQuestions(Hid);
            QuestionsRepeater.DataSource = questions;
            QuestionsRepeater.DataBind();
            IntakeCard.Visible = questions.Count > 0;
            BindSlots();
        }

        protected void Options_Changed(object sender, EventArgs e) { BindSlots(); }

        private void BindSlots()
        {
            SlotList.Items.Clear();
            SlotMessage.Text = "";
            var date = ParseDate(VisitDate.Text);
            if (string.IsNullOrEmpty(DoctorList.SelectedValue) || !date.HasValue) return;
            if (date.Value < DateTime.Today) { SlotMessage.Text = "<div class=\"text-xs text-danger\">Choose today or a later date.</div>"; return; }
            var type = SchedulingService.GetAppointmentType(Hid, VisitType.SelectedValue);
            var free = SchedulingService.ListAvailableSlots(DoctorList.SelectedValue, Hid, date.Value, type != null ? type.DurationMinutes : null)
                .Where(s => !s.IsBooked && s.SlotStart > DateTime.Now).ToList();
            foreach (var s in free) SlotList.Items.Add(new ListItem(" " + FmtTimeH(s.SlotStart), s.Value));
            if (free.Count == 0)
                SlotMessage.Text = "<div class=\"zone-a-empty\">No free slots — " + HttpUtility.HtmlEncode(SchedulingService.NoSlotReason(SchedulingService.GetDoctorSchedule(DoctorList.SelectedValue, Hid, date.Value))) + ". Try another date.</div>";
        }

        private string FmtTimeH(DateTime t) { return HospitalService.FormatTime(Hid, t); }

        protected void QuestionsRepeater_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var q = e.Item.DataItem as IntakeQuestion;
            if (q == null) return;
            var text = (TextBox)e.Item.FindControl("AnswerText");
            var choice = (DropDownList)e.Item.FindControl("AnswerChoice");
            if (q.QuestionType == "CHOICE")
            {
                text.Visible = false;
                choice.Visible = true;
                choice.Items.Add(new ListItem("—", ""));
                foreach (var o in (q.Options ?? "").Split(',').Where(x => x.Trim().Length > 0)) choice.Items.Add(o.Trim());
            }
            else if (q.QuestionType == "NUMBER") text.TextMode = TextBoxMode.Number;
        }

        private Patient FindPatient()
        {
            var mrn = PatientMrn.Text.Trim();
            var phone = PatientPhone.Text.Trim();
            var all = PatientService.Search(Hid, "");
            if (mrn.Length > 0) return all.FirstOrDefault(p => string.Equals(p.Mrn, mrn, StringComparison.OrdinalIgnoreCase));
            if (phone.Length > 0)
            {
                var digits = new string(phone.Where(char.IsDigit).ToArray());
                return all.FirstOrDefault(p => !string.IsNullOrEmpty(p.Phone) && new string(p.Phone.Where(char.IsDigit).ToArray()) == digits);
            }
            return null;
        }

        protected void BookingValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            if (PatientMrn.Text.Trim().Length == 0 && PatientPhone.Text.Trim().Length == 0) { Fail(args, "Enter your " + PatientIdLabelText + " or phone number."); return; }
            _patient = FindPatient();
            if (_patient == null) { Fail(args, "We couldn't find a patient with those details. Check the number or register first."); return; }
            if (string.IsNullOrEmpty(DoctorList.SelectedValue)) { Fail(args, "Choose a doctor."); return; }
            if (string.IsNullOrEmpty(SlotList.SelectedValue)) { Fail(args, "Choose a time slot."); return; }
            args.IsValid = true;
        }

        private void Fail(ServerValidateEventArgs args, string message)
        {
            args.IsValid = false;
            BookingValidator.Text = HttpUtility.HtmlEncode(message);
        }

        protected void BookButton_Click(object sender, EventArgs e)
        {
            if (!IsValid) return;
            var parts = SlotList.SelectedValue.Split('|');
            var start = DateTime.ParseExact(parts[0], "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
            var end = DateTime.ParseExact(parts[1], "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
            var stillFree = SchedulingService.ListAvailableSlots(DoctorList.SelectedValue, Hid, start.Date, (int)(end - start).TotalMinutes).Any(s => !s.IsBooked && s.SlotStart == start);
            if (!stillFree)
            {
                BookingValidator.IsValid = false;
                BookingValidator.Text = "That time was just taken — please choose another slot.";
                BindSlots();
                return;
            }
            var appt = SchedulingService.CreateAppointment(Hid, _patient.PatientId, DoctorList.SelectedValue, start, end, VisitType.SelectedValue, "Booked online");
            var answers = new List<IntakeAnswer>();
            foreach (RepeaterItem item in QuestionsRepeater.Items)
            {
                var qid = ((HiddenField)item.FindControl("QuestionId")).Value;
                var q = SchedulingService.ListIntakeQuestions(Hid).FirstOrDefault(x => x.QuestionId == qid);
                var text = (TextBox)item.FindControl("AnswerText");
                var choice = (DropDownList)item.FindControl("AnswerChoice");
                var answer = choice.Visible ? choice.SelectedValue : text.Text.Trim();
                if (q != null && answer.Length > 0) answers.Add(new IntakeAnswer { QuestionId = qid, Label = q.Label, Answer = answer });
            }
            if (answers.Count > 0) SchedulingService.SubmitIntake(Hid, appt.AppointmentId, _patient.PatientId, answers);
            AuditService.Record(Hid, null, "CREATE", "Appointment", appt.AppointmentId, "Booked online by patient " + _patient.Mrn);
            BookingPanel.Visible = false;
            ConfirmPanel.Visible = true;
            ConfirmText.Text = "<p class=\"text-secondary\">" + HttpUtility.HtmlEncode(_patient.FullName) + " with <strong>" + HttpUtility.HtmlEncode(UserService.UserName(appt.DoctorId)) + "</strong></p>"
                + "<div style=\"font-size:20px; font-weight:700; color:var(--color-primary);\">" + HttpUtility.HtmlEncode(HospitalService.FormatDate(Hid, start) + " · " + FmtTimeH(start)) + "</div>"
                + "<p class=\"text-xs text-muted mt-1\">Reference " + HttpUtility.HtmlEncode(appt.AppointmentId) + (answers.Count > 0 ? " · intake answers received" : "") + "</p>";
        }
    }
}
