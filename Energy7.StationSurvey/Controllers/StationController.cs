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
                // Completed (non-project) stations — IsProject false or null — are read-only.
                // Label them "(Completed)" in the dropdown so they read as finished sites
                // before selection, and expose their ids so the view can badge + lock them.
                ViewBag.Sites = new SelectList(
                    sites.Select(s => new
                    {
                        Id = s.Id,
                        Name = (s.Id > 0 && s.IsProject != true) ? (s.Name + " (Completed)") : s.Name
                    }), "Id", "Name");

                ViewBag.CompletedSiteIds = sites
                    .Where(s => s.Id > 0 && s.IsProject != true)
                    .Select(s => s.Id)
                    .ToArray();
            }
            catch
            {
                ViewBag.Sites = new SelectList(new List<Site>(), "Id", "Name");
                ViewBag.CompletedSiteIds = new int[0];
            }
        }

        // ── Completed-site guard ──────────────────────────────────────────────
        // A station whose IsProject is false or null is a completed (non-project)
        // site and is read-only: every survey / asset / location / yard / material /
        // document update is rejected. Only live project stations (IsProject == true)
        // may be edited.
        private bool IsSiteLocked(int siteId)
        {
            if (siteId <= 0) return false;
            try
            {
                var site = _siteService.Get(siteId);
                return site != null && site.Id > 0 && site.IsProject != true;
            }
            catch { return false; }   // never block an update because the lookup failed
        }

        private static JsonResult SiteLockedJson()
            => new JsonResult(new { error = "This station is marked Completed and is read-only. Updates are not allowed." });

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
                // Same API route as 90 Yard Config card-line popup (AssetAttributeService.GetBy)
                var response = await hcf.Client.GetAsync($"AssetAttribute/AssetTypeId/{assetTypeId}");
                if (response.StatusCode != HttpStatusCode.OK)
                    return Json(new { error = "Could not load attributes for this asset type." });
                var json = await response.Content.ReadAsStringAsync();
                attributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(json) ?? new();
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
            if (IsSiteLocked(mAsset?.SiteId ?? 0)) return SiteLockedJson();
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
                    // Number Of Cell is no longer edited here; the stored value is sent back unchanged.
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

        // ══ SURVEY tab ═══════════════════════════════════════════════════════════════
        // One page per station. Stored in the existing tables through the API (no schema change):
        //   SiteSurvey row per location box  → LocationBoxNo, Cluster (RF), LocationSide, DistanceFromRR and the
        //                                      gear columns TR, TF, PointMachine, Signal ("S1 HG,S1 RG"), AxleCounter,
        //                                      BusBar, UFSBI, BHMS (comma lists, upper case — same as the 90 Site Survey)
        //   SurveyPointMachine per point     → full/half, TWS A/B/Both, series LH/RH (SiteSurvey/CreatePM)
        public async Task<JsonResult> GetSurvey(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (siteId <= 0) return Json(new { error = "Select a station first." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var rows = await GetSiteSurveyRows(hcf, siteId);
                var pms = await GetSurveyPointMachines(hcf, siteId);
                return Json(new { rows, pms });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        private static async Task<List<SiteSurvey>> GetSiteSurveyRows(HttpClientHelper hcf, int siteId)
        {
            var response = await hcf.Client.GetAsync($"SiteSurvey/SiteId/{siteId}");
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Could not load survey rows.");
            return JsonConvert.DeserializeObject<List<SiteSurvey>>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        private static async Task<List<SurveyPointMachine>> GetSurveyPointMachines(HttpClientHelper hcf, int siteId)
        {
            var response = await hcf.Client.GetAsync($"SiteSurvey/GetPM/SiteId/{siteId}");
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Could not load point machine details.");
            return JsonConvert.DeserializeObject<List<SurveyPointMachine>>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        public class SurveySaveRequest
        {
            public int SiteId { get; set; }
            public List<SiteSurvey> Rows { get; set; } = new();
            public List<SurveyPointMachine> PointMachines { get; set; } = new();
        }

        // Comma list → trimmed, single-spaced, upper-case, distinct names
        private static List<string> SurveyNames(string? v) =>
            (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(x => System.Text.RegularExpressions.Regex.Replace(x.Trim(), @"\s+", " ").ToUpperInvariant())
                     .Where(x => x.Length > 0).Distinct().ToList();

        private static string? SurveyJoin(IEnumerable<string> names)
        {
            var list = names.ToList();
            return list.Count == 0 ? null : string.Join(",", list);
        }

        private static string? SurveyText(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToUpperInvariant();

        /// <summary>
        /// Saves the whole survey of one station. The API checks each row on its own (TR/TF unique across the
        /// station, new rows' RF cluster = max or max+1), so the order matters:
        ///   1. delete rows that were removed on screen
        ///   2. take moved TR/TF names out of their old rows first
        ///   3. save changed and new rows (new rows in RF cluster order)
        ///   4. save point machine details
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> SaveSurvey([FromBody] SurveySaveRequest m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            var done = 0;
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0) return Json(new { error = "Select a station first." });
                var rows = m.Rows ?? new();

                // ── Normalise + validate the whole survey before touching anything ──
                foreach (var r in rows)
                {
                    r.LocationBoxNo = SurveyText(r.LocationBoxNo) ?? "";
                    if (r.LocationBoxNo.Length == 0) return Json(new { error = "Every location needs a name." });
                    if (r.Cluster < 1 || r.Cluster > 50) return Json(new { error = $"{r.LocationBoxNo}: RF cluster must be 1 to 50." });
                    r.LocationSide = SurveyText(r.LocationSide);
                    r.DistanceFromRR = string.IsNullOrWhiteSpace(r.DistanceFromRR) ? null : r.DistanceFromRR.Trim();
                    r.LocToLocDist = string.IsNullOrWhiteSpace(r.LocToLocDist) ? null : r.LocToLocDist.Trim();
                    r.TR = SurveyJoin(SurveyNames(r.TR)); r.TF = SurveyJoin(SurveyNames(r.TF));
                    r.PointMachine = SurveyJoin(SurveyNames(r.PointMachine)); r.Signal = SurveyJoin(SurveyNames(r.Signal));
                    r.AxleCounter = SurveyJoin(SurveyNames(r.AxleCounter)); r.BusBar = SurveyJoin(SurveyNames(r.BusBar));
                    r.UFSBI = SurveyJoin(SurveyNames(r.UFSBI)); r.BHMS = SurveyJoin(SurveyNames(r.BHMS));
                }
                var dupLoc = rows.GroupBy(r => r.LocationBoxNo).FirstOrDefault(g => g.Count() > 1);
                if (dupLoc != null) return Json(new { error = $"Location {dupLoc.Key} is entered twice." });

                string? Twice(Func<SiteSurvey, string?> col, string what)
                {
                    var hit = rows.SelectMany(r => SurveyNames(col(r)).Select(n => (n, r.LocationBoxNo)))
                                  .GroupBy(x => x.n).FirstOrDefault(g => g.Count() > 1);
                    return hit == null ? null : $"{what} {hit.Key} is in {string.Join(" and ", hit.Select(x => x.LocationBoxNo))}. Keep it in one location.";
                }
                var twice = Twice(r => r.TR, "TR end of track") ?? Twice(r => r.TF, "TF end of track")
                         ?? Twice(r => r.PointMachine, "Point machine") ?? Twice(r => r.Signal, "Signal aspect");
                if (twice != null) return Json(new { error = twice });

                var badSignal = rows.SelectMany(r => SurveyNames(r.Signal)).FirstOrDefault(s => s.Split(' ').Length != 2);
                if (badSignal != null) return Json(new { error = $"Signal entry \"{badSignal}\" must be a name and one aspect, like S1 HG." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var existing = await GetSiteSurveyRows(hcf, m.SiteId);
                var byId = existing.ToDictionary(r => r.Id);
                if (rows.Any(r => r.Id > 0 && !byId.ContainsKey(r.Id)))
                    return Json(new { error = "The survey was changed by someone else. Reload and try again." });

                // New rows: the API only accepts RF cluster = current max or max + 1
                var keptIds = rows.Where(r => r.Id > 0).Select(r => r.Id).ToHashSet();
                var max = existing.Where(r => keptIds.Contains(r.Id)).Select(r => r.Cluster).DefaultIfEmpty(0).Max();
                foreach (var r in rows.Where(r => r.Id <= 0).OrderBy(r => r.Cluster))
                {
                    if (r.Cluster > max + 1)
                        return Json(new { error = $"{r.LocationBoxNo}: RF cluster {r.Cluster} skips a number. Use {max + 1} or lower." });
                    max = Math.Max(max, r.Cluster);
                }

                async Task<SiteSurvey> Post(SiteSurvey row)
                {
                    row.SiteId = m.SiteId; row.CreatedBy = loginUser.Id;
                    using var str = new StringContent(JsonConvert.SerializeObject(row), Encoding.UTF8, "application/json");
                    var response = await hcf.Client.PostAsync("SiteSurvey", str);
                    if (response.StatusCode != HttpStatusCode.OK) throw new Exception(await ApiErrorMessage(response));
                    done++;
                    return JsonConvert.DeserializeObject<SiteSurvey>(await response.Content.ReadAsStringAsync()) ?? row;
                }

                // 1. Deleted locations (API delete removes the row — same as the 90 page)
                foreach (var old in existing.Where(r => !keptIds.Contains(r.Id)))
                {
                    var response = await hcf.Client.DeleteAsync($"SiteSurvey/{old.Id}");
                    if (response.StatusCode != HttpStatusCode.OK) throw new Exception(await ApiErrorMessage(response));
                    done++;
                }

                // 2. Track ends moved to another location: remove them from their old row first
                foreach (var r in rows.Where(r => r.Id > 0))
                {
                    var old = byId[r.Id];
                    var oldTr = SurveyNames(old.TR); var oldTf = SurveyNames(old.TF);
                    var keepTr = oldTr.Intersect(SurveyNames(r.TR)).ToList();
                    var keepTf = oldTf.Intersect(SurveyNames(r.TF)).ToList();
                    if (keepTr.Count == oldTr.Count && keepTf.Count == oldTf.Count) continue;
                    old.TR = SurveyJoin(keepTr); old.TF = SurveyJoin(keepTf);
                    await Post(old);
                }

                // 3. Changed and new rows. Existing rows start from the stored row so FtuBox / VC / Conductor are kept.
                static bool Same(SiteSurvey a, SiteSurvey b) =>
                    a.LocationBoxNo == SurveyText(b.LocationBoxNo) && a.Cluster == b.Cluster
                    && a.LocationSide == SurveyText(b.LocationSide)
                    && a.DistanceFromRR == (string.IsNullOrWhiteSpace(b.DistanceFromRR) ? null : b.DistanceFromRR.Trim())
                    && a.LocToLocDist == (string.IsNullOrWhiteSpace(b.LocToLocDist) ? null : b.LocToLocDist.Trim())
                    && a.TR == SurveyJoin(SurveyNames(b.TR)) && a.TF == SurveyJoin(SurveyNames(b.TF))
                    && a.PointMachine == SurveyJoin(SurveyNames(b.PointMachine)) && a.Signal == SurveyJoin(SurveyNames(b.Signal))
                    && a.AxleCounter == SurveyJoin(SurveyNames(b.AxleCounter)) && a.BusBar == SurveyJoin(SurveyNames(b.BusBar))
                    && a.UFSBI == SurveyJoin(SurveyNames(b.UFSBI)) && a.BHMS == SurveyJoin(SurveyNames(b.BHMS));

                static void Copy(SiteSurvey from, SiteSurvey to)
                {
                    to.LocationBoxNo = from.LocationBoxNo; to.Cluster = from.Cluster; to.LocationSide = from.LocationSide;
                    to.DistanceFromRR = from.DistanceFromRR; to.LocToLocDist = from.LocToLocDist; to.TR = from.TR; to.TF = from.TF; to.PointMachine = from.PointMachine;
                    to.Signal = from.Signal; to.AxleCounter = from.AxleCounter; to.BusBar = from.BusBar; to.UFSBI = from.UFSBI; to.BHMS = from.BHMS;
                }

                var original = await GetSiteSurveyRows(hcf, m.SiteId);   // after steps 1-2
                var origById = original.ToDictionary(r => r.Id);
                foreach (var r in rows.Where(r => r.Id > 0))
                {
                    var stored = origById[r.Id];
                    if (Same(r, stored)) continue;
                    Copy(r, stored);
                    await Post(stored);
                }
                max = original.Select(r => r.Cluster).DefaultIfEmpty(0).Max();
                foreach (var r in rows.Where(r => r.Id <= 0).OrderBy(r => r.Cluster))
                {
                    var row = new SiteSurvey();
                    Copy(r, row);
                    if (r.Cluster < max)
                    {
                        // API rejects a new row below the current max: create it in the max cluster, then move it
                        row.Cluster = max;
                        var created = await Post(row);
                        if (created.Id <= 0) throw new Exception($"{r.LocationBoxNo}: could not move it to RF cluster {r.Cluster}.");
                        created.Cluster = r.Cluster;
                        await Post(created);
                    }
                    else
                    {
                        await Post(row);
                        max = Math.Max(max, r.Cluster);
                    }
                }

                // 4. Point machine details (API upserts by name; points removed from the survey are left as they are)
                //    IsSeriesOpration = Series / Parallel, IsHalfPointMachine = one end only,
                //    SeriesOprationTypeId = which end for a one-end point (1 = A / Normal end, 2 = B / Reverse end);
                //    for a full point it keeps whatever was stored (LH/RH from the 90 page).
                var pmNames = rows.SelectMany(r => SurveyNames(r.PointMachine)).ToHashSet();
                var pms = (m.PointMachines ?? new())
                    .Where(p => !string.IsNullOrWhiteSpace(p.Name) && pmNames.Contains(p.Name.Trim().ToUpperInvariant()))
                    .Select(p => new SurveyPointMachine
                    {
                        SiteId = m.SiteId, Name = p.Name.Trim().ToUpperInvariant(),
                        IsHalfPointMachine = p.IsHalfPointMachine,
                        IsThickWave = p.IsThickWave, ThickWaveTypeId = p.IsThickWave ? p.ThickWaveTypeId : null,
                        IsSeriesOpration = p.IsSeriesOpration,
                        SeriesOprationTypeId = p.SeriesOprationTypeId is 1 or 2 ? p.SeriesOprationTypeId : null,
                        MachineA = p.MachineA, MachineB = p.MachineB
                    }).ToList();
                if (pms.Count > 0)
                {
                    using var str = new StringContent(JsonConvert.SerializeObject(pms), Encoding.UTF8, "application/json");
                    var response = await hcf.Client.PostAsync("SiteSurvey/CreatePM", str);
                    if (response.StatusCode != HttpStatusCode.NoContent && response.StatusCode != HttpStatusCode.OK)
                        throw new Exception(await ApiErrorMessage(response));
                }

                // 5. Auto-propagate to Location (Cluster) + Assets. Create-only, idempotent, best-effort:
                //    never fails the save. Yard card-line wiring is NOT generated (it needs cards/ADC/pins
                //    the survey does not have — that stays a manual Yard Config step).
                object? propagated = null;
                try { propagated = await PropagateSurvey(hcf, m.SiteId, loginUser.Id); }
                catch { /* best-effort only */ }

                return Json(new { success = true, propagated });
            }
            catch (Exception ex)
            {
                return Json(new { error = done > 0 ? $"{ex.Message} Part of the survey was already saved, so reload before editing again." : ex.Message, partial = done > 0 });
            }
        }

        // ══ Survey → Location + Assets propagation ═══════════════════════════════════════
        // Create-only + idempotent: never edits or deletes an existing Cluster/Asset, only adds the
        // ones the survey has that are missing. Name-based (the API has no survey→entity link), so
        // re-saving a survey will not duplicate. Yard CardLine wiring is intentionally NOT created:
        // it needs cards / ADC / pins that the survey does not capture.
        //   Gear → AssetTypeId (Utility.SiteSurveyAssetType): TF/TR → Track (1, one "…T" per track),
        //   Signal column (signal/shunt/route/CO-ON) → Signal (2), Point → Point Machine (3),
        //   Axle counter → 4, Bus bar → 14, UFSBI → 23, BHMS → 28.
        private async Task<object?> PropagateSurvey(HttpClientHelper hcf, int siteId, int createdBy)
        {
            var site = _siteService.Get(siteId);
            if (site == null || site.Id != siteId) return null;

            var rows = await GetSiteSurveyRows(hcf, siteId);
            var pms = await GetSurveyPointMachines(hcf, siteId);
            var pmByName = pms.Where(p => !string.IsNullOrWhiteSpace(p.Name))
                              .GroupBy(p => p.Name.Trim().ToUpperInvariant())
                              .ToDictionary(g => g.Key, g => g.First());

            int locCreated = 0, assetCreated = 0, failed = 0;

            // ── 1. Locations → Clusters named "cluster_{RF}/{box}", default Outdoor (TypeId 2) ──
            var clResp = await hcf.Client.GetAsync($"Cluster/SiteId/{siteId}");
            var clusters = clResp.StatusCode == HttpStatusCode.OK
                ? JsonConvert.DeserializeObject<List<Cluster>>(await clResp.Content.ReadAsStringAsync()) ?? new()
                : new List<Cluster>();
            var haveLoc = clusters.Select(c => SplitClusterName(c.Name).location.Trim().ToUpperInvariant())
                                  .Where(s => s.Length > 0).ToHashSet();
            foreach (var r in rows)
            {
                var loc = (r.LocationBoxNo ?? "").Trim();
                if (loc.Length == 0 || loc.Contains('/') || r.Cluster < 1 || r.Cluster > 50) continue;
                if (!haveLoc.Add(loc.ToUpperInvariant())) continue;   // already exists / duplicate in survey
                var cluster = new Cluster
                {
                    SiteId = siteId, Name = $"cluster_{r.Cluster}/{loc}", TypeId = 2, PowerSupplyId = null, CreatedBy = createdBy,
                    CurrentCardNumbers = new(), V5Numbers = new(), V82Numbers = new(), MintCardNumbers = new(), DINumbers = new()
                };
                using var str = new StringContent(JsonConvert.SerializeObject(cluster), Encoding.UTF8, "application/json");
                var resp = await hcf.Client.PostAsync("Cluster", str);
                if (resp.StatusCode == HttpStatusCode.OK) locCreated++;
                else { failed++; haveLoc.Remove(loc.ToUpperInvariant()); }
            }

            // ── 2. Gears → Assets (create-only) ──
            var asResp = await hcf.Client.GetAsync($"Asset/GetAll/{siteId}");
            var assets = asResp.StatusCode == HttpStatusCode.OK
                ? JsonConvert.DeserializeObject<List<Asset>>(await asResp.Content.ReadAsStringAsync()) ?? new()
                : new List<Asset>();
            string Key(int t, string? n) => t + ":" + (n ?? "").Trim().ToUpperInvariant();
            var haveAsset = assets.Select(a => Key(a.AssetTypeId, a.Name)).ToHashSet();

            string TrackBase(string n) => System.Text.RegularExpressions.Regex.Replace(n.Trim(), "T[FR]$", "T");
            var want = new List<(int type, string name)>();
            foreach (var r in rows)
            {
                foreach (var n in SurveyNames(r.TF)) want.Add((1, TrackBase(n)));
                foreach (var n in SurveyNames(r.TR)) want.Add((1, TrackBase(n)));
                foreach (var e in SurveyNames(r.Signal)) want.Add((2, e.Split(' ')[0]));   // "S1 HG" → "S1"
                foreach (var n in SurveyNames(r.PointMachine)) want.Add((3, n));
                foreach (var n in SurveyNames(r.AxleCounter)) want.Add((4, n));
                foreach (var n in SurveyNames(r.BusBar)) want.Add((14, n));
                foreach (var n in SurveyNames(r.UFSBI)) want.Add((23, n));
                foreach (var n in SurveyNames(r.BHMS)) want.Add((28, n));
            }
            foreach (var (type, rawName) in want)
            {
                var name = (rawName ?? "").Trim();
                if (name.Length == 0) continue;
                var key = Key(type, name);
                if (!haveAsset.Add(key)) continue;   // exists or already created this run
                var asset = new Asset
                {
                    Id = 0, SiteId = siteId, AssetTypeId = type, Name = name, IsActive = true,
                    ZoneId = site.ZoneId, DivisionId = site.DivisionId, CreatedBy = createdBy,
                    mAssetInfos = new()
                };
                if (type == 3 && pmByName.TryGetValue(name.ToUpperInvariant(), out var pm))
                {
                    asset.IsHalfPointMachine = pm.IsHalfPointMachine;
                    asset.IsSeriesOpration = pm.IsSeriesOpration;
                    asset.SeriesOprationTypeId = pm.SeriesOprationTypeId;
                    asset.IsThickWave = pm.IsThickWave;
                    asset.ThickWaveTypeId = pm.ThickWaveTypeId;
                }
                try
                {
                    using var str = new StringContent(JsonConvert.SerializeObject(asset), Encoding.UTF8, "application/json");
                    var resp = await hcf.Client.PostAsync("Asset/SaveAsset", str);
                    var ok = resp.StatusCode == HttpStatusCode.OK
                             && (JsonConvert.DeserializeObject<APIResponse>(await resp.Content.ReadAsStringAsync())?.IsSuccess ?? false);
                    if (ok) assetCreated++;
                    else { failed++; haveAsset.Remove(key); }
                }
                catch { failed++; haveAsset.Remove(key); }
            }

            return new { locations = locCreated, assets = assetCreated, failed };
        }

        // API returns validation errors as 400 with the message as a JSON string body ("\"Signal: ...\"")
        private static async Task<string> ApiErrorMessage(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body)) return "Internal server error.";
            try
            {
                var msg = JsonConvert.DeserializeObject(body);
                if (msg is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            catch (JsonException) { }
            return response.StatusCode == HttpStatusCode.BadRequest ? body.Trim('"') : "Internal server error.";
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
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
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

        public class DeleteLocationRequest
        {
            public int Id { get; set; }        // Cluster (location) id
            public int SiteId { get; set; }
        }

        // ── Soft-delete a location: API DELETE Cluster/{id}/UserId/{userId} sets DeletedBy/DeletedDate
        //    (the row is kept; its ADCs and cards are cascaded by the API's ClusterService.Delete). ──
        [HttpPost]
        public async Task<JsonResult> DeleteLocation([FromBody] DeleteLocationRequest m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.Id <= 0) return Json(new { error = "Location not found." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                // Verify the cluster exists and belongs to this station before deleting
                var cluster = await GetClusterFromApi(hcf, m.Id);
                if (cluster == null || cluster.Id != m.Id) return Json(new { error = "Location not found." });
                if (m.SiteId > 0 && cluster.SiteId != m.SiteId) return Json(new { error = "Location does not belong to this station." });

                var response = await hcf.Client.DeleteAsync($"Cluster/{m.Id}/UserId/{loginUser.Id}");
                if (response.StatusCode != HttpStatusCode.NoContent && response.StatusCode != HttpStatusCode.OK)
                    return Json(new { error = await ApiErrorMessage(response) });
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
            if (IsSiteLocked(update?.SiteId ?? 0)) return SiteLockedJson();
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
            if (IsSiteLocked(mCardLine?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (mCardLine == null || mCardLine.Id <= 0) return Json(new { error = "Card line not found." });
                // 90 ValidateCardLineCircuit: Location Name is required
                if (string.IsNullOrWhiteSpace(mCardLine.LocationName)) return Json(new { error = "Location Name is required." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                // Start from the stored card line so the API gets its AssetId / AttributeName etc.
                var getResponse = await hcf.Client.GetAsync($"CardLine/Id/{mCardLine.Id}");
                if (getResponse.StatusCode != HttpStatusCode.OK)
                    return Json(new { error = "Could not load the card line." });
                var existing = JsonConvert.DeserializeObject<CardLine>(await getResponse.Content.ReadAsStringAsync());
                if (existing == null || existing.Id != mCardLine.Id)
                    return Json(new { error = "Could not load the card line." });

                existing.LocationName = mCardLine.LocationName.Trim();
                existing.RowNumber = string.IsNullOrWhiteSpace(mCardLine.RowNumber) ? null : mCardLine.RowNumber.Trim();
                existing.TerminalNumberPositive = mCardLine.TerminalNumberPositive;
                // Same as 90 EditCardLineCircuit: current → +ve terminal + sequence no. (−ve is cleared),
                // voltage → +ve and −ve terminals (sequence no. is cleared)
                if (IsCurrentAttribute(existing))
                {
                    existing.TerminalNumberNegative = null;
                    existing.CurrentSequenceNumber = mCardLine.CurrentSequenceNumber;
                }
                else
                {
                    existing.TerminalNumberNegative = mCardLine.TerminalNumberNegative;
                    existing.CurrentSequenceNumber = null;
                }

                using var str = new StringContent(JsonConvert.SerializeObject(existing), Encoding.UTF8, "application/json");
                var response = await hcf.Client.PutAsync($"CardLine/UpdateLocation/Id/{existing.Id}", str);
                if (response.StatusCode != HttpStatusCode.OK)
                    return Json(new { error = await ApiErrorMessage(response) });

                // API keeps the old Row when the new one is blank, so re-read what was actually stored
                var saved = existing;
                var reread = await hcf.Client.GetAsync($"CardLine/Id/{existing.Id}");
                if (reread.StatusCode == HttpStatusCode.OK)
                    saved = JsonConvert.DeserializeObject<CardLine>(await reread.Content.ReadAsStringAsync()) ?? existing;

                return Json(new
                {
                    success = true,
                    locationName = saved.LocationName,
                    rowNumber = saved.RowNumber,
                    terminalNumberPositive = saved.TerminalNumberPositive,
                    terminalNumberNegative = saved.TerminalNumberNegative,
                    currentSequenceNumber = saved.CurrentSequenceNumber
                });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // ── Card-line wiring (same flow as 90 Yard Config → Create → card → line popup) ──────────

        // ── Yard Config tab: clusters of the station with full detail + wired counts ─────────
        public async Task<JsonResult> GetYardClusters(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (siteId <= 0) return Json(new { error = "Select a station first." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                var response = await hcf.Client.GetAsync($"Cluster/SiteId/{siteId}");
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load clusters." });
                var list = JsonConvert.DeserializeObject<List<Cluster>>(await response.Content.ReadAsStringAsync()) ?? new();

                // Full detail per cluster (TypeId / power / modem / IP are not on the list projection)
                var full = await Task.WhenAll(list.Select(c => GetClusterFromApi(hcf, c.Id)));

                // Wired lines per cluster + distinct assets, from one site-wide call
                var wiredLines = new Dictionary<int, int>();
                var assetSets = new Dictionary<int, HashSet<int>>();
                var clResp = await hcf.Client.GetAsync($"CardLine/SiteId/{siteId}");
                if (clResp.StatusCode == HttpStatusCode.OK)
                {
                    var lines = JsonConvert.DeserializeObject<List<CardLine>>(await clResp.Content.ReadAsStringAsync()) ?? new();
                    foreach (var l in lines.Where(x => x.ClusterId.HasValue && x.AssetId > 0))
                    {
                        wiredLines[l.ClusterId!.Value] = wiredLines.GetValueOrDefault(l.ClusterId.Value) + 1;
                        if (!assetSets.TryGetValue(l.ClusterId.Value, out var set)) assetSets[l.ClusterId.Value] = set = new();
                        set.Add(l.AssetId!.Value);
                    }
                }

                var result = full.Where(c => c != null).Select(c =>
                {
                    var (rf, loc) = SplitClusterName(c!.Name);
                    return new
                    {
                        id = c.Id,
                        name = c.Name,
                        rfCluster = rf,
                        location = loc,
                        typeId = c.TypeId,               // 1 Indoor, 2 Outdoor
                        powerSupplyId = c.PowerSupplyId, // 1 24V, 2 110V, 3 110→24V
                        modemId = c.ModemId,
                        ip = c.IP,
                        wiredLines = wiredLines.GetValueOrDefault(c.Id),
                        assetCount = assetSets.TryGetValue(c.Id, out var s) ? s.Count : 0
                    };
                }).OrderBy(x => x.rfCluster ?? int.MaxValue).ThenBy(x => x.location).ToList();

                return Json(new { clusters = result });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // Clusters of the station (Cluster/SiteId) for the Wire form
        public async Task<JsonResult> GetWireClusters(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var response = await hcf.Client.GetAsync($"Cluster/SiteId/{siteId}");
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load clusters." });
                var list = JsonConvert.DeserializeObject<List<Cluster>>(await response.Content.ReadAsStringAsync()) ?? new();

                // Wired-line count per cluster (one call for the whole station)
                var wired = new Dictionary<int, int>();
                var clResp = await hcf.Client.GetAsync($"CardLine/SiteId/{siteId}");
                if (clResp.StatusCode == HttpStatusCode.OK)
                {
                    var lines = JsonConvert.DeserializeObject<List<CardLine>>(await clResp.Content.ReadAsStringAsync()) ?? new();
                    foreach (var g in lines.Where(l => l.ClusterId.HasValue && l.AssetId > 0).GroupBy(l => l.ClusterId!.Value))
                        wired[g.Key] = g.Count();
                }
                return Json(list.OrderBy(c => c.Name).Select(c => new
                {
                    id = c.Id, name = c.Name, wired = wired.TryGetValue(c.Id, out var n) ? n : 0
                }));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // Cards + ADCs of one cluster (Card/ClusterId, ADC/ClusterId)
        public async Task<JsonResult> GetWireCards(int clusterId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                var cardResp = await hcf.Client.GetAsync($"Card/ClusterId/{clusterId}");
                if (cardResp.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load cards." });
                var cards = JsonConvert.DeserializeObject<List<Card>>(await cardResp.Content.ReadAsStringAsync()) ?? new();

                var adcs = new List<ADC>();
                var adcResp = await hcf.Client.GetAsync($"ADC/ClusterId/{clusterId}");
                if (adcResp.StatusCode == HttpStatusCode.OK)
                    adcs = JsonConvert.DeserializeObject<List<ADC>>(await adcResp.Content.ReadAsStringAsync()) ?? new();

                // Lines of every card (CardLine/CardId also returns empty lines) → used / free + who is on each
                var linesPerCard = await Task.WhenAll(cards.Select(async c =>
                {
                    var r = await hcf.Client.GetAsync($"CardLine/CardId/{c.Id}");
                    var l = r.StatusCode == HttpStatusCode.OK
                        ? JsonConvert.DeserializeObject<List<CardLine>>(await r.Content.ReadAsStringAsync()) ?? new()
                        : new List<CardLine>();
                    return (cardId: c.Id, lines: l);
                }));
                var lineMap = linesPerCard.ToDictionary(x => x.cardId, x => x.lines);

                return Json(new
                {
                    cards = cards
                        .OrderBy(c => CardSortKey(c.CardName))
                        .Select(c =>
                        {
                            var l = lineMap.TryGetValue(c.Id, out var v) ? v : new List<CardLine>();
                            return new
                            {
                                id = c.Id,
                                name = c.CardName,
                                total = l.Count,
                                used = l.Count(x => x.AssetId > 0),
                                lines = l.OrderBy(x => x.Number).Select(x => new
                                {
                                    id = x.Id,
                                    number = x.Number,
                                    used = x.AssetId > 0,
                                    assetName = x.AssetName,
                                    attributeName = x.AttributeName,
                                    pin = x.Pin,
                                    adc = x.ADCId > 0 && x.SequenceNumber.HasValue ? $"ADC{x.SequenceNumber}" : null,
                                    // IDs so the card-line mapping popup can pre-select the dropdowns
                                    assetTypeId = x.AssetTypeId,
                                    assetId = x.AssetId,
                                    attributeId = x.AttributeId,
                                    adcId = x.ADCId
                                })
                            };
                        }),
                    adcs = adcs.OrderBy(a => a.SequenceNumber)
                               .Select(a => new { id = a.Id, name = $"ADC{a.SequenceNumber} ({a.ADCNumber})" })
                });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public class AddCardRequest
        {
            public int SiteId { get; set; }
            public int ClusterId { get; set; }
            public string CardType { get; set; } = "";   // C | V5 | V82 | MintCard | DI
        }

        // Add the next card of one type to a cluster, the way the 90 Yard Config cluster popup does:
        // re-save the cluster with that card number in its *Numbers list → API ClusterService.Save
        // creates the card and its empty lines (C 10, V5 5, V82 10, MintCard 10, DI 10).
        [HttpPost]
        public async Task<JsonResult> AddClusterCard([FromBody] AddCardRequest m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0 || m.ClusterId <= 0) return Json(new { error = "Select a cluster first." });

                var prefix = m.CardType switch
                {
                    "C" => "C-", "V5" => "V5-", "V82" => "V82-", "MintCard" => "MintCard-", "DI" => "DI-", _ => null
                };
                if (prefix == null) return Json(new { error = "Select a card type." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                // API Cluster save overwrites every column (modem, IP…), so start from the stored row
                var cluster = await GetClusterFromApi(hcf, m.ClusterId);
                if (cluster == null || cluster.Id != m.ClusterId || cluster.SiteId != m.SiteId)
                    return Json(new { error = "Could not load the cluster." });

                var cardResp = await hcf.Client.GetAsync($"Card/ClusterId/{m.ClusterId}");
                var cards = cardResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<Card>>(await cardResp.Content.ReadAsStringAsync()) ?? new()
                    : new List<Card>();
                var existingNos = cards
                    .Select(c => c.CardName ?? "")
                    .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(n => int.TryParse(n.Substring(prefix.Length), out var no) ? no : 0)
                    .Where(no => no > 0).ToList();
                var next = existingNos.Count == 0 ? 1 : existingNos.Max() + 1;
                var newName = prefix + next;

                // Only the chosen type gets a list; the API skips a type whose list is empty.
                // Its count must not be below the cards already there, or the API skips creation.
                cluster.CurrentCardNumbers = new(); cluster.V5Numbers = new(); cluster.V82Numbers = new();
                cluster.MintCardNumbers = new(); cluster.DINumbers = new();
                var count = Math.Max(existingNos.Count + 1, 1);
                switch (m.CardType)
                {
                    case "C":        cluster.CurrentCardNumbers = new() { next }; cluster.CurrentCard = Math.Max(cluster.CurrentCard, count); break;
                    case "V5":       cluster.V5Numbers = new() { next };          cluster.V5 = Math.Max(cluster.V5, count); break;
                    case "V82":      cluster.V82Numbers = new() { next };         cluster.V82 = Math.Max(cluster.V82, count); break;
                    case "MintCard": cluster.MintCardNumbers = new() { next };    cluster.MintCard = Math.Max(cluster.MintCard, count); break;
                    case "DI":       cluster.DINumbers = new() { next };          cluster.DI = Math.Max(cluster.DI, count); break;
                }
                cluster.CreatedBy = loginUser.Id;

                using var str = new StringContent(JsonConvert.SerializeObject(cluster), Encoding.UTF8, "application/json");
                var response = await hcf.Client.PostAsync("Cluster", str);
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = await ApiErrorMessage(response) });

                // Return the refreshed card list so the Wire form can select the new card
                var after = await hcf.Client.GetAsync($"Card/ClusterId/{m.ClusterId}");
                var list = after.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<Card>>(await after.Content.ReadAsStringAsync()) ?? new()
                    : new List<Card>();
                var created = list.FirstOrDefault(c => string.Equals(c.CardName, newName, StringComparison.OrdinalIgnoreCase));
                if (created == null) return Json(new { error = $"Card {newName} was not created." });
                return Json(new { success = true, cardId = created.Id, cards = list.Select(c => new { id = c.Id, name = c.CardName }) });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public class AddAdcRequest
        {
            public int SiteId { get; set; }
            public int ClusterId { get; set; }
            public int AdcNumber { get; set; }
            public int AdcTypeId { get; set; }   // 1 Special 1056, 2 Normal, 3 Special 1507
        }

        // Add an ADC to a cluster (same as 90 Yard Config cluster popup → POST api/ADC).
        // SequenceNumber is auto-assigned as the next in the cluster.
        [HttpPost]
        public async Task<JsonResult> AddClusterAdc([FromBody] AddAdcRequest m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0 || m.ClusterId <= 0) return Json(new { error = "Select a cluster first." });
                if (m.AdcNumber <= 0) return Json(new { error = "Enter a valid ADC number." });
                if (m.AdcTypeId < 1 || m.AdcTypeId > 3) return Json(new { error = "Select the ADC type." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                var cluster = await GetClusterFromApi(hcf, m.ClusterId);
                if (cluster == null || cluster.Id != m.ClusterId || cluster.SiteId != m.SiteId)
                    return Json(new { error = "Cluster not found on this station." });

                var listResp = await hcf.Client.GetAsync($"ADC/ClusterId/{m.ClusterId}");
                var adcs = listResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<ADC>>(await listResp.Content.ReadAsStringAsync()) ?? new()
                    : new List<ADC>();
                var nextSeq = adcs.Count == 0 ? 1 : adcs.Max(a => a.SequenceNumber) + 1;

                var row = new ADC
                {
                    ClusterId = m.ClusterId, SiteId = m.SiteId, SequenceNumber = nextSeq,
                    ADCNumber = m.AdcNumber, ADCTypeId = m.AdcTypeId, CreatedBy = loginUser.Id
                };
                using var str = new StringContent(JsonConvert.SerializeObject(row), Encoding.UTF8, "application/json");
                var response = await hcf.Client.PostAsync("ADC", str);
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = await ApiErrorMessage(response) });

                var after = await hcf.Client.GetAsync($"ADC/ClusterId/{m.ClusterId}");
                var list = after.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<ADC>>(await after.Content.ReadAsStringAsync()) ?? new()
                    : new List<ADC>();
                return Json(new
                {
                    success = true,
                    adcs = list.OrderBy(a => a.SequenceNumber).Select(a => new { id = a.Id, name = $"ADC{a.SequenceNumber} ({a.ADCNumber})" })
                });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public class GenerateYardRequest
        {
            public int SiteId { get; set; }
            public int ClusterId { get; set; }
        }

        // ══ PERSIST: generate cards + ADCs + card-line wiring for ONE cluster from the survey ═══════
        // Scope: selected cluster only, and only when it has NO cards yet (create-only, no clashes).
        // Wires each asset's own physical attributes — V = voltage, I = title contains "mA".
        // DB pin rules (PinRuleError): MintCard 1-8 = voltage, MintCard 9-10 = current, C card = current.
        // So NO buckel here (current never on MintCard 1-8): current → MintCard 9-10 then CI cards.
        [HttpPost]
        public async Task<JsonResult> GenerateYardForCluster([FromBody] GenerateYardRequest m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0 || m.ClusterId <= 0) return Json(new { error = "Select a cluster first." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                var cluster = await GetClusterFromApi(hcf, m.ClusterId);
                if (cluster == null || cluster.Id != m.ClusterId || cluster.SiteId != m.SiteId)
                    return Json(new { error = "Cluster not found on this station." });

                // Only an empty cluster (so we never clash with manually-added cards)
                var curCardsResp = await hcf.Client.GetAsync($"Card/ClusterId/{m.ClusterId}");
                var curCards = curCardsResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<Card>>(await curCardsResp.Content.ReadAsStringAsync()) ?? new() : new();
                if (curCards.Count > 0)
                    return Json(new { error = "This cluster already has cards. Generation runs only on an empty cluster — clear its cards or wire manually." });

                // Survey location matching this cluster → gear names → (assetType, assetName)
                var (_, locName) = SplitClusterName(cluster.Name);
                var rows = await GetSiteSurveyRows(hcf, m.SiteId);
                var row = rows.FirstOrDefault(r => string.Equals((r.LocationBoxNo ?? "").Trim(), locName, StringComparison.OrdinalIgnoreCase));
                if (row == null) return Json(new { error = $"No survey location named \"{locName}\". Add it on the Survey tab first." });

                string TrackBase(string n) => System.Text.RegularExpressions.Regex.Replace(n.Trim(), "T[FR]$", "T");
                var want = new List<(int type, string name)>();
                foreach (var n in SurveyNames(row.TF)) want.Add((1, TrackBase(n)));
                foreach (var n in SurveyNames(row.TR)) want.Add((1, TrackBase(n)));
                foreach (var e in SurveyNames(row.Signal)) want.Add((2, e.Split(' ')[0]));
                foreach (var n in SurveyNames(row.PointMachine)) want.Add((3, n));
                foreach (var n in SurveyNames(row.AxleCounter)) want.Add((4, n));
                foreach (var n in SurveyNames(row.BusBar)) want.Add((14, n));
                foreach (var n in SurveyNames(row.UFSBI)) want.Add((23, n));
                foreach (var n in SurveyNames(row.BHMS)) want.Add((28, n));
                want = want.GroupBy(x => x.type + ":" + x.name.ToUpperInvariant()).Select(g => g.First()).ToList();
                if (want.Count == 0) return Json(new { error = "This location has no wireable gears in the survey." });

                // Match gears to real assets
                var asResp = await hcf.Client.GetAsync($"Asset/GetAll/{m.SiteId}");
                var assets = asResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<Asset>>(await asResp.Content.ReadAsStringAsync()) ?? new() : new();
                var assetByKey = assets.GroupBy(a => a.AssetTypeId + ":" + (a.Name ?? "").Trim().ToUpperInvariant())
                                       .ToDictionary(g => g.Key, g => g.First());
                var matched = new List<Asset>(); var missingAssets = new List<string>();
                foreach (var (type, name) in want)
                    if (assetByKey.TryGetValue(type + ":" + name.ToUpperInvariant(), out var a)) matched.Add(a);
                    else missingAssets.Add(name);
                if (matched.Count == 0) return Json(new { error = "None of this location's gears have matching assets yet. Save the survey first (it creates the assets)." });

                // Already-wired attributes anywhere on the station → skip
                var siteLinesResp = await hcf.Client.GetAsync($"CardLine/SiteId/{m.SiteId}");
                var siteLines = siteLinesResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<CardLine>>(await siteLinesResp.Content.ReadAsStringAsync()) ?? new() : new();
                var wiredSet = siteLines.Where(l => l.AssetId > 0 && l.AttributeId > 0)
                                        .Select(l => l.AssetId + ":" + l.AttributeId).ToHashSet();

                // Each matched asset's physical attributes, classified V/I
                var volt = new Queue<(int assetId, int typeId, int attrId)>();
                var curr = new Queue<(int assetId, int typeId, int attrId)>();
                var attrCache = new Dictionary<int, List<AssetAttribute>>();
                foreach (var a in matched)
                {
                    if (!attrCache.TryGetValue(a.AssetTypeId, out var attrs))
                    {
                        var ar = await hcf.Client.GetAsync($"AssetAttribute/AssetTypeId/{a.AssetTypeId}");
                        attrs = ar.StatusCode == HttpStatusCode.OK
                            ? JsonConvert.DeserializeObject<List<AssetAttribute>>(await ar.Content.ReadAsStringAsync()) ?? new() : new();
                        attrCache[a.AssetTypeId] = attrs;
                    }
                    foreach (var at in attrs)
                    {
                        if (at.IsDerived == true) continue;                       // calculated, not wireable
                        if (wiredSet.Contains(a.Id + ":" + at.Id)) continue;      // already wired
                        if ((at.Title ?? "").ToLower().Contains("ma")) curr.Enqueue((a.Id, a.AssetTypeId, at.Id));
                        else volt.Enqueue((a.Id, a.AssetTypeId, at.Id));
                    }
                }
                int V = volt.Count, C = curr.Count;
                if (V + C == 0) return Json(new { error = "Every attribute of this location's assets is already wired." });

                // DB-valid card sizing (no buckel): MintCard current capacity = 2 per card (pins 9-10)
                int ai = V > 0 ? (int)Math.Ceiling(V / 8.0) : 0;
                int ci = ai == 0 ? (int)Math.Ceiling(C / 10.0)
                                 : (C <= 2 * ai ? 0 : (int)Math.Ceiling((C - 2 * ai) / 10.0));
                int totalCards = ai + ci;

                // Create the cards in one Cluster save (API makes each card with its empty lines)
                cluster.CurrentCardNumbers = new(); cluster.V5Numbers = new(); cluster.V82Numbers = new();
                cluster.MintCardNumbers = new(); cluster.DINumbers = new();
                if (ai > 0) { cluster.MintCardNumbers = Enumerable.Range(1, ai).ToList(); cluster.MintCard = Math.Max(cluster.MintCard, ai); }
                if (ci > 0) { cluster.CurrentCardNumbers = Enumerable.Range(1, ci).ToList(); cluster.CurrentCard = Math.Max(cluster.CurrentCard, ci); }
                cluster.CreatedBy = loginUser.Id;
                using (var cstr = new StringContent(JsonConvert.SerializeObject(cluster), Encoding.UTF8, "application/json"))
                {
                    var cr = await hcf.Client.PostAsync("Cluster", cstr);
                    if (cr.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not create cards: " + await ApiErrorMessage(cr) });
                }

                // One ADC per card (SequenceNumber auto = 1..totalCards)
                for (int i = 1; i <= totalCards; i++)
                {
                    var adc = new ADC { ClusterId = m.ClusterId, SiteId = m.SiteId, SequenceNumber = i, ADCNumber = i, ADCTypeId = 2, CreatedBy = loginUser.Id };
                    using var astr = new StringContent(JsonConvert.SerializeObject(adc), Encoding.UTF8, "application/json");
                    await hcf.Client.PostAsync("ADC", astr);   // best-effort
                }

                // Re-read cards, lines, ADCs
                var cards = JsonConvert.DeserializeObject<List<Card>>(await (await hcf.Client.GetAsync($"Card/ClusterId/{m.ClusterId}")).Content.ReadAsStringAsync()) ?? new();
                var adcs = JsonConvert.DeserializeObject<List<ADC>>(await (await hcf.Client.GetAsync($"ADC/ClusterId/{m.ClusterId}")).Content.ReadAsStringAsync()) ?? new();
                var adcBySeq = adcs.GroupBy(a => a.SequenceNumber).ToDictionary(g => g.Key, g => g.First().Id);
                Card Named(string nm) => cards.FirstOrDefault(c => string.Equals(c.CardName, nm, StringComparison.OrdinalIgnoreCase));
                var mintCards = Enumerable.Range(1, ai).Select(i => Named("MintCard-" + i)).Where(c => c != null).ToList();
                var ciCards = Enumerable.Range(1, ci).Select(i => Named("C-" + i)).Where(c => c != null).ToList();

                var linesByCard = new Dictionary<int, List<CardLine>>();
                foreach (var c in mintCards.Concat(ciCards))
                    linesByCard[c.Id] = JsonConvert.DeserializeObject<List<CardLine>>(await (await hcf.Client.GetAsync($"CardLine/CardId/{c.Id}")).Content.ReadAsStringAsync()) ?? new();

                int wired = 0, failed = 0;
                async Task<bool> WirePin(Card card, int seq, int pin, (int assetId, int typeId, int attrId) inp)
                {
                    var line = linesByCard[card.Id].FirstOrDefault(l => l.Number == pin && !(l.AssetId > 0));
                    if (line == null) return false;
                    int? adcId = adcBySeq.TryGetValue(seq, out var aid) ? aid : (int?)null;
                    var rowCL = new CardLine
                    {
                        Id = line.Id, CardId = card.Id, Number = line.Number, CreatedBy = loginUser.Id, SiteId = m.SiteId,
                        AssetTypeId = inp.typeId, AssetId = inp.assetId, AttributeId = inp.attrId, ADCId = adcId, Pin = pin
                    };
                    using var str = new StringContent(JsonConvert.SerializeObject(rowCL), Encoding.UTF8, "application/json");
                    var resp = await hcf.Client.PostAsync("CardLine", str);
                    if (resp.StatusCode == HttpStatusCode.OK) { line.AssetId = inp.assetId; return true; }
                    return false;
                }

                // voltage → MintCard pins 1-8
                for (int ci2 = 0; ci2 < mintCards.Count; ci2++)
                    for (int pin = 1; pin <= 8 && volt.Count > 0; pin++)
                    { var ok = await WirePin(mintCards[ci2], ci2 + 1, pin, volt.Peek()); volt.Dequeue(); if (ok) wired++; else failed++; }
                // current → MintCard pins 9-10
                for (int ci2 = 0; ci2 < mintCards.Count; ci2++)
                    for (int pin = 9; pin <= 10 && curr.Count > 0; pin++)
                    { var ok = await WirePin(mintCards[ci2], ci2 + 1, pin, curr.Peek()); curr.Dequeue(); if (ok) wired++; else failed++; }
                // current → CI cards pins 1-10
                for (int k = 0; k < ciCards.Count; k++)
                    for (int pin = 1; pin <= 10 && curr.Count > 0; pin++)
                    { var ok = await WirePin(ciCards[k], ai + k + 1, pin, curr.Peek()); curr.Dequeue(); if (ok) wired++; else failed++; }

                return Json(new
                {
                    success = true,
                    cards = totalCards,
                    adcs = totalCards,
                    wired,
                    failed,
                    leftover = volt.Count + curr.Count,
                    missingAssets = missingAssets.Distinct().ToList()
                });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // "C-2" → ("C-", 2) so cards sort C-1, C-2 … C-10, then V5-1 …
        private static (string, int) CardSortKey(string? name)
        {
            name ??= "";
            var dash = name.LastIndexOf('-');
            return dash >= 0 && int.TryParse(name[(dash + 1)..], out var n) ? (name[..(dash + 1)], n) : (name, 0);
        }

        // Free (not yet assigned) lines of one card (CardLine/CardId)
        public async Task<JsonResult> GetWireLines(int cardId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var response = await hcf.Client.GetAsync($"CardLine/CardId/{cardId}");
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load card lines." });
                var lines = JsonConvert.DeserializeObject<List<CardLine>>(await response.Content.ReadAsStringAsync()) ?? new();
                return Json(lines.Where(l => !(l.AssetId > 0)).OrderBy(l => l.Number)
                                 .Select(l => new { id = l.Id, number = l.Number }));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public class WireCardLineRequest
        {
            public int SiteId { get; set; }
            public int ClusterId { get; set; }
            public int CardId { get; set; }
            public int CardLineId { get; set; }
            public int AssetTypeId { get; set; }
            public int AssetId { get; set; }
            public int AttributeId { get; set; }
            public int? ADCId { get; set; }
            public int? Pin { get; set; }
        }

        // Assign Asset + Attribute (+ ADC/Pin) to a free card line → POST api/CardLine (90 CreateCardLine)
        [HttpPost]
        public async Task<JsonResult> WireCardLine([FromBody] WireCardLineRequest m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0) return Json(new { error = "Select a station first." });
                if (m.ClusterId <= 0 || m.CardId <= 0 || m.CardLineId <= 0) return Json(new { error = "Select a cluster, card and line." });
                if (m.AssetId <= 0 || m.AttributeId <= 0 || m.AssetTypeId <= 0) return Json(new { error = "Asset / attribute missing." });
                if ((m.ADCId > 0) != (m.Pin > 0)) return Json(new { error = "Select both ADC and Pin, or neither." });
                if (m.Pin.HasValue && (m.Pin < 1 || m.Pin > 10)) return Json(new { error = "Pin must be 1–10." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                // The target line must be on this station's cluster → card, and still be free.
                // Not via CardLine/Id: that API query drops a line with no asset type yet (it filters
                // assetType.IsActive), so an empty line comes back as null. CardLine/CardId returns empty
                // lines too (it is what the 90 card-line popup uses).
                var cluster = await GetClusterFromApi(hcf, m.ClusterId);
                if (cluster == null || cluster.Id != m.ClusterId || cluster.SiteId != m.SiteId)
                    return Json(new { error = "Cluster not found on this station." });
                var cardResp = await hcf.Client.GetAsync($"Card/ClusterId/{m.ClusterId}");
                var clusterCards = cardResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<Card>>(await cardResp.Content.ReadAsStringAsync()) ?? new()
                    : new List<Card>();
                if (!clusterCards.Any(c => c.Id == m.CardId)) return Json(new { error = "Card not found in this cluster." });

                var linesResp = await hcf.Client.GetAsync($"CardLine/CardId/{m.CardId}");
                if (linesResp.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load the card's lines." });
                var cardLines = JsonConvert.DeserializeObject<List<CardLine>>(await linesResp.Content.ReadAsStringAsync()) ?? new();
                var line = cardLines.FirstOrDefault(l => l.Id == m.CardLineId);
                if (line == null) return Json(new { error = "Card line not found on this card." });
                if (line.AssetId > 0) return Json(new { error = "This line is already used by another asset. Pick a free line." });
                line.CardId = m.CardId;   // the fallback rows of CardLine/CardId may omit it

                // Card pin rules (C = current only; MintCard 1-8 voltage, 9-10 current)
                var pinMap = await TryGetSensorMapByAttribute(hcf);
                var onCard = cardLines.Where(l => l.Id != line.Id && l.AssetId > 0 && l.AttributeId > 0)
                                      .Select(l => (l.Number, l.AttributeId!.Value)).ToList();
                onCard.Add((line.Number, m.AttributeId));
                var pinError = PinRuleError(clusterCards.First(c => c.Id == m.CardId).CardName, onCard, pinMap);
                if (pinError != null) return Json(new { error = pinError });

                // Same duplicate rules as API CardLineService.Save — checked here because the API
                // turns them into a bare 500 "Internal Server Error"
                var siteResp = await hcf.Client.GetAsync($"CardLine/SiteId/{m.SiteId}");
                if (siteResp.StatusCode == HttpStatusCode.OK)
                {
                    var siteLines = JsonConvert.DeserializeObject<List<CardLine>>(await siteResp.Content.ReadAsStringAsync()) ?? new();
                    var dupAttr = siteLines.FirstOrDefault(x => x.Id != line.Id && x.AssetId == m.AssetId && x.AttributeId == m.AttributeId);
                    if (dupAttr != null)
                        return Json(new { error = $"This attribute is already wired: {dupAttr.ClusterName} / {dupAttr.CardName} / line {dupAttr.Number}." });
                    if (m.ADCId > 0)
                    {
                        var dupPin = siteLines.FirstOrDefault(x => x.Id != line.Id && x.ADCId == m.ADCId && x.Pin == m.Pin);
                        if (dupPin != null)
                            return Json(new { error = $"Pin-{m.Pin} of this ADC is already used in {dupPin.CardName} (line {dupPin.Number})." });
                    }
                }

                var row = new CardLine
                {
                    Id = line.Id, CardId = line.CardId, Number = line.Number, CreatedBy = loginUser.Id,
                    SiteId = m.SiteId,   // send SiteId in the API request too
                    AssetTypeId = m.AssetTypeId, AssetId = m.AssetId, AttributeId = m.AttributeId,
                    ADCId = m.ADCId > 0 ? m.ADCId : null, Pin = m.Pin > 0 ? m.Pin : null
                };
                using var str = new StringContent(JsonConvert.SerializeObject(row), Encoding.UTF8, "application/json");
                var response = await hcf.Client.PostAsync("CardLine", str);
                if (response.StatusCode != HttpStatusCode.OK) return Json(new { error = await ApiErrorMessage(response) });
                return Json(new { success = true, id = line.Id });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public class LineMap
        {
            public int Id { get; set; }                 // card line id
            public int? AssetTypeId { get; set; }
            public int? AssetId { get; set; }
            public int? AttributeId { get; set; }
            public int? ADCId { get; set; }
            public int? Pin { get; set; }
        }
        public class SaveCardLinesRequest
        {
            public int SiteId { get; set; }
            public int ClusterId { get; set; }
            public int CardId { get; set; }
            public List<LineMap> Lines { get; set; } = new();
        }

        // Map / re-map / clear all lines of one card in a single Save, the way the 90 Yard Config
        // card-line popup (fnEditCardLine) does. Each changed line is posted to api/CardLine.
        [HttpPost]
        public async Task<JsonResult> SaveCardLines([FromBody] SaveCardLinesRequest m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0 || m.ClusterId <= 0 || m.CardId <= 0) return Json(new { error = "Select a card first." });
                if (m.Lines == null || m.Lines.Count == 0) return Json(new { error = "Nothing to save." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                var cluster = await GetClusterFromApi(hcf, m.ClusterId);
                if (cluster == null || cluster.Id != m.ClusterId || cluster.SiteId != m.SiteId)
                    return Json(new { error = "Cluster not found on this station." });

                var cardResp = await hcf.Client.GetAsync($"Card/ClusterId/{m.ClusterId}");
                var clusterCards = cardResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<Card>>(await cardResp.Content.ReadAsStringAsync()) ?? new()
                    : new List<Card>();
                if (!clusterCards.Any(c => c.Id == m.CardId)) return Json(new { error = "Card not found in this cluster." });

                var linesResp = await hcf.Client.GetAsync($"CardLine/CardId/{m.CardId}");
                if (linesResp.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load the card's lines." });
                var cardLines = JsonConvert.DeserializeObject<List<CardLine>>(await linesResp.Content.ReadAsStringAsync()) ?? new();

                var siteResp = await hcf.Client.GetAsync($"CardLine/SiteId/{m.SiteId}");
                var siteLines = siteResp.StatusCode == HttpStatusCode.OK
                    ? JsonConvert.DeserializeObject<List<CardLine>>(await siteResp.Content.ReadAsStringAsync()) ?? new()
                    : new List<CardLine>();

                // Normalise each requested line to this card, and work out what actually changed
                var cardLineIds = cardLines.Select(l => l.Id).ToHashSet();
                var planned = new List<(CardLine existing, LineMap want, bool wire)>();
                foreach (var w in m.Lines)
                {
                    var existing = cardLines.FirstOrDefault(l => l.Id == w.Id);
                    if (existing == null) continue;   // not a line of this card — skip silently
                    bool wire = w.AssetId > 0 && w.AttributeId > 0 && w.AssetTypeId > 0;
                    if (wire && (w.ADCId > 0) != (w.Pin > 0)) return Json(new { error = $"Line {existing.Number}: select both ADC and Pin, or neither." });
                    if (wire && w.Pin.HasValue && (w.Pin < 1 || w.Pin > 10)) return Json(new { error = $"Line {existing.Number}: Pin must be 1–10." });
                    planned.Add((existing, w, wire));
                }

                // Duplicate checks across the final desired state (within the batch + the rest of the station)
                var otherSiteLines = siteLines.Where(l => !cardLineIds.Contains(l.Id)).ToList();
                var seenAttr = new HashSet<string>();
                var seenPin = new HashSet<string>();
                foreach (var (existing, w, wire) in planned)
                {
                    if (!wire) continue;
                    var ak = $"{w.AssetId}:{w.AttributeId}";
                    if (!seenAttr.Add(ak)) return Json(new { error = $"Line {existing.Number}: this asset + attribute is used on two lines of this card." });
                    if (otherSiteLines.Any(l => l.AssetId == w.AssetId && l.AttributeId == w.AttributeId))
                        return Json(new { error = $"Line {existing.Number}: this attribute is already wired elsewhere on the station." });
                    if (w.ADCId > 0)
                    {
                        var pk = $"{w.ADCId}:{w.Pin}";
                        if (!seenPin.Add(pk)) return Json(new { error = $"Line {existing.Number}: this ADC + Pin is used on two lines." });
                        if (otherSiteLines.Any(l => l.ADCId == w.ADCId && l.Pin == w.Pin))
                            return Json(new { error = $"Line {existing.Number}: this ADC + Pin is already used elsewhere." });
                    }
                }

                // Card pin rules on the card's final state (C = current only; MintCard 1-8 voltage, 9-10 current)
                var pinMap = await TryGetSensorMapByAttribute(hcf);
                var finalOnCard = cardLines.Select(l =>
                {
                    var p = planned.FirstOrDefault(x => x.existing.Id == l.Id);
                    if (p.existing != null) return p.wire ? (l.Number, p.want.AttributeId) : (l.Number, (int?)null);
                    return (l.Number, l.AssetId > 0 ? l.AttributeId : null);
                }).Where(x => x.Item2 > 0).Select(x => (x.Number, x.Item2!.Value)).ToList();
                var pinError = PinRuleError(clusterCards.First(c => c.Id == m.CardId).CardName, finalOnCard, pinMap);
                if (pinError != null) return Json(new { error = pinError });

                // Apply only the lines whose mapping changed
                int changed = 0;
                foreach (var (existing, w, wire) in planned)
                {
                    bool wasWired = existing.AssetId > 0;
                    bool same = wasWired == wire
                        && existing.AssetId == (wire ? w.AssetId : null)
                        && existing.AttributeId == (wire ? w.AttributeId : null)
                        && existing.ADCId == (wire && w.ADCId > 0 ? w.ADCId : null)
                        && existing.Pin == (wire && w.Pin > 0 ? w.Pin : null);
                    if (same) continue;

                    var row = new CardLine
                    {
                        Id = existing.Id, CardId = m.CardId, Number = existing.Number, CreatedBy = loginUser.Id, SiteId = m.SiteId,
                        AssetTypeId = wire ? w.AssetTypeId : null,
                        AssetId = wire ? w.AssetId : null,
                        AttributeId = wire ? w.AttributeId : null,
                        ADCId = wire && w.ADCId > 0 ? w.ADCId : null,
                        Pin = wire && w.Pin > 0 ? w.Pin : null
                    };
                    using var str = new StringContent(JsonConvert.SerializeObject(row), Encoding.UTF8, "application/json");
                    var resp = await hcf.Client.PostAsync("CardLine", str);
                    if (resp.StatusCode != HttpStatusCode.OK)
                        return Json(new { error = $"Line {existing.Number}: {await ApiErrorMessage(resp)}" });
                    changed++;
                }

                return Json(new { success = true, changed });
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

        // ══ Material Positioning (survey → AttributeSensorMapping → cards / A10 / ADC) ══════════

        private static async Task<List<AttributeSensorMapping>> GetSensorMapping(HttpClientHelper hcf)
        {
            var response = await hcf.Client.GetAsync("AssetAttribute/GetSensorMapping");
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Could not load the attribute sensor mapping from the API.");
            return JsonConvert.DeserializeObject<List<AttributeSensorMapping>>(await response.Content.ReadAsStringAsync()) ?? new();
        }

        // Pin-rule check must not block wiring while the API endpoint is not deployed yet
        private static async Task<Dictionary<int, AttributeSensorMapping>> TryGetSensorMapByAttribute(HttpClientHelper hcf)
        {
            try
            {
                return (await GetSensorMapping(hcf)).GroupBy(m => m.AssetAttributeId).ToDictionary(g => g.Key, g => g.First());
            }
            catch { return new(); }
        }

        private async Task<StationMaterialPositioning> BuildMaterialPositioning(HttpClientHelper hcf, int siteId, decimal sparePercent, User loginUser)
        {
            var map = await GetSensorMapping(hcf);
            var rows = await GetSiteSurveyRows(hcf, siteId);

            var pms = new List<SurveyPointMachine>();
            var pmResp = await hcf.Client.GetAsync($"SiteSurvey/GetPM/SiteId/{siteId}");
            if (pmResp.StatusCode == HttpStatusCode.OK)
                pms = JsonConvert.DeserializeObject<List<SurveyPointMachine>>(await pmResp.Content.ReadAsStringAsync()) ?? new();

            // Yard Config clusters ("cluster_{RF}/{location}") and wired lines → actual figures
            var clusters = new List<Cluster>();
            var clResp = await hcf.Client.GetAsync($"Cluster/SiteId/{siteId}");
            if (clResp.StatusCode == HttpStatusCode.OK)
                clusters = JsonConvert.DeserializeObject<List<Cluster>>(await clResp.Content.ReadAsStringAsync()) ?? new();
            var fullClusters = (await Task.WhenAll(clusters.Select(c => GetClusterFromApi(hcf, c.Id)))).Where(c => c != null).Select(c => c!).ToList();
            var lines = new List<CardLine>();
            var lnResp = await hcf.Client.GetAsync($"CardLine/SiteId/{siteId}");
            if (lnResp.StatusCode == HttpStatusCode.OK)
                lines = JsonConvert.DeserializeObject<List<CardLine>>(await lnResp.Content.ReadAsStringAsync()) ?? new();
            var mapByAttr = map.GroupBy(m => m.AssetAttributeId).ToDictionary(g => g.Key, g => g.First());

            var site = _siteService.Get(siteId);
            var result = new StationMaterialPositioning
            {
                SiteId = siteId,
                StationName = site?.Name ?? "",
                StationCode = site?.StationCode ?? "",
                ZoneDivision = string.Join(" / ", new[] { site?.ZoneName, site?.DivisionName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                PreparedBy = string.IsNullOrWhiteSpace(loginUser.Name) ? (loginUser.UserName ?? "") : loginUser.Name,
                GeneratedAt = DateTime.Now,
                SparePercent = sparePercent,
                MappingRows = map.Count
            };
            if (map.Count == 0) result.Warnings.Add("AttributeSensorMapping returned no rows — nothing can be calculated.");

            foreach (var row in rows.OrderBy(r => r.Cluster).ThenBy(r => r.LocationBoxNo))
            {
                var plan = MaterialPositioning.Calculate(row, map, pms);
                var loc = new StationLocation { Plan = plan };
                var cluster = fullClusters.FirstOrDefault(c =>
                {
                    var (rf, name) = SplitClusterName(c.Name);
                    return rf == row.Cluster && string.Equals(name, plan.Location, StringComparison.OrdinalIgnoreCase);
                });
                if (cluster != null)
                {
                    FillWired(loc, cluster);
                    if (loc.WiredPoints.Count > 0)
                    {
                        if (loc.ActualVoltage != plan.Voltage) loc.Gaps.Add($"Voltage points: planned {plan.Voltage}, wired {loc.ActualVoltage}");
                        if (loc.ActualCurrent != plan.Current) loc.Gaps.Add($"Current points: planned {plan.Current}, wired {loc.ActualCurrent}");
                        if (loc.ActualVoltageCards > plan.VoltageCards) loc.Gaps.Add($"Voltage cards: planned {plan.VoltageCards}, used {loc.ActualVoltageCards}");
                        if (loc.ActualCurrentCards > plan.CurrentCards) loc.Gaps.Add($"Current cards: planned {plan.CurrentCards}, used {loc.ActualCurrentCards}");
                    }
                }
                else
                {
                    plan.Warnings.Add($"No Yard Config location \"cluster_{row.Cluster}/{plan.Location}\" — wired figures not compared.");
                }
                result.Locations.Add(loc);
            }

            // Yard Config clusters with no survey gear row (relay room, IPS …) — shown with their wired points only
            foreach (var c in fullClusters.Where(c => !result.Locations.Any(l => l.ClusterId == c.Id)).OrderBy(c => SplitClusterName(c.Name).rf ?? int.MaxValue))
            {
                var (rf, name) = SplitClusterName(c.Name);
                var loc = new StationLocation
                {
                    HasSurvey = false,
                    Plan = new MaterialPositioning.LocationResult { RfCluster = rf ?? 0, Location = name }
                };
                FillWired(loc, c);
                if (loc.WiredPoints.Count == 0) continue;
                loc.Plan.Warnings.Add("No survey gear row for this location — only the wired lines are shown.");
                result.Locations.Add(loc);
            }

            result.Bom = MaterialPositioning.Bom(result.Locations, sparePercent);
            result.Assets = MaterialPositioning.Assets(result.Locations);
            return result;

            // Wired lines of one Yard Config cluster → WiredPoints, actual counts and cards in use
            void FillWired(StationLocation loc, Cluster cluster)
            {
                loc.ClusterId = cluster.Id;
                loc.ClusterName = cluster.Name ?? "";
                loc.LocationType = cluster.TypeId == 1 ? "Indoor" : cluster.TypeId == 2 ? "Outdoor" : "";
                loc.Power = cluster.PowerSupplyId switch { 1 => "24 V", 2 => "110 V", 3 => "110→24 V", _ => "" };
                var wired = lines.Where(l => l.ClusterId == cluster.Id && l.AssetId > 0 && l.AttributeId > 0)
                                 .OrderBy(l => CardSortKey(l.CardName)).ThenBy(l => l.Number).ToList();
                foreach (var l in wired)
                {
                    mapByAttr.TryGetValue(l.AttributeId!.Value, out var m);
                    string kind;
                    if (m != null) kind = string.Equals(m.Kind?.Trim(), MaterialPositioning.KindCurrent, StringComparison.OrdinalIgnoreCase) ? MaterialPositioning.KindCurrent : MaterialPositioning.KindVoltage;
                    else if (l.IsDigital == true || (l.CardName ?? "").StartsWith("DI-", StringComparison.OrdinalIgnoreCase)) kind = "DI";
                    else if (l.ValueType == (int)Utility.Utility.ValueType.Current) kind = MaterialPositioning.KindCurrent;
                    else if (l.ValueType == (int)Utility.Utility.ValueType.Voltage) kind = MaterialPositioning.KindVoltage;
                    else kind = IsCurrentAttribute(l) ? MaterialPositioning.KindCurrent : MaterialPositioning.KindVoltage;

                    loc.WiredPoints.Add(new WiredPoint
                    {
                        Asset = (l.AssetName ?? "").Trim(), AssetType = (l.AssetTypeName ?? m?.AssetTypeName ?? "").Trim(),
                        Title = (l.AttributeName ?? "").Trim(), Kind = kind,
                        Type = m?.Type?.Trim() ?? "", Card = l.CardName ?? "", Line = l.Number, InMapping = m != null
                    });
                    if (m == null) continue;   // planned-vs-wired compares only attributes the survey can plan
                    var key = MaterialPositioning.SensorKey(kind, m.Type);
                    loc.ActualSensors[key] = loc.ActualSensors.GetValueOrDefault(key) + 1;
                    if (kind == MaterialPositioning.KindVoltage) loc.ActualVoltage++; else loc.ActualCurrent++;
                }
                var cardNames = wired.Select(l => l.CardName ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                loc.ActualVoltageCards = cardNames.Count(n => n.StartsWith("MintCard-", StringComparison.OrdinalIgnoreCase));
                loc.ActualCurrentCards = cardNames.Count(n => n.StartsWith("C-", StringComparison.OrdinalIgnoreCase));
                // e7mriv2web "Type Card" / "FTU Type": cards in use per type, × 10 channels
                var byType = cardNames.GroupBy(n => n.Split('-')[0]).OrderBy(g => g.Key).ToList();
                loc.WiredTypeCard = string.Join(", ", byType.Select(g => $"{g.Key}({g.Count()})"));
                loc.WiredFtuChannels = cardNames.Count * MaterialPositioning.PinsPerCard;
            }
        }

        // ── AJAX: Material tab ──
        public async Task<JsonResult> GetMaterialPositioning(int siteId, decimal spare = 0)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (siteId <= 0) return Json(new { error = "Select a station first." });
                if (spare < 0 || spare > 100) return Json(new { error = "Spare must be 0–100 %." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                return Json(await BuildMaterialPositioning(hcf, siteId, spare, loginUser));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // ── Printable Material Positioning report (browser Print → PDF) ──
        public async Task<IActionResult> MaterialReport(int siteId, decimal spare = 0)
        {
            var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
            if (loginUser == null) return RedirectToAction("Index", "Login");
            if (siteId <= 0) return BadRequest("Select a station first.");
            if (spare < 0 || spare > 100) return BadRequest("Spare must be 0–100 %.");
            using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
            return View(await BuildMaterialPositioning(hcf, siteId, spare, loginUser));
        }

        // ── Styled Excel workbook (.xlsx) ──
        public async Task<IActionResult> MaterialExcel(int siteId, decimal spare = 0)
        {
            var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
            if (loginUser == null) return RedirectToAction("Index", "Login");
            if (siteId <= 0) return BadRequest("Select a station first.");
            if (spare < 0 || spare > 100) return BadRequest("Spare must be 0–100 %.");
            using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
            var model = await BuildMaterialPositioning(hcf, siteId, spare, loginUser);
            var name = string.Concat((string.IsNullOrWhiteSpace(model.StationCode) ? model.StationName : model.StationCode)
                                     .Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            return File(MaterialPositioningExcel.Build(model),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"MaterialPositioning_{name}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        /// <summary>
        /// Card pin rules, checked when a line is wired:
        ///   C-n        : all 10 lines current only.
        ///   MintCard-n : lines 1-8 voltage only, lines 9-10 current only;
        ///                lines 9-10 stay blank while the card carries an axle-counter point.
        /// Attributes not in AttributeSensorMapping (e.g. datalogger) and other card types are not checked.
        /// </summary>
        private static string? PinRuleError(string? cardName, List<(int number, int attributeId)> wiredOnCard, Dictionary<int, AttributeSensorMapping> mapByAttr)
        {
            cardName ??= "";
            bool isCurrentCard = cardName.StartsWith("C-", StringComparison.OrdinalIgnoreCase);
            bool isVoltageCard = cardName.StartsWith("MintCard-", StringComparison.OrdinalIgnoreCase);
            if (!isCurrentCard && !isVoltageCard) return null;

            bool Is(AttributeSensorMapping m, string kind) => string.Equals(m.Kind?.Trim(), kind, StringComparison.OrdinalIgnoreCase);
            var known = wiredOnCard.Where(w => mapByAttr.ContainsKey(w.attributeId))
                                   .Select(w => (w.number, m: mapByAttr[w.attributeId])).ToList();
            bool hasAxle = known.Any(k => k.m.AssetTypeId == (int)Utility.Utility.AssetType.AXLE_COUNTER);
            foreach (var (number, m) in known)
            {
                var what = $"Line {number} ({m.Title?.Trim()}, {m.Kind?.Trim()})";
                if (isCurrentCard && Is(m, MaterialPositioning.KindVoltage))
                    return $"{what}: a current card takes current inputs only. Use a MintCard line 1–8.";
                if (isVoltageCard && number <= MaterialPositioning.VoltagePinsPerCard && Is(m, MaterialPositioning.KindCurrent))
                    return $"{what}: MintCard lines 1–8 are for voltage only. Use line 9–10 or a current card.";
                if (isVoltageCard && number > MaterialPositioning.VoltagePinsPerCard && Is(m, MaterialPositioning.KindVoltage))
                    return $"{what}: MintCard lines 9–10 are for current only.";
                if (isVoltageCard && number > MaterialPositioning.VoltagePinsPerCard && hasAxle)
                    return $"{what}: this MintCard carries axle-counter points, so lines 9–10 stay blank.";
            }
            return null;
        }

        // ── Uploaded files live on the API server; build their public root from APIBaseUrl ──
        private string FileRoot()
        {
            var b = _config["APIBaseUrl"] ?? "";
            return b.Replace("api/", "").TrimEnd('/');   // "http://energy7.in:90/api/" -> "http://energy7.in:90"
        }

        // ══ Survey NMDL upload (proxy to api/SiteSurveyNMDL) ══════════════════════════════
        public class UploadNmdlDto
        {
            public int SiteId { get; set; }
            public string Category { get; set; }
            public string OriginalFileName { get; set; }
            public string Remarks { get; set; }
            public string FileBase64 { get; set; }
            public string FileExtension { get; set; }
        }

        [HttpGet]
        public async Task<JsonResult> GetNMDL(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (siteId <= 0) return Json(new { error = "Select a station first." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var resp = await hcf.Client.GetAsync($"SiteSurveyNMDL/SiteId/{siteId}");
                if (resp.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load NMDL files." });
                var list = JsonConvert.DeserializeObject<List<SiteSurveyNMDL>>(await resp.Content.ReadAsStringAsync()) ?? new();
                var root = FileRoot();
                return Json(list.Select(x => new
                {
                    id = x.Id,
                    name = string.IsNullOrWhiteSpace(x.OriginalFileName) ? x.FileName : x.OriginalFileName,
                    remarks = x.Remarks,
                    category = x.Category,
                    createdDate = x.CreatedDate,
                    url = string.IsNullOrEmpty(x.FileUrl) ? null : root + x.FileUrl
                }));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        [HttpPost]
        public async Task<JsonResult> UploadNMDL([FromBody] UploadNmdlDto m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0) return Json(new { error = "Select a station first." });
                if (string.IsNullOrEmpty(m.FileBase64)) return Json(new { error = "Choose a file to upload." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var row = new SiteSurveyNMDL
                {
                    SiteId = m.SiteId,
                    Category = string.IsNullOrWhiteSpace(m.Category) ? "NMDL" : m.Category.Trim(),
                    OriginalFileName = m.OriginalFileName,
                    Remarks = string.IsNullOrWhiteSpace(m.Remarks) ? null : m.Remarks.Trim(),
                    FileBase64 = m.FileBase64,
                    FileExtension = m.FileExtension,
                    CreatedBy = loginUser.Id
                };
                using var str = new StringContent(JsonConvert.SerializeObject(row), Encoding.UTF8, "application/json");
                var resp = await hcf.Client.PostAsync("SiteSurveyNMDL", str);
                if (resp.StatusCode != HttpStatusCode.OK) return Json(new { error = await ApiErrorMessage(resp) });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        [HttpPost]
        public async Task<JsonResult> DeleteNMDL(int id)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (id <= 0) return Json(new { error = "Invalid file." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var resp = await hcf.Client.DeleteAsync($"SiteSurveyNMDL/{id}/UserId/{loginUser.Id}");
                if (resp.StatusCode != HttpStatusCode.OK) return Json(new { error = await ApiErrorMessage(resp) });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // ══ Location-wise photos (proxy to api/LocationPhoto) ═════════════════════════════
        public class UploadPhotoDto
        {
            public int ClusterId { get; set; }
            public int SiteId { get; set; }
            public string Caption { get; set; }
            public string OriginalFileName { get; set; }
            public string FileBase64 { get; set; }
            public string FileExtension { get; set; }
        }

        [HttpGet]
        public async Task<JsonResult> GetLocationPhotos(int clusterId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (clusterId <= 0) return Json(new { error = "Select a location first." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var resp = await hcf.Client.GetAsync($"LocationPhoto/ClusterId/{clusterId}");
                if (resp.StatusCode != HttpStatusCode.OK) return Json(new { error = "Could not load photos." });
                var list = JsonConvert.DeserializeObject<List<LocationPhoto>>(await resp.Content.ReadAsStringAsync()) ?? new();
                var root = FileRoot();
                return Json(list.Select(x => new
                {
                    id = x.Id,
                    url = string.IsNullOrEmpty(x.ImageUrl) ? null : root + x.ImageUrl,
                    caption = x.Caption,
                    name = x.OriginalFileName,
                    createdDate = x.CreatedDate
                }));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        [HttpPost]
        public async Task<JsonResult> UploadLocationPhoto([FromBody] UploadPhotoDto m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.ClusterId <= 0 || m.SiteId <= 0) return Json(new { error = "Select a location first." });
                if (string.IsNullOrEmpty(m.FileBase64)) return Json(new { error = "Choose a photo to upload." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var row = new LocationPhoto
                {
                    ClusterId = m.ClusterId,
                    SiteId = m.SiteId,
                    Caption = string.IsNullOrWhiteSpace(m.Caption) ? null : m.Caption.Trim(),
                    OriginalFileName = m.OriginalFileName,
                    FileBase64 = m.FileBase64,
                    FileExtension = m.FileExtension,
                    CreatedBy = loginUser.Id
                };
                using var str = new StringContent(JsonConvert.SerializeObject(row), Encoding.UTF8, "application/json");
                var resp = await hcf.Client.PostAsync("LocationPhoto", str);
                if (resp.StatusCode != HttpStatusCode.OK) return Json(new { error = await ApiErrorMessage(resp) });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        [HttpPost]
        public async Task<JsonResult> DeleteLocationPhoto(int id)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (id <= 0) return Json(new { error = "Invalid photo." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var resp = await hcf.Client.DeleteAsync($"LocationPhoto/{id}/UserId/{loginUser.Id}");
                if (resp.StatusCode != HttpStatusCode.OK) return Json(new { error = await ApiErrorMessage(resp) });
                return Json(new { success = true });
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

                    // A completed (non-project) station — IsProject false or null — is read-only.
                    if (existing.IsProject != true)
                        return Json(new { error = "This station is marked Completed and is read-only. Updates are not allowed." });

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

        // ── Project details (SiteProjectInfo table). Degrades to { ready = false } until the API side
        //    (table + api/SiteProjectInfo endpoints) is deployed, so the New Station form never breaks. ──
        public async Task<JsonResult> GetProjectInfo(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (siteId <= 0) return Json(new { error = "Select a station." });
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var resp = await hcf.Client.GetAsync($"SiteProjectInfo/SiteId/{siteId}");
                if (resp.StatusCode != HttpStatusCode.OK) return Json(new { ready = false });   // API not deployed yet
                var body = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body) || body == "null") return Json(new { });
                var info = JsonConvert.DeserializeObject<SiteProjectInfo>(body);
                return Json(info);
            }
            catch { return Json(new { ready = false }); }
        }

        [HttpPost]
        public async Task<JsonResult> SaveProjectInfo([FromBody] SiteProjectInfo m)
        {
            if (IsSiteLocked(m?.SiteId ?? 0)) return SiteLockedJson();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0) return Json(new { error = "Select a station." });
                m.CreatedBy = loginUser.Id;
                if (m.PcclStarted == true) { m.PcclStartedBy = loginUser.Id; m.PcclStartedDate = DateTime.Now; }
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                using var str = new StringContent(JsonConvert.SerializeObject(m), Encoding.UTF8, "application/json");
                var resp = await hcf.Client.PostAsync("SiteProjectInfo", str);
                if (resp.StatusCode == HttpStatusCode.NotFound) return Json(new { ready = false });   // API not built yet
                if (resp.StatusCode != HttpStatusCode.OK && resp.StatusCode != HttpStatusCode.NoContent)
                    return Json(new { error = await ApiErrorMessage(resp) });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
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
