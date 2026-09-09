using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
public class AdminTestimonialsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorageService;

    public AdminTestimonialsController(AppDbContext context, IFileStorageService fileStorageService)
    {
        _context = context;
        _fileStorageService = fileStorageService;
    }

    private string GenerateSlug(string phrase)
    {
        string str = phrase.ToLower().Trim();
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
        str = Regex.Replace(str, @"\s+", " ").Trim();
        str = str.Substring(0, str.Length <= 45 ? str.Length : 45).Trim();
        str = Regex.Replace(str, @"\s", "-");
        return str;
    }

    private TestimonialResponseDto MapToDto(Testimonial t)
    {
        return new TestimonialResponseDto
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
        };
    }

    // =====================================
    // 1. UNIFIED ENDPOINT
    // =====================================

    [HttpGet("testimonials")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetTestimonialsCombined([FromQuery] string? status, [FromQuery] long? categoryId, [FromQuery] string? search)
    {
        var testimonialsQuery = _context.Testimonials.Include(t => t.Category).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            testimonialsQuery = testimonialsQuery.Where(t => t.Status.ToLower() == status.ToLower());
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            testimonialsQuery = testimonialsQuery.Where(t => t.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            testimonialsQuery = testimonialsQuery.Where(t => t.Name.ToLower().Contains(searchLower) || t.Comment.ToLower().Contains(searchLower));
        }

        var testimonials = await testimonialsQuery
            .OrderByDescending(t => t.Id)
            .ToListAsync();

        var categories = await _context.TestimonialCategories
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new TestimonialCategoryResponseDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                Slug = c.Slug,
                Status = c.Status,
                CreatedAt = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                UpdatedAt = c.UpdatedAt.HasValue ? DateTime.SpecifyKind(c.UpdatedAt.Value, DateTimeKind.Utc) : null
            })
            .ToListAsync();

        var total = await _context.Testimonials.CountAsync();
        var active = await _context.Testimonials.CountAsync(t => t.Status == "Active" || t.Status == "Approved" || t.Status == "Published");
        var pending = await _context.Testimonials.CountAsync(t => t.Status == "Pending Review");
        var draft = await _context.Testimonials.CountAsync(t => t.Status == "Draft");

        var response = new
        {
            success = true,
            message = "Testimonials and categories retrieved successfully",
            data = new CombinedTestimonialsResponseDto
            {
                Testimonials = testimonials.Select(MapToDto).ToList(),
                Categories = categories,
                Stats = new CombinedStatsDto
                {
                    Total = total,
                    Active = active,
                    Pending = pending,
                    Draft = draft
                }
            }
        };

        return Ok(response);
    }

    [HttpGet("testimonials/combined")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> GetTestimonialsCombinedAlias([FromQuery] string? status, [FromQuery] long? categoryId, [FromQuery] string? search)
    {
        return GetTestimonialsCombined(status, categoryId, search);
    }

    // =====================================
    // 2. DASHBOARD STATS
    // =====================================
    [HttpGet("testimonials/dashboard-stats")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetDashboardStats([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var testimonialsQuery = _context.Testimonials.AsNoTracking();

        var total = await testimonialsQuery.CountAsync();
        var active = await testimonialsQuery.CountAsync(t => t.Status == "Active" || t.Status == "Approved" || t.Status == "Published");
        var inactive = await testimonialsQuery.CountAsync(t => t.Status == "Inactive" || t.Status == "Draft" || t.Status == "Rejected");
        var avgRating = total > 0 ? await testimonialsQuery.AverageAsync(t => (double)t.Rating) : 0.0;
        var categoriesCount = await _context.TestimonialCategories.CountAsync();

        var stats = new TestimonialDashboardStatsDto
        {
            TotalTestimonials = total,
            ActiveTestimonials = active,
            InactiveTestimonials = inactive,
            AverageRating = Math.Round(avgRating, 1),
            TotalCategories = categoriesCount
        };

        return Ok(stats);
    }

    // =====================================
    // 3. TESTIMONIAL CATEGORIES CRUD
    // =====================================

    [HttpPost("testimonial-categories")]
    public async Task<IActionResult> CreateCategory([FromBody] UpsertTestimonialCategoryRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var slug = GenerateSlug(dto.Name);
        var baseSlug = slug;
        var slugCount = 1;

        while (await _context.TestimonialCategories.AnyAsync(c => c.Slug == slug))
        {
            slug = $"{baseSlug}-{slugCount++}";
        }

        var category = new TestimonialCategory
        {
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Slug = slug,
            Status = dto.Status,
            CreatedAt = DateTime.UtcNow
        };

        _context.TestimonialCategories.Add(category);
        await _context.SaveChangesAsync();

        var response = new TestimonialCategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            Slug = category.Slug,
            Status = category.Status,
            CreatedAt = DateTime.SpecifyKind(category.CreatedAt, DateTimeKind.Utc)
        };

        return CreatedAtAction(nameof(GetTestimonialsCombined), response);
    }

    [HttpPut("testimonial-categories/{id:long}")]
    public async Task<IActionResult> UpdateCategory(long id, [FromBody] UpsertTestimonialCategoryRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var category = await _context.TestimonialCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound(new { message = "Category not found." });

        if (category.Name.Trim().ToLower() != dto.Name.Trim().ToLower())
        {
            var slug = GenerateSlug(dto.Name);
            var baseSlug = slug;
            var slugCount = 1;

            while (await _context.TestimonialCategories.AnyAsync(c => c.Slug == slug && c.Id != id))
            {
                slug = $"{baseSlug}-{slugCount++}";
            }
            category.Slug = slug;
        }

        category.Name = dto.Name.Trim();
        category.Description = dto.Description?.Trim();
        category.Status = dto.Status;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var response = new TestimonialCategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            Slug = category.Slug,
            Status = category.Status,
            CreatedAt = DateTime.SpecifyKind(category.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(category.UpdatedAt.Value, DateTimeKind.Utc)
        };

        return Ok(response);
    }

    [HttpDelete("testimonial-categories/{id:long}")]
    public async Task<IActionResult> DeleteCategory(long id)
    {
        var category = await _context.TestimonialCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound(new { message = "Category not found." });

        _context.TestimonialCategories.Remove(category);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Category deleted successfully." });
    }

    // =====================================
    // 4. TESTIMONIALS CRUD & STATUS
    // =====================================

    [HttpPost("testimonials")]
    [Consumes("multipart/form-data", "application/json")]
    public async Task<IActionResult> CreateTestimonial([FromForm] UpsertTestimonialRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        string? savedPath = null;
        string? fileName = null;
        if (dto.Image != null)
        {
            savedPath = await _fileStorageService.SaveFileAsync(dto.Image, "uploads/testimonials");
            fileName = dto.Image.FileName;
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
            Role = string.IsNullOrWhiteSpace(dto.Role) ? "Traveler" : dto.Role.Trim(),
            Location = dto.Location?.Trim(),
            Rating = dto.Rating,
            Comment = dto.Comment.Trim(),
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "Draft" : dto.Status,
            ImageUrl = savedPath,
            ImageFileName = fileName,
            ImageStatus = savedPath != null ? "uploaded" : "none",
            DisplayOrder = dto.DisplayOrder,
            Featured = dto.Featured,
            CategoryId = dto.CategoryId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Testimonials.Add(testimonial);
        await _context.SaveChangesAsync();

        testimonial = await _context.Testimonials.Include(t => t.Category).FirstAsync(t => t.Id == testimonial.Id);

        return StatusCode(201, new
        {
            success = true,
            message = "Testimonial created successfully",
            data = MapToDto(testimonial)
        });
    }

    [HttpPut("testimonials/{id:long}")]
    [Consumes("multipart/form-data", "application/json")]
    public async Task<IActionResult> UpdateTestimonial(long id, [FromForm] UpsertTestimonialRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var testimonial = await _context.Testimonials.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id);
        if (testimonial == null) return NotFound(new { message = "Testimonial not found." });

        if (dto.CategoryId.HasValue && dto.CategoryId.Value != testimonial.CategoryId)
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
            testimonial.ImageFileName = dto.Image.FileName;
            testimonial.ImageStatus = "uploaded";

            if (!string.IsNullOrEmpty(oldImage))
            {
                _fileStorageService.DeleteFile(oldImage);
            }
        }

        testimonial.Name = dto.Name.Trim();
        testimonial.Role = string.IsNullOrWhiteSpace(dto.Role) ? "Traveler" : dto.Role.Trim();
        testimonial.Location = dto.Location?.Trim();
        testimonial.Rating = dto.Rating;
        testimonial.Comment = dto.Comment.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Status)) testimonial.Status = dto.Status;
        testimonial.DisplayOrder = dto.DisplayOrder;
        testimonial.Featured = dto.Featured;
        testimonial.CategoryId = dto.CategoryId;
        testimonial.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        
        testimonial = await _context.Testimonials.Include(t => t.Category).FirstAsync(t => t.Id == testimonial.Id);

        return Ok(new
        {
            success = true,
            message = "Testimonial updated successfully",
            data = MapToDto(testimonial)
        });
    }

    [HttpPatch("testimonials/{id:long}/status")]
    public async Task<IActionResult> UpdateTestimonialStatus(long id, [FromBody] UpdateTestimonialStatusDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var testimonial = await _context.Testimonials.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id);
        if (testimonial == null) return NotFound(new { message = "Testimonial not found." });

        testimonial.Status = dto.Status.Trim();
        testimonial.Featured = dto.Featured;
        testimonial.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Status updated successfully",
            data = MapToDto(testimonial)
        });
    }

    [HttpDelete("testimonials/{id:long}")]
    public async Task<IActionResult> DeleteTestimonial(long id)
    {
        var testimonial = await _context.Testimonials.FirstOrDefaultAsync(t => t.Id == id);
        if (testimonial == null) return NotFound(new { message = "Testimonial not found." });

        if (!string.IsNullOrEmpty(testimonial.ImageUrl))
        {
            _fileStorageService.DeleteFile(testimonial.ImageUrl);
        }

        _context.Testimonials.Remove(testimonial);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Testimonial deleted successfully",
            data = new { id = testimonial.Id }
        });
    }

    // =====================================
    // 5. GLOBAL SETTINGS
    // =====================================

    [HttpGet("testimonials/settings")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _context.TestimonialSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new TestimonialSetting
            {
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.TestimonialSettings.Add(settings);
            await _context.SaveChangesAsync();
        }

        var dto = new TestimonialSettingsDto
        {
            ApprovalRequired = settings.ApprovalRequired,
            AllowUserSubmission = settings.AllowUserSubmission,
            AllowRating = settings.AllowRating,
            AllowCustomerImage = settings.AllowCustomerImage,
            DefaultCategory = settings.DefaultCategory,
            FeaturedLimit = settings.FeaturedLimit,
            DisplayOrderMode = settings.DisplayOrderMode,
            AutoPublish = settings.AutoPublish,
            CreatedAt = DateTime.SpecifyKind(settings.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(settings.UpdatedAt, DateTimeKind.Utc)
        };

        return Ok(new
        {
            success = true,
            message = "Testimonial settings retrieved successfully",
            data = dto
        });
    }

    [HttpPut("testimonials/settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] TestimonialSettingsDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var settings = await _context.TestimonialSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new TestimonialSetting
            {
                CreatedAt = DateTime.UtcNow
            };
            _context.TestimonialSettings.Add(settings);
        }

        settings.ApprovalRequired = dto.ApprovalRequired;
        settings.AllowUserSubmission = dto.AllowUserSubmission;
        settings.AllowRating = dto.AllowRating;
        settings.AllowCustomerImage = dto.AllowCustomerImage;
        settings.DefaultCategory = dto.DefaultCategory?.Trim() ?? "Hotel Stay";
        settings.FeaturedLimit = dto.FeaturedLimit;
        settings.DisplayOrderMode = dto.DisplayOrderMode;
        settings.AutoPublish = dto.AutoPublish;
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        
        var responseDto = new TestimonialSettingsDto
        {
            ApprovalRequired = settings.ApprovalRequired,
            AllowUserSubmission = settings.AllowUserSubmission,
            AllowRating = settings.AllowRating,
            AllowCustomerImage = settings.AllowCustomerImage,
            DefaultCategory = settings.DefaultCategory,
            FeaturedLimit = settings.FeaturedLimit,
            DisplayOrderMode = settings.DisplayOrderMode,
            AutoPublish = settings.AutoPublish,
            CreatedAt = DateTime.SpecifyKind(settings.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(settings.UpdatedAt, DateTimeKind.Utc)
        };

        return Ok(new
        {
            success = true,
            message = "Global Testimonial Settings saved successfully",
            data = responseDto
        });
    }
}
