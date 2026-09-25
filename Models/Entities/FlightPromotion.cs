using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models
{
    /// <summary>
    /// Represents a flight promotion rule for automatic discounts.
    /// </summary>
    public class FlightPromotion
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public FlightDiscountType DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal? MaximumDiscount { get; set; }
        public decimal MinimumFare { get; set; }
        public int Priority { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsAutoApply { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<FlightPromotionCondition> Conditions { get; set; } = new List<FlightPromotionCondition>();
    }
}
