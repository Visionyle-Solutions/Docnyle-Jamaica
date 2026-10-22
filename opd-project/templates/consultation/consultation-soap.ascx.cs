using System;
using System.Linq;
using System.Text;
using System.Web;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>
    /// SOAP observations with phrase chips, server-side "/trigger" expansion, the Auto-SOAP composer,
    /// personal phrase saving and drawing-pad sketches (posted back as JPEG data).
    /// </summary>
    public partial class consultation_soap : ConsultationSection
    {
        protected override string SectionKey { get { return "soap"; } }

        protected override void ApplyFormConfig()
        {
            SubjectiveHolder.Visible = FieldOn("subjective");
            ObjectiveHolder.Visible = FieldOn("objective");
            PlanHolder.Visible = FieldOn("plan");
            SubjectivePersonalHolder.Visible = ObjectivePersonalHolder.Visible = PlanPersonalHolder.Visible = FieldOn("personal");
            ChipsSHolder.Visible = ChipsOHolder.Visible = ChipsPHolder.Visible = FieldOn("phrases");
            VoiceHolder.Visible = !Host.IsLocked && FieldOn("voice");
            AutoGenerateButton.Visible = FieldOn("autoGenerate");
            if (!FieldOn("autoGenerate")) DraftPanel.Visible = false;
            ShowSavePhraseButton.Visible = FieldOn("savePhrase");
            if (!FieldOn("savePhrase")) SavePhrasePanel.Visible = false;
            SketchesHolder.Visible = FieldOn("sketches");
        }

        private string SpecialtyId
        {
            get
            {
                var doctor = UserService.GetUser(Host.DoctorId);
                if (doctor != null && !string.IsNullOrEmpty(doctor.SpecialtyId)) return doctor.SpecialtyId;
                /* "SPEC-GEN" exists only in the demo store; the service rejects it, so with no specialty send none */
                return CatalogService.Api ? null : "SPEC-GEN";
            }
        }

        public override void Bind()
        {
            var c = ConsultationService.Get(ConsultationId);
            soapSubjective.Text = c.SubjectiveText;
            soapObjective.Text = c.ObjectiveText;
            soapPlan.Text = c.PlanText;
            SubjectivePersonal.Text = c.SubjectivePersonal;
            ObjectivePersonal.Text = c.ObjectivePersonal;
            PlanPersonal.Text = c.PlanPersonal;
            BindChips();

            bool locked = Host.IsLocked;
            foreach (var tb in new[] { soapSubjective, soapObjective, soapPlan, SubjectivePersonal, ObjectivePersonal, PlanPersonal }) tb.ReadOnly = locked;
            ActionsHolder.Visible = VoiceHolder.Visible = PadToggleHolder.Visible = !locked;
            if (locked) DraftPanel.Visible = SavePhrasePanel.Visible = false;

            SketchesRepeater.DataSource = ConsultationService.ListPatientSketches(Visit.PatientId);
            SketchesRepeater.DataBind();
        }

        private void BindChips()
        {
            ChipsS.Text = Chips("S", soapSubjective.ClientID);
            ChipsO.Text = Chips("O", soapObjective.ClientID);
            ChipsP.Text = Chips("P", soapPlan.ClientID);
        }

        /// <summary>Phrase chips for a section (doctor → specialty → hospital-wide; condition phrases when the visit matches).</summary>
        private string Chips(string section, string targetId)
        {
            if (Host.IsLocked) return "";
            var sb = new StringBuilder();
            foreach (var p in PhraseService.ListVisiblePhrases(section, SpecialtyId, Host.DoctorId, ConsultationId).Take(6))
            {
                var label = p.PhraseText.Length > 35 ? p.PhraseText.Substring(0, 32) + "..." : p.PhraseText;
                sb.AppendFormat("<a href=\"#\" class=\"phrase-chip\" data-insert-target=\"{0}\" data-insert-text=\"{1}\" title=\"{2}\">{3}</a>",
                    targetId, HttpUtility.HtmlAttributeEncode(p.PhraseText), HttpUtility.HtmlAttributeEncode(p.TriggerKeyword + ": " + p.PhraseText), HttpUtility.HtmlEncode(label));
            }
            return sb.Length > 0 ? sb.ToString() : "<span class=\"text-xs text-muted\">No phrases for this section yet.</span>";
        }

        protected void SaveSoapButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked()) return;
            var spec = SpecialtyId;
            Func<string, string, string> expand = (text, section) => PhraseService.ExpandTriggers((text ?? "").Trim(), section, spec, Host.DoctorId, ConsultationId);
            ConsultationService.SaveSoap(ConsultationId, expand(soapSubjective.Text, "S"), expand(soapObjective.Text, "O"), expand(soapPlan.Text, "P"),
                SubjectivePersonal.Text.Trim(), ObjectivePersonal.Text.Trim(), PlanPersonal.Text.Trim());
            Audit("UPDATE", "ConsultationSOAP");
            AppPage.ToastSuccess("Observations saved");
            Host.ActivateStep("soap");
            Host.RefreshAll();
        }

        protected void AutoGenerateButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked()) return;
            var states = ConsultationService.GetSymptomStates(ConsultationId);
            Func<string, System.Collections.Generic.List<string>> labels = st => states.Where(kv => kv.Value == st)
                .Select(kv => ConsultationService.SymptomLabel(HospitalId, kv.Key)).ToList();
            var c = ConsultationService.Get(ConsultationId);
            SoapDraft draft = SoapComposer.Compose(ConsultationService.GetVitals(ConsultationId), labels("YES"), labels("NO"),
                ConsultationService.ListDiagnoses(ConsultationId), ConsultationService.ListPrescriptions(ConsultationId),
                ConsultationService.ListOrders(ConsultationId), c.FollowupDate, c.FollowupNotes, HospitalId);
            DraftS.Text = draft.Subjective;
            DraftO.Text = draft.Objective;
            DraftA.Text = draft.Assessment;
            DraftP.Text = draft.Plan;
            DraftPanel.Visible = true;
            SavePhrasePanel.Visible = false;
            Host.ActivateStep("soap");
        }

        protected void AcceptDraftButton_Click(object sender, EventArgs e)
        {
            soapSubjective.Text = DraftS.Text;
            soapObjective.Text = DraftO.Text + (string.IsNullOrWhiteSpace(DraftA.Text) ? "" : "\nAssessment:\n" + DraftA.Text);
            soapPlan.Text = DraftP.Text;
            DraftPanel.Visible = false;
            AppPage.ToastSuccess("SOAP note filled", "Review and save when ready.");
            Host.ActivateStep("soap");
        }

        protected void CancelDraftButton_Click(object sender, EventArgs e)
        {
            DraftPanel.Visible = false;
            Host.ActivateStep("soap");
        }

        protected void ShowSavePhraseButton_Click(object sender, EventArgs e)
        {
            SavePhrasePanel.Visible = true;
            DraftPanel.Visible = false;
            PhraseTrigger.Text = PhraseText.Text = "";
            Host.ActivateStep("soap");
        }

        protected void CancelPhraseButton_Click(object sender, EventArgs e)
        {
            SavePhrasePanel.Visible = false;
            Host.ActivateStep("soap");
        }

        protected void SavePhraseButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("soap");
            Page.Validate("Phrase");
            if (!Page.IsValid) return;
            var trigger = "/" + PhraseTrigger.Text.Trim().TrimStart('/').ToLowerInvariant();
            PhraseService.Save(new Phrase
            {
                Scope = "DOCTOR", DoctorId = Host.DoctorId, Section = PhraseSection.SelectedValue, TriggerKeyword = trigger,
                PhraseText = PhraseText.Text.Trim(), SortOrder = 0,
            });
            AppPage.ToastSuccess("Phrase saved to your library", trigger + " is ready to use");
            SavePhrasePanel.Visible = false;
            BindChips();
        }

        protected void SaveSketchButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("soap");
            if (!EnsureNotLocked()) return;
            var data = Request.Form["sketchData"];
            if (string.IsNullOrEmpty(data) || !data.StartsWith("data:image/"))
            {
                AppPage.ToastWarning("Nothing to save", "Draw on the pad first.");
                return;
            }
            var sketch = ConsultationService.SaveSketch(Visit, Host.DoctorId, Request.Form["sketchTitle"], data);
            Audit("CREATE", "ConsultationSketch", "Sketch saved: " + sketch.Title);
            AppPage.ToastSuccess("Sketch saved");
            SketchesRepeater.DataSource = ConsultationService.ListPatientSketches(Visit.PatientId);
            SketchesRepeater.DataBind();
        }
    }
}
