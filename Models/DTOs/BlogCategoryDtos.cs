using Microsoft.AspNetCore.Http;

namespace PickNBook.Api.Models.DTOs;

public class UpsertBlogCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Status { get; set; } = "Active";
    public string? MetaTitle { get; set; }
    public string? MetaKeyword { get; set; }
    public string? MetaDescription { get; set; }
    public IFormFile? Image { get; set; }
}

public class UpsertBlogSubCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Status { get; set; } = "Active";
    public string? MetaTitle { get; set; }
    public string? MetaKeyword { get; set; }
    public string? MetaDescription { get; set; }
    public IFormFile? Image { get; set; }
}
