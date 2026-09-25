namespace PickNBook.Api.Models.DTOs;

public class FlightDiscountRequestDto
{
    public decimal Value { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string UpdatedBy { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class FlightRemarkRequestDto
{
    public string SourceType { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
}

public class FlightCouponRequestDto
{
    public decimal Value { get; set; }
    public string CouponType { get; set; } = string.Empty;
    public string CouponCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public int UseLimit { get; set; }
    public bool IsFirstTimeUserOnly { get; set; } = false;
    public string Status { get; set; } = "Active";
    public string? Remark { get; set; }
}

public class FlightConvenienceFeeRequestDto
{
    public string AmountType { get; set; } = "Fixed";
    public decimal Value { get; set; }
    public string Status { get; set; } = "Active";
    public string UpdatedBy { get; set; } = string.Empty;
}

public class PendingAirlineRequestDto
{
    public string AirlineCode { get; set; } = string.Empty;
    public string FareType { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class AirlineRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Status { get; set; } = "Active";
}

public class AirlineWebcheckLinkRequestDto
{
    public string Airline { get; set; } = string.Empty;
    public string AirlineCode { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class PopularDestinationRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string SubTitle { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Placement { get; set; } = "Main";
    public string? Url { get; set; }
    public string Status { get; set; } = "Active";
}

public class FlightCancellationRequestDto
{
    public int FlightReservationId { get; set; }
    public string CancellationStatus { get; set; } = "Pending";
    public string CustomerRefundStatus { get; set; } = "Pending";
    public string AdminRefundStatus { get; set; } = "Pending";
    public decimal CustomerRefundAmountInr { get; set; }
    public decimal CustomerCancellationChargeInr { get; set; }
    public decimal CustomerServiceChargeInr { get; set; }
    public decimal AdminRefundAmountInr { get; set; }
    public decimal AdminCancellationChargeInr { get; set; }
    public decimal AdminServiceChargeInr { get; set; }
    public string? SupplierRemark { get; set; }
    public string? CustomerRemark { get; set; }
    public string? AdminRemark { get; set; }
}

public class FlightAmendmentRequestDto
{
    public int FlightReservationId { get; set; }
    public string AmendmentStatus { get; set; } = "Pending";
    public string? SupplierRemark { get; set; }
    public string? CustomerRemark { get; set; }
    public string? AdminRemark { get; set; }
}

public class AdminFlightBookingResponseDto
{
    public int Id { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string Pnr { get; set; } = string.Empty;
    public string TripType { get; set; } = "OneWay";
    public string Status { get; set; } = string.Empty;
    public DateTime BookingDateUtc { get; set; }
    public DateTime BookingDateIst { get; set; }
    public DateOnly JourneyDateIst { get; set; }

    // Backward-Compatible Frontend Fields
    public string FromCity { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    public string Passenger { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string PassengerPhone { get; set; } = string.Empty;
    public string? PassengerEmail { get; set; }
    public string BookedBy { get; set; } = string.Empty;

    // Dynamic Route & Summary
    public string Route { get; set; } = string.Empty;
    public string Airline { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string TravelClass { get; set; } = "Economy";

    // Collections
    public List<FlightSegmentDto> Segments { get; set; } = new();
    public List<AdminFlightPassengerDto> Passengers { get; set; } = new();

    // Financial Breakdown
    public decimal BaseFareInr { get; set; }
    public decimal TaxInr { get; set; }
    public decimal MarkupAmountInr { get; set; }
    public decimal DiscountAmountInr { get; set; }
    public decimal ConvenienceFeeInr { get; set; }
    public decimal SsrAmountInr { get; set; }
    public decimal CustomerFareInr { get; set; }
    public decimal NetFareInr { get; set; }
    public decimal ProfitInr { get; set; }

    // Payments Table Source
    public string? PaymentStatus { get; set; }
    public string? RefundStatus { get; set; }
    public string? FulfillmentStatus { get; set; }

    // Cancellation Details
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public decimal? CancellationChargeInr { get; set; }
    public decimal? RefundAmountInr { get; set; }
}

public class FlightSegmentDto
{
    public int SegmentIndicator { get; set; }
    public int TripIndicator { get; set; }
    public string Airline { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTime DepartureTimeIst { get; set; }
    public DateTime ArrivalTimeIst { get; set; }
    public int DurationMinutes { get; set; }
    public string? Pnr { get; set; }
    public string? Baggage { get; set; }
    public string? CabinBaggage { get; set; }
}

public class AdminFlightPassengerDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PassengerType { get; set; } = "Adult";
    public string Gender { get; set; } = string.Empty;
    public string? SeatNumber { get; set; }
    public string? TicketNumber { get; set; }
    public string? Status { get; set; }
}

public class AdminFlightCancellationRequestDto
{
    public int Id { get; set; }
    public int? BookingId { get; set; }
    public string? BookingReference { get; set; }
    public string? Pnr { get; set; }
    public DateTime RequestDateUtc { get; set; }
    public string Segment { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string Status { get; set; } = "Pending";
    public decimal CustomerRefundAmountInr { get; set; }
    public decimal AdminRefundAmountInr { get; set; }
    public string? Remark { get; set; }
    public AdminFlightCancellationDetailsDto Details { get; set; } = new();
}

public class AdminFlightCancellationDetailsDto
{
    public string CancellationStatus { get; set; } = "Pending";
    public string CustomerRefundStatus { get; set; } = "Pending";
    public string AdminRefundStatus { get; set; } = "Pending";
    public decimal CustomerRefundAmountInr { get; set; }
    public decimal CustomerCancellationChargeInr { get; set; }
    public decimal CustomerServiceChargeInr { get; set; }
    public decimal AdminRefundAmountInr { get; set; }
    public decimal AdminCancellationChargeInr { get; set; }
    public decimal AdminServiceChargeInr { get; set; }
    public string? SupplierRemark { get; set; }
    public string? CustomerRemark { get; set; }
    public string? AdminRemark { get; set; }
}
