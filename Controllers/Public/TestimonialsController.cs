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
            .Where(t => t.Status == "Active")
            .OrderBy(t => t.Id)
            .Select(t => new TestimonialResponseDto
            {
                Id = t.Id,
                Name = t.Name,
                Designation = t.Designation,
                Rating = t.Rating,
                Comment = t.Comment,
                Status = t.Status,
                ImageUrl = t.ImageUrl != null ? t.ImageUrl.TrimStart('/') : null,
                CategoryId = t.CategoryId
            })
            .ToListAsync();

        return Ok(list);
    }

    // =====================================
    // ADMIN TESTIMONIAL ENDPOINTS
    // =====================================

    // 1.2. Admin: Get All Testimonials
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAllTestimonials()
    {
        var list = await _context.Testimonials
            .OrderByDescending(t => t.Id)
            .Select(t => new TestimonialResponseDto
            {
                Id = t.Id,
                Name = t.Name,
                Designation = t.Designation,
                Rating = t.Rating,
                Comment = t.Comment,
                Status = t.Status,
                ImageUrl = t.ImageUrl != null ? t.ImageUrl.TrimStart('/') : null,
                CategoryId = t.CategoryId
            })
            .ToListAsync();

        return Ok(list);
    }

    // 1.3. Admin: Create Testimonial
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPost("admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateTestimonial([FromForm] UpsertTestimonialRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        string? savedPath = null;
        if (dto.Image != null)
        {
            savedPath = await _fileStorageService.SaveFileAsync(dto.Image, "uploads/testimonials");
        }

        if (dto.CategoryId.HasValue)
        {
            var categoryExists = await _context.TestimonialCategories.AnyAsync(c => c.Id == dto.CategoryId.Value);
            if (!categoryExists)
            {
                return BadRequest(new { message = $"Category with ID {dto.CategoryId.Value} does not exist." });
            }
        }

        var testimonial = new Testimonial
        {
            Name = dto.Name.Trim(),
            Designation = dto.Designation.Trim(),
            Rating = dto.Rating,
            Comment = dto.Comment.Trim(),
            Status = dto.Status,
            ImageUrl = savedPath,
            CategoryId = dto.CategoryId
        };

        _context.Testimonials.Add(testimonial);
        await _context.SaveChangesAsync();

        var response = new TestimonialResponseDto
        {
            Id = testimonial.Id,
            Name = testimonial.Name,
            Designation = testimonial.Designation,
            Rating = testimonial.Rating,
            Comment = testimonial.Comment,
            Status = testimonial.Status,
            ImageUrl = testimonial.ImageUrl != null ? testimonial.ImageUrl.TrimStart('/') : null,
            CategoryId = testimonial.CategoryId
        };

        return CreatedAtAction(nameof(GetActiveTestimonials), response);
    }

    // 1.4. Admin: Update Testimonial
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPut("admin/{id:long}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateTestimonial(long id, [FromForm] UpsertTestimonialRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var testimonial = await _context.Testimonials.FirstOrDefaultAsync(t => t.Id == id);
        if (testimonial == null)
        {
            return NotFound(new { message = "Testimonial not found." });
        }

        if (dto.CategoryId.HasValue)
        {
            var categoryExists = await _context.TestimonialCategories.AnyAsync(c => c.Id == dto.CategoryId.Value);
            if (!categoryExists)
            {
                return BadRequest(new { message = $"Category with ID {dto.CategoryId.Value} does not exist." });
            }
        }

        if (dto.Image != null)
        {
            var oldImage = testimonial.ImageUrl;
            testimonial.ImageUrl = await _fileStorageService.SaveFileAsync(dto.Image, "uploads/testimonials");
            if (!string.IsNullOrEmpty(oldImage))
            {
                _fileStorageService.DeleteFile(oldImage);
            }
        }

        testimonial.Name = dto.Name.Trim();
        testimonial.Designation = dto.Designation.Trim();
        testimonial.Rating = dto.Rating;
        testimonial.Comment = dto.Comment.Trim();
        testimonial.Status = dto.Status;
        testimonial.CategoryId = dto.CategoryId;

        await _context.SaveChangesAsync();

        var response = new TestimonialResponseDto
        {
            Id = testimonial.Id,
            Name = testimonial.Name,
            Designation = testimonial.Designation,
            Rating = testimonial.Rating,
            Comment = testimonial.Comment,
            Status = testimonial.Status,
            ImageUrl = testimonial.ImageUrl != null ? testimonial.ImageUrl.TrimStart('/') : null,
            CategoryId = testimonial.CategoryId
        };

        return Ok(response);
    }

    // 1.5. Admin: Delete Testimonial
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpDelete("admin/{id:long}")]
    public async Task<IActionResult> DeleteTestimonial(long id)
    {
        var testimonial = await _context.Testimonials.FirstOrDefaultAsync(t => t.Id == id);
        if (testimonial == null)
        {
            return NotFound(new { message = "Testimonial not found." });
        }

        if (!string.IsNullOrEmpty(testimonial.ImageUrl))
        {
            _fileStorageService.DeleteFile(testimonial.ImageUrl);
        }

        _context.Testimonials.Remove(testimonial);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Testimonial deleted successfully." });
    }

    // 1.6. Admin: Toggle Testimonial Status
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPost("admin/{id:long}/toggle-status")]
    public async Task<IActionResult> ToggleTestimonialStatus(long id)
    {
        var testimonial = await _context.Testimonials.FirstOrDefaultAsync(t => t.Id == id);
        if (testimonial == null)
        {
            return NotFound(new { message = "Testimonial not found." });
        }

        testimonial.Status = testimonial.Status == "Active" ? "Inactive" : "Active";
        await _context.SaveChangesAsync();

        return Ok(new { id = testimonial.Id, status = testimonial.Status });
    }

    // =====================================
    // RECOMMENDED TESTIMONIAL CATEGORY ENDPOINTS
    // =====================================

    // 2.1. Admin: Get Category List
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpGet("admin/categories")]
    public async Task<IActionResult> GetCategories()
    {
        var list = await _context.TestimonialCategories
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new TestimonialCategoryResponseDto
            {
                Id = c.Id,
                Name = c.Name,
                Status = c.Status,
                CreatedAt = DateTime.SpecifyKind(c.CreatedAtUtc, DateTimeKind.Utc)
            })
            .ToListAsync();

        return Ok(list);
    }

    // 2.2. Admin: Create Category
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPost("admin/categories")]
    public async Task<IActionResult> CreateCategory([FromBody] UpsertTestimonialCategoryRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var category = new TestimonialCategory
        {
            Name = dto.Name.Trim(),
            Status = dto.Status,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.TestimonialCategories.Add(category);
        await _context.SaveChangesAsync();

        var response = new TestimonialCategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            Status = category.Status,
            CreatedAt = DateTime.SpecifyKind(category.CreatedAtUtc, DateTimeKind.Utc)
        };

        return CreatedAtAction(nameof(GetCategories), response);
    }

    // 2.3. Admin: Update Category
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPut("admin/categories/{id:long}")]
    public async Task<IActionResult> UpdateCategory(long id, [FromBody] UpsertTestimonialCategoryRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var category = await _context.TestimonialCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound(new { message = "Category not found." });
        }

        category.Name = dto.Name.Trim();
        category.Status = dto.Status;

        await _context.SaveChangesAsync();

        var response = new TestimonialCategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            Status = category.Status,
            CreatedAt = DateTime.SpecifyKind(category.CreatedAtUtc, DateTimeKind.Utc)
        };

        return Ok(response);
    }

    // 2.4. Admin: Delete Category
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpDelete("admin/categories/{id:long}")]
    public async Task<IActionResult> DeleteCategory(long id)
    {
        var category = await _context.TestimonialCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound(new { message = "Category not found." });
        }

        _context.TestimonialCategories.Remove(category);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Category deleted successfully." });
    }

    // 2.5. Admin: Toggle Category Status
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPost("admin/categories/{id:long}/toggle-status")]
    public async Task<IActionResult> ToggleCategoryStatus(long id)
    {
        var category = await _context.TestimonialCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound(new { message = "Category not found." });
        }

        category.Status = category.Status == "Active" ? "Inactive" : "Active";
        await _context.SaveChangesAsync();

        return Ok(new { id = category.Id, status = category.Status });
    }
}
