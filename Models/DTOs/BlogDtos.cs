namespace PickNBook.Api.Models.DTOs;

public class UpsertBlogRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string LongDescription { get; set; } = string.Empty;
    public string? SubTitle { get; set; }
    public bool IsFeatured { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaKeyword { get; set; }
    public string? MetaDescription { get; set; }
    public bool IsPublished { get; set; } = true;
    public IFormFile? Image { get; set; }
    public IFormFile? OgImage { get; set; }
}
