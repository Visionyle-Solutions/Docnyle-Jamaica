using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.reports
{
    /// <summary>Reports dashboard — all aggregation runs in <see cref="ReportService"/>; bars are rendered as server HTML.</summary>
    public partial class reports_dashboard : BasePage
    {
        protected override string RequiredPermission { get { return "reports"; } }
        public override string PageTitle { get { return "Reports Dashboard"; } }

        private static string H(string s) { return HttpUtility.HtmlEncode(s ?? ""); }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (IsPostBack) return;
            ToDate.Text = IsoDate(DateTime.Today);
            FromDate.Text = IsoDate(DateTime.Today.AddDays(-30));
            Render();
        }

        protected void RefreshButton_Click(object sender, EventArgs e)
        {
            if (IsValid) Render();
        }

        private static string Header(string icon, string title)
        {
            return "<div style=\"display:flex; align-items:center; gap:6px; margin-bottom:10px;\"><i class=\"ti " + icon + "\" style=\"color:var(--color-primary);\"></i><strong class=\"text-sm\">" + H(title) + "</strong></div>";
        }

        private static string Bars(List<CountRow> rows, string color, int max)
        {
            if (rows.Count == 0) return "<div class=\"zone-a-empty\">No data for this period.</div>";
            var top = rows.Take(max).ToList();
            var peak = Math.Max(1, top.Max(r => r.Count));
            var sb = new StringBuilder();
            foreach (var r in top)
                sb.Append("<div class=\"report-bar-row\"><div class=\"report-bar-label\" title=\"" + H(r.Label) + "\">" + H(r.Label) + "</div><div class=\"report-bar-track\"><div class=\"report-bar-fill\" style=\"width:"
                    + (r.Count * 100 / peak) + "%; background:" + color + ";\"></div></div><div class=\"report-bar-value\">" + r.Count + "</div></div>");
            return sb.ToString();
        }

        private void Render()
        {
            var from = ParseDate(FromDate.Text) ?? DateTime.Today.AddDays(-30);
            var to = ParseDate(ToDate.Text) ?? DateTime.Today;
            RangeText.Text = H(FmtDate(from) + " → " + FmtDate(to));

            var volume = ReportService.ConsultationVolume(HospitalId, from, to);
            VolumeHtml.Text = Header("ti-chart-bar", "Daily Consultation Volume") + "<div class=\"text-xs text-muted mb-2\">Total: <strong>" + volume.Sum(v => v.Count) + "</strong> consultations</div>" + Bars(volume, "var(--color-primary)", 14);

            DiagnosisHtml.Text = Header("ti-stethoscope", "ICD-10 Diagnosis Frequency") + "<div class=\"text-xs text-muted mb-2\">Top diagnoses in period</div>" + Bars(ReportService.DiagnosisFrequency(HospitalId, from, to), "var(--color-success)", 8);

            RxHtml.Text = Header("ti-pill", "Drug Prescribing Patterns") + "<div class=\"text-xs text-muted mb-2\">Most prescribed drugs</div>" + Bars(ReportService.Prescribing(HospitalId, from, to), "#f59e0b", 8);

            int total, rate;
            var status = ReportService.AppointmentStatus(HospitalId, from, to, out total, out rate);
            var sb = new StringBuilder(Header("ti-calendar-off", "Appointment No-Show Rate"));
            sb.Append("<div style=\"display:flex; gap:16px; margin-bottom:10px;\"><div style=\"text-align:center; flex:1; padding:12px; background:var(--surface-page); border-radius:8px;\"><div style=\"font-size:28px; font-weight:700;\">" + total + "</div><div class=\"text-xs text-muted\">Total appts</div></div>");
            sb.Append("<div style=\"text-align:center; flex:1; padding:12px; background:" + (rate > 15 ? "#fef2f2" : "var(--color-success-bg)") + "; border-radius:8px;\"><div style=\"font-size:28px; font-weight:700; color:" + (rate > 15 ? "var(--color-danger)" : "var(--color-success)") + ";\">" + rate + "%</div><div class=\"text-xs text-muted\">No-show rate</div></div></div>");
            if (status.Count == 0) sb.Append("<div class=\"zone-a-empty\">No appointment data.</div>");
            foreach (var s in status)
                sb.Append("<div style=\"display:flex; justify-content:space-between; padding:4px 0; border-bottom:1px solid var(--border-default);\"><span class=\"badge " + StatusBadge(s.Label) + "\" style=\"font-size:10px;\">" + H(s.Label) + "</span><span class=\"text-sm\" style=\"font-weight:600;\">" + s.Count + "</span></div>");
            NoShowHtml.Text = sb.ToString();

            var revenue = ReportService.Revenue(HospitalId, from, to);
            decimal invoiced = revenue.Sum(r => r.Invoiced), paid = revenue.Sum(r => r.Paid);
            sb = new StringBuilder(Header("ti-report-money", "Revenue Summary"));
            Func<string, string, string, string> tile = (value, label, style) => "<div style=\"text-align:center; flex:1; padding:16px; border-radius:8px; " + style + "\"><div style=\"font-size:24px; font-weight:700;\">" + H(value) + "</div><div class=\"text-xs text-muted\">" + label + "</div></div>";
            sb.Append("<div style=\"display:flex; gap:16px; margin-bottom:12px;\">");
            sb.Append(tile(Money(invoiced), "Total invoiced", "background:var(--surface-page);"));
            sb.Append(tile(Money(paid), "Collected", "background:var(--color-success-bg); color:var(--color-success);"));
            sb.Append(tile(Money(invoiced - paid), "Outstanding", invoiced - paid > 0 ? "background:#fff7ed; color:#f59e0b;" : "background:var(--surface-page);"));
            sb.Append("</div>");
            if (revenue.Count == 0) sb.Append("<div class=\"zone-a-empty\">No revenue data for this period.</div>");
            else
            {
                sb.Append("<table class=\"data-table\"><thead><tr><th>Doctor</th><th style=\"text-align:right;\">Invoiced</th><th style=\"text-align:right;\">Collected</th><th style=\"text-align:right;\">Invoices</th></tr></thead><tbody>");
                foreach (var r in revenue)
                    sb.Append("<tr><td>" + H(r.DoctorName) + "</td><td style=\"text-align:right;\">" + H(Money(r.Invoiced)) + "</td><td style=\"text-align:right;\">" + H(Money(r.Paid)) + "</td><td style=\"text-align:right;\">" + r.Count + "</td></tr>");
                sb.Append("</tbody></table>");
            }
            RevenueHtml.Text = sb.ToString();
        }
    }
}
