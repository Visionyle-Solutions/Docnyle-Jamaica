using System;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>Formulary: search, add/edit (validated, unique brand), activate/deactivate drugs.</summary>
    public partial class formulary_management : BasePage
    {
        protected override string RequiredPermission { get { return "admin_formulary"; } }
        public override string PageTitle { get { return "Drug Formulary"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) Bind();
        }

        private void Bind()
        {
            var drugs = CatalogService.SearchFormulary(HospitalId, SearchInput.Text, ShowInactive.Checked);
            DrugsRepeater.DataSource = drugs;
            DrugsRepeater.DataBind();
            EmptyRow.Visible = drugs.Count == 0;
        }

        protected void SearchButton_Click(object sender, EventArgs e) { Bind(); }

        protected void AddDrugButton_Click(object sender, EventArgs e)
        {
            EditingDrugId.Value = "";
            DrugModalTitle.Text = "Add Drug to Formulary";
            FdBrand.Text = FdGeneric.Text = FdComposition.Text = FdClass.Text = FdGenericClass.Text = FdDosage.Text = FdFrequency.Text = FdInstructions.Text = "";
            FdDuration.Text = "5";
            FdForm.SelectedIndex = 0;
            FdControlled.Checked = false;
            DrugModal.Visible = true;
        }

        protected void DrugsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var d = CatalogService.GetDrug((string)e.CommandArgument);
            if (d == null) return;
            if (e.CommandName == "Toggle")
            {
                CatalogService.ToggleDrug(d.DrugId);
                Audit("UPDATE", "Drug", d.DrugId, d.BrandName + (d.IsActive ? " activated" : " deactivated"));
                ToastSuccess("Drug status updated", d.BrandName + " is now " + (d.IsActive ? "active" : "inactive"));
                Bind();
                return;
            }
            EditingDrugId.Value = d.DrugId;
            DrugModalTitle.Text = "Edit: " + Server.HtmlEncode(d.BrandName);
            FdBrand.Text = d.BrandName; FdGeneric.Text = d.GenericName; FdComposition.Text = d.Composition; FdClass.Text = d.TherapeuticClass;
            FdGenericClass.Text = d.GenericClass; FdDosage.Text = d.DefaultDosage; FdFrequency.Text = d.DefaultFrequency;
            FdDuration.Text = d.DefaultDurationDays.HasValue ? d.DefaultDurationDays.Value.ToString() : "";
            FdInstructions.Text = d.DefaultInstructions;
            if (FdForm.Items.FindByValue(d.DrugForm) == null) FdForm.Items.Add(d.DrugForm);
            FdForm.SelectedValue = d.DrugForm;
            FdControlled.Checked = d.IsControlledSubstance;
            DrugModal.Visible = true;
        }

        protected void BrandUnique_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = !CatalogService.BrandExists(args.Value.Trim(), string.IsNullOrEmpty(EditingDrugId.Value) ? null : EditingDrugId.Value);
        }

        protected void SaveDrugButton_Click(object sender, EventArgs e)
        {
            Validate("Drug");
            if (!IsValid) { DrugModal.Visible = true; return; }
            var saved = CatalogService.SaveDrug(new Drug
            {
                DrugId = string.IsNullOrEmpty(EditingDrugId.Value) ? null : EditingDrugId.Value,
                BrandName = FdBrand.Text.Trim(), GenericName = FdGeneric.Text.Trim(), Composition = FdComposition.Text.Trim(),
                TherapeuticClass = FdClass.Text.Trim(), GenericClass = FdGenericClass.Text.Trim(), DrugForm = FdForm.SelectedValue,
                DefaultDosage = FdDosage.Text.Trim(), DefaultFrequency = FdFrequency.Text.Trim(), DefaultDurationDays = ParseInt(FdDuration.Text, 5),
                DefaultInstructions = FdInstructions.Text.Trim(), IsControlledSubstance = FdControlled.Checked,
            });
            Audit(string.IsNullOrEmpty(EditingDrugId.Value) ? "CREATE" : "UPDATE", "Drug", saved.DrugId, saved.BrandName);
            ToastSuccess(saved.BrandName + (string.IsNullOrEmpty(EditingDrugId.Value) ? " added to formulary" : " updated"));
            DrugModal.Visible = false;
            Bind();
        }

        protected void CloseModal_Click(object sender, EventArgs e) { DrugModal.Visible = false; }
    }
}
