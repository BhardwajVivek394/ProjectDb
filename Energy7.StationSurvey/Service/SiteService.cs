using Domain;
using Energy7.StationSurvey.Interface;
using Energy7.StationSurvey.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Energy7.StationSurvey.Service
{
    public class SiteService : ISiteService
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SiteService(IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
        }

        // ── GetAll ────────────────────────────────────────────────────────────
        public List<Site> GetAll()
        {
            var sites = new List<Site>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            try
            {
                var endpoint = IsRestrictedRole(loginUser!.RoleId)
                    ? $"Site/GetSiteByUserId/UserId/{loginUser.Id}"
                    : "Site/GetAll";

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var response = hcf.Client.GetAsync(endpoint).Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    sites = JsonConvert.DeserializeObject<List<Site>>(jsonString) ?? new();
            }
            catch (Exception ex)
            {
                throw new Exception("SiteService.GetAll failed", ex);
            }

            return ApplyNameFormat(sites);
        }

        // ── Get by Id ─────────────────────────────────────────────────────────
        public Site Get(int id)
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"Site/GetSiteById/{id}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    return JsonConvert.DeserializeObject<Site>(jsonString) ?? new Site();
            }
            catch (Exception ex)
            {
                throw new Exception("SiteService.Get failed", ex);
            }
            return new Site();
        }

        // ── Get by name ───────────────────────────────────────────────────────
        public Site GetBy(string siteName)
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"Site/SiteName/{siteName}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    return JsonConvert.DeserializeObject<Site>(jsonString) ?? new Site();
            }
            catch (Exception ex)
            {
                throw new Exception("SiteService.GetBy(name) failed", ex);
            }
            return new Site();
        }

        // ── Get by zone + division ────────────────────────────────────────────
        public List<Site> GetBy(int zoneId, int divisionId)
        {
            var sites = new List<Site>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"Site/GetSites/{zoneId}/{divisionId}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    sites = JsonConvert.DeserializeObject<List<Site>>(jsonString) ?? new();

                    if (IsRestrictedRole(loginUser.RoleId))
                        sites = FilterByUserSites(sites);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("SiteService.GetBy(zoneId, divisionId) failed", ex);
            }
            return sites;
        }

        // ── Get by division ───────────────────────────────────────────────────
        public List<Site> GetBy(int divisionId)
        {
            var sites = new List<Site>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"Site/GetSites/DivisionId/{divisionId}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    sites = JsonConvert.DeserializeObject<List<Site>>(jsonString) ?? new();

                    if (IsRestrictedRole(loginUser.RoleId))
                        sites = FilterByUserSites(sites);

                    ApplyNameFormat(sites);
                }
            }
            catch { /* silent */ }

            return sites;
        }

        // ── GetSite (all, no filter) ──────────────────────────────────────────
        public List<Site> GetSite()
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("Site/GetAll").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    return JsonConvert.DeserializeObject<List<Site>>(jsonString) ?? new();
            }
            catch (Exception ex)
            {
                throw new Exception("SiteService.GetSite failed", ex);
            }
            return new List<Site>();
        }

        // ── GetDataLoggerSite ─────────────────────────────────────────────────
        public List<Site> GetDataLoggerSite()
        {
            var sites = new List<Site>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("Site/GetDataLoggerSite").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    sites = JsonConvert.DeserializeObject<List<Site>>(jsonString) ?? new();
            }
            catch (Exception ex)
            {
                throw new Exception("SiteService.GetDataLoggerSite failed", ex);
            }

            if (IsRestrictedRole(loginUser!.RoleId))
                sites = FilterByUserSites(sites);

            return ApplyNameFormat(sites);
        }

        // ── GetSiteLister ─────────────────────────────────────────────────────
        public List<Site> GetSiteLister(SiteLister mSiteLister)
        {
            var mSites = new List<Site>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = loginUser!.Id;
            mSiteLister.SearchCriteria.RoleId = loginUser.RoleId;

            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var body = new StringContent(JsonConvert.SerializeObject(mSiteLister), Encoding.UTF8, "application/json");
                var response = hcf.Client.PostAsync("Site/GetAllSiteLister", body).Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonString = response.Content.ReadAsStringAsync().Result;
                    mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString)!;

                    if (mSiteLister?.mSites?.Count > 0)
                        mSites = mSiteLister.mSites;
                }
            }
            catch { /* silent */ }

            return mSites;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static bool IsRestrictedRole(int roleId)
            => roleId == (int)Utility.Utility.Role.User
            || roleId == (int)Utility.Utility.Role.DivisionalAdmin
            || roleId == (int)Utility.Utility.Role.ZonalAdmin;

        /// <summary>Filter sites down to only those assigned to the logged-in user.</summary>
        private List<Site> FilterByUserSites(List<Site> sites)
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"Site/GetSiteByUserId/UserId/{loginUser.Id}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var userSites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
                    if (userSites?.Count > 0)
                    {
                        var siteIds = userSites.Select(x => x.Id).ToHashSet();
                        sites = sites.Where(x => siteIds.Contains(x.Id)).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("SiteService.FilterByUserSites failed", ex);
            }
            return sites;
        }

        /// <summary>Sort + append station code to name. Returns the same list.</summary>
        private static List<Site> ApplyNameFormat(List<Site> sites)
        {
            if (sites?.Count > 0)
            {
                sites = sites.OrderBy(s => s.Name).ToList();
                sites.ForEach(x =>
                {
                    if (!string.IsNullOrEmpty(x.StationCode)) x.Name = $"{x.Name} - {x.StationCode}";
                });
            }
            return sites ?? new();
        }
    }
}