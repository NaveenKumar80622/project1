using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models.DTOs
{
    public class FlightPromotionConditionDto
    {
        public int Id { get; set; }
        public FlightConditionType ConditionType { get; set; }
        public string Operator { get; set; } = "Equals";
        public string Value { get; set; } = string.Empty;
    }

    public class CreateFlightPromotionDto
    {
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
        public List<FlightPromotionConditionDto> Conditions { get; set; } = new();
    }

    public class UpdateFlightPromotionDto
    {
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
        public List<FlightPromotionConditionDto> Conditions { get; set; } = new();
    }

    public class FlightPromotionResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DiscountType { get; set; } = string.Empty; // string representation
        public decimal DiscountValue { get; set; }
        public decimal? MaximumDiscount { get; set; }
        public decimal MinimumFare { get; set; }
        public int Priority { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsAutoApply { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<FlightPromotionConditionDto> Conditions { get; set; } = new();
    }
}
