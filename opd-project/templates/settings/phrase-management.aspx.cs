using System;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>
    /// Phrase library management. Doctors manage their own phrases; administrators can also manage specialty and
    /// hospital-wide phrases and clinical conditions.
    /// </summary>
    public partial class phrase_management : BasePage
    {
        protected override string RequiredPermission { get { return "admin_phrases"; } }
        public override string PageTitle { get { return "Phrase Library"; } }

        protected bool IsAdmin { get { return HasRole("ADMIN"); } }

        protected void Page_Load(object sender, EventArgs e)
        {
            AddConditionPanel.Visible = IsAdmin;
            if (IsPostBack) return;
            foreach (var d in UserService.ListActiveDoctors(HospitalId)) PhraseDoctor.Items.Add(new ListItem(d.FullName, d.UserId));
            foreach (var s in HospitalService.ListSpecialties(HospitalId, true)) PhraseSpecialty.Items.Add(new ListItem(s.SpecialtyName, s.SpecialtyId));
            BindConditions();
            Bind();
        }

        private void BindConditions()
        {
            var conditions = PhraseService.ListConditions(HospitalId);
            ConditionsRepeater.DataSource = conditions;
            ConditionsRepeater.DataBind();
            var current = ConditionFilter.SelectedValue;
            ConditionFilter.Items.Clear();
            ConditionFilter.Items.Add(new ListItem("All conditions", "ALL"));
            ConditionFilter.Items.Add(new ListItem("No condition", "NONE"));
            PhraseCondition.Items.Clear();
            PhraseCondition.Items.Add(new ListItem("Any condition", ""));
            foreach (var c in conditions)
            {
                ConditionFilter.Items.Add(new ListItem(c.Name, c.ConditionId));
                PhraseCondition.Items.Add(new ListItem(c.Name, c.ConditionId));
            }
            if (ConditionFilter.Items.FindByValue(current) != null) ConditionFilter.SelectedValue = current;
        }

        private void Bind()
        {
            var rows = PhraseService.ListAll(HospitalId).AsEnumerable();
            switch (ScopeFilter.SelectedValue)
            {
                case "MINE": rows = rows.Where(p => p.Scope == "DOCTOR" && p.DoctorId == UserId); break;
                case "ALL": break;
                default: rows = rows.Where(p => p.Scope == ScopeFilter.SelectedValue); break;
            }
            if (SectionFilter.SelectedValue != "ALL") rows = rows.Where(p => p.Section == SectionFilter.SelectedValue);
            if (ConditionFilter.SelectedValue == "NONE") rows = rows.Where(p => p.ConditionId == null);
            else if (ConditionFilter.SelectedValue != "ALL" && ConditionFilter.SelectedValue != "") rows = rows.Where(p => p.ConditionId == ConditionFilter.SelectedValue);
            var list = rows.OrderBy(p => p.TriggerKeyword).ToList();
            PhrasesRepeater.DataSource = list;
            PhrasesRepeater.DataBind();
            EmptyRow.Visible = list.Count == 0;
        }

        protected void Filter_Changed(object sender, EventArgs e) { Bind(); }

        protected string SectionLabel(string s)
        {
            return s == "S" ? "Subjective" : s == "O" ? "Objective" : s == "P" ? "Plan" : "Any";
        }

        protected string ScopeBadge(Phrase p)
        {
            if (p.Scope == "GLOBAL") return "<span class=\"badge badge--success\">Hospital-wide</span>";
            if (p.Scope == "SPECIALTY") return "<span class=\"badge badge--warning\">" + HttpUtility.HtmlEncode(HospitalService.SpecialtyName(p.SpecialtyId)) + "</span>";
            return "<span class=\"badge badge--info\">" + HttpUtility.HtmlEncode(UserService.UserName(p.DoctorId)) + "</span>";
        }

        protected string ConditionName(string id)
        {
            if (id == null) return "Any";
            var c = PhraseService.ListConditions(HospitalId).FirstOrDefault(x => x.ConditionId == id);
            return c != null ? c.Name : "(removed condition)";
        }

        /// <summary>Doctors edit only their own phrases; administrators edit everything.</summary>
        protected bool CanEdit(Phrase p) { return IsAdmin || (p.Scope == "DOCTOR" && p.DoctorId == UserId); }

        private void ApplyScopeVisibility()
        {
            DoctorHolder.Visible = PhraseScope.SelectedValue == "DOCTOR";
            SpecialtyHolder.Visible = PhraseScope.SelectedValue == "SPECIALTY";
            PhraseScope.Enabled = PhraseDoctor.Enabled = IsAdmin;
        }

        protected void AddPhraseButton_Click(object sender, EventArgs e)
        {
            EditingPhraseId.Value = "";
            PhraseModalTitle.Text = "New Phrase";
            PhraseTrigger.Text = "/";
            PhraseText.Text = "";
            PhraseSection.SelectedValue = "S";
            PhraseCondition.SelectedValue = "";
            PhraseScope.SelectedValue = "DOCTOR";
            if (PhraseDoctor.Items.FindByValue(UserId) != null) PhraseDoctor.SelectedValue = UserId;
            ApplyScopeVisibility();
            PhraseModal.Visible = true;
        }

        protected void PhraseScope_Changed(object sender, EventArgs e)
        {
            ApplyScopeVisibility();
            PhraseModal.Visible = true;
        }

        protected void PhrasesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var p = PhraseService.Get((string)e.CommandArgument);
            if (p == null || !CanEdit(p)) return;
            if (e.CommandName == "DeletePhrase")
            {
                PhraseService.Delete(p.PhraseId);
                Audit("DELETE", "Phrase", p.PhraseId, p.TriggerKeyword);
                ToastSuccess("Phrase deleted");
                Bind();
                return;
            }
            EditingPhraseId.Value = p.PhraseId;
            PhraseModalTitle.Text = "Edit Phrase";
            PhraseTrigger.Text = p.TriggerKeyword;
            PhraseText.Text = p.PhraseText;
            PhraseSection.SelectedValue = p.Section;
            PhraseCondition.SelectedValue = PhraseCondition.Items.FindByValue(p.ConditionId ?? "") != null ? (p.ConditionId ?? "") : "";
            PhraseScope.SelectedValue = p.Scope;
            if (p.DoctorId != null && PhraseDoctor.Items.FindByValue(p.DoctorId) != null) PhraseDoctor.SelectedValue = p.DoctorId;
            if (p.SpecialtyId != null && PhraseSpecialty.Items.FindByValue(p.SpecialtyId) != null) PhraseSpecialty.SelectedValue = p.SpecialtyId;
            ApplyScopeVisibility();
            PhraseModal.Visible = true;
        }

        protected void ScopeValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = IsAdmin || (PhraseScope.SelectedValue == "DOCTOR" && PhraseDoctor.SelectedValue == UserId);
        }

        protected void SavePhraseButton_Click(object sender, EventArgs e)
        {
            if (!IsAdmin) { PhraseScope.SelectedValue = "DOCTOR"; if (PhraseDoctor.Items.FindByValue(UserId) != null) PhraseDoctor.SelectedValue = UserId; }
            Validate("Phrase");
            if (!IsValid) { ApplyScopeVisibility(); PhraseModal.Visible = true; return; }
            var trigger = "/" + PhraseTrigger.Text.Trim().TrimStart('/').ToLowerInvariant();
            var scope = PhraseScope.SelectedValue;
            var saved = PhraseService.Save(new Phrase
            {
                PhraseId = string.IsNullOrEmpty(EditingPhraseId.Value) ? null : EditingPhraseId.Value,
                TriggerKeyword = trigger, PhraseText = PhraseText.Text.Trim(), Section = PhraseSection.SelectedValue,
                ConditionId = PhraseCondition.SelectedValue == "" ? null : PhraseCondition.SelectedValue, Scope = scope,
                DoctorId = scope == "DOCTOR" ? PhraseDoctor.SelectedValue : null, SpecialtyId = scope == "SPECIALTY" ? PhraseSpecialty.SelectedValue : null,
            });
            Audit(string.IsNullOrEmpty(EditingPhraseId.Value) ? "CREATE" : "UPDATE", "Phrase", saved.PhraseId, trigger);
            ToastSuccess(string.IsNullOrEmpty(EditingPhraseId.Value) ? "Phrase added" : "Phrase updated", trigger);
            PhraseModal.Visible = false;
            Bind();
        }

        protected void CloseModal_Click(object sender, EventArgs e) { PhraseModal.Visible = false; }

        protected void AddConditionButton_Click(object sender, EventArgs e)
        {
            if (!IsAdmin) return;
            Validate("Condition");
            if (!IsValid) return;
            var prefixes = string.Join(",", NewConditionPrefixes.Text.Split(',').Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0));
            if (!PhraseService.AddCondition(HospitalId, NewConditionName.Text.Trim(), prefixes)) { ToastWarning("That condition already exists"); return; }
            Audit("CREATE", "ClinicalCondition", NewConditionName.Text.Trim(), prefixes);
            ToastSuccess("Condition added");
            NewConditionName.Text = NewConditionPrefixes.Text = "";
            BindConditions();
            Bind();
        }

        protected void ConditionsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (!IsAdmin) return;
            PhraseService.RemoveCondition((string)e.CommandArgument);
            Audit("DELETE", "ClinicalCondition", (string)e.CommandArgument);
            ToastSuccess("Condition removed");
            BindConditions();
            Bind();
        }
    }
}
