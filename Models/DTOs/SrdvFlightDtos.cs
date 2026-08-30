using System.Collections.Generic;

namespace PickNBook.Api.Models.DTOs
{
    public class FlightFareQuoteDto
    {
        public bool IsPriceChanged { get; set; }
        public decimal NewPrice { get; set; }
        public bool IsAvailable { get; set; }
        public string? ResponseJson { get; set; }
    }

    public class FlightBookingRequestDto
    {
        public string TraceId { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
        public List<FlightPassengerDto> Passengers { get; set; } = new();
    }

    public class FlightPassengerDto
    {
        public string Title { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int PaxType { get; set; } // 1: Adult, 2: Child, 3: Infant
        public string DateOfBirth { get; set; } = string.Empty; // yyyy-MM-ddT00:00:00
        public int Gender { get; set; } // 1: Male, 2: Female
        public string PassportNo { get; set; } = string.Empty;
        public string PassportExpiry { get; set; } = string.Empty;
        public string AddressLine1 { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string CountryCode { get; set; } = "IN";
        public string ContactNo { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class FlightBookingResponseDto
    {
        public bool Success { get; set; }
        public string? SrdvBookingId { get; set; }
        public string? Pnr { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResponseJson { get; set; }
    }

    public class FlightTicketResponseDto
    {
        public bool Success { get; set; }
        public string? Pnr { get; set; }
        public string? ErrorMessage { get; set; }
        public string? TicketResponseJson { get; set; }
    }
}
