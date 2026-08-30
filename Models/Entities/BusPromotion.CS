namespace PickNBook.Api.Models;

public class BusPromotion
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string PromotionType { get; set; } = string.Empty;

    public string DiscountType { get; set; } = string.Empty;

    public decimal DiscountValue { get; set; }

    public decimal? MaxDiscountAmount { get; set; }

    public decimal MinBookingAmount { get; set; }

    public bool IsActive { get; set; }
    public bool IsExclusive { get; set; }
    public bool IsAutoApply { get; set; }
    public bool IsFirstTimeUserOnly { get; set; } = false;

    public int Priority { get; set; }

    public int? MaxUsage { get; set; }

    public int UsedCount { get; set; }

    public int MaxUsagePerUser { get; set; }

    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }

    public string? SourceType { get; set; }

    public string? SourceKey { get; set; }
    public int? SourceId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<BusPromotionCondition> Conditions
    { get; set; } = new List<BusPromotionCondition>();
}