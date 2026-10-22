using System;
using System.Linq;
using System.Security.Cryptography;
using System.Web;
using OpdProject.Services.Api;

namespace OpdProject.Web
{
    /// <summary>What went wrong with an API call, mapped from the service's Message / ErrorNumber.</summary>
    public enum ApiFaultKind { BusinessRule, Validation, NotFound, Conflict, Unauthenticated, Forbidden, Network }

    /// <summary>An API call that did not return Status 1 (or did not return at all).</summary>
    public class ApiException : Exception
    {
        public ApiFaultKind Kind { get; private set; }
        public int? ErrorNumber { get; private set; }
        public string Operation { get; private set; }

        /// <summary>True when CommonHelper has already shown this failure as a toast, so pages do not show it again.</summary>
        public bool AlreadyShown { get; private set; }

        public ApiException(ApiFaultKind kind, string operation, string message, int? errorNumber = null, Exception inner = null, bool alreadyShown = false)
            : base(message, inner)
        {
            Kind = kind;
            Operation = operation;
            ErrorNumber = errorNumber;
            AlreadyShown = alreadyShown;
        }

        /// <summary>Text that is safe to show the user.</summary>
        public string UserMessage
        {
            get
            {
                switch (Kind)
                {
                    case ApiFaultKind.Network: return "The service could not be reached. Check your connection and try again.";
                    case ApiFaultKind.Unauthenticated: return "Your session has expired. Please sign in again.";
                    case ApiFaultKind.Forbidden: return "You do not have access to this action.";
                    default: return Friendly(Message);
                }
            }
        }

        /// <summary>"INVALID EMAIL OR PASSWORD" → "Invalid email or password".</summary>
        private static string Friendly(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "The request could not be completed.";
            var m = message.Trim().TrimEnd('!');
            if (m.Any(char.IsLower)) return m;
            return char.ToUpperInvariant(m[0]) + m.Substring(1).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Shared helpers for the API calls made through <see cref="Helpers.CommonHelper"/>: not-found handling and the wire
    /// formats of API_Jamaica_Consult360.xlsx (dates, slot times, instants, session tokens).
    /// </summary>
    public static class DocNyle
    {
        /// <summary>
        /// The caller's Status check on a CommonHelper response: returns it when Status is 1, otherwise throws an
        /// <see cref="ApiException"/> built from the service's Message / ErrorNumber so the facade stops. A response with
        /// no Message is one CommonHelper could not get at all (already shown as a toast).
        /// </summary>
        public static T EnsureSuccess<T>(this T resp) where T : RespInfo
        {
            if (resp != null && resp.Status == 1) return resp;
            var op = typeof(T).Name;
            var msg = resp == null ? "" : (resp.Message ?? "").Trim();
            if (msg.Length == 0)
                throw new ApiException(ApiFaultKind.Network, op, "The service could not be reached.", null, null, alreadyShown: true);
            var upper = msg.ToUpperInvariant();
            ApiFaultKind kind;
            if (upper.StartsWith("INVALID SESSION")) kind = ApiFaultKind.Unauthenticated;
            else if (upper.StartsWith("ACCESS DENIED")) kind = ApiFaultKind.Forbidden;
            else if (ConflictNumbers.Contains(resp.ErrorNumber ?? 0)) kind = ApiFaultKind.Conflict;
            else if (upper.Contains("NOT FOUND")) kind = ApiFaultKind.NotFound;
            else if (upper.StartsWith("ENTER MANDATORY") || upper.StartsWith("INVALID")) kind = ApiFaultKind.Validation;
            else kind = ApiFaultKind.BusinessRule;
            throw new ApiException(kind, op, msg, resp.ErrorNumber);
        }

        /// <summary>Error numbers the service reports as a conflict (state changed underneath the caller).</summary>
        private static readonly int[] ConflictNumbers = { 50121, 50122, 50213, 50214, 50220, 50241 };

        /// <summary>Runs a read and returns null instead of throwing when the record is not found.</summary>
        public static T OrNull<T>(Func<T> call) where T : class
        {
            try { return call(); }
            catch (ApiException ex) when (ex.Kind == ApiFaultKind.NotFound) { return null; }
        }

        /* ── Wire formats (API_Jamaica_Consult360.xlsx): dates dd-MM-yyyy, slots dd-MM-yyyy HH:mm, instants ISO-8601 UTC ── */

        private static readonly string[] DateFormats = { "dd-MM-yyyy", "yyyy-MM-dd", "dd-MM-yyyy HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy HH:mm:ss" };

        public static string Date(DateTime? d) { return d.HasValue ? d.Value.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture) : null; }
        public static string DateTimeLocal(DateTime d) { return d.ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture); }
        public static string Instant(DateTime d) { return (d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d).ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>A calendar date / wall-clock value (Kind Unspecified), or null.</summary>
        public static DateTime? ParseLocal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTime d;
            if (DateTime.TryParseExact(s.Trim(), DateFormats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d))
                return DateTime.SpecifyKind(d, DateTimeKind.Unspecified);
            DateTimeOffset o;
            if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out o))
                return DateTime.SpecifyKind(o.DateTime, DateTimeKind.Unspecified);
            return null;
        }

        /// <summary>An ISO-8601 instant as a UTC DateTime, or null.</summary>
        public static DateTime? ParseInstant(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTimeOffset o;
            if (DateTimeOffset.TryParse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out o))
                return DateTime.SpecifyKind(o.UtcDateTime, DateTimeKind.Utc);
            var d = ParseLocal(s);
            return d.HasValue ? DateTime.SpecifyKind(d.Value, DateTimeKind.Utc) : (DateTime?)null;
        }

        public static DateTime InstantOrMin(string s) { return ParseInstant(s) ?? DateTime.MinValue; }
        public static DateTime LocalOrMin(string s) { return ParseLocal(s) ?? DateTime.MinValue; }

        /// <summary>A URL-safe random token of 64 hex characters (256 bits) for CreateUserSession.</summary>
        public static string NewSessionToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }

        /// <summary>The client IP of the current request, for CreateUserSession.</summary>
        public static string ClientIp()
        {
            var ctx = HttpContext.Current;
            return ctx != null && ctx.Request != null ? ctx.Request.UserHostAddress : null;
        }
    }
}
