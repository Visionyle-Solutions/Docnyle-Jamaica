using System;
using System.Web.UI.WebControls;
using OpdProject.Services;

namespace OpdProject.Web
{
    /// <summary>
    /// Validates a phone field against the hospital's rule (PhoneService): Jamaican numbers only for a Jamaican hospital.
    /// Checks in the browser and again on the server; an empty field passes (pair with a RequiredFieldValidator).
    /// Usage: &lt;c360:PhoneValidator runat="server" ControlToValidate="MobileInput" /&gt;
    /// </summary>
    public class PhoneValidator : RegularExpressionValidator
    {
        /// <summary>The hospital whose rule applies; defaults to the signed-in user's hospital.</summary>
        public string HospitalId { get; set; }

        private string ResolvedHospitalId
        {
            get
            {
                if (!string.IsNullOrEmpty(HospitalId)) return HospitalId;
                var user = AppSession.CurrentUser;
                return user != null ? user.HospitalId : null;
            }
        }

        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            if (string.IsNullOrEmpty(CssClass)) CssClass = "field-error";
            if (Display == ValidatorDisplay.Static) Display = ValidatorDisplay.Dynamic;
            if (string.IsNullOrEmpty(Text)) Text = "Invalid phone number";
        }

        /// <summary>Set late so a HospitalId assigned by the page during Load still applies.</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyRule();
        }

        public void ApplyRule()
        {
            ValidationExpression = PhoneService.Pattern(ResolvedHospitalId);
            if (string.IsNullOrEmpty(ErrorMessage) || ErrorMessage.StartsWith("Enter a Jamaican number") || ErrorMessage.StartsWith("Phone may contain"))
                ErrorMessage = PhoneService.Message(ResolvedHospitalId);
        }

        protected override bool EvaluateIsValid()
        {
            ApplyRule();   // the server check always uses the current rule, whatever the browser sent
            return base.EvaluateIsValid();
        }
    }
}
