namespace PickNBook.Api.Models.DTOs
{

    public class SeatPreviewDto
    {
        public string SeatCode { get; set; } = string.Empty;
        public decimal BaseFare { get; set; }
        public string SeatType { get; set; } = string.Empty;
        public decimal ExternalGst { get; set; }
    }

    public class BusSeatPriceBreakdownDto
    {
        public string SeatCode { get; set; } = string.Empty;

        public string SeatType { get; set; } = string.Empty;

        public decimal BaseFare { get; set; }

        public decimal MarkupAmount { get; set; }

        public decimal FareBeforeTax { get; set; }
    }

    /// <summary>
    /// Lightweight request DTO for the pricing-preview endpoint.
    /// Does NOT require passenger names/ages — only seat pricing data from the seat layout response.
    /// </summary>
    public class BusPricingPreviewRequestDto
    {
        public string? TraceId { get; set; }
        public string? CouponCode { get; set; }
        public int? PromotionId { get; set; }
        public int? SelectedFeaturedOfferId { get; set; }
        
        // Bus Details (required for Promotion Validation)
        public string FromCity { get; set; } = string.Empty;
        public string ToCity { get; set; } = string.Empty;
        public string DepartureTime { get; set; } = string.Empty;
        public string? OperatorName { get; set; }
        public string? BusType { get; set; }
        public decimal TotalFare { get; set; }
        
        public List<SeatPreviewDto> Seats { get; set; } = [];
    }

    public class BusPricingPreviewResponseDto
    {
        public decimal SubtotalBeforeCoupon { get; set; }
        public decimal CouponAmount { get; set; }
        public decimal TaxableFare { get; set; }

        public decimal GstPercent { get; set; }

        public decimal GstAmount { get; set; }

        public decimal ConvenienceFee { get; set; }

        public decimal GrandTotal { get; set; }

        public List<BusSeatPriceBreakdownDto> Seats { get; set; } = [];
        public string? AppliedPromotionCode { get; set; }
        public string? AutoPromotionCode { get; set; }

        public string? AppliedPromotionTitle { get; set; }

        public string? DiscountSource { get; set; }
        public string? DiscountLabel { get; set; }

        public bool CouponAllowed { get; set; } = true;
        public string? AppliedPromotionType { get; set; }
        public decimal AutoDiscountAmount { get; set; }

        public decimal CouponDiscountAmount { get; set; }
        public decimal ManualDiscountAmount { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal FinalAmount { get; set; }
    }
}
