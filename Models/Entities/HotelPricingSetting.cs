using System;

namespace PickNBook.Api.Models
{
    /// <summary>
    /// Represents the pricing configuration for hotels, including markup, convenience fee, and service GST.
    /// </summary>
    public class HotelPricingSetting
    {
        public int Id { get; set; }
        
        public string MarkupType { get; set; } = "Percentage"; // "Flat" or "Percentage"
        public decimal MarkupValue { get; set; }
        

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public string? UpdatedBy { get; set; }
    }
}
