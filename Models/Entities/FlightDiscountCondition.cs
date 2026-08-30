using System.Text.Json.Serialization;

namespace PickNBook.Api.Models
{
    public class FlightDiscountCondition
    {
        public int Id { get; set; }

        public int FlightDiscountId { get; set; }

        [JsonIgnore]
        public FlightDiscount? Discount { get; set; }

        public string ConditionType { get; set; } = string.Empty;

        public string ConditionOperator { get; set; } = "Equals";

        public string Value1 { get; set; } = string.Empty;

        public string? Value2 { get; set; }
    }
}
