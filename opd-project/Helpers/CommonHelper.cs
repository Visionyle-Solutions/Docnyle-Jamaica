using Newtonsoft.Json;
using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using OpdProject.Services.Api;
using OpdProject.Web;

namespace OpdProject.Helpers
{
    /// <summary>
    /// The one place the DOCNyle web API is called (API_Jamaica_Consult360.xlsx) — one method per operation, grouped by module.
    /// Callers pass HospitalId / UserId / hashValue (GET) or set them on the request (POST), as the reference project does;
    /// the SessionToken and vendor headers are added here. Each method returns the service's response (the caller checks
    /// Status with EnsureSuccess); a call that gets no usable answer shows a toast and returns an empty response (Status 0).
    /// </summary>
    public class CommonHelper
    {
        string SERVICE_URL = (ConfigurationManager.AppSettings["SERVICE_URL"] ?? "").TrimEnd('/') + "/";
        private static readonly string VendorId = ConfigurationManager.AppSettings["VendorId"] ?? "";
        private static readonly string VendorPwd = ConfigurationManager.AppSettings["VendorPwd"] ?? "";

        /// <summary>Request timeout in milliseconds (appSettings ApiTimeoutSeconds, default 30 s).</summary>
        private static readonly int TimeoutMs = 1000 * ParseInt(ConfigurationManager.AppSettings["ApiTimeoutSeconds"], 30);

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
        };

        #region HELPER_METHODS
        /// <summary>Lower-case hex SHA-256 of the text — the API's hashValue.</summary>
        public static string GetSHA265(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ServicePointManager is process-wide, so it is configured once. Certificate validation is left on.
        private static readonly object _spmLock = new object();
        private static bool _spmConfigured;

        private static void EnsureServicePointConfigured()
        {
            if (_spmConfigured) return;
            lock (_spmLock)
            {
                if (_spmConfigured) return;
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                _spmConfigured = true;
            }
        }

        private T _download_serialized_json_data<T>(string url) where T : RespInfo, new()
        {
            return Send<T>("GET", url, null);
        }

        private T _serialized_json_data<T>(string url, string indata) where T : RespInfo, new()
        {
            return Send<T>("POST", url, indata ?? string.Empty);
        }

        /// <summary>"&amp;name=value" with the value URL-encoded, or "" when the value is null (the parameter is left out).</summary>
        private static string QueryParam(string name, string value)
        {
            return value == null ? "" : "&" + name + "=" + Uri.EscapeDataString(value);
        }

        /// <summary>
        /// Sends one request and returns the service's response as it came back — the caller checks Status
        /// (EnsureSuccess). Only a transport failure (no answer, or no readable JSON) throws.
        /// </summary>
        private T Send<T>(string method, string url, string indata) where T : RespInfo, new()
        {
            string json_data;
            try
            {
                EnsureServicePointConfigured();

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = method;
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Timeout = TimeoutMs;
                request.ReadWriteTimeout = TimeoutMs;

                // Headers
                request.Headers["VenderId"] = VendorId;
                request.Headers["VenderPwd"] = VendorPwd;
                if (!string.IsNullOrEmpty(AppSession.ServiceToken)) request.Headers["SessionToken"] = AppSession.ServiceToken;

                if (indata != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(indata);
                    request.ContentLength = data.Length;
                    using (var stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json_data = reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                // An HTTP error that still carries the service's JSON envelope is read like a normal response.
                json_data = ErrorBody(ex);
                System.Diagnostics.Debug.WriteLine($"API {method} {url} failed: {ex.Message}");
                if (json_data == null)
                {
                    var http = ex.Response as HttpWebResponse;
                    var detail = http != null ? "HTTP " + (int)http.StatusCode : ex.Status.ToString();
                    throw new WebException("The service could not be reached (" + detail + ").", ex);
                }
            }

            try
            {
                return !string.IsNullOrWhiteSpace(json_data) ? JsonConvert.DeserializeObject<T>(json_data, JsonSettings) ?? new T() : new T();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("The service returned an unreadable response.", ex);
            }
        }

        private static string ErrorBody(WebException ex)
        {
            var resp = ex.Response as HttpWebResponse;
            if (resp == null) return null;
            using (var s = resp.GetResponseStream())
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    var text = r.ReadToEnd();
                    return !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("{") ? text : null;
                }
            }
        }

        private static int ParseInt(string value, int fallback)
        {
            int i;
            return int.TryParse(value, out i) && i > 0 ? i : fallback;
        }
        #endregion

        #region Auth Methods
        public SEC_AuthenticateUserResp AuthenticateUser(SEC_AuthenticateUserReq authenticateUserReq)
        {
            SEC_AuthenticateUserResp resp = new SEC_AuthenticateUserResp();
            try
            {
                string data = JsonConvert.SerializeObject(authenticateUserReq, JsonSettings);
                resp = _serialized_json_data<SEC_AuthenticateUserResp>(SERVICE_URL + "AuthenticateUser", data);
            }
            catch (Exception ex) {
                AppSession.Toast("danger", "Failed to Authenticate", ex.Message);
            
            }
            return resp;
        }

        public SEC_UserSessionResp CreateUserSession(SEC_CreateUserSessionReq createUserSessionReq)
        {
            SEC_UserSessionResp resp = new SEC_UserSessionResp();
            try
            {
                string data = JsonConvert.SerializeObject(createUserSessionReq, JsonSettings);
                resp = _serialized_json_data<SEC_UserSessionResp>(SERVICE_URL + "CreateUserSession", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create User Session", ex.Message);
            }
            return resp;
        }

        public SEC_SessionStatusResp CheckSessionValid(string hashValue)
        {
            SEC_SessionStatusResp resp = new SEC_SessionStatusResp();
            try
            {
                resp = _download_serialized_json_data<SEC_SessionStatusResp>(SERVICE_URL + "CheckSessionValid?hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Check Session Valid", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Users & Permissions Methods
        public SEC_UserListResp ListUsers(string hospitalId, string userId, string hashValue)
        {
            SEC_UserListResp resp = new SEC_UserListResp();
            try
            {
                resp = _download_serialized_json_data<SEC_UserListResp>(SERVICE_URL + "ListUsers?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Users", ex.Message);
            }
            return resp;
        }

        public SEC_UserResp CreateUser(SEC_CreateUserReq createUserReq)
        {
            SEC_UserResp resp = new SEC_UserResp();
            try
            {
                string data = JsonConvert.SerializeObject(createUserReq, JsonSettings);
                resp = _serialized_json_data<SEC_UserResp>(SERVICE_URL + "CreateUser", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create User", ex.Message);
            }
            return resp;
        }

        public SEC_UserResp UpdateUser(SEC_UpdateUserReq updateUserReq)
        {
            SEC_UserResp resp = new SEC_UserResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateUserReq, JsonSettings);
                resp = _serialized_json_data<SEC_UserResp>(SERVICE_URL + "UpdateUser", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update User", ex.Message);
            }
            return resp;
        }

        public SEC_UserActiveStateResp UpdateUserActive(SEC_UpdateUserActiveReq updateUserActiveReq)
        {
            SEC_UserActiveStateResp resp = new SEC_UserActiveStateResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateUserActiveReq, JsonSettings);
                resp = _serialized_json_data<SEC_UserActiveStateResp>(SERVICE_URL + "UpdateUserActive", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update User Active", ex.Message);
            }
            return resp;
        }

        public SEC_PermissionMatrixResp GetPermissionMatrix(string hospitalId, string userId, string hashValue)
        {
            SEC_PermissionMatrixResp resp = new SEC_PermissionMatrixResp();
            try
            {
                resp = _download_serialized_json_data<SEC_PermissionMatrixResp>(SERVICE_URL + "GetPermissionMatrix?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Permission Matrix", ex.Message);
            }
            return resp;
        }

        public StringListResp ListRoles(string hospitalId, string userId, string hashValue)
        {
            StringListResp resp = new StringListResp();
            try
            {
                resp = _download_serialized_json_data<StringListResp>(SERVICE_URL + "ListRoles?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Roles", ex.Message);
            }
            return resp;
        }

        public StringListResp ListPermissionFeatures(string hospitalId, string userId, string hashValue)
        {
            StringListResp resp = new StringListResp();
            try
            {
                resp = _download_serialized_json_data<StringListResp>(SERVICE_URL + "ListPermissionFeatures?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Permission Features", ex.Message);
            }
            return resp;
        }

        public SEC_PermissionMatrixResp UpdatePermission(SEC_UpdatePermissionReq updatePermissionReq)
        {
            SEC_PermissionMatrixResp resp = new SEC_PermissionMatrixResp();
            try
            {
                string data = JsonConvert.SerializeObject(updatePermissionReq, JsonSettings);
                resp = _serialized_json_data<SEC_PermissionMatrixResp>(SERVICE_URL + "UpdatePermission", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Permission", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Hospital & Configuration Methods
        public CFG_HospitalResp GetHospital(string hospitalId, string userId, string hashValue)
        {
            CFG_HospitalResp resp = new CFG_HospitalResp();
            try
            {
                resp = _download_serialized_json_data<CFG_HospitalResp>(SERVICE_URL + "GetHospital?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Hospital", ex.Message);
            }
            return resp;
        }

        public CFG_HospitalResp UpdateHospital(CFG_UpdateHospitalReq updateHospitalReq)
        {
            CFG_HospitalResp resp = new CFG_HospitalResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateHospitalReq, JsonSettings);
                resp = _serialized_json_data<CFG_HospitalResp>(SERVICE_URL + "UpdateHospital", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Hospital", ex.Message);
            }
            return resp;
        }

        public CFG_ListTimeZonesResp ListTimeZones(string hospitalId, string userId, string hashValue)
        {
            CFG_ListTimeZonesResp resp = new CFG_ListTimeZonesResp();
            try
            {
                resp = _download_serialized_json_data<CFG_ListTimeZonesResp>(SERVICE_URL + "ListTimeZones?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Time Zones", ex.Message);
            }
            return resp;
        }

        public CFG_HospitalSettingsResp GetHospitalSettings(string hospitalId, string userId, string hashValue)
        {
            CFG_HospitalSettingsResp resp = new CFG_HospitalSettingsResp();
            try
            {
                resp = _download_serialized_json_data<CFG_HospitalSettingsResp>(SERVICE_URL + "GetHospitalSettings?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Hospital Settings", ex.Message);
            }
            return resp;
        }

        public CFG_HospitalSettingsResp SaveHospitalSettings(CFG_SaveHospitalSettingsReq saveHospitalSettingsReq)
        {
            CFG_HospitalSettingsResp resp = new CFG_HospitalSettingsResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveHospitalSettingsReq, JsonSettings);
                resp = _serialized_json_data<CFG_HospitalSettingsResp>(SERVICE_URL + "SaveHospitalSettings", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Hospital Settings", ex.Message);
            }
            return resp;
        }

        public CFG_PaymentMethodListResp ListPaymentMethods(string hospitalId, string userId, string hashValue)
        {
            CFG_PaymentMethodListResp resp = new CFG_PaymentMethodListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_PaymentMethodListResp>(SERVICE_URL + "ListPaymentMethods?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Payment Methods", ex.Message);
            }
            return resp;
        }

        public CFG_SpecialtyListResp ListSpecialties(string hospitalId, string userId, string hashValue)
        {
            CFG_SpecialtyListResp resp = new CFG_SpecialtyListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_SpecialtyListResp>(SERVICE_URL + "ListSpecialties?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Specialties", ex.Message);
            }
            return resp;
        }

        public CFG_SpecialtyListResp ListSpecialtyConfig(string hospitalId, string userId, string hashValue)
        {
            CFG_SpecialtyListResp resp = new CFG_SpecialtyListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_SpecialtyListResp>(SERVICE_URL + "ListSpecialtyConfig?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Specialty Config", ex.Message);
            }
            return resp;
        }

        public CFG_SpecialtyResp CreateSpecialty(CFG_CreateSpecialtyReq createSpecialtyReq)
        {
            CFG_SpecialtyResp resp = new CFG_SpecialtyResp();
            try
            {
                string data = JsonConvert.SerializeObject(createSpecialtyReq, JsonSettings);
                resp = _serialized_json_data<CFG_SpecialtyResp>(SERVICE_URL + "CreateSpecialty", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Specialty", ex.Message);
            }
            return resp;
        }

        public CFG_SpecialtyResp ToggleSpecialty(CFG_ToggleSpecialtyReq toggleSpecialtyReq)
        {
            CFG_SpecialtyResp resp = new CFG_SpecialtyResp();
            try
            {
                string data = JsonConvert.SerializeObject(toggleSpecialtyReq, JsonSettings);
                resp = _serialized_json_data<CFG_SpecialtyResp>(SERVICE_URL + "ToggleSpecialty", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Toggle Specialty", ex.Message);
            }
            return resp;
        }

        public CFG_MedicalHistoryItemListResp ListMedicalHistoryItems(string hospitalId, string userId, string hashValue)
        {
            CFG_MedicalHistoryItemListResp resp = new CFG_MedicalHistoryItemListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_MedicalHistoryItemListResp>(SERVICE_URL + "ListMedicalHistoryItems?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Medical History Items", ex.Message);
            }
            return resp;
        }

        public CFG_MedicalHistoryItemListResp ListMedicalHistoryConfig(string hospitalId, string userId, string hashValue)
        {
            CFG_MedicalHistoryItemListResp resp = new CFG_MedicalHistoryItemListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_MedicalHistoryItemListResp>(SERVICE_URL + "ListMedicalHistoryConfig?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Medical History Config", ex.Message);
            }
            return resp;
        }

        public CFG_MedicalHistoryItemResp CreateMedicalHistoryConfigItem(CFG_CreateMedicalHistoryConfigItemReq createMedicalHistoryConfigItemReq)
        {
            CFG_MedicalHistoryItemResp resp = new CFG_MedicalHistoryItemResp();
            try
            {
                string data = JsonConvert.SerializeObject(createMedicalHistoryConfigItemReq, JsonSettings);
                resp = _serialized_json_data<CFG_MedicalHistoryItemResp>(SERVICE_URL + "CreateMedicalHistoryConfigItem", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Medical History Config Item", ex.Message);
            }
            return resp;
        }

        public RespInfo UpdateMedicalHistoryItemStatus(CFG_UpdateMedicalHistoryItemStatusReq updateMedicalHistoryItemStatusReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(updateMedicalHistoryItemStatusReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "UpdateMedicalHistoryItemStatus", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Medical History Item Status", ex.Message);
            }
            return resp;
        }

        public CFG_MedicalEventTypeListResp ListMedicalEventItems(string hospitalId, string userId, string hashValue)
        {
            CFG_MedicalEventTypeListResp resp = new CFG_MedicalEventTypeListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_MedicalEventTypeListResp>(SERVICE_URL + "ListMedicalEventItems?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Medical Event Items", ex.Message);
            }
            return resp;
        }

        public CFG_VitalDefinitionListResp ListVitalsConfig(string hospitalId, string userId, string hashValue)
        {
            CFG_VitalDefinitionListResp resp = new CFG_VitalDefinitionListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_VitalDefinitionListResp>(SERVICE_URL + "ListVitalsConfig?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Vitals Config", ex.Message);
            }
            return resp;
        }

        public CFG_VitalDefinitionListResp ListEnabledVitalsConfig(string hospitalId, string userId, string hashValue)
        {
            CFG_VitalDefinitionListResp resp = new CFG_VitalDefinitionListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_VitalDefinitionListResp>(SERVICE_URL + "ListEnabledVitalsConfig?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Enabled Vitals Config", ex.Message);
            }
            return resp;
        }

