using System;
using System.IO;
using System.Web;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>Serves a patient's signed consent form (PDF or image, inline) to staff of the same hospital. Every access is audited.</summary>
    public partial class patient_consent_file : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Consent Form"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            var doc = PatientService.GetConsentDocument(RouteValue("documentId"));
            if (doc == null || doc.HospitalId != HospitalId || doc.PatientId != RouteValue("patientId")) { NotFound(); return; }
            var path = PatientService.ConsentDocumentPath(doc);
            if (!File.Exists(path)) { NotFound(); return; }

            Audit("VIEW", "PatientConsentDocument", doc.ConsentDocumentId, doc.FileName);
            Response.Clear();
            Response.ContentType = doc.ContentType;
            Response.AddHeader("Content-Disposition", "inline; filename=\"" + SafeName(doc.FileName) + "\"; filename*=UTF-8''" + Uri.EscapeDataString(doc.FileName));
            Response.AddHeader("X-Content-Type-Options", "nosniff");
            Response.Cache.SetCacheability(HttpCacheability.Private);
            Response.Cache.SetNoStore();
            Response.TransmitFile(path);
            Response.End();
        }

        /// <summary>Same response for "missing" and "not yours", so file ids cannot be probed.</summary>
        private void NotFound()
        {
            Response.Clear();
            Response.StatusCode = 404;
            Response.ContentType = "text/plain";
            Response.Write("File not found.");
            Response.End();
        }

        private static string SafeName(string name)
        {
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (chars[i] < 32 || chars[i] > 126 || chars[i] == '"' || chars[i] == '\\') chars[i] = '_';
            return new string(chars);
        }
    }
}
