using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.reports
{
    /// <summary>Home dashboard — counts come from <see cref="ReportService.Dashboard"/>; tiles and the pie chart are rendered as server HTML/SVG.</summary>
    public partial class home_dashboard : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Dashboard"; } }

        private static string H(string s) { return HttpUtility.HtmlEncode(s ?? ""); }

        /// <summary>Slice colours by appointment status.</summary>
        private static readonly Dictionary<string, string> StatusColors = new Dictionary<string, string>
        {
            { "BOOKED", "#1a6b9a" }, { "CHECKED_IN", "#5fa8d3" }, { "IN_CONSULT", "#14b8a6" },
            { "COMPLETED", "#15803d" }, { "NO_SHOW", "#dc2626" },
        };

        protected void Page_Load(object sender, EventArgs e)
        {
            var doctorId = SeesOwnPatientsOnly ? UserId : null;
            var stats = ReportService.Dashboard(HospitalId, doctorId, Today);

            WelcomeText.Text = "Welcome, " + H(CurrentUser.FullName);
            ConsultationsHtml.Text = Group("Consultations", "consultations", stats.Consultations);
            AppointmentsHtml.Text = Group("Appointments", "appointments", stats.Appointments);
            CancelledHtml.Text = Group("Cancelled Consultations", "cancelled", stats.Cancelled);
            StatusChartHtml.Text = "<h2 class=\"dash-title dash-title--center\">Consultation Status · Today</h2>" + Pie(stats.TodayStatus);
        }

        private static string Group(string title, string kind, PeriodCounts c)
        {
            return "<h2 class=\"dash-title\">" + H(title) + "</h2><div class=\"dash-tiles\">"
                + Tile("Today", c.Today, kind, "today") + Tile("This Week", c.Week, kind, "week") + Tile("This Month", c.Month, kind, "month") + "</div>";
        }

        private static string Tile(string label, int value, string kind, string period)
        {
            return "<a class=\"dash-tile dash-tile--" + period + "\" href=\"" + H(Urls.DashboardList(kind, period, null)) + "\"><div class=\"dash-tile-label\">" + label
                + "</div><div class=\"dash-tile-value\">" + value + "</div></a>";
        }

        private static string Num(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        /// <summary>SVG pie of today's visits by status, with a legend (count and share).</summary>
        private static string Pie(List<CountRow> rows)
        {
            var total = rows.Sum(x => x.Count);
            if (total == 0) return "<div class=\"zone-a-empty\">No visits today.</div>";

            const double cx = 100, cy = 100, r = 96;
            var svg = new StringBuilder("<svg class=\"dash-pie\" viewBox=\"0 0 200 200\" role=\"img\" aria-label=\"Today's visits by status\">");
            var legend = new StringBuilder("<ul class=\"dash-legend\">");
            double angle = -Math.PI / 2;
            foreach (var row in rows)
            {
                string color;
                if (!StatusColors.TryGetValue(row.Label, out color)) color = "#94a3b8";
                var share = (double)row.Count / total;
                var label = StatusLabel(row.Label);
                var pct = (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                var tip = "<title>" + H(label + ": " + row.Count + " (" + pct + ")") + "</title>";
                var href = H(Urls.DashboardList("appointments", "today", row.Label));
                svg.Append("<a href=\"" + href + "\">");
                if (rows.Count == 1)
                    svg.Append("<circle cx=\"100\" cy=\"100\" r=\"" + Num(r) + "\" fill=\"" + color + "\">" + tip + "</circle>");
                else
                {
                    var end = angle + share * 2 * Math.PI;
                    svg.Append("<path d=\"M100 100 L" + Num(cx + r * Math.Cos(angle)) + " " + Num(cy + r * Math.Sin(angle))
                        + " A" + Num(r) + " " + Num(r) + " 0 " + (share > 0.5 ? 1 : 0) + " 1 " + Num(cx + r * Math.Cos(end)) + " " + Num(cy + r * Math.Sin(end))
                        + " Z\" fill=\"" + color + "\" stroke=\"#fff\" stroke-width=\"1.5\">" + tip + "</path>");
                    angle = end;
                }
                svg.Append("</a>");
                legend.Append("<li><a href=\"" + href + "\"><span class=\"dash-swatch\" style=\"background:" + color + ";\"></span>" + H(label)
                    + " <strong>" + row.Count + "</strong> <span class=\"text-muted\">(" + pct + ")</span></a></li>");
            }
            return "<div class=\"dash-chart\">" + svg.Append("</svg>") + legend.Append("</ul>") + "</div>";
        }
    }
}
