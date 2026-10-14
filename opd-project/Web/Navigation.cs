using System.Collections.Generic;
using System.Linq;

namespace OpdProject.Web
{
    public class NavItem
    {
        public string Icon { get; set; }
        public string Label { get; set; }
        public string Path { get; set; }
        public string Permission { get; set; }
        public string Url { get { return Urls.To(Path); } }
    }

    public class NavSection
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public List<NavItem> Items { get; set; }
        public bool IsOpen { get; set; }
        /// <summary>A section without a label has no collapsible header — its items always show.</summary>
        public bool HasHeader { get { return !string.IsNullOrEmpty(Label); } }
    }

    /// <summary>Sidebar sections (same groups, labels, icons and permissions as the original layout.js).</summary>
    public static class Navigation
    {
        private static NavItem I(string icon, string label, string path, string perm)
        {
            return new NavItem { Icon = icon, Label = label, Path = path, Permission = perm };
        }

        private static List<NavSection> All()
        {
            return new List<NavSection>
            {
                /* no label: rendered as a standalone link above the groups */
                new NavSection { Key = "home", Label = null, Items = new List<NavItem> {
                    I("ti-layout-dashboard", "Dashboard", "dashboard", "patients") } },
                new NavSection { Key = "consultation", Label = "Consultation", Items = new List<NavItem> {
                    I("ti-stethoscope", "New Consultation", "patients/search", "consultation"),
                    I("ti-flask", "Lab Results", "lab-results", "consultation"),
                    I("ti-adjustments", "Consultation Setup", "settings/consultation", "admin_consultation") } },
                new NavSection { Key = "patients", Label = "Patients", Items = new List<NavItem> {
                    I("ti-users", "Today's Patients", "patients", "patients"),
                    I("ti-search", "Search / Register", "patients/search", "patients") } },
                new NavSection { Key = "scheduling", Label = "Scheduling", Items = new List<NavItem> {
                    I("ti-calendar", "Appointments", "scheduling/appointments", "scheduling"),
                    I("ti-list-numbers", "Queue Display", "queue-display", "scheduling"),
                    I("ti-heartbeat", "Nurse Vitals", "nurse/vitals", "patients"),
                    I("ti-calendar-cog", "Schedule Settings", "settings/scheduling", "admin_scheduling") } },
                new NavSection { Key = "billing", Label = "Billing", Items = new List<NavItem> {
                    I("ti-file-invoice", "Invoices", "billing/invoices", "billing"),
                    I("ti-cash", "Outstanding Balances", "billing/balances", "billing"),
                    I("ti-report-money", "Day-End Closure", "billing/day-end", "billing") } },
                new NavSection { Key = "reports", Label = "Reports & Comms", Items = new List<NavItem> {
                    I("ti-chart-bar", "Reports", "reports", "reports"),
                    I("ti-message", "Message Log", "communications", "scheduling") } },
                new NavSection { Key = "lab", Label = "Lab Partner", Items = new List<NavItem> {
                    I("ti-microscope", "Lab Portal", "lab-portal", "lab_portal") } },
                new NavSection { Key = "master", Label = "Master Data", Items = new List<NavItem> {
                    I("ti-pill", "Drug Formulary", "settings/formulary", "admin_formulary"),
                    I("ti-vocabulary", "Phrase Library", "settings/phrases", "admin_phrases") } },
                new NavSection { Key = "admin", Label = "Admin", Items = new List<NavItem> {
                    I("ti-settings", "Settings", "settings", "admin_hospital"),
                    I("ti-users-group", "Users", "settings/users", "admin_users"),
                    I("ti-shield-lock", "Permissions", "settings/permissions", "admin_users"),
                    I("ti-history", "Audit Log", "audit-log", "audit_log"),
                    I("ti-download", "Data Export", "data-export", "data_export"),
                    I("ti-upload", "Data Import", "data-import", "data_import") } },
            };
        }

        /// <summary>Sections filtered to the items the role may open; the section holding the current page starts open.</summary>
        public static List<NavSection> ForRole(string role, string currentPath)
        {
            var sections = All();
            foreach (var s in sections)
            {
                s.Items = s.Items.Where(i => Services.UserService.HasPermission(role, i.Permission)).ToList();
                s.IsOpen = s.Items.Any(i => IsCurrent(i, currentPath));
            }
            return sections.Where(s => s.Items.Count > 0).ToList();
        }

        /// <summary>Where a user lands after sign-in: lab partners → Lab Portal, everyone else → Dashboard (or their first allowed page).</summary>
        public static string LandingFor(string role)
        {
            if (role == "LAB_PARTNER") return Urls.LabPortal;
            if (Services.UserService.HasPermission(role, "patients")) return Urls.Dashboard;
            var first = ForRole(role, null).SelectMany(s => s.Items).FirstOrDefault();
            return first != null ? first.Url : Urls.Login;
        }

        public static bool IsCurrent(NavItem item, string currentPath)
        {
            return string.Equals(item.Url.TrimEnd('/'), (currentPath ?? "").TrimEnd('/'), System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
