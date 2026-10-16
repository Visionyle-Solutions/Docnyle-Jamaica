using System.Web;
using System.Web.Routing;

namespace OpdProject.Web
{
    /// <summary>
    /// Friendly URLs that mirror the old SPA hash routes (#/patients → /patients). Each maps to a real
    /// Web Forms page under templates/; the .aspx paths also work directly.
    /// </summary>
    public static class RouteConfig
    {
        public static void Register(RouteCollection routes)
        {
            routes.MapPageRoute("login", "login", "~/templates/settings/login.aspx");
            routes.MapPageRoute("access-denied", "access-denied", "~/templates/shared/access-denied.aspx");

            routes.MapPageRoute("patients", "patients", "~/templates/patients/patient-today-list.aspx");
            routes.MapPageRoute("patient-search", "patients/search", "~/templates/patients/patient-search.aspx");
            routes.MapPageRoute("patient-register", "patients/register", "~/templates/patients/patient-registration.aspx");
            routes.MapPageRoute("patient-edit", "patients/{patientId}/edit", "~/templates/patients/patient-registration.aspx");
            routes.MapPageRoute("patient-allergies", "patients/{patientId}/allergies", "~/templates/patients/patient-allergies.aspx");
            routes.MapPageRoute("patient-coverage", "patients/{patientId}/coverage", "~/templates/patients/patient-coverage.aspx");
            routes.MapPageRoute("patient-consent-form", "patients/{patientId}/consent-form", "~/templates/patients/patient-consent-form.aspx");
            routes.MapPageRoute("patient-consent-file", "patients/{patientId}/consent-file/{documentId}", "~/templates/patients/patient-consent-file.aspx");

            routes.MapPageRoute("consultation", "consultation/patient/{patientId}", "~/templates/consultation/consultation-360.aspx");
            routes.MapPageRoute("print", "print/{document}/{consultationId}", "~/templates/consultation/print-document.aspx");
            routes.MapPageRoute("lab-results", "lab-results", "~/templates/lab/lab-results.aspx");
            routes.MapPageRoute("lab-portal", "lab-portal", "~/templates/lab/lab-portal.aspx");
            routes.MapPageRoute("lab-report-file", "lab-results/file/{attachmentId}", "~/templates/lab/lab-report-file.aspx");

            routes.MapPageRoute("appointments", "scheduling/appointments", "~/templates/scheduling/appointment-calendar.aspx");
            routes.MapPageRoute("queue-display", "queue-display", "~/templates/scheduling/queue-display.aspx");
            routes.MapPageRoute("nurse-vitals", "nurse/vitals", "~/templates/scheduling/nurse-vitals.aspx");

            routes.MapPageRoute("invoices", "billing/invoices", "~/templates/billing/invoice-list.aspx");
            routes.MapPageRoute("checkout", "billing/checkout/{consultationId}", "~/templates/billing/billing-checkout.aspx");
            routes.MapPageRoute("invoice-print", "billing/print/{invoiceId}", "~/templates/billing/invoice-print.aspx");
            routes.MapPageRoute("balances", "billing/balances", "~/templates/billing/patient-balances.aspx");
            routes.MapPageRoute("day-end", "billing/day-end", "~/templates/billing/day-end.aspx");

            routes.MapPageRoute("dashboard", "dashboard", "~/templates/reports/home-dashboard.aspx");
            routes.MapPageRoute("dashboard-list", "dashboard/list", "~/templates/reports/dashboard-list.aspx");
            routes.MapPageRoute("reports", "reports", "~/templates/reports/reports-dashboard.aspx");
            routes.MapPageRoute("communications", "communications", "~/templates/communications/message-log.aspx");

            routes.MapPageRoute("settings", "settings", "~/templates/settings/settings-hub.aspx");
            routes.MapPageRoute("settings-hospital", "settings/hospital", "~/templates/settings/settings-hub.aspx", false, new RouteValueDictionary { { "tab", "hospital" } });
            routes.MapPageRoute("settings-vitals", "settings/vitals", "~/templates/settings/settings-hub.aspx", false, new RouteValueDictionary { { "tab", "clinical" } });
            routes.MapPageRoute("settings-specialties", "settings/specialties", "~/templates/settings/settings-hub.aspx", false, new RouteValueDictionary { { "tab", "clinical" } });
            routes.MapPageRoute("schedule-settings", "settings/scheduling", "~/templates/settings/schedule-settings.aspx");
            routes.MapPageRoute("consultation-config", "settings/consultation", "~/templates/settings/consultation-config.aspx");
            routes.MapPageRoute("formulary", "settings/formulary", "~/templates/settings/formulary-management.aspx");
            routes.MapPageRoute("inventory", "settings/inventory", "~/templates/settings/inventory-management.aspx");
            routes.MapPageRoute("phrases", "settings/phrases", "~/templates/settings/phrase-management.aspx");
            routes.MapPageRoute("users", "settings/users", "~/templates/settings/user-management.aspx");
            routes.MapPageRoute("permissions", "settings/permissions", "~/templates/settings/permissions.aspx");
            routes.MapPageRoute("audit-log", "audit-log", "~/templates/settings/audit-log.aspx");
            routes.MapPageRoute("data-export", "data-export", "~/templates/settings/data-export.aspx");
            routes.MapPageRoute("data-import", "data-import", "~/templates/settings/data-import.aspx");

            routes.MapPageRoute("portal-register", "portal/register", "~/templates/portal/self-register.aspx");
            routes.MapPageRoute("portal-book", "portal/book", "~/templates/portal/self-book.aspx");
        }
    }

