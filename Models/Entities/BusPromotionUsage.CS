using System;

namespace PickNBook.Api.Models
{
    public class BusPromotionUsage
    {
        public long Id { get; set; }

        public int BusPromotionId { get; set; }
        public BusPromotion? Promotion { get; set; }

        public int? FeaturedOfferId { get; set; }

        public int BusReservationId { get; set; }
        public BusReservation? BusReservation { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string PromotionCode { get; set; } = string.Empty;

        public string PromotionType { get; set; } = string.Empty;

        public decimal DiscountAmountInr { get; set; }

        public decimal BookingTotalInr { get; set; }

        public string BookingStatus { get; set; } = "Booked";

        public DateTime UsedAtUtc { get; set; } = DateTime.UtcNow;
    }
}