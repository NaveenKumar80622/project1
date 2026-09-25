using System;

namespace PickNBook.Api.Models.DTOs
{
    public class CreateFlightConvenienceFeeRuleDto
    {
        public TripType TripType { get; set; }
        public string FeeType { get; set; } = "Flat"; // Flat or Percentage
        public decimal FeeValue { get; set; }
        public bool IsActive { get; set; }
    }

    public class UpdateFlightConvenienceFeeRuleDto
    {
        public TripType TripType { get; set; }
        public string FeeType { get; set; } = "Flat"; // Flat or Percentage
        public decimal FeeValue { get; set; }
        public bool IsActive { get; set; }
    }

    public class FlightConvenienceFeeRuleResponseDto
    {
        public int Id { get; set; }
        public string TripType { get; set; } = string.Empty;
        public string FeeType { get; set; } = "Flat";
        public decimal FeeValue { get; set; }
        public bool IsActive { get; set; }
    }
}
