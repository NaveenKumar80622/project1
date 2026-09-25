namespace PickNBook.Api.Models.DTOs;

public class UnifiedCouponDto
{
    public int Id { get; set; }
    public string ServiceType { get; set; } = string.Empty; // "bus", "hotel", "flight"
    public string PromotionCategory { get; set; } = "Coupon"; // "Coupon" or "Offer"
    public string CouponCode { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string CouponType { get; set; } = "Percentage"; // "Percentage" or "Fixed"
    public decimal Value { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal MinBookingAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public bool IsAutoApply { get; set; }
    public bool IsExclusive { get; set; }
    public bool IsFirstTimeUserOnly { get; set; }
    public int Priority { get; set; }
    public string? ImageUrl { get; set; }
    public string? Remark { get; set; }
}

public class ValidateUnifiedCouponRequestDto
{
    public string ServiceType { get; set; } = string.Empty; // "bus", "hotel", "flight"
    public string CouponCode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? UserId { get; set; }
    public DateTime? TravelDate { get; set; }
}

public class ValidateUnifiedCouponResponseDto
{
    public bool IsValid { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalTotal { get; set; }
    public string Message { get; set; } = string.Empty;
}
