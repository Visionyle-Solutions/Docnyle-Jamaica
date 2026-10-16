using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.Web
{
    public class DayWindows
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public List<DoctorSlotConfig> Windows { get; set; }
    }
}

namespace OpdProject.templates.settings
{
    /// <summary>
    /// Schedule Settings. All inputs are validated on the server; booking (slot engine) and billing
    /// (consultation charge) read these values directly, so saves take effect immediately.
    /// </summary>
    public partial class schedule_settings : BasePage
    {
        protected override string RequiredPermission { get { return "admin_scheduling"; } }
        public override string PageTitle { get { return "Schedule Settings"; } }

        private static readonly string[][] Tabs =
        {
            new[] { "hospital", "Hospital Defaults", "ti-building-hospital" }, new[] { "doctors", "Doctors", "ti-stethoscope" },
            new[] { "types", "Appointment Types", "ti-calendar-event" }, new[] { "intake", "Intake & Consent", "ti-clipboard-check" },
        };

        protected string ActiveTab
        {
            get
            {
                var t = Request.QueryString["tab"] ?? "hospital";
                return Tabs.Any(x => x[0] == t) ? t : "hospital";
            }
        }

        /// <summary>Doctors can edit only their own profile/timetable; administrators can edit everyone.</summary>
        private bool IsAdmin { get { return HasRole("ADMIN"); } }

        protected string SelectedDoctorId
        {
            get { return ViewState["doctor"] as string; }
            set { ViewState["doctor"] = value; }
        }

        protected string RegLabel
        {
            get
            {
                switch (Hospital.Country) { case "IN": return "NMC Reg. No."; case "JM": return "MCJ Reg. No."; default: return "Registration No."; }
            }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            TabsRepeater.DataSource = Tabs.Where(t => IsAdmin || t[0] == "doctors").Select(t => new NavItem { Path = t[0], Label = t[1], Icon = t[2] }).ToList();
            TabsRepeater.DataBind();
            var tab = IsAdmin ? ActiveTab : "doctors";
            HospitalTab.Visible = tab == "hospital";
            DoctorsTab.Visible = tab == "doctors";
            TypesTab.Visible = tab == "types";
            IntakeTab.Visible = tab == "intake";
            AddDoctorButton.Visible = IsAdmin;
            if (IsPostBack) return;
            switch (tab)
            {
                case "hospital": BindDefaults(); break;
                case "doctors": BindDoctors(); if (!IsAdmin) OpenSchedule(UserId); break;
                case "types": BindTypes(); break;
                case "intake": BindIntake(); break;
            }
        }

        /* ═══ Hospital defaults ═══ */

        private void BindDefaults()
        {
            var s = HospitalService.GetSettings(HospitalId);
            SlotMins.Text = s["APPT_SLOT_MINS"];
            BufferMins.Text = s["APPT_BUFFER_MINS"];
            FreeAllowed.Checked = s["FREE_FOLLOWUP_ALLOWED"] == "true";
            FreeDays.Text = s["FREE_FOLLOWUP_DAYS"];
        }

        protected void SaveDefaultsButton_Click(object sender, EventArgs e)
        {
            Validate("Defaults");
            if (!IsValid || !IsAdmin) return;
            HospitalService.SaveSettings(HospitalId, new Dictionary<string, string>
            {
                { "APPT_SLOT_MINS", SlotMins.Text.Trim() }, { "APPT_BUFFER_MINS", BufferMins.Text.Trim().Length == 0 ? "0" : BufferMins.Text.Trim() },
                { "FREE_FOLLOWUP_ALLOWED", FreeAllowed.Checked ? "true" : "false" }, { "FREE_FOLLOWUP_DAYS", FreeDays.Text.Trim().Length == 0 ? "0" : FreeDays.Text.Trim() },
            });
            Audit("UPDATE", "HospitalSetting", HospitalId, "Slot " + SlotMins.Text + " min, buffer " + BufferMins.Text + " min, free follow-up " + (FreeAllowed.Checked ? "within " + FreeDays.Text + " days" : "off"));
            ToastSuccess("Hospital defaults saved");
        }

        /* ═══ Doctors ═══ */

        private void BindDoctors()
        {
            var doctors = UserService.ListActiveDoctors(HospitalId);
            if (!IsAdmin) doctors = doctors.Where(d => d.UserId == UserId).ToList();
            DoctorsRepeater.DataSource = doctors;
            DoctorsRepeater.DataBind();
        }

        protected string SpecialtyNames(string doctorId)
        {
            return string.Join(", ", SchedulingService.DoctorSpecialtyIds(doctorId).Select(HospitalService.SpecialtyName));
        }

