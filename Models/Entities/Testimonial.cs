using System;

namespace PickNBook.Api.Models;

public class Testimonial
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "Traveler";
    public string? Location { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft"; // Active, Inactive, Draft, Pending Review, Approved, Published
    public string? ImageUrl { get; set; }
    public string? ImageFileName { get; set; }
    public string ImageStatus { get; set; } = "uploaded"; // uploaded, pending, none
    public int DisplayOrder { get; set; } = 1;
    public bool Featured { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Relationship to category
    public long? CategoryId { get; set; }
    public TestimonialCategory? Category { get; set; }
}
