using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Linq;
using System.Runtime.Caching;
using System.Web;

namespace OpdProject.Web
{
    /// <summary>
    /// Per-module data source switch (API-INTEGRATION-GUIDE.md §4.1): appSettings "DataSource:{Module}" =
    /// "Api" (the DOCNyle web API; "Wcf" is accepted too) or "InProc" (the in-memory DataStore demo data).
    /// </summary>
    public static class DataSource
    {
        public static readonly string[] Modules =
            { "Users", "Hospital", "Patients", "Scheduling", "Consultation", "Catalog", "Lab", "Inventory", "Billing", "Operations", "ConsultationConfig", "UserNotifications" };

        public static bool IsApi(string module)
        {
            var v = ConfigurationManager.AppSettings["DataSource:" + module];
            return string.Equals(v, "Api", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "Wcf", StringComparison.OrdinalIgnoreCase);
        }

        public static bool AnyInProc { get { return Modules.Any(m => !IsApi(m)); } }
    }

    /// <summary>Values cached for the current request only (HttpContext.Items) — patient data never outlives the request.</summary>
    public static class RequestCache
    {
        public static T Get<T>(string key, Func<T> load)
        {
            var ctx = HttpContext.Current;
            if (ctx == null) return load();
            var k = "rc:" + key;
            if (ctx.Items.Contains(k)) return (T)ctx.Items[k];
            var v = load();
            ctx.Items[k] = v;
            return v;
        }

        public static void Remove(string key)
        {
            var ctx = HttpContext.Current;
            if (ctx != null) ctx.Items.Remove("rc:" + key);
        }

        /// <summary>Drops every request-cached value whose key starts with the prefix (after a write).</summary>
        public static void RemovePrefix(string prefix)
        {
            var ctx = HttpContext.Current;
            if (ctx == null) return;
            foreach (var k in ctx.Items.Keys.OfType<string>().Where(k => k.StartsWith("rc:" + prefix)).ToList()) ctx.Items.Remove(k);
        }
    }

    /// <summary>
    /// Hospital reference data (settings, specialties, vitals config, appointment types, users, …) cached
    /// per hospital for 5 minutes. Facade save methods clear their entry.
    /// </summary>
    public static class HospitalCache
    {
        private static readonly MemoryCache Cache = MemoryCache.Default;
        private static readonly ConcurrentDictionary<string, object> Locks = new ConcurrentDictionary<string, object>();
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

        private static string HospitalKey(string key)
        {
            var u = AppSession.ApiUser;
            return "hc:" + (u != null ? u.HospitalId : "-") + ":" + key;
        }

        public static T Get<T>(string key, Func<T> load) where T : class
        {
            var k = HospitalKey(key);
            var hit = Cache.Get(k) as T;
            if (hit != null) return hit;
            lock (Locks.GetOrAdd(k, _ => new object()))
            {
                hit = Cache.Get(k) as T;
                if (hit != null) return hit;
                var v = load();
                if (v != null) Cache.Set(k, v, DateTimeOffset.UtcNow.Add(Ttl));
                return v;
            }
        }

        public static void Clear(string key) { Cache.Remove(HospitalKey(key)); }

        /// <summary>Clears every entry of the caller's hospital whose key starts with the prefix.</summary>
        public static void ClearPrefix(string prefix)
        {
            var p = HospitalKey(prefix);
            foreach (var k in Cache.Select(e => e.Key).Where(k => k.StartsWith(p)).ToList()) Cache.Remove(k);
        }
    }

    /// <summary>
    /// Catalog rows (drugs, lab tests, phrases) the user has been shown, kept in session by id so a later postback can act
    /// on "the row that was clicked" when the API has no get-by-id operation for it. Never holds patient data.
    /// </summary>
    public static class SessionIndex
    {
        private const int MaxEntries = 500;

        private static System.Collections.Generic.Dictionary<string, object> Index
        {
            get
            {
                var s = HttpContext.Current != null ? HttpContext.Current.Session : null;
                if (s == null) return new System.Collections.Generic.Dictionary<string, object>();
                var d = s["Consult360.SessionIndex"] as System.Collections.Generic.Dictionary<string, object>;
                if (d == null) s["Consult360.SessionIndex"] = d = new System.Collections.Generic.Dictionary<string, object>();
                return d;
            }
        }

        public static T Put<T>(string kind, string id, T value) where T : class
        {
            if (string.IsNullOrEmpty(id) || value == null) return value;
            var d = Index;
            if (d.Count >= MaxEntries) d.Clear();
            d[kind + ":" + id] = value;
            return value;
        }

        public static T Get<T>(string kind, string id) where T : class
        {
            object v;
            return !string.IsNullOrEmpty(id) && Index.TryGetValue(kind + ":" + id, out v) ? v as T : null;
        }
    }
}
