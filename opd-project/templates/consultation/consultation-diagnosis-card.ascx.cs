using System;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>ICD-10 diagnosis: server-side search by code/term/synonym, confirm with clinical notes, remove.</summary>
    public partial class consultation_diagnosis_card : ConsultationSection
    {
        protected override string SectionKey { get { return "diagnosis"; } }

        protected override void ApplyFormConfig()
        {
            NotesHolder.Visible = FieldOn("notes");
            LateralityInput.Visible = FieldOn("laterality");
            AcuteChronicInput.Visible = FieldOn("acuteChronic");
            ApplyOptions("laterality", LateralityInput);
            ApplyOptions("acuteChronic", AcuteChronicInput);
        }

        public override void Bind()
        {
            var list = ConsultationService.ListDiagnoses(ConsultationId);
            DiagnosesRepeater.DataSource = list;
            DiagnosesRepeater.DataBind();
            NoDiagnoses.Visible = list.Count == 0;
            SearchHolder.Visible = !Host.IsLocked;
        }

        protected void SearchButton_Click(object sender, EventArgs e)
        {
            var term = Icd10SearchInput.Text.Trim();
            var results = ConsultationService.SearchIcd10(term);
            ResultsRepeater.DataSource = results;
            ResultsRepeater.DataBind();
            SearchHint.Text = term.Length < 2 ? "<div class=\"text-xs text-muted\">Type at least 2 characters to search.</div>"
                : results.Count == 0 ? "<div class=\"text-xs text-muted\">No matching codes.</div>" : "";
            Host.ActivateStep("diagnosis");
        }

        protected void ResultsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "Pick" || !EnsureNotLocked()) return;
            var entry = ConsultationService.GetIcd10((string)e.CommandArgument);
            if (entry == null) return;
            PendingCode.Value = entry.Icd10Code;
            PendingDescription.Value = entry.Description;
            PendingTitle.Text = Server.HtmlEncode(entry.Icd10Code + " — " + entry.Description);
            ClinicalNotesInput.Text = "";
            LateralityInput.SelectedIndex = AcuteChronicInput.SelectedIndex = 0;
            PendingPanel.Visible = true;
            ResultsRepeater.DataSource = null;
            ResultsRepeater.DataBind();
            SearchHint.Text = "";
            Host.ActivateStep("diagnosis");
        }

        protected void ConfirmDiagnosisButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked() || string.IsNullOrEmpty(PendingCode.Value)) return;
            ConsultationService.AddDiagnosis(ConsultationId, PendingCode.Value, PendingDescription.Value,
                FieldOn("notes") ? ClinicalNotesInput.Text.Trim() : "", FieldOn("laterality") ? LateralityInput.SelectedValue : "",
                FieldOn("acuteChronic") ? AcuteChronicInput.SelectedValue : "");
            Audit("CREATE", "ConsultationDiagnosis", PendingCode.Value + " " + PendingDescription.Value);
            AppPage.ToastSuccess("Diagnosis added", PendingCode.Value + " — " + PendingDescription.Value);
            PendingPanel.Visible = false;
            PendingCode.Value = "";
            Icd10SearchInput.Text = "";
            Host.ActivateStep("diagnosis");
            Host.RefreshAll();
        }

        protected void CancelDiagnosisButton_Click(object sender, EventArgs e)
        {
            PendingPanel.Visible = false;
            PendingCode.Value = "";
            Host.ActivateStep("diagnosis");
        }

        protected void DiagnosesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "Remove" || !EnsureNotLocked()) return;
            ConsultationService.RemoveDiagnosis((string)e.CommandArgument);
            Audit("DELETE", "ConsultationDiagnosis", (string)e.CommandArgument);
            AppPage.ToastSuccess("Diagnosis removed");
            Host.ActivateStep("diagnosis");
            Host.RefreshAll();
        }
    }
}
