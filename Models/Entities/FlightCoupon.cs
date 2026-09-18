using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models
{
    public class FlightCoupon
    {
        public int Id { get; set; }
        public string PromotionCategory { get; set; } = "Coupon"; // "Coupon" or "Offer"
        public string? Title { get; set; }
        public string? Description { get; set; }
        public decimal Value { get; set; }
        public string CouponType { get; set; } = string.Empty;
        public string CouponCode { get; set; } = string.Empty;
        public decimal? MaxDiscountAmount { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public int UseLimit { get; set; }
        public int UsedCount { get; set; }
        public string Status { get; set; } = "Active";
        public bool IsExclusive { get; set; } = true;
        public bool IsAutoApply { get; set; } = false;
        public bool IsFirstTimeUserOnly { get; set; } = false;
        public int Priority { get; set; } = 0;
        public int MaxUsagePerUser { get; set; } = 1;
        public decimal MinBookingAmount { get; set; } = 0;
        public DateTime EntryDateUtc { get; set; }
        public string? Remark { get; set; }
        public string? ImageUrl { get; set; }

        public ICollection<FlightCouponCondition> Conditions { get; set; } = new List<FlightCouponCondition>();
    }
}

