using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

/// <summary>
/// API Controller exposing public and administrative endpoints for blog management.
/// Delegating business operations to the IBlogsService.
/// </summary>
public class BlogsController : BaseApiController
{
    private readonly IBlogsService _blogsService;

    public BlogsController(IBlogsService blogsService)
    {
        _blogsService = blogsService;
    }

    /// <summary>
    /// GET: api/blogs
    /// Retrieves a paginated list of published blog posts for public display.
    /// </summary>
    /// <param name="page">The page offset number (defaults to 1).</param>
    /// <param name="pageSize">The page records count (defaults to 10, clamped to 50 max).</param>
    /// <param name="category">Optional category tag to filter by.</param>
    /// <param name="featuredOnly">If true, retrieves only featured blog posts.</param>
    /// <returns>Returns a 200 OK result with paginated results.</returns>
    [HttpGet]
    public async Task<IActionResult> GetPublishedBlogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? category = null,
        [FromQuery] bool featuredOnly = false)
    {
        // 1. Clamp values at API boundary to prevent resource exhaustion attacks
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        // 2. Fetch and return data from service layer
        var (total, blogs) = await _blogsService.GetPublishedBlogsAsync(page, pageSize, category, featuredOnly);
        return Ok(new
        {
            total,
            page,
            pageSize,
            blogs
        });
    }

    /// <summary>
    /// GET: api/blogs/{slug}
    /// Retrieves full blog post details for a public reader by its unique URL slug.
    /// </summary>
    /// <param name="slug">The unique slug string.</param>
    /// <returns>Returns 200 OK with the blog post, 400 BadRequest if empty, or 404 NotFound if missing.</returns>
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetPublishedBlogBySlug(string slug)
    {
        // 1. Verify slug is not empty
        if (string.IsNullOrWhiteSpace(slug))
        {
            return BadRequest("Slug is required.");
        }

        // 2. Fetch blog by slug
        var blog = await _blogsService.GetPublishedBlogBySlugAsync(slug);
        if (blog == null)
        {
            return NotFound("Blog not found.");
        }

        return Ok(blog);
    }

    /// <summary>
    /// GET: api/blogs/admin/list
    /// Retrieves a paginated list of all blog posts (including drafts) for admin dashboards.
    /// </summary>
    /// <param name="page">The page number.</param>
    /// <param name="pageSize">Number of records per page (clamped to 100 max).</param>
    /// <param name="isPublished">Optional filter on publication status.</param>
    /// <returns>Returns 200 OK with paginated list.</returns>
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminBlogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isPublished = null)
    {
        // 1. Clamp input values
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // 2. Execute retrieval
        var (total, blogs) = await _blogsService.GetAdminBlogsAsync(page, pageSize, isPublished);
        return Ok(new
        {
            total,
            page,
            pageSize,
            blogs
        });
    }

    /// <summary>
    /// POST: api/blogs/admin
    /// Creates a new blog post. Requires admin authorization.
    /// </summary>
    /// <param name="request">Multipart form-data carrying text fields and binary file images.</param>
    /// <returns>Returns 200 OK with success details, or 400 BadRequest on failure.</returns>
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPost("admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateBlog([FromForm] UpsertBlogRequest request)
    {
        // 1. Resolve current user identity
        var userId = TryGetCurrentUserId();
        
        // 2. Delegate creation to service layer
        var (success, error, blog) = await _blogsService.CreateBlogAsync(request, userId);

        if (!success)
        {
            return BadRequest(error);
        }

        return Ok(blog);
    }

    /// <summary>
    /// PUT: api/blogs/admin/{id}
    /// Updates an existing blog post by its unique ID.
    /// </summary>
    /// <param name="id">Database ID of the blog post.</param>
    /// <param name="request">Updated fields and optional new files.</param>
    /// <returns>Returns 200 OK with details, 404 if not found, or 400 if validation fails.</returns>
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPut("admin/{id:long}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateBlog(long id, [FromForm] UpsertBlogRequest request)
    {
        // 1. Execute update
        var (success, error, blog) = await _blogsService.UpdateBlogAsync(id, request);

        if (!success)
        {
            if (error == "Blog not found.")
            {
                return NotFound(error);
            }
            return BadRequest(error);
        }

        return Ok(blog);
    }

    /// <summary>
    /// DELETE: api/blogs/admin/{id}
    /// Deletes a blog post and its image assets from disk.
    /// </summary>
    /// <param name="id">The blog post ID.</param>
    /// <returns>Returns 200 OK if successful, or 404 NotFound if missing.</returns>
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpDelete("admin/{id:long}")]
    public async Task<IActionResult> DeleteBlog(long id)
    {
        // 1. Execute delete operation
        var (success, error) = await _blogsService.DeleteBlogAsync(id);

        if (!success)
        {
            return NotFound(error);
        }

        return Ok(new { success = true, message = "Blog deleted successfully" });
    }

    /// <summary>
    /// Resolves the authenticated user ID from the JWT claims principal block.
    /// </summary>
    private int? TryGetCurrentUserId()
    {
        if (User == null) return null;
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
