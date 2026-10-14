using System.Web.UI;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.settings
{
    /// <summary>
    /// The hospital's vitals, read-only: whether each is recorded, its unit and valid range. Vitals are added, switched
    /// on and off and given their units in Consultation Setup. Built-in vitals keep their fixed ranges.
    /// </summary>
    public partial class vitals_config : UserControl
    {
        private string HospitalId { get { return AppSession.CurrentUser.HospitalId; } }

        public void Bind()
        {
            VitalsRepeater.DataSource = HospitalService.ListVitalsConfig(HospitalId, false);
            VitalsRepeater.DataBind();
        }

        protected string RangeLabel(string code)
        {
            var r = ConsultationService.RangeFor(HospitalId, code);
            return r != null ? r.Label : "Any value";
        }
    }
}
