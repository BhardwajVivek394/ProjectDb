using System;

namespace Domain
{
    public class SiteSurveyNMDL
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string Category { get; set; }
        public string FileName { get; set; }
        public string OriginalFileName { get; set; }
        public string Remarks { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }

        // transport
        public string FileBase64 { get; set; }
        public string FileExtension { get; set; }

        // display
        public string FileUrl { get; set; }
    }
}
