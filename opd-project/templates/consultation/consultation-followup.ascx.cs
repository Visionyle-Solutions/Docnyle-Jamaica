using System;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>Next visit: saves the follow-up on the consultation and (optionally) books the first free FOLLOWUP slot that day.</summary>
    public partial class consultation_followup : ConsultationSection
    {
        protected override string SectionKey { get { return "followup"; } }

        protected override void ApplyFormConfig()
        {
            NotesHolder.Visible = FieldOn("instructions");
            AutoBook.Visible = FieldOn("autoBook");
            SkipFollowupButton.Visible = FieldOn("skip");
        }

        public override void Bind()
        {
            var c = ConsultationService.Get(ConsultationId);
            FollowupDate.Text = c.FollowupDate.HasValue ? c.FollowupDate.Value.ToString("yyyy-MM-dd") : "";
            FollowupNotes.Text = c.FollowupNotes;
            FormPanel.Visible = !Host.IsLocked;
            SavedText.Text = c.FollowupDate.HasValue
                ? "<div class=\"zone-a-item mt-2\"><i class=\"ti ti-calendar-check\" aria-hidden=\"true\"></i> Follow-up on <strong>" + HttpUtility.HtmlEncode(AppPage.FmtDate(c.FollowupDate.Value)) + "</strong>"
                  + (string.IsNullOrEmpty(c.FollowupNotes) ? "" : " — " + HttpUtility.HtmlEncode(c.FollowupNotes)) + "</div>"
                : "";
        }

        protected void FollowupDate_ServerValidate(object source, ServerValidateEventArgs args)
        {
            DateTime d;
            args.IsValid = DateTime.TryParse(args.Value, out d) && d.Date > DateTime.Today;
        }

        protected void SaveFollowupButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("followup");
            if (!EnsureNotLocked()) return;
            Page.Validate("Followup");
            if (!Page.IsValid) return;
            var date = DateTime.Parse(FollowupDate.Text).Date;
            var notes = FieldOn("instructions") ? FollowupNotes.Text.Trim() : "";
            ConsultationService.SaveFollowup(ConsultationId, date, notes);
            Audit("UPDATE", "ConsultationFollowup", date.ToString("yyyy-MM-dd"));
            if (AutoBook.Checked && FieldOn("autoBook"))
            {
                var type = SchedulingService.GetAppointmentType(HospitalId, "FOLLOWUP");
                var slot = SchedulingService.ListAvailableSlots(Host.DoctorId, HospitalId, date, type != null ? type.DurationMinutes : null).FirstOrDefault(s => !s.IsBooked);
                if (slot != null)
                {
                    SchedulingService.CreateAppointment(HospitalId, Visit.PatientId, Host.DoctorId, slot.SlotStart, slot.SlotEnd, "FOLLOWUP",
                        string.IsNullOrEmpty(notes) ? "Auto-booked from follow-up" : notes);
                    AppPage.ToastSuccess("Follow-up saved & appointment booked", AppPage.FmtDate(date) + " at " + AppPage.FmtTime(slot.SlotStart));
                }
                else
                {
                    var reason = SchedulingService.NoSlotReason(SchedulingService.GetDoctorSchedule(Host.DoctorId, HospitalId, date));
                    AppPage.ToastWarning("Follow-up saved — no slot to auto-book", reason + ". Book it from Appointments.");
                }
            }
            else AppPage.ToastSuccess("Follow-up saved");
            Host.RefreshAll();
        }

        protected void SkipFollowupButton_Click(object sender, EventArgs e)
        {
            ConsultationService.SkipStep(ConsultationId, "followup");
            AppPage.ToastSuccess("Marked as not needed");
            Host.RefreshAll();
        }
    }
}
