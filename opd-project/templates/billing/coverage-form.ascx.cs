using System;
using System.Globalization;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;

namespace OpdProject.templates.billing
{
    /// <summary>
    /// Add-coverage fields shared by billing checkout and the patient Coverage &amp; Insurance page. Offers private
    /// insurers and the govt scheme (NHF in Jamaica) of the hospital's country only. Hosts call
    /// <see cref="Reset"/> to open a blank form, validate group "Coverage", then <see cref="BuildRecord"/>.
    /// </summary>
    public partial class coverage_form : UserControl
    {
        /// <summary>Raised when the coverage type changes (auto-postback), so a modal host can stay open.</summary>
        public event EventHandler TypeChanged;

        /// <summary>Hospital country (JM, IN …); kept in view state so postbacks offer the same payers and scheme.</summary>
        private string Country
        {
            get { return ViewState["Country"] as string; }
            set { ViewState["Country"] = value; }
        }

        private GovtSchemeProfile Scheme { get { return PatientService.GetGovtSchemeProfile(Country); } }

        /// <summary>Fills the pick lists for the hospital's country and clears the fields; policy holder defaults to the patient.</summary>
        public void Reset(Hospital hospital, Patient patient)
        {
            Country = hospital.Country;
            CovType.Items.Clear();
            CovType.Items.Add(new ListItem("Private Insurance", "PRIVATE_INSURANCE"));
            CovType.Items.Add(new ListItem(Scheme.Label, "GOVT_SCHEME"));
            CovPayerPrivate.DataSource = PatientService.ListPayers(Country, "PRIVATE");
            CovPayerPrivate.DataTextField = "Name"; CovPayerPrivate.DataValueField = "PayerId"; CovPayerPrivate.DataBind();
            CovPayerGovt.DataSource = PatientService.ListPayers(Country, "GOVT_SCHEME");
            CovPayerGovt.DataTextField = "Name"; CovPayerGovt.DataValueField = "PayerId"; CovPayerGovt.DataBind();
            CardLabel.Text = HttpUtility.HtmlEncode(Scheme.CardLabel);
            CovGovtValidator.ErrorMessage = "Enter the " + Scheme.CardLabel;
            CovPlan.Text = CovMember.Text = CovPolicy.Text = CovGroup.Text = CovCard.Text = CovEffective.Text = CovExpiry.Text = "";
            CovHolder.Text = patient.FullName;
            CovRelation.SelectedIndex = CovMode.SelectedIndex = CovStatus.SelectedIndex = 0;
            ApplyType();
        }

        /// <summary>Preselects private insurance or the govt scheme (e.g. from a "+ Add NHF" shortcut).</summary>
        public void SelectType(string coverageType)
        {
            if (CovType.Items.FindByValue(coverageType) != null) CovType.SelectedValue = coverageType;
            ApplyType();
        }

        /// <summary>The coverage the fields describe (not yet saved).</summary>
        public PatientCoverage BuildRecord(string hospitalId, Patient patient)
        {
            var govt = CovType.SelectedValue == "GOVT_SCHEME";
            return new PatientCoverage
            {
                HospitalId = hospitalId, PatientId = patient.PatientId, CoverageType = CovType.SelectedValue,
                PayerId = govt ? CovPayerGovt.SelectedValue : CovPayerPrivate.SelectedValue,
                PlanName = govt ? Scheme.Label : CovPlan.Text.Trim(), MemberId = govt ? "" : CovMember.Text.Trim(), PolicyNumber = govt ? "" : CovPolicy.Text.Trim(),
                GroupNumber = govt ? "" : CovGroup.Text.Trim(), PolicyHolderName = govt ? patient.FullName : CovHolder.Text.Trim(),
                Relationship = govt ? "Self" : CovRelation.SelectedValue, ClaimMode = govt ? "" : CovMode.SelectedValue,
                GovtCardNumber = govt ? CovCard.Text.Trim() : "", EffectiveDate = ParseDate(CovEffective.Text), ExpiryDate = ParseDate(CovExpiry.Text), Status = CovStatus.SelectedValue,
            };
        }

        private void ApplyType()
        {
            CovPrivate.Visible = CovType.SelectedValue != "GOVT_SCHEME";
            CovGovt.Visible = CovType.SelectedValue == "GOVT_SCHEME";
        }

        protected void CovType_Changed(object sender, EventArgs e)
        {
            ApplyType();
            if (TypeChanged != null) TypeChanged(this, EventArgs.Empty);
        }

        protected void CovPrivateValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = CovType.SelectedValue != "PRIVATE_INSURANCE" || CovMember.Text.Trim().Length > 0 || CovPolicy.Text.Trim().Length > 0;
        }

        protected void CovGovtValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = CovType.SelectedValue != "GOVT_SCHEME" || CovCard.Text.Trim().Length > 0;
        }

        private static DateTime? ParseDate(string value)
        {
            DateTime d;
            return DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }
    }
}
