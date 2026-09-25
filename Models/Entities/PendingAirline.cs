namespace PickNBook.Api.Models
{
    public class PendingAirline
    {
        public int Id { get; set; }
        public string AirlineCode { get; set; } = string.Empty;
        public string FareType { get; set; } = string.Empty;
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime UpdatedOnUtc { get; set; }
        public string? Remark { get; set; }
    }
}
