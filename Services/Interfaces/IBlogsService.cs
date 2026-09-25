using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PickNBook.Api.Services;

/// <summary>
/// Service interface encapsulating business logic, validations, database queries, and storage routines for blogs.
/// </summary>
public interface IBlogsService
{
    /// <summary>
    /// Gets a paginated list of published blog posts.
    /// </summary>
    /// <param name="page">Current page number (1-indexed).</param>
    /// <param name="pageSize">Number of records per page.</param>
    /// <param name="category">Optional category slug to filter by.</param>
    /// <param name="featuredOnly">If true, returns only featured posts.</param>
    /// <returns>A tuple containing total count of matching posts and projected blog summaries.</returns>
    Task<(int Total, IEnumerable<object> Blogs)> GetPublishedBlogsAsync(int page, int pageSize, string? category, bool featuredOnly);

    /// <summary>
    /// Gets a single published blog post details by its unique URL slug.
    /// </summary>
    /// <param name="slug">Unique URL slug of the blog.</param>
    /// <returns>Projected full blog post object, or null if not found.</returns>
    Task<object?> GetPublishedBlogBySlugAsync(string slug);

    /// <summary>
    /// Gets a paginated list of blog posts for administrative management.
    /// </summary>
    /// <param name="page">Current page number.</param>
    /// <param name="pageSize">Number of records per page.</param>
    /// <param name="isPublished">Optional filter to return published or unpublished posts.</param>
    /// <returns>A tuple containing total count and projected blog summaries.</returns>
    Task<(int Total, IEnumerable<object> Blogs)> GetAdminBlogsAsync(int page, int pageSize, bool? isPublished);

    /// <summary>
    /// Validates inputs, checks slug uniqueness, saves file attachments, and adds a new blog post to the database.
    /// </summary>
    /// <param name="request">Input data carrying blog fields and files.</param>
    /// <param name="userId">ID of the administrator creating the blog.</param>
    /// <returns>A tuple containing success status, validation error messages, and created blog object.</returns>
    Task<(bool Success, string? Error, BlogPost? Blog)> CreateBlogAsync(UpsertBlogRequest request, int? userId);

    /// <summary>
    /// Validates inputs, checks unique slug rules, updates file attachments, and modifies an existing blog post.
    /// </summary>
    /// <param name="id">ID of the blog post to update.</param>
    /// <param name="request">New blog data and files.</param>
    /// <returns>A tuple containing success status, validation error messages, and updated blog object.</returns>
    Task<(bool Success, string? Error, BlogPost? Blog)> UpdateBlogAsync(long id, UpsertBlogRequest request);

    /// <summary>
    /// Deletes a blog post and removes its physical image files from static file storage.
    /// </summary>
    /// <param name="id">ID of the blog post to delete.</param>
    /// <returns>A tuple containing success status and error message if deletion fails.</returns>
    Task<(bool Success, string? Error)> DeleteBlogAsync(long id);
}
