namespace PickNBook.Api.Models
{
    public class FlightConvenienceFee
    {
        public int Id { get; set; }
        public string AmountType { get; set; } = "Fixed";
        public decimal Value { get; set; }
        public DateTime EntryDateUtc { get; set; }
        public DateTime UpdateDateUtc { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
    }
}
