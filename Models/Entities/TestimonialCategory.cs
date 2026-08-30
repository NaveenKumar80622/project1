using System;

namespace PickNBook.Api.Models;

public class TestimonialCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Active"; // Active, Inactive
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
