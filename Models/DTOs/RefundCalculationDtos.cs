namespace PickNBook.Api.Models.DTOs
{
    public class RefundCalculationInput
    {
        public decimal OriginalCustomerPaid { get; set; }
        public decimal SupplierAmount { get; set; }
        public decimal MarkupAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal ConvenienceFee { get; set; }
        public decimal SupplierCancellationCharge { get; set; }
        public decimal SupplierRefundAmount { get; set; }
    }

    public class RefundCalculationResult
    {
        public decimal SupplierRefundAmount { get; set; }
        public decimal SupplierCancellationCharge { get; set; }
        public decimal MarkupRefunded { get; set; }
        public decimal MarkupRetained { get; set; }
        public decimal CouponForfeited { get; set; }
        public decimal CouponRefunded { get; set; }
        public decimal FeeRefunded { get; set; }
        public decimal FeeRetained { get; set; }
        public decimal FinalCustomerRefundAmount { get; set; }
    }
}
