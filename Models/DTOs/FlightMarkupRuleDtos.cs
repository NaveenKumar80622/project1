using System;

namespace PickNBook.Api.Models.DTOs
{
    public class CreateFlightMarkupRuleDto
    {
        public string AirlineCode { get; set; } = string.Empty;
        public TripType TripType { get; set; }
        public string CabinClass { get; set; } = "*";
        public FlightMarkupType MarkupType { get; set; }
        public decimal MarkupValue { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
    }

    public class UpdateFlightMarkupRuleDto
    {
        public string AirlineCode { get; set; } = string.Empty;
        public TripType TripType { get; set; }
        public string CabinClass { get; set; } = "*";
        public FlightMarkupType MarkupType { get; set; }
        public decimal MarkupValue { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
    }

    public class FlightMarkupRuleResponseDto
    {
        public int Id { get; set; }
        public string AirlineCode { get; set; } = string.Empty;
        public string TripType { get; set; } = string.Empty;
        public string CabinClass { get; set; } = string.Empty;
        public string MarkupType { get; set; } = string.Empty;
        public decimal MarkupValue { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
