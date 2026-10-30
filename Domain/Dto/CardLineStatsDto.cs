using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class CardLineStatsDto
    {
        public int TotalClusters { get; set; }
        public int TotalAssets { get; set; }
        public int TotalDataPoints { get; set; }
        public int TotalLocations { get; set; }
        public string AssetBreakdown { get; set; }
        public List<AssetTypeCountDto> AssetTypeCounts { get; set; } = new();

        public List<CardLine> Records { get; set; } = new();
    }

    public class AssetTypeCountDto
    {
        public int? AssetTypeId { get; set; }
        public string AssetTypeName { get; set; }
        public int Count { get; set; }          // unique assets of this type
        public int DataPoints { get; set; }     // unique AssetId+AttributeId pairs
    }
}
