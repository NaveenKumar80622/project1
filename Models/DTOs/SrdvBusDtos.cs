using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class SrdvBusOfferDto
    {
        public string RouteId { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public string OperatorId { get; set; } = string.Empty;
        public string BusType { get; set; } = string.Empty;
        public string DepartureTime { get; set; } = string.Empty;
        public string ArrivalTime { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int AvailableSeats { get; set; }
        
        public string? TraceId { get; set; }
        public string? ResultIndex { get; set; }
        public int? SrdvIndex { get; set; }

        public bool IsGSTMandatory { get; set; }
        public bool IsTypeRequired { get; set; }
        public bool IsDropPointMandatory { get; set; }
    }
    public class SrdvCancellationPolicyDto
    {
        public string CancellationCharge { get; set; } = string.Empty;
        public string CancellationChargeType { get; set; } = string.Empty;
        public string TimeBeforeDept { get; set; } = string.Empty;
        public string FromDate { get; set; } = string.Empty;
        public string PolicyString { get; set; } = string.Empty;
    }

    public class BusSeatLayoutProxyRequestDto
    {
        public string TraceId { get; set; } = string.Empty;
        public string SrdvIndex { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
        public string? BoardingPointId { get; set; }
        public string? DroppingPointId { get; set; }
        public bool? BpDpSeatLayout { get; set; }
    }

    public class BusBoardingPointsProxyRequestDto
    {
        public string TraceId { get; set; } = string.Empty;
        public string? SrdvIndex { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
    }
    public class SrdvBusBookingRequestDto
    {
        public string TraceId { get; set; } = string.Empty;
        public string ResultIndex { get; set; } = string.Empty;
        public int SrdvIndex { get; set; }
        public string BoardingPointId { get; set; } = string.Empty;
        public string DroppingPointId { get; set; } = string.Empty;
        public string? RefId { get; set; }
        public string FromCity { get; set; } = string.Empty;
        public string ToCity { get; set; } = string.Empty;
        public string DepartureTime { get; set; } = string.Empty;
        public string ArrivalTime { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public string BusType { get; set; } = string.Empty;
        public decimal TotalFare { get; set; }
        public List<SrdvBusPassengerDto> Passengers { get; set; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;
    }

    public class SrdvBusPassengerDto
    {
        public string Title { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Age { get; set; }
        public int Gender { get; set; } // 1: Male, 2: Female
        public string SeatName { get; set; } = string.Empty;
        public decimal Fare { get; set; }
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ContactNo { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool? LeadPassenger { get; set; }
        
        public string? IdType { get; set; }
        public string? IdNumber { get; set; }
        public string? GSTCompanyAddress { get; set; }
        public string? GSTCompanyContactNumber { get; set; }
        public string? GSTCompanyName { get; set; }
        public string? GSTNumber { get; set; }
        public string? GSTCompanyEmail { get; set; }
    }

    public class SrdvBusBookingResponseDto
    {
        public bool Success { get; set; }
        public string? SrdvBookingId { get; set; }
        public string? TicketNo { get; set; }
        public string? TravelOperatorPNR { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResponseJson { get; set; }
    }

    public class SrdvSeatDto
    {
        public string SeatName { get; set; } = string.Empty;
        public string SeatStatus { get; set; } = string.Empty;
        public string SeatType { get; set; } = string.Empty;
        public decimal SeatFare { get; set; }
        public int RowNo { get; set; }
        public int ColumnNo { get; set; }
        public bool IsUpper { get; set; }
    }

    public class SrdvBoardingDroppingDetailsDto
    {
        public List<BusPointDto> BoardingPoints { get; set; } = new();
        public List<BusPointDto> DroppingPoints { get; set; } = new();
    }

    public class BusSearchProxyRequestDto
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int FromCityCode { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int ToCityCode { get; set; }

        public string DepartDate { get; set; } = string.Empty; // Format: YYYY-MM-DD
    }

    public class BusCityDto
    {
        public string CityId { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;
        public string StateName { get; set; } = string.Empty;
    }

    public class AdminCancelBusBookingRequestDto
    {
        public string Reason { get; set; } = "Cancelled by admin";
        public decimal CancellationCharges { get; set; } = 0m;
        public List<int>? PassengerIdsToCancel { get; set; }
    }

    public class SrdvBusBookingDetailsResponseDto
    {
        public bool Success { get; set; }
        public SrdvBusBookingDetailsErrorDto? Error { get; set; }
        public long TraceId { get; set; }
        public SrdvBusBookingDetailsResultDto? Result { get; set; }
        public string? ResponseJson { get; set; }
    }

    public class SrdvBusBookingDetailsErrorDto
    {
        public int ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class SrdvBusBookingDetailsResultDto
    {
        public int SrdvIndex { get; set; }
        public string? ResultIndex { get; set; }
        public string? BookingId { get; set; }
        public string? RefId { get; set; }
        public string? BookingStatus { get; set; }
        public string? TicketNo { get; set; }
        public string? TravelOperatorPNR { get; set; }
        public decimal DsaFare { get; set; }
        public string? CurrencyCode { get; set; }
        public string? CancelStatus { get; set; }
        public string? RefundStatus { get; set; }
        public int ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<SrdvBusBookingDetailsPassengerDto> Passengers { get; set; } = new();
        public List<SrdvBusBookingDetailsCancellationDto> Cancellations { get; set; } = new();
    }

    public class SrdvBusBookingDetailsPassengerDto
    {
        public string? SeatName { get; set; }
        public int SeatIndex { get; set; }
        public bool IsUpper { get; set; }
        public string? Title { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Gender { get; set; }
        public int Age { get; set; }
        public bool LeadPassenger { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal BaseFare { get; set; }
        public decimal Tax { get; set; }
        public decimal PublishedFare { get; set; }
        public decimal OfferedFare { get; set; }
        public decimal GstRate { get; set; }
        public decimal GSTAmount { get; set; }
        public string? CancelStatus { get; set; }
        public DateTime? CancelledAt { get; set; }
    }

    public class SrdvBusBookingDetailsCancellationDto
    {
        public string? CancelId { get; set; }
        public string? Status { get; set; }
        public string? CancellationType { get; set; }
        public List<string> SeatName { get; set; } = new();
        public string? SupplierCancelId { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal CancellationCharge { get; set; }
        public string? RefundStatus { get; set; }
        public int ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
