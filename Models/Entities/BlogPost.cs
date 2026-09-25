namespace PickNBook.Api.Models;

public class BlogPost
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string LongDescription { get; set; } = string.Empty;
    public string? SubTitle { get; set; }
    public bool IsFeatured { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaKeyword { get; set; }
    public string? MetaDescription { get; set; }
    public string? ImageUrl { get; set; }
    public string? OgImageUrl { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime? PublishedAtUtc { get; set; }
    public int? AddedByUserId { get; set; }
    public string AddedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
