namespace PickNBook.Api.Models.Payments
{
    /// <summary>
    /// Represents a PickNBook payment record that tracks a Cashfree payment lifecycle.
    /// Stores the full pricing breakdown (original amount, markup, discount, final amount)
    /// and all Cashfree identifiers needed for reconciliation.
    /// </summary>
    public class Payment
    {
        public int Id { get; set; }

        /// <summary>PickNBook internal payment reference (e.g., "PAY-20260826143000-1234").</summary>
        public string PaymentReference { get; set; } = string.Empty;

        /// <summary>Cashfree's order_id returned from create-order API.</summary>
        public string CashfreeOrderId { get; set; } = string.Empty;

        /// <summary>Cashfree's cf_order_id returned from create-order API.</summary>
        public string? CashfreeCfOrderId { get; set; }

        /// <summary>Cashfree's payment_session_id for frontend checkout.</summary>
        public string? PaymentSessionId { get; set; }

        /// <summary>PickNBook user ID (links to Users table).</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>"Bus", "Hotel", or "Flight" — see BookingType constants.</summary>
        public string BookingType { get; set; } = string.Empty;

        /// <summary>Reservation ID after SRDV booking (Phase 2). Null until booking is created.</summary>
        public int? BookingId { get; set; }

        // =============================
        // Pricing Breakdown
        // =============================

        /// <summary>Provider/SRDV validated price before PickNBook markup/discount.</summary>
        public decimal OriginalAmount { get; set; }

        /// <summary>PickNBook markup amount added on top of provider price.</summary>
        public decimal MarkupAmount { get; set; }

        /// <summary>PickNBook convenience fee.</summary>
        public decimal ConvenienceFee { get; set; }

        /// <summary>Total discount applied (coupon + promotion + auto-discount).</summary>
        public decimal DiscountAmount { get; set; }

        /// <summary>Applied coupon code, if any.</summary>
        public string? CouponCode { get; set; }

        /// <summary>Applied featured offer title, if any.</summary>
        public string? OfferCode { get; set; }

        /// <summary>
        /// Exact amount sent to Cashfree. 
        /// Formula: OriginalAmount + MarkupAmount + ConvenienceFee - DiscountAmount
        /// This is the single source of truth for reconciliation.
        /// </summary>
        public decimal FinalPayableAmount { get; set; }

        /// <summary>Currency code, default "INR".</summary>
        public string Currency { get; set; } = "INR";

        // =============================
        // Payment Status
        // =============================

        /// <summary>Current payment status — see PaymentStatus constants.</summary>
        public string Status { get; set; } = PaymentStatus.Created;

        /// <summary>Cashfree's cf_payment_id from webhook/verification.</summary>
        public string? CashfreePaymentId { get; set; }

        /// <summary>Status of the final SRDV booking fulfillment (Phase 2).</summary>
        public string FulfillmentStatus { get; set; } = "Pending";

        /// <summary>The PickNBook reservation ID (BusReservationId, HotelReservationId, etc.) once fulfilled.</summary>
        public int? BookingReferenceId { get; set; }

        /// <summary>Payment method used (e.g., "upi", "card", "netbanking").</summary>
        public string? PaymentMethod { get; set; }

        // =============================
        // Refund Status
        // =============================
        public string RefundStatus { get; set; } = "NotRequired";
        public string? RefundId { get; set; }
        public int RefundAttempts { get; set; }
        public string? RefundReason { get; set; }
        public string? LastError { get; set; }

        // =============================
        // Timestamps
        // =============================

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Timestamp when payment was successfully completed.</summary>
        public DateTime? PaidAt { get; set; }

        /// <summary>Failure reason from Cashfree or amount mismatch description.</summary>
        public string? FailureReason { get; set; }

        /// <summary>Timestamp when the Cashfree webhook was received for this payment.</summary>
        public DateTime? WebhookReceivedAt { get; set; }
    }
}
