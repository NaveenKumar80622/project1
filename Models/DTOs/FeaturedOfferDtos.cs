using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models.DTOs
{
    public class FeaturedOfferDto
    {
        public int OfferId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string BookingType { get; set; } = string.Empty;
        public string DiscountType { get; set; } = string.Empty;
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscountAmount { get; set; }
        public decimal MinBookingAmount { get; set; }
        public DateTime? StartDateUtc { get; set; }
        public DateTime? EndDateUtc { get; set; }
        public int? MaxUsage { get; set; }
        public int UsedCount { get; set; }
        public decimal PreviewFinalPrice { get; set; }

        // Homepage conditions for search prefilling
        public List<FeaturedOfferConditionDto> Conditions { get; set; } = new List<FeaturedOfferConditionDto>();
    }

    public class FeaturedOfferConditionDto
    {
        public string ConditionType { get; set; } = string.Empty;
        public string Value1 { get; set; } = string.Empty;
        public string? Value2 { get; set; }
    }

    public class ExclusiveOfferSubscriptionRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? WhatsAppNumber { get; set; }
    }

    public class ExclusiveOfferSubscriptionResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool AlreadySubscribed { get; set; }
        public bool EmailSent { get; set; }
        public bool WhatsAppSent { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? WhatsAppNumber { get; set; }
        public int OffersIncluded { get; set; }
        public DateTime SubscribedAtUtc { get; set; }
    }
}
