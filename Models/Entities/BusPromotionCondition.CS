namespace PickNBook.Api.Models;

public class BusPromotionCondition
{
    public int Id { get; set; }

    public int BusPromotionId { get; set; }

    public BusPromotion? Promotion { get; set; }

    public string ConditionType { get; set; }
        = string.Empty;

    public string ConditionOperator { get; set; }
        = "Equals";

    public string Value1 { get; set; } = string.Empty;

    public string? Value2 { get; set; }
}