using System;

namespace PickNBook.Api.Models
{
    /// <summary>
    /// Represents a markup rule applied to flights based on airline and trip type.
    /// </summary>
    public class FlightMarkupRule
    {
        public int Id { get; set; }
        public string AirlineCode { get; set; } = string.Empty; // e.g., "AI", "6E" or "*" for all
        public TripType TripType { get; set; }
        public string CabinClass { get; set; } = "*"; // e.g., "Economy", "Business", "*" for all
        public FlightMarkupType MarkupType { get; set; }
        public decimal MarkupValue { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
