using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

public class BlogCategoriesController : BaseApiController
{
    private const long MaxImageBytes = 4 * 1024 * 1024; // 4MB

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public BlogCategoriesController(AppDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    // GET: api/blogcategories
    [HttpGet]
    [OutputCache(Duration = 3600, Tags = new[] { "categories-tag" })]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _context.BlogCategories
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync();

        return Ok(categories);
    }

    // POST: api/blogcategories/admin
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPost("admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateCategory([FromForm] UpsertBlogCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Category name is required.");
        }

        if (request.Image != null)
        {
            if (request.Image.Length > MaxImageBytes)
                return BadRequest("Image size must be less than or equal to 4MB.");
            if (!IsSupportedImage(request.Image.FileName))
                return BadRequest("Unsupported image format. Use .jpg, .jpeg, .png, or .webp.");
        }

        var slugBase = BuildSlug(request.Name, request.Slug);
        var slug = await EnsureUniqueSlugAsync(slugBase, null);
        var imageUrl = await SaveImageAsync(request.Image, "blogs/categories");

        var category = new BlogCategory
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Status = request.Status,
            ImageUrl = imageUrl,
            MetaTitle = request.MetaTitle?.Trim(),
            MetaKeyword = request.MetaKeyword?.Trim(),
            MetaDescription = request.MetaDescription?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.BlogCategories.Add(category);
        await _context.SaveChangesAsync();

        var cacheStore = HttpContext?.RequestServices?.GetService<IOutputCacheStore>();
        if (cacheStore != null)
        {
            await cacheStore.EvictByTagAsync("categories-tag", default);
        }

        return Ok(category);
    }

    // PUT: api/blogcategories/admin/{id}
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPut("admin/{id:long}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateCategory(long id, [FromForm] UpsertBlogCategoryRequest request)
    {
        var category = await _context.BlogCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound("Category not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Category name is required.");
        }

        if (request.Image != null)
        {
            if (request.Image.Length > MaxImageBytes)
                return BadRequest("Image size must be less than or equal to 4MB.");
            if (!IsSupportedImage(request.Image.FileName))
                return BadRequest("Unsupported image format. Use .jpg, .jpeg, .png, or .webp.");

            var previousImage = category.ImageUrl;
            category.ImageUrl = await SaveImageAsync(request.Image, "blogs/categories");
            DeleteStaticFile(previousImage);
        }

        var slugBase = BuildSlug(request.Name, request.Slug);
        category.Slug = await EnsureUniqueSlugAsync(slugBase, id);
        category.Name = request.Name.Trim();
        category.Status = request.Status;
        category.MetaTitle = request.MetaTitle?.Trim();
        category.MetaKeyword = request.MetaKeyword?.Trim();
        category.MetaDescription = request.MetaDescription?.Trim();
        category.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var cacheStore = HttpContext?.RequestServices?.GetService<IOutputCacheStore>();
        if (cacheStore != null)
        {
            await cacheStore.EvictByTagAsync("categories-tag", default);
        }

        return Ok(category);
    }

    // PUT: api/blogcategories/admin/{id}/status
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPut("admin/{id:long}/status")]
    public async Task<IActionResult> ToggleStatus(long id)
    {
        var category = await _context.BlogCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound("Category not found.");
        }

        category.Status = category.Status == "Active" ? "Inactive" : "Active";
        category.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var cacheStore = HttpContext?.RequestServices?.GetService<IOutputCacheStore>();
        if (cacheStore != null)
        {
            await cacheStore.EvictByTagAsync("categories-tag", default);
        }

        return Ok(category);
    }

    // DELETE: api/blogcategories/admin/{id}
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpDelete("admin/{id:long}")]
    public async Task<IActionResult> DeleteCategory(long id)
    {
        var category = await _context.BlogCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound("Category not found.");
        }

        DeleteStaticFile(category.ImageUrl);
        _context.BlogCategories.Remove(category);
        await _context.SaveChangesAsync();

        var cacheStore = HttpContext?.RequestServices?.GetService<IOutputCacheStore>();
        if (cacheStore != null)
        {
            await cacheStore.EvictByTagAsync("categories-tag", default);
        }

        return Ok(new { success = true });
    }

    private static bool IsSupportedImage(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return AllowedImageExtensions.Contains(extension);
    }

    private static string BuildSlug(string name, string? providedSlug)
    {
        var source = string.IsNullOrWhiteSpace(providedSlug) ? name : providedSlug;
        var lower = source.Trim().ToLowerInvariant();

        lower = Regex.Replace(lower, @"[^a-z0-9\s-]", string.Empty);
        lower = Regex.Replace(lower, @"\s+", "-");
        lower = Regex.Replace(lower, @"-+", "-");
        lower = lower.Trim('-');

        if (string.IsNullOrWhiteSpace(lower))
        {
            lower = $"category-{Guid.NewGuid():N}".Substring(0, 13);
        }

        return lower;
    }

    private async Task<string> EnsureUniqueSlugAsync(string slugBase, long? ignoreId)
    {
        var slug = slugBase;
        var suffix = 2;

        while (await _context.BlogCategories.AnyAsync(x =>
                   x.Slug == slug &&
                   (!ignoreId.HasValue || x.Id != ignoreId.Value)))
        {
            slug = $"{slugBase}-{suffix}";
            suffix += 1;
        }

        return slug;
    }

    private async Task<string?> SaveImageAsync(IFormFile? file, string folderRelativePath)
    {
        if (file == null || file.Length <= 0)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid():N}{extension}";

        var webRootPath = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var targetFolder = Path.Combine(webRootPath, folderRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        var filePath = Path.Combine(targetFolder, fileName);
        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return $"/{folderRelativePath}/{fileName}".Replace("\\", "/");
    }

    private void DeleteStaticFile(string? staticPath)
    {
        if (string.IsNullOrWhiteSpace(staticPath))
        {
            return;
        }

        var normalized = staticPath.Trim().Replace("\\", "/");
        if (!normalized.StartsWith('/'))
        {
            return;
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            return;
        }

        var webRootPath = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var physicalPath = Path.Combine(webRootPath, normalized.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

        try
        {
            if (System.IO.File.Exists(physicalPath))
            {
                System.IO.File.Delete(physicalPath);
            }
        }
        catch
        {
            // Swallow
        }
    }
}
