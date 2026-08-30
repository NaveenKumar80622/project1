using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PickNBook.Api.Services;

/// <summary>
/// Concrete implementation of IBlogsService executing data operations and business rules.
/// </summary>
public class BlogsService : IBlogsService
{
    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorageService;

    // Constraints matching original controller rules
    private const long MaxImageBytes = 1 * 1024 * 1024; // 1MB
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public BlogsService(AppDbContext context, IFileStorageService fileStorageService)
    {
        _context = context;
        _fileStorageService = fileStorageService;
    }

    /// <summary>
    /// Gets a paginated list of published blog posts.
    /// </summary>
    public async Task<(int Total, IEnumerable<object> Blogs)> GetPublishedBlogsAsync(int page, int pageSize, string? category, bool featuredOnly)
    {
        var query = _context.BlogPosts
            .AsNoTracking()
            .Where(x => x.IsPublished);

        // Apply category filter if provided
        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim();
            query = query.Where(x => x.Category == normalizedCategory);
        }

        // Apply featured posts filter if active
        if (featuredOnly)
        {
            query = query.Where(x => x.IsFeatured);
        }

        // Count total results for pagination pagination metadata
        var total = await query.CountAsync();

        // Project all fields to match frontend paginated blog list contract
        var blogs = await query
            .OrderByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Slug,
                x.Category,
                x.SubCategory,
                x.SubTitle,
                x.ShortDescription,
                x.LongDescription,
                x.ImageUrl,
                x.OgImageUrl,
                x.IsFeatured,
                x.IsPublished,
                publishedAtUtc = x.PublishedAtUtc ?? x.CreatedAtUtc,
                x.CreatedAtUtc,
                x.AddedByName,
                x.MetaTitle,
                x.MetaKeyword,
                x.MetaDescription
            })
            .ToListAsync();

        return (total, blogs);
    }

    /// <summary>
    /// Gets a single published blog post details by its unique URL slug.
    /// </summary>
    public async Task<object?> GetPublishedBlogBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();

        // Retrieve and project full fields (including IsPublished and CreatedAtUtc)
        return await _context.BlogPosts
            .AsNoTracking()
            .Where(x => x.IsPublished && x.Slug == normalizedSlug)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Slug,
                x.Category,
                x.SubCategory,
                x.SubTitle,
                x.ShortDescription,
                x.LongDescription,
                x.ImageUrl,
                x.OgImageUrl,
                x.IsFeatured,
                x.IsPublished,
                publishedAtUtc = x.PublishedAtUtc ?? x.CreatedAtUtc,
                x.CreatedAtUtc,
                x.AddedByName,
                x.MetaTitle,
                x.MetaKeyword,
                x.MetaDescription
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Gets a paginated list of blog posts for administrative management.
    /// </summary>
    public async Task<(int Total, IEnumerable<object> Blogs)> GetAdminBlogsAsync(int page, int pageSize, bool? isPublished)
    {
        var query = _context.BlogPosts.AsNoTracking().AsQueryable();

        // Filter by publication status if requested
        if (isPublished.HasValue)
        {
            query = query.Where(x => x.IsPublished == isPublished.Value);
        }

        var total = await query.CountAsync();

        // Sort by creation date descending so newer items show up first, projecting all fields for admin dashboard consistency
        var blogs = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Slug,
                x.Category,
                x.SubCategory,
                x.SubTitle,
                x.ShortDescription,
                x.LongDescription,
                x.ImageUrl,
                x.OgImageUrl,
                x.IsFeatured,
                x.IsPublished,
                publishedAtUtc = x.PublishedAtUtc ?? x.CreatedAtUtc,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.AddedByName,
                x.MetaTitle,
                x.MetaKeyword,
                x.MetaDescription
            })
            .ToListAsync();

        return (total, blogs);
    }

    /// <summary>
    /// Validates inputs, checks slug uniqueness, saves file attachments, and adds a new blog post to the database.
    /// </summary>
    public async Task<(bool Success, string? Error, BlogPost? Blog)> CreateBlogAsync(UpsertBlogRequest request, int? userId)
    {
        // 1. Validate request fields
        var validationError = ValidateBlogRequest(request);
        if (validationError != null)
        {
            return (false, validationError, null);
        }

        // 2. Build unique URL slug
        var slugBase = BuildSlug(request.Title, request.Slug);
        var slug = await EnsureUniqueSlugAsync(slugBase, null);

        // 3. Save uploaded image assets using file storage service abstraction
        var imageUrl = await _fileStorageService.SaveFileAsync(request.Image, "blogs/images");
        var ogImageUrl = await _fileStorageService.SaveFileAsync(request.OgImage, "blogs/og-images");

        // 4. Resolve author details from database users table
        var addedByName = await ResolveAddedByNameAsync(userId);
        var now = DateTime.UtcNow;

        // 5. Populate and write blog entity to database
        var blog = new BlogPost
        {
            Title = request.Title.Trim(),
            Slug = slug,
            Category = request.Category.Trim(),
            SubCategory = request.SubCategory.Trim(),
            ShortDescription = request.ShortDescription.Trim(),
            LongDescription = request.LongDescription.Trim(),
            SubTitle = request.SubTitle?.Trim(),
            IsFeatured = request.IsFeatured,
            MetaTitle = request.MetaTitle?.Trim(),
            MetaKeyword = request.MetaKeyword?.Trim(),
            MetaDescription = request.MetaDescription?.Trim(),
            ImageUrl = imageUrl,
            OgImageUrl = ogImageUrl,
            IsPublished = request.IsPublished,
            PublishedAtUtc = request.IsPublished ? now : null,
            AddedByUserId = userId,
            AddedByName = addedByName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.BlogPosts.Add(blog);
        await _context.SaveChangesAsync();

        return (true, null, blog);
    }

    /// <summary>
    /// Validates inputs, checks unique slug rules, updates file attachments, and modifies an existing blog post.
    /// </summary>
    public async Task<(bool Success, string? Error, BlogPost? Blog)> UpdateBlogAsync(long id, UpsertBlogRequest request)
    {
        // 1. Validate request fields
        var validationError = ValidateBlogRequest(request);
        if (validationError != null)
        {
            return (false, validationError, null);
        }

        // 2. Verify blog post exists
        var blog = await _context.BlogPosts.FirstOrDefaultAsync(x => x.Id == id);
        if (blog == null)
        {
            return (false, "Blog not found.", null);
        }

        // 3. Verify slug uniqueness
        var slugBase = BuildSlug(request.Title, request.Slug);
        var slug = await EnsureUniqueSlugAsync(slugBase, id);

        // 4. Update image file attachments (delete previous physical file if replaced)
        if (request.Image != null)
        {
            var previous = blog.ImageUrl;
            blog.ImageUrl = await _fileStorageService.SaveFileAsync(request.Image, "blogs/images");
            _fileStorageService.DeleteFile(previous);
        }

        if (request.OgImage != null)
        {
            var previous = blog.OgImageUrl;
            blog.OgImageUrl = await _fileStorageService.SaveFileAsync(request.OgImage, "blogs/og-images");
            _fileStorageService.DeleteFile(previous);
        }

        // 5. Update database record properties
        blog.Title = request.Title.Trim();
        blog.Slug = slug;
        blog.Category = request.Category.Trim();
        blog.SubCategory = request.SubCategory.Trim();
        blog.ShortDescription = request.ShortDescription.Trim();
        blog.LongDescription = request.LongDescription.Trim();
        blog.SubTitle = request.SubTitle?.Trim();
        blog.IsFeatured = request.IsFeatured;
        blog.MetaTitle = request.MetaTitle?.Trim();
        blog.MetaKeyword = request.MetaKeyword?.Trim();
        blog.MetaDescription = request.MetaDescription?.Trim();
        blog.IsPublished = request.IsPublished;
        blog.PublishedAtUtc = request.IsPublished
            ? (blog.PublishedAtUtc ?? DateTime.UtcNow)
            : null;
        blog.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return (true, null, blog);
    }

    /// <summary>
    /// Deletes a blog post and removes its physical image files from static file storage.
    /// </summary>
    public async Task<(bool Success, string? Error)> DeleteBlogAsync(long id)
    {
        // 1. Verify blog post exists
        var blog = await _context.BlogPosts.FirstOrDefaultAsync(x => x.Id == id);
        if (blog == null)
        {
            return (false, "Blog not found.");
        }

        // 2. Delete linked image assets from disk
        _fileStorageService.DeleteFile(blog.ImageUrl);
        _fileStorageService.DeleteFile(blog.OgImageUrl);

        // 3. Remove DB record
        _context.BlogPosts.Remove(blog);
        await _context.SaveChangesAsync();

        return (true, null);
    }

    /// <summary>
    /// Internal validation rules matching original controller logic constraints.
    /// </summary>
    private static string? ValidateBlogRequest(UpsertBlogRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return "Title is required.";
        if (string.IsNullOrWhiteSpace(request.Category))
            return "Category is required.";
        if (string.IsNullOrWhiteSpace(request.SubCategory))
            return "SubCategory is required.";
        if (string.IsNullOrWhiteSpace(request.ShortDescription))
            return "ShortDescription is required.";
        if (string.IsNullOrWhiteSpace(request.LongDescription))
            return "LongDescription is required.";

        if (request.Image != null && request.Image.Length > MaxImageBytes)
            return "Image size must be less than or equal to 1MB.";
        if (request.OgImage != null && request.OgImage.Length > MaxImageBytes)
            return "OG image size must be less than or equal to 1MB.";

        if (request.Image != null && !IsSupportedImage(request.Image.FileName))
            return "Unsupported image format. Use .jpg, .jpeg, .png, or .webp.";
        if (request.OgImage != null && !IsSupportedImage(request.OgImage.FileName))
            return "Unsupported OG image format. Use .jpg, .jpeg, .png, or .webp.";

        return null;
    }

    private static bool IsSupportedImage(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return AllowedImageExtensions.Contains(extension);
    }

    /// <summary>
    /// Normalizes text or provided slug to generate a clean URL slug.
    /// Removes non-alphanumeric symbols and formats spaces to hyphens.
    /// </summary>
    private static string BuildSlug(string title, string? providedSlug)
    {
        var source = string.IsNullOrWhiteSpace(providedSlug) ? title : providedSlug;
        var lower = source.Trim().ToLowerInvariant();

        // Strip non-alphanumeric characters, leaving spaces and hyphens
        lower = Regex.Replace(lower, @"[^a-z0-9\s-]", string.Empty);
        // Replace spaces with hyphens
        lower = Regex.Replace(lower, @"\s+", "-");
        // Remove duplicate/consecutive hyphens
        lower = Regex.Replace(lower, @"-+", "-");
        // Trim leading and trailing hyphens
        lower = lower.Trim('-');

        // Fallback to random identifier if slug ends up empty
        if (string.IsNullOrWhiteSpace(lower))
        {
            lower = $"blog-{Guid.NewGuid():N}".Substring(0, 13);
        }

        return lower;
    }

    /// <summary>
    /// Recursive uniqueness helper. Appends incremental suffix numbers if slug already exists in DB.
    /// </summary>
    private async Task<string> EnsureUniqueSlugAsync(string slugBase, long? ignoreId)
    {
        var slug = slugBase;
        var suffix = 2;

        while (await _context.BlogPosts.AnyAsync(x =>
                   x.Slug == slug &&
                   (!ignoreId.HasValue || x.Id != ignoreId.Value)))
        {
            slug = $"{slugBase}-{suffix}";
            suffix += 1;
        }

        return slug;
    }

    /// <summary>
    /// Resolves the writer's full name from the users database using their user identifier.
    /// Defaults to "Admin" if not found or no ID was provided.
    /// </summary>
    private async Task<string> ResolveAddedByNameAsync(int? userId)
    {
        if (!userId.HasValue)
        {
            return "Admin";
        }

        var user = await _context.Users
            .AsNoTracking()
            .Where(x => x.Id == userId.Value)
            .Select(x => new { x.FirstName, x.LastName })
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return "Admin";
        }

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "Admin" : fullName;
    }
}
