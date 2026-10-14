using System;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.patients
{
    /// <summary>
    /// Patient registration (/patients/register) and edit (/patients/{id}/edit). Validated server-side with
    /// validator controls; the national health id is mandatory when the hospital settings say so.
    /// </summary>
    public partial class patient_registration : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return IsEdit ? "Edit Patient" : "New Patient Registration"; } }

        private string EditingId { get { return RouteValue("patientId"); } }
        private bool IsEdit { get { return !string.IsNullOrEmpty(EditingId); } }

        protected void Page_Load(object sender, EventArgs e)
        {
            HeadingText.Text = PageTitle;
            SaveLabel.Text = IsEdit ? "Update patient" : "Register patient";
            HealthIdRequired.Enabled = HealthIdRequiredMark.Visible = Hospital.NationalHealthIdMandatory;
            HealthIdRequired.ErrorMessage = HealthIdLabel + " is required";
            HealthIdInput.Attributes["placeholder"] = HealthIdLabel;
            if (IsPostBack) return;

            if (IsEdit)
            {
                var p = PatientService.Get(EditingId);
                if (p == null || p.HospitalId != HospitalId)
                {
                    ToastWarning("Patient not found");
                    Go(Urls.PatientSearch);
                    return;
                }
                MrnText.Text = "<span class=\"badge badge--info\">" + Server.HtmlEncode(p.Mrn) + "</span>";
                FullNameInput.Text = p.FullName;
                SetValue(GenderInput, p.Gender);
                DobInput.Text = IsoDate(p.Dob);
                PhoneInput.Text = p.Phone;
                EmailInput.Text = p.Email;
                HealthIdInput.Text = p.NationalHealthId;
                AddressInput.Text = p.Address;
                SetValue(SmokingInput, p.SmokingStatus);
                SetValue(AlcoholInput, p.AlcoholUse);
                OccupationInput.Text = p.Occupation;
                SetValue(BloodGroupInput, p.BloodGroup);
                EmergencyNameInput.Text = p.EmergencyContactName;
                EmergencyPhoneInput.Text = p.EmergencyContactPhone;
                InsuranceProviderInput.Text = p.InsuranceProvider;
                InsurancePolicyInput.Text = p.InsurancePolicyNo;
            }
        }

        private static void SetValue(DropDownList list, string value)
        {
            if (value != null && list.Items.FindByValue(value) != null) list.SelectedValue = value;
        }

        protected void DobValidator_ServerValidate(object source, ServerValidateEventArgs args)
        {
            var d = ParseDate(args.Value);
            args.IsValid = d.HasValue && d.Value <= DateTime.Today && d.Value >= DateTime.Today.AddYears(-130);
        }

        private static string Clean(string s) { s = (s ?? "").Trim(); return s.Length == 0 ? null : s; }

        protected void SaveButton_Click(object sender, EventArgs e)
        {
            if (!IsValid) return;
            var record = new Patient
            {
                HospitalId = HospitalId, FullName = FullNameInput.Text.Trim(), Gender = GenderInput.SelectedValue, Dob = ParseDate(DobInput.Text),
                Phone = PhoneInput.Text.Trim(), Email = Clean(EmailInput.Text), Address = Clean(AddressInput.Text),
                NationalHealthId = Clean(HealthIdInput.Text), SmokingStatus = Clean(SmokingInput.SelectedValue), AlcoholUse = Clean(AlcoholInput.SelectedValue),
                Occupation = Clean(OccupationInput.Text), BloodGroup = Clean(BloodGroupInput.SelectedValue),
                EmergencyContactName = Clean(EmergencyNameInput.Text), EmergencyContactPhone = Clean(EmergencyPhoneInput.Text),
                InsuranceProvider = Clean(InsuranceProviderInput.Text), InsurancePolicyNo = Clean(InsurancePolicyInput.Text), RegisteredVia = "FRONT_DESK",
            };
            if (IsEdit)
            {
                PatientService.Update(EditingId, record);
                Audit("UPDATE", "Patient", EditingId);
                ToastSuccess("Patient updated");
            }
            else
            {
                var created = PatientService.Create(record);
                Audit("CREATE", "Patient", created.PatientId);
                ToastSuccess("Patient registered — " + PatientIdLabel + ": " + created.Mrn);
                /* New patients go straight to the Health Record walkthrough: allergies → history → medications → family & social. */
                Go(Urls.Allergies(created.PatientId) + "?new=1");
            }
            Go(Urls.PatientSearch);
        }
    }
}
