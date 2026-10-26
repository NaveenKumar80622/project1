using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class SrdvV8ErrorDto
    {
        [JsonPropertyName("ErrorCode")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("ErrorMessage")]
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class SrdvV8ErrorResponseDto
    {
        [JsonPropertyName("Error")]
        public SrdvV8ErrorDto Error { get; set; } = new();
    }

    public class SrdvV8TicketCRInfoDto
    {
        [JsonPropertyName("ChangeRequestId")]
        public long ChangeRequestId { get; set; }

        [JsonPropertyName("TicketId")]
        public string TicketId { get; set; } = string.Empty;

        [JsonPropertyName("Status")]
        public int Status { get; set; } // 1: Pending, 4: Rejected

        [JsonPropertyName("Remarks")]
        public string Remarks { get; set; } = string.Empty;

        [JsonPropertyName("ChangeRequestStatus")]
        public int ChangeRequestStatus { get; set; }
    }

    public class SrdvV8SendChangeResponseDto
    {
        [JsonPropertyName("Error")]
        public SrdvV8ErrorDto Error { get; set; } = new() { ErrorCode = 0, ErrorMessage = "" };

        [JsonPropertyName("ResponseStatus")]
        public int ResponseStatus { get; set; } = 1;

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; } = "MixAPI";

        [JsonPropertyName("SrdvIndex")]
        public int SrdvIndex { get; set; } = 1;

        [JsonPropertyName("TraceId")]
        public long TraceId { get; set; }

        [JsonPropertyName("TicketCRInfo")]
        public List<SrdvV8TicketCRInfoDto> TicketCRInfo { get; set; } = new();
    }

    public class SrdvV8GetCancelStatusResponseDto
    {
        [JsonPropertyName("Error")]
        public SrdvV8ErrorDto Error { get; set; } = new() { ErrorCode = 0, ErrorMessage = "" };

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; } = "MixAPI";

        [JsonPropertyName("ChangeRequestId")]
        public long ChangeRequestId { get; set; }

        [JsonPropertyName("BookingId")]
        public long BookingId { get; set; }

        [JsonPropertyName("PNR")]
        public string PNR { get; set; } = string.Empty;

        [JsonPropertyName("TicketId")]
        public string TicketId { get; set; } = string.Empty;

        [JsonPropertyName("PaxName")]
        public string PaxName { get; set; } = string.Empty;

        [JsonPropertyName("Sectors")]
        public string Sectors { get; set; } = string.Empty;

        [JsonPropertyName("RequestType")]
        public string RequestType { get; set; } = "1";

        [JsonPropertyName("CancellationType")]
        public string CancellationType { get; set; } = "1";

        [JsonPropertyName("Status")]
        public int Status { get; set; } // 1: Pending, 3: Processed, 4: Rejected

        [JsonPropertyName("ChangeRequestStatus")]
        public int ChangeRequestStatus { get; set; }

        [JsonPropertyName("CancellationStatus")]
        public string CancellationStatus { get; set; } = "PENDING"; // PENDING, IN_PROCESS, CANCELLED, FAILED, MANUAL_CHECK_REQUIRED

        [JsonPropertyName("RefundStatus")]
        public string RefundStatus { get; set; } = "NOT_STARTED"; // NOT_STARTED, MANUAL_PENDING, REFUNDED, REJECTED

        [JsonPropertyName("ExpectedRefundAmount")]
        public decimal? ExpectedRefundAmount { get; set; }

        [JsonPropertyName("RefundAmount")]
        public decimal? RefundAmount { get; set; }

        [JsonPropertyName("CancellationCharge")]
        public decimal? CancellationCharge { get; set; }

        [JsonPropertyName("Remarks")]
        public string Remarks { get; set; } = string.Empty;

        [JsonPropertyName("RequestedOn")]
        public string RequestedOn { get; set; } = string.Empty;

        [JsonPropertyName("CompletedOn")]
        public string CompletedOn { get; set; } = string.Empty;

        [JsonPropertyName("IsSettled")]
        public bool IsSettled { get; set; }
    }

    public class SrdvV8GetCancellationChargesResponseDto
    {
        [JsonPropertyName("Error")]
        public SrdvV8ErrorDto Error { get; set; } = new() { ErrorCode = 0, ErrorMessage = "" };

        [JsonPropertyName("SrdvType")]
        public string SrdvType { get; set; } = "MixAPI";

        [JsonPropertyName("SrdvIndex")]
        public int SrdvIndex { get; set; } = 1;

        [JsonPropertyName("TraceId")]
        public long TraceId { get; set; }

        [JsonPropertyName("BookingId")]
        public long BookingId { get; set; }

        [JsonPropertyName("Result")]
        public SrdvV8CancellationQuoteResultDto Result { get; set; } = new();
    }

    public class SrdvV8CancellationQuoteResultDto
    {
        [JsonPropertyName("CancellationType")]
        public string CancellationType { get; set; } = "Cancellation Charge Per Pax";

        [JsonPropertyName("Currency")]
        public string Currency { get; set; } = "INR";

        [JsonPropertyName("Segments")]
        public List<SrdvV8SegmentQuoteDto> Segments { get; set; } = new();

        [JsonPropertyName("TotalCancellationCharge")]
        public decimal? TotalCancellationCharge { get; set; }

        [JsonPropertyName("TotalRefundAmount")]
        public decimal? TotalRefundAmount { get; set; }
    }

    public class SrdvV8SegmentQuoteDto
    {
        [JsonPropertyName("PNR")]
        public string PNR { get; set; } = string.Empty;

        [JsonPropertyName("Direction")]
        public string Direction { get; set; } = "OUTBOUND";

        [JsonPropertyName("FromAirport")]
        public string FromAirport { get; set; } = string.Empty;

        [JsonPropertyName("ToAirport")]
        public string ToAirport { get; set; } = string.Empty;

        [JsonPropertyName("DepartDate")]
        public string DepartDate { get; set; } = string.Empty;

        [JsonPropertyName("Airlines")]
        public List<string> Airlines { get; set; } = new();

        [JsonPropertyName("FlightNumbers")]
        public List<string> FlightNumbers { get; set; } = new();

        [JsonPropertyName("CancellationCharge")]
        public decimal? CancellationCharge { get; set; }

        [JsonPropertyName("RefundAmount")]
        public decimal? RefundAmount { get; set; }

        [JsonPropertyName("Remarks")]
        public string Remarks { get; set; } = string.Empty;

        [JsonPropertyName("PaxBreakup")]
        public List<SrdvV8PaxBreakupDto> PaxBreakup { get; set; } = new();
    }

    public class SrdvV8PaxBreakupDto
    {
        [JsonPropertyName("PaxType")]
        public string PaxType { get; set; } = "ADULT";

        [JsonPropertyName("PaxCount")]
        public int PaxCount { get; set; }

        [JsonPropertyName("CancellationChargePerPax")]
        public decimal? CancellationChargePerPax { get; set; }

        [JsonPropertyName("RefundAmountPerPax")]
        public decimal? RefundAmountPerPax { get; set; }

        [JsonPropertyName("TotalFarePerPax")]
        public decimal? TotalFarePerPax { get; set; }

        [JsonPropertyName("CancellationCharge")]
        public decimal? CancellationCharge { get; set; }

        [JsonPropertyName("RefundAmount")]
        public decimal? RefundAmount { get; set; }

        [JsonPropertyName("TotalFare")]
        public decimal? TotalFare { get; set; }
    }
}
