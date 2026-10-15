using System;
using System.Collections.Generic;
using System.Linq;
using OpdProject.Models;

namespace OpdProject.Services
{
    /// <summary>Authentication, user management and the role → feature permission matrix.</summary>
    public static class UserService
    {
        public static readonly string[] Roles = { "ADMIN", "DOCTOR", "RECEPTIONIST", "BILLING", "NURSE", "LAB_PARTNER" };

        /// <summary>Roles an administrator can assign from User Management. A LAB_PARTNER account must be linked to a lab partner.</summary>
        public static readonly string[] AssignableRoles = { "ADMIN", "DOCTOR", "RECEPTIONIST", "BILLING", "NURSE", "LAB_PARTNER" };

        public static readonly Dictionary<string, string> FeatureLabels = new Dictionary<string, string>
        {
            { "consultation", "Consultation" }, { "patients", "Patients" }, { "scheduling", "Scheduling" },
            { "billing", "Billing & Invoices" }, { "reports", "Reports" }, { "admin_hospital", "Hospital Profile" },
            { "admin_scheduling", "Schedule Settings" }, { "admin_vitals", "Vitals Config" }, { "admin_specialty", "Specialty Config" },
            { "admin_formulary", "Drug Formulary" }, { "admin_phrases", "Phrase Library" }, { "admin_users", "User Management" },
            { "audit_log", "Audit Log" }, { "data_export", "Data Export" }, { "data_import", "CSV Import" },
            { "print", "Print Documents" }, { "lab_portal", "Lab Portal" }, { "admin_consultation", "Consultation Setup" },
        };

        /// <summary>Verifies e-mail + password for an active user whose hospital exists. Returns null on failure.</summary>
        public static AppUser Authenticate(string email, string password)
        {
            lock (DataStore.Sync)
            {
                var user = DataStore.Users.FirstOrDefault(u =>
                    string.Equals(u.Email, (email ?? "").Trim(), StringComparison.OrdinalIgnoreCase) && u.Password == password && u.IsActive);
                if (user == null) return null;
                return DataStore.Hospitals.Any(h => h.HospitalId == user.HospitalId) ? user : null;
            }
        }

        public static AppUser GetUser(string userId)
        {
            lock (DataStore.Sync) return DataStore.Users.FirstOrDefault(u => u.UserId == userId);
        }

        public static List<AppUser> ListUsers(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.Users.Where(u => u.HospitalId == hospitalId).ToList();
        }

        public static List<AppUser> ListActiveDoctors(string hospitalId)
        {
            lock (DataStore.Sync) return DataStore.Users.Where(u => u.HospitalId == hospitalId && u.HasRole("DOCTOR") && u.IsActive).ToList();
        }

        public static string UserName(string userId)
        {
            var u = GetUser(userId);
            return u != null ? u.FullName : userId;
        }

        public static bool EmailInUse(string email, string exceptUserId)
        {
            lock (DataStore.Sync)
                return DataStore.Users.Any(u => u.UserId != exceptUserId && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Distinct, known roles in canonical (Roles) order. Returns null when the combination is not allowed:
        /// no roles, or LAB_PARTNER mixed with a clinic role (an outside lab must never inherit clinical access).
        /// </summary>
        public static List<string> NormalizeRoles(IEnumerable<string> roles)
        {
            var set = new HashSet<string>(roles ?? Enumerable.Empty<string>());
            var list = Roles.Where(set.Contains).ToList();
            if (list.Count == 0 || (list.Contains("LAB_PARTNER") && list.Count > 1)) return null;
            return list;
        }

        /// <summary>labPartnerId is kept only for LAB_PARTNER accounts; specialty only for clinic roles.</summary>
        public static AppUser CreateUser(string hospitalId, string fullName, string email, IEnumerable<string> roles, string specialtyId, string password, string labPartnerId)
        {
            var list = NormalizeRoles(roles);
            if (list == null) throw new ArgumentException("Invalid role combination", "roles");
            bool lab = list.Contains("LAB_PARTNER");
            var user = new AppUser
            {
                UserId = DataStore.NextId("USR"), HospitalId = hospitalId, FullName = fullName, Email = email, Phone = "",
                Roles = list, SpecialtyId = lab || string.IsNullOrWhiteSpace(specialtyId) ? null : specialtyId,
                LabPartnerId = lab ? labPartnerId : null,
                Password = string.IsNullOrEmpty(password) ? "demo123" : password, IsActive = true
            };
            if (user.HasRole("DOCTOR"))
            {
                var parts = fullName.Replace("Dr.", "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                user.Prefix = "Dr.";
                user.FirstName = parts.Length > 1 ? string.Join(" ", parts.Take(parts.Length - 1)) : (parts.FirstOrDefault() ?? "");
                user.LastName = parts.Length > 1 ? parts.Last() : "";
            }
            lock (DataStore.Sync) DataStore.Users.Add(user);
            return user;
        }

        public static void UpdateUser(string userId, string fullName, string email, IEnumerable<string> roles, string specialtyId, string labPartnerId)
        {
            var list = NormalizeRoles(roles);
            if (list == null) throw new ArgumentException("Invalid role combination", "roles");
            bool lab = list.Contains("LAB_PARTNER");
            lock (DataStore.Sync)
            {
                var u = DataStore.Users.FirstOrDefault(x => x.UserId == userId);
                if (u == null) return;
                u.FullName = fullName;
                u.Email = email;
                u.Roles = list;
                u.SpecialtyId = lab || string.IsNullOrWhiteSpace(specialtyId) ? null : specialtyId;
                u.LabPartnerId = lab ? labPartnerId : null;
            }
        }

        public static AppUser ToggleActive(string userId)
        {
            lock (DataStore.Sync)
            {
                var u = DataStore.Users.FirstOrDefault(x => x.UserId == userId);
                if (u != null) u.IsActive = !u.IsActive;
                return u;
            }
        }

        /* ── Permission matrix ── */

        /// <summary>True when any of the user's roles grants the feature (null/empty feature = any signed-in user).</summary>
        public static bool HasPermission(AppUser user, string feature)
        {
            if (string.IsNullOrEmpty(feature)) return true;
            return user != null && user.Roles.Any(r => HasPermission(r, feature));
        }

        public static bool HasPermission(string role, string feature)
        {
            if (string.IsNullOrEmpty(feature)) return true;
            lock (DataStore.Sync)
            {
                Dictionary<string, bool> perms;
                bool allowed;
                return role != null && DataStore.Permissions.TryGetValue(role, out perms) && perms.TryGetValue(feature, out allowed) && allowed;
            }
        }

        public static List<string> ListFeatures()
        {
            lock (DataStore.Sync) return DataStore.Permissions["ADMIN"].Keys.ToList();
        }

        public static List<PermissionRow> GetPermissionMatrix()
        {
            lock (DataStore.Sync)
            {
                return ListFeatures().Select(f => new PermissionRow
                {
                    Feature = f,
                    Label = FeatureLabels.ContainsKey(f) ? FeatureLabels[f] : f,
                    Access = Roles.ToDictionary(r => r, r => DataStore.Permissions[r][f]),
                }).ToList();
            }
        }

        /// <summary>Flips one role/feature cell. ADMIN always keeps full access.</summary>
        public static bool TogglePermission(string role, string feature)
        {
            if (role == "ADMIN") return true;
            lock (DataStore.Sync)
            {
                Dictionary<string, bool> perms;
                if (!DataStore.Permissions.TryGetValue(role, out perms) || !perms.ContainsKey(feature)) return false;
                perms[feature] = !perms[feature];
                return perms[feature];
            }
        }
    }

    /// <summary>Append-only audit trail (sec.AuditLog).</summary>
    public static class AuditService
    {
        public static void Record(string hospitalId, string userId, string action, string entityType, string entityId, string details = null)
        {
            var row = new AuditLogEntry
            {
                AuditLogId = DataStore.NextId("AUD"), HospitalId = hospitalId, UserId = userId, Action = action,
                EntityType = entityType, EntityId = entityId, Details = details, Timestamp = DateTime.Now
            };
            var ctx = System.Web.HttpContext.Current;
            if (ctx != null && ctx.Request != null)
            {
                row.IpAddress = ctx.Request.UserHostAddress;
                row.UserAgent = ctx.Request.UserAgent;
            }
            lock (DataStore.Sync) DataStore.AuditLogs.Add(row);
        }

        public static List<AuditLogEntry> List(string hospitalId, string action, string entityType, DateTime? from, DateTime? to)
        {
            lock (DataStore.Sync)
            {
                IEnumerable<AuditLogEntry> q = DataStore.AuditLogs.Where(a => a.HospitalId == hospitalId);
                if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action == action);
                if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
                if (from.HasValue) q = q.Where(a => a.Timestamp >= from.Value.Date);
                if (to.HasValue) q = q.Where(a => a.Timestamp < to.Value.Date.AddDays(1));
                return q.OrderByDescending(a => a.Timestamp).ToList();
            }
        }
    }
}
