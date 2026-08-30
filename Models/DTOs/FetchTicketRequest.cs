namespace PickNBook.Api.Models.DTOs
{
    public class FetchTicketRequest
    {
        public string Mobile { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string BookingType { get; set; } = string.Empty; // "bus" | "flight"
        public bool ActiveOnly { get; set; } = true; // default true
    }
}
