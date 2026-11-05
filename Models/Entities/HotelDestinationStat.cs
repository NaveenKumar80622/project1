namespace PickNBook.Api.Models
{
    public class HotelDestinationStat
    {
        public int Id { get; set; }
        public string CityName { get; set; } = string.Empty;
        public long? CityId { get; set; }
        public string? FeaturedHotelName { get; set; }
        public decimal? StartingPrice { get; set; }
        public string? ImageUrl { get; set; }
        public long SearchCount { get; set; }
        public long BookingCount { get; set; }
        public DateTime? LastSearchedAtUtc { get; set; }
        public DateTime? LastBookedAtUtc { get; set; }
    }
}

