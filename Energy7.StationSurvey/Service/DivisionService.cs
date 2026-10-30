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
using System.Text;

namespace Energy7.StationSurvey.Service
{
    public class DivisionService : IDivisionService
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ISiteService _siteService;

        public DivisionService(IConfiguration config, IHttpContextAccessor httpContextAccessor, ISiteService siteService)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _siteService = siteService;
        }

        public Division Get(int id)
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"Division/GetDivisionById/{id}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    return JsonConvert.DeserializeObject<Division>(jsonString) ?? new Division();
            }
            catch (Exception ex)
            {
                throw new Exception("DivisionService.Get failed", ex);
            }
            return new Division();
        }

        public List<Division> GetAll()
        {
            var mDivisions = new List<Division>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("Division/GetAllDivisions").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var divisions = JsonConvert.DeserializeObject<List<Division>>(jsonString) ?? new();

                    if (IsRestrictedRole(loginUser.RoleId))
                    {
                        var sites = _siteService.GetAll();
                        if (divisions.Count > 0 && sites?.Count > 0)
                            mDivisions = divisions.Where(d => sites.Any(s => s.DivisionId == d.Id)).ToList();
                    }
                    else
                    {
                        mDivisions = divisions;
                    }

                    mDivisions.Insert(0, new Division { Id = 0, Name = "Select Division" });
                }
            }
            catch (Exception ex)
            {
                throw new Exception("DivisionService.GetAll failed", ex);
            }
            return mDivisions;
        }

        public List<Division> GetAllDivisions()
        {
            var mDivisions = new List<Division>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("Division/GetAllDivisions").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var divisions = JsonConvert.DeserializeObject<List<Division>>(jsonString) ?? new();

                    if (IsRestrictedRole(loginUser.RoleId))
                    {
                        var sites = _siteService.GetAll();
                        if (divisions.Count > 0 && sites?.Count > 0)
                            mDivisions = divisions.Where(d => sites.Any(s => s.DivisionId == d.Id)).ToList();
                    }
                    else
                    {
                        mDivisions = divisions;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("DivisionService.GetAllDivisions failed", ex);
            }
            return mDivisions;
        }

        public List<Division> GetByZoneId(int zoneId)
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"Division/GetAllDivisionsByZoneId/{zoneId}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    return JsonConvert.DeserializeObject<List<Division>>(jsonString) ?? new();
            }
            catch (Exception ex)
            {
                throw new Exception("DivisionService.GetByZoneId failed", ex);
            }
            return new List<Division>();
        }

        public List<Division> GetDivisionList(DivisionLister mDivisionLister)
        {
            var divisions = new List<Division>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            mDivisionLister.Pager.Take = -1;

            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var body = new StringContent(JsonConvert.SerializeObject(mDivisionLister), Encoding.UTF8, "application/json");
                var response = hcf.Client.PostAsync("Division/GetAllDivisionLister", body).Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonString = response.Content.ReadAsStringAsync().Result;
                    mDivisionLister = JsonConvert.DeserializeObject<DivisionLister>(jsonString)!;

                    if (mDivisionLister?.mDivisions?.Count > 0)
                    {
                        if (IsRestrictedRole(loginUser.RoleId))
                        {
                            var sites = _siteService.GetAll();
                            if (sites?.Count > 0)
                                divisions = mDivisionLister.mDivisions
                                    .Where(d => sites.Any(s => s.DivisionId == d.Id))
                                    .ToList();
                        }
                        else
                        {
                            divisions = mDivisionLister.mDivisions;
                        }
                    }
                }
            }
            catch { /* silent */ }

            return divisions;
        }

        // ── Helper ────────────────────────────────────────────────────────────
        private static bool IsRestrictedRole(int roleId)
            => roleId == (int)Utility.Utility.Role.User
            || roleId == (int)Utility.Utility.Role.DivisionalAdmin
            || roleId == (int)Utility.Utility.Role.ZonalAdmin;
    }
}