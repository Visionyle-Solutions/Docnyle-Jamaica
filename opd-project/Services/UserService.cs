using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Runtime.Caching;
using System;
using OpdProject.Helpers;
using OpdProject.Models;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Services
{
    /// <summary>Authentication, user management and the role → feature permission matrix.</summary>
    public static class UserService
    {
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        /// <summary>The fixed role set (in-memory mode, and the order roles are normalised to). A LAB_PARTNER account must be linked to a lab partner.</summary>
        public static readonly string[] Roles = { "ADMIN", "DOCTOR", "RECEPTIONIST", "BILLING", "NURSE", "LAB_PARTNER" };

        public static readonly Dictionary<string, string> FeatureLabels = new Dictionary<string, string>
        {
            { "consultation", "Consultation" }, { "patients", "Patients" }, { "scheduling", "Scheduling" },
            { "billing", "Billing & Invoices" }, { "reports", "Reports" }, { "admin_hospital", "Hospital Profile" },
            { "admin_scheduling", "Schedule Settings" }, { "admin_vitals", "Vitals Config" }, { "admin_specialty", "Specialty Config" },
            { "admin_formulary", "Drug Formulary" }, { "admin_phrases", "Phrase Library" }, { "admin_users", "User Management" },
            { "audit_log", "Audit Log" }, { "data_export", "Data Export" }, { "data_import", "CSV Import" },
            { "print", "Print Documents" }, { "lab_portal", "Lab Portal" }, { "admin_consultation", "Consultation Setup" },
            { "admin_inventory", "Clinic Inventory" },
        };

        /// <summary>
        /// The product's default role → feature grid (the same grid the database seed creates). Used by the in-memory
        /// store, and in API mode only to drive menus for a user who may not read the hospital's matrix
        /// (GetPermissionMatrix requires admin_users) — the service still enforces the real matrix on every call.
        /// </summary>
        public static readonly string[] DefaultFeatures = { "consultation", "patients", "scheduling", "billing", "reports", "admin_hospital", "admin_scheduling",
            "admin_vitals", "admin_specialty", "admin_formulary", "admin_phrases", "admin_users", "audit_log", "data_export",
            "data_import", "print", "lab_portal", "admin_consultation", "admin_inventory" };

        public static readonly Dictionary<string, string[]> DefaultGrants = new Dictionary<string, string[]>
        {
            { "ADMIN", DefaultFeatures },
            { "DOCTOR", new[] { "consultation", "patients", "scheduling", "reports", "admin_scheduling", "admin_phrases", "admin_consultation", "print" } },
            { "RECEPTIONIST", new[] { "patients", "scheduling", "billing" } },
            { "BILLING", new[] { "patients", "billing", "reports", "print" } },
            { "NURSE", new[] { "patients", "scheduling" } },
            { "LAB_PARTNER", new[] { "lab_portal" } },
        };

        private static bool Api { get { return DataSource.IsApi("Users"); } }

        /* ── Sign-in and session ── */

        /// <summary>
        /// Verifies e-mail + password. API mode: AuthenticateUser, then CreateUserSession with a new 256-bit token;
        /// the returned user carries the token in <paramref name="serviceToken"/>. Returns null when the credentials
        /// do not match; throws ApiException for a lockout or an unreachable service.
        /// </summary>
        public static AppUser Authenticate(string email, string password, out string serviceToken)
        {
            serviceToken = null;
            if (Api)
            {
                SEC_AuthenticateUserResp auth;
                try
                {
                    var signInEmail = (email ?? "").Trim();
                    auth = cnHelperObj.AuthenticateUser(new SEC_AuthenticateUserReq { Email = signInEmail, Password = password, hashValue = CommonHelper.GetSHA265(signInEmail) }).EnsureSuccess();
                }
                catch (ApiException ex) when (ex.Kind != ApiFaultKind.Network && (ex.Message ?? "").ToUpperInvariant().Contains("INVALID EMAIL OR PASSWORD"))
                {
                    return null;
                }
                var a = auth.AuthenticatedUser;
                if (a == null || !a.IsActive) return null;

                var token = DocNyle.NewSessionToken();
                var ctx = System.Web.HttpContext.Current;
                var browser = ctx != null && ctx.Request.Browser != null ? (ctx.Request.Browser.Browser + " " + ctx.Request.Browser.Version) : null;
                cnHelperObj.CreateUserSession(new SEC_CreateUserSessionReq
                {
                    UserId = a.UserId, 
                    SessionToken = token,
                    Browser = browser != null && browser.Length > 100 ? browser.Substring(0, 100) : browser,
                    IpAddress = DocNyle.ClientIp(),
                    ExpiresAt = DocNyle.Instant(DateTime.UtcNow.AddHours(SessionHours)),
                    hashValue = CommonHelper.GetSHA265(a.UserId),
                }).EnsureSuccess();
                serviceToken = token;
                return new AppUser
                {
                    UserId = a.UserId, HospitalId = a.HospitalId, FullName = a.FullName, Email = a.Email, Phone = a.Phone,
                    Roles = NormalizeRoles(a.Roles) ?? (a.Roles ?? new List<string>()), SpecialtyId = a.SpecialtyId,
                    LicenseNumber = a.LicenseNumber, LabPartnerId = a.LabPartnerId, IsActive = a.IsActive,
                };
            }
            lock (DataStore.Sync)
            {
                var user = DataStore.Users.FirstOrDefault(u =>
                    string.Equals(u.Email, (email ?? "").Trim(), StringComparison.OrdinalIgnoreCase) && u.Password == password && u.IsActive);
                if (user == null) return null;
                return DataStore.Hospitals.Any(h => h.HospitalId == user.HospitalId) ? user : null;
            }
        }

        /// <summary>Lifetime of the service session (appSettings ApiSessionHours, default 12); idle timeout is the ASP.NET session's.</summary>
        private static double SessionHours
        {
            get
            {
                double h;
                return double.TryParse(ConfigurationManager.AppSettings["ApiSessionHours"], out h) && h > 0 ? h : 12;
            }
        }

        /// <summary>CheckSessionValid for the session's token, cached 60 seconds. A network failure keeps the session (the next call reports it).</summary>
        public static bool IsSessionValid()
        {
            var token = AppSession.ServiceToken;
            if (string.IsNullOrEmpty(token)) return false;
            var key = "session:" + CommonHelper.GetSHA265(token);
            var cached = MemoryCache.Default.Get(key);
            if (cached is bool) return (bool)cached;
            bool valid;
            try
            {
                var s = cnHelperObj.CheckSessionValid(CommonHelper.GetSHA265(AppSession.ServiceToken ?? "")).EnsureSuccess().SessionStatus;
                valid = s != null && s.Valid && (AppSession.ApiUser == null || s.UserId == null
                        || string.Equals(s.UserId, AppSession.ApiUser.UserId, StringComparison.OrdinalIgnoreCase));
            }
            catch (ApiException ex) when (ex.Kind == ApiFaultKind.Network)
            {
                return true;
            }
            catch (ApiException)
            {
                valid = false;
            }
            MemoryCache.Default.Set(key, valid, DateTimeOffset.UtcNow.AddSeconds(60));
            return valid;
        }

        /* ── Users ── */

        private static AppUser ToModel(SEC_UserInfo u)
        {
            return new AppUser
            {
                UserId = u.UserId, HospitalId = AppSession.ApiUser != null ? AppSession.ApiUser.HospitalId : null, FullName = u.FullName,
                Email = u.Email, Phone = u.Phone, Roles = NormalizeRoles(u.Roles) ?? (u.Roles ?? new List<string>()),
                SpecialtyId = u.SpecialtyId, LicenseNumber = u.LicenseNumber, LabPartnerId = u.LabPartnerId, IsActive = u.IsActive,
            };
        }

        /// <summary>
        /// API mode: the hospital's users. ListUsers needs admin_users, so for other callers the directory is the
        /// doctors from ListDoctorProfiles (feature scheduling) plus the signed-in user. Cached per hospital.
        /// </summary>
        private static List<AppUser> Directory()
        {
            return HospitalCache.Get("users", () =>
            {
                var me = AppSession.ApiUser;
                if (me != null && HasPermission(me, "admin_users"))
                {
                    try { return cnHelperObj.ListUsers(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Users.Select(ToModel).ToList(); }
                    catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden) { }
                }
                var list = new List<AppUser>();
                if (me != null && HasPermission(me, "scheduling"))
                {
                    try { list.AddRange(SchedulingService.ApiDoctorProfiles().Select(SchedulingService.DoctorFromProfile)); }
                    catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden) { }
                }
                if (me != null && list.All(u => u.UserId != me.UserId)) list.Add(me);
                return list;
            });
        }

        public static AppUser GetUser(string userId)
        {
            if (Api)
            {
                if (string.IsNullOrEmpty(userId)) return null;
                var me = AppSession.ApiUser;
                var hit = Directory().FirstOrDefault(u => string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase));
                if (hit != null && me != null && hit.UserId == me.UserId && hit.Roles.Count == 0) return me;
                return hit ?? (me != null && me.UserId == userId ? me : null);
            }
            lock (DataStore.Sync) return DataStore.Users.FirstOrDefault(u => u.UserId == userId);
        }

        public static List<AppUser> ListUsers(string hospitalId)
        {
            if (Api) return cnHelperObj.ListUsers(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Users.Select(ToModel).ToList();
            lock (DataStore.Sync) return DataStore.Users.Where(u => u.HospitalId == hospitalId).ToList();
        }

        public static List<AppUser> ListActiveDoctors(string hospitalId)
        {
            if (Api)
            {
                // No session (patient portal): the portal hospital's doctors from GetPortalBookingOptions.
                if (AppSession.ApiUser == null)
                    return (SchedulingService.PortalOptions().Doctors ?? new List<SCH_PortalDoctorInfo>())
                        .Select(d => new AppUser { UserId = d.DoctorId, FullName = d.DisplayName, Role = "DOCTOR", IsActive = true }).ToList();
                return SchedulingService.ApiDoctorProfiles().Select(SchedulingService.DoctorFromProfile).ToList();
            }
            lock (DataStore.Sync) return DataStore.Users.Where(u => u.HospitalId == hospitalId && u.HasRole("DOCTOR") && u.IsActive).ToList();
        }

        public static string UserName(string userId)
        {
            var u = GetUser(userId);
            return u != null ? u.FullName : (Api ? "—" : userId);
        }

        /// <summary>In API mode the service enforces the unique e-mail (CreateUser fault), so the pre-check passes.</summary>
        public static bool EmailInUse(string email, string exceptUserId)
        {
            if (Api) return false;
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

        /// <summary>labPartnerId is kept only for LAB_PARTNER accounts; specialty only for clinic roles. A password is required.</summary>
        public static AppUser CreateUser(string hospitalId, string fullName, string email, IEnumerable<string> roles, string specialtyId, string password, string labPartnerId)
        {
            var list = NormalizeRoles(roles);
            if (list == null) throw new ArgumentException("Invalid role combination", "roles");
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("A password is required", "password");
            bool lab = list.Contains("LAB_PARTNER");
            if (Api)
            {
                var created = cnHelperObj.CreateUser(new SEC_CreateUserReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    FullName = fullName, Email = email, Password = password, Roles = list,
                    SpecialtyId = lab || string.IsNullOrWhiteSpace(specialtyId) ? null : specialtyId,
                    LabPartnerId = lab ? labPartnerId : null,
                }).EnsureSuccess().User;
                HospitalCache.Clear("users");
                HospitalCache.Clear("doctors");
                return ToModel(created);
            }
            var user = new AppUser
            {
                UserId = DataStore.NextId("USR"), HospitalId = hospitalId, FullName = fullName, Email = email, Phone = "",
                Roles = list, SpecialtyId = lab || string.IsNullOrWhiteSpace(specialtyId) ? null : specialtyId,
                LabPartnerId = lab ? labPartnerId : null,
                Password = password, IsActive = true
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

        /// <summary>API mode: UpdateUser changes name, roles and specialty; the e-mail and lab partner of an account cannot be changed there.</summary>
        public static void UpdateUser(string userId, string fullName, string email, IEnumerable<string> roles, string specialtyId, string labPartnerId)
        {
            var list = NormalizeRoles(roles);
            if (list == null) throw new ArgumentException("Invalid role combination", "roles");
            bool lab = list.Contains("LAB_PARTNER");
            if (Api)
            {
                cnHelperObj.UpdateUser(new SEC_UpdateUserReq
                {
                    HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue,
                    TargetUserId = userId, FullName = fullName, Roles = list,
                    SpecialtyId = lab || string.IsNullOrWhiteSpace(specialtyId) ? null : specialtyId,
                }).EnsureSuccess();
                HospitalCache.Clear("users");
                HospitalCache.Clear("doctors");
                return;
            }
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
            if (Api)
            {
                var state = cnHelperObj.UpdateUserActive(new SEC_UpdateUserActiveReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, TargetUserId = userId }).EnsureSuccess().UserActiveState;
                HospitalCache.Clear("users");
                HospitalCache.Clear("doctors");
                var u = GetUser(userId);
                if (u != null && state != null) u.IsActive = state.IsActive;
                return u;
            }
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
            if (Api)
            {
                Dictionary<string, bool> row;
                bool allowed;
                return role != null && ApiGrid().TryGetValue(role, out row) && row.TryGetValue(feature, out allowed) && allowed;
            }
            lock (DataStore.Sync)
            {
                Dictionary<string, bool> perms;
                bool allowed;
                return role != null && DataStore.Permissions.TryGetValue(role, out perms) && perms.TryGetValue(feature, out allowed) && allowed;
            }
        }

        /// <summary>
        /// The hospital's role → feature grid from GetPermissionMatrix (cached per hospital, 5 minutes). A caller
        /// without admin_users cannot read it; for them the product default grid drives the menus.
        /// </summary>
        private static Dictionary<string, Dictionary<string, bool>> ApiGrid()
        {
            return RequestCache.Get("perm-grid", () =>
            {
                var hospitalGrid = HospitalCache.Get("perm-grid", () =>
                {
                    var me = AppSession.ApiUser;
                    if (me == null || !me.Roles.Any(r => DefaultGrants.ContainsKey(r) && DefaultGrants[r].Contains("admin_users"))) return null;
                    try { return Pivot(cnHelperObj.GetPermissionMatrix(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().PermissionGrants); }
                    catch (ApiException ex) when (ex.Kind == ApiFaultKind.Forbidden || ex.Kind == ApiFaultKind.Network) { return null; }
                });
                return hospitalGrid ?? DefaultGrants.ToDictionary(g => g.Key, g => DefaultFeatures.ToDictionary(f => f, f => g.Value.Contains(f)));
            });
        }

        private static Dictionary<string, Dictionary<string, bool>> Pivot(IEnumerable<SEC_PermissionGrantInfo> grants)
        {
            var grid = new Dictionary<string, Dictionary<string, bool>>();
            foreach (var g in grants ?? Enumerable.Empty<SEC_PermissionGrantInfo>())
            {
                Dictionary<string, bool> row;
                if (!grid.TryGetValue(g.Role, out row)) grid[g.Role] = row = new Dictionary<string, bool>();
                row[g.Feature] = g.IsGranted;
            }
            return grid;
        }

        public static List<string> ListFeatures()
        {
            if (Api) return HospitalCache.Get("features", () => cnHelperObj.ListPermissionFeatures(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Values ?? new List<string>());
            lock (DataStore.Sync) return DataStore.Permissions["ADMIN"].Keys.ToList();
        }

        /// <summary>Roles shown in the permission matrix and offered in User Management: ListRoles in API mode (cached), else <see cref="Roles"/>.</summary>
        public static List<string> ListRoles()
        {
            if (Api) return HospitalCache.Get("roles", () => cnHelperObj.ListRoles(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().Values ?? new List<string>());
            return Roles.ToList();
        }

        public static List<PermissionRow> GetPermissionMatrix()
        {
            if (Api)
            {
                var grid = Pivot(cnHelperObj.GetPermissionMatrix(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess().PermissionGrants);
                var roles = ListRoles();
                return ListFeatures().Select(f => new PermissionRow
                {
                    Feature = f,
                    Label = FeatureLabels.ContainsKey(f) ? FeatureLabels[f] : f,
                    Access = roles.ToDictionary(r => r, r => grid.ContainsKey(r) && grid[r].ContainsKey(f) && grid[r][f]),
                }).ToList();
            }
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
            if (Api)
            {
                var row = cnHelperObj.UpdatePermission(new SEC_UpdatePermissionReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, Role = role, Feature = feature, IsGranted = null }).EnsureSuccess().PermissionGrants;
                HospitalCache.Clear("perm-grid");
                RequestCache.Remove("perm-grid");
                var cell = (row ?? new List<SEC_PermissionGrantInfo>()).FirstOrDefault(g => g.Role == role && g.Feature == feature);
                return cell != null && cell.IsGranted;
            }
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
        private static readonly CommonHelper cnHelperObj = new CommonHelper();

        /// <summary>
        /// Page events that the API operation behind them already audits (API_Jamaica_Consult360.xlsx, "Audited:"),
        /// as ACTION:EntityType of the page's own Audit call. Recording them again would double the audit trail.
        /// </summary>
        private static readonly HashSet<string> AuditedByService = new HashSet<string>
        {
            "LOGIN:Session", "CREATE:User", "UPDATE:User", "UPDATE:Permission", "UPDATE:Hospital", "UPDATE:HospitalCompliance",
            "PUBLISH:ConsultationConfig", "CREATE:Patient", "UPDATE:Patient", "UPDATE:PatientAllergy", "UPDATE:PatientMedicalHistory",
            "CREATE:PatientMedicalEvent", "UPDATE:PatientMedicalEvent", "DELETE:PatientMedicalEvent", "CREATE:PatientFamilyHistory",
            "UPDATE:PatientFamilyHistory", "DELETE:PatientFamilyHistory", "CREATE:PatientConsentDocument", "VIEW:PatientConsentDocument",
            "CREATE:Payer", "UPDATE:Payer", "CREATE:DoctorProfile", "UPDATE:DoctorProfile", "CREATE:ConsentRecord", "SIGN_OFF:Consultation",
            "CREATE:Prescription", "UPDATE:Drug", "CREATE:LabResult", "UPDATE:LabResult", "CREATE:LabResultAttachment",
            "VIEW:LabResultAttachment", "UPDATE:ServiceCharge", "DELETE:Invoice", "UPDATE:Payment", "UPDATE:NotificationConfig",
            "EXPORT:DataExport", "IMPORT:CsvImport",
        };

        public static void Record(string hospitalId, string userId, string action, string entityType, string entityId, string details = null)
        {
            if (DataSource.IsApi("Users"))
            {
                if (AuditedByService.Contains(action + ":" + entityType)) return;
                // Screen-level events only; operations marked "Audited" in the API write their own entry.
                try
                {
                    cnHelperObj.RecordAuditEntry(new SEC_RecordAuditEntryReq { HospitalId = AppSession.ApiHospitalId, UserId = AppSession.ApiUserId, hashValue = AppSession.ApiHashValue, Action = action, EntityType = entityType, EntityId = entityId, Details = details }).EnsureSuccess();
                }
                catch (ApiException ex)
                {
                    System.Diagnostics.Trace.TraceWarning("RecordAuditEntry failed: " + ex.Message);
                }
                return;
            }
            var row = new AuditLogEntry
            {
                AuditLogId = DataStore.NextId("AUD"), HospitalId = hospitalId, UserId = userId, Action = action,
                EntityType = entityType, EntityId = entityId, Details = details, Timestamp = DateTime.UtcNow
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
            if (DataSource.IsApi("Operations"))
            {
                var resp = string.IsNullOrEmpty(action) && string.IsNullOrEmpty(entityType) && !from.HasValue && !to.HasValue
                    ? cnHelperObj.ListAuditLogs(AppSession.ApiHospitalId, AppSession.ApiUserId, AppSession.ApiHashValue).EnsureSuccess()
                    : cnHelperObj.ListAuditLogsFiltered(AppSession.ApiHospitalId, AppSession.ApiUserId, NullIfEmpty(action), NullIfEmpty(entityType), null, DocNyle.Date(from), DocNyle.Date(to), null, AppSession.ApiHashValue).EnsureSuccess();
                return (resp.AuditEntries ?? new List<SEC_AuditEntryInfo>()).Select(a => new AuditLogEntry
                {
                    AuditLogId = a.AuditLogId, HospitalId = hospitalId, UserId = a.UserId, Action = a.Action, EntityType = a.EntityType,
                    EntityId = a.EntityId, Details = a.Details, IpAddress = a.IpAddress, Timestamp = DocNyle.InstantOrMin(a.Timestamp),
                }).OrderByDescending(a => a.Timestamp).ToList();
            }
            lock (DataStore.Sync)
            {
                IEnumerable<AuditLogEntry> q = DataStore.AuditLogs.Where(a => a.HospitalId == hospitalId);
                if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action == action);
                if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
                // from / to are hospital-local dates; Timestamp is a UTC instant.
                var zone = TimeZoneService.HospitalZoneId(hospitalId);
                if (from.HasValue) q = q.Where(a => a.Timestamp >= TimeZoneService.ToUtc(from.Value.Date, zone));
                if (to.HasValue) q = q.Where(a => a.Timestamp < TimeZoneService.ToUtc(to.Value.Date.AddDays(1), zone));
                return q.OrderByDescending(a => a.Timestamp).ToList();
            }
        }

        private static string NullIfEmpty(string s) { return string.IsNullOrEmpty(s) ? null : s; }
    }
}
