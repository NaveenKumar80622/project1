namespace PickNBook.Api.Models.Payments
{
    public class CreateCashfreeOrderRequest
    {
        public decimal OrderAmount { get; set; }

        public string OrderCurrency { get; set; } = "INR";

        public string CustomerId { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public string CustomerEmail { get; set; } = string.Empty;

        public string CustomerPhone { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;

        public string NotifyUrl { get; set; } = string.Empty;

        // =============================
        // NEW: Payment context fields (optional for backward compat)
        // =============================

        /// <summary>"Bus", "Hotel", or "Flight". Required for server-side price validation.</summary>
        public string? BookingType { get; set; }

        /// <summary>Coupon code to apply. Server will validate and compute discount.</summary>
        public string? CouponCode { get; set; }

        /// <summary>Promotion ID to apply (Bus/Flight promotions).</summary>
        public int? PromotionId { get; set; }

        /// <summary>Featured offer ID to apply.</summary>
        public int? SelectedFeaturedOfferId { get; set; }

        /// <summary>
        /// Full serialized booking request DTO (CreateBusBookingRequestDto, HotelBookRequestDto, etc.).
        /// When provided, server computes FinalPayableAmount and ignores OrderAmount.
        /// </summary>
        public string? BookingPayloadJson { get; set; }
    }
}
