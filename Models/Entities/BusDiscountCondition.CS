namespace PickNBook.Api.Models;

public class BusDiscountCondition
{
    public int Id { get; set; }

    public int BusDiscountId { get; set; }

    public BusDiscount? Discount { get; set; }

    public string ConditionType { get; set; }
        = string.Empty;

    public string ConditionOperator { get; set; }
        = "Equals";

    public string Value1 { get; set; } = string.Empty;

    public string? Value2 { get; set; }
}