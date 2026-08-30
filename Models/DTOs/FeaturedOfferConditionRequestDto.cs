namespace PickNBook.Api.Models.DTOs
{
    public class FeaturedOfferConditionRequestDto
    {
        public string ConditionType { get; set; } = string.Empty;
        public string Value1 { get; set; } = string.Empty;
        public string? Value2 { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
