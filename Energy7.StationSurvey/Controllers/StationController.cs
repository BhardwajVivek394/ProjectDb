using Domain;
using Domain.Dto;
using E7MRIWeb.Service;
using Energy7.StationSurvey.Interface;
using Energy7.StationSurvey.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Energy7.StationSurvey.Controllers
{
    [Authenticate]
    public class StationController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly ISiteService _siteService;
        private readonly IAssetTypeService _assetTypeService;

        public StationController(IConfiguration config, IHttpContextAccessor httpContextAccessor, IZoneService zoneService, IDivisionService divisionService, ISiteService siteService, IAssetTypeService assetTypeService)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _siteService = siteService;
            _assetTypeService = assetTypeService;
        }

        // GET: /Station
        public IActionResult Index()
        {
            LoadZones();
            LoadDivisions();
            LoadStations();
            return View();
        }

        // ── AJAX: divisions by zone ───────────────────────────────────────────
        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            var divisions = new List<Division>();
            try
            {
                divisions = zoneId > 0
                    ? _divisionService.GetByZoneId(zoneId)
                    : _divisionService.GetAllDivisions();
            }
            catch (Exception ex) { ViewBag.Error = ex.Message; }
            return Json(divisions);
        }

        // ── AJAX: stations by division ────────────────────────────────────────
        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var sites = new List<Site>();
            try
            {
                sites = divisionId > 0
                    ? _siteService.GetBy(divisionId)
                    : _siteService.GetAll();
            }
            catch (Exception ex) { ViewBag.Error = ex.Message; }
            return Json(sites);
        }

        public async Task<IActionResult> GetCardLineBySiteId(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = await hcf.Client.GetAsync($"CardLine/SiteId/{siteId}");

                if (response.StatusCode != HttpStatusCode.OK)
                    return Json(new CardLineStatsDto());

                var json = await response.Content.ReadAsStringAsync();
                var cardLines = JsonConvert.DeserializeObject<List<CardLine>>(json) ?? new();

                // ── Total Clusters: unique ClusterId ─────────────────────────────
                var totalClusters = cardLines
                    .Where(x => x.ClusterId.HasValue)
                    .Select(x => x.ClusterId!.Value)
                    .Distinct()
                    .Count();

                // ── Total Assets: unique AssetId ─────────────────────────────────
                var totalAssets = cardLines
                    .Where(x => x.AssetId.HasValue)
                    .Select(x => x.AssetId!.Value)
                    .Distinct()
                    .Count();

                // ── Total Data Points: unique (AssetId, AttributeId) pairs ───────
                var totalDataPoints = cardLines
                    .Where(x => x.AssetId.HasValue && x.AttributeId.HasValue)
                    .Select(x => new { x.AssetId, x.AttributeId })
                    .Distinct()
                    .Count();

                // ── Total Locations: unique part after "/" in ClusterName ────────
                // e.g. "cluster_15/lc9" → "lc9"
                var totalLocations = cardLines
                    .Where(x => !string.IsNullOrWhiteSpace(x.ClusterName) && x.ClusterName.Contains('/'))
                    .Select(x => x.ClusterName.Split('/').Last().Trim().ToLowerInvariant())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Distinct()
                    .Count();

                // ── Asset breakdown by AssetTypeName (e.g. "TRA:6") ──────────────
                var assetBreakdown = string.Join(" · ",
                    cardLines
                        .Where(x => !string.IsNullOrEmpty(x.AssetTypeName) && x.AssetId.HasValue)
                        .GroupBy(x => x.AssetTypeName)
                        .Select(g => $"{g.Key.Substring(0, Math.Min(3, g.Key.Length))}:{g.Select(r => r.AssetId).Distinct().Count()}")
                );

                // ── Per-type asset counts (from CardLine) ─────────────────────────
                var assetTypeCounts = cardLines
                    .Where(x => !string.IsNullOrEmpty(x.AssetTypeName) && x.AssetId.HasValue)
                    .GroupBy(x => new { x.AssetTypeId, x.AssetTypeName })
                    .Select(g => new AssetTypeCountDto
                    {
                        AssetTypeId = g.Key.AssetTypeId,
                        AssetTypeName = g.Key.AssetTypeName,
                        Count = g.Select(r => r.AssetId).Distinct().Count(),
                        DataPoints = g.Where(r => r.AttributeId.HasValue)
                                         .Select(r => new { r.AssetId, r.AttributeId })
                                         .Distinct()
                                         .Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .ToList();

                var result = new CardLineStatsDto
                {
                    TotalClusters = totalClusters,
                    TotalAssets = totalAssets,
                    TotalDataPoints = totalDataPoints,
                    TotalLocations = totalLocations,
                    AssetBreakdown = assetBreakdown,
                    AssetTypeCounts = assetTypeCounts,   // ← NEW
                    Records = cardLines
                };

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }        // ── Helpers ───────────────────────────────────────────────────────────
        private void LoadZones()
        {
            try
            {
                var zones = _zoneService.GetAllZones() ?? new List<Zone>();
                zones.Insert(0, new Zone { Id = 0, Name = "Select Zone" });
                ViewBag.Zones = new SelectList(zones, "Id", "Name");
            }
            catch { ViewBag.Zones = new SelectList(new List<Zone>(), "Id", "Name"); }
        }

        private void LoadDivisions()
        {
            try
            {
                var divisions = _divisionService.GetAllDivisions() ?? new List<Division>();
                divisions.Insert(0, new Division { Id = 0, Name = "Select Division" });
                ViewBag.Divisions = new SelectList(divisions, "Id", "Name");
            }
            catch { ViewBag.Divisions = new SelectList(new List<Division>(), "Id", "Name"); }
        }

        private void LoadStations()
        {
            try
            {
                var sites = _siteService.GetAll() ?? new List<Site>();
                sites.Insert(0, new Site { Id = 0, Name = "Select Station" });
                ViewBag.Sites = new SelectList(sites, "Id", "Name");
            }
            catch { ViewBag.Sites = new SelectList(new List<Site>(), "Id", "Name"); }
        }

        public JsonResult GetAllAssetType()
        {
            List<AssetType> mAssetTypes = new List<AssetType>();
            try
            {
                mAssetTypes = _assetTypeService.GetAll();
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetTypes);
        }
        [HttpPost]
        public async Task<JsonResult> GetAllAssetTypeById([FromBody] List<int> siteIds)
        {
            var mAssetType = new List<AssetType>();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);

                var jsonStr = JsonConvert.SerializeObject(siteIds);
                using var str = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                var response = await hcf.Client.PostAsync("AssetType/GetAllAssetTypeBySiteId", str);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    mAssetType = JsonConvert.DeserializeObject<List<AssetType>>(jsonString) ?? new List<AssetType>();
                }
                else
                {
                    return Json(new { error = "Internal server error!" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
            return Json(mAssetType);
        }

        // ── AJAX: asset attributes by asset type id ───────────────────────────
        [HttpGet]
        public async Task<JsonResult> GetAssetAttributesByTypeId(int assetTypeId)
        {
            var attributes = new List<AssetAttribute>();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = await hcf.Client.GetAsync($"AssetAttribute/GetAllAssetAttributeByAssetTypeId/{assetTypeId}");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    attributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(json) ?? new();
                }
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
            return Json(attributes);
        }

        // ── AJAX: save new asset ──────────────────────────────────────────────
        [HttpPost]
        public async Task<JsonResult> SaveAsset([FromBody] Asset mAsset)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                mAsset.CreatedBy = loginUser.Id;
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var jsonStr = JsonConvert.SerializeObject(mAsset);
                using var str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                var response = await hcf.Client.PostAsync("Asset/SaveAsset", str);
                if (response.StatusCode != HttpStatusCode.OK)
                    return Json(new { error = "Internal server error." });
                var jsonString = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                if (apiResponse != null && apiResponse.IsSuccess)
                    return Json(new { success = true });
                return Json(new { error = "Error saving asset." });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public JsonResult GetZonesJson()
        {
            try
            {
                var zones = _zoneService.GetAllZones() ?? new List<Zone>();
                var payload = zones.Select(z => new { id = z.Id, name = z.Name }).ToList();
                return Json(payload);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }
        [HttpPost]
        public async Task<ActionResult> SaveSite([FromBody] Site mSite)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null)
                    return Json(new { error = "Session expired. Please log in again." });

                mSite.CreatedBy = loginUser.Id;

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                var jsonStr = JsonConvert.SerializeObject(mSite);
                var str = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                var response = await hcf.Client.PostAsync("Site/SaveSite", str);

                if (response.StatusCode != HttpStatusCode.OK)
                    return Json(new { error = "Internal server error." });

                var jsonString = await response.Content.ReadAsStringAsync();
                var mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);

                if (mAPIResponse != null && mAPIResponse.IsSuccess && mAPIResponse.Value != null)
                {
                    return Json(new { success = true, message = "Site has been saved." });
                }

                return Json(new { error = "Error occurred while saving Site!" });
            }
            catch (Exception ex)
            {
                // TODO: _logger.LogError(ex, "SaveSite failed");
                return Json(new { error = "Internal server error." });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetAssetTypeInfo(int assetTypeId, int siteId)
        {
            var mAsset = new Asset();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = await hcf.Client.GetAsync($"AssetType/GetAssetTypeInfo/{assetTypeId}/{siteId}");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    mAsset = JsonConvert.DeserializeObject<Asset>(json) ?? new Asset();
                    mAsset.AssetTypeId = assetTypeId;
                    mAsset.SiteId = siteId;
                }
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
            return Json(mAsset);
        }
    }
}
