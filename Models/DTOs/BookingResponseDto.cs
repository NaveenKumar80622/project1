namespace PickNBook.Api.Models.DTOs;

public class BookingResponseDto
{
    public string BookingId { get; set; } = string.Empty;
    public int Id { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string Pnr { get; set; } = string.Empty;
    public string TripType { get; set; } = string.Empty;
    public int TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string FromCity { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public DateTime DepartureTimeUtc { get; set; }
    public DateTime ArrivalTimeUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string PassengerPhone { get; set; } = string.Empty;
    public string? PassengerEmail { get; set; }
    public string TravelClass { get; set; } = "Economy";
    public int Adults { get; set; }
    public int Children { get; set; }
    public int Infants { get; set; }
    public int SeatsBooked { get; set; }
    public decimal TotalPriceInr { get; set; }
    public DateTime BookedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
}

public class CreateFlightBookingRequestDto
{
    public string PassengerName { get; set; } = string.Empty;
    public string PassengerPhone { get; set; } = string.Empty;
    public string? PassengerEmail { get; set; }
    public string? CouponCode { get; set; }
    public int? SelectedPromotionId { get; set; }
    public int? SelectedFeaturedOfferId { get; set; }
    public int Adults { get; set; } = 1;
    public int Children { get; set; }
    public int Infants { get; set; }
    public string TravelClass { get; set; } = "Economy";
    public List<CreateFlightPassengerDto> Passengers { get; set; } = [];
    public string? PaymentMethod { get; set; }
}

public class CreateFlightPassengerDto
{
    public string FullName { get; set; } = string.Empty;
    public string PassengerType { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string? Nationality { get; set; }
    public string? Dob { get; set; }
}

public class FlightPricingPreviewRequestDto
{
    public string FlightId { get; set; } = string.Empty;
    public string TravelClass { get; set; } = "Economy";
    public string TripType { get; set; } = "OneWay";
    public int PassengerCount { get; set; } = 1;
    public string? CouponCode { get; set; }
    public int? SelectedFeaturedOfferId { get; set; }
}

public class FlightPassengerResponseDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PassengerType { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string? SeatNumber { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}

public class CreateBusBookingRequestDto
{
    // SRDV Tracking
    public string TraceId { get; set; } = string.Empty;
    public string ResultIndex { get; set; } = string.Empty;
    public int SrdvIndex { get; set; }
    public string? BlockKey { get; set; }
    
    // User Selection
    public string? BoardingPointId { get; set; }
    public string? BoardingPointName { get; set; }
    public DateTime? BoardingPointTime { get; set; }
    
    public string? DroppingPointId { get; set; }
    public string? DroppingPointName { get; set; }
    public DateTime? DroppingPointTime { get; set; }

    // Bus Details (required for DB creation since bus_bookings caching is removed)
    public string FromCity { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public string DepartureTime { get; set; } = string.Empty;
    public string? ArrivalTime { get; set; }
    public string? OperatorName { get; set; }
    public string? BusType { get; set; }
    public decimal TotalFare { get; set; }

    // Existing fields
    public string PassengerName { get; set; } = string.Empty;
    public string PassengerPhone { get; set; } = string.Empty;
    public string? PassengerEmail { get; set; }
    public string? CouponCode { get; set; }
    public int Seats { get; set; } = 1; // legacy fallback
    public List<CreateBusPassengerDto> Passengers { get; set; } = [];
    public int? PromotionId { get; set; }
    public int? SelectedFeaturedOfferId { get; set; }
    public string? PaymentMethod { get; set; }
}

public class CreateBusPassengerDto
{
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string? SeatNumber { get; set; }
    public int Age { get; set; }
    public decimal BaseFare { get; set; } // Added for SRDV dynamic pricing
    public string SeatType { get; set; } = string.Empty; // Added for SRDV dynamic pricing
    public decimal ExternalGst { get; set; } // Extracted from SRDV API directly
}

public class BusPassengerResponseDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string SeatNumber { get; set; } = string.Empty;
    public int Age { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}

public class CancelPassengersRequestDto
{
    public List<int> PassengerIds { get; set; } = new();
    public string? Reason { get; set; }
}
