using System;
using System.Web.UI.WebControls;
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
        protected bool CanConsult { get { return HasRole("DOCTOR") && Can("consultation"); } }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                SearchInput.Text = Request.QueryString["q"] ?? "";
                BindResults();
            }
        }

        protected void SearchButton_Click(object sender, EventArgs e) { BindResults(); }

        /// <summary>The query the shown results came from — exports use it, not whatever is typed in the box since.</summary>
        private string LastQuery
        {
            get { return ViewState["q"] as string ?? ""; }
            set { ViewState["q"] = value; }
        }

        private void BindResults()
        {
            LastQuery = SearchInput.Text;
            var results = PatientService.Search(HospitalId, LastQuery);
            ResultsRepeater.DataSource = results;
            ResultsRepeater.DataBind();
            EmptyRow.Visible = results.Count == 0;
            ResultCount.Text = SearchInput.Text.Trim().Length >= 2
                ? results.Count + " Match" + (results.Count == 1 ? "" : "es") + " For \"" + Server.HtmlEncode(SearchInput.Text.Trim()) + "\""
                : results.Count + " Registered Patients";
        }

        /// <summary>Excel / PDF of the results currently listed.</summary>
        protected void Export_Command(object sender, CommandEventArgs e)
        {
            var query = LastQuery.Trim();
            var results = PatientService.Search(HospitalId, query);
            if (results.Count == 0) { ToastWarning("No patients to export"); return; }

            var table = new TableExport.Table
            {
                Title = "Patient Search",
                Subtitle = query.Length >= 2 ? results.Count + " match" + (results.Count == 1 ? "" : "es") + " for \"" + query + "\"" : results.Count + " registered patients",
            };
            table.Headers.AddRange(new[] { "S.No", "Name", PatientIdLabel, "Gender", "DOB", "Phone Number" });
            var n = 0;
            foreach (var p in results)
                table.Rows.Add(new object[] { ++n, p.FullName, p.Mrn, TitleCase(p.Gender), FmtDate(p.Dob), string.IsNullOrEmpty(p.Phone) ? "—" : p.Phone });
            DownloadTable((string)e.CommandArgument, "patient-search-" + Today.ToString("yyyy-MM-dd"), table);
        }
    }
}
