using System;
using System.Text;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>Streams a table as a CSV attachment from the server.</summary>
    public partial class data_export : BasePage
    {
        protected override string RequiredPermission { get { return "data_export"; } }
        public override string PageTitle { get { return "Data Export"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            TablesRepeater.DataSource = ExportService.ListTables(HospitalId);
            TablesRepeater.DataBind();
        }

        protected void TablesRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var key = (string)e.CommandArgument;
            var csv = ExportService.ToCsv(key, HospitalId);
            if (csv == null) { ToastWarning("No data in " + ExportService.TableLabel(key)); return; }
            Audit("EXPORT", "DataExport", key, "Exported " + ExportService.TableLabel(key) + " to CSV");
            Response.Clear();
            Response.ContentType = "text/csv";
            Response.ContentEncoding = Encoding.UTF8;
            Response.AddHeader("Content-Disposition", "attachment; filename=" + key + "-export-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv");
            Response.BinaryWrite(Encoding.UTF8.GetPreamble());
            Response.Write(csv);
            Response.End();
        }
    }
}
