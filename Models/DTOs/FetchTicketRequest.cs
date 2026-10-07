namespace PickNBook.Api.Models.DTOs
{
    public class FetchTicketRequest
    {
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? BookingType { get; set; } // "bus" | "flight" | "hotel" | "all"
        public string? BookingReference { get; set; } // PNR, BookingReference, or ConfirmationNo
        public bool ActiveOnly { get; set; } = true; // default true
    }
}
