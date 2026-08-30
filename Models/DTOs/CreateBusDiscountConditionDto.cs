namespace PickNBook.Api.Models.DTOs;

public class CreateBusDiscountConditionDto
{
    public string ConditionType { get; set; }
        = string.Empty;

    public string ConditionOperator { get; set; }
        = "Equals";

    public string Value1 { get; set; }
        = string.Empty;

    public string? Value2 { get; set; }
}