using Microsoft.AspNetCore.Http;
using System;
using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs;

public class TestimonialResponseDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string? ImageUrl { get; set; }
    public long? CategoryId { get; set; }
}

public class UpsertTestimonialRequestDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Designation { get; set; } = string.Empty;

    [Required]
    [Range(1, 5)]
    public int Rating { get; set; }

    [Required]
    public string Comment { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "Active"; // Active, Inactive

    public IFormFile? Image { get; set; }
    
    public long? CategoryId { get; set; }
}

public class TestimonialCategoryResponseDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
}

public class UpsertTestimonialCategoryRequestDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "Active"; // Active, Inactive
}
