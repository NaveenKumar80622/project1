using System;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models
{
    public class FeaturedOfferCondition
    {
        public int Id { get; set; }

        public int FeaturedOfferId { get; set; }

        [JsonIgnore]
        public FeaturedOffer FeaturedOffer { get; set; } = null!;

        public string ConditionType { get; set; } = string.Empty;
        // SourceCity
        // DestinationCity
        // BusType
        // TravelDate

        public string Value1 { get; set; } = string.Empty;

        public string? Value2 { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
