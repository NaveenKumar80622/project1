using System;

namespace PickNBook.Api.Models;

public class HotelCoupon
{
    public int Id { get; set; }
    public string CouponCode { get; set; } = string.Empty;
    public string CouponType { get; set; } = "Percentage"; // Percentage, Flat
    public decimal Value { get; set; }
    public decimal MinBookingAmount { get; set; } = 0;
    public decimal MaxDiscountAmount { get; set; } = 0; // Caps the discount if percentage type
    public DateOnly StartDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public int UseLimit { get; set; } = 0; // 0 means unlimited
    public int UsedCount { get; set; } = 0;
    public int MaxUsagePerUser { get; set; } = 1;
    public string Status { get; set; } = "Active"; // Active, Inactive
    public bool IsFirstTimeUserOnly { get; set; } = false;
    public DateTime EntryDateUtc { get; set; } = DateTime.UtcNow;
    public string? Remark { get; set; }
}