        protected string DaysText(string doctorId) { return SchedulingService.DaysSummary(SchedulingService.GetDoctorSchedule(doctorId, HospitalId, Today).WorkingDays); }

        protected string SlotText(string doctorId)
        {
            var own = SchedulingService.ResolveSetting(HospitalId, doctorId, "APPT_SLOT_MINS").Source == "DOCTOR";
            return SchedulingService.SlotMinutes(HospitalId, doctorId) + " min (" + (own ? "own" : "hospital") + ")";
        }

        protected string ChargeText(string doctorId)
        {
            var c = SchedulingService.GetDoctorCharge(HospitalId, doctorId);
            return c.Model == "UNIT" ? c.UnitRate.ToString("0.##", CultureInfo.InvariantCulture) + " / " + c.UnitMinutes + " min"
                : "New " + c.NewFee.ToString("0.##", CultureInfo.InvariantCulture) + " · F/U " + c.FollowupFee.ToString("0.##", CultureInfo.InvariantCulture);
        }

        protected void DoctorsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var id = (string)e.CommandArgument;
            if (!IsAdmin && id != UserId) return;
            if (e.CommandName == "Profile") OpenProfile(id);
            else OpenSchedule(id);
            BindDoctors();
        }

        protected void ClosePanels_Click(object sender, EventArgs e)
        {
            ProfilePanel.Visible = SchedulePanel.Visible = false;
            SelectedDoctorId = null;
            BindDoctors();
        }

        private void FillSpecialties(IEnumerable<string> selected)
        {
            DpSpecialties.Items.Clear();
            var set = new HashSet<string>(selected);
            foreach (var s in HospitalService.ListSpecialties(HospitalId, true)) DpSpecialties.Items.Add(new ListItem(" " + s.SpecialtyName, s.SpecialtyId) { Selected = set.Contains(s.SpecialtyId) });
        }

        protected void AddDoctorButton_Click(object sender, EventArgs e)
        {
            if (!IsAdmin) return;
            SelectedDoctorId = null;
            ProfileTitle.Text = "New Doctor";
            foreach (var tb in new[] { DpFirst, DpMiddle, DpLast, DpQualifications, DpDisplayOverride, DpLicense, DpRoom, DpEmail, DpPhone, DpLanguages, DpBio }) tb.Text = "";
            DpPrefix.Text = "Dr.";
            FillSpecialties(new string[0]);
            ProfilePanel.Visible = true;
            SchedulePanel.Visible = false;
        }

        private void OpenProfile(string doctorId)
        {
            var d = UserService.GetUser(doctorId);
            if (d == null) return;
            SelectedDoctorId = doctorId;
            ProfileTitle.Text = "Profile — " + Server.HtmlEncode(d.FullName);
            DpPrefix.Text = d.Prefix; DpFirst.Text = d.FirstName; DpMiddle.Text = d.MiddleName; DpLast.Text = d.LastName;
            DpQualifications.Text = d.Qualifications; DpDisplayOverride.Text = d.DisplayNameOverride; DpLicense.Text = d.LicenseNumber;
            DpRoom.Text = d.Room; DpEmail.Text = d.Email; DpPhone.Text = d.Phone; DpLanguages.Text = d.Languages; DpBio.Text = d.Bio;
            FillSpecialties(SchedulingService.DoctorSpecialtyIds(doctorId));
            ProfilePanel.Visible = true;
            SchedulePanel.Visible = false;
        }

