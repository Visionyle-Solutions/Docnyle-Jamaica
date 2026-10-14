using System.Web.UI;

namespace OpdProject.templates.shared
{
    /// <summary>Centered error panel (e.g. "Patient not found.").</summary>
    public partial class error_state : UserControl
    {
        public string Message { get; set; }
    }
}
