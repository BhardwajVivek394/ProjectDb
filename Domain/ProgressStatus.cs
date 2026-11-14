namespace Domain
{
    // Lookup for stage/item status (dbo.ProgressStatus):
    // 0 Not Started, 1 Not Complete, 2 Partial Complete, 3 Completed.
    public class ProgressStatus
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string ColorCode { get; set; }
        public int SequenceNo { get; set; }
        public bool IsActive { get; set; }
    }
}
