namespace PickNBook.Api.Models.Payments
{
    /// <summary>
    /// Response returned by Cashfree CreateOrder API supporting Hybrid, Full Wallet, and Cashfree-only checkout.
    /// </summary>
    public class CreateOrderResponseDto
    {
        public decimal TotalAmount { get; set; }
        public decimal WalletUsedAmount { get; set; }
        public decimal GatewayPaidAmount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public bool IsWalletFullyPaid { get; set; }
        public string? CashfreeOrderId { get; set; }
        public string? PaymentSessionId { get; set; }
        public string? CfOrderId { get; set; }
        public string? OrderStatus { get; set; }
        public string? PaymentReference { get; set; }
    }
}
