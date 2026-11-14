using System;
using System.Collections.Generic;
using System.Linq;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// Attribute-wise check / uncheck list for the TERMINATION, VALIDATION and CALIBRATION stages.
    /// One checklist row per wired yard card line (Cluster → Location → Asset Type → Asset → Attribute → ADC).
    /// Progress is stored in the existing AppConfig table (no schema change), exactly like SurveyChecklist:
    ///   GroupName = TVC_{STAGECODE}_{siteId}   (e.g. TVC_TERMINATION_123), ZoneId = site's zone
    ///   AppKey    = L_{cardLineId}             → AppValue "1|userId|yyyy-MM-dd" (done) / "0|userId|yyyy-MM-dd"
    ///   AppKey    = STAGE                      → AppValue "Completed|.." / "Partial|.." / "NotComplete|.."
    /// AppConfig.AppValue is nvarchar(50), so no remarks are stored on a row.
    /// </summary>
    public static class StageChecklist
    {
        public const string StageKey = "STAGE";

        // Stage codes that use the attribute-wise checklist (match ProjectStage.Code).
        public static readonly IReadOnlyList<string> Stages = new[] { "TERMINATION", "VALIDATION", "CALIBRATION" };

        public static bool AppliesTo(string stageCode) =>
            !string.IsNullOrWhiteSpace(stageCode) &&
            Stages.Contains(stageCode.Trim().ToUpperInvariant());

        public static string Normalize(string stageCode) => (stageCode ?? "").Trim().ToUpperInvariant();

        public static string GroupName(string stageCode, int siteId) => $"TVC_{Normalize(stageCode)}_{siteId}";

        public static string LineKey(int cardLineId) => $"L_{cardLineId}";

        public static bool IsDone(string appValue) => (appValue ?? "").StartsWith("1|");

        // Status derived from done vs total rows: all done → Completed, some → Partial, none → NotComplete.
        public static string DeriveStatus(int doneCount, int total) =>
            total > 0 && doneCount >= total ? "Completed" : doneCount > 0 ? "Partial" : "NotComplete";

        // Maps the stored status word to the ProgressStatus id the web dialog uses (1/2/3).
        public static int StatusId(string status) => status switch
        {
            "Completed" => 3,
            "Partial" => 2,
            _ => 1   // NotComplete (default)
        };
    }
}