        public RespInfo UpdateVitalEnabled(CFG_UpdateVitalEnabledReq updateVitalEnabledReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(updateVitalEnabledReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "UpdateVitalEnabled", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Vital Enabled", ex.Message);
            }
            return resp;
        }

        public CFG_VitalDefinitionResp CreateVitalConfig(CFG_CreateVitalConfigReq createVitalConfigReq)
        {
            CFG_VitalDefinitionResp resp = new CFG_VitalDefinitionResp();
            try
            {
                string data = JsonConvert.SerializeObject(createVitalConfigReq, JsonSettings);
                resp = _serialized_json_data<CFG_VitalDefinitionResp>(SERVICE_URL + "CreateVitalConfig", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Vital Config", ex.Message);
            }
            return resp;
        }

        public CFG_ConsultFormDocumentResp GetConsultationFormConfig(string hospitalId, string userId, string configState, string hashValue)
        {
            CFG_ConsultFormDocumentResp resp = new CFG_ConsultFormDocumentResp();
            try
            {
                resp = _download_serialized_json_data<CFG_ConsultFormDocumentResp>(SERVICE_URL + "GetConsultationFormConfig?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("configState", configState) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Consultation Form Config", ex.Message);
            }
            return resp;
        }

        public CFG_ConsultFormDocumentResp SaveConsultationFormDraft(CFG_SaveConsultationFormDraftReq saveConsultationFormDraftReq)
        {
            CFG_ConsultFormDocumentResp resp = new CFG_ConsultFormDocumentResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveConsultationFormDraftReq, JsonSettings);
                resp = _serialized_json_data<CFG_ConsultFormDocumentResp>(SERVICE_URL + "SaveConsultationFormDraft", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Consultation Form Draft", ex.Message);
            }
            return resp;
        }

        public CFG_FormPublishResultResp ConfirmConsultationFormPublish(CFG_ConfirmConsultationFormPublishReq confirmConsultationFormPublishReq)
        {
            CFG_FormPublishResultResp resp = new CFG_FormPublishResultResp();
            try
            {
                string data = JsonConvert.SerializeObject(confirmConsultationFormPublishReq, JsonSettings);
                resp = _serialized_json_data<CFG_FormPublishResultResp>(SERVICE_URL + "ConfirmConsultationFormPublish", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Confirm Consultation Form Publish", ex.Message);
            }
            return resp;
        }

        public RespInfo CancelConsultationFormDraft(CFG_CancelConsultationFormDraftReq cancelConsultationFormDraftReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(cancelConsultationFormDraftReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "CancelConsultationFormDraft", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Cancel Consultation Form Draft", ex.Message);
            }
            return resp;
        }

        public CFG_ComplianceListResp ListCompliances(string hospitalId, string userId, string country, string hashValue)
        {
            CFG_ComplianceListResp resp = new CFG_ComplianceListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_ComplianceListResp>(SERVICE_URL + "ListCompliances?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("country", country) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Compliances", ex.Message);
            }
            return resp;
        }

