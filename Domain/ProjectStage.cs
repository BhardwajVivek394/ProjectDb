namespace Domain
{
    // Master list of project stages (dbo.ProjectStage).
    // Survey, Fitting, Networking, Wiring, Termination, Validation, Calibration, Alert Test, Document.
    public class ProjectStage
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public int SequenceNo { get; set; }
        public string ColorCode { get; set; }
        public bool IsActive { get; set; }
    }
}
