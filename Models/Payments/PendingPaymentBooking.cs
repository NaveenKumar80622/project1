namespace PickNBook.Api.Models.Payments
{
    /// <summary>
    /// Stores the pending booking context between Cashfree order creation and payment completion.
    /// Contains enough data (BookingPayloadJson + PricingSnapshotJson) for Phase 2 to
    /// execute the actual SRDV booking after payment success without re-calling pricing engines.
    /// </summary>
    public class PendingPaymentBooking
    {
        public int Id { get; set; }

        /// <summary>FK to Payment table.</summary>
        public int PaymentId { get; set; }

        /// <summary>"Bus", "Hotel", or "Flight".</summary>
        public string BookingType { get; set; } = string.Empty;

        /// <summary>PickNBook user ID.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Payment amount (should match Payment.FinalPayableAmount).</summary>
        public decimal Amount { get; set; }

        /// <summary>Currency code.</summary>
        public string Currency { get; set; } = "INR";

        /// <summary>
        /// Full serialized booking request DTO.
        /// Bus: CreateBusBookingRequestDto
        /// Hotel: HotelBookRequestDto
        /// Flight: FlightTicketLCCProxyRequestDto or HoldGDSRequestDto
        /// </summary>
        public string BookingPayloadJson { get; set; } = string.Empty;

        /// <summary>
        /// Serialized pricing engine result.
        /// Bus: BusPricingPreviewResponseDto
        /// Hotel: { BasePrice, Markup, GstAmount, CouponDiscount, FinalTotal }
        /// Flight: FlightPricingBreakdownDto + SSR amount
        /// </summary>
        public string? PricingSnapshotJson { get; set; }

        /// <summary>"Pending", "Completed", "Expired", "Cancelled".</summary>
        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>TTL — typically 30 minutes from creation.</summary>
        public DateTime ExpiresAt { get; set; }
    }
}
