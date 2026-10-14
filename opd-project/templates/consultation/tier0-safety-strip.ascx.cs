using System;
using System.Linq;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>
    /// Allergy / active-medication / continuity alerts pinned at the top of the Clinical Intelligence panel.
    /// Safety information is recomputed on every request (it must never show stale or empty data).
    /// </summary>
    public partial class tier0_safety_strip : ConsultationSection
    {
        protected string AllergyTone { get; private set; }
        protected string AllergyIcon { get; private set; }
        protected string AllergyText { get; private set; }
        protected string MedsText { get; private set; }
        protected string MedNames { get; private set; }
        protected string ContinuityText { get; private set; }

        /// <summary>Nothing to cache — the strip is rendered fresh in <see cref="OnPreRender"/>.</summary>
        public override void Bind() { }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            if (Host.Consultation == null) return;
            var allergies = PatientService.ListAllergies(Visit.PatientId, true);
            AllergyTone = allergies.Any(a => a.Severity == "SEVERE") ? "danger" : allergies.Count > 0 ? "warning" : "ok";
            AllergyIcon = allergies.Count == 0 ? "ti-shield-check" : "ti-alert-triangle";
            AllergyText = allergies.Count == 0 ? "No Known Allergies" : string.Join(" | ", allergies.Select(a => a.AllergyName + " · " + OpdProject.Web.BasePage.TitleCase(a.Severity)));
            var meds = ConsultationService.ListActiveMedications(Visit.PatientId);
            MedsText = meds.Count + " Active Medication" + (meds.Count == 1 ? "" : "s");
            MedNames = string.Join(", ", meds.Take(4).Select(m => m.MedicineName + (string.IsNullOrEmpty(m.Strength) ? "" : " " + m.Strength)))
                + (meds.Count > 4 ? " +" + (meds.Count - 4) + " More" : "");
            MedNamesHolder.Visible = meds.Count > 0;

            /* Continuity flag: the follow-up instruction left at the last signed visit */
            var last = ConsultationService.ListPastVisits(Visit.PatientId).FirstOrDefault(v => v.ConsultationId != ConsultationId);
            ContinuityText = last != null && !string.IsNullOrEmpty(last.FollowupNotes) ? last.FollowupNotes : null;
            ContinuityHolder.Visible = ContinuityText != null;
        }
    }
}
