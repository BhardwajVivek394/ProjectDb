using System;

namespace Domain
{
    public class LocationPhoto
    {
        public int Id { get; set; }
        public int ClusterId { get; set; }
        public int SiteId { get; set; }
        public string ImagePath { get; set; }
        public string OriginalFileName { get; set; }
        public string Caption { get; set; }
        public int? SortOrder { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }

        // transport
        public string FileBase64 { get; set; }
        public string FileExtension { get; set; }

        // display
        public string ImageUrl { get; set; }
        public string ClusterName { get; set; }
    }
}
