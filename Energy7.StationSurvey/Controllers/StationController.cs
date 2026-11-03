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
        private readonly ISectionService _sectionService;

        public StationController(IConfiguration config, IHttpContextAccessor httpContextAccessor, IZoneService zoneService, IDivisionService divisionService, ISiteService siteService, IAssetTypeService assetTypeService, ISectionService sectionService)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _siteService = siteService;
            _assetTypeService = assetTypeService;
            _sectionService = sectionService;
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

        // ── AJAX: every asset of the station (same API as the 90 Sites page asset cards) ──
        public async Task<JsonResult> GetSiteAssets(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var response = await hcf.Client.GetAsync($"Asset/GetAll/{siteId}");
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load assets." });

                var assets = JsonConvert.DeserializeObject<List<Asset>>(await response.Content.ReadAsStringAsync()) ?? new();
                return Json(assets.Select(a => new { id = a.Id, assetTypeId = a.AssetTypeId, name = a.Name, isActive = a.IsActive }));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // ── AJAX: existing asset with its saved channel/value per attribute ───
        public async Task<JsonResult> GetAssetJson(int id)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var asset = await GetAssetFromApi(hcf, id);
                if (asset == null || asset.Id != id) return Json(new { error = "Asset not found." });
                return Json(asset);
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        private static async Task<Asset?> GetAssetFromApi(HttpClientHelper hcf, int id)
        {
            var response = await hcf.Client.GetAsync($"Asset/GetAssetById/{id}");
            if (response.StatusCode != HttpStatusCode.OK) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<Asset>(json);
        }

        // ── AJAX: save new or edited asset ────────────────────────────────────
        [HttpPost]
        public async Task<JsonResult> SaveAsset([FromBody] Asset mAsset)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                // API Asset/SaveAsset overwrites every asset column, so on edit start from the
                // stored asset and apply only what this modal edits.
                if (mAsset.Id > 0)
                {
                    var existing = await GetAssetFromApi(hcf, mAsset.Id);
                    if (existing == null || existing.Id != mAsset.Id)
                        return Json(new { error = "Could not load the asset for editing." });

                    existing.Name = mAsset.Name;
                    existing.HttpMisLink = mAsset.HttpMisLink;
                    existing.NumberOfCell = mAsset.NumberOfCell;
                    existing.IsActive = mAsset.IsActive;
                    if (existing.AssetTypeId == (int)Utility.Utility.AssetType.POINT_MACHINE)
                    {
                        // Point Machine details edited on the modal (vibration / displacement stay as stored).
                        // IRS/TWS is stored as IsThickWave + ThickWaveTypeId, same as the 90 site ("Is TWS").
                        existing.IsThickWave = mAsset.IsThickWave;
                        existing.ThickWaveTypeId = mAsset.ThickWaveTypeId;
                        existing.IsHalfPointMachine = mAsset.IsHalfPointMachine;
                        existing.IsSeriesOpration = mAsset.IsSeriesOpration;
                        existing.SeriesOprationTypeId = mAsset.SeriesOprationTypeId;
                        existing.AliasDirectionA = mAsset.AliasDirectionA;
                        existing.AliasDirectionB = mAsset.AliasDirectionB;
                    }
                    existing.mAssetInfos = MergeAssetInfos(existing.mAssetInfos, mAsset.mAssetInfos);

                    // Edited on the modal's Alerts / FRSAlerts / Cause Code / Signal Mapping tabs.
                    // (GetAssetById returns mSignalTrackMappings as a display list, so it must be replaced.)
                    existing.mAlerts = mAsset.mAlerts;
                    existing.mFRSAttributeRanges = mAsset.mFRSAttributeRanges ?? new();
                    existing.mAlertInfoActivations = mAsset.mAlertInfoActivations ?? new();
                    existing.mSignalTrackMappings = mAsset.mSignalTrackMappings ?? new();

                    // existing.mFamilyTracks is sent back unchanged: SaveAsset deletes and re-adds
                    // family tracks on every save, so dropping it would lose them.

                    // Not edited here — leave them out so the API does not rewrite them
                    existing.mGlobalConfigs = null!;
                    existing.mAssetInfoDataloggers = new();
                    existing.mAssetMappings = new();
                    existing.mErrorCodeIdentifier = new();
                    existing.mErrorCodes = new();
                    mAsset = existing;
                }

                // API matches existing AssetInfo rows by AssetId + AssetAttributeId
                mAsset.mAssetInfos?.ForEach(x => x.AssetId = mAsset.Id);
                mAsset.CreatedBy = loginUser.Id;
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

        /// <summary>
        /// API SaveAssetInfoDefaultValue overwrites HttpPostName, ThresHold, GateContectTypeId and
        /// DefaultMultiplication on every save. The modal only edits Channel / Value / Multiplication,
        /// so apply those onto the stored rows instead of sending bare rows that would blank the rest.
        /// </summary>
        private static List<AssetInfo> MergeAssetInfos(List<AssetInfo>? stored, List<AssetInfo>? edited)
        {
            var result = new List<AssetInfo>();
            foreach (var e in edited ?? new List<AssetInfo>())
            {
                var s = stored?.FirstOrDefault(x => x.AssetAttributeId == e.AssetAttributeId);
                if (s == null) { result.Add(e); continue; }

                s.Channels = e.Channels;
                s.Value = e.Value;
                s.Multiplication = e.Multiplication;
                result.Add(s);
            }
            // Stored rows the modal did not show (e.g. hidden attributes) go back unchanged, so the
            // API does not delete them for Signal / Point Machine assets.
            foreach (var s in stored ?? new List<AssetInfo>())
                if (!result.Any(x => x.AssetAttributeId == s.AssetAttributeId))
                    result.Add(s);
            return result;
        }

        // ── AJAX: Survey gear mapping per location box — existing SiteSurvey table (same as 90 Site Survey) ──
        public async Task<JsonResult> GetSurveyGear(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                return Json(await GetSiteSurveyRows(hcf, siteId));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        private static async Task<List<SiteSurvey>> GetSiteSurveyRows(HttpClientHelper hcf, int siteId)
        {
            var response = await hcf.Client.GetAsync($"SiteSurvey/SiteId/{siteId}");
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Could not load survey rows.");
            return JsonConvert.DeserializeObject<List<SiteSurvey>>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        [HttpPost]
        public async Task<JsonResult> SaveSurveyGear([FromBody] SiteSurvey m)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0) return Json(new { error = "Select a station first." });
                if (string.IsNullOrWhiteSpace(m.LocationBoxNo)) return Json(new { error = "Enter the location box." });
                if (m.Cluster <= 0) return Json(new { error = "Enter the RF cluster number." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                if (m.Id > 0 && !(await GetSiteSurveyRows(hcf, m.SiteId)).Any(r => r.Id == m.Id))
                    return Json(new { error = "Survey row not found for this station." });

                // Same normalisation as the 90 Site Survey page (upper-case text)
                static string? U(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToUpperInvariant();
                var row = new SiteSurvey
                {
                    Id = m.Id, SiteId = m.SiteId, Cluster = m.Cluster, CreatedBy = loginUser.Id,
                    LocationBoxNo = U(m.LocationBoxNo), LocationSide = U(m.LocationSide),
                    DistanceFromRR = string.IsNullOrWhiteSpace(m.DistanceFromRR) ? null : m.DistanceFromRR.Trim(),
                    TR = U(m.TR), TF = U(m.TF), PointMachine = U(m.PointMachine), Signal = U(m.Signal),
                    AxleCounter = U(m.AxleCounter), BusBar = U(m.BusBar), UFSBI = U(m.UFSBI), BHMS = U(m.BHMS)
                };

                using var str = new StringContent(JsonConvert.SerializeObject(row), Encoding.UTF8, "application/json");
                var response = await hcf.Client.PostAsync("SiteSurvey", str);
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = "Internal server error." });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        [HttpPost]
        public async Task<JsonResult> DeleteSurveyGear(int id, int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                if (!(await GetSiteSurveyRows(hcf, siteId)).Any(r => r.Id == id))
                    return Json(new { error = "Survey row not found for this station." });

                // API SiteSurvey delete removes the row (no soft delete) — same as the 90 page
                var response = await hcf.Client.DeleteAsync($"SiteSurvey/{id}");
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = "Internal server error." });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // ── AJAX: Locations = Cluster rows named "cluster_{RF no}/{location}" (90 Yard Config model) ──
        public async Task<JsonResult> GetLocations(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                var response = await hcf.Client.GetAsync($"Cluster/SiteId/{siteId}");
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load locations." });
                var list = JsonConvert.DeserializeObject<List<Cluster>>(await response.Content.ReadAsStringAsync()) ?? new();

                // Cluster/SiteId does not return TypeId (Indoor/Outdoor), so read each location in full
                var full = await Task.WhenAll(list.Select(c => GetClusterFromApi(hcf, c.Id)));
                var result = full.Where(c => c != null).Select(c =>
                {
                    var (rf, loc) = SplitClusterName(c!.Name);
                    return new
                    {
                        id = c.Id,
                        name = c.Name,
                        rfCluster = rf,          // null when the name has no "cluster_N/" prefix
                        location = loc,
                        typeId = c.TypeId,
                        powerSupplyId = c.PowerSupplyId,   // 1 = 24 V, 2 = 110 V, 3 = Converter 110→24 V
                        modemId = c.ModemId,
                        ip = c.IP
                    };
                }).OrderBy(x => x.rfCluster ?? int.MaxValue).ThenBy(x => x.location).ToList();
                return Json(result);
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public class LocationSave
        {
            public int Id { get; set; }
            public int SiteId { get; set; }
            public int RfCluster { get; set; }
            public string LocationName { get; set; } = "";
            public int TypeId { get; set; }
            public int? PowerSupplyId { get; set; }
        }

        [HttpPost]
        public async Task<JsonResult> SaveLocation([FromBody] LocationSave m)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });

                var loc = (m?.LocationName ?? "").Trim();
                if (m == null || m.SiteId <= 0) return Json(new { error = "Select a station first." });
                if (loc.Length == 0) return Json(new { error = "Enter a location name." });
                if (loc.Contains('/')) return Json(new { error = "Location name cannot contain \"/\"." });
                if (m.RfCluster < 1 || m.RfCluster > 50) return Json(new { error = "RF cluster must be 1 to 50." });
                if (m.TypeId != 1 && m.TypeId != 2) return Json(new { error = "Select Indoor or Outdoor." });
                if (m.PowerSupplyId.HasValue && (m.PowerSupplyId < 1 || m.PowerSupplyId > 3))
                    return Json(new { error = "Invalid power supply." });

                var name = $"cluster_{m.RfCluster}/{loc}";
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                // Same location name twice at one station would make the location counts and card lines ambiguous
                var listResp = await hcf.Client.GetAsync($"Cluster/SiteId/{m.SiteId}");
                var siteClusters = listResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<Cluster>>(await listResp.Content.ReadAsStringAsync()) ?? new()
                    : new List<Cluster>();
                if (siteClusters.Any(c => c.Id != m.Id &&
                        string.Equals(SplitClusterName(c.Name).location, loc, StringComparison.OrdinalIgnoreCase)))
                    return Json(new { error = $"Location \"{loc}\" already exists at this station." });

                Cluster cluster;
                if (m.Id > 0)
                {
                    // API Cluster save overwrites every column (modem, IP, cards…), so start from the stored row
                    var existing = await GetClusterFromApi(hcf, m.Id);
                    if (existing == null || existing.Id != m.Id || existing.SiteId != m.SiteId)
                        return Json(new { error = "Could not load the location." });
                    cluster = existing;
                }
                else
                {
                    cluster = new Cluster { SiteId = m.SiteId };
                }
                cluster.Name = name;
                cluster.TypeId = m.TypeId;
                cluster.PowerSupplyId = m.PowerSupplyId;
                cluster.CreatedBy = loginUser.Id;
                // Empty card-number lists → the API adds no cards (cards are set up in Yard Config)
                cluster.CurrentCardNumbers = new(); cluster.V5Numbers = new(); cluster.V82Numbers = new();
                cluster.MintCardNumbers = new(); cluster.DINumbers = new();

                using var str = new StringContent(JsonConvert.SerializeObject(cluster), Encoding.UTF8, "application/json");
                var response = await hcf.Client.PostAsync("Cluster", str);
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = "Internal server error." });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        private static async Task<Cluster?> GetClusterFromApi(HttpClientHelper hcf, int id)
        {
            var response = await hcf.Client.GetAsync($"Cluster/{id}");
            if (response.StatusCode != HttpStatusCode.OK) return null;
            return JsonConvert.DeserializeObject<Cluster>(await response.Content.ReadAsStringAsync());
        }

        /// <summary>"cluster_15/lc9" → (15, "lc9"); a name without that prefix → (null, whole name).</summary>
        private static (int? rf, string location) SplitClusterName(string? name)
        {
            name = (name ?? "").Trim();
            var slash = name.IndexOf('/');
            if (slash < 0) return (null, name);
            var prefix = name.Substring(0, slash).Trim();
            var loc = name.Substring(slash + 1).Trim();
            int? rf = prefix.StartsWith("cluster_", StringComparison.OrdinalIgnoreCase)
                      && int.TryParse(prefix.Substring(8), out var n) ? n : null;
            return (rf, loc);
        }

        // ── AJAX: SURVEY stage checklist (stored in AppConfig, see Utility/SurveyChecklist) ──
        public async Task<JsonResult> GetSurveyChecklist(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                var site = _siteService.Get(siteId);
                if (site == null || site.Id != siteId) return Json(new { error = "Station not found." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var rows = await GetSurveyRows(hcf, site);
                return Json(BuildSurveyState(rows));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public class SurveyItemUpdate
        {
            public int SiteId { get; set; }
            public int ItemNo { get; set; }
            public bool Done { get; set; }
        }

        [HttpPost]
        public async Task<JsonResult> SaveSurveyChecklistItem([FromBody] SurveyItemUpdate update)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (update == null || update.ItemNo < 1 || update.ItemNo > SurveyChecklist.Items.Count)
                    return Json(new { error = "Invalid checklist item." });

                var site = _siteService.Get(update.SiteId);
                if (site == null || site.Id != update.SiteId) return Json(new { error = "Station not found." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var rows = await GetSurveyRows(hcf, site);
                var stamp = $"{loginUser.Id}|{DateTime.Now:yyyy-MM-dd}";
                var changes = new List<AppConfig>();

                var itemKey = SurveyChecklist.ItemKey(update.ItemNo);
                changes.Add(SurveyRow(rows, site, itemKey, $"{(update.Done ? 1 : 0)}|{stamp}"));

                // Stage completion: all items done → Completed; otherwise back to InProgress
                var doneCount = Enumerable.Range(1, SurveyChecklist.Items.Count).Count(n =>
                {
                    var key = SurveyChecklist.ItemKey(n);
                    if (key == itemKey) return update.Done;
                    return IsDone(rows.FirstOrDefault(r => r.AppKey == key)?.AppValue);
                });
                var stageRow = rows.FirstOrDefault(r => r.AppKey == SurveyChecklist.StageKey);
                var newStatus = doneCount == SurveyChecklist.Items.Count ? "Completed" : "InProgress";
                if (stageRow == null || !(stageRow.AppValue ?? "").StartsWith(newStatus + "|"))
                    changes.Add(SurveyRow(rows, site, SurveyChecklist.StageKey, $"{newStatus}|{stamp}"));

                using var str = new StringContent(JsonConvert.SerializeObject(changes), Encoding.UTF8, "application/json");
                var response = await hcf.Client.PostAsync("AppConfig", str);
                if (!response.IsSuccessStatusCode)   // API returns 204 No Content on success
                    return Json(new { error = "Internal server error." });

                return Json(BuildSurveyState(await GetSurveyRows(hcf, site)));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        private static async Task<List<AppConfig>> GetSurveyRows(HttpClientHelper hcf, Site site)
        {
            // GroupName + ZoneId lookup is not cached by the API (GroupName-only lookup is cached 10 min)
            var response = await hcf.Client.GetAsync(
                $"AppConfig/GroupName/{SurveyChecklist.GroupName(site.Id)}/ZoneId/{site.ZoneId}");
            if (response.StatusCode != HttpStatusCode.OK) return new List<AppConfig>();
            return JsonConvert.DeserializeObject<List<AppConfig>>(await response.Content.ReadAsStringAsync())
                   ?? new List<AppConfig>();
        }

        private static AppConfig SurveyRow(List<AppConfig> rows, Site site, string key, string value)
        {
            var existing = rows.FirstOrDefault(r => r.AppKey == key);
            return new AppConfig
            {
                Id = existing?.Id ?? 0,   // API upserts by Id
                ZoneId = site.ZoneId,
                GroupName = SurveyChecklist.GroupName(site.Id),
                AppKey = key,
                AppValue = value
            };
        }

        private static bool IsDone(string? value) => (value ?? "").StartsWith("1|");

        private static object BuildSurveyState(List<AppConfig> rows)
        {
            var items = SurveyChecklist.Items.Select((title, i) =>
            {
                var parts = (rows.FirstOrDefault(r => r.AppKey == SurveyChecklist.ItemKey(i + 1))?.AppValue ?? "").Split('|');
                var done = parts.Length > 0 && parts[0] == "1";
                return new { itemNo = i + 1, title, done, doneOn = done && parts.Length > 2 ? parts[2] : null };
            }).ToList();

            var stage = (rows.FirstOrDefault(r => r.AppKey == SurveyChecklist.StageKey)?.AppValue ?? "").Split('|');
            var completed = stage.Length > 0 && stage[0] == "Completed";
            return new
            {
                items,
                doneCount = items.Count(x => x.done),
                total = items.Count,
                stageCompleted = completed,
                completedOn = completed && stage.Length > 2 ? stage[2] : null
            };
        }

        // ── AJAX: save Row / Terminal for one card line (same API as 90 Yard Config) ──
        [HttpPost]
        public async Task<JsonResult> UpdateCardLineLocation([FromBody] CardLine mCardLine)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (mCardLine == null || mCardLine.Id <= 0) return Json(new { error = "Card line not found." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                // UpdateLocation always overwrites TerminalNumberNegative and CurrentSequenceNumber,
                // so start from the stored card line and change only Row / Terminal(+).
                var getResponse = await hcf.Client.GetAsync($"CardLine/Id/{mCardLine.Id}");
                if (getResponse.StatusCode != HttpStatusCode.OK)
                    return Json(new { error = "Could not load the card line." });
                var existing = JsonConvert.DeserializeObject<CardLine>(await getResponse.Content.ReadAsStringAsync());
                if (existing == null || existing.Id != mCardLine.Id)
                    return Json(new { error = "Could not load the card line." });

                existing.RowNumber = mCardLine.RowNumber;
                existing.TerminalNumberPositive = mCardLine.TerminalNumberPositive;
                // Same rule as 90 EditCardLineCircuit: current → only +ve terminal + sequence no.,
                // voltage → +ve and -ve terminals
                if (IsCurrentAttribute(existing))
                    existing.CurrentSequenceNumber = mCardLine.CurrentSequenceNumber;
                else
                    existing.TerminalNumberNegative = mCardLine.TerminalNumberNegative;

                using var str = new StringContent(JsonConvert.SerializeObject(existing), Encoding.UTF8, "application/json");
                var response = await hcf.Client.PutAsync($"CardLine/UpdateLocation/Id/{existing.Id}", str);
                if (response.StatusCode != HttpStatusCode.OK)
                    return Json(new { error = "Internal server error." });

                return Json(new
                {
                    success = true,
                    rowNumber = existing.RowNumber,
                    terminalNumberPositive = existing.TerminalNumberPositive,
                    terminalNumberNegative = existing.TerminalNumberNegative,
                    currentSequenceNumber = existing.CurrentSequenceNumber
                });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        /// <summary>90 rule: attribute name contains "ma", or a Point Machine attribute containing "-c", is a current input.</summary>
        private static bool IsCurrentAttribute(CardLine c)
        {
            var name = (c.AttributeName ?? "").ToLower();
            return name.Contains("ma")
                || (c.AssetTypeId == (int)Utility.Utility.AssetType.POINT_MACHINE && name.Contains("-c"));
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
        // ── AJAX: full site record for the edit modal ─────────────────────────
        public JsonResult GetSiteJson(int id)
        {
            try
            {
                var site = _siteService.Get(id);
                if (site == null || site.Id == 0)
                    return Json(new { error = "Station not found." });
                return Json(site);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        public JsonResult GetSectionsJson()
        {
            try
            {
                var payload = (_sectionService.GetAll() ?? new List<Section>())
                    .Select(s => new { id = s.Id, name = s.Name })
                    .ToList();
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

                // API Site/SaveSite overwrites every column it knows, so on edit start
                // from the stored record and apply only the fields this form edits.
                if (mSite.Id > 0)
                {
                    var existing = _siteService.Get(mSite.Id);
                    if (existing == null || existing.Id != mSite.Id)
                        return Json(new { error = "Could not load the station for editing." });

                    ApplyEditableFields(existing, mSite);
                    // Edit: "Project Completed" ticked → IsProject = false, otherwise stays a project station
                    existing.IsProject = mSite.IsProject ?? true;
                    mSite = existing;
                }
                else
                {
                    mSite.IsProject = true;   // every station created from this app is a project station
                }

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
                    return Json(new { success = true, siteId = Convert.ToInt32((object?)mAPIResponse.Value ?? 0), message = "Site has been saved." });
                }

                return Json(new { error = "Error occurred while saving Site!" });
            }
            catch (Exception ex)
            {
                // TODO: _logger.LogError(ex, "SaveSite failed");
                return Json(new { error = "Internal server error." });
            }
        }

        /// <summary>Copies the fields shown on the station modal (same set as the 90 Edit Site page).</summary>
        private static void ApplyEditableFields(Site target, Site source)
        {
            target.ZoneId = source.ZoneId;
            target.DivisionId = source.DivisionId;
            target.SectionId = source.SectionId;
            target.Name = source.Name;
            target.Description = source.Description;
            target.MQTTBasePath = source.MQTTBasePath;
            target.MQTTNoOfChannels = source.MQTTNoOfChannels;
            target.MQTTActiveChannel = source.MQTTActiveChannel;
            target.MACId = source.MACId;
            target.StationCode = source.StationCode;
            target.SmsFrequency = source.SmsFrequency;
            target.SmsLimit = source.SmsLimit;
            target.PLCLogId = source.PLCLogId;
            target.ChargerDelay = source.ChargerDelay;
            target.RebootTotal = source.RebootTotal;
            target.RebootDay = source.RebootDay;
            target.RebootDaynum = source.RebootDaynum;
            target.RouterId = source.RouterId;
            target.MobileNumber = source.MobileNumber;
            target.ModemType = source.ModemType;
            target.SingleModamType = source.SingleModamType;
            target.ServerType = source.ServerType;
            target.IsActive = source.IsActive;
            target.IsSetupLocalSite = source.IsSetupLocalSite;
            target.IsHttpPost = source.IsHttpPost;
            target.IsEIEnable = source.IsEIEnable;
            target.IsTest = source.IsTest;
            target.IsNoneRE = source.IsNoneRE;
            target.IsUnderDevlopment = source.IsUnderDevlopment;
            target.IsAnalytics = source.IsAnalytics;
            target.IsTestUser = source.IsTestUser;
            target.IsEarthFaultPushNotification = source.IsEarthFaultPushNotification;
            target.IsCuttingRelayVoltage = source.IsCuttingRelayVoltage;
            target.IsSummary = source.IsSummary;
            target.IsSeriesOpration = source.IsSeriesOpration;
            target.IsPointDetection50v = source.IsPointDetection50v;
            target.IsDatalogger = source.IsDatalogger;
            target.DataLoggerVersion = source.DataLoggerVersion;
            target.IsDataLoggerJsonReverse = source.IsDataLoggerJsonReverse;
            target.IsCombinePointMachine = source.IsCombinePointMachine;
            target.IsFRSAlert = source.IsFRSAlert;
            target.IsSiemensPoint = source.IsSiemensPoint;
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
