namespace PickNBook.Api.Models.DTOs
{
    public class BookingHistoryDto
    {
        public int BookingId { get; set; }

        public string BookingReference { get; set; } = string.Empty;

        public string TripType { get; set; } = string.Empty;

        public string From { get; set; } = string.Empty;

        public string To { get; set; } = string.Empty;

        public string Date { get; set; } = string.Empty;

        public string Time { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;

        public string CtaLabel { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime OriginalDate { get; set; }
    }
}
