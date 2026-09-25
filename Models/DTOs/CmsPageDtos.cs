using Microsoft.AspNetCore.Http;

namespace PickNBook.Api.Models.DTOs;

public class UpsertCmsPageRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Module { get; set; } = "All";
    public string Status { get; set; } = "Active";
    public string? MetaTitle { get; set; }
    public string? MetaKeyword { get; set; }
    public string? MetaDescription { get; set; }
    public string Description { get; set; } = string.Empty;
    public IFormFile? Image { get; set; }
    public IFormFile? Banner { get; set; }
}
