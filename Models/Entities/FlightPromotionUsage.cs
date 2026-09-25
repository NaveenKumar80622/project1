using System;

namespace PickNBook.Api.Models
{
    /// <summary>
    /// Tracks the usage of flight promotions on reservations.
    /// </summary>
    public class FlightPromotionUsage
    {
        public int Id { get; set; }
        public int FlightPromotionId { get; set; }
        public FlightPromotion? FlightPromotion { get; set; }

        public int ReservationId { get; set; }
        public FlightReservation? FlightReservation { get; set; }

        public string UserId { get; set; } = string.Empty;
        public decimal DiscountAmount { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
