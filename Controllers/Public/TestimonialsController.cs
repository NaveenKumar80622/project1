using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

[Route("api/testimonials")]
public class TestimonialsController : BaseApiController
{
    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorageService;

    public TestimonialsController(AppDbContext context, IFileStorageService fileStorageService)
    {
        _context = context;
        _fileStorageService = fileStorageService;
    }

    // =====================================
    // PUBLIC ENDPOINTS
    // =====================================

    // 1.1. Public: Get Active Testimonials
    [HttpGet("active")]
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetActiveTestimonials()
    {
        var list = await _context.Testimonials
            .Include(t => t.Category)
            .Where(t => t.Status == "Active" || t.Status == "Approved" || t.Status == "Published")
            .OrderBy(t => t.DisplayOrder).ThenByDescending(t => t.Id)
            .Select(t => new TestimonialResponseDto
            {
                Id = t.Id,
                Name = t.Name,
                Role = t.Role,
                Location = t.Location,
                Rating = t.Rating,
                Comment = t.Comment,
                Status = t.Status,
                ImageUrl = t.ImageUrl != null ? t.ImageUrl.TrimStart('/') : null,
                ImageFileName = t.ImageFileName,
                ImageStatus = t.ImageStatus,
                DisplayOrder = t.DisplayOrder,
                Featured = t.Featured,
                CreatedAt = DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc),
                UpdatedAt = t.UpdatedAt.HasValue ? DateTime.SpecifyKind(t.UpdatedAt.Value, DateTimeKind.Utc) : null,
                CategoryId = t.CategoryId,
                Category = t.Category != null ? new TestimonialCategoryResponseDto
                {
                    Id = t.Category.Id,
                    Name = t.Category.Name,
                    Description = t.Category.Description,
                    Slug = t.Category.Slug,
                    Status = t.Category.Status,
                    CreatedAt = DateTime.SpecifyKind(t.Category.CreatedAt, DateTimeKind.Utc),
                    UpdatedAt = t.Category.UpdatedAt.HasValue ? DateTime.SpecifyKind(t.Category.UpdatedAt.Value, DateTimeKind.Utc) : null
                } : null
            })
            .ToListAsync();

        return Ok(list);
    }

    // 1.2. Public: Submit Testimonial
    [HttpPost]
    [AllowAnonymous]
    [Consumes("multipart/form-data", "application/json")]
    public async Task<IActionResult> SubmitTestimonial([FromForm] UpsertTestimonialRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var settings = await _context.TestimonialSettings.FirstOrDefaultAsync();
        if (settings != null && !settings.AllowUserSubmission)
        {
            return StatusCode(403, new { success = false, message = "Public testimonial submissions are currently disabled." });
        }

        if (settings != null && !settings.AllowRating)
        {
            dto.Rating = 5; // Force a default if rating is disabled but someone submitted anyway
        }

        string? savedPath = null;
        string? fileName = null;
        if (dto.Image != null)
        {
            if (settings != null && !settings.AllowCustomerImage)
            {
                return BadRequest(new { success = false, message = "Customer image uploads are not allowed." });
            }

            savedPath = await _fileStorageService.SaveFileAsync(dto.Image, "uploads/testimonials");
            fileName = dto.Image.FileName;
        }

        var defaultCategory = settings?.DefaultCategory ?? "Hotel Stay";
        long? categoryId = dto.CategoryId;
        
        if (!categoryId.HasValue)
        {
            var cat = await _context.TestimonialCategories.FirstOrDefaultAsync(c => c.Name == defaultCategory);
            if (cat != null)
            {
                categoryId = cat.Id;
            }
        }

        string finalStatus = "Pending Review";
        if (settings != null)
        {
            if (settings.AutoPublish)
            {
                finalStatus = "Active";
            }
            else if (!settings.ApprovalRequired)
            {
                finalStatus = "Active";
            }
        }

        var testimonial = new Testimonial
        {
            Name = dto.Name.Trim(),
            Role = string.IsNullOrWhiteSpace(dto.Role) ? "Traveler" : dto.Role.Trim(),
            Location = dto.Location?.Trim(),
            Rating = dto.Rating,
            Comment = dto.Comment.Trim(),
            Status = finalStatus,
            ImageUrl = savedPath,
            ImageFileName = fileName,
            ImageStatus = savedPath != null ? "uploaded" : "none",
            DisplayOrder = 999, // New submissions at the end by default
            Featured = false,   // Never featured by default
            CategoryId = categoryId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Testimonials.Add(testimonial);
        await _context.SaveChangesAsync();

        return StatusCode(201, new
        {
            success = true,
            message = finalStatus == "Active" ? "Testimonial submitted and published successfully." : "Testimonial submitted and is pending review.",
            data = new { id = testimonial.Id, status = finalStatus }
        });
    }
}