        protected void DoctorEmailUnique_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = !UserService.EmailInUse(args.Value.Trim(), SelectedDoctorId);
        }

        protected void SpecialtiesRequired_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = DpSpecialties.Items.Cast<ListItem>().Any(i => i.Selected);
        }

        protected void SaveProfileButton_Click(object sender, EventArgs e)
        {
            if (!IsAdmin && SelectedDoctorId != UserId) return;
            Validate("DoctorProfile");
            if (!IsValid) { ProfilePanel.Visible = true; return; }
            var isNew = SelectedDoctorId == null;
            var saved = SchedulingService.SaveDoctorProfile(HospitalId, new AppUser
            {
                UserId = SelectedDoctorId, Prefix = DpPrefix.Text.Trim(), FirstName = DpFirst.Text.Trim(), MiddleName = DpMiddle.Text.Trim(), LastName = DpLast.Text.Trim(),
                Qualifications = DpQualifications.Text.Trim(), DisplayNameOverride = DpDisplayOverride.Text.Trim(), LicenseNumber = DpLicense.Text.Trim(),
                Email = DpEmail.Text.Trim(), Phone = DpPhone.Text.Trim(), Room = DpRoom.Text.Trim(), Languages = DpLanguages.Text.Trim(), Bio = DpBio.Text.Trim(),
            }, DpSpecialties.Items.Cast<ListItem>().Where(i => i.Selected).Select(i => i.Value).ToList());
            Audit(isNew ? "CREATE" : "UPDATE", "DoctorProfile", saved.UserId, saved.FullName);
            ToastSuccess(isNew ? "Doctor added" : "Doctor saved", saved.FullName + (isNew ? " — set the weekly timetable next" : ""));
            if (isNew) OpenSchedule(saved.UserId);
            else ProfilePanel.Visible = false;
            BindDoctors();
        }

        private void OpenSchedule(string doctorId)
        {
            var d = UserService.GetUser(doctorId);
            if (d == null) return;
            SelectedDoctorId = doctorId;
            ScheduleTitle.Text = "Scheduling & Charges — " + Server.HtmlEncode(d.FullName);
            BindWeek();
            var settings = SchedulingService.GetDoctorSettings(HospitalId, doctorId);
            HospitalSlotText.Text = HospitalService.GetSetting(HospitalId, "APPT_SLOT_MINS") + " min";
            OvSlot.Text = settings["APPT_SLOT_MINS"].Source == "DOCTOR" ? settings["APPT_SLOT_MINS"].Value : "";
            OvFreeAllowed.SelectedValue = settings["FREE_FOLLOWUP_ALLOWED"].Source == "DOCTOR" ? settings["FREE_FOLLOWUP_ALLOWED"].Value : "";
            OvFreeDays.Text = settings["FREE_FOLLOWUP_DAYS"].Source == "DOCTOR" ? settings["FREE_FOLLOWUP_DAYS"].Value : "";
            var charge = SchedulingService.GetDoctorCharge(HospitalId, doctorId);
            ChModel.SelectedValue = charge.Model == "UNIT" ? "UNIT" : "FLAT";
            ChNewFee.Text = charge.NewFee.ToString("0.##", CultureInfo.InvariantCulture);
            ChFollowFee.Text = charge.FollowupFee.ToString("0.##", CultureInfo.InvariantCulture);
            ChUnitMins.Text = charge.UnitMinutes.ToString();
            ChUnitRate.Text = charge.UnitRate.ToString("0.##", CultureInfo.InvariantCulture);
            UnitHolder.Visible = ChModel.SelectedValue == "UNIT";
            BindLeave();
            SchedulePanel.Visible = true;
            ProfilePanel.Visible = false;
        }

        private static readonly Dictionary<string, string> DayLabels = new Dictionary<string, string>
        {
            { "MON", "Monday" }, { "TUE", "Tuesday" }, { "WED", "Wednesday" }, { "THU", "Thursday" }, { "FRI", "Friday" }, { "SAT", "Saturday" }, { "SUN", "Sunday" },
        };

        private void BindWeek()
        {
            var windows = SchedulingService.ListSlotConfig(SelectedDoctorId, HospitalId);
            WeekRepeater.DataSource = SchedulingService.WeekDays.Select(d => new DayWindows { Code = d, Label = DayLabels[d], Windows = windows.Where(w => w.DayOfWeek == d).ToList() }).ToList();
            WeekRepeater.DataBind();
        }

        protected void WeekRepeater_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var day = e.Item.DataItem as DayWindows;
            if (day == null) return;
            var inner = (Repeater)e.Item.FindControl("WindowsRepeater");
            inner.DataSource = day.Windows;
            inner.DataBind();
            ((Literal)e.Item.FindControl("ClosedText")).Text = day.Windows.Count == 0 ? "<span class=\"text-xs text-muted\">Closed</span>" : "";
        }

        protected void WeekRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "AddWindow" || (!IsAdmin && SelectedDoctorId != UserId)) return;
            var day = (string)e.CommandArgument;
            var start = ((TextBox)e.Item.FindControl("WinStart")).Text.Trim();
            var end = ((TextBox)e.Item.FindControl("WinEnd")).Text.Trim();
            DateTime s, en;
            if (!DateTime.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out s) || !DateTime.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out en))
            { ToastWarning("Enter a start and end time"); return; }
            if (en <= s) { ToastWarning("End time must be after the start time"); return; }
            if ((en - s).TotalMinutes < SchedulingService.SlotMinutes(HospitalId, SelectedDoctorId)) { ToastWarning("The window is shorter than one slot"); return; }
            if (SchedulingService.WindowOverlaps(SelectedDoctorId, HospitalId, day, start, end)) { ToastWarning("That window overlaps an existing window on " + DayLabels[day]); return; }
            SchedulingService.AddSlotWindow(SelectedDoctorId, HospitalId, day, start, end);
            Audit("UPDATE", "DoctorSlotConfig", SelectedDoctorId, "Added " + day + " " + start + "–" + end);
            ToastSuccess("Availability added", DayLabels[day] + " " + start + "–" + end);
            BindWeek();
            BindDoctors();
        }

        protected void WindowsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "RemoveWindow" || (!IsAdmin && SelectedDoctorId != UserId)) return;
            SchedulingService.RemoveSlotWindow((string)e.CommandArgument);
            Audit("UPDATE", "DoctorSlotConfig", SelectedDoctorId, "Removed window " + e.CommandArgument);
            ToastSuccess("Availability removed");
            BindWeek();
            BindDoctors();
        }

        protected void ChModel_Changed(object sender, EventArgs e)
        {
            UnitHolder.Visible = ChModel.SelectedValue == "UNIT";
            SchedulePanel.Visible = true;
        }

        protected void SaveOverridesButton_Click(object sender, EventArgs e)
        {
            if (!IsAdmin && SelectedDoctorId != UserId) return;
            Validate("Overrides");
            if (!IsValid) { SchedulePanel.Visible = true; return; }
            SchedulingService.SaveDoctorSettings(HospitalId, SelectedDoctorId, new Dictionary<string, string>
            {
                { "APPT_SLOT_MINS", OvSlot.Text.Trim() }, { "FREE_FOLLOWUP_ALLOWED", OvFreeAllowed.SelectedValue }, { "FREE_FOLLOWUP_DAYS", OvFreeDays.Text.Trim() },
            });
            SchedulingService.SaveDoctorCharge(HospitalId, SelectedDoctorId, ChModel.SelectedValue, ParseMoney(ChNewFee.Text), ParseMoney(ChFollowFee.Text),
                ParseInt(ChUnitMins.Text, 15), ParseMoney(ChUnitRate.Text));
            Audit("UPDATE", "DoctorSetting", SelectedDoctorId, "Overrides & charges: " + ChargeText(SelectedDoctorId));
            ToastSuccess("Overrides & charges saved");
            BindDoctors();
        }

        private void BindLeave()
        {
            var leave = SchedulingService.ListLeave(SelectedDoctorId, HospitalId);
            LeaveRepeater.DataSource = leave;
            LeaveRepeater.DataBind();
            NoLeave.Text = leave.Count == 0 ? "<span class=\"text-xs text-muted\">No leave scheduled.</span>" : "";
        }

        protected void AddLeaveButton_Click(object sender, EventArgs e)
        {
            if (!IsAdmin && SelectedDoctorId != UserId) return;
            Validate("Leave");
            if (!IsValid) return;
            var date = ParseDate(LeaveDate.Text);
            if (!date.HasValue || date.Value < Today) { ToastWarning("Leave date must be today or later"); return; }
            if (!SchedulingService.AddLeave(SelectedDoctorId, HospitalId, date.Value, LeaveReason.Text.Trim())) { ToastWarning("Leave already recorded for that date"); return; }
            var booked = SchedulingService.ListAppointments(HospitalId, SelectedDoctorId, date.Value).Count(a => a.Status == "BOOKED" || a.Status == "CHECKED_IN");
            Audit("CREATE", "DoctorLeave", SelectedDoctorId, date.Value.ToString("yyyy-MM-dd") + " " + LeaveReason.Text.Trim());
            if (booked > 0) ToastWarning("Leave added — " + booked + " appointment(s) already booked that day", "Reschedule them from the Appointments calendar.");
            else ToastSuccess("Leave added", FmtDate(date.Value));
            LeaveDate.Text = LeaveReason.Text = "";
            BindLeave();
        }

        protected void LeaveRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (!IsAdmin && SelectedDoctorId != UserId) return;
            SchedulingService.RemoveLeave((string)e.CommandArgument);
            Audit("DELETE", "DoctorLeave", SelectedDoctorId, (string)e.CommandArgument);
            ToastSuccess("Leave removed");
            BindLeave();
        }

        /* ═══ Appointment types ═══ */

        private void BindTypes()
        {
            TypesRepeater.DataSource = SchedulingService.ListAppointmentTypes(HospitalId, true);
            TypesRepeater.DataBind();
        }

        protected void TypesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var t = SchedulingService.GetAppointmentTypeById((string)e.CommandArgument);
            if (t == null || t.HospitalId != HospitalId) return;
            if (e.CommandName == "Toggle")
            {
                if (t.IsActive && SchedulingService.ListAppointmentTypes(HospitalId, false).Count == 1) { ToastWarning("At least one appointment type must stay active"); return; }
                SchedulingService.ToggleAppointmentType(t.AppointmentTypeId);
                Audit("UPDATE", "AppointmentType", t.AppointmentTypeId, t.Name + (t.IsActive ? " activated" : " deactivated"));
                ToastSuccess(t.Name + (t.IsActive ? " activated" : " deactivated"));
                BindTypes();
                return;
            }
            EditingTypeId.Value = t.AppointmentTypeId;
            TypeFormTitle.Text = "Edit: " + Server.HtmlEncode(t.Name);
            TypeName.Text = t.Name;
            TypeDuration.Text = t.DurationMinutes.HasValue ? t.DurationMinutes.Value.ToString() : "";
            TypeColor.Text = t.Color;
            TypeFeeCategory.SelectedValue = t.FeeCategory;
            TypeChargeable.Checked = t.IsChargeable;
            CancelTypeButton.Visible = true;
        }

        protected void CancelTypeButton_Click(object sender, EventArgs e) { ResetTypeForm(); }

        private void ResetTypeForm()
        {
            EditingTypeId.Value = "";
            TypeFormTitle.Text = "Add Appointment Type";
            TypeName.Text = TypeDuration.Text = "";
            TypeColor.Text = "#0f766e";
            TypeFeeCategory.SelectedIndex = 0;
            TypeChargeable.Checked = true;
            CancelTypeButton.Visible = false;
        }

        protected void SaveTypeButton_Click(object sender, EventArgs e)
        {
            Validate("Type");
            if (!IsValid) return;
            int d;
            var saved = SchedulingService.SaveAppointmentType(HospitalId, string.IsNullOrEmpty(EditingTypeId.Value) ? null : EditingTypeId.Value, TypeName.Text.Trim(),
                int.TryParse(TypeDuration.Text, out d) ? d : (int?)null, TypeColor.Text, TypeFeeCategory.SelectedValue, TypeChargeable.Checked);
            Audit(string.IsNullOrEmpty(EditingTypeId.Value) ? "CREATE" : "UPDATE", "AppointmentType", saved.AppointmentTypeId, saved.Name);
            ToastSuccess("Appointment type saved", saved.Name + " (" + saved.Code + ")");
            ResetTypeForm();
            BindTypes();
        }

        /* ═══ Intake & consent ═══ */

        private void BindIntake()
        {
            QuestionsRepeater.DataSource = SchedulingService.ListIntakeQuestions(HospitalId);
            QuestionsRepeater.DataBind();
            var form = SchedulingService.GetConsentForm(HospitalId);
            ConsentTitle.Text = form != null ? form.Title : "";
            ConsentBody.Text = form != null ? form.BodyText : "";
            ConsentVersion.Text = form != null ? form.Version.ToString() : "—";
        }

        protected void ChoiceOptions_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = QuestionType.SelectedValue != "CHOICE" || QuestionOptions.Text.Split(',').Count(x => x.Trim().Length > 0) >= 2;
        }

        protected void AddQuestionButton_Click(object sender, EventArgs e)
        {
            Validate("Question");
            if (!IsValid) return;
            var options = QuestionType.SelectedValue == "CHOICE" ? string.Join(",", QuestionOptions.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)) : "";
            SchedulingService.AddIntakeQuestion(HospitalId, QuestionLabel.Text.Trim(), QuestionType.SelectedValue, options);
            Audit("CREATE", "IntakeQuestion", HospitalId, QuestionLabel.Text.Trim());
            ToastSuccess("Question added");
            QuestionLabel.Text = QuestionOptions.Text = "";
            BindIntake();
        }

        protected void QuestionsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            SchedulingService.RemoveIntakeQuestion((string)e.CommandArgument);
            Audit("DELETE", "IntakeQuestion", (string)e.CommandArgument);
            ToastSuccess("Question removed");
            BindIntake();
        }

        protected void SaveConsentButton_Click(object sender, EventArgs e)
        {
            Validate("Consent");
            if (!IsValid) return;
            var f = SchedulingService.SaveConsentForm(HospitalId, ConsentTitle.Text.Trim(), ConsentBody.Text.Trim());
            Audit("UPDATE", "ConsentForm", f.ConsentFormId, "Version " + f.Version);
            ToastSuccess("Consent text saved", "Now version " + f.Version);
            ConsentVersion.Text = f.Version.ToString();
        }
    }
}
