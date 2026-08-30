namespace PickNBook.Api.Models
{
    public class FlightRouteStat
    {
        public int Id { get; set; }
        public string FromCity { get; set; } = string.Empty;
        public string ToCity { get; set; } = string.Empty;
        public long SearchCount { get; set; }
        public long BookingCount { get; set; }
        public DateTime? LastSearchedAtUtc { get; set; }
        public DateTime? LastBookedAtUtc { get; set; }
    }
}
