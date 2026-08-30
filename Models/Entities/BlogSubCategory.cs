using System;

namespace PickNBook.Api.Models;

public class BlogSubCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // References the parent category name string
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Status { get; set; } = "Active"; // Active, Inactive
    public string? MetaTitle { get; set; }
    public string? MetaKeyword { get; set; }
    public string? MetaDescription { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
