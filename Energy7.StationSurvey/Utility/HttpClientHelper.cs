using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// Wraps HttpClient for all API calls.
    /// Renamed from HttpClientFactory to avoid conflict with built-in IHttpClientFactory.
    /// </summary>
    public class HttpClientHelper : IDisposable
    {
        public HttpClient Client { get; private set; }

        public HttpClientHelper(
            IConfiguration config,
            IHttpContextAccessor httpContextAccessor,
            string? baseUrl   = null,
            string? mediaType = null,
            string? token     = null)
        {
            var httpContext = httpContextAccessor.HttpContext;

            var handler = new HttpClientHandler();

            // Ignore cert errors on HTTPS (dev only — remove in production)
            if (httpContext?.Request.IsHttps == true)
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            Client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(80) };

            // Base URL from appsettings.json
            if (baseUrl == null)
            {
                var url = httpContext?.Request.IsHttps == true
    ? config["APIBaseUrlSecure"] ?? config["APIBaseUrl"]
    : config["APIBaseUrl"];

                Client.BaseAddress = new Uri(
                    url ?? throw new InvalidOperationException("APIBaseUrl missing in appsettings.json"));
            }
            else
            {
                Client.BaseAddress = new Uri(baseUrl);
            }

            // Accept header
            Client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(mediaType ?? "application/json"));

            // Bearer token
            if (token != null)
                Client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

            // User headers from session
            if (httpContext != null)
            {
                var loginUser = ClsHttpContent.GetLoginUser(httpContext);
                if (loginUser != null)
                {
                    Client.DefaultRequestHeaders.Add("UserId",  loginUser.Id.ToString());
                    Client.DefaultRequestHeaders.Add("RoleId",  loginUser.RoleId.ToString());
                }
            }
        }

        public async Task<HttpResponseMessage> PostAsync(string endpoint, string json)
            => await Client.PostAsync(endpoint,
                   new StringContent(json, Encoding.UTF8, "application/json"));

        public async Task<HttpResponseMessage> GetAsync(string endpoint)
            => await Client.GetAsync(endpoint);

        public void Dispose()
        {
            Client?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
