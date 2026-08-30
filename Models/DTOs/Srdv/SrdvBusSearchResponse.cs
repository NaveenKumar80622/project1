using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs.Srdv
{
    public class SrdvBusSearchResponse
    {
        [JsonPropertyName("Error")]
        public SrdvError Error { get; set; }

        [JsonPropertyName("TraceId")]
        [JsonConverter(typeof(StringOrIntConverter))]
        public int TraceId { get; set; }

        [JsonPropertyName("Result")]
        public List<SrdvBusOfferResponse> Result { get; set; }
    }

    public class SrdvError
    {
        [JsonPropertyName("ErrorCode")]
        [JsonConverter(typeof(StringOrIntConverter))]
        public int ErrorCode { get; set; }

        [JsonPropertyName("ErrorMessage")]
        public string ErrorMessage { get; set; }
    }

    public class SrdvBusOfferResponse
    {
        [JsonPropertyName("TravelsName")]
        public string TravelsName { get; set; }

        [JsonPropertyName("OperatorId")]
        public string OperatorId { get; set; }

        [JsonPropertyName("BusType")]
        public string BusType { get; set; }

        [JsonPropertyName("DepartureTime")]
        public string DepartureTime { get; set; }

        [JsonPropertyName("ArrivalTime")]
        public string ArrivalTime { get; set; }

        [JsonPropertyName("DisplayFare")]
        [JsonConverter(typeof(StringOrDecimalConverter))]
        public decimal DisplayFare { get; set; }

        [JsonPropertyName("AvailableSeats")]
        [JsonConverter(typeof(StringOrIntConverter))]
        public int AvailableSeats { get; set; }
        
        [JsonPropertyName("BoardingPoints")]
        public List<SrdvBoardingPointResponse> BoardingPoints { get; set; }
        
        [JsonPropertyName("DroppingPoints")]
        public List<SrdvBoardingPointResponse> DroppingPoints { get; set; }

        [JsonPropertyName("ResultIndex")]
        public string ResultIndex { get; set; }

        [JsonPropertyName("CancellationPolicy")]
        public string CancellationPolicy { get; set; }
        
        [JsonPropertyName("CancellationPolicies")]
        public List<SrdvCancellationPolicyResponse> CancellationPolicies { get; set; }

        [JsonPropertyName("PartialCancellationAllowed")]
        [JsonConverter(typeof(StringOrBoolConverter))]
        public bool PartialCancellationAllowed { get; set; }

        [JsonPropertyName("MTicketAllowed")]
        [JsonConverter(typeof(StringOrBoolConverter))]
        public bool MTicketAllowed { get; set; }

        [JsonPropertyName("IdProofRequired")]
        [JsonConverter(typeof(StringOrBoolConverter))]
        public bool IdProofRequired { get; set; }
    }

    public class SrdvBoardingPointResponse
    {
        [JsonPropertyName("CityPointIndex")]
        [JsonConverter(typeof(StringOrIntConverter))]
        public int CityPointIndex { get; set; }

        [JsonPropertyName("CityPointLocation")]
        public string CityPointLocation { get; set; }

        [JsonPropertyName("CityPointName")]
        public string CityPointName { get; set; }

        [JsonPropertyName("CityPointTime")]
        public string CityPointTime { get; set; }
    }

    public class SrdvCancellationPolicyResponse
    {
        [JsonPropertyName("CancellationCharge")]
        [JsonConverter(typeof(StringOrDecimalConverter))]
        public decimal CancellationCharge { get; set; }

        [JsonPropertyName("CancellationChargeType")]
        [JsonConverter(typeof(StringOrIntConverter))]
        public int CancellationChargeType { get; set; }

        [JsonPropertyName("PolicyString")]
        public string PolicyString { get; set; }

        [JsonPropertyName("TimeBeforeDeparture")]
        public string TimeBeforeDeparture { get; set; }

        [JsonPropertyName("FromDate")]
        public string FromDate { get; set; }

        [JsonPropertyName("ToDate")]
        public string ToDate { get; set; }
    }
}
