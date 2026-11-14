using System;

namespace Domain
{
    // One measurable line under a SiteStageProgress header (dbo.SiteStageProgressItem).
    // RefType decides what the line represents:
    //   COMPONENT  (RefId null)       FTU, SENSOR, AI, CI, ISOLATION, POWERCARD, MODEM, ANTENNA, ADC, VWIRE, CWIRE
    //   ASSETTYPE  (RefId = AssetType.Id)       e.g. Alert Test per asset type  -> PlannedQty / DoneQty
    //   ATTRIBUTE  (RefId = AssetAttribute.Id)  yard attribute check/uncheck     -> IsDone
    //   ASSET      (RefId = Asset.Id)           per-asset                        -> IsDone or Planned/Done
    //   LOCATION   (RefId = SiteSurvey.Id)      per location box
    public class SiteStageProgressItem
    {
        public int Id { get; set; }
        public int SiteStageProgressId { get; set; }
        public int SiteId { get; set; }
        public int ProjectStageId { get; set; }

        public string RefType { get; set; }      // COMPONENT | ASSETTYPE | ATTRIBUTE | ASSET | LOCATION
        public int? RefId { get; set; }
        public string ItemCode { get; set; }
        public string ItemLabel { get; set; }

        public int? PlannedQty { get; set; }
        public int? DoneQty { get; set; }
        public bool? IsDone { get; set; }

        public int? ProgressStatusId { get; set; }
        public string Remarks { get; set; }
        public int? DoneBy { get; set; }
        public DateTime? DoneDate { get; set; }

        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int? LastModifiedBy { get; set; }
        public DateTime? LastModifiedDate { get; set; }

        // Extra (display only, filled by the API join — not columns)
        public string DoneByName { get; set; }
        public string StatusName { get; set; }
    }
}
