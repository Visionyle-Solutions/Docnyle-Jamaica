using System;
using System.IO;
using System.Web;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.lab
{
    /// <summary>
    /// Serves a lab report file to the clinic (doctors, lab-portal staff) or to the lab partner that uploaded it.
    /// PDFs and images open inline; DICOM always downloads. Every access is audited.
    /// </summary>
    public partial class lab_report_file : BasePage
    {
        public override string PageTitle { get { return "Lab Report"; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            var att = LabService.GetAttachment(RouteValue("attachmentId"));
            if (!LabService.CanViewAttachment(CurrentUser, att)) { NotFound(); return; }
            var path = LabService.StoredPath(att);
            if (!File.Exists(path)) { NotFound(); return; }

            Audit("VIEW", "LabResultAttachment", att.AttachmentId, att.FileName);
            bool inline = att.ContentType == "application/pdf" || att.ContentType.StartsWith("image/");
            Response.Clear();
            Response.ContentType = att.ContentType;
            Response.AddHeader("Content-Disposition", (inline ? "inline" : "attachment") + "; filename=\"" + SafeName(att.FileName) + "\"; filename*=UTF-8''" + Uri.EscapeDataString(att.FileName));
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
