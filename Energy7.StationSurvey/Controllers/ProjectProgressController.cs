using Domain;
using Energy7.StationSurvey.Interface;
using Energy7.StationSurvey.Utility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Energy7.StationSurvey.Controllers
{
    [Authenticate]
    public class ProjectProgressController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly ISiteService _siteService;

        public ProjectProgressController(
            IWebHostEnvironment env, IConfiguration config, IHttpContextAccessor httpContextAccessor,
            IZoneService zoneService, IDivisionService divisionService, ISiteService siteService)
        {
            _env = env;
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _siteService = siteService;
        }

        // GET: /ProjectProgress  — hosts the page in an iframe
        public IActionResult Index() => View();

        // GET: /ProjectProgress/Page  — the static page shown inside the iframe
        public IActionResult Page()
        {
            var path = Path.Combine(_env.ContentRootPath, "App_Data", "rdpms_project_progress.html");
            return PhysicalFile(path, "text/html; charset=utf-8");
        }

        // ── Pickers + lookups ────────────────────────────────────────────────
        // Initial load: the logged-in user (recorded automatically on save), zones, status + QC lookups.
        public JsonResult GetBootstrap()
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });

                var zones = (_zoneService.GetAllZones() ?? new List<Zone>())
                            .Select(z => new { id = z.Id, name = z.Name }).OrderBy(z => z.name).ToList();

                var meName = string.IsNullOrWhiteSpace(loginUser.Name) ? $"{loginUser.FirstName} {loginUser.LastName}".Trim() : loginUser.Name;
                if (string.IsNullOrWhiteSpace(meName)) meName = "Me";

                return Json(new
                {
                    me = new { id = loginUser.Id, name = meName },
                    zones,
                    statuses = new[]
                    {
                        new { id = 1, name = "Not Complete",     color = "#DC3545" },
                        new { id = 2, name = "Partial Complete", color = "#FFC107" },
                        new { id = 3, name = "Completed",         color = "#198754" }
                    },
                    qc = new[]
                    {
                        new { id = 1, name = "OK" },
                        new { id = 2, name = "Not OK" }
                    }
                });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public JsonResult GetDivisions(int zoneId)
        {
            try
            {
                var divs = (zoneId > 0 ? _divisionService.GetByZoneId(zoneId) : _divisionService.GetAllDivisions()) ?? new List<Division>();
                return Json(divs.Select(d => new { id = d.Id, name = d.Name }).OrderBy(d => d.name));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public JsonResult GetStations(int divisionId)
        {
            try
            {
                var sites = (divisionId > 0 ? _siteService.GetBy(divisionId) : _siteService.GetAll()) ?? new List<Site>();
                return Json(sites.Select(s => new { id = s.Id, name = s.Name, code = s.StationCode }).OrderBy(s => s.name));
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // ── Stage master list ────────────────────────────────────────────────
        // Returns the active stages. Degrades to { ready = false } + a built-in list so the page still
        // renders (read-only) before the API/DB side is deployed.
        public async Task<JsonResult> GetStages()
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var resp = await hcf.Client.GetAsync("ProjectStage/GetAll");
                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    var stages = JsonConvert.DeserializeObject<List<ProjectStage>>(body) ?? new();
                    stages = stages.Where(s => s.IsActive).OrderBy(s => s.SequenceNo).ToList();
                    if (stages.Count > 0) return Json(new { ready = true, stages });
                }
            }
            catch { /* fall through to the built-in list */ }

            return Json(new { ready = false, stages = DefaultStages() });
        }

        // ── Per-station progress (header + items for every stage) ─────────────
        public async Task<JsonResult> GetProgress(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (siteId <= 0) return Json(new { error = "Select a station." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var resp = await hcf.Client.GetAsync($"SiteStageProgress/SiteId/{siteId}");
                if (resp.StatusCode != HttpStatusCode.OK) return Json(new { ready = false });   // API not deployed yet

                var body = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body) || body == "null")
                    return Json(new { ready = true, siteId, locations = new List<ProgressLocation>(), stages = new List<SiteStageProgress>() });

                var prog = JsonConvert.DeserializeObject<SiteProgress>(body);
                return Json(new
                {
                    ready = true,
                    siteId,
                    locations = prog?.Locations ?? new List<ProgressLocation>(),
                    stages = prog?.Stages ?? new List<SiteStageProgress>()
                });
            }
            catch { return Json(new { ready = false }); }
        }

        // ── Save a stage header (status / remarks / Done By / QC) ─────────────
        [HttpPost]
        public async Task<JsonResult> SaveStage([FromBody] SiteStageProgress m)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0 || m.ProjectStageId <= 0) return Json(new { error = "Select a station and stage." });

                // stamp the actor (client may override Done By with a picked user)
                if (m.Id == 0) m.CreatedBy = loginUser.Id;
                m.LastModifiedBy = loginUser.Id;
                if ((m.DoneBy ?? 0) == 0 && m.ProgressStatusId >= 2) { m.DoneBy = loginUser.Id; m.DoneDate ??= DateTime.Now; }
                if (m.QcStatus != null && (m.QcBy ?? 0) == 0) { m.QcBy = loginUser.Id; m.QcDate ??= DateTime.Now; }

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                using var str = new StringContent(JsonConvert.SerializeObject(m), Encoding.UTF8, "application/json");
                var resp = await hcf.Client.PostAsync("SiteStageProgress", str);
                if (resp.StatusCode == HttpStatusCode.NotFound) return Json(new { ready = false });  // API not built yet
                if (resp.StatusCode != HttpStatusCode.OK && resp.StatusCode != HttpStatusCode.NoContent)
                    return Json(new { error = await ApiErrorMessage(resp) });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // ── Save one detail line (planned/done or check-uncheck) ──────────────
        [HttpPost]
        public async Task<JsonResult> SaveItem([FromBody] SiteStageProgressItem m)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0 || m.ProjectStageId <= 0 || string.IsNullOrWhiteSpace(m.ItemCode))
                    return Json(new { error = "Missing item details." });

                if (m.Id == 0) m.CreatedBy = loginUser.Id;
                m.LastModifiedBy = loginUser.Id;
                if ((m.DoneBy ?? 0) == 0 && ((m.DoneQty ?? 0) > 0 || m.IsDone == true)) { m.DoneBy = loginUser.Id; m.DoneDate ??= DateTime.Now; }

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                using var str = new StringContent(JsonConvert.SerializeObject(m), Encoding.UTF8, "application/json");
                var resp = await hcf.Client.PostAsync("SiteStageProgress/Item", str);
                if (resp.StatusCode == HttpStatusCode.NotFound) return Json(new { ready = false });
                if (resp.StatusCode != HttpStatusCode.OK && resp.StatusCode != HttpStatusCode.NoContent)
                    return Json(new { error = await ApiErrorMessage(resp) });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        // ── Attribute-wise checklist (TERMINATION / VALIDATION / CALIBRATION) ──
        // One row per wired yard card line: Cluster → Location → Asset Type → Asset → Attribute → ADC,
        // each with a check / uncheck. Check state lives in AppConfig (no DB schema change), same as the
        // Survey checklist. The rows (the yard) are identical for all three stages; only the ticks differ.
        public async Task<JsonResult> GetYardChecklist(int siteId)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (siteId <= 0) return Json(new { error = "Select a station." });

                var site = _siteService.Get(siteId);
                if (site == null || site.Id != siteId) return Json(new { error = "Station not found." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);

                // Cluster id → name (for Cluster / Location columns)
                var clusterNames = new Dictionary<int, string>();
                var clResp = await hcf.Client.GetAsync($"Cluster/SiteId/{siteId}");
                if (clResp.StatusCode == HttpStatusCode.OK)
                {
                    var clusters = JsonConvert.DeserializeObject<List<Cluster>>(await clResp.Content.ReadAsStringAsync()) ?? new();
                    foreach (var c in clusters) clusterNames[c.Id] = c.Name;
                }

                // Wired card lines = the attribute rows of the yard
                var lines = new List<CardLine>();
                var lnResp = await hcf.Client.GetAsync($"CardLine/SiteId/{siteId}");
                if (lnResp.StatusCode == HttpStatusCode.OK)
                    lines = JsonConvert.DeserializeObject<List<CardLine>>(await lnResp.Content.ReadAsStringAsync()) ?? new();

                var rows = lines
                    .Where(l => (l.AssetId ?? 0) > 0 && (l.AttributeId ?? 0) > 0)
                    .Select(l =>
                    {
                        var (rf, loc) = SplitCluster(l.ClusterId.HasValue && clusterNames.TryGetValue(l.ClusterId.Value, out var cn) ? cn : l.ClusterName);
                        return new
                        {
                            lineId = l.Id,
                            clusterId = l.ClusterId ?? 0,
                            rf,
                            cluster = rf.HasValue ? $"Cluster {rf}" : "",
                            location = loc,
                            assetType = (l.AssetTypeName ?? "").Trim(),
                            asset = (l.AssetName ?? "").Trim(),
                            attribute = (l.AttributeName ?? "").Trim(),
                            adc = (l.ADCId ?? 0) > 0 && l.SequenceNumber.HasValue ? $"ADC{l.SequenceNumber}" : "",
                            card = l.CardName ?? "",
                            line = l.Number
                        };
                    })
                    .OrderBy(r => r.rf ?? int.MaxValue).ThenBy(r => r.location).ThenBy(r => r.card).ThenBy(r => r.line)
                    .ToList();

                var total = rows.Count;

                // Per-stage tick maps + derived status
                var stages = new Dictionary<string, object>();
                foreach (var code in StageChecklist.Stages)
                {
                    var cfg = await GetChecklistRows(hcf, StageChecklist.GroupName(code, siteId), site.ZoneId);
                    var checks = new Dictionary<int, bool>();
                    int doneCount = 0;
                    foreach (var row in rows)
                    {
                        var done = StageChecklist.IsDone(cfg.FirstOrDefault(r => r.AppKey == StageChecklist.LineKey(row.lineId))?.AppValue);
                        checks[row.lineId] = done;
                        if (done) doneCount++;
                    }
                    var status = StageChecklist.DeriveStatus(doneCount, total);
                    stages[code] = new { checks, doneCount, total, status, statusId = StageChecklist.StatusId(status) };
                }

                return Json(new { ready = true, siteId, total, rows, stages });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        public class YardCheckUpdate
        {
            public int SiteId { get; set; }
            public string StageCode { get; set; }
            public int LineId { get; set; }
            public bool Done { get; set; }
            public int Total { get; set; }   // total wired rows (denominator), sent by the page
        }

        [HttpPost]
        public async Task<JsonResult> SaveYardChecklistItem([FromBody] YardCheckUpdate m)
        {
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);
                if (loginUser == null) return Json(new { error = "Session expired." });
                if (m == null || m.SiteId <= 0 || m.LineId <= 0 || !StageChecklist.AppliesTo(m.StageCode))
                    return Json(new { error = "Invalid checklist item." });

                var site = _siteService.Get(m.SiteId);
                if (site == null || site.Id != m.SiteId) return Json(new { error = "Station not found." });

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser.Token);
                var group = StageChecklist.GroupName(m.StageCode, m.SiteId);
                var cfg = await GetChecklistRows(hcf, group, site.ZoneId);

                var stamp = $"{loginUser.Id}|{DateTime.Now:yyyy-MM-dd}";
                var lineKey = StageChecklist.LineKey(m.LineId);
                var changes = new List<AppConfig>
                {
                    ConfigRow(cfg, site.ZoneId, group, lineKey, $"{(m.Done ? 1 : 0)}|{stamp}")
                };

                // Recompute done count from the stored ticks, applying this change.
                // Count OTHER checked rows, then add this one if it's now done (handles a first-time tick
                // that has no stored row yet).
                var doneCount = cfg.Count(r => r.AppKey != null && r.AppKey.StartsWith("L_") && r.AppKey != lineKey && StageChecklist.IsDone(r.AppValue))
                                + (m.Done ? 1 : 0);
                var total = m.Total > 0 ? m.Total : doneCount;
                var status = StageChecklist.DeriveStatus(doneCount, total);
                changes.Add(ConfigRow(cfg, site.ZoneId, group, StageChecklist.StageKey, $"{status}|{stamp}"));

                using var str = new StringContent(JsonConvert.SerializeObject(changes), Encoding.UTF8, "application/json");
                var resp = await hcf.Client.PostAsync("AppConfig", str);
                if (!resp.IsSuccessStatusCode) return Json(new { error = "Could not save — AppConfig returned " + (int)resp.StatusCode + "." });

                return Json(new { success = true, stageCode = StageChecklist.Normalize(m.StageCode), doneCount, total, status, statusId = StageChecklist.StatusId(status) });
            }
            catch (Exception ex) { return Json(new { error = ex.Message }); }
        }

        private static async Task<List<AppConfig>> GetChecklistRows(HttpClientHelper hcf, string group, int? zoneId)
        {
            var resp = await hcf.Client.GetAsync($"AppConfig/GroupName/{group}/ZoneId/{zoneId ?? 0}");
            if (resp.StatusCode != HttpStatusCode.OK) return new List<AppConfig>();
            return JsonConvert.DeserializeObject<List<AppConfig>>(await resp.Content.ReadAsStringAsync()) ?? new List<AppConfig>();
        }

        private static AppConfig ConfigRow(List<AppConfig> rows, int? zoneId, string group, string key, string value)
        {
            var existing = rows.FirstOrDefault(r => r.AppKey == key);
            return new AppConfig { Id = existing?.Id ?? 0, ZoneId = zoneId, GroupName = group, AppKey = key, AppValue = value };
        }

        // "cluster_3/Relay Room" → (3, "Relay Room");  "Relay Room" → (null, "Relay Room")
        private static (int? rf, string location) SplitCluster(string name)
        {
            name = (name ?? "").Trim();
            var slash = name.IndexOf('/');
            if (slash < 0) return (null, name);
            var prefix = name.Substring(0, slash).Trim();
            var loc = name.Substring(slash + 1).Trim();
            int? rf = prefix.StartsWith("cluster_", StringComparison.OrdinalIgnoreCase) && int.TryParse(prefix.Substring(8), out var n) ? n : null;
            return (rf, loc);
        }

        // ── helpers ───────────────────────────────────────────────────────────
        private static List<ProjectStage> DefaultStages() => new()
        {
            new ProjectStage { Id = 1, Code = "SURVEY",      Name = "Survey",      SequenceNo = 1, ColorCode = "#6C757D", IsActive = true },
            new ProjectStage { Id = 2, Code = "FITTING",     Name = "Fitting",     SequenceNo = 2, ColorCode = "#0D6EFD", IsActive = true },
            new ProjectStage { Id = 3, Code = "NETWORKING",  Name = "Networking",  SequenceNo = 3, ColorCode = "#6610F2", IsActive = true },
            new ProjectStage { Id = 4, Code = "WIRING",      Name = "Wiring",      SequenceNo = 4, ColorCode = "#FD7E14", IsActive = true },
            new ProjectStage { Id = 5, Code = "TERMINATION", Name = "Termination", SequenceNo = 5, ColorCode = "#D63384", IsActive = true },
            new ProjectStage { Id = 6, Code = "VALIDATION",  Name = "Validation",  SequenceNo = 6, ColorCode = "#20C997", IsActive = true },
            new ProjectStage { Id = 7, Code = "CALIBRATION", Name = "Calibration", SequenceNo = 7, ColorCode = "#FFC107", IsActive = true },
            new ProjectStage { Id = 9, Code = "ALERTTEST",   Name = "Alert Test",  SequenceNo = 8, ColorCode = "#0DCAF0", IsActive = true },
            new ProjectStage { Id = 8, Code = "DOCUMENT",    Name = "Document",    SequenceNo = 9, ColorCode = "#198754", IsActive = true }
        };

        private static async Task<string> ApiErrorMessage(System.Net.Http.HttpResponseMessage response)
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
    }
}
