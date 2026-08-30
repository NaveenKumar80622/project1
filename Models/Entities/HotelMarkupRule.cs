using System;

namespace PickNBook.Api.Models
{
    /// <summary>
    /// Represents a markup rule applied to hotel pricing based on city, hotel, user type, or global settings.
    /// </summary>
    public class HotelMarkupRule
    {
        public int Id { get; set; }
        public string RuleName { get; set; } = "Hotel Markup Rule";
        public string CityCode { get; set; } = "*"; // e.g. "DEL", "BOM", or "*" for all cities
        public string HotelCode { get; set; } = "*"; // e.g. "1000045" or "*" for all hotels
        public string UserType { get; set; } = "All"; // "All", "B2C", "B2B"
        public string MarkupType { get; set; } = "Flat"; // "Flat" or "Percentage"
        public decimal MarkupValue { get; set; }
        public int Priority { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
