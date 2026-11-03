using System.Collections.Generic;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// SURVEY stage tasks. Progress is stored in the existing AppConfig table (no schema change):
    ///   GroupName = SURVEY_CL_{siteId}, ZoneId = site's zone
    ///   AppKey    = ITEM_01..ITEM_16  → AppValue "1|userId|yyyy-MM-dd" (done) or "0|userId|yyyy-MM-dd"
    ///   AppKey    = STAGE             → AppValue "Completed|userId|yyyy-MM-dd" or "InProgress|userId|yyyy-MM-dd"
    /// AppConfig.AppValue is nvarchar(50), so no remarks are stored.
    /// </summary>
    public static class SurveyChecklist
    {
        public const string StageKey = "STAGE";

        public static string GroupName(int siteId) => $"SURVEY_CL_{siteId}";

        public static string ItemKey(int itemNo) => $"ITEM_{itemNo:00}";

        public static readonly IReadOnlyList<string> Items = new[]
        {
            "NMDL & SIP & SMMS - create asset attribute vs location list, get it filled by SSE",
            "Confirm power supply (24V / 110V)",
            "Point machine details - IRS/TWS, series/parallel, confirm A-end & B-end, full/half P/M",
            "Location drawing",
            "IPS & ELD gear mapping",
            "SPN attribute vs railway attribute (IPS input, equipment room)",
            "Load calculation LOC wise",
            "Space for FTU (master LOC) & sensor in all LOCs",
            "Check network strength",
            "Site register",
            "Mention distance (LOC to LOC)",
            "Photo of each location",
            "Sign of site survey on register by supervisor and railway staff",
            "Finalised trenching & cable laying plan with cable core",
            "Survey drawing submission",
            "Row terminal details collected during the survey"
        };
    }
}
