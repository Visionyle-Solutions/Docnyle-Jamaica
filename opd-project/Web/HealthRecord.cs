using System;
using System.Globalization;
using System.Web.UI.WebControls;

namespace OpdProject.Web
{
    /// <summary>Shared helpers for the patient Health Record tabs (month/year pickers and labels).</summary>
    public static class HealthRecord
    {
        /// <summary>Tabs filled in by the post-registration walkthrough, in order.</summary>
        public static readonly string[] Tabs = { "allergies", "history", "meds", "family" };

        /// <summary>Read-only tab of signed visits; shown to these roles only.</summary>
        public const string VisitsTab = "visits";
        public static bool CanSeeVisits(Models.AppUser user) { return user != null && (user.HasRole("DOCTOR") || user.HasRole("NURSE")); }

        /// <summary>"Mar 2018", "2015" or "—".</summary>
        public static string SinceLabel(int? month, int? year)
        {
            if (!year.HasValue) return "—";
            return month.HasValue && month.Value >= 1 && month.Value <= 12
                ? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Value) + " " + year.Value
                : year.Value.ToString();
        }

        /// <summary>Fills a month list ("—", Jan…Dec) and a year list ("—", this year back to 1930).</summary>
        public static void FillMonthYear(DropDownList month, DropDownList year)
        {
            month.Items.Clear();
            month.Items.Add(new ListItem("—", ""));
            for (int m = 1; m <= 12; m++) month.Items.Add(new ListItem(CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m), m.ToString()));
            year.Items.Clear();
            year.Items.Add(new ListItem("—", ""));
            for (int y = DateTime.UtcNow.Year; y >= 1930; y--) year.Items.Add(new ListItem(y.ToString(), y.ToString()));
        }

        public static int? SelectedInt(DropDownList list)
        {
            int v;
            return int.TryParse(list.SelectedValue, out v) ? v : (int?)null;
        }

        public static void Select(DropDownList list, string value)
        {
            var item = list.Items.FindByValue(value ?? "");
            list.ClearSelection();
            if (item != null) item.Selected = true;
        }
    }

    /// <summary>One signed visit on the Health Record → Visits tab (detail and action links are pre-rendered, encoded HTML).</summary>
    public class VisitRecord
    {
        public string DateLabel { get; set; }
        public string DoctorName { get; set; }
        public string PrimaryDiagnosis { get; set; }
        public string ActionsHtml { get; set; }
        public string DetailHtml { get; set; }
    }
}
