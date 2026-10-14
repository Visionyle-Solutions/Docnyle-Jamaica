using System;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>Server-side patient search by name, MRN, phone or national health id (fewer than 2 characters lists everyone).</summary>
    public partial class patient_search : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Patient Search"; } }

        /// <summary>Only doctors open a consultation (it is recorded under the signed-in doctor).</summary>
        protected bool CanConsult { get { return Role == "DOCTOR" && Can("consultation"); } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                SearchInput.Text = Request.QueryString["q"] ?? "";
                BindResults();
            }
        }

        protected void SearchButton_Click(object sender, EventArgs e) { BindResults(); }

        private void BindResults()
        {
            var results = PatientService.Search(HospitalId, SearchInput.Text);
            ResultsRepeater.DataSource = results;
            ResultsRepeater.DataBind();
            EmptyRow.Visible = results.Count == 0;
            ResultCount.Text = SearchInput.Text.Trim().Length >= 2
                ? results.Count + " match" + (results.Count == 1 ? "" : "es") + " for \"" + Server.HtmlEncode(SearchInput.Text.Trim()) + "\""
                : results.Count + " registered patients";
        }
    }
}
