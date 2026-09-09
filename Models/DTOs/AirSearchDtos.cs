using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class AirSearchRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        [JsonPropertyName("AdultCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int AdultCount { get; set; } = 1;

        [JsonPropertyName("ChildCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int ChildCount { get; set; } = 0;

        [JsonPropertyName("InfantCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int InfantCount { get; set; } = 0;

        [JsonPropertyName("JourneyType")]
        [JsonConverter(typeof(SafeStringConverter))]
        public string JourneyType { get; set; } = "1";

        [JsonPropertyName("CurrencyCode")]
        public string CurrencyCode { get; set; } = "INR";

        [JsonPropertyName("FareType")]
        public string FareType { get; set; } = "1";

        [JsonPropertyName("DirectFlight")]
        public bool? DirectFlight { get; set; }

        [JsonPropertyName("Segments")]
        public List<AirSearchSegmentDto> Segments { get; set; } = new();
    }

    public class AirSearchSegmentDto
    {
        [JsonPropertyName("Origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("Destination")]
        public string Destination { get; set; } = string.Empty;

        [JsonPropertyName("FlightCabinClass")]
        [JsonConverter(typeof(SafeStringConverter))]
        public string FlightCabinClass { get; set; } = "1";

        [JsonPropertyName("PreferredDepartureTime")]
        public DateTime PreferredDepartureTime { get; set; }

        [JsonPropertyName("PreferredArrivalTime")]
        public DateTime PreferredArrivalTime { get; set; }
    }

    public class AirRecheckSearchRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }
    }

    public class AirBookingDetailsRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }
    }

    public class AirFareQuoteRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }
    }

    public class AirFareRuleRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; } = string.Empty;

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; } = string.Empty;

        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("CouponCode")]
        public string? CouponCode { get; set; }

        // Client-side hints for pricing (not sent to SRDV, used locally for markup calculation)
        [JsonPropertyName("JourneyType")]
        public int? JourneyType { get; set; }

        [JsonPropertyName("AdultCount")]
        public int? AdultCount { get; set; }

        [JsonPropertyName("ChildCount")]
        public int? ChildCount { get; set; }

        [JsonPropertyName("InfantCount")]
        public int? InfantCount { get; set; }
    }

    public class TicketLCCRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("SrdvType")]
        public string? SrdvType { get; set; } = "MixAPI";

        [JsonPropertyName("SrdvIndex")]
        public string? SrdvIndex { get; set; } = "1";

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; } = string.Empty;

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("CouponCode")]
        public string? CouponCode { get; set; }

        [JsonPropertyName("PromoCode")]
        public string? PromoCode { get; set; }

        [JsonPropertyName("PromotionId")]
        public int? PromotionId { get; set; }

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        [JsonPropertyName("JourneyType")]
        public int? JourneyType { get; set; }

        [JsonPropertyName("RefID")]
        public string? RefID { get; set; } = string.Empty;

        [JsonPropertyName("Module")]
        public string? Module { get; set; } = "b2c";

        [JsonPropertyName("BookedById")]
        public int? BookedById { get; set; }

        [JsonPropertyName("BookedByName")]
        public string? BookedByName { get; set; } = string.Empty;

        [JsonPropertyName("CustomerFare")]
        public decimal? CustomerFare { get; set; }

        [JsonPropertyName("ReturnCustomerFare")]
        public decimal? ReturnCustomerFare { get; set; }

        [JsonPropertyName("Passengers")]
        public List<LCCPassengerDto> Passengers { get; set; } = new();
    }

    public class LCCPassengerDto
    {
        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("FirstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("LastName")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("MiddleName")]
        public string MiddleName { get; set; } = string.Empty;


        [JsonPropertyName("PaxType")]
        [JsonConverter(typeof(SafeIntConverter))]
        public int PaxType { get; set; } = 1;

        [JsonPropertyName("DateOfBirth")]
        public string DateOfBirth { get; set; } = string.Empty;

        [JsonPropertyName("Gender")]
        public string Gender { get; set; } = "1";

        [JsonPropertyName("PassportNo")]
        public string PassportNo { get; set; } = string.Empty;

        [JsonPropertyName("PassportExpiry")]
        public string PassportExpiry { get; set; } = string.Empty;

        [JsonPropertyName("PassportIssueDate")]
        public string PassportIssueDate { get; set; } = string.Empty;

        [JsonPropertyName("PassportIssueCountryCode")]
        public string PassportIssueCountryCode { get; set; } = string.Empty;

        [JsonPropertyName("DocumentType")]
        public string DocumentType { get; set; } = string.Empty;

        [JsonPropertyName("DocumentId")]
        public string DocumentId { get; set; } = string.Empty;


        [JsonPropertyName("AddressLine1")]
        public string AddressLine1 { get; set; } = string.Empty;

        [JsonPropertyName("City")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("CountryCode")]
        public string CountryCode { get; set; } = string.Empty;

        [JsonPropertyName("CountryName")]
        public string CountryName { get; set; } = string.Empty;

        [JsonPropertyName("CellCountryCode")]
        public string CellCountryCode { get; set; } = "+91";

        [JsonPropertyName("ContactNo")]
        public string ContactNo { get; set; } = string.Empty;

        [JsonPropertyName("Email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("IsLeadPax")]
        public bool IsLeadPax { get; set; } = true;

        [JsonPropertyName("GSTCompanyAddress")]
        public string GSTCompanyAddress { get; set; } = string.Empty;

        [JsonPropertyName("GSTCompanyContactNumber")]
        public string GSTCompanyContactNumber { get; set; } = string.Empty;

        [JsonPropertyName("GSTCompanyName")]
        public string GSTCompanyName { get; set; } = string.Empty;

        [JsonPropertyName("GSTNumber")]
        public string GSTNumber { get; set; } = string.Empty;

        [JsonPropertyName("GSTCompanyEmail")]
        public string GSTCompanyEmail { get; set; } = string.Empty;


        [JsonPropertyName("Fare")]
        [JsonConverter(typeof(SafePassengerFareConverter))]
        public LCCPassengerFareDto? Fare { get; set; } = new();

        [JsonPropertyName("Baggage")]
        public List<LCCBaggageDto> Baggage { get; set; } = new();

        [JsonPropertyName("MealDynamic")]
        public List<LCCMealDynamicDto> MealDynamic { get; set; } = new();

        [JsonPropertyName("EContactName")]
        public string? EContactName { get; set; } = string.Empty;

        [JsonPropertyName("EContactEmail")]
        public string? EContactEmail { get; set; } = string.Empty;

        [JsonPropertyName("EContactMobile")]
        public string? EContactMobile { get; set; } = string.Empty;

        [JsonPropertyName("Seat")]
        public List<LCCSeatDto> Seat { get; set; } = new();
    }

    public class LCCPassengerFareDto
    {
        [JsonPropertyName("Currency")]
        public string Currency { get; set; } = "INR";

        [JsonPropertyName("BaseFare")]
        public decimal BaseFare { get; set; }

        [JsonPropertyName("Tax")]
        public decimal Tax { get; set; }

        [JsonPropertyName("TransactionFee")]
        public decimal TransactionFee { get; set; }

        [JsonPropertyName("YQTax")]
        public decimal YQTax { get; set; }

        [JsonPropertyName("OtherCharges")]
        public decimal OtherCharges { get; set; }

        [JsonPropertyName("AdditionalTxnFeeOfrd")]
        public decimal AdditionalTxnFeeOfrd { get; set; }

        [JsonPropertyName("AdditionalTxnFeePub")]
        public decimal AdditionalTxnFeePub { get; set; }

        [JsonPropertyName("AirTransFee")]
        public decimal AirTransFee { get; set; }
    }

    public class LCCBaggageDto
    {
        [JsonPropertyName("AirlineCode")]
        public string AirlineCode { get; set; } = string.Empty;

        [JsonPropertyName("FlightNumber")]
        public string FlightNumber { get; set; } = string.Empty;

        [JsonPropertyName("WayType")]
        public int WayType { get; set; }

        [JsonPropertyName("Code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("Weight")]
        public string Weight { get; set; } = string.Empty;

        [JsonPropertyName("Currency")]
        public string Currency { get; set; } = "INR";

        [JsonPropertyName("Price")]
        [JsonConverter(typeof(SafeNullableDecimalConverter))]
        public decimal? Price { get; set; }

        [JsonPropertyName("Origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("Destination")]
        public string Destination { get; set; } = string.Empty;
    }

    public class LCCMealDynamicDto
    {
        [JsonPropertyName("AirlineCode")]
        public string AirlineCode { get; set; } = string.Empty;

        [JsonPropertyName("FlightNumber")]
        public string FlightNumber { get; set; } = string.Empty;

        [JsonPropertyName("WayType")]
        public int WayType { get; set; }

        [JsonPropertyName("Code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("AirlineDescription")]
        public string AirlineDescription { get; set; } = string.Empty;

        [JsonPropertyName("Quantity")]
        public string Quantity { get; set; } = string.Empty;

        [JsonPropertyName("Currency")]
        public string Currency { get; set; } = "INR";

        [JsonPropertyName("Price")]
        [JsonConverter(typeof(SafeNullableDecimalConverter))]
        public decimal? Price { get; set; }

        [JsonPropertyName("Origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("Destination")]
        public string Destination { get; set; } = string.Empty;
    }

    public class LCCSeatDto
    {
        [JsonPropertyName("AirlineCode")]
        public string AirlineCode { get; set; } = string.Empty;

        [JsonPropertyName("FlightNumber")]
        public string FlightNumber { get; set; } = string.Empty;

        [JsonPropertyName("SeatNumber")]
        public string SeatNumber { get; set; } = string.Empty;

        [JsonPropertyName("IsBooked")]
        public bool IsBooked { get; set; }

        [JsonPropertyName("IsLegroom")]
        public bool? IsLegroom { get; set; }

        [JsonPropertyName("IsAisle")]
        public bool IsAisle { get; set; }

        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(SafeNullableDoubleConverter))]
        public double? Amount { get; set; }

        [JsonPropertyName("Code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("Origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("Destination")]
        public string Destination { get; set; } = string.Empty;
    }

    public class HoldGDSRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; } = string.Empty;

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; } = string.Empty;

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; } = string.Empty;

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("CouponCode")]
        public string? CouponCode { get; set; }

        [JsonPropertyName("PromoCode")]
        public string? PromoCode { get; set; }

        [JsonPropertyName("PromotionId")]
        public int? PromotionId { get; set; }

        [JsonPropertyName("JourneyType")]
        public int? JourneyType { get; set; }

        [JsonPropertyName("Passengers")]
        public List<LCCPassengerDto> Passengers { get; set; } = new();
    }

    public class TicketGDSRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; } = string.Empty;

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; } = string.Empty;

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; } = string.Empty;

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("PNR")]
        public string PNR { get; set; } = string.Empty;

        [JsonPropertyName("BookingId")]
        public int BookingId { get; set; }

        [JsonPropertyName("CouponCode")]
        public string? CouponCode { get; set; }

        [JsonPropertyName("PromoCode")]
        public string? PromoCode { get; set; }

        [JsonPropertyName("PromotionId")]
        public int? PromotionId { get; set; }

        [JsonPropertyName("Passengers")]
        public List<LCCPassengerDto> Passengers { get; set; } = new();
    }

    public class CalendarFareSegmentDto
    {
        [JsonPropertyName("Origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("Destination")]
        public string Destination { get; set; } = string.Empty;

        [JsonPropertyName("FlightCabinClass")]
        public int FlightCabinClass { get; set; }

        [JsonPropertyName("PreferredDepartureTime")]
        public DateTime PreferredDepartureTime { get; set; }

        [JsonPropertyName("PreferredArrivalTime")]
        public DateTime PreferredArrivalTime { get; set; }
    }

    public class CalendarFareRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        [JsonPropertyName("JourneyType")]
        public int JourneyType { get; set; } = 1;

        [JsonPropertyName("Sources")]
        public string? Sources { get; set; }

        [JsonPropertyName("FareType")]
        public int FareType { get; set; } = 1;

        [JsonPropertyName("Segments")]
        public List<CalendarFareSegmentDto> Segments { get; set; } = new();
    }

    public class CallbackPassengerDto
    {
        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("FirstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("LastName")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("TicketNumber")]
        public string? TicketNumber { get; set; }
    }

    public class FlightCallbackRequestDto
    {
        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; } = string.Empty;

        [JsonPropertyName("BookingId")]
        public string BookingId { get; set; } = string.Empty;

        [JsonPropertyName("PNR")]
        public string PNR { get; set; } = string.Empty;

        [JsonPropertyName("GdsPNR")]
        public string? GdsPNR { get; set; }

        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("Remark")]
        public string? Remark { get; set; }

        [JsonPropertyName("Passengers")]
        public List<CallbackPassengerDto> Passengers { get; set; } = new();
    }

    public class ChangeRequestSectorDto
    {
        [JsonPropertyName("Origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("Destination")]
        public string Destination { get; set; } = string.Empty;
    }

    public class ChangeRequestTicketDataDto
    {
        [JsonPropertyName("TicketId")]
        public string TicketId { get; set; } = string.Empty;

        [JsonPropertyName("FirstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("LastName")]
        public string LastName { get; set; } = string.Empty;
    }

    public class SendChangeRequestDto
    {
        [JsonPropertyName("BookingId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long BookingId { get; set; }

        [JsonPropertyName("RequestType")]
        [JsonConverter(typeof(SafeIntConverter))]
        public int RequestType { get; set; } = 2;

        [JsonPropertyName("CancellationType")]
        [JsonConverter(typeof(SafeIntConverter))]
        public int CancellationType { get; set; } = 3;

        [JsonPropertyName("PNR")]
        public string PNR { get; set; } = string.Empty;

        [JsonPropertyName("Remarks")]
        public string Remarks { get; set; } = string.Empty;

        [JsonPropertyName("ClientRefId")]
        public string? ClientRefId { get; set; } = string.Empty;

        [JsonPropertyName("Sectors")]
        public List<ChangeRequestSectorDto> Sectors { get; set; } = new();

        [JsonPropertyName("TicketData")]
        public List<ChangeRequestTicketDataDto> TicketData { get; set; } = new();

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        // Legacy compatibility fields
        public string? EndUserIp { get; set; }
        public string? ClientId { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? SrdvType { get; set; } = "MixAPI";
        public string? SrdvIndex { get; set; } = string.Empty;
    }

    public class GetCancelStatusRequestDto
    {
        [JsonPropertyName("ChangeRequestId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long ChangeRequestId { get; set; }

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        // Legacy compatibility fields
        public string? UserId { get; set; }
        public string? EndUserIp { get; set; }
        public string? ClientId { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
    }

    public class GetCancellationChargesRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string? EndUserIp { get; set; }

        [JsonPropertyName("ClientId")]
        public string? ClientId { get; set; }

        [JsonPropertyName("UserName")]
        public string? UserName { get; set; }

        [JsonPropertyName("Password")]
        public string? Password { get; set; }

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }

        [JsonPropertyName("RequestType")]
        public int RequestType { get; set; } = 1;

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; } = string.Empty;
    }

    public class ApiBalanceRequestDto
    {
        [JsonPropertyName("EndUserIp")]
        public string EndUserIp { get; set; } = string.Empty;

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        [JsonPropertyName("Password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("ApiToken")]
        public string? ApiToken { get; set; }
    }

    public class MyFlightPassengerDto
    {
        public string FullName { get; set; } = string.Empty;
        public string? SeatNumber { get; set; }
        public string? TicketId { get; set; }
    }

    public class MyFlightBookingResponseDto
    {
        public string BookingReference { get; set; } = string.Empty;
        public string FromCity { get; set; } = string.Empty;
        public string ToCity { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal TotalFare { get; set; }

        // Fields needed for Cancellation API
        public string? TraceId { get; set; }
        public string? BookingId { get; set; } // maps to SrdvBookingId
        public string? PNR { get; set; }
        public string? SrdvType { get; set; }
        public string? SrdvIndex { get; set; }

        public List<MyFlightPassengerDto> Passengers { get; set; } = new();
    }
}

