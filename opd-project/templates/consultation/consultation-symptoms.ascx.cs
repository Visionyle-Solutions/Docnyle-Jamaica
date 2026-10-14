using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>Presenting complaints. Any complaint marked yes/no is promoted to the quick-pick row.</summary>
    public partial class consultation_symptoms : ConsultationSection
    {
        protected override string SectionKey { get { return "complaints"; } }

        protected bool Locked { get { return Host.IsLocked; } }

        private bool Expanded
        {
            get { return ViewState["Expanded"] is bool && (bool)ViewState["Expanded"]; }
            set { ViewState["Expanded"] = value; }
        }

        public override void Bind()
        {
            var states = ConsultationService.GetSymptomStates(ConsultationId);
            var all = ConsultationService.ListSymptomCatalog(HospitalId).Select(s => new SymptomChip
            {
                Code = s.SymptomCode, Label = s.DisplayLabel, State = states.ContainsKey(s.SymptomCode) ? states[s.SymptomCode] : "UNSET",
            }).ToList();
            var quickCodes = new HashSet<string>(ConsultationService.ListSymptomCatalog(HospitalId).Where(s => s.IsQuickPick).Select(s => s.SymptomCode));
            QuickPicks.DataSource = all.Where(c => quickCodes.Contains(c.Code) || c.State != "UNSET").ToList();
            QuickPicks.DataBind();
            OtherSymptoms.DataSource = all.Where(c => !quickCodes.Contains(c.Code) && c.State == "UNSET").ToList();
            OtherSymptoms.DataBind();
            ExpandedPanel.Visible = Expanded;
            MoreToggle.Text = Expanded ? "Less" : "...More";
            CustomHolder.Visible = !Locked;
        }

        protected override void ApplyFormConfig()
        {
            /* "...more" opens the other-symptoms list and the custom-complaint entry — shown while either is configured */
            bool more = FieldOn("more"), custom = !Locked && FieldOn("custom");
            OtherHolder.Visible = more;
            CustomHolder.Visible = custom;
            MoreToggle.Visible = more || custom;
            if (!MoreToggle.Visible) ExpandedPanel.Visible = false;
        }

        protected void Chip_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "Cycle" || !EnsureNotLocked()) return;
            ConsultationService.CycleSymptom(ConsultationId, (string)e.CommandArgument);
            Host.ActivateStep("complaints");
            Host.RefreshAll();
        }

        protected void MoreToggle_Click(object sender, EventArgs e)
        {
            Expanded = !Expanded;
            Host.ActivateStep("complaints");
            Bind();
        }

        protected void AddCustomButton_Click(object sender, EventArgs e)
        {
            if (!EnsureNotLocked()) return;
            var label = CustomComplaintInput.Text.Trim();
            if (label.Length < 2)
            {
                AppPage.ToastWarning("Enter the complaint");
                return;
            }
            ConsultationService.AddCustomSymptom(HospitalId, ConsultationId, label);
            CustomComplaintInput.Text = "";
            AppPage.ToastSuccess("\"" + label + "\" added and marked present");
            Host.ActivateStep("complaints");
            Host.RefreshAll();
        }
    }
}
