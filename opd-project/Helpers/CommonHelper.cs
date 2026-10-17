using Newtonsoft.Json;
using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;

namespace OpdProject.Helpers
{
    public class CommonHelper
    {
        private static readonly string ServiceUrl = ConfigurationManager.AppSettings["SERVICE_URL"]?.TrimEnd('/') + "/";
        private static readonly string VendorId = ConfigurationManager.AppSettings["VendorId"] ?? "WOEIY1654";
        private static readonly string VendorPwd = ConfigurationManager.AppSettings["VendorPwd"] ?? "";

        public T _download_serialized_json_data<T>(string endpoint) where T : new()
        {
            string fullUrl = CombineUrl(ServiceUrl, endpoint);

            try
            {
                using (var w = new WebClient())
                {
                    w.Encoding = Encoding.UTF8;
                    w.Headers[HttpRequestHeader.ContentType] = "application/json";

                    w.Headers.Add("VenderId", VendorId);
                    w.Headers.Add("VenderPwd", VendorPwd);

                    string jsonData = w.DownloadString(fullUrl);

                    return !string.IsNullOrWhiteSpace(jsonData)
                        ? JsonConvert.DeserializeObject<T>(jsonData)
                        : new T();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"API GET Error ({fullUrl}): {ex.Message}");
                throw;
            }
        }
        public T _serialized_json_data<T>(string endpoint, string indata) where T : new()
        {
            string fullUrl = CombineUrl(ServiceUrl, endpoint);

            try
            {
                using (var w = new WebClient())
                {
                    w.Encoding = Encoding.UTF8;
                    w.Headers[HttpRequestHeader.ContentType] = "application/json";

                    w.Headers.Add("VenderId", VendorId);
                    w.Headers.Add("VenderPwd", VendorPwd);

                    string jsonData = w.UploadString(fullUrl, "POST", indata ?? string.Empty);

                    return !string.IsNullOrWhiteSpace(jsonData)
                        ? JsonConvert.DeserializeObject<T>(jsonData)
                        : new T();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"API POST Error ({fullUrl}): {ex.Message}");
                throw;
            }
        }

        private static string CombineUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return relativeUrl ?? string.Empty;
            if (string.IsNullOrEmpty(relativeUrl)) return baseUrl;

            if (relativeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return relativeUrl;
            }

            return baseUrl + relativeUrl.TrimStart('/');
        }
    }

    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string UserId { get; set; }
        public string HospitalId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Roles { get; set; }
        public string SpecialtyId { get; set; }
        public string LabPartnerId { get; set; }
    }
}