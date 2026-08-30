namespace PickNBook.Api.Models.DTOs
{
    public class FlightPricingBreakdownDto
    {
        public decimal SupplierBaseFare { get; set; }
        public decimal SupplierTaxAmount { get; set; }
        public decimal SupplierTotalFare { get; set; }
        public decimal MarkupAmount { get; set; }
        public decimal PromotionDiscount { get; set; }
        public decimal CouponDiscount { get; set; }
        public decimal ConvenienceFee { get; set; }
        public decimal FinalAmount { get; set; }
        public int? PromotionId { get; set; }
        public string? PromotionName { get; set; }
        public int? CouponId { get; set; }
        public string? CouponCode { get; set; }
    }
}
