using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>Hospital profile + logo upload, display formats (with preview) and statutory registrations.</summary>
    public partial class hospital_profile : UserControl
    {
        private BasePage AppPage { get { return (BasePage)Page; } }
        private AppUser User { get { return AppSession.CurrentUser; } }
        private string HospitalId { get { return User.HospitalId; } }

        /// <summary>Default currency for each country in the Country list; Jamaica is the default country.</summary>
        private static readonly Dictionary<string, string> CountryCurrency = new Dictionary<string, string>
        {
            { "JM", "JMD" }, { "IN", "INR" }, { "US", "USD" }, { "GB", "GBP" }, { "AE", "AED" },
        };
        private const string DefaultCountry = "JM";

        protected string CountryCurrencyJson { get { return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(CountryCurrency); } }

        public void Bind()
        {
            var h = HospitalService.GetHospital(HospitalId);
            HpName.Text = h.Name; HpAddress.Text = h.LegalAddress; HpPhone.Text = h.ContactNumber; HpEmail.Text = h.PrimaryEmail;
            var country = string.IsNullOrEmpty(h.Country) ? DefaultCountry : h.Country;
            Select(HpCountry, country);
            Select(HpCurrency, !string.IsNullOrEmpty(h.Currency) ? h.Currency : CountryCurrency.ContainsKey(country) ? CountryCurrency[country] : null);
            HpTimeZone.Items.Clear();
            foreach (var z in TimeZoneService.HospitalZones) HpTimeZone.Items.Add(new ListItem(z.Value, z.Key));
            Select(HpTimeZone, TimeZoneService.HospitalZoneId(HospitalId));
            HpLicense.Text = h.LicenseNumber; HpRxValidity.Text = h.DefaultRxValidityDays.ToString();
            Select(HpTempUnit, h.DefaultTempUnit ?? "F");
            HpPatientIdLabel.Text = h.PatientIdLabel; HpHealthIdLabel.Text = h.NationalHealthIdLabel;
            HpHealthIdMandatory.SelectedValue = h.NationalHealthIdMandatory ? "1" : "0";
            Select(HpGrowthChart, h.GrowthChartStandard);
            LogoPreview.Text = string.IsNullOrEmpty(h.LogoBase64) ? "<span class=\"text-xs text-muted\">No logo</span>"
                : "<img src=\"" + HttpUtility.HtmlAttributeEncode(h.LogoBase64) + "\" style=\"max-width:100%; max-height:100%;\" alt=\"Hospital logo\" />";
            RemoveLogo.Checked = false;

            var s = HospitalService.GetSettings(HospitalId);
            Select(FmtDateFormat, Get(s, "DATE_FORMAT")); Select(FmtTimeFormat, Get(s, "TIME_FORMAT"));
            FmtMrnPrefix.Text = Get(s, "MRN_PREFIX"); FmtMrnFormat.Text = Get(s, "MRN_FORMAT"); FmtNameTemplate.Text = Get(s, "DOCTOR_NAME_TEMPLATE");
            ShowPreview();

            ComplianceRepeater.DataSource = HospitalService.ListCompliances(h.Country);
            ComplianceRepeater.DataBind();
        }

        private static string Get(Dictionary<string, string> s, string k) { return s.ContainsKey(k) ? s[k] : ""; }

        private static void Select(DropDownList list, string value)
        {
            if (value != null && list.Items.FindByValue(value) != null) list.SelectedValue = value;
        }

        protected void SaveProfileButton_Click(object sender, EventArgs e)
        {
            Page.Validate("Profile");
            if (!Page.IsValid) return;
            string logo = null;
            if (LogoUpload.HasFile)
            {
                var ext = Path.GetExtension(LogoUpload.FileName).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") { AppPage.ToastWarning("Logo must be a PNG or JPG image"); return; }
                if (LogoUpload.PostedFile.ContentLength > 200 * 1024) { AppPage.ToastWarning("Logo must be 200 KB or smaller"); return; }
                logo = "data:" + (ext == ".png" ? "image/png" : "image/jpeg") + ";base64," + Convert.ToBase64String(LogoUpload.FileBytes);
            }
            var remove = RemoveLogo.Checked;
            HospitalService.UpdateHospital(HospitalId, h =>
            {
                h.Name = HpName.Text.Trim(); h.LegalAddress = HpAddress.Text.Trim(); h.ContactNumber = HpPhone.Text.Trim(); h.PrimaryEmail = HpEmail.Text.Trim();
                h.Country = HpCountry.SelectedValue; h.Currency = HpCurrency.SelectedValue;
                if (TimeZoneService.IsKnown(HpTimeZone.SelectedValue)) h.Timezone = HpTimeZone.SelectedValue; h.LicenseNumber = HpLicense.Text.Trim();
                int days;
                h.DefaultRxValidityDays = int.TryParse(HpRxValidity.Text, out days) ? days : 30;
                h.DefaultTempUnit = HpTempUnit.SelectedValue;
                h.PatientIdLabel = HpPatientIdLabel.Text.Trim().Length > 0 ? HpPatientIdLabel.Text.Trim() : "MRN";
                h.NationalHealthIdLabel = HpHealthIdLabel.Text.Trim().Length > 0 ? HpHealthIdLabel.Text.Trim() : "National ID";
                h.NationalHealthIdMandatory = HpHealthIdMandatory.SelectedValue == "1";
                h.GrowthChartStandard = HpGrowthChart.SelectedValue;
                if (logo != null) h.LogoBase64 = logo;
                else if (remove) h.LogoBase64 = null;
            });
            AuditService.Record(HospitalId, User.UserId, "UPDATE", "Hospital", HospitalId, "Hospital profile saved");
            AppPage.ToastSuccess("Hospital settings saved");
            Response.Redirect(Request.RawUrl, true);
        }

        private void ShowPreview()
        {
            int seq;
            int.TryParse(HospitalService.GetSetting(HospitalId, "MRN_SEQ"), out seq);
            var mrn = HospitalService.FormatMrn(FmtMrnFormat.Text, seq + 1, FmtMrnPrefix.Text);
            var sample = new AppUser { Prefix = "Dr.", FirstName = "Ravi Teja", MiddleName = "", LastName = "Varma", Qualifications = "MBBS", FullName = "Dr. Ravi Teja Varma" };
            var name = SchedulingService.RenderDoctorName(sample, FmtNameTemplate.Text);
            FormatPreview.Text = "<span class=\"badge badge--info\">Next MRN: " + HttpUtility.HtmlEncode(mrn) + "</span> <span class=\"badge badge--info\">Doctor name: " + HttpUtility.HtmlEncode(name) + "</span>"
                + " <span class=\"badge badge--info\">Today: " + HttpUtility.HtmlEncode(PreviewDate()) + "</span>";
        }

        private string PreviewDate()
        {
            switch (FmtDateFormat.SelectedValue)
            {
                case "DD/MM/YYYY": return ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("dd/MM/yyyy") + " " + (FmtTimeFormat.SelectedValue == "24h" ? ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("HH:mm") : ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("h:mm tt"));
                case "MM/DD/YYYY": return ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("MM/dd/yyyy") + " " + (FmtTimeFormat.SelectedValue == "24h" ? ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("HH:mm") : ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("h:mm tt"));
                default: return ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("yyyy-MM-dd") + " " + (FmtTimeFormat.SelectedValue == "24h" ? ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("HH:mm") : ((OpdProject.Web.BasePage)Page).HospitalNow.ToString("h:mm tt"));
            }
        }

        protected void PreviewFormatsButton_Click(object sender, EventArgs e)
        {
            Page.Validate("Formats");
            if (Page.IsValid) ShowPreview();
        }

        protected void SaveFormatsButton_Click(object sender, EventArgs e)
        {
            Page.Validate("Formats");
            if (!Page.IsValid) return;
            HospitalService.SaveSettings(HospitalId, new Dictionary<string, string>
            {
                { "DATE_FORMAT", FmtDateFormat.SelectedValue }, { "TIME_FORMAT", FmtTimeFormat.SelectedValue }, { "MRN_PREFIX", FmtMrnPrefix.Text.Trim() },
                { "MRN_FORMAT", FmtMrnFormat.Text.Trim() }, { "DOCTOR_NAME_TEMPLATE", FmtNameTemplate.Text.Trim() },
            });
            AuditService.Record(HospitalId, User.UserId, "UPDATE", "HospitalSetting", HospitalId,
                "Formats: " + FmtDateFormat.SelectedValue + ", " + FmtTimeFormat.SelectedValue + ", MRN " + FmtMrnFormat.Text.Trim() + ", name " + FmtNameTemplate.Text.Trim());
            AppPage.ToastSuccess("Formats saved", "Dates, times and doctor names now follow the new formats.");
            ShowPreview();
        }

        protected void ComplianceRepeater_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var c = e.Item.DataItem as Compliance;
            if (c == null) return;
            var hc = HospitalService.GetHospitalCompliance(HospitalId, c.ComplianceId);
            if (hc == null) return;
            ((TextBox)e.Item.FindControl("RegNumber")).Text = hc.RegistrationNumber;
            ((TextBox)e.Item.FindControl("Issued")).Text = hc.IssuedDate.HasValue ? hc.IssuedDate.Value.ToString("yyyy-MM-dd") : "";
            ((TextBox)e.Item.FindControl("Expiry")).Text = hc.ExpiryDate.HasValue ? hc.ExpiryDate.Value.ToString("yyyy-MM-dd") : "";
        }

        protected void SaveComplianceButton_Click(object sender, EventArgs e)
        {
            int saved = 0;
            foreach (RepeaterItem item in ComplianceRepeater.Items)
            {
                var number = ((TextBox)item.FindControl("RegNumber")).Text.Trim();
                if (number.Length == 0) continue;
                DateTime issued, expiry;
                var hasIssued = DateTime.TryParse(((TextBox)item.FindControl("Issued")).Text, out issued);
                var hasExpiry = DateTime.TryParse(((TextBox)item.FindControl("Expiry")).Text, out expiry);
                if (hasIssued && hasExpiry && expiry < issued) { AppPage.ToastWarning("Expiry cannot be before the issue date"); return; }
                HospitalService.SaveHospitalCompliance(HospitalId, ((HiddenField)item.FindControl("ComplianceId")).Value, number,
                    hasIssued ? issued : (DateTime?)null, hasExpiry ? expiry : (DateTime?)null);
                saved++;
            }
            AuditService.Record(HospitalId, User.UserId, "UPDATE", "HospitalCompliance", HospitalId, saved + " registration(s)");
            AppPage.ToastSuccess("Statutory registrations saved");
        }
    }
}
