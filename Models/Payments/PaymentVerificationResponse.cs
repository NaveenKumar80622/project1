namespace PickNBook.Api.Models.Payments
{
    /// <summary>
    /// PickNBook's response DTO for the payment verification endpoint.
    /// Returns internal payment state after reconciling with Cashfree's API.
    /// </summary>
    public class PaymentVerificationResponse
    {
        public string PaymentReference { get; set; } = string.Empty;
        public string CashfreeOrderId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string BookingType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string? PaymentMethod { get; set; }
        public DateTime? PaidAt { get; set; }
        public string? FailureReason { get; set; }
    }
}
