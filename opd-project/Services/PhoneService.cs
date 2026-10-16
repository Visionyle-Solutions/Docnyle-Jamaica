using System.Linq;
using System.Text.RegularExpressions;

namespace OpdProject.Services
{
    /// <summary>
    /// Phone number rules. A Jamaican hospital (Country = JM) accepts only Jamaican numbers: optional country code +1,
    /// area code 876 or 658, then 7 digits, with optional spaces or dashes — "+1 876 555 1234", "876-555-1234",
    /// "8765551234". US (+1 212 …) and other countries' numbers are rejected. Hospitals elsewhere keep a generic check.
    /// </summary>
    public static class PhoneService
    {
        public const string JamaicaPattern = @"^(?:\+?1[\s-]?)?(?:876|658)[\s-]?\d{3}[\s-]?\d{4}$";
        public const string GenericPattern = @"^\+?[0-9][0-9\- ]{5,15}$";

        /// <summary>True for a Jamaican hospital, or when the hospital is not known (the application is Jamaica-first).</summary>
        public static bool UsesJamaicaRule(string hospitalId)
        {
            var h = string.IsNullOrEmpty(hospitalId) ? null : HospitalService.GetHospital(hospitalId);
            return h == null || string.IsNullOrEmpty(h.Country) || h.Country == "JM";
        }

        public static string Pattern(string hospitalId) { return UsesJamaicaRule(hospitalId) ? JamaicaPattern : GenericPattern; }

        public static bool IsValid(string hospitalId, string phone)
        {
            return Regex.IsMatch((phone ?? "").Trim(), Pattern(hospitalId));
        }

        public static string Message(string hospitalId)
        {
            return UsesJamaicaRule(hospitalId)
                ? "Enter a Jamaican number: area code 876 or 658 and 7 digits, e.g. 876-555-1234"
                : "Phone may contain only digits, spaces, '-' and a leading +";
        }

        public static string Example(string hospitalId) { return UsesJamaicaRule(hospitalId) ? "876-555-1234" : ""; }

        /// <summary>A valid Jamaican number without its +1 / 1 prefix, as "876-555-1234" (for the Mobile field, whose country code is separate).</summary>
        public static string JamaicaLocal(string phone)
        {
            var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length == 11 && digits[0] == '1') digits = digits.Substring(1);
            return digits.Length == 10 ? digits.Substring(0, 3) + "-" + digits.Substring(3, 3) + "-" + digits.Substring(6) : (phone ?? "").Trim();
        }
    }
}
