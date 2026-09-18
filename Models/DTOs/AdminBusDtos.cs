namespace PickNBook.Api.Models.DTOs;



public class BusCouponRequestDto
{
    public string Type { get; set; } = "bus"; // "bus", "hotel", "flight"
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
    public int MaxUsagePerUser { get; set; } = 1;
    public bool IsExclusive { get; set; } = true;
    public bool IsAutoApply { get; set; } = false;
    public bool IsFirstTimeUserOnly { get; set; } = false;
    public int Priority { get; set; }
    public string Status { get; set; } = "Active";
    public string? Remark { get; set; }
    public decimal MinBookingAmount { get; set; }
    public string? ImageUrl { get; set; }
}

public class CreateBusCouponConditionDto
{
    public string ConditionType { get; set; } = "DayOfWeek";
    public string ConditionOperator { get; set; } = "Equals";
    public string Value1 { get; set; } = string.Empty;
    public string? Value2 { get; set; }
}

public class UpdateBusCouponConditionDto
{
    public string ConditionType { get; set; } = "DayOfWeek";
    public string ConditionOperator { get; set; } = "Equals";
    public string Value1 { get; set; } = string.Empty;
    public string? Value2 { get; set; }
}


public class BusConvenienceFeeRequestDto
{
    public decimal FeeInr { get; set; }
    public string Status { get; set; } = "Active";
    public string UpdatedBy { get; set; } = string.Empty;
}

public class BusMarkupRequestDto
{
    public string SeatType { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string MarkupType { get; set; } = string.Empty; // Percentage / Fixed
    public string Status { get; set; } = "Active";
    public string UpdatedBy { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class BusGstRequestDto
{
    public string GstCategory { get; set; } = string.Empty;
    public decimal GstPercent { get; set; }
    public string Status { get; set; } = "Active";
    public string UpdatedBy { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class AdminBusCancellationRequestDto
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public string TicketNo { get; set; } = string.Empty;
    public string Pnr { get; set; } = string.Empty;
    public DateTime? RequestDateUtc { get; set; }
    public string Segment { get; set; } = string.Empty;
    public DateOnly? JourneyDate { get; set; }
    public string? BusOperator { get; set; }
    public string? BusType { get; set; }
    public string Customer { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string Status { get; set; } = "Pending";
    public decimal CustomerRefundAmountInr { get; set; }
    public decimal AdminRefundAmountInr { get; set; }
    public string? Remark { get; set; }
    public AdminBusCancellationDetailsDto Details { get; set; } = new();
}

public class AdminBusCancellationDetailsDto
{
    public string CancellationStatus { get; set; } = "Pending";
    public string CustomerRefundStatus { get; set; } = "Pending";
    public string AdminRefundStatus { get; set; } = "Pending";
    public decimal CustomerRefundAmountInr { get; set; }
    public decimal CustomerCancellationChargeInr { get; set; }
    public decimal CustomerServiceChargeInr { get; set; }
    public decimal AdminRefundAmountInr { get; set; }
    public decimal AdminCancellationChargeInr { get; set; }
    public decimal AdminServiceChargeInr { get; set; }
    public string? SupplierRemark { get; set; }
    public string? CustomerRemark { get; set; }
    public string? AdminRemark { get; set; }
}

public class BusCancellationRequestUpdateDto
{
    public string CancellationStatus { get; set; } = "Pending";
    public string CustomerRefundStatus { get; set; } = "Pending";
    public string AdminRefundStatus { get; set; } = "Pending";
    public decimal CustomerRefundAmountInr { get; set; }
    public decimal CustomerCancellationChargeInr { get; set; }
    public decimal CustomerServiceChargeInr { get; set; }
    public decimal AdminRefundAmountInr { get; set; }
    public decimal AdminCancellationChargeInr { get; set; }
    public decimal AdminServiceChargeInr { get; set; }
    public string? SupplierRemark { get; set; }
    public string? CustomerRemark { get; set; }
    public string? AdminRemark { get; set; }
}