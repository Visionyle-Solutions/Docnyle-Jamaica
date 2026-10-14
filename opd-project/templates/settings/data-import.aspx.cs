using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>
    /// CSV import: the uploaded file is parsed and validated on the server; valid rows are kept in session
    /// until the user confirms the import (each imported patient gets an MRN as usual).
    /// </summary>
    public partial class data_import : BasePage
    {
        private const string PendingKey = "Consult360.PendingImport";

        protected override string RequiredPermission { get { return "data_import"; } }
        public override string PageTitle { get { return "CSV Data Import"; } }

        private static string H(string s) { return HttpUtility.HtmlEncode(s ?? ""); }

        protected void FileValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            if (!CsvFile.HasFile) { args.IsValid = false; FileValidator.Text = "Choose a CSV file"; return; }
            var ext = Path.GetExtension(CsvFile.FileName).ToLowerInvariant();
            if (ext != ".csv" && ext != ".txt") { args.IsValid = false; FileValidator.Text = "Only .csv or .txt files"; return; }
            if (CsvFile.PostedFile.ContentLength > 1024 * 1024) { args.IsValid = false; FileValidator.Text = "File must be 1 MB or smaller"; return; }
            args.IsValid = true;
        }

        protected void ValidateButton_Click(object sender, EventArgs e)
        {
            if (!IsValid) return;
            List<string> headers, errors;
            var rows = ExportService.ParseCsv(CsvFile.PostedFile.InputStream, out headers, out errors);
            Session[PendingKey] = rows;

            NoFile.Visible = false;
            PreviewHolder.Visible = true;
            FileSummary.Text = "File: <strong>" + H(CsvFile.FileName) + "</strong> · " + (rows.Count + errors.Count) + " data rows · " + headers.Count + " columns";
            ValidCount.Text = rows.Count.ToString();
            ErrorCount.Text = errors.Count.ToString();
            ErrorList.Text = errors.Count == 0 ? "" : "<div style=\"max-height:110px; overflow-y:auto; font-size:11px; color:var(--color-danger); margin-bottom:8px;\">"
                + string.Join("<br/>", errors.Take(20).Select(H)) + (errors.Count > 20 ? "<br/>… " + (errors.Count - 20) + " more" : "") + "</div>";
            var sb = new StringBuilder("<table class=\"data-table\" style=\"font-size:10px;\"><thead><tr>" + string.Join("", headers.Select(h => "<th>" + H(h) + "</th>")) + "</tr></thead><tbody>");
            foreach (var r in rows.Take(5)) sb.Append("<tr>" + string.Join("", headers.Select(h => "<td>" + H(r.ContainsKey(h) ? r[h] : "") + "</td>")) + "</tr>");
            if (rows.Count > 5) sb.Append("<tr><td colspan=\"" + headers.Count + "\" class=\"text-muted\">… " + (rows.Count - 5) + " more valid rows</td></tr>");
            PreviewTable.Text = sb.Append("</tbody></table>").ToString();
            ImportButton.Visible = rows.Count > 0;
        }

        protected void ImportButton_Click(object sender, EventArgs e)
        {
            var rows = Session[PendingKey] as List<Dictionary<string, string>>;
            if (rows == null || rows.Count == 0) { ToastWarning("Upload and validate a file first"); return; }
            var imported = ExportService.ImportPatients(HospitalId, rows);
            Session.Remove(PendingKey);
            Audit("IMPORT", "CsvImport", TargetTable.SelectedValue, "Imported " + imported + " rows into " + TargetTable.SelectedValue);
            ToastSuccess(imported + " records imported into Patients");
            NoFile.Visible = true;
            PreviewHolder.Visible = false;
        }
    }
}