        public CFG_HospitalComplianceListResp ListHospitalCompliances(string hospitalId, string userId, string hashValue)
        {
            CFG_HospitalComplianceListResp resp = new CFG_HospitalComplianceListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_HospitalComplianceListResp>(SERVICE_URL + "ListHospitalCompliances?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Hospital Compliances", ex.Message);
            }
            return resp;
        }

        public CFG_HospitalComplianceResp SaveHospitalCompliance(CFG_SaveHospitalComplianceReq saveHospitalComplianceReq)
        {
            CFG_HospitalComplianceResp resp = new CFG_HospitalComplianceResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveHospitalComplianceReq, JsonSettings);
                resp = _serialized_json_data<CFG_HospitalComplianceResp>(SERVICE_URL + "SaveHospitalCompliance", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Hospital Compliance", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Patient Methods
        public PAT_PatientListResp SearchPatients(string hospitalId, string userId, string term, string hashValue)
        {
            PAT_PatientListResp resp = new PAT_PatientListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_PatientListResp>(SERVICE_URL + "SearchPatients?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("term", term) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Search Patients", ex.Message);
            }
            return resp;
        }

        public PAT_PatientResp GetPatient(string hospitalId, string userId, string patientId, string hashValue)
        {
            PAT_PatientResp resp = new PAT_PatientResp();
            try
            {
                resp = _download_serialized_json_data<PAT_PatientResp>(SERVICE_URL + "GetPatient?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Patient", ex.Message);
            }
            return resp;
        }

        public PAT_CreatedPatientResp CreatePatient(PAT_CreatePatientReq createPatientReq)
        {
            PAT_CreatedPatientResp resp = new PAT_CreatedPatientResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPatientReq, JsonSettings);
                resp = _serialized_json_data<PAT_CreatedPatientResp>(SERVICE_URL + "CreatePatient", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Patient", ex.Message);
            }
            return resp;
        }

        public PAT_PatientResp UpdatePatient(PAT_UpdatePatientReq updatePatientReq)
        {
            PAT_PatientResp resp = new PAT_PatientResp();
            try
            {
                string data = JsonConvert.SerializeObject(updatePatientReq, JsonSettings);
                resp = _serialized_json_data<PAT_PatientResp>(SERVICE_URL + "UpdatePatient", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Patient", ex.Message);
            }
            return resp;
        }

        public PAT_PatientDuplicateResp CheckDuplicatePatient(string hospitalId, string userId, string patientId, string nationalHealthId, string trn, string mobile, string hashValue)
        {
            PAT_PatientDuplicateResp resp = new PAT_PatientDuplicateResp();
            try
            {
                resp = _download_serialized_json_data<PAT_PatientDuplicateResp>(SERVICE_URL + "CheckDuplicatePatient?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + QueryParam("nationalHealthId", nationalHealthId) + QueryParam("trn", trn) + QueryParam("mobile", mobile) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Check Duplicate Patient", ex.Message);
            }
            return resp;
        }

        public PAT_SocialHistoryResp UpdatePatientSocialHistory(PAT_UpdatePatientSocialHistoryReq updatePatientSocialHistoryReq)
        {
            PAT_SocialHistoryResp resp = new PAT_SocialHistoryResp();
            try
            {
                string data = JsonConvert.SerializeObject(updatePatientSocialHistoryReq, JsonSettings);
                resp = _serialized_json_data<PAT_SocialHistoryResp>(SERVICE_URL + "UpdatePatientSocialHistory", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Patient Social History", ex.Message);
            }
            return resp;
        }

        public PAT_CreatedPatientListResp CreatePatientsBatch(PAT_CreatePatientsBatchReq createPatientsBatchReq)
        {
            PAT_CreatedPatientListResp resp = new PAT_CreatedPatientListResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPatientsBatchReq, JsonSettings);
                resp = _serialized_json_data<PAT_CreatedPatientListResp>(SERVICE_URL + "CreatePatientsBatch", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Patients Batch", ex.Message);
            }
            return resp;
        }

        public PAT_AllergyListResp ListAllergies(string hospitalId, string userId, string patientId, string hashValue)
        {
            PAT_AllergyListResp resp = new PAT_AllergyListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_AllergyListResp>(SERVICE_URL + "ListAllergies?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Allergies", ex.Message);
            }
            return resp;
        }

        public PAT_AllergyResp CreateAllergy(PAT_CreateAllergyReq createAllergyReq)
        {
            PAT_AllergyResp resp = new PAT_AllergyResp();
            try
            {
                string data = JsonConvert.SerializeObject(createAllergyReq, JsonSettings);
                resp = _serialized_json_data<PAT_AllergyResp>(SERVICE_URL + "CreateAllergy", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Allergy", ex.Message);
            }
            return resp;
        }

        public PAT_AllergyResp UpdateAllergy(PAT_UpdateAllergyReq updateAllergyReq)
        {
            PAT_AllergyResp resp = new PAT_AllergyResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateAllergyReq, JsonSettings);
                resp = _serialized_json_data<PAT_AllergyResp>(SERVICE_URL + "UpdateAllergy", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Allergy", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteAllergy(PAT_DeleteAllergyReq deleteAllergyReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteAllergyReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteAllergy", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Allergy", ex.Message);
            }
            return resp;
        }

        public PAT_MedicalHistoryListResp ListPatientMedicalHistory(string hospitalId, string userId, string patientId, string hashValue)
        {
            PAT_MedicalHistoryListResp resp = new PAT_MedicalHistoryListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_MedicalHistoryListResp>(SERVICE_URL + "ListPatientMedicalHistory?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Patient Medical History", ex.Message);
            }
            return resp;
        }

        public PAT_MedicalHistoryListResp GetPatientMedicalHistory(string hospitalId, string userId, string patientId, string hashValue)
        {
            PAT_MedicalHistoryListResp resp = new PAT_MedicalHistoryListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_MedicalHistoryListResp>(SERVICE_URL + "GetPatientMedicalHistory?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Patient Medical History", ex.Message);
            }
            return resp;
        }

        public PAT_MedicalHistoryResp CreatePatientMedicalHistory(PAT_CreatePatientMedicalHistoryReq createPatientMedicalHistoryReq)
        {
            PAT_MedicalHistoryResp resp = new PAT_MedicalHistoryResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPatientMedicalHistoryReq, JsonSettings);
                resp = _serialized_json_data<PAT_MedicalHistoryResp>(SERVICE_URL + "CreatePatientMedicalHistory", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Patient Medical History", ex.Message);
            }
            return resp;
        }

        public PAT_MedicalHistoryResp SavePatientMedicalHistory(PAT_SavePatientMedicalHistoryReq savePatientMedicalHistoryReq)
        {
            PAT_MedicalHistoryResp resp = new PAT_MedicalHistoryResp();
            try
            {
                string data = JsonConvert.SerializeObject(savePatientMedicalHistoryReq, JsonSettings);
                resp = _serialized_json_data<PAT_MedicalHistoryResp>(SERVICE_URL + "SavePatientMedicalHistory", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Patient Medical History", ex.Message);
            }
            return resp;
        }

        public RespInfo UpdatePatientMedicalHistory(PAT_UpdatePatientMedicalHistoryReq updatePatientMedicalHistoryReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(updatePatientMedicalHistoryReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "UpdatePatientMedicalHistory", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Patient Medical History", ex.Message);
            }
            return resp;
        }

        public RespInfo DeletePatientMedicalHistory(PAT_DeletePatientMedicalHistoryReq deletePatientMedicalHistoryReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deletePatientMedicalHistoryReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeletePatientMedicalHistory", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Patient Medical History", ex.Message);
            }
            return resp;
        }

        public PAT_MedicalEventListResp ListPatientMedicalEvents(string hospitalId, string userId, string patientId, string hashValue)
        {
            PAT_MedicalEventListResp resp = new PAT_MedicalEventListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_MedicalEventListResp>(SERVICE_URL + "ListPatientMedicalEvents?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Patient Medical Events", ex.Message);
            }
            return resp;
        }

        public PAT_MedicalEventResp CreatePatientMedicalEvent(PAT_CreatePatientMedicalEventReq createPatientMedicalEventReq)
        {
            PAT_MedicalEventResp resp = new PAT_MedicalEventResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPatientMedicalEventReq, JsonSettings);
                resp = _serialized_json_data<PAT_MedicalEventResp>(SERVICE_URL + "CreatePatientMedicalEvent", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Patient Medical Event", ex.Message);
            }
            return resp;
        }

        public PAT_FamilyHistoryListResp ListPatientFamilyHistory(string hospitalId, string userId, string patientId, string hashValue)
        {
            PAT_FamilyHistoryListResp resp = new PAT_FamilyHistoryListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_FamilyHistoryListResp>(SERVICE_URL + "ListPatientFamilyHistory?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Patient Family History", ex.Message);
            }
            return resp;
        }

        public PAT_FamilyHistoryResp CreatePatientFamilyHistory(PAT_CreatePatientFamilyHistoryReq createPatientFamilyHistoryReq)
        {
            PAT_FamilyHistoryResp resp = new PAT_FamilyHistoryResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPatientFamilyHistoryReq, JsonSettings);
                resp = _serialized_json_data<PAT_FamilyHistoryResp>(SERVICE_URL + "CreatePatientFamilyHistory", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Patient Family History", ex.Message);
            }
            return resp;
        }

        public PAT_MedicalEventResp GetPatientMedicalEvent(string hospitalId, string userId, string patientMedEventId, string hashValue)
        {
            PAT_MedicalEventResp resp = new PAT_MedicalEventResp();
            try
            {
                resp = _download_serialized_json_data<PAT_MedicalEventResp>(SERVICE_URL + "GetPatientMedicalEvent?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientMedEventId", patientMedEventId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Patient Medical Event", ex.Message);
            }
            return resp;
        }

        public PAT_MedicalEventResp UpdatePatientMedicalEvent(PAT_UpdatePatientMedicalEventReq updatePatientMedicalEventReq)
        {
            PAT_MedicalEventResp resp = new PAT_MedicalEventResp();
            try
            {
                string data = JsonConvert.SerializeObject(updatePatientMedicalEventReq, JsonSettings);
                resp = _serialized_json_data<PAT_MedicalEventResp>(SERVICE_URL + "UpdatePatientMedicalEvent", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Patient Medical Event", ex.Message);
            }
            return resp;
        }

        public RespInfo DeletePatientMedicalEvent(PAT_DeletePatientMedicalEventReq deletePatientMedicalEventReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deletePatientMedicalEventReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeletePatientMedicalEvent", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Patient Medical Event", ex.Message);
            }
            return resp;
        }

        public PAT_FamilyHistoryResp GetPatientFamilyHistory(string hospitalId, string userId, string familyHistoryId, string hashValue)
        {
            PAT_FamilyHistoryResp resp = new PAT_FamilyHistoryResp();
            try
            {
                resp = _download_serialized_json_data<PAT_FamilyHistoryResp>(SERVICE_URL + "GetPatientFamilyHistory?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("familyHistoryId", familyHistoryId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Patient Family History", ex.Message);
            }
            return resp;
        }

        public PAT_FamilyHistoryResp UpdatePatientFamilyHistory(PAT_UpdatePatientFamilyHistoryReq updatePatientFamilyHistoryReq)
        {
            PAT_FamilyHistoryResp resp = new PAT_FamilyHistoryResp();
            try
            {
                string data = JsonConvert.SerializeObject(updatePatientFamilyHistoryReq, JsonSettings);
                resp = _serialized_json_data<PAT_FamilyHistoryResp>(SERVICE_URL + "UpdatePatientFamilyHistory", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Patient Family History", ex.Message);
            }
            return resp;
        }

        public RespInfo DeletePatientFamilyHistory(PAT_DeletePatientFamilyHistoryReq deletePatientFamilyHistoryReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deletePatientFamilyHistoryReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeletePatientFamilyHistory", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Patient Family History", ex.Message);
            }
            return resp;
        }

        public PAT_ConsentDocumentListResp ListPatientConsentDocuments(string hospitalId, string userId, string patientId, string hashValue)
        {
            PAT_ConsentDocumentListResp resp = new PAT_ConsentDocumentListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_ConsentDocumentListResp>(SERVICE_URL + "ListPatientConsentDocuments?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Patient Consent Documents", ex.Message);
            }
            return resp;
        }

        public PAT_ConsentDocumentResp GetPatientConsentDocument(string hospitalId, string userId, string consentDocumentId, string hashValue)
        {
            PAT_ConsentDocumentResp resp = new PAT_ConsentDocumentResp();
            try
            {
                resp = _download_serialized_json_data<PAT_ConsentDocumentResp>(SERVICE_URL + "GetPatientConsentDocument?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consentDocumentId", consentDocumentId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Patient Consent Document", ex.Message);
            }
            return resp;
        }

        public FileContentResp GetPatientConsentDocumentContent(string hospitalId, string userId, string consentDocumentId, string hashValue)
        {
            FileContentResp resp = new FileContentResp();
            try
            {
                resp = _download_serialized_json_data<FileContentResp>(SERVICE_URL + "GetPatientConsentDocumentContent?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consentDocumentId", consentDocumentId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Patient Consent Document Content", ex.Message);
            }
            return resp;
        }

        public PAT_ConsentDocumentResp CreatePatientConsentDocument(PAT_CreatePatientConsentDocumentReq createPatientConsentDocumentReq)
        {
            PAT_ConsentDocumentResp resp = new PAT_ConsentDocumentResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPatientConsentDocumentReq, JsonSettings);
                resp = _serialized_json_data<PAT_ConsentDocumentResp>(SERVICE_URL + "CreatePatientConsentDocument", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Patient Consent Document", ex.Message);
            }
            return resp;
        }

        public PAT_PayerListResp ListPayers(string hospitalId, string userId, string country, string category, string hashValue)
        {
            PAT_PayerListResp resp = new PAT_PayerListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_PayerListResp>(SERVICE_URL + "ListPayers?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("country", country) + QueryParam("category", category) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Payers", ex.Message);
            }
            return resp;
        }

        public PAT_CoverageListResp ListPatientCoverages(string hospitalId, string userId, string patientId, string includeInactive, string hashValue)
        {
            PAT_CoverageListResp resp = new PAT_CoverageListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_CoverageListResp>(SERVICE_URL + "ListPatientCoverages?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + QueryParam("includeInactive", includeInactive) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Patient Coverages", ex.Message);
            }
            return resp;
        }

        public PAT_CoverageResp SavePatientCoverage(PAT_SavePatientCoverageReq savePatientCoverageReq)
        {
            PAT_CoverageResp resp = new PAT_CoverageResp();
            try
            {
                string data = JsonConvert.SerializeObject(savePatientCoverageReq, JsonSettings);
                resp = _serialized_json_data<PAT_CoverageResp>(SERVICE_URL + "SavePatientCoverage", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Patient Coverage", ex.Message);
            }
            return resp;
        }

        public RespInfo DeactivatePatientCoverage(PAT_DeactivatePatientCoverageReq deactivatePatientCoverageReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deactivatePatientCoverageReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeactivatePatientCoverage", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Deactivate Patient Coverage", ex.Message);
            }
            return resp;
        }

        public PAT_PayerListResp ListAllPayers(string hospitalId, string userId, string category, string hashValue)
        {
            PAT_PayerListResp resp = new PAT_PayerListResp();
            try
            {
                resp = _download_serialized_json_data<PAT_PayerListResp>(SERVICE_URL + "ListAllPayers?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("category", category) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List All Payers", ex.Message);
            }
            return resp;
        }

        public PAT_PayerResp GetPayer(string hospitalId, string userId, string payerId, string hashValue)
        {
            PAT_PayerResp resp = new PAT_PayerResp();
            try
            {
                resp = _download_serialized_json_data<PAT_PayerResp>(SERVICE_URL + "GetPayer?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("payerId", payerId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Payer", ex.Message);
            }
            return resp;
        }

        public PAT_PayerResp SavePayer(PAT_SavePayerReq savePayerReq)
        {
            PAT_PayerResp resp = new PAT_PayerResp();
            try
            {
                string data = JsonConvert.SerializeObject(savePayerReq, JsonSettings);
                resp = _serialized_json_data<PAT_PayerResp>(SERVICE_URL + "SavePayer", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Payer", ex.Message);
            }
            return resp;
        }

        public PAT_PayerResp UpdatePayerActive(PAT_UpdatePayerActiveReq updatePayerActiveReq)
        {
            PAT_PayerResp resp = new PAT_PayerResp();
            try
            {
                string data = JsonConvert.SerializeObject(updatePayerActiveReq, JsonSettings);
                resp = _serialized_json_data<PAT_PayerResp>(SERVICE_URL + "UpdatePayerActive", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Payer Active", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Scheduling Methods
        public SCH_AppointmentListResp ListTodayAppointmentsForDoctor(string hospitalId, string userId, string doctorId, string hashValue)
        {
            SCH_AppointmentListResp resp = new SCH_AppointmentListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AppointmentListResp>(SERVICE_URL + "ListTodayAppointmentsForDoctor?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Today Appointments For Doctor", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentListResp ListAppointmentsForDate(string hospitalId, string userId, string doctorId, string date, string hashValue)
        {
            SCH_AppointmentListResp resp = new SCH_AppointmentListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AppointmentListResp>(SERVICE_URL + "ListAppointmentsForDate?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + QueryParam("date", date) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Appointments For Date", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentListResp ListAppointmentsForHospitalDate(string hospitalId, string userId, string date, string hashValue)
        {
            SCH_AppointmentListResp resp = new SCH_AppointmentListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AppointmentListResp>(SERVICE_URL + "ListAppointmentsForHospitalDate?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("date", date) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Appointments For Hospital Date", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentResp CreateAppointment(SCH_CreateAppointmentReq createAppointmentReq)
        {
            SCH_AppointmentResp resp = new SCH_AppointmentResp();
            try
            {
                string data = JsonConvert.SerializeObject(createAppointmentReq, JsonSettings);
                resp = _serialized_json_data<SCH_AppointmentResp>(SERVICE_URL + "CreateAppointment", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Appointment", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentResp UpdateAppointmentStatus(SCH_UpdateAppointmentStatusReq updateAppointmentStatusReq)
        {
            SCH_AppointmentResp resp = new SCH_AppointmentResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateAppointmentStatusReq, JsonSettings);
                resp = _serialized_json_data<SCH_AppointmentResp>(SERVICE_URL + "UpdateAppointmentStatus", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Appointment Status", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentResp GetAppointment(string hospitalId, string userId, string appointmentId, string hashValue)
        {
            SCH_AppointmentResp resp = new SCH_AppointmentResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AppointmentResp>(SERVICE_URL + "GetAppointment?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("appointmentId", appointmentId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Appointment", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentListResp ListAppointmentsForPatient(string hospitalId, string userId, string patientId, string hashValue)
        {
            SCH_AppointmentListResp resp = new SCH_AppointmentListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AppointmentListResp>(SERVICE_URL + "ListAppointmentsForPatient?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Appointments For Patient", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentListResp ListAppointmentsForRange(string hospitalId, string userId, string fromDate, string toDate, string doctorId, string kind, string status, string hashValue)
        {
            SCH_AppointmentListResp resp = new SCH_AppointmentListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AppointmentListResp>(SERVICE_URL + "ListAppointmentsForRange?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("fromDate", fromDate) + QueryParam("toDate", toDate) + QueryParam("doctorId", doctorId) + QueryParam("kind", kind) + QueryParam("status", status) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Appointments For Range", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentDashboardResp GetAppointmentDashboard(string hospitalId, string userId, string today, string hashValue)
        {
            SCH_AppointmentDashboardResp resp = new SCH_AppointmentDashboardResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AppointmentDashboardResp>(SERVICE_URL + "GetAppointmentDashboard?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("today", today) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Appointment Dashboard", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentResp UpdateAppointmentSlot(SCH_UpdateAppointmentSlotReq updateAppointmentSlotReq)
        {
            SCH_AppointmentResp resp = new SCH_AppointmentResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateAppointmentSlotReq, JsonSettings);
                resp = _serialized_json_data<SCH_AppointmentResp>(SERVICE_URL + "UpdateAppointmentSlot", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Appointment Slot", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentListResp CreateRecurringAppointments(SCH_CreateRecurringAppointmentsReq createRecurringAppointmentsReq)
        {
            SCH_AppointmentListResp resp = new SCH_AppointmentListResp();
            try
            {
                string data = JsonConvert.SerializeObject(createRecurringAppointmentsReq, JsonSettings);
                resp = _serialized_json_data<SCH_AppointmentListResp>(SERVICE_URL + "CreateRecurringAppointments", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Recurring Appointments", ex.Message);
            }
            return resp;
        }

        public SCH_SlotConfigListResp ListDoctorSlotConfig(string hospitalId, string userId, string doctorId, string hashValue)
        {
            SCH_SlotConfigListResp resp = new SCH_SlotConfigListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_SlotConfigListResp>(SERVICE_URL + "ListDoctorSlotConfig?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Doctor Slot Config", ex.Message);
            }
            return resp;
        }

        public SCH_SlotConfigListResp SaveDoctorSlotConfig(SCH_SaveDoctorSlotConfigReq saveDoctorSlotConfigReq)
        {
            SCH_SlotConfigListResp resp = new SCH_SlotConfigListResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveDoctorSlotConfigReq, JsonSettings);
                resp = _serialized_json_data<SCH_SlotConfigListResp>(SERVICE_URL + "SaveDoctorSlotConfig", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Doctor Slot Config", ex.Message);
            }
            return resp;
        }

        public SCH_AvailableSlotListResp ListAvailableSlots(string hospitalId, string userId, string doctorId, string date, string durationMinutes, string hashValue)
        {
            SCH_AvailableSlotListResp resp = new SCH_AvailableSlotListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AvailableSlotListResp>(SERVICE_URL + "ListAvailableSlots?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + QueryParam("date", date) + QueryParam("durationMinutes", durationMinutes) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Available Slots", ex.Message);
            }
            return resp;
        }

        public SCH_DoctorScheduleResp GetDoctorScheduleForDate(string hospitalId, string userId, string doctorId, string date, string hashValue)
        {
            SCH_DoctorScheduleResp resp = new SCH_DoctorScheduleResp();
            try
            {
                resp = _download_serialized_json_data<SCH_DoctorScheduleResp>(SERVICE_URL + "GetDoctorScheduleForDate?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + QueryParam("date", date) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Doctor Schedule For Date", ex.Message);
            }
            return resp;
        }

        public SCH_DoctorLeaveListResp ListDoctorLeave(string hospitalId, string userId, string doctorId, string hashValue)
        {
            SCH_DoctorLeaveListResp resp = new SCH_DoctorLeaveListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_DoctorLeaveListResp>(SERVICE_URL + "ListDoctorLeave?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Doctor Leave", ex.Message);
            }
            return resp;
        }

        public SCH_DoctorLeaveResp CreateDoctorLeave(SCH_CreateDoctorLeaveReq createDoctorLeaveReq)
        {
            SCH_DoctorLeaveResp resp = new SCH_DoctorLeaveResp();
            try
            {
                string data = JsonConvert.SerializeObject(createDoctorLeaveReq, JsonSettings);
                resp = _serialized_json_data<SCH_DoctorLeaveResp>(SERVICE_URL + "CreateDoctorLeave", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Doctor Leave", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteDoctorLeave(SCH_DeleteDoctorLeaveReq deleteDoctorLeaveReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteDoctorLeaveReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteDoctorLeave", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Doctor Leave", ex.Message);
            }
            return resp;
        }

        public SCH_DoctorSettingsResp GetDoctorSettings(string hospitalId, string userId, string doctorId, string hashValue)
        {
            SCH_DoctorSettingsResp resp = new SCH_DoctorSettingsResp();
            try
            {
                resp = _download_serialized_json_data<SCH_DoctorSettingsResp>(SERVICE_URL + "GetDoctorSettings?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Doctor Settings", ex.Message);
            }
            return resp;
        }

        public SCH_DoctorSettingsResp SaveDoctorSettings(SCH_SaveDoctorSettingsReq saveDoctorSettingsReq)
        {
            SCH_DoctorSettingsResp resp = new SCH_DoctorSettingsResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveDoctorSettingsReq, JsonSettings);
                resp = _serialized_json_data<SCH_DoctorSettingsResp>(SERVICE_URL + "SaveDoctorSettings", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Doctor Settings", ex.Message);
            }
            return resp;
        }

        public SCH_DoctorProfileListResp ListDoctorProfiles(string hospitalId, string userId, string hashValue)
        {
            SCH_DoctorProfileListResp resp = new SCH_DoctorProfileListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_DoctorProfileListResp>(SERVICE_URL + "ListDoctorProfiles?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Doctor Profiles", ex.Message);
            }
            return resp;
        }

        public SCH_SavedDoctorProfileResp SaveDoctorProfile(SCH_SaveDoctorProfileReq saveDoctorProfileReq)
        {
            SCH_SavedDoctorProfileResp resp = new SCH_SavedDoctorProfileResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveDoctorProfileReq, JsonSettings);
                resp = _serialized_json_data<SCH_SavedDoctorProfileResp>(SERVICE_URL + "SaveDoctorProfile", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Doctor Profile", ex.Message);
            }
            return resp;
        }

        public SCH_DoctorChargeResp GetDoctorCharge(string hospitalId, string userId, string doctorId, string hashValue)
        {
            SCH_DoctorChargeResp resp = new SCH_DoctorChargeResp();
            try
            {
                resp = _download_serialized_json_data<SCH_DoctorChargeResp>(SERVICE_URL + "GetDoctorCharge?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Doctor Charge", ex.Message);
            }
            return resp;
        }

        public SCH_DoctorChargeResp SaveDoctorCharge(SCH_SaveDoctorChargeReq saveDoctorChargeReq)
        {
            SCH_DoctorChargeResp resp = new SCH_DoctorChargeResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveDoctorChargeReq, JsonSettings);
                resp = _serialized_json_data<SCH_DoctorChargeResp>(SERVICE_URL + "SaveDoctorCharge", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Doctor Charge", ex.Message);
            }
            return resp;
        }

        public SCH_FeeScheduleResp CheckEffectiveFee(string hospitalId, string userId, string doctorId, string consultationType, string onDate, string hashValue)
        {
            SCH_FeeScheduleResp resp = new SCH_FeeScheduleResp();
            try
            {
                resp = _download_serialized_json_data<SCH_FeeScheduleResp>(SERVICE_URL + "CheckEffectiveFee?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + QueryParam("consultationType", consultationType) + QueryParam("onDate", onDate) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Check Effective Fee", ex.Message);
            }
            return resp;
        }

        public SCH_FeeScheduleListResp ListDoctorFees(string hospitalId, string userId, string hashValue)
        {
            SCH_FeeScheduleListResp resp = new SCH_FeeScheduleListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_FeeScheduleListResp>(SERVICE_URL + "ListDoctorFees?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Doctor Fees", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentTypeListResp ListAppointmentTypes(string hospitalId, string userId, string includeInactive, string hashValue)
        {
            SCH_AppointmentTypeListResp resp = new SCH_AppointmentTypeListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AppointmentTypeListResp>(SERVICE_URL + "ListAppointmentTypes?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("includeInactive", includeInactive) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Appointment Types", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentTypeResp SaveAppointmentType(SCH_SaveAppointmentTypeReq saveAppointmentTypeReq)
        {
            SCH_AppointmentTypeResp resp = new SCH_AppointmentTypeResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveAppointmentTypeReq, JsonSettings);
                resp = _serialized_json_data<SCH_AppointmentTypeResp>(SERVICE_URL + "SaveAppointmentType", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Appointment Type", ex.Message);
            }
            return resp;
        }

        public SCH_AppointmentTypeResp UpdateAppointmentTypeActive(SCH_UpdateAppointmentTypeActiveReq updateAppointmentTypeActiveReq)
        {
            SCH_AppointmentTypeResp resp = new SCH_AppointmentTypeResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateAppointmentTypeActiveReq, JsonSettings);
                resp = _serialized_json_data<SCH_AppointmentTypeResp>(SERVICE_URL + "UpdateAppointmentTypeActive", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Appointment Type Active", ex.Message);
            }
            return resp;
        }

        public SCH_IntakeQuestionListResp ListIntakeQuestions(string hospitalId, string userId, string hashValue)
        {
            SCH_IntakeQuestionListResp resp = new SCH_IntakeQuestionListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_IntakeQuestionListResp>(SERVICE_URL + "ListIntakeQuestions?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Intake Questions", ex.Message);
            }
            return resp;
        }

        public SCH_IntakeQuestionResp CreateIntakeQuestion(SCH_CreateIntakeQuestionReq createIntakeQuestionReq)
        {
            SCH_IntakeQuestionResp resp = new SCH_IntakeQuestionResp();
            try
            {
                string data = JsonConvert.SerializeObject(createIntakeQuestionReq, JsonSettings);
                resp = _serialized_json_data<SCH_IntakeQuestionResp>(SERVICE_URL + "CreateIntakeQuestion", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Intake Question", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteIntakeQuestion(SCH_DeleteIntakeQuestionReq deleteIntakeQuestionReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteIntakeQuestionReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteIntakeQuestion", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Intake Question", ex.Message);
            }
            return resp;
        }

        public SCH_ConsentFormResp GetConsentForm(string hospitalId, string userId, string hashValue)
        {
            SCH_ConsentFormResp resp = new SCH_ConsentFormResp();
            try
            {
                resp = _download_serialized_json_data<SCH_ConsentFormResp>(SERVICE_URL + "GetConsentForm?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Consent Form", ex.Message);
            }
            return resp;
        }

        public SCH_ConsentFormResp SaveConsentForm(SCH_SaveConsentFormReq saveConsentFormReq)
        {
            SCH_ConsentFormResp resp = new SCH_ConsentFormResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveConsentFormReq, JsonSettings);
                resp = _serialized_json_data<SCH_ConsentFormResp>(SERVICE_URL + "SaveConsentForm", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Consent Form", ex.Message);
            }
            return resp;
        }

        public SCH_IntakeResponseResp SubmitIntakeResponse(SCH_SubmitIntakeResponseReq submitIntakeResponseReq)
        {
            SCH_IntakeResponseResp resp = new SCH_IntakeResponseResp();
            try
            {
                string data = JsonConvert.SerializeObject(submitIntakeResponseReq, JsonSettings);
                resp = _serialized_json_data<SCH_IntakeResponseResp>(SERVICE_URL + "SubmitIntakeResponse", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Submit Intake Response", ex.Message);
            }
            return resp;
        }

        public SCH_ConsentRecordResp RecordConsent(SCH_RecordConsentReq recordConsentReq)
        {
            SCH_ConsentRecordResp resp = new SCH_ConsentRecordResp();
            try
            {
                string data = JsonConvert.SerializeObject(recordConsentReq, JsonSettings);
                resp = _serialized_json_data<SCH_ConsentRecordResp>(SERVICE_URL + "RecordConsent", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Record Consent", ex.Message);
            }
            return resp;
        }

        public SCH_VisitIntakeAndConsentResp GetVisitIntakeAndConsent(string hospitalId, string userId, string consultationId, string hashValue)
        {
            SCH_VisitIntakeAndConsentResp resp = new SCH_VisitIntakeAndConsentResp();
            try
            {
                resp = _download_serialized_json_data<SCH_VisitIntakeAndConsentResp>(SERVICE_URL + "GetVisitIntakeAndConsent?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Visit Intake And Consent", ex.Message);
            }
            return resp;
        }

        public SCH_ClinicalConditionListResp ListClinicalConditions(string hospitalId, string userId, string hashValue)
        {
            SCH_ClinicalConditionListResp resp = new SCH_ClinicalConditionListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_ClinicalConditionListResp>(SERVICE_URL + "ListClinicalConditions?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Clinical Conditions", ex.Message);
            }
            return resp;
        }

        public SCH_ClinicalConditionResp CreateClinicalCondition(SCH_CreateClinicalConditionReq createClinicalConditionReq)
        {
            SCH_ClinicalConditionResp resp = new SCH_ClinicalConditionResp();
            try
            {
                string data = JsonConvert.SerializeObject(createClinicalConditionReq, JsonSettings);
                resp = _serialized_json_data<SCH_ClinicalConditionResp>(SERVICE_URL + "CreateClinicalCondition", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Clinical Condition", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteClinicalCondition(SCH_DeleteClinicalConditionReq deleteClinicalConditionReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteClinicalConditionReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteClinicalCondition", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Clinical Condition", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Patient Portal Methods
        public SCH_PortalOptionsResp GetPortalBookingOptions(string hashValue)
        {
            SCH_PortalOptionsResp resp = new SCH_PortalOptionsResp();
            try
            {
                resp = _download_serialized_json_data<SCH_PortalOptionsResp>(SERVICE_URL + "GetPortalBookingOptions?hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Portal Booking Options", ex.Message);
            }
            return resp;
        }

        public SCH_AvailableSlotListResp ListPortalAvailableSlots(string doctorId, string date, string durationMinutes, string hashValue)
        {
            SCH_AvailableSlotListResp resp = new SCH_AvailableSlotListResp();
            try
            {
                resp = _download_serialized_json_data<SCH_AvailableSlotListResp>(SERVICE_URL + "ListPortalAvailableSlots?hashValue=" + hashValue + QueryParam("doctorId", doctorId) + QueryParam("date", date) + QueryParam("durationMinutes", durationMinutes));
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Portal Available Slots", ex.Message);
            }
            return resp;
        }

        public SCH_PortalPatientResp CreatePortalPatient(SCH_CreatePortalPatientReq createPortalPatientReq)
        {
            SCH_PortalPatientResp resp = new SCH_PortalPatientResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPortalPatientReq, JsonSettings);
                resp = _serialized_json_data<SCH_PortalPatientResp>(SERVICE_URL + "CreatePortalPatient", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Portal Patient", ex.Message);
            }
            return resp;
        }

        public SCH_PortalPatientResp GetPortalPatient(string mrn, string phone, string hashValue)
        {
            SCH_PortalPatientResp resp = new SCH_PortalPatientResp();
            try
            {
                resp = _download_serialized_json_data<SCH_PortalPatientResp>(SERVICE_URL + "GetPortalPatient?hashValue=" + hashValue + QueryParam("mrn", mrn) + QueryParam("phone", phone));
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Portal Patient", ex.Message);
            }
            return resp;
        }

        public SCH_PortalAppointmentResp CreatePortalAppointment(SCH_CreatePortalAppointmentReq createPortalAppointmentReq)
        {
            SCH_PortalAppointmentResp resp = new SCH_PortalAppointmentResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPortalAppointmentReq, JsonSettings);
                resp = _serialized_json_data<SCH_PortalAppointmentResp>(SERVICE_URL + "CreatePortalAppointment", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Portal Appointment", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Consultation Methods
        public CON_ConsultationResp GetOrCreateConsultation(CON_GetOrCreateConsultationReq getOrCreateConsultationReq)
        {
            CON_ConsultationResp resp = new CON_ConsultationResp();
            try
            {
                string data = JsonConvert.SerializeObject(getOrCreateConsultationReq, JsonSettings);
                resp = _serialized_json_data<CON_ConsultationResp>(SERVICE_URL + "GetOrCreateConsultation", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Or Create Consultation", ex.Message);
            }
            return resp;
        }

        public CON_ConsultationDetailResp GetConsultationById(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_ConsultationDetailResp resp = new CON_ConsultationDetailResp();
            try
            {
                resp = _download_serialized_json_data<CON_ConsultationDetailResp>(SERVICE_URL + "GetConsultationById?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Consultation By Id", ex.Message);
            }
            return resp;
        }

        public CON_ConsultationListResp ListConsultationsForDate(string hospitalId, string userId, string visitDate, string hashValue)
        {
            CON_ConsultationListResp resp = new CON_ConsultationListResp();
            try
            {
                resp = _download_serialized_json_data<CON_ConsultationListResp>(SERVICE_URL + "ListConsultationsForDate?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("visitDate", visitDate) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Consultations For Date", ex.Message);
            }
            return resp;
        }

        public RespInfo SaveSoapNotes(CON_SaveSoapNotesReq saveSoapNotesReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(saveSoapNotesReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "SaveSoapNotes", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Soap Notes", ex.Message);
            }
            return resp;
        }

        public RespInfo SaveFollowup(CON_SaveFollowupReq saveFollowupReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(saveFollowupReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "SaveFollowup", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Followup", ex.Message);
            }
            return resp;
        }

        public CON_AttestationResp ConfirmSignOff(CON_ConfirmSignOffReq confirmSignOffReq)
        {
            CON_AttestationResp resp = new CON_AttestationResp();
            try
            {
                string data = JsonConvert.SerializeObject(confirmSignOffReq, JsonSettings);
                resp = _serialized_json_data<CON_AttestationResp>(SERVICE_URL + "ConfirmSignOff", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Confirm Sign Off", ex.Message);
            }
            return resp;
        }

        public CON_PastVisitListResp ListPatientPastVisits(string hospitalId, string userId, string patientId, string hashValue)
        {
            CON_PastVisitListResp resp = new CON_PastVisitListResp();
            try
            {
                resp = _download_serialized_json_data<CON_PastVisitListResp>(SERVICE_URL + "ListPatientPastVisits?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Patient Past Visits", ex.Message);
            }
            return resp;
        }

        public CON_ConsultationResp GetInProgressConsultation(string hospitalId, string userId, string patientId, string doctorId, string hashValue)
        {
            CON_ConsultationResp resp = new CON_ConsultationResp();
            try
            {
                resp = _download_serialized_json_data<CON_ConsultationResp>(SERVICE_URL + "GetInProgressConsultation?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + QueryParam("doctorId", doctorId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get In Progress Consultation", ex.Message);
            }
            return resp;
        }

        public RespInfo SaveConsultationCustomValues(CON_SaveConsultationCustomValuesReq saveConsultationCustomValuesReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(saveConsultationCustomValuesReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "SaveConsultationCustomValues", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Consultation Custom Values", ex.Message);
            }
            return resp;
        }

        public CON_CustomFieldValueListResp ListConsultationCustomValues(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_CustomFieldValueListResp resp = new CON_CustomFieldValueListResp();
            try
            {
                resp = _download_serialized_json_data<CON_CustomFieldValueListResp>(SERVICE_URL + "ListConsultationCustomValues?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Consultation Custom Values", ex.Message);
            }
            return resp;
        }

        public CON_VitalsResp GetConsultationVitals(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_VitalsResp resp = new CON_VitalsResp();
            try
            {
                resp = _download_serialized_json_data<CON_VitalsResp>(SERVICE_URL + "GetConsultationVitals?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Consultation Vitals", ex.Message);
            }
            return resp;
        }

        public CON_VitalsResp SaveVitals(CON_SaveVitalsReq saveVitalsReq)
        {
            CON_VitalsResp resp = new CON_VitalsResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveVitalsReq, JsonSettings);
                resp = _serialized_json_data<CON_VitalsResp>(SERVICE_URL + "SaveVitals", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Vitals", ex.Message);
            }
            return resp;
        }

        public CON_SymptomListResp ListSymptomCatalog(string hospitalId, string userId, string hashValue)
        {
            CON_SymptomListResp resp = new CON_SymptomListResp();
            try
            {
                resp = _download_serialized_json_data<CON_SymptomListResp>(SERVICE_URL + "ListSymptomCatalog?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Symptom Catalog", ex.Message);
            }
            return resp;
        }

        public CON_SymptomListResp ListSymptomQuickPicks(string hospitalId, string userId, string hashValue)
        {
            CON_SymptomListResp resp = new CON_SymptomListResp();
            try
            {
                resp = _download_serialized_json_data<CON_SymptomListResp>(SERVICE_URL + "ListSymptomQuickPicks?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Symptom Quick Picks", ex.Message);
            }
            return resp;
        }

        public CON_SymptomListResp ListSymptomOthers(string hospitalId, string userId, string hashValue)
        {
            CON_SymptomListResp resp = new CON_SymptomListResp();
            try
            {
                resp = _download_serialized_json_data<CON_SymptomListResp>(SERVICE_URL + "ListSymptomOthers?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Symptom Others", ex.Message);
            }
            return resp;
        }

        public CON_SymptomResp CreateCustomSymptom(CON_CreateCustomSymptomReq createCustomSymptomReq)
        {
            CON_SymptomResp resp = new CON_SymptomResp();
            try
            {
                string data = JsonConvert.SerializeObject(createCustomSymptomReq, JsonSettings);
                resp = _serialized_json_data<CON_SymptomResp>(SERVICE_URL + "CreateCustomSymptom", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Custom Symptom", ex.Message);
            }
            return resp;
        }

        public CON_ConsultationSymptomListResp ListConsultationSymptoms(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_ConsultationSymptomListResp resp = new CON_ConsultationSymptomListResp();
            try
            {
                resp = _download_serialized_json_data<CON_ConsultationSymptomListResp>(SERVICE_URL + "ListConsultationSymptoms?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Consultation Symptoms", ex.Message);
            }
            return resp;
        }

        public RespInfo UpdateSymptomState(CON_UpdateSymptomStateReq updateSymptomStateReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(updateSymptomStateReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "UpdateSymptomState", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Symptom State", ex.Message);
            }
            return resp;
        }

        public CON_Icd10ListResp SearchIcd10Catalog(string hospitalId, string userId, string term, string hashValue)
        {
            CON_Icd10ListResp resp = new CON_Icd10ListResp();
            try
            {
                resp = _download_serialized_json_data<CON_Icd10ListResp>(SERVICE_URL + "SearchIcd10Catalog?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("term", term) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Search Icd10 Catalog", ex.Message);
            }
            return resp;
        }

        public CON_DiagnosisListResp ListConsultationDiagnoses(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_DiagnosisListResp resp = new CON_DiagnosisListResp();
            try
            {
                resp = _download_serialized_json_data<CON_DiagnosisListResp>(SERVICE_URL + "ListConsultationDiagnoses?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Consultation Diagnoses", ex.Message);
            }
            return resp;
        }

        public CON_DiagnosisResp CreateDiagnosis(CON_CreateDiagnosisReq createDiagnosisReq)
        {
            CON_DiagnosisResp resp = new CON_DiagnosisResp();
            try
            {
                string data = JsonConvert.SerializeObject(createDiagnosisReq, JsonSettings);
                resp = _serialized_json_data<CON_DiagnosisResp>(SERVICE_URL + "CreateDiagnosis", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Diagnosis", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteDiagnosis(CON_DeleteDiagnosisReq deleteDiagnosisReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteDiagnosisReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteDiagnosis", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Diagnosis", ex.Message);
            }
            return resp;
        }

        public CON_PrescriptionListResp ListPrescriptions(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_PrescriptionListResp resp = new CON_PrescriptionListResp();
            try
            {
                resp = _download_serialized_json_data<CON_PrescriptionListResp>(SERVICE_URL + "ListPrescriptions?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Prescriptions", ex.Message);
            }
            return resp;
        }

        public CON_PrescriptionListResp ListActiveMedicationsForPatient(string hospitalId, string userId, string patientId, string hashValue)
        {
            CON_PrescriptionListResp resp = new CON_PrescriptionListResp();
            try
            {
                resp = _download_serialized_json_data<CON_PrescriptionListResp>(SERVICE_URL + "ListActiveMedicationsForPatient?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Active Medications For Patient", ex.Message);
            }
            return resp;
        }

        public CON_PreviousPrescriptionsResp ListPreviousPrescriptionsByDoctor(string hospitalId, string userId, string patientId, string doctorId, string hashValue)
        {
            CON_PreviousPrescriptionsResp resp = new CON_PreviousPrescriptionsResp();
            try
            {
                resp = _download_serialized_json_data<CON_PreviousPrescriptionsResp>(SERVICE_URL + "ListPreviousPrescriptionsByDoctor?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + QueryParam("doctorId", doctorId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Previous Prescriptions By Doctor", ex.Message);
            }
            return resp;
        }

        public CON_PrescriptionResp CreatePrescription(CON_CreatePrescriptionReq createPrescriptionReq)
        {
            CON_PrescriptionResp resp = new CON_PrescriptionResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPrescriptionReq, JsonSettings);
                resp = _serialized_json_data<CON_PrescriptionResp>(SERVICE_URL + "CreatePrescription", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Prescription", ex.Message);
            }
            return resp;
        }

        public RespInfo CancelPrescription(CON_CancelPrescriptionReq cancelPrescriptionReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(cancelPrescriptionReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "CancelPrescription", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Cancel Prescription", ex.Message);
            }
            return resp;
        }

        public CON_DrugAllergyAlertListResp CheckDrugAllergyConflicts(string hospitalId, string userId, string patientId, string medicineName, string composition, string hashValue)
        {
            CON_DrugAllergyAlertListResp resp = new CON_DrugAllergyAlertListResp();
            try
            {
                resp = _download_serialized_json_data<CON_DrugAllergyAlertListResp>(SERVICE_URL + "CheckDrugAllergyConflicts?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + QueryParam("medicineName", medicineName) + QueryParam("composition", composition) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Check Drug Allergy Conflicts", ex.Message);
            }
            return resp;
        }

        public CON_PrescriptionListResp ListPatientMedicationHistory(string hospitalId, string userId, string patientId, string hashValue)
        {
            CON_PrescriptionListResp resp = new CON_PrescriptionListResp();
            try
            {
                resp = _download_serialized_json_data<CON_PrescriptionListResp>(SERVICE_URL + "ListPatientMedicationHistory?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Patient Medication History", ex.Message);
            }
            return resp;
        }

        public CON_PrescriptionResp SaveOutsideMedication(CON_SaveOutsideMedicationReq saveOutsideMedicationReq)
        {
            CON_PrescriptionResp resp = new CON_PrescriptionResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveOutsideMedicationReq, JsonSettings);
                resp = _serialized_json_data<CON_PrescriptionResp>(SERVICE_URL + "SaveOutsideMedication", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Outside Medication", ex.Message);
            }
            return resp;
        }

        public CON_InvestigationOrderResp CreateInvestigationOrder(CON_CreateInvestigationOrderReq createInvestigationOrderReq)
        {
            CON_InvestigationOrderResp resp = new CON_InvestigationOrderResp();
            try
            {
                string data = JsonConvert.SerializeObject(createInvestigationOrderReq, JsonSettings);
                resp = _serialized_json_data<CON_InvestigationOrderResp>(SERVICE_URL + "CreateInvestigationOrder", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Investigation Order", ex.Message);
            }
            return resp;
        }

        public CON_InvestigationOrderListResp ListInvestigationOrders(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_InvestigationOrderListResp resp = new CON_InvestigationOrderListResp();
            try
            {
                resp = _download_serialized_json_data<CON_InvestigationOrderListResp>(SERVICE_URL + "ListInvestigationOrders?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Investigation Orders", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteInvestigationOrder(CON_DeleteInvestigationOrderReq deleteInvestigationOrderReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteInvestigationOrderReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteInvestigationOrder", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Investigation Order", ex.Message);
            }
            return resp;
        }

        public CON_ReferralResp CreateReferral(CON_CreateReferralReq createReferralReq)
        {
            CON_ReferralResp resp = new CON_ReferralResp();
            try
            {
                string data = JsonConvert.SerializeObject(createReferralReq, JsonSettings);
                resp = _serialized_json_data<CON_ReferralResp>(SERVICE_URL + "CreateReferral", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Referral", ex.Message);
            }
            return resp;
        }

        public CON_ReferralListResp ListReferrals(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_ReferralListResp resp = new CON_ReferralListResp();
            try
            {
                resp = _download_serialized_json_data<CON_ReferralListResp>(SERVICE_URL + "ListReferrals?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Referrals", ex.Message);
            }
            return resp;
        }

        public CON_SketchResp SaveSketch(CON_SaveSketchReq saveSketchReq)
        {
            CON_SketchResp resp = new CON_SketchResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveSketchReq, JsonSettings);
                resp = _serialized_json_data<CON_SketchResp>(SERVICE_URL + "SaveSketch", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Sketch", ex.Message);
            }
            return resp;
        }

        public CON_SketchListResp ListSketches(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_SketchListResp resp = new CON_SketchListResp();
            try
            {
                resp = _download_serialized_json_data<CON_SketchListResp>(SERVICE_URL + "ListSketches?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Sketches", ex.Message);
            }
            return resp;
        }

        public CON_SketchListResp ListPatientSketches(string hospitalId, string userId, string patientId, string hashValue)
        {
            CON_SketchListResp resp = new CON_SketchListResp();
            try
            {
                resp = _download_serialized_json_data<CON_SketchListResp>(SERVICE_URL + "ListPatientSketches?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Patient Sketches", ex.Message);
            }
            return resp;
        }

        public CON_ConfidentialNoteResp GetConfidentialNote(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_ConfidentialNoteResp resp = new CON_ConfidentialNoteResp();
            try
            {
                resp = _download_serialized_json_data<CON_ConfidentialNoteResp>(SERVICE_URL + "GetConfidentialNote?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Confidential Note", ex.Message);
            }
            return resp;
        }

        public CON_ConfidentialNoteResp SaveConfidentialNote(CON_SaveConfidentialNoteReq saveConfidentialNoteReq)
        {
            CON_ConfidentialNoteResp resp = new CON_ConfidentialNoteResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveConfidentialNoteReq, JsonSettings);
                resp = _serialized_json_data<CON_ConfidentialNoteResp>(SERVICE_URL + "SaveConfidentialNote", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Confidential Note", ex.Message);
            }
            return resp;
        }

        public CON_AddendumResp CreateAddendum(CON_CreateAddendumReq createAddendumReq)
        {
            CON_AddendumResp resp = new CON_AddendumResp();
            try
            {
                string data = JsonConvert.SerializeObject(createAddendumReq, JsonSettings);
                resp = _serialized_json_data<CON_AddendumResp>(SERVICE_URL + "CreateAddendum", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Addendum", ex.Message);
            }
            return resp;
        }

        public CON_AddendumListResp ListAddendums(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_AddendumListResp resp = new CON_AddendumListResp();
            try
            {
                resp = _download_serialized_json_data<CON_AddendumListResp>(SERVICE_URL + "ListAddendums?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Addendums", ex.Message);
            }
            return resp;
        }

        public CON_ServiceItemListResp ListConsultationServiceItems(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_ServiceItemListResp resp = new CON_ServiceItemListResp();
            try
            {
                resp = _download_serialized_json_data<CON_ServiceItemListResp>(SERVICE_URL + "ListConsultationServiceItems?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Consultation Service Items", ex.Message);
            }
            return resp;
        }

        public CON_ServiceItemResp CreateConsultationServiceItem(CON_CreateConsultationServiceItemReq createConsultationServiceItemReq)
        {
            CON_ServiceItemResp resp = new CON_ServiceItemResp();
            try
            {
                string data = JsonConvert.SerializeObject(createConsultationServiceItemReq, JsonSettings);
                resp = _serialized_json_data<CON_ServiceItemResp>(SERVICE_URL + "CreateConsultationServiceItem", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Consultation Service Item", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteConsultationServiceItem(CON_DeleteConsultationServiceItemReq deleteConsultationServiceItemReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteConsultationServiceItemReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteConsultationServiceItem", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Consultation Service Item", ex.Message);
            }
            return resp;
        }

        public CON_StockShortageListResp ListStockShortages(string hospitalId, string userId, string consultationId, string hashValue)
        {
            CON_StockShortageListResp resp = new CON_StockShortageListResp();
            try
            {
                resp = _download_serialized_json_data<CON_StockShortageListResp>(SERVICE_URL + "ListStockShortages?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Stock Shortages", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Catalog Methods
        public CFG_DrugListResp SearchFormulary(string hospitalId, string userId, string term, string hashValue)
        {
            CFG_DrugListResp resp = new CFG_DrugListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_DrugListResp>(SERVICE_URL + "SearchFormulary?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("term", term) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Search Formulary", ex.Message);
            }
            return resp;
        }

        public CFG_DrugResp GetDrugByName(string hospitalId, string userId, string name, string hashValue)
        {
            CFG_DrugResp resp = new CFG_DrugResp();
            try
            {
                resp = _download_serialized_json_data<CFG_DrugResp>(SERVICE_URL + "GetDrugByName?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("name", name) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Drug By Name", ex.Message);
            }
            return resp;
        }

        public CFG_DrugResp CreateDrug(CFG_CreateDrugReq createDrugReq)
        {
            CFG_DrugResp resp = new CFG_DrugResp();
            try
            {
                string data = JsonConvert.SerializeObject(createDrugReq, JsonSettings);
                resp = _serialized_json_data<CFG_DrugResp>(SERVICE_URL + "CreateDrug", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Drug", ex.Message);
            }
            return resp;
        }

        public CFG_DrugResp UpdateDrugStatus(CFG_UpdateDrugStatusReq updateDrugStatusReq)
        {
            CFG_DrugResp resp = new CFG_DrugResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateDrugStatusReq, JsonSettings);
                resp = _serialized_json_data<CFG_DrugResp>(SERVICE_URL + "UpdateDrugStatus", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Drug Status", ex.Message);
            }
            return resp;
        }

        public CFG_DrugResp UpdateDrug(CFG_UpdateDrugReq updateDrugReq)
        {
            CFG_DrugResp resp = new CFG_DrugResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateDrugReq, JsonSettings);
                resp = _serialized_json_data<CFG_DrugResp>(SERVICE_URL + "UpdateDrug", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Drug", ex.Message);
            }
            return resp;
        }

        public CFG_LabTestListResp SearchLabTests(string hospitalId, string userId, string term, string specialtyId, string hashValue)
        {
            CFG_LabTestListResp resp = new CFG_LabTestListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_LabTestListResp>(SERVICE_URL + "SearchLabTests?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("term", term) + QueryParam("specialtyId", specialtyId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Search Lab Tests", ex.Message);
            }
            return resp;
        }

        public CFG_LabTestResp CreateLabTest(CFG_CreateLabTestReq createLabTestReq)
        {
            CFG_LabTestResp resp = new CFG_LabTestResp();
            try
            {
                string data = JsonConvert.SerializeObject(createLabTestReq, JsonSettings);
                resp = _serialized_json_data<CFG_LabTestResp>(SERVICE_URL + "CreateLabTest", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Lab Test", ex.Message);
            }
            return resp;
        }

        public CFG_LabTestListResp ListDoctorLabFavorites(string hospitalId, string userId, string doctorId, string specialtyId, string hashValue)
        {
            CFG_LabTestListResp resp = new CFG_LabTestListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_LabTestListResp>(SERVICE_URL + "ListDoctorLabFavorites?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("doctorId", doctorId) + QueryParam("specialtyId", specialtyId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Doctor Lab Favorites", ex.Message);
            }
            return resp;
        }

        public CFG_LabFavoriteResp CreateLabFavorite(CFG_LabFavoriteReq createLabFavoriteReq)
        {
            CFG_LabFavoriteResp resp = new CFG_LabFavoriteResp();
            try
            {
                string data = JsonConvert.SerializeObject(createLabFavoriteReq, JsonSettings);
                resp = _serialized_json_data<CFG_LabFavoriteResp>(SERVICE_URL + "CreateLabFavorite", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Lab Favorite", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteLabFavorite(CFG_LabFavoriteReq deleteLabFavoriteReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteLabFavoriteReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteLabFavorite", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Lab Favorite", ex.Message);
            }
            return resp;
        }

        public CFG_PhraseListResp ListPhrases(string hospitalId, string userId, string section, string specialtyId, string doctorId, string consultationId, string hashValue)
        {
            CFG_PhraseListResp resp = new CFG_PhraseListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_PhraseListResp>(SERVICE_URL + "ListPhrases?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("section", section) + QueryParam("specialtyId", specialtyId) + QueryParam("doctorId", doctorId) + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Phrases", ex.Message);
            }
            return resp;
        }

        public CFG_PhraseListResp SearchPhrases(string hospitalId, string userId, string section, string specialtyId, string doctorId, string consultationId, string term, string hashValue)
        {
            CFG_PhraseListResp resp = new CFG_PhraseListResp();
            try
            {
                resp = _download_serialized_json_data<CFG_PhraseListResp>(SERVICE_URL + "SearchPhrases?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("section", section) + QueryParam("specialtyId", specialtyId) + QueryParam("doctorId", doctorId) + QueryParam("consultationId", consultationId) + QueryParam("term", term) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Search Phrases", ex.Message);
            }
            return resp;
        }

        public CFG_PhraseResp CreatePhrase(CFG_CreatePhraseReq createPhraseReq)
        {
            CFG_PhraseResp resp = new CFG_PhraseResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPhraseReq, JsonSettings);
                resp = _serialized_json_data<CFG_PhraseResp>(SERVICE_URL + "CreatePhrase", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Phrase", ex.Message);
            }
            return resp;
        }

        public RespInfo DeletePhrase(CFG_DeletePhraseReq deletePhraseReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deletePhraseReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeletePhrase", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Phrase", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Lab Methods
        public LAB_OrderListResp ListPendingLabOrders(string hospitalId, string userId, string hashValue)
        {
            LAB_OrderListResp resp = new LAB_OrderListResp();
            try
            {
                resp = _download_serialized_json_data<LAB_OrderListResp>(SERVICE_URL + "ListPendingLabOrders?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Pending Lab Orders", ex.Message);
            }
            return resp;
        }

        public RespInfo UpdateOrderStatus(LAB_UpdateOrderStatusReq updateOrderStatusReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(updateOrderStatusReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "UpdateOrderStatus", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Order Status", ex.Message);
            }
            return resp;
        }

        public LAB_LabResultResp SubmitLabResult(LAB_SubmitLabResultReq submitLabResultReq)
        {
            LAB_LabResultResp resp = new LAB_LabResultResp();
            try
            {
                string data = JsonConvert.SerializeObject(submitLabResultReq, JsonSettings);
                resp = _serialized_json_data<LAB_LabResultResp>(SERVICE_URL + "SubmitLabResult", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Submit Lab Result", ex.Message);
            }
            return resp;
        }

        public LAB_LabResultListResp ListLabResultsForPatient(string hospitalId, string userId, string patientId, string hashValue)
        {
            LAB_LabResultListResp resp = new LAB_LabResultListResp();
            try
            {
                resp = _download_serialized_json_data<LAB_LabResultListResp>(SERVICE_URL + "ListLabResultsForPatient?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("patientId", patientId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Lab Results For Patient", ex.Message);
            }
            return resp;
        }

        public LAB_LabResultListResp ListPendingReviewResults(string hospitalId, string userId, string hashValue)
        {
            LAB_LabResultListResp resp = new LAB_LabResultListResp();
            try
            {
                resp = _download_serialized_json_data<LAB_LabResultListResp>(SERVICE_URL + "ListPendingReviewResults?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Pending Review Results", ex.Message);
            }
            return resp;
        }

        public RespInfo UpdateLabResultStatus(LAB_UpdateLabResultStatusReq updateLabResultStatusReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(updateLabResultStatusReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "UpdateLabResultStatus", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Lab Result Status", ex.Message);
            }
            return resp;
        }

        public LAB_LabPartnerListResp ListLabPartners(string hospitalId, string userId, string hashValue)
        {
            LAB_LabPartnerListResp resp = new LAB_LabPartnerListResp();
            try
            {
                resp = _download_serialized_json_data<LAB_LabPartnerListResp>(SERVICE_URL + "ListLabPartners?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Lab Partners", ex.Message);
            }
            return resp;
        }

        public LAB_LabPartnerResp GetLabPartner(string hospitalId, string userId, string labPartnerId, string hashValue)
        {
            LAB_LabPartnerResp resp = new LAB_LabPartnerResp();
            try
            {
                resp = _download_serialized_json_data<LAB_LabPartnerResp>(SERVICE_URL + "GetLabPartner?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("labPartnerId", labPartnerId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Lab Partner", ex.Message);
            }
            return resp;
        }

        public LAB_LabPartnerResp SaveLabPartner(LAB_SaveLabPartnerReq saveLabPartnerReq)
        {
            LAB_LabPartnerResp resp = new LAB_LabPartnerResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveLabPartnerReq, JsonSettings);
                resp = _serialized_json_data<LAB_LabPartnerResp>(SERVICE_URL + "SaveLabPartner", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Lab Partner", ex.Message);
            }
            return resp;
        }

        public LAB_OrderListResp SearchLabOrdersByMobile(LAB_SearchLabOrdersByMobileReq searchLabOrdersByMobileReq)
        {
            LAB_OrderListResp resp = new LAB_OrderListResp();
            try
            {
                string data = JsonConvert.SerializeObject(searchLabOrdersByMobileReq, JsonSettings);
                resp = _serialized_json_data<LAB_OrderListResp>(SERVICE_URL + "SearchLabOrdersByMobile", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Search Lab Orders By Mobile", ex.Message);
            }
            return resp;
        }

        public LAB_OrderResp ConfirmLabOrderClaim(LAB_ConfirmLabOrderClaimReq confirmLabOrderClaimReq)
        {
            LAB_OrderResp resp = new LAB_OrderResp();
            try
            {
                string data = JsonConvert.SerializeObject(confirmLabOrderClaimReq, JsonSettings);
                resp = _serialized_json_data<LAB_OrderResp>(SERVICE_URL + "ConfirmLabOrderClaim", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Confirm Lab Order Claim", ex.Message);
            }
            return resp;
        }

        public LAB_LabAttachmentResp CreateLabResultAttachment(LAB_CreateLabResultAttachmentReq createLabResultAttachmentReq)
        {
            LAB_LabAttachmentResp resp = new LAB_LabAttachmentResp();
            try
            {
                string data = JsonConvert.SerializeObject(createLabResultAttachmentReq, JsonSettings);
                resp = _serialized_json_data<LAB_LabAttachmentResp>(SERVICE_URL + "CreateLabResultAttachment", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Lab Result Attachment", ex.Message);
            }
            return resp;
        }

        public LAB_LabAttachmentListResp ListLabResultAttachments(string hospitalId, string userId, string resultId, string orderId, string hashValue)
        {
            LAB_LabAttachmentListResp resp = new LAB_LabAttachmentListResp();
            try
            {
                resp = _download_serialized_json_data<LAB_LabAttachmentListResp>(SERVICE_URL + "ListLabResultAttachments?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("resultId", resultId) + QueryParam("orderId", orderId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Lab Result Attachments", ex.Message);
            }
            return resp;
        }

        public LAB_LabAttachmentResp GetLabResultAttachment(string hospitalId, string userId, string attachmentId, string hashValue)
        {
            LAB_LabAttachmentResp resp = new LAB_LabAttachmentResp();
            try
            {
                resp = _download_serialized_json_data<LAB_LabAttachmentResp>(SERVICE_URL + "GetLabResultAttachment?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("attachmentId", attachmentId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Lab Result Attachment", ex.Message);
            }
            return resp;
        }

        public FileContentResp GetLabResultAttachmentContent(string hospitalId, string userId, string attachmentId, string hashValue)
        {
            FileContentResp resp = new FileContentResp();
            try
            {
                resp = _download_serialized_json_data<FileContentResp>(SERVICE_URL + "GetLabResultAttachmentContent?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("attachmentId", attachmentId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Lab Result Attachment Content", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Billing Methods
        public BIL_ConsultationChargeResp GetConsultationCharge(string hospitalId, string userId, string consultationId, string hashValue)
        {
            BIL_ConsultationChargeResp resp = new BIL_ConsultationChargeResp();
            try
            {
                resp = _download_serialized_json_data<BIL_ConsultationChargeResp>(SERVICE_URL + "GetConsultationCharge?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Consultation Charge", ex.Message);
            }
            return resp;
        }

        public BIL_ServiceChargeListResp ListServiceCharges(string hospitalId, string userId, string hashValue)
        {
            BIL_ServiceChargeListResp resp = new BIL_ServiceChargeListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_ServiceChargeListResp>(SERVICE_URL + "ListServiceCharges?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Service Charges", ex.Message);
            }
            return resp;
        }

        public BIL_ServiceChargeResp GetServiceCharge(string hospitalId, string userId, string serviceChargeId, string hashValue)
        {
            BIL_ServiceChargeResp resp = new BIL_ServiceChargeResp();
            try
            {
                resp = _download_serialized_json_data<BIL_ServiceChargeResp>(SERVICE_URL + "GetServiceCharge?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("serviceChargeId", serviceChargeId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Service Charge", ex.Message);
            }
            return resp;
        }

        public BIL_ServiceChargeListResp SearchServiceCharges(string hospitalId, string userId, string term, string hashValue)
        {
            BIL_ServiceChargeListResp resp = new BIL_ServiceChargeListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_ServiceChargeListResp>(SERVICE_URL + "SearchServiceCharges?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("term", term) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Search Service Charges", ex.Message);
            }
            return resp;
        }

        public BIL_ServiceChargeResp CreateServiceCharge(BIL_CreateServiceChargeReq createServiceChargeReq)
        {
            BIL_ServiceChargeResp resp = new BIL_ServiceChargeResp();
            try
            {
                string data = JsonConvert.SerializeObject(createServiceChargeReq, JsonSettings);
                resp = _serialized_json_data<BIL_ServiceChargeResp>(SERVICE_URL + "CreateServiceCharge", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Service Charge", ex.Message);
            }
            return resp;
        }

        public BIL_ServiceChargeListResp ListAllServiceCharges(string hospitalId, string userId, string hashValue)
        {
            BIL_ServiceChargeListResp resp = new BIL_ServiceChargeListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_ServiceChargeListResp>(SERVICE_URL + "ListAllServiceCharges?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List All Service Charges", ex.Message);
            }
            return resp;
        }

        public BIL_ServiceChargeResp UpdateServiceChargeActive(BIL_UpdateServiceChargeActiveReq updateServiceChargeActiveReq)
        {
            BIL_ServiceChargeResp resp = new BIL_ServiceChargeResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateServiceChargeActiveReq, JsonSettings);
                resp = _serialized_json_data<BIL_ServiceChargeResp>(SERVICE_URL + "UpdateServiceChargeActive", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Service Charge Active", ex.Message);
            }
            return resp;
        }

        public BIL_InvoiceResp CreateInvoice(BIL_CreateInvoiceReq createInvoiceReq)
        {
            BIL_InvoiceResp resp = new BIL_InvoiceResp();
            try
            {
                string data = JsonConvert.SerializeObject(createInvoiceReq, JsonSettings);
                resp = _serialized_json_data<BIL_InvoiceResp>(SERVICE_URL + "CreateInvoice", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Invoice", ex.Message);
            }
            return resp;
        }

        public BIL_InvoiceResp GetInvoiceForConsultation(string hospitalId, string userId, string consultationId, string hashValue)
        {
            BIL_InvoiceResp resp = new BIL_InvoiceResp();
            try
            {
                resp = _download_serialized_json_data<BIL_InvoiceResp>(SERVICE_URL + "GetInvoiceForConsultation?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("consultationId", consultationId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Invoice For Consultation", ex.Message);
            }
            return resp;
        }

        public BIL_InvoiceListResp ListInvoices(string hospitalId, string userId, string hashValue)
        {
            BIL_InvoiceListResp resp = new BIL_InvoiceListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_InvoiceListResp>(SERVICE_URL + "ListInvoices?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Invoices", ex.Message);
            }
            return resp;
        }

        public BIL_InvoiceResp GetInvoice(string hospitalId, string userId, string invoiceId, string hashValue)
        {
            BIL_InvoiceResp resp = new BIL_InvoiceResp();
            try
            {
                resp = _download_serialized_json_data<BIL_InvoiceResp>(SERVICE_URL + "GetInvoice?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("invoiceId", invoiceId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Invoice", ex.Message);
            }
            return resp;
        }

        public BIL_InvoiceResp UpdateInvoice(BIL_UpdateInvoiceReq updateInvoiceReq)
        {
            BIL_InvoiceResp resp = new BIL_InvoiceResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateInvoiceReq, JsonSettings);
                resp = _serialized_json_data<BIL_InvoiceResp>(SERVICE_URL + "UpdateInvoice", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Invoice", ex.Message);
            }
            return resp;
        }

        public BIL_CreditNoteResp CancelInvoice(BIL_CancelInvoiceReq cancelInvoiceReq)
        {
            BIL_CreditNoteResp resp = new BIL_CreditNoteResp();
            try
            {
                string data = JsonConvert.SerializeObject(cancelInvoiceReq, JsonSettings);
                resp = _serialized_json_data<BIL_CreditNoteResp>(SERVICE_URL + "CancelInvoice", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Cancel Invoice", ex.Message);
            }
            return resp;
        }

        public BIL_CreditNoteListResp ListCreditNotes(string hospitalId, string userId, string invoiceId, string hashValue)
        {
            BIL_CreditNoteListResp resp = new BIL_CreditNoteListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_CreditNoteListResp>(SERVICE_URL + "ListCreditNotes?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("invoiceId", invoiceId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Credit Notes", ex.Message);
            }
            return resp;
        }

        public BIL_LineItemResp CreateInvoiceLineItem(BIL_CreateInvoiceLineItemReq createInvoiceLineItemReq)
        {
            BIL_LineItemResp resp = new BIL_LineItemResp();
            try
            {
                string data = JsonConvert.SerializeObject(createInvoiceLineItemReq, JsonSettings);
                resp = _serialized_json_data<BIL_LineItemResp>(SERVICE_URL + "CreateInvoiceLineItem", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Invoice Line Item", ex.Message);
            }
            return resp;
        }

        public BIL_LineItemListResp ListInvoiceLineItems(string hospitalId, string userId, string invoiceId, string hashValue)
        {
            BIL_LineItemListResp resp = new BIL_LineItemListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_LineItemListResp>(SERVICE_URL + "ListInvoiceLineItems?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("invoiceId", invoiceId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Invoice Line Items", ex.Message);
            }
            return resp;
        }

        public RespInfo DeleteInvoiceLineItem(BIL_DeleteInvoiceLineItemReq deleteInvoiceLineItemReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(deleteInvoiceLineItemReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "DeleteInvoiceLineItem", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Delete Invoice Line Item", ex.Message);
            }
            return resp;
        }

        public BIL_InvoiceResp RecalculateInvoiceTotals(BIL_InvoiceIdReq recalculateInvoiceTotalsReq)
        {
            BIL_InvoiceResp resp = new BIL_InvoiceResp();
            try
            {
                string data = JsonConvert.SerializeObject(recalculateInvoiceTotalsReq, JsonSettings);
                resp = _serialized_json_data<BIL_InvoiceResp>(SERVICE_URL + "RecalculateInvoiceTotals", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Recalculate Invoice Totals", ex.Message);
            }
            return resp;
        }

        public BIL_PaymentResp CreatePayment(BIL_CreatePaymentReq createPaymentReq)
        {
            BIL_PaymentResp resp = new BIL_PaymentResp();
            try
            {
                string data = JsonConvert.SerializeObject(createPaymentReq, JsonSettings);
                resp = _serialized_json_data<BIL_PaymentResp>(SERVICE_URL + "CreatePayment", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Payment", ex.Message);
            }
            return resp;
        }

        public BIL_PaymentListResp ListPaymentsForInvoice(string hospitalId, string userId, string invoiceId, string hashValue)
        {
            BIL_PaymentListResp resp = new BIL_PaymentListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_PaymentListResp>(SERVICE_URL + "ListPaymentsForInvoice?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("invoiceId", invoiceId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Payments For Invoice", ex.Message);
            }
            return resp;
        }

        public BIL_PaymentListResp ListPayments(string hospitalId, string userId, string hashValue)
        {
            BIL_PaymentListResp resp = new BIL_PaymentListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_PaymentListResp>(SERVICE_URL + "ListPayments?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Payments", ex.Message);
            }
            return resp;
        }

        public RespInfo UpdatePaymentVoid(BIL_UpdatePaymentVoidReq updatePaymentVoidReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(updatePaymentVoidReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "UpdatePaymentVoid", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Payment Void", ex.Message);
            }
            return resp;
        }

        public BIL_ReceiptResp CreateReceipt(BIL_CreateReceiptReq createReceiptReq)
        {
            BIL_ReceiptResp resp = new BIL_ReceiptResp();
            try
            {
                string data = JsonConvert.SerializeObject(createReceiptReq, JsonSettings);
                resp = _serialized_json_data<BIL_ReceiptResp>(SERVICE_URL + "CreateReceipt", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Receipt", ex.Message);
            }
            return resp;
        }

        public BIL_ReceiptResp GetReceiptForInvoice(string hospitalId, string userId, string invoiceId, string hashValue)
        {
            BIL_ReceiptResp resp = new BIL_ReceiptResp();
            try
            {
                resp = _download_serialized_json_data<BIL_ReceiptResp>(SERVICE_URL + "GetReceiptForInvoice?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("invoiceId", invoiceId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Receipt For Invoice", ex.Message);
            }
            return resp;
        }

        public BIL_EligibilityVerificationListResp ListEligibilityVerifications(string hospitalId, string userId, string coverageId, string hashValue)
        {
            BIL_EligibilityVerificationListResp resp = new BIL_EligibilityVerificationListResp();
            try
            {
                resp = _download_serialized_json_data<BIL_EligibilityVerificationListResp>(SERVICE_URL + "ListEligibilityVerifications?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("coverageId", coverageId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Eligibility Verifications", ex.Message);
            }
            return resp;
        }

        public BIL_BillingSplitResp ApplyCoverageToInvoice(BIL_ApplyCoverageToInvoiceReq applyCoverageToInvoiceReq)
        {
            BIL_BillingSplitResp resp = new BIL_BillingSplitResp();
            try
            {
                string data = JsonConvert.SerializeObject(applyCoverageToInvoiceReq, JsonSettings);
                resp = _serialized_json_data<BIL_BillingSplitResp>(SERVICE_URL + "ApplyCoverageToInvoice", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Apply Coverage To Invoice", ex.Message);
            }
            return resp;
        }

        public BIL_BillingSplitResp EditInvoiceCoverageLine(BIL_EditInvoiceCoverageLineReq editInvoiceCoverageLineReq)
        {
            BIL_BillingSplitResp resp = new BIL_BillingSplitResp();
            try
            {
                string data = JsonConvert.SerializeObject(editInvoiceCoverageLineReq, JsonSettings);
                resp = _serialized_json_data<BIL_BillingSplitResp>(SERVICE_URL + "EditInvoiceCoverageLine", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Edit Invoice Coverage Line", ex.Message);
            }
            return resp;
        }

        public BIL_BillingSplitResp RemoveInvoiceCoverageLine(BIL_RemoveInvoiceCoverageLineReq removeInvoiceCoverageLineReq)
        {
            BIL_BillingSplitResp resp = new BIL_BillingSplitResp();
            try
            {
                string data = JsonConvert.SerializeObject(removeInvoiceCoverageLineReq, JsonSettings);
                resp = _serialized_json_data<BIL_BillingSplitResp>(SERVICE_URL + "RemoveInvoiceCoverageLine", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Remove Invoice Coverage Line", ex.Message);
            }
            return resp;
        }

        public BIL_BillingSplitResp GetInvoiceBillingSplit(string hospitalId, string userId, string invoiceId, string hashValue)
        {
            BIL_BillingSplitResp resp = new BIL_BillingSplitResp();
            try
            {
                resp = _download_serialized_json_data<BIL_BillingSplitResp>(SERVICE_URL + "GetInvoiceBillingSplit?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("invoiceId", invoiceId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Invoice Billing Split", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Inventory Methods
        public INV_InventoryItemListResp ListInventoryItems(string hospitalId, string userId, string includeInactive, string hashValue)
        {
            INV_InventoryItemListResp resp = new INV_InventoryItemListResp();
            try
            {
                resp = _download_serialized_json_data<INV_InventoryItemListResp>(SERVICE_URL + "ListInventoryItems?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("includeInactive", includeInactive) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Inventory Items", ex.Message);
            }
            return resp;
        }

        public INV_InventoryItemResp GetInventoryItem(string hospitalId, string userId, string inventoryItemId, string hashValue)
        {
            INV_InventoryItemResp resp = new INV_InventoryItemResp();
            try
            {
                resp = _download_serialized_json_data<INV_InventoryItemResp>(SERVICE_URL + "GetInventoryItem?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("inventoryItemId", inventoryItemId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Inventory Item", ex.Message);
            }
            return resp;
        }

        public INV_SavedInventoryItemResp SaveInventoryItem(INV_SaveInventoryItemReq saveInventoryItemReq)
        {
            INV_SavedInventoryItemResp resp = new INV_SavedInventoryItemResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveInventoryItemReq, JsonSettings);
                resp = _serialized_json_data<INV_SavedInventoryItemResp>(SERVICE_URL + "SaveInventoryItem", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Inventory Item", ex.Message);
            }
            return resp;
        }

        public INV_InventoryItemActiveResp UpdateInventoryItemActive(INV_UpdateInventoryItemActiveReq updateInventoryItemActiveReq)
        {
            INV_InventoryItemActiveResp resp = new INV_InventoryItemActiveResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateInventoryItemActiveReq, JsonSettings);
                resp = _serialized_json_data<INV_InventoryItemActiveResp>(SERVICE_URL + "UpdateInventoryItemActive", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Inventory Item Active", ex.Message);
            }
            return resp;
        }

        public INV_LinkedServiceListResp ListItemServices(string hospitalId, string userId, string inventoryItemId, string hashValue)
        {
            INV_LinkedServiceListResp resp = new INV_LinkedServiceListResp();
            try
            {
                resp = _download_serialized_json_data<INV_LinkedServiceListResp>(SERVICE_URL + "ListItemServices?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("inventoryItemId", inventoryItemId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Item Services", ex.Message);
            }
            return resp;
        }

        public INV_LinkedServiceListResp SaveItemServices(INV_SaveItemServicesReq saveItemServicesReq)
        {
            INV_LinkedServiceListResp resp = new INV_LinkedServiceListResp();
            try
            {
                string data = JsonConvert.SerializeObject(saveItemServicesReq, JsonSettings);
                resp = _serialized_json_data<INV_LinkedServiceListResp>(SERVICE_URL + "SaveItemServices", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Item Services", ex.Message);
            }
            return resp;
        }

        public INV_ServiceStockItemListResp ListItemsForService(string hospitalId, string userId, string serviceChargeId, string hashValue)
        {
            INV_ServiceStockItemListResp resp = new INV_ServiceStockItemListResp();
            try
            {
                resp = _download_serialized_json_data<INV_ServiceStockItemListResp>(SERVICE_URL + "ListItemsForService?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("serviceChargeId", serviceChargeId) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Items For Service", ex.Message);
            }
            return resp;
        }

        public INV_StockBatchListResp ListStockBatches(string hospitalId, string userId, string inventoryItemId, string includeEmpty, string hashValue)
        {
            INV_StockBatchListResp resp = new INV_StockBatchListResp();
            try
            {
                resp = _download_serialized_json_data<INV_StockBatchListResp>(SERVICE_URL + "ListStockBatches?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("inventoryItemId", inventoryItemId) + QueryParam("includeEmpty", includeEmpty) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Stock Batches", ex.Message);
            }
            return resp;
        }

        public INV_StockBatchListResp RecordStockReceipt(INV_RecordStockReceiptReq recordStockReceiptReq)
        {
            INV_StockBatchListResp resp = new INV_StockBatchListResp();
            try
            {
                string data = JsonConvert.SerializeObject(recordStockReceiptReq, JsonSettings);
                resp = _serialized_json_data<INV_StockBatchListResp>(SERVICE_URL + "RecordStockReceipt", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Record Stock Receipt", ex.Message);
            }
            return resp;
        }

        public RespInfo RecordStockAdjustment(INV_RecordStockAdjustmentReq recordStockAdjustmentReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(recordStockAdjustmentReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "RecordStockAdjustment", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Record Stock Adjustment", ex.Message);
            }
            return resp;
        }

        public INV_StockMovementListResp ListStockMovements(string hospitalId, string userId, string inventoryItemId, string take, string hashValue)
        {
            INV_StockMovementListResp resp = new INV_StockMovementListResp();
            try
            {
                resp = _download_serialized_json_data<INV_StockMovementListResp>(SERVICE_URL + "ListStockMovements?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("inventoryItemId", inventoryItemId) + QueryParam("take", take) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Stock Movements", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Report Methods
        public RPT_ConsultationVolumeResp ListConsultationVolumeReport(string hospitalId, string userId, string fromDate, string toDate, string hashValue)
        {
            RPT_ConsultationVolumeResp resp = new RPT_ConsultationVolumeResp();
            try
            {
                resp = _download_serialized_json_data<RPT_ConsultationVolumeResp>(SERVICE_URL + "ListConsultationVolumeReport?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("fromDate", fromDate) + QueryParam("toDate", toDate) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Consultation Volume Report", ex.Message);
            }
            return resp;
        }

        public RPT_DiagnosisFrequencyResp ListDiagnosisFrequencyReport(string hospitalId, string userId, string fromDate, string toDate, string hashValue)
        {
            RPT_DiagnosisFrequencyResp resp = new RPT_DiagnosisFrequencyResp();
            try
            {
                resp = _download_serialized_json_data<RPT_DiagnosisFrequencyResp>(SERVICE_URL + "ListDiagnosisFrequencyReport?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("fromDate", fromDate) + QueryParam("toDate", toDate) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Diagnosis Frequency Report", ex.Message);
            }
            return resp;
        }

        public RPT_PrescribingResp ListPrescribingReport(string hospitalId, string userId, string fromDate, string toDate, string hashValue)
        {
            RPT_PrescribingResp resp = new RPT_PrescribingResp();
            try
            {
                resp = _download_serialized_json_data<RPT_PrescribingResp>(SERVICE_URL + "ListPrescribingReport?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("fromDate", fromDate) + QueryParam("toDate", toDate) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Prescribing Report", ex.Message);
            }
            return resp;
        }

        public RPT_NoShowReportResp ListNoShowReport(string hospitalId, string userId, string fromDate, string toDate, string hashValue)
        {
            RPT_NoShowReportResp resp = new RPT_NoShowReportResp();
            try
            {
                resp = _download_serialized_json_data<RPT_NoShowReportResp>(SERVICE_URL + "ListNoShowReport?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("fromDate", fromDate) + QueryParam("toDate", toDate) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List No Show Report", ex.Message);
            }
            return resp;
        }

        public RPT_RevenueResp ListRevenueReport(string hospitalId, string userId, string fromDate, string toDate, string hashValue)
        {
            RPT_RevenueResp resp = new RPT_RevenueResp();
            try
            {
                resp = _download_serialized_json_data<RPT_RevenueResp>(SERVICE_URL + "ListRevenueReport?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("fromDate", fromDate) + QueryParam("toDate", toDate) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Revenue Report", ex.Message);
            }
            return resp;
        }

        public RPT_RevenueByServiceResp ListRevenueByServiceReport(string hospitalId, string userId, string fromDate, string toDate, string hashValue)
        {
            RPT_RevenueByServiceResp resp = new RPT_RevenueByServiceResp();
            try
            {
                resp = _download_serialized_json_data<RPT_RevenueByServiceResp>(SERVICE_URL + "ListRevenueByServiceReport?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("fromDate", fromDate) + QueryParam("toDate", toDate) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Revenue By Service Report", ex.Message);
            }
            return resp;
        }

        public RPT_DayEndSummaryResp GetDayEndSummary(string hospitalId, string userId, string date, string hashValue)
        {
            RPT_DayEndSummaryResp resp = new RPT_DayEndSummaryResp();
            try
            {
                resp = _download_serialized_json_data<RPT_DayEndSummaryResp>(SERVICE_URL + "GetDayEndSummary?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("date", date) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Day End Summary", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Notification Methods
        public MSG_MessageTemplateListResp ListMessageTemplates(string hospitalId, string userId, string hashValue)
        {
            MSG_MessageTemplateListResp resp = new MSG_MessageTemplateListResp();
            try
            {
                resp = _download_serialized_json_data<MSG_MessageTemplateListResp>(SERVICE_URL + "ListMessageTemplates?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Message Templates", ex.Message);
            }
            return resp;
        }

        public RespInfo UpdateMessageTemplateStatus(MSG_UpdateMessageTemplateStatusReq updateMessageTemplateStatusReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(updateMessageTemplateStatusReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "UpdateMessageTemplateStatus", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Message Template Status", ex.Message);
            }
            return resp;
        }

        public MSG_MessageTemplateResp UpdateMessageTemplate(MSG_UpdateMessageTemplateReq updateMessageTemplateReq)
        {
            MSG_MessageTemplateResp resp = new MSG_MessageTemplateResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateMessageTemplateReq, JsonSettings);
                resp = _serialized_json_data<MSG_MessageTemplateResp>(SERVICE_URL + "UpdateMessageTemplate", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update Message Template", ex.Message);
            }
            return resp;
        }

        public MSG_NotificationConfigListResp GetNotificationConfig(string hospitalId, string userId, string hashValue)
        {
            MSG_NotificationConfigListResp resp = new MSG_NotificationConfigListResp();
            try
            {
                resp = _download_serialized_json_data<MSG_NotificationConfigListResp>(SERVICE_URL + "GetNotificationConfig?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Notification Config", ex.Message);
            }
            return resp;
        }

        public RespInfo SaveNotificationConfig(MSG_SaveNotificationConfigReq saveNotificationConfigReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(saveNotificationConfigReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "SaveNotificationConfig", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Save Notification Config", ex.Message);
            }
            return resp;
        }

        public MSG_QueuedMessageResp CreateQueuedMessage(MSG_CreateQueuedMessageReq createQueuedMessageReq)
        {
            MSG_QueuedMessageResp resp = new MSG_QueuedMessageResp();
            try
            {
                string data = JsonConvert.SerializeObject(createQueuedMessageReq, JsonSettings);
                resp = _serialized_json_data<MSG_QueuedMessageResp>(SERVICE_URL + "CreateQueuedMessage", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Create Queued Message", ex.Message);
            }
            return resp;
        }

        public RespInfo SubmitQueuedMessages(MSG_SubmitQueuedMessagesReq submitQueuedMessagesReq)
        {
            RespInfo resp = new RespInfo();
            try
            {
                string data = JsonConvert.SerializeObject(submitQueuedMessagesReq, JsonSettings);
                resp = _serialized_json_data<RespInfo>(SERVICE_URL + "SubmitQueuedMessages", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Submit Queued Messages", ex.Message);
            }
            return resp;
        }

        public MSG_QueuedMessageListResp ListMessageLog(string hospitalId, string userId, string hashValue)
        {
            MSG_QueuedMessageListResp resp = new MSG_QueuedMessageListResp();
            try
            {
                resp = _download_serialized_json_data<MSG_QueuedMessageListResp>(SERVICE_URL + "ListMessageLog?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Message Log", ex.Message);
            }
            return resp;
        }

        public MSG_MessageStatsResp GetMessageStats(string hospitalId, string userId, string hashValue)
        {
            MSG_MessageStatsResp resp = new MSG_MessageStatsResp();
            try
            {
                resp = _download_serialized_json_data<MSG_MessageStatsResp>(SERVICE_URL + "GetMessageStats?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get Message Stats", ex.Message);
            }
            return resp;
        }

        public MSG_UserNotificationListResp ListUserNotifications(string hospitalId, string userId, string take, string hashValue)
        {
            MSG_UserNotificationListResp resp = new MSG_UserNotificationListResp();
            try
            {
                resp = _download_serialized_json_data<MSG_UserNotificationListResp>(SERVICE_URL + "ListUserNotifications?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("take", take) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List User Notifications", ex.Message);
            }
            return resp;
        }

        public CountResp GetUserNotificationUnreadCount(string hospitalId, string userId, string hashValue)
        {
            CountResp resp = new CountResp();
            try
            {
                resp = _download_serialized_json_data<CountResp>(SERVICE_URL + "GetUserNotificationUnreadCount?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Get User Notification Unread Count", ex.Message);
            }
            return resp;
        }

        public MSG_UserNotificationResp UpdateUserNotificationRead(MSG_UpdateUserNotificationReadReq updateUserNotificationReadReq)
        {
            MSG_UserNotificationResp resp = new MSG_UserNotificationResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateUserNotificationReadReq, JsonSettings);
                resp = _serialized_json_data<MSG_UserNotificationResp>(SERVICE_URL + "UpdateUserNotificationRead", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update User Notification Read", ex.Message);
            }
            return resp;
        }

        public CountResp UpdateUserNotificationsReadAll(MSG_UpdateUserNotificationsReadAllReq updateUserNotificationsReadAllReq)
        {
            CountResp resp = new CountResp();
            try
            {
                string data = JsonConvert.SerializeObject(updateUserNotificationsReadAllReq, JsonSettings);
                resp = _serialized_json_data<CountResp>(SERVICE_URL + "UpdateUserNotificationsReadAll", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Update User Notifications Read All", ex.Message);
            }
            return resp;
        }

        #endregion

        #region Audit & Export Methods
        public SEC_AuditEntryResp RecordAuditEntry(SEC_RecordAuditEntryReq recordAuditEntryReq)
        {
            SEC_AuditEntryResp resp = new SEC_AuditEntryResp();
            try
            {
                string data = JsonConvert.SerializeObject(recordAuditEntryReq, JsonSettings);
                resp = _serialized_json_data<SEC_AuditEntryResp>(SERVICE_URL + "RecordAuditEntry", data);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to Record Audit Entry", ex.Message);
            }
            return resp;
        }

        public SEC_AuditEntryListResp ListAuditLogs(string hospitalId, string userId, string hashValue)
        {
            SEC_AuditEntryListResp resp = new SEC_AuditEntryListResp();
            try
            {
                resp = _download_serialized_json_data<SEC_AuditEntryListResp>(SERVICE_URL + "ListAuditLogs?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Audit Logs", ex.Message);
            }
            return resp;
        }

        public SEC_AuditEntryListResp ListAuditLogsFiltered(string hospitalId, string userId, string action, string entityType, string filterUserId, string fromDate, string toDate, string top, string hashValue)
        {
            SEC_AuditEntryListResp resp = new SEC_AuditEntryListResp();
            try
            {
                resp = _download_serialized_json_data<SEC_AuditEntryListResp>(SERVICE_URL + "ListAuditLogsFiltered?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("action", action) + QueryParam("entityType", entityType) + QueryParam("filterUserId", filterUserId) + QueryParam("fromDate", fromDate) + QueryParam("toDate", toDate) + QueryParam("top", top) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Audit Logs Filtered", ex.Message);
            }
            return resp;
        }

        public SEC_ExportTableListResp ListExportTables(string hospitalId, string userId, string hashValue)
        {
            SEC_ExportTableListResp resp = new SEC_ExportTableListResp();
            try
            {
                resp = _download_serialized_json_data<SEC_ExportTableListResp>(SERVICE_URL + "ListExportTables?HospitalId=" + hospitalId + "&UserId=" + userId + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Export Tables", ex.Message);
            }
            return resp;
        }

        public FileContentResp ListExportData(string hospitalId, string userId, string tableKey, string hashValue)
        {
            FileContentResp resp = new FileContentResp();
            try
            {
                resp = _download_serialized_json_data<FileContentResp>(SERVICE_URL + "ListExportData?HospitalId=" + hospitalId + "&UserId=" + userId + QueryParam("tableKey", tableKey) + "&hashValue=" + hashValue);
            }
            catch (Exception ex)
            {
                AppSession.Toast("danger", "Failed to List Export Data", ex.Message);
            }
            return resp;
        }

        #endregion
    }
}
