using Microsoft.EntityFrameworkCore;
using System;

namespace PickNBook.Api.Models;

[Index(nameof(Slug), IsUnique = true)]
public class TestimonialCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = "Active"; // Active, Inactive
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
