namespace PickNBook.Api.Models.DTOs;

public class FlightDiscountRequestDto
{
    public decimal Value { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string UpdatedBy { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class FlightRemarkRequestDto
{
    public string SourceType { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
}

public class FlightCouponRequestDto
{
    public decimal Value { get; set; }
    public string CouponType { get; set; } = string.Empty;
    public string CouponCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public int UseLimit { get; set; }
    public string Status { get; set; } = "Active";
    public string? Remark { get; set; }
}

public class FlightConvenienceFeeRequestDto
{
    public string AmountType { get; set; } = "Fixed";
    public decimal Value { get; set; }
    public string Status { get; set; } = "Active";
    public string UpdatedBy { get; set; } = string.Empty;
}

public class PendingAirlineRequestDto
{
    public string AirlineCode { get; set; } = string.Empty;
    public string FareType { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class AirlineRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Status { get; set; } = "Active";
}

public class AirlineWebcheckLinkRequestDto
{
    public string Airline { get; set; } = string.Empty;
    public string AirlineCode { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class PopularDestinationRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string SubTitle { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Placement { get; set; } = "Main";
    public string? Url { get; set; }
    public string Status { get; set; } = "Active";
}

public class FlightCancellationRequestDto
{
    public int FlightReservationId { get; set; }
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

public class FlightAmendmentRequestDto
{
    public int FlightReservationId { get; set; }
    public string AmendmentStatus { get; set; } = "Pending";
    public string? SupplierRemark { get; set; }
    public string? CustomerRemark { get; set; }
    public string? AdminRemark { get; set; }
}
