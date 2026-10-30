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

namespace Energy7.StationSurvey.Service
{
    public class ZoneService : IZoneService
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ISiteService _siteService;

        public ZoneService(IConfiguration config, IHttpContextAccessor httpContextAccessor, ISiteService siteService)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _siteService = siteService;
        }

        public List<Zone> GetAll()
        {
            var mZones = new List<Zone>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("Zone/GetAllZones").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var zones = JsonConvert.DeserializeObject<List<Zone>>(jsonString);

                    if (IsRestrictedRole(loginUser.RoleId))
                    {
                        var sites = _siteService.GetAll();
                        if (zones?.Count > 0 && sites?.Count > 0)
                            mZones = zones.Where(z => sites.Any(s => s.ZoneId == z.Id)).ToList();
                    }
                    else
                    {
                        mZones = zones ?? new List<Zone>();
                    }

                    mZones.Insert(0, new Zone { Id = 0, Name = "Select Zone" });
                }
            }
            catch (Exception ex)
            {
                throw new Exception("ZoneService.GetAll failed", ex);
            }

            return mZones;
        }

        public List<Zone> GetAllZones()
        {
            var mZones = new List<Zone>();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("Zone/GetAllZones").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var zones = JsonConvert.DeserializeObject<List<Zone>>(jsonString);

                    if (IsRestrictedRole(loginUser.RoleId))
                    {
                        var sites = _siteService.GetAll();
                        if (zones?.Count > 0 && sites?.Count > 0)
                            mZones = zones.Where(z => sites.Any(s => s.ZoneId == z.Id)).ToList();
                    }
                    else
                    {
                        mZones = zones ?? new List<Zone>();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("ZoneService.GetAllZones failed", ex);
            }

            return mZones;
        }

        public Zone GetZoneById(int id)
        {
            var mZone = new Zone();
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"Zone/GetZoneById/{id}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    mZone = JsonConvert.DeserializeObject<Zone>(jsonString) ?? new Zone();
            }
            catch { /* silent — returns empty Zone */ }

            return mZone;
        }

        // ── Helper ────────────────────────────────────────────────────────────
        private static bool IsRestrictedRole(int roleId)
            => roleId == (int)Utility.Utility.Role.User
            || roleId == (int)Utility.Utility.Role.DivisionalAdmin
            || roleId == (int)Utility.Utility.Role.ZonalAdmin;
    }
}