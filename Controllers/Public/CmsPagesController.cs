using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

public class CmsPagesController : BaseApiController
{
    private const long MaxFileBytes = 1 * 1024 * 1024; // 1MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly IAboutUsService _aboutUsService;

    public CmsPagesController(AppDbContext context, IWebHostEnvironment environment, IAboutUsService aboutUsService)
    {
        _context = context;
        _environment = environment;
        _aboutUsService = aboutUsService;
    }

    // ---------------- PUBLIC API ENDPOINTS ----------------

    [HttpGet]
    public async Task<IActionResult> GetActivePages()
    {
        var pages = await _context.CmsPages
            .AsNoTracking()
            .Where(x => x.Status == "Active")
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Slug,
                x.Status,
                x.Module,
                x.Description,
                imagePath = x.ImageUrl,
                bannerPath = x.BannerUrl,
                x.MetaTitle,
                x.MetaKeyword,
                x.MetaDescription,
                x.CreatedAtUtc,
                x.UpdatedAtUtc
            })
            .ToListAsync();

        return Ok(pages);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetPageBySlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return BadRequest("Slug is required.");
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var page = await _context.CmsPages
            .AsNoTracking()
            .Where(x => x.Slug == normalizedSlug && x.Status == "Active")
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Slug,
                x.Status,
                x.Module,
                x.Description,
                imagePath = x.ImageUrl,
                bannerPath = x.BannerUrl,
                x.MetaTitle,
                x.MetaKeyword,
                x.MetaDescription,
                x.CreatedAtUtc,
                x.UpdatedAtUtc
            })
            .FirstOrDefaultAsync();

        if (page == null)
        {
            return NotFound("Page not found.");
        }

        return Ok(page);
    }

    [HttpGet("about-us")]
    public async Task<IActionResult> GetAboutUs([FromQuery] string module)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            return BadRequest("Module is required.");
        }

        var data = await _aboutUsService.GetAsync(module);
        if (data == null || !string.Equals(data.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound("About Us data not found or is inactive.");
        }

        return Ok(new
        {
            aboutDescription = data.AboutDescription,
            countSection = data.CountSection,
            whoWeAre = data.WhoWeAre,
            teamMembers = data.TeamMembers
        });
    }

    // ---------------- ADMIN API ENDPOINTS ----------------

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpGet("admin/about-us")]
    public async Task<IActionResult> GetEditableAboutUs([FromQuery] string module)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            return BadRequest("Module is required.");
        }

        var data = await _aboutUsService.GetAsync(module);
        if (data == null)
        {
            return Ok(new AboutUsDto { Module = module });
        }

        return Ok(data);
    }

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPut("admin/about-us")]
    public async Task<IActionResult> UpdateAboutUs([FromBody] UpdateAboutUsDto dto)
    {
        var validationError = ValidateAboutUsRequest(dto);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        await _aboutUsService.UpdateAsync(dto);
        var updated = await _aboutUsService.GetAsync(dto.Module);
        return Ok(updated);
    }

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminPages()
    {
        var pages = await _context.CmsPages
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Slug,
                x.Status,
                x.Module,
                x.Description,
                imagePath = x.ImageUrl,
                bannerPath = x.BannerUrl,
                x.MetaTitle,
                x.MetaKeyword,
                x.MetaDescription,
                x.CreatedAtUtc,
                x.UpdatedAtUtc
            })
            .ToListAsync();

        return Ok(pages);
    }

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPost("admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreatePage([FromForm] UpsertCmsPageRequest request)
    {
        var validationError = ValidatePageRequest(request);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        var slugBase = BuildSlug(request.Title, request.Slug);
        var slug = await EnsureUniqueSlugAsync(slugBase, null);

        var imageUrl = await SaveFileAsync(request.Image, "pages/images");
        var bannerUrl = await SaveFileAsync(request.Banner, "pages/banners");

        var page = new CmsPage
        {
            Title = request.Title.Trim(),
            Slug = slug,
            Module = request.Module,
            Status = request.Status,
            MetaTitle = request.MetaTitle?.Trim(),
            MetaKeyword = request.MetaKeyword?.Trim(),
            MetaDescription = request.MetaDescription?.Trim(),
            Description = request.Description,
            ImageUrl = imageUrl,
            BannerUrl = bannerUrl,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.CmsPages.Add(page);
        await _context.SaveChangesAsync();

        return Ok(ProjectPage(page));
    }

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPut("admin/{id:long}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdatePage(long id, [FromForm] UpsertCmsPageRequest request)
    {
        var validationError = ValidatePageRequest(request);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        var page = await _context.CmsPages.FirstOrDefaultAsync(x => x.Id == id);
        if (page == null)
        {
            return NotFound("Page not found.");
        }

        var slugBase = BuildSlug(request.Title, request.Slug);
        var slug = await EnsureUniqueSlugAsync(slugBase, id);

        if (request.Image != null)
        {
            var oldPath = page.ImageUrl;
            page.ImageUrl = await SaveFileAsync(request.Image, "pages/images");
            DeleteStaticFile(oldPath);
        }

        if (request.Banner != null)
        {
            var oldPath = page.BannerUrl;
            page.BannerUrl = await SaveFileAsync(request.Banner, "pages/banners");
            DeleteStaticFile(oldPath);
        }

        page.Title = request.Title.Trim();
        page.Slug = slug;
        page.Module = request.Module;
        page.Status = request.Status;
        page.MetaTitle = request.MetaTitle?.Trim();
        page.MetaKeyword = request.MetaKeyword?.Trim();
        page.MetaDescription = request.MetaDescription?.Trim();
        page.Description = request.Description;
        page.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(ProjectPage(page));
    }

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpDelete("admin/{id:long}")]
    public async Task<IActionResult> DeletePage(long id)
    {
        var page = await _context.CmsPages.FirstOrDefaultAsync(x => x.Id == id);
        if (page == null)
        {
            return NotFound("Page not found.");
        }

        DeleteStaticFile(page.ImageUrl);
        DeleteStaticFile(page.BannerUrl);

        _context.CmsPages.Remove(page);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Page deleted successfully" });
    }

    private static object ProjectPage(CmsPage x)
    {
        return new
        {
            x.Id,
            x.Title,
            x.Slug,
            x.Status,
            x.Module,
            x.Description,
            imagePath = x.ImageUrl,
            bannerPath = x.BannerUrl,
            x.MetaTitle,
            x.MetaKeyword,
            x.MetaDescription,
            x.CreatedAtUtc,
            x.UpdatedAtUtc
        };
    }

    // ---------------- HELPER METHODS ----------------

    private static string? ValidatePageRequest(UpsertCmsPageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return "Title is required.";
        if (string.IsNullOrWhiteSpace(request.Description))
            return "Description is required.";

        if (request.Image != null && request.Image.Length > MaxFileBytes)
            return "Image size must be less than or equal to 2MB.";
        if (request.Banner != null && request.Banner.Length > MaxFileBytes)
            return "Banner size must be less than or equal to 2MB.";

        if (request.Image != null && !IsSupportedExtension(request.Image.FileName))
            return "Unsupported image format. Use .jpg, .jpeg, .png, or .webp.";
        if (request.Banner != null && !IsSupportedExtension(request.Banner.FileName))
            return "Unsupported banner format. Use .jpg, .jpeg, .png, or .webp.";

        return null;
    }

    private static string? ValidateAboutUsRequest(UpdateAboutUsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Module)) return "Module is required.";
        if (string.IsNullOrWhiteSpace(dto.Status)) return "Status is required.";

        if (dto.TeamMembers != null)
        {
            foreach (var member in dto.TeamMembers)
            {
                if (string.IsNullOrWhiteSpace(member.Name) || member.Name.Length > 200)
                    return "Team member Name is required and must be max 200 characters.";
                if (string.IsNullOrWhiteSpace(member.Designation) || member.Designation.Length > 200)
                    return "Team member Designation is required and must be max 200 characters.";
                if (member.ImageUrl != null && member.ImageUrl.Length > 1000)
                    return "Team member ImageUrl must be max 1000 characters.";
            }
        }

        if (dto.CountSection != null)
        {
            foreach (var count in dto.CountSection)
            {
                if (string.IsNullOrWhiteSpace(count.CountValue) || count.CountValue.Length > 50)
                    return "CountValue is required and must be max 50 characters.";
                if (string.IsNullOrWhiteSpace(count.CountTitle) || count.CountTitle.Length > 100)
                    return "CountTitle is required and must be max 100 characters.";
            }
        }

        return null;
    }

    private static bool IsSupportedExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return AllowedExtensions.Contains(ext);
    }

    private static string BuildSlug(string title, string? providedSlug)
    {
        var source = string.IsNullOrWhiteSpace(providedSlug) ? title : providedSlug;
        var lower = source.Trim().ToLowerInvariant();
        lower = Regex.Replace(lower, @"[^a-z0-9\s-]", string.Empty);
        lower = Regex.Replace(lower, @"\s+", "-");
        lower = Regex.Replace(lower, @"-+", "-");
        lower = lower.Trim('-');

        if (string.IsNullOrWhiteSpace(lower))
        {
            lower = $"page-{Guid.NewGuid():N}".Substring(0, 13);
        }

        return lower;
    }

    private async Task<string> EnsureUniqueSlugAsync(string slugBase, long? ignoreId)
    {
        var slug = slugBase;
        var suffix = 2;

        while (await _context.CmsPages.AnyAsync(x =>
                   x.Slug == slug &&
                   (!ignoreId.HasValue || x.Id != ignoreId.Value)))
        {
            slug = $"{slugBase}-{suffix}";
            suffix++;
        }

        return slug;
    }

    private async Task<string?> SaveFileAsync(IFormFile? file, string relativeDir)
    {
        if (file == null || file.Length == 0)
        {
            return null;
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var name = $"{Guid.NewGuid():N}{ext}";

        var root = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var dir = Path.Combine(root, relativeDir.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var path = Path.Combine(dir, name);
        await using var stream = new FileStream(path, FileMode.Create);
        await file.CopyToAsync(stream);

        return $"/{relativeDir}/{name}".Replace("\\", "/");
    }

    private void DeleteStaticFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var cleanPath = path.Trim().Replace("\\", "/");
        if (!cleanPath.StartsWith('/') || cleanPath.Contains(".."))
        {
            return;
        }

        var root = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var physicalPath = Path.Combine(root, cleanPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

        try
        {
            if (System.IO.File.Exists(physicalPath))
            {
                System.IO.File.Delete(physicalPath);
            }
        }
        catch
        {
            // Swallow exception
        }
    }
}
