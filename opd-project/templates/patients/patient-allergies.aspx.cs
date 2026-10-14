using System;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Patient Health Record: allergies, medical history, current medications and family &amp; social history,
    /// one tab at a time (?tab=…). The summary tiles and tab counts are computed after the request's events.
    /// </summary>
    public partial class patient_allergies : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Health Record"; } }

        private string PatientId { get { return RouteValue("patientId"); } }

        protected string ActiveTab
        {
            get
            {
                var tab = Request.QueryString["tab"];
                if (tab == HealthRecord.VisitsTab && CanSeeVisits) return tab;
                return HealthRecord.Tabs.Contains(tab) ? tab : "allergies";
            }
        }

        protected int AllergyCount { get; private set; }
        protected int HistoryCount { get; private set; }
        protected int MedsCount { get; private set; }
        protected int FamilyCount { get; private set; }
        protected int VisitsCount { get; private set; }

        /// <summary>Past visits (with diagnoses) are clinical detail: doctors and nurses only.</summary>
        protected bool CanSeeVisits { get { return HealthRecord.CanSeeVisits(Role); } }
        protected string AllergySummary { get; private set; }
        protected string ConditionSummary { get; private set; }
        protected string MedsSummary { get; private set; }
        protected string FamilySummary { get; private set; }

        /* Post-registration walkthrough (?new=1): Allergies → Medical History → patient list */
        private bool IsOnboarding { get { return Request.QueryString["new"] == "1"; } }

        /// <summary>Title and guidance per step, in <see cref="HealthRecord.Tabs"/> order ({0} = patient name).</summary>
        private static readonly string[][] OnboardingSteps =
        {
            new[] { "Allergies", "Record any known allergies for {0} (leave empty if none), then continue." },
            new[] { "Medical history", "Add chronic conditions and past surgeries for {0}, then continue." },
            new[] { "Current medications", "Record medicines {0} already takes from other doctors or over the counter (leave empty if none)." },
            new[] { "Family & social", "Add family history and lifestyle details for {0}, then finish." },
        };
        protected string OnboardingTitle { get; private set; }
        protected string OnboardingText { get; private set; }
        protected string OnboardingNextUrl { get; private set; }
        protected string OnboardingNextLabel { get; private set; }

        protected string TabUrl(string tab)
        {
            var query = (tab == "allergies" ? "" : "tab=" + HttpUtility.UrlEncode(tab)) + (IsOnboarding ? (tab == "allergies" ? "" : "&") + "new=1" : "");
            return Urls.Allergies(PatientId) + (query.Length > 0 ? "?" + query : "");
        }

        protected void Page_Init(object sender, EventArgs e)
        {
            Body.EditRequested += Body_EditRequested;
            AllergyForm.Saved += (s, a) => Body.Bind();
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            var patient = PatientService.Get(PatientId);
            if (patient == null || patient.HospitalId != HospitalId)
            {
                NotFound.Visible = true;
                Workspace.Visible = false;
                return;
            }
            Strip.PatientId = Body.PatientId = AllergyForm.PatientId = PatientId;
            History.PatientId = Medications.PatientId = FamilySocial.PatientId = Visits.PatientId = PatientId;
            ConsultLink.NavigateUrl = Urls.Consultation(PatientId);
            ConsultLink.Visible = Role == "DOCTOR" && Can("consultation");

            AllergiesTab.Visible = ActiveTab == "allergies";
            HistoryTabHolder.Visible = ActiveTab == "history";
            MedsTabHolder.Visible = ActiveTab == "meds";
            FamilyTabHolder.Visible = ActiveTab == "family";
            VisitsTabHolder.Visible = ActiveTab == HealthRecord.VisitsTab;

            /* Walkthrough follows the tab order: one step per tab, the last one finishes at the patient list */
            var step = Array.IndexOf(HealthRecord.Tabs, ActiveTab);
            OnboardingBanner.Visible = IsOnboarding && step >= 0;
            if (step < 0) step = 0;
            var last = step == HealthRecord.Tabs.Length - 1;
            OnboardingTitle = (step == 0 ? "Patient registered · " : "") + "Step " + (step + 1) + " of " + HealthRecord.Tabs.Length + " · " + OnboardingSteps[step][0];
            OnboardingText = string.Format(OnboardingSteps[step][1], patient.FullName);
            OnboardingNextUrl = last ? Urls.PatientSearch : Urls.Allergies(PatientId) + "?tab=" + HealthRecord.Tabs[step + 1] + "&new=1";
            OnboardingNextLabel = last ? "Finish" : "Next: " + OnboardingSteps[step + 1][0];
            if (IsPostBack) return;
            switch (ActiveTab)
            {
                case "history": History.Bind(); break;
                case "meds": Medications.Bind(); break;
                case "family": FamilySocial.Bind(); break;
                case HealthRecord.VisitsTab: Visits.Bind(); break;
                default: Body.Bind(); break;
            }
        }

        private void Body_EditRequested(object sender, CommandEventArgs e)
        {
            AllergyForm.LoadAllergy((string)e.CommandArgument);
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            if (!Workspace.Visible) return;
            var allergies = PatientService.ListAllergies(PatientId, true);
            var conditions = PatientService.ListMedicalHistory(PatientId);
            var events = PatientService.ListMedicalEvents(PatientId);
            var meds = ConsultationService.ListActiveMedications(PatientId);
            var family = PatientService.ListFamilyHistory(PatientId);
            var patient = PatientService.Get(PatientId);

            AllergyCount = allergies.Count;
            HistoryCount = conditions.Count + events.Count;
            MedsCount = meds.Count;
            FamilyCount = family.Count;
            VisitsCount = CanSeeVisits ? ConsultationService.ListPastVisits(PatientId).Count : 0;

            AllergySummary = allergies.Count == 0 ? "No known allergies"
                : string.Join(", ", allergies.Select(a => a.AllergyName + " · " + Title(a.Severity)));
            var active = conditions.Where(c => c.Status != "RESOLVED").Select(c => c.ConditionName == "OTHER" ? c.Notes : c.ConditionName).ToList();
            ConditionSummary = active.Count == 0 ? "None recorded" : string.Join(", ", active);
            MedsSummary = meds.Count == 0 ? "None" : meds.Count + " active · " + string.Join(", ", meds.Select(m => m.MedicineName));
            FamilySummary = family.Count > 0 ? string.Join(" · ", family.Select(f => Title(f.RelationshipType) + ": " + f.ConditionName))
                : !string.IsNullOrEmpty(patient.SmokingStatus) ? "Smoking: " + Title(patient.SmokingStatus) : "—";
        }

        private static string Title(string code)
        {
            return string.IsNullOrEmpty(code) ? "" : code.Substring(0, 1).ToUpperInvariant() + code.Substring(1).ToLowerInvariant();
        }
    }
}
