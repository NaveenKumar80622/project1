namespace PickNBook.Api.Models
{
    public class FlightRemark
    {
        public int Id { get; set; }
        public DateTime EntryDateUtc { get; set; }
        public DateTime UpdateDateUtc { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public string UpdatedBy { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
    }
}
