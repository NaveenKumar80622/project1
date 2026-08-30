using System;

namespace PickNBook.Api.Models;

public class Testimonial
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Active"; // Active, Inactive
    public string? ImageUrl { get; set; }

    // Relationship to category
    public long? CategoryId { get; set; }
    public TestimonialCategory? Category { get; set; }
}
