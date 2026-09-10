using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class SrdvBookingCallbackRequestDto
    {
        [JsonPropertyName("ClientId")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int ClientId { get; set; }

        [JsonPropertyName("TraceId")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long TraceId { get; set; }

        [JsonPropertyName("BookingId")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int BookingId { get; set; }

        [JsonPropertyName("PNR")]
        public string PNR { get; set; } = string.Empty;

        [JsonPropertyName("GdsPNR")]
        public string GdsPNR { get; set; } = string.Empty;

        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("Remark")]
        public string Remark { get; set; } = string.Empty;

        [JsonPropertyName("Passengers")]
        public List<SrdvCallbackPassengerDto> Passengers { get; set; } = new();

        [JsonPropertyName("Module")]
        public string Module { get; set; } = string.Empty;

        [JsonPropertyName("Event")]
        public string Event { get; set; } = string.Empty;

        [JsonPropertyName("SrdvIndex")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int SrdvIndex { get; set; }

        [JsonPropertyName("ReturnPNR")]
        public string ReturnPNR { get; set; } = string.Empty;

        [JsonPropertyName("ReturnStatus")]
        public string ReturnStatus { get; set; } = string.Empty;

        [JsonPropertyName("RefundStatus")]
        public string RefundStatus { get; set; } = string.Empty;

        [JsonPropertyName("CancellationStatus")]
        public string CancellationStatus { get; set; } = string.Empty;

        [JsonPropertyName("Origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("Destination")]
        public string Destination { get; set; } = string.Empty;

        [JsonPropertyName("UpdatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;

        // Legacy / Echo fields for safety
        [JsonPropertyName("UserName")]
        public string? UserName { get; set; }

        [JsonPropertyName("Password")]
        public string? Password { get; set; }

        [JsonPropertyName("EndUserIp")]
        public string? EndUserIp { get; set; }
    }

    public class SrdvCallbackPassengerDto
    {
        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("FirstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("LastName")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("TicketNumber")]
        public string? TicketNumber { get; set; }

        [JsonPropertyName("ReturnTicketNumber")]
        public string? ReturnTicketNumber { get; set; }
    }

    public class SrdvBookingCallbackResponseDto
    {
        [JsonPropertyName("Error")]
        public SrdvCallbackErrorDto Error { get; set; } = new();
    }

    public class SrdvCallbackErrorDto
    {
        [JsonPropertyName("ErrorCode")]
        public string ErrorCode { get; set; } = string.Empty;

        [JsonPropertyName("ErrorMessage")]
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
