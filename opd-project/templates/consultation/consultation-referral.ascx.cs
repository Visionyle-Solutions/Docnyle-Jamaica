using System;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>Specialist referral (validated: specialty and reason required) or "not needed".</summary>
    public partial class consultation_referral : ConsultationSection
    {
        protected override string SectionKey { get { return "referral"; } }

        protected override void ApplyFormConfig()
        {
            DoctorNameHolder.Visible = FieldOn("doctorName");
            UrgencyHolder.Visible = FieldOn("urgency");
            ApplyOptions("urgency", ReferralUrgency);
            SkipReferralButton.Visible = FieldOn("skip");
        }

        public override void Bind()
        {
            FormPanel.Visible = !Host.IsLocked;
            ReferralsRepeater.DataSource = ConsultationService.ListReferrals(ConsultationId);
            ReferralsRepeater.DataBind();
        }

        protected void AddReferralButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("referral");
            if (!EnsureNotLocked()) return;
            Page.Validate("Referral");
            if (!Page.IsValid) return;
            var r = ConsultationService.AddReferral(Visit, ReferralSpecialty.Text.Trim(), FieldOn("doctorName") ? ReferralDoctorName.Text.Trim() : "",
                FieldOn("urgency") ? ReferralUrgency.SelectedValue : "ROUTINE", ReferralReason.Text.Trim());
            Audit("CREATE", "Referral", r.Specialty + ": " + r.Reason);
            ReferralSpecialty.Text = ReferralDoctorName.Text = ReferralReason.Text = "";
            AppPage.ToastSuccess("Referral added", "→ " + r.Specialty);
            Host.RefreshAll();
        }

        protected void SkipReferralButton_Click(object sender, EventArgs e)
        {
            ConsultationService.SkipStep(ConsultationId, "referral");
            AppPage.ToastSuccess("Marked as not needed");
            Host.ActivateStep("followup");
            Host.RefreshAll();
        }
    }
}
