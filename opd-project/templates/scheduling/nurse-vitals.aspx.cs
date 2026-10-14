using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.Web
{
    public class NurseVital
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public string Range { get; set; }
        public string Value { get; set; }
    }
}

namespace OpdProject.templates.scheduling
{
    /// <summary>
    /// Nurse vitals pre-entry. Values are entered in the units the consultation setup enables, converted to the
    /// stored base unit, range-validated on the server and saved into the consultation of the appointment's doctor
    /// (created if needed), so they appear when the doctor opens the visit.
    /// </summary>
    public partial class nurse_vitals : BasePage
    {
        protected override string RequiredPermission { get { return "patients"; } }
        public override string PageTitle { get { return "Nurse Vitals Entry"; } }

        private ConsultFormView _form;
        private ConsultFormView FormConfig { get { return _form ?? (_form = ConsultationConfigService.GetView(HospitalId)); } }

        protected string SelectedAppointmentId
        {
            get { return ViewState["appt"] as string; }
            set { ViewState["appt"] = value; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) BindPatients();
        }

        private List<AppointmentView> CheckedIn()
        {
            return SchedulingService.ListAppointments(HospitalId, null, DateTime.Today).Where(a => a.Status == "CHECKED_IN").ToList();
        }

        private void BindPatients()
        {
            var list = CheckedIn();
            PatientsRepeater.DataSource = list;
            PatientsRepeater.DataBind();
            NoPatients.Visible = list.Count == 0;
        }

        protected bool HasVitals(AppointmentView a)
        {
            var c = ConsultationService.FindInProgress(a.PatientId, a.DoctorId);
            return c != null && ConsultationService.HasAnyVital(ConsultationService.GetVitals(c.ConsultationId));
        }

        protected void RefreshButton_Click(object sender, EventArgs e) { BindPatients(); }

        protected void PatientsRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            SelectedAppointmentId = (string)e.CommandArgument;
            BindPatients();
            BindForm();
        }

        private void BindForm()
        {
            var appt = CheckedIn().FirstOrDefault(a => a.AppointmentId == SelectedAppointmentId);
            VitalsCard.Visible = appt != null;
            SelectPrompt.Visible = appt == null;
            if (appt == null) return;
            SelectedName.Text = Server.HtmlEncode(appt.PatientName + " · " + appt.DoctorName);
            var existing = ConsultationService.FindInProgress(appt.PatientId, appt.DoctorId);
            var v = existing != null ? ConsultationService.GetVitals(existing.ConsultationId) : null;
            /* Values and ranges are shown in each vital's default unit */
            Func<string, string> valueOf = code =>
            {
                var val = ConsultationService.GetVital(v, code);
                return val.HasValue ? VitalUnitService.Format(FormConfig.DefaultUnit(code), val.Value) : "";
            };
            Func<string, string> rangeOf = code => VitalUnitService.RangeText(FormConfig.DefaultUnit(code), ConsultationService.RangeFor(HospitalId, code));
            /* The same vitals as the consultation's Vitals card: those the consultation setup captures, in vital order */
            var rows = HospitalService.ListVitalsConfig(HospitalId, false).Where(cfg => FormConfig.VitalOn(cfg.VitalCode))
                .Select(cfg => new NurseVital
                {
                    Code = cfg.VitalCode, Label = cfg.DisplayLabel, Range = rangeOf(cfg.VitalCode), Value = valueOf(cfg.VitalCode),
                })
                .ToList();
            VitalsRepeater.DataSource = rows;
            VitalsRepeater.DataBind();
        }

        protected void VitalsRepeater_ItemDataBound(object sender, RepeaterItemEventArgs e)
        {
            var row = e.Item.DataItem as NurseVital;
            if (row == null) return;
            VitalUnitUi.Bind((DropDownList)e.Item.FindControl("UnitInput"), (Literal)e.Item.FindControl("UnitLabel"), FormConfig.EntryUnits(row.Code));
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            foreach (RepeaterItem item in VitalsRepeater.Items)
            {
                var code = ((HiddenField)item.FindControl("Code")).Value;
                VitalUnitUi.Render((DropDownList)item.FindControl("UnitInput"), (TextBox)item.FindControl("Value"), FormConfig.UnitsFor(code).Units, FormConfig.DefaultUnit(code));
            }
        }

        protected void SaveVitalsButton_Click(object sender, EventArgs e)
        {
            var appt = CheckedIn().FirstOrDefault(a => a.AppointmentId == SelectedAppointmentId);
            if (appt == null) { ToastWarning("This patient is no longer waiting"); BindPatients(); BindForm(); return; }
            var values = new Dictionary<string, double?>();
            var errors = new Dictionary<string, string>();
            foreach (RepeaterItem item in VitalsRepeater.Items)
            {
                var code = ((HiddenField)item.FindControl("Code")).Value;
                var raw = ((TextBox)item.FindControl("Value")).Text.Trim();
                if (raw.Length == 0) continue;
                /* Typed in the picked unit (or the default) → converted to the stored base unit */
                var unit = VitalUnitUi.Selected((DropDownList)item.FindControl("UnitInput"), FormConfig.UnitsFor(code).Units, FormConfig.DefaultUnit(code));
                double d;
                if (unit == null) { errors[code] = "That unit is no longer available — choose another"; continue; }
                if (!VitalUnitService.TryParse(unit, raw, out d)) { errors[code] = unit.IsFeetInches ? "Enter feet and inches, e.g. 5'7\"" : "Enter a number"; continue; }
                var range = ConsultationService.RangeFor(HospitalId, code);
                if (range != null && (d < range.Min || d > range.Max)) { errors[code] = "Must be " + VitalUnitService.RangeText(unit, range); continue; }
                values[code] = d;
            }
            foreach (var kv in ConsultationService.ValidateVitals(values, HospitalId)) if (!errors.ContainsKey(kv.Key)) errors[kv.Key] = kv.Value;
            foreach (RepeaterItem item in VitalsRepeater.Items)
            {
                var code = ((HiddenField)item.FindControl("Code")).Value;
                ((Literal)item.FindControl("Error")).Text = errors.ContainsKey(code) ? Server.HtmlEncode(errors[code]) : "";
            }
            if (errors.Count > 0) { ToastWarning("Please fix the highlighted errors"); return; }
            if (values.Count == 0) { ToastWarning("Enter at least one vital"); return; }

            var consultation = ConsultationService.GetOrCreate(appt.PatientId, appt.DoctorId, HospitalId);
            ConsultationService.SaveVitals(consultation.ConsultationId, values, UserId);
            Audit("CREATE", "NurseVitals", consultation.ConsultationId, "Nurse pre-entered vitals for " + appt.PatientName);
            ToastSuccess("Vitals saved for " + appt.PatientName);
            SelectedAppointmentId = null;
            BindPatients();
            BindForm();
        }
    }
}
