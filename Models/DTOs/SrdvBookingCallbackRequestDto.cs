using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class SrdvBookingCallbackRequestDto
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
        public string GdsPNR { get; set; } = string.Empty;

        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("Remark")]
        public string Remark { get; set; } = string.Empty;

        [JsonPropertyName("Passengers")]
        public List<SrdvCallbackPassengerDto> Passengers { get; set; } = new();
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
