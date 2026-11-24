using System;
using System.Collections.Generic;

namespace Domain
{
    // One row per Site x ProjectStage (dbo.SiteStageProgress): the stage's status at a station,
    // its planned/done roll-up, who finished it, and the QA/Quality-Check verdict.
    public class SiteStageProgress
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int ProjectStageId { get; set; }
        public int? ClusterId { get; set; }            // the location (dbo.Cluster) this progress is for

        public int ProgressStatusId { get; set; }     // 0..3, see ProgressStatus
        public int? PlannedQty { get; set; }
        public int? DoneQty { get; set; }
        public decimal? DonePercent { get; set; }

        public string Remarks { get; set; }
        public int? DoneBy { get; set; }
        public DateTime? DoneDate { get; set; }

        // Quality Check (QA team) — one verdict per stage
        public byte? QcStatus { get; set; }            // null pending, 1 OK, 2 Not OK
        public string QcRemarks { get; set; }
        public int? QcBy { get; set; }
        public DateTime? QcDate { get; set; }

        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int? LastModifiedBy { get; set; }
        public DateTime? LastModifiedDate { get; set; }

        // Extra (display only, filled by the API join — not columns)
        public string StageCode { get; set; }
        public string StageName { get; set; }
        public string StageColor { get; set; }
        public int StageSequenceNo { get; set; }
        public string StatusName { get; set; }
        public string DoneByName { get; set; }
        public string QcByName { get; set; }
        public string LocationName { get; set; }       // Cluster display name (e.g. "cluster_3/Relay Room")

        public List<SiteStageProgressItem> Items { get; set; } = new List<SiteStageProgressItem>();
    }

    // One location (dbo.Cluster) of a station, for the Project Progress location picker.
    public class ProgressLocation
    {
        public int ClusterId { get; set; }
        public string Name { get; set; }
        public int? RfCluster { get; set; }
        public string Location { get; set; }
    }

    // Combined payload the web app loads for one station (API: SiteStageProgress/SiteId/{siteId}).
    // Stages is a flat list of (stage x location) rows; filter by ClusterId on the client.
    public class SiteProgress
    {
        public int SiteId { get; set; }
        public List<ProgressLocation> Locations { get; set; } = new List<ProgressLocation>();
        public List<SiteStageProgress> Stages { get; set; } = new List<SiteStageProgress>();
    }
}
