using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class FlightSearchProxyRequestDto
    {

        [JsonPropertyName("AdultCount")]
        public int AdultCount { get; set; }

        [JsonPropertyName("ChildCount")]
        public int ChildCount { get; set; }

        [JsonPropertyName("InfantCount")]
        public int InfantCount { get; set; }

        [JsonPropertyName("JourneyType")]
        public int JourneyType { get; set; }

        [JsonPropertyName("DirectFlight")]
        public bool? DirectFlight { get; set; }

        [JsonPropertyName("Segments")]
        public List<AirSearchSegmentDto> Segments { get; set; } = new();
    }

    public class FlightFareRuleProxyRequestDto
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

        [JsonPropertyName("JourneyType")]
        public int? JourneyType { get; set; }

        [JsonPropertyName("AdultCount")]
        public int? AdultCount { get; set; }

        [JsonPropertyName("ChildCount")]
        public int? ChildCount { get; set; }

        [JsonPropertyName("InfantCount")]
        public int? InfantCount { get; set; }
    }

    public class FlightTicketLCCProxyRequestDto
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
        public string BookingId { get; set; }

        [JsonPropertyName("RequestType")]
        public string RequestType { get; set; }

        [JsonPropertyName("CancellationType")]
        public string CancellationType { get; set; }

        [JsonPropertyName("Remarks")]
        public string Remarks { get; set; }

        [JsonPropertyName("Sectors")]
        public List<ChangeRequestSectorDto> Sectors { get; set; } = new();

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; }

        [JsonPropertyName("SrdvIndex")]
        public string SrdvIndex { get; set; }

        [JsonPropertyName("TicketData")]
        public List<ChangeRequestTicketDataDto> TicketData { get; set; } = new();

        [JsonPropertyName("PNR")]
        public string PNR { get; set; }
    }

    public class FlightGetCancelStatusProxyRequestDto
    {

        [JsonPropertyName("ChangeRequestId")]
        public string ChangeRequestId { get; set; }
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

