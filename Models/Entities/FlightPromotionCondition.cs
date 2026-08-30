using System;

namespace PickNBook.Api.Models
{
    /// <summary>
    /// Represents a specific condition that a flight must satisfy to qualify for a promotion.
    /// </summary>
    public class FlightPromotionCondition
    {
        public int Id { get; set; }
        public int FlightPromotionId { get; set; }
        public FlightPromotion? FlightPromotion { get; set; }

        public FlightConditionType ConditionType { get; set; }
        public string Operator { get; set; } = "Equals"; // Equals, GreaterThanOrEqual, LessThanOrEqual, Contains
        public string Value { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