    /// <summary>Builds application-absolute URLs for the friendly routes.</summary>
    public static class Urls
    {
        public static string To(string path) { return VirtualPathUtility.ToAbsolute("~/" + path.TrimStart('/')); }

        private static string E(string id) { return HttpUtility.UrlEncode(id ?? ""); }

        public static string Login { get { return To("login"); } }
        public static string Dashboard { get { return To("dashboard"); } }
        /// <summary>
        /// The list behind one dashboard tile or pie slice: today's go to the Appointment Summary (a single-day list);
        /// this week / this month go to the dashboard list page, which covers the whole period.
        /// </summary>
        public static string DashboardList(string kind, string period, string status)
        {
            var filters = "?kind=" + E(kind) + (string.IsNullOrEmpty(status) ? "" : "&status=" + E(status));
            return period == "today" ? To("patients") + filters : To("dashboard/list") + filters + "&period=" + E(period);
        }
        public static string TodayPatients { get { return To("patients"); } }
        public static string PatientSearch { get { return To("patients/search"); } }
        public static string PatientRegister { get { return To("patients/register"); } }
        public static string PatientEdit(string patientId) { return To("patients/" + E(patientId) + "/edit"); }
        public static string Allergies(string patientId) { return To("patients/" + E(patientId) + "/allergies"); }
        public static string PatientCoverage(string patientId) { return To("patients/" + E(patientId) + "/coverage"); }
        public static string PatientConsentForm(string patientId) { return To("patients/" + E(patientId) + "/consent-form"); }
        public static string PatientConsentFile(string patientId, string documentId) { return To("patients/" + E(patientId) + "/consent-file/" + E(documentId)); }
        public static string Consultation(string patientId) { return To("consultation/patient/" + E(patientId)); }
        public static string ConsultationConfig { get { return To("settings/consultation"); } }
        public static string Print(string document, string consultationId) { return To("print/" + document + "/" + E(consultationId)); }
        public static string Checkout(string consultationId) { return To("billing/checkout/" + E(consultationId)); }
        public static string InvoicePrint(string invoiceId) { return To("billing/print/" + E(invoiceId)); }
        public static string LabPortal { get { return To("lab-portal"); } }
        public static string LabReportFile(string attachmentId) { return To("lab-results/file/" + E(attachmentId)); }
    }
}
