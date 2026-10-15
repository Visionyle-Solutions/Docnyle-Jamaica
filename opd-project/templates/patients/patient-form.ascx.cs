using System;
using System.Globalization;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Patient demographic fields shared by staff registration/edit and the public self-registration portal.
    /// The identifier and region fields follow the hospital's country (TRN and Parish for Jamaica).
    /// Host pages set <see cref="Hospital"/>, call <see cref="Setup"/> on every request, <see cref="Fill"/> to
    /// load a patient, and <see cref="ApplyTo"/> to copy the posted values onto a record.
    /// </summary>
    public partial class patient_form : UserControl
    {
        private static readonly string[] JamaicaParishes =
        {
            "Kingston", "St. Andrew", "St. Catherine", "Clarendon", "Manchester", "St. Elizabeth", "Westmoreland",
            "Hanover", "St. James", "Trelawny", "St. Ann", "St. Mary", "Portland", "St. Thomas",
        };

        /// <summary>The hospital the patient registers with; drives country fields, health-id rules and the physician list.</summary>
        public Hospital Hospital { get; set; }

        private string CountryCode { get { return (Hospital.Country ?? "").ToUpperInvariant(); } }
        private bool IsJamaica { get { return CountryCode == "JM"; } }

        public string CountryName
        {
            get
            {
                switch (CountryCode)
                {
                    case "JM": return "Jamaica";
                    case "IN": return "India";
                    case "US": return "United States";
                    default: return string.IsNullOrEmpty(Hospital.Country) ? "Generic" : Hospital.Country;
                }
            }
        }

        public string HealthIdLabel { get { return string.IsNullOrEmpty(Hospital.NationalHealthIdLabel) ? "National ID" : Hospital.NationalHealthIdLabel; } }
        protected string RegionLabel { get { return IsJamaica ? "Parish" : CountryCode == "IN" || CountryCode == "US" ? "State" : "Region / State"; } }
        private string DefaultDialCode { get { return CountryCode == "IN" ? "+91" : "+1"; } }

        /// <summary>Applies the hospital's rules (every request) and fills the pick lists and defaults (first request).</summary>
        public void Setup()
        {
            HealthIdRequired.Enabled = HealthIdRequiredMark.Visible = Hospital.NationalHealthIdMandatory;
            HealthIdRequired.ErrorMessage = HealthIdLabel + " is required";
            TrnGroup.Visible = IsJamaica;
            RegionList.Visible = IsJamaica;
            RegionInput.Visible = !IsJamaica;
            if (Page.IsPostBack) return;

            RegionList.Items.Add(new ListItem("—", ""));
            foreach (var parish in JamaicaParishes) RegionList.Items.Add(parish);
            PreferredDoctorInput.Items.Add(new ListItem("—", ""));
            foreach (var d in UserService.ListActiveDoctors(Hospital.HospitalId).OrderBy(d => d.FullName))
                PreferredDoctorInput.Items.Add(new ListItem(d.FullName, d.UserId));
            CountryCodeInput.Text = DefaultDialCode;
            CountryInput.Text = CountryName;
        }

        /// <summary>Loads a patient into the form, splitting the composed name/phone/address of records registered before the structured fields existed.</summary>
        public void Fill(Patient p)
        {
            SetValue(PrefixInput, p.Prefix);
            if (!string.IsNullOrEmpty(p.FirstName) || !string.IsNullOrEmpty(p.LastName))
            {
                FirstNameInput.Text = p.FirstName; MiddleNameInput.Text = p.MiddleName; LastNameInput.Text = p.LastName;
            }
            else
            {
                var parts = (p.FullName ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) FirstNameInput.Text = parts[0];
                if (parts.Length > 1) LastNameInput.Text = parts[parts.Length - 1];
                if (parts.Length > 2) MiddleNameInput.Text = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));
            }
            SetValue(GenderInput, p.Gender);
            DobInput.Text = p.Dob.HasValue ? p.Dob.Value.ToString("yyyy-MM-dd") : "";
            SetValue(BloodGroupInput, p.BloodGroup);
            TrnInput.Text = p.Trn;
            HealthIdInput.Text = p.NationalHealthId;

            if (!string.IsNullOrEmpty(p.Mobile))
            {
                CountryCodeInput.Text = p.CountryCode; MobileInput.Text = p.Mobile;
            }
            else
            {
                var phone = (p.Phone ?? "").Trim();
                var cut = phone.StartsWith("+") ? phone.IndexOfAny(new[] { '-', ' ' }) : -1;
                if (cut > 0) { CountryCodeInput.Text = phone.Substring(0, cut); MobileInput.Text = phone.Substring(cut + 1).Trim(); }
                else MobileInput.Text = phone;
            }
            EmailInput.Text = p.Email;
            LanguageInput.Text = p.PreferredLanguage;

            if (string.IsNullOrEmpty(p.AddrLine1) && string.IsNullOrEmpty(p.AddrLine2) && string.IsNullOrEmpty(p.City))
                AddrLine1Input.Text = p.Address;
            else
            {
                AddrLine1Input.Text = p.AddrLine1; AddrLine2Input.Text = p.AddrLine2; CityInput.Text = p.City;
            }
            if (IsJamaica)
            {
                if (!string.IsNullOrEmpty(p.Region) && RegionList.Items.FindByValue(p.Region) == null) RegionList.Items.Add(p.Region);
                SetValue(RegionList, p.Region);
            }
            else RegionInput.Text = p.Region;
            if (!string.IsNullOrEmpty(p.Country)) CountryInput.Text = p.Country;

            EmergencyNameInput.Text = p.EmergencyContactName;
            EmergencyPhoneInput.Text = p.EmergencyContactPhone;
            SetValue(MaritalInput, p.MaritalStatus);
            OccupationInput.Text = p.Occupation;
            ReferralInput.Text = p.ReferralSource;
            SetValue(PreferredDoctorInput, p.PreferredDoctorId);
        }

        /// <summary>Copies the posted values onto the record, composing FullName, Phone and Address for the rest of the app.</summary>
        public void ApplyTo(Patient record)
        {
            var first = FirstNameInput.Text.Trim();
            var middle = Clean(MiddleNameInput.Text);
            var last = LastNameInput.Text.Trim();
            var cc = Clean(CountryCodeInput.Text);
            if (cc != null && !cc.StartsWith("+")) cc = "+" + cc;
            var mobile = MobileInput.Text.Trim();
            var region = Clean(IsJamaica ? RegionList.SelectedValue : RegionInput.Text);
            var country = Clean(CountryInput.Text);
            var line1 = Clean(AddrLine1Input.Text);
            var line2 = Clean(AddrLine2Input.Text);
            var city = Clean(CityInput.Text);

            record.Prefix = Clean(PrefixInput.SelectedValue);
            record.FirstName = first; record.MiddleName = middle; record.LastName = last;
            record.FullName = JoinParts(" ", first, middle, last);
            record.Gender = GenderInput.SelectedValue;
            record.Dob = ParseDate(DobInput.Text);
            record.BloodGroup = Clean(BloodGroupInput.SelectedValue);
            record.Trn = IsJamaica ? Clean(TrnInput.Text) : null;
            record.NationalHealthId = Clean(HealthIdInput.Text);
            record.CountryCode = cc; record.Mobile = mobile; record.Phone = cc != null ? cc + "-" + mobile : mobile;
            record.Email = Clean(EmailInput.Text);
            record.PreferredLanguage = Clean(LanguageInput.Text);
            record.AddrLine1 = line1; record.AddrLine2 = line2; record.City = city; record.Region = region; record.Country = country;
            record.Address = Clean(JoinParts(", ", line1, line2, city, region, string.Equals(country, CountryName, StringComparison.OrdinalIgnoreCase) ? null : country));
            record.EmergencyContactName = Clean(EmergencyNameInput.Text);
            record.EmergencyContactPhone = Clean(EmergencyPhoneInput.Text);
            record.MaritalStatus = Clean(MaritalInput.SelectedValue);
            record.Occupation = Clean(OccupationInput.Text);
            record.ReferralSource = Clean(ReferralInput.Text);
            record.PreferredDoctorId = Clean(PreferredDoctorInput.SelectedValue);
        }

        /// <summary>Human label for a PatientDuplicate.Field value.</summary>
        public string DuplicateFieldLabel(string field)
        {
            return field == "Mobile" ? "phone number" : field == "Trn" ? "TRN" : HealthIdLabel;
        }

        /// <summary>Puts the cursor on the field a duplicate was found for.</summary>
        public void FocusDuplicate(string field)
        {
            var bad = field == "Mobile" ? (WebControl)MobileInput : field == "Trn" ? TrnInput : HealthIdInput;
            bad.Focus();
        }

        protected void DobValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var d = ParseDate(args.Value);
            args.IsValid = d.HasValue && d.Value <= DateTime.Today && d.Value >= DateTime.Today.AddYears(-130);
        }

        private static DateTime? ParseDate(string value)
        {
            DateTime d;
            return DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }

        private static void SetValue(DropDownList list, string value)
        {
            if (value != null && list.Items.FindByValue(value) != null) list.SelectedValue = value;
        }

        private static string Clean(string s) { s = (s ?? "").Trim(); return s.Length == 0 ? null : s; }

        private static string JoinParts(string separator, params string[] parts)
        {
            return string.Join(separator, parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        }
    }
}
