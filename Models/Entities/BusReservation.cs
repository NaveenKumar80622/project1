namespace PickNBook.Api.Models
{
    public class BusReservation
    {
            public int Id { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public string Pnr { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public int BusBookingId { get; set; }
        public BusBooking? BusBooking { get; set; }
        public string PassengerName { get; set; } = string.Empty;
        public string PassengerPhone { get; set; } = string.Empty;
        public string? PassengerEmail { get; set; }
        public int SeatsBooked { get; set; }
        public decimal TotalPriceInr { get; set; }
        public decimal CustomerFareInr { get; set; }
        public decimal NetFareInr { get; set; }
        public decimal DiscountAmountInr { get; set; }
        public decimal ConvenienceFeeInr { get; set; }
        public string? CouponCode { get; set; }
        public string Status { get; set; } = "Booked";
        public DateTime BookedAtUtc { get; set; }
        public DateTime? CancelledAtUtc { get; set; }
        public string? CancellationReason { get; set; }
        public decimal? CancellationChargeInr { get; set; }
        public decimal? RefundAmountInr { get; set; }
        public decimal BaseFareInr { get; set; }

        public decimal MarkupAmountInr { get; set; }

        //public decimal MarkupPercent { get; set; }

        public decimal TaxableFareInr { get; set; }

        public decimal GstPercent { get; set; }

        public decimal GstAmountInr { get; set; }
        public int? AppliedPromotionId { get; set; }

        public string? AppliedPromotionCode { get; set; }

        public string? AppliedPromotionType { get; set; }

        public int? AppliedFeaturedOfferId { get; set; }

        public string? AppliedFeaturedOfferTitle { get; set; }

        public decimal FeaturedOfferDiscountAmount { get; set; }

        public int? AutoPromotionId { get; set; }

        public string? AutoPromotionCode { get; set; }

        public string? DiscountSource { get; set; }
        public decimal AutoDiscountAmountInr { get; set; }

        public decimal CouponDiscountAmountInr { get; set; }

        // SRDV Booking Response Fields
        public string? SrdvBookingId { get; set; }
        public string? SrdvTicketNo { get; set; }
        public string? SrdvBookingResponseJson { get; set; }
        public string? CancellationPolicyJson { get; set; }

        // Specific SRDV Selected Points
        public string? BoardingPointName { get; set; }
        public DateTime? BoardingPointTime { get; set; }
        public string? DroppingPointName { get; set; }
        public DateTime? DroppingPointTime { get; set; }

        // State Machine & Financial Correlation Fields
        public string? FinancialStatus { get; set; }
        public long? ProviderCancelId { get; set; }
        public string? SupplierCancelId { get; set; }
    }

    public static class BusBookingStatus
    {
        public const string Pending = "PENDING";
        public const string Blocked = "BLOCKED";
        public const string BookingInProgress = "BOOKING_IN_PROGRESS";
        public const string Success = "SUCCESS";
        public const string Booked = "Booked"; // Legacy alias
        public const string Failed = "FAILED";
        public const string ManualCheckRequired = "MANUAL_CHECK_REQUIRED";
        public const string CancelInProcess = "CANCEL_IN_PROCESS";
        public const string Cancelled = "Cancelled";
        public const string PartiallyCancelled = "Partially Cancelled";

        public static bool IsConfirmed(string? status) =>
            string.Equals(status, Success, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, Booked, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Confirmed", StringComparison.OrdinalIgnoreCase);

        public static bool IsCancelled(string? status) =>
            string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase);
    }
}
