namespace PickNBook.Api.Models
{
    public class BusConvenienceFee
    {
        public int Id { get; set; }
        public decimal FeeInr { get; set; }
        public DateTime EntryDateUtc { get; set; }
        public DateTime UpdateDateUtc { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
    }
}
