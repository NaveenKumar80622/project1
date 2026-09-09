using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class FlightSearchProxyRequestDto
    {
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

    public class FlightRecheckSearchProxyRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }
    }

    public class FlightBookingDetailsProxyRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }
    }

    public class FlightFareRuleProxyRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("SrdvType")]
        public string? SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string? SrdvIndex { get; set; }

        [JsonPropertyName("CouponCode")]
        public string? CouponCode { get; set; }

        [JsonPropertyName("JourneyType")]
        public int? JourneyType { get; set; }

        [JsonPropertyName("AdultCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? AdultCount { get; set; }

        [JsonPropertyName("ChildCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? ChildCount { get; set; }

        [JsonPropertyName("InfantCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? InfantCount { get; set; }
    }

    public class FlightSeatMapProxyRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("SrdvType")]
        public string? SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string? SrdvIndex { get; set; }
    }

    public class FlightSSRProxyRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("SrdvType")]
        public string? SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string? SrdvIndex { get; set; }
    }

    public class FlightFareQuoteProxyRequestDto
    {
        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

        [JsonPropertyName("CouponCode")]
        public string? CouponCode { get; set; }

        [JsonPropertyName("JourneyType")]
        [JsonConverter(typeof(SafeStringConverter))]
        public string? JourneyType { get; set; }

        [JsonPropertyName("AdultCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? AdultCount { get; set; }

        [JsonPropertyName("ChildCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? ChildCount { get; set; }

        [JsonPropertyName("InfantCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? InfantCount { get; set; }

        [JsonPropertyName("SrdvType")]
        public string? SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string? SrdvIndex { get; set; }
    }

    public class FlightTicketLCCProxyRequestDto
    {

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; }

        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; } = string.Empty;

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

    public class FlightHoldGDSProxyRequestDto
    {

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; }

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; }

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

    public class FlightTicketGDSProxyRequestDto
    {

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; }

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; }

        [JsonPropertyName("PNR")]
        public string PNR { get; set; }

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

    public class FlightCalendarFareProxyRequestDto
    {

        [JsonPropertyName("JourneyType")]
        public int JourneyType { get; set; }

        [JsonPropertyName("Sources")]
        public string? Sources { get; set; }

        [JsonPropertyName("FareType")]
        public int FareType { get; set; }

        [JsonPropertyName("Segments")]
        public List<CalendarFareSegmentDto> Segments { get; set; } = new();
    }

    public class FlightSendChangeProxyRequestDto
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

        [JsonPropertyName("Remarks")]
        public string Remarks { get; set; } = string.Empty;

        [JsonPropertyName("ClientRefId")]
        public string? ClientRefId { get; set; } = string.Empty;

        [JsonPropertyName("Sectors")]
        public List<ChangeRequestSectorDto> Sectors { get; set; } = new();

        [JsonPropertyName("TicketData")]
        public List<ChangeRequestTicketDataDto> TicketData { get; set; } = new();

        [JsonPropertyName("PNR")]
        public string PNR { get; set; } = string.Empty;

        [JsonPropertyName("SrdvType")]
        public string? SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string? SrdvIndex { get; set; }
    }

    public class FlightGetCancelStatusProxyRequestDto
    {
        [JsonPropertyName("ChangeRequestId")]
        [JsonConverter(typeof(SafeLongConverter))]
        public long ChangeRequestId { get; set; }
    }

    public class FlightGetCancellationChargesProxyRequestDto
    {

        [JsonPropertyName("RequestType")]
        public int RequestType { get; set; }

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; }
    }
    public class FlightBookingCallbackProxyRequestDto
    {

        [JsonPropertyName("TraceId")]
        public string TraceId { get; set; }

        [JsonPropertyName("BookingId")]
        public string BookingId { get; set; }

        [JsonPropertyName("PNR")]
        public string PNR { get; set; }

        [JsonPropertyName("GdsPNR")]
        public string GdsPNR { get; set; }

        [JsonPropertyName("Status")]
        public string Status { get; set; }

        [JsonPropertyName("Remark")]
        public string Remark { get; set; }

        [JsonPropertyName("Passengers")]
        public List<SrdvCallbackPassengerDto> Passengers { get; set; } = new();
    }
}

