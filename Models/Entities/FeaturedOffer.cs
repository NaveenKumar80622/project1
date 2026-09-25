using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models
{
    public class FeaturedOffer
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string BookingType { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public string DiscountType { get; set; } = string.Empty;
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscountAmount { get; set; }
        public decimal MinBookingAmount { get; set; }
        public DateTime? StartDateUtc { get; set; }
        public DateTime? EndDateUtc { get; set; }
        public int? MaxUsage { get; set; }
        public int UsedCount { get; set; }

        public ICollection<FeaturedOfferCondition> Conditions { get; set; } 
            = new List<FeaturedOfferCondition>();

        public ICollection<FeaturedOfferUsage> Usages { get; set; }
            = new List<FeaturedOfferUsage>();
    }
}
