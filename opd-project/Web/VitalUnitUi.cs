using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using OpdProject.Models;

namespace OpdProject.Web
{
    /// <summary>
    /// The unit picker next to a vital input (Consultation 360 vitals, Nurse Vitals). With one entry unit it shows
    /// the unit as text; with several, a dropdown whose options carry the conversion for js/site.js, which converts
    /// what is typed when the unit is switched. Values are converted to the base unit on the server when saved.
    /// </summary>
    public static class VitalUnitUi
    {
        private static void SetUnitData(System.Web.UI.AttributeCollection a, VitalUnit u)
        {
            a["data-f"] = u.Factor.ToString("R", CultureInfo.InvariantCulture);
            a["data-o"] = u.Offset.ToString("R", CultureInfo.InvariantCulture);
            a["data-d"] = u.Decimals.ToString(CultureInfo.InvariantCulture);
            a["data-fi"] = u.IsFeetInches ? "1" : "0";
        }

        /// <summary>Fills the picker (or the unit label) at bind time; <paramref name="units"/> starts with the default unit.</summary>
        public static void Bind(DropDownList picker, Literal label, IList<VitalUnit> units)
        {
            picker.Items.Clear();
            picker.Visible = units.Count > 1;
            label.Text = units.Count > 1 ? "" : HttpUtility.HtmlEncode(units[0].Label);
            if (picker.Visible) picker.Items.AddRange(units.Select(u => new ListItem(u.Label, u.Key)).ToArray());
        }

        /// <summary>
        /// Writes the conversion data on every request (option attributes are not kept in view state) and links the
        /// picker and the input by client id. <paramref name="allUnits"/> is every unit the vital has.
        /// </summary>
        public static void Render(DropDownList picker, TextBox input, IList<VitalUnit> allUnits, VitalUnit defaultUnit)
        {
            if (picker.Visible)
            {
                picker.Attributes["data-vital-unit"] = input.ClientID;
                picker.Attributes["aria-label"] = "Unit";
                input.Attributes["data-unit-select"] = picker.ClientID;
                foreach (ListItem item in picker.Items)
                {
                    var u = allUnits.FirstOrDefault(x => x.Key == item.Value);
                    if (u != null) SetUnitData(item.Attributes, u);
                }
            }
            else
            {
                input.Attributes.Remove("data-unit-select");
                SetUnitData(input.Attributes, defaultUnit);
            }
        }

        /// <summary>The unit a value was typed in: the picked unit, or <paramref name="fallback"/> (the default) with no picker. Null if the picked unit no longer exists.</summary>
        public static VitalUnit Selected(DropDownList picker, IList<VitalUnit> allUnits, VitalUnit fallback)
        {
            return picker.Visible && picker.SelectedItem != null ? allUnits.FirstOrDefault(x => x.Key == picker.SelectedValue) : fallback;
        }
    }
}
