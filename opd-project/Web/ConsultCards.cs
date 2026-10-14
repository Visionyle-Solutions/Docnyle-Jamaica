using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using OpdProject.Models;

namespace OpdProject.Web
{
    /// <summary>
    /// One module card on the Consultation 360 page. Renders the card frame (header from the host page) around its
    /// section user control, followed by the section's custom fields. Hidden (Visible = false) when the section is
    /// disabled in the consultation configuration.
    /// </summary>
    public class ConsultCard : PlaceHolder
    {
        /// <summary>Workflow step / configuration section key, e.g. "vitals".</summary>
        public string Step { get; set; }

        protected override void Render(HtmlTextWriter writer)
        {
            var host = Page as IConsultationHost;
            writer.Write(host != null ? host.CardOpenHtml(Step) : "<div><div>");
            RenderChildren(writer);
            if (host != null) writer.Write(host.CustomFieldsHtml(Step));
            writer.Write("</div></div>");
        }
    }

    /// <summary>
    /// Holds the consultation cards and renders them in the configured order, with the hospital's custom-section
    /// cards in their configured places. Only the render order changes — the control tree stays as declared, so
    /// view state and postbacks are unaffected by re-ordering or by custom sections coming and going.
    /// </summary>
    public class ConsultCardList : PlaceHolder
    {
        /// <summary>Section keys in display order; cards not listed keep their declared order at the end.</summary>
        public IList<string> Order { get; set; }

        public IEnumerable<ConsultCard> Cards { get { return Controls.OfType<ConsultCard>(); } }

        protected override void RenderChildren(HtmlTextWriter writer)
        {
            var host = Page as IConsultationHost;
            var cards = Cards.ToList();
            foreach (var key in Order ?? new List<string>())
            {
                var card = cards.FirstOrDefault(c => c.Step == key);
                if (card != null) { card.RenderControl(writer); cards.Remove(card); }
                else if (host != null) writer.Write(host.CustomCardHtml(key));
            }
            foreach (var card in cards) card.RenderControl(writer);
        }
    }

    /// <summary>
    /// Plain HTML inputs for custom fields (named cf_{fieldKey}); they are read back from the posted form when the
    /// section's Save button is pressed, so they need no server controls and survive any configuration change.
    /// </summary>
    public static class CustomFieldRenderer
    {
        private static string E(string s) { return HttpUtility.HtmlEncode(s ?? ""); }
        private static string A(string s) { return HttpUtility.HtmlAttributeEncode(s ?? ""); }

        public static string InputName(string fieldKey) { return "cf_" + fieldKey; }

        /// <param name="section">Section the fields belong to (its key names the Save action).</param>
        /// <param name="fields">Shown custom fields, in order.</param>
        /// <param name="value">Current value of a field (posted or saved).</param>
        /// <param name="errors">Validation messages by field key.</param>
        /// <param name="standalone">True for a custom-section card (no "Additional fields" heading).</param>
        public static string Block(ConsultFormSection section, IList<ConsultFormField> fields, Func<string, string> value,
            IDictionary<string, string> errors, bool locked, bool standalone)
        {
            if (fields.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append("<div class=\"c360-custom").Append(standalone ? " c360-custom--standalone" : "").Append("\" data-custom-section=\"").Append(A(section.Key)).Append("\">");
            if (!standalone) sb.Append("<div class=\"c360-subhead\"><i class=\"ti ti-forms\" aria-hidden=\"true\"></i> Additional fields</div>");
            sb.Append("<input type=\"hidden\" name=\"cfs_").Append(A(section.Key)).Append("\" value=\"1\" />");
            sb.Append("<div class=\"form-grid\">");
            foreach (var f in fields)
            {
                var id = InputName(f.Key);
                var current = value(f.Key) ?? "";
                var wide = f.InputType == "TEXTAREA" || f.InputType == "MULTI";
                sb.Append("<div class=\"form-group").Append(wide ? " form-group--full" : "").Append("\">");
                if (f.InputType == "MULTI") sb.Append("<span class=\"form-label\" id=\"").Append(id).Append("_l\">");
                else sb.Append("<label class=\"form-label\" for=\"").Append(id).Append("\">");
                sb.Append(E(f.Label));
                if (f.RequiredAtSignOff) sb.Append(" <span class=\"required\" title=\"Required before sign-off\">*</span>");
                sb.Append(f.InputType == "MULTI" ? "</span>" : "</label>");
                var dis = locked ? " disabled" : "";
                switch (f.InputType)
                {
                    case "TEXTAREA":
                        sb.Append("<textarea class=\"form-control\" id=\"").Append(id).Append("\" name=\"").Append(id).Append("\" maxlength=\"2000\" rows=\"3\"").Append(dis).Append(">")
                          .Append(E(current)).Append("</textarea>");
                        break;
                    case "NUMBER":
                        sb.Append("<input type=\"number\" step=\"any\" class=\"form-control\" id=\"").Append(id).Append("\" name=\"").Append(id).Append("\" value=\"").Append(A(current)).Append("\"").Append(dis).Append(" />");
                        break;
                    case "DATE":
                        sb.Append("<input type=\"date\" class=\"form-control\" id=\"").Append(id).Append("\" name=\"").Append(id).Append("\" value=\"").Append(A(current)).Append("\"").Append(dis).Append(" />");
                        break;
                    case "YESNO":
                        sb.Append("<select class=\"form-select\" id=\"").Append(id).Append("\" name=\"").Append(id).Append("\"").Append(dis).Append(">")
                          .Append("<option value=\"\">—</option>")
                          .Append("<option value=\"YES\"").Append(current == "YES" ? " selected" : "").Append(">Yes</option>")
                          .Append("<option value=\"NO\"").Append(current == "NO" ? " selected" : "").Append(">No</option></select>");
                        break;
                    case "SELECT":
                        sb.Append("<select class=\"form-select\" id=\"").Append(id).Append("\" name=\"").Append(id).Append("\"").Append(dis).Append("><option value=\"\">—</option>");
                        foreach (var o in Options(f, new[] { current }))
                            sb.Append("<option value=\"").Append(A(o.Key)).Append("\"").Append(o.Key == current ? " selected" : "").Append(">").Append(E(o.Label)).Append("</option>");
                        sb.Append("</select>");
                        break;
                    case "MULTI":
                        var chosen = current.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                        sb.Append("<div class=\"c360-custom-checks\" role=\"group\" aria-labelledby=\"").Append(id).Append("_l\">");
                        foreach (var o in Options(f, chosen))
                            sb.Append("<label class=\"c360-custom-check\"><input type=\"checkbox\" name=\"").Append(id).Append("\" value=\"").Append(A(o.Key)).Append("\"")
                              .Append(chosen.Contains(o.Key) ? " checked" : "").Append(dis).Append(" /> ").Append(E(o.Label)).Append("</label>");
                        sb.Append("</div>");
                        break;
                    default:
                        sb.Append("<input type=\"text\" class=\"form-control\" id=\"").Append(id).Append("\" name=\"").Append(id).Append("\" maxlength=\"250\" value=\"").Append(A(current)).Append("\"").Append(dis).Append(" />");
                        break;
                }
                if (!string.IsNullOrEmpty(f.Description)) sb.Append("<div class=\"text-xs text-muted mt-1\">").Append(E(f.Description)).Append("</div>");
                string error;
                if (errors != null && errors.TryGetValue(f.Key, out error)) sb.Append("<span class=\"field-error\">").Append(E(error)).Append("</span>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
            if (!locked)
                sb.Append("<button type=\"button\" class=\"btn btn--sm btn--primary mt-2\" data-custom-save=\"").Append(A(section.Key)).Append("\">")
                  .Append("<i class=\"ti ti-device-floppy\" aria-hidden=\"true\"></i> Save ").Append(standalone ? E(section.Label) : "additional fields").Append("</button>");
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>Offered options, plus any already-recorded option that has since been switched off (so the record still shows it).</summary>
        private static IEnumerable<ConsultFormValue> Options(ConsultFormField f, IEnumerable<string> recorded)
        {
            var keys = new HashSet<string>(recorded.Where(k => !string.IsNullOrEmpty(k)));
            return f.Values.Where(v => v.Enabled || keys.Contains(v.Key));
        }
    }
}
