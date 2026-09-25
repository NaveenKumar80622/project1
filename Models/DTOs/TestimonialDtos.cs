using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs;

public class TestimonialResponseDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string? ImageUrl { get; set; }
    public string? ImageFileName { get; set; }
    public string ImageStatus { get; set; } = "none";
    public int DisplayOrder { get; set; }
    public bool Featured { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? CategoryId { get; set; }
    public TestimonialCategoryResponseDto? Category { get; set; }
}

public class UpsertTestimonialRequestDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = "Traveler";
    
    public string? Location { get; set; }

    [Required]
    [Range(1, 5)]
    public int Rating { get; set; }

    [Required]
    public string Comment { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "Draft";

    public int DisplayOrder { get; set; } = 1;

    public bool Featured { get; set; } = false;

    public IFormFile? Image { get; set; }
    
    public long? CategoryId { get; set; }
}

public class TestimonialCategoryResponseDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UpsertTestimonialCategoryRequestDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public string Status { get; set; } = "Active";
}

public class CombinedTestimonialsResponseDto
{
    public List<TestimonialResponseDto> Testimonials { get; set; } = new();
    public List<TestimonialCategoryResponseDto> Categories { get; set; } = new();
    public CombinedStatsDto Stats { get; set; } = new();
}

public class CombinedStatsDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Pending { get; set; }
    public int Draft { get; set; }
}

public class TestimonialDashboardStatsDto
{
    public int TotalTestimonials { get; set; }
    public int ActiveTestimonials { get; set; }
    public int InactiveTestimonials { get; set; }
    public double AverageRating { get; set; }
    public int TotalCategories { get; set; }
}

public class TestimonialSettingsDto
{
    public bool ApprovalRequired { get; set; } = true;
    public bool AllowUserSubmission { get; set; } = true;
    public bool AllowRating { get; set; } = true;
    public bool AllowCustomerImage { get; set; } = true;
    public string DefaultCategory { get; set; } = "Hotel Stay";
    
    [Range(1, 50)]
    public int FeaturedLimit { get; set; } = 6;
    
    [RegularExpression("^(Manual Order|Latest First)$", ErrorMessage = "DisplayOrderMode must be 'Manual Order' or 'Latest First'")]
    public string DisplayOrderMode { get; set; } = "Manual Order";
    
    public bool AutoPublish { get; set; } = false;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UpdateTestimonialStatusDto
{
    [Required]
    public string Status { get; set; } = "Active";
    public bool Featured { get; set; }
}
