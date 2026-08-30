using System;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models
{
    public class FeaturedOfferUsage
    {
        public int Id { get; set; }

        public int FeaturedOfferId { get; set; }
        
        [JsonIgnore]
        public FeaturedOffer? FeaturedOffer { get; set; }

        public string UserId { get; set; } = string.Empty;

        public int BusReservationId { get; set; }
        
        [JsonIgnore]
        public BusReservation? BusReservation { get; set; }

        public decimal DiscountAmount { get; set; }

        public DateTime UsedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
