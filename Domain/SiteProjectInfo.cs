using System;

namespace Domain
{
    // Project-creation metadata for a station. Stored in the SiteProjectInfo table (1 row per site).
    // The API side (entity + repository + controller) is added after the table exists; this DTO is the
    // shape the web app sends/receives over api/SiteProjectInfo.
    public class SiteProjectInfo
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string ProjectName { get; set; }
        public DateTime? StartDate { get; set; }
        public int? FrsYear { get; set; }
        public string Stage { get; set; }                 // Survey | Execution | Commissioning …
        public DateTime? TargetCommissioning { get; set; }
        public DateTime? TDC { get; set; }                // Target Date of Completion
        public string SiteRegisterRef { get; set; }
        public bool? PcclStarted { get; set; }
        public int? PcclStartedBy { get; set; }
        public DateTime? PcclStartedDate { get; set; }
        public int CreatedBy { get; set; }
    }
}
