using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;

namespace OpdProject.Web
{
    /// <summary>Implemented by the Consultation 360 page; gives every section user control the visit context.</summary>
    public interface IConsultationHost
    {
        Consultation Consultation { get; }
        Patient Patient { get; }
        Hospital ConsultHospital { get; }
        string DoctorId { get; }
        bool IsLocked { get; }

        /// <summary>Re-reads the visit and rebinds every section, the safety strip, context panel and progress strip.</summary>
        void RefreshAll();

        /// <summary>Opens a section of the accordion (e.g. after a save, or from a validation error).</summary>
        void ActivateStep(string step);

        /// <summary>Published consultation form configuration (which sections, fields and options are shown).</summary>
        ConsultFormView FormConfig { get; }

        /// <summary>Opening markup of a module card (header + body start) for <see cref="ConsultCard"/>.</summary>
        string CardOpenHtml(string step);

        /// <summary>The custom fields block at the foot of a built-in card ("" when it has none).</summary>
        string CustomFieldsHtml(string step);

        /// <summary>A whole custom-section card ("" when the key is not a shown custom section).</summary>
        string CustomCardHtml(string step);
    }

    /// <summary>Base class for the Consultation 360 section user controls (vitals, complaints, diagnosis, SOAP, …).</summary>
    public abstract class ConsultationSection : UserControl
    {
        protected IConsultationHost Host { get { return (IConsultationHost)Page; } }
        protected BasePage AppPage { get { return (BasePage)Page; } }
        protected Consultation Visit { get { return Host.Consultation; } }
        protected string ConsultationId { get { return Host.Consultation.ConsultationId; } }
        protected string HospitalId { get { return Host.Consultation.HospitalId; } }

        /// <summary>Loads the section's data into its controls.</summary>
        public abstract void Bind();

        /// <summary>This section's key in the consultation form configuration (null = not configurable).</summary>
        protected virtual string SectionKey { get { return null; } }

        /// <summary>True when the configuration shows the given field of this section.</summary>
        protected bool FieldOn(string field) { return Host.FormConfig.FieldOn(SectionKey, field); }

        /// <summary>
        /// Matches a choice field's list to the configuration: removes disabled options (empty "N/A" options stay)
        /// and adds the hospital's custom options.
        /// </summary>
        protected void ApplyOptions(string field, ListControl list)
        {
            foreach (var item in list.Items.Cast<ListItem>().Where(i => i.Value.Length > 0 && !Host.FormConfig.ValueOn(SectionKey, field, i.Value)).ToList())
                list.Items.Remove(item);
            foreach (var option in Host.FormConfig.EnabledOptions(SectionKey, field).Where(o => list.Items.FindByValue(o.Key) == null))
                list.Items.Add(new ListItem(option.Label, option.Key));
        }

        /// <summary>Shows / hides the section's configurable fields. Runs on every request, after any event handler.</summary>
        protected virtual void ApplyFormConfig() { }

        protected override void OnPreRender(System.EventArgs e)
        {
            base.OnPreRender(e);
            ApplyFormConfig();
        }

        /// <summary>Guards write actions once the record is signed and locked (corrections need an addendum).</summary>
        protected bool EnsureNotLocked()
        {
            if (!Host.IsLocked) return true;
            AppPage.ToastWarning("Consultation is locked", "This record was signed and locked. Corrections require an addendum.");
            return false;
        }

        protected void Audit(string action, string entityType, string details = null)
        {
            AuditService.Record(HospitalId, Host.DoctorId, action, entityType, ConsultationId, details);
        }

        protected List<string> YesComplaintLabels()
        {
            return ConsultationService.GetSymptomStates(ConsultationId).Where(kv => kv.Value == "YES")
                .Select(kv => ConsultationService.SymptomLabel(HospitalId, kv.Key)).ToList();
        }
    }

    /// <summary>One row of the patient-context storyboard (key, label, one-line summary).</summary>
    public class ContextRow
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>One complaint chip: code, label and current tri-state.</summary>
    public class SymptomChip
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public string State { get; set; }
    }
}
