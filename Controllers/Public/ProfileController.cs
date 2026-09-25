using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

[Authorize]
public class ProfileController : BaseApiController
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public ProfileController(AppDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized("Invalid token");

        var profile = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserProfileDto
            {
                UserId = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Location = u.City,
                ProfileImage = u.ProfileImageUrl,
                WalletBalance = u.WalletBalance,
                WalletStatus = u.WalletStatus
            })
            .FirstOrDefaultAsync();

        if (profile == null)
            return NotFound("User not found.");

        return Ok(profile);
    }

    /// <summary>
    /// Admin-only endpoint to inspect another user's profile by ID.
    /// Normal customers are forbidden from accessing other users' profiles.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    public async Task<IActionResult> GetUserById(int id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserProfileDto
            {
                UserId = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Location = u.City,
                ProfileImage = u.ProfileImageUrl,
                WalletBalance = u.WalletBalance,
                WalletStatus = u.WalletStatus
            })
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound("User not found.");

        return Ok(user);
    }

    [HttpPut("edit")]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    public Task<IActionResult> EditProfileFromForm([FromForm] EditProfileRequest request)
        => EditProfileCore(request.FirstName, request.LastName, request.PhoneNumber, request.Location, request.ProfileImage, null);

    [HttpPut("edit")]
    [Consumes("application/json", "text/json", "application/*+json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<IActionResult> EditProfileFromJson([FromBody] EditProfileJsonRequest request)
        => EditProfileCore(request.FirstName, request.LastName, request.PhoneNumber, request.Location, null, request.ProfileImage);

    [HttpPost("upload-image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadProfileImage(IFormFile file)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized("Invalid token");

        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "No image file provided." });

        var (isValid, errorMessage, relativePath) = await SaveProfileImageAsync(file);
        if (!isValid)
            return BadRequest(new { success = false, message = errorMessage });

        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.ProfileImageUrl = relativePath;
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            success = true,
            profileImage = relativePath
        });
    }

    private async Task<IActionResult> EditProfileCore(
        string? firstName,
        string? lastName,
        string? phoneNumber,
        string? location,
        IFormFile? profileImageFile,
        string? profileImageUrl = null)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized("Invalid token");

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return NotFound("User not found.");

        if (firstName is not null)
            user.FirstName = firstName.Trim();

        if (lastName is not null)
            user.LastName = lastName.Trim();

        if (phoneNumber is not null)
            user.PhoneNumber = phoneNumber.Trim();

        if (location is not null)
            user.City = location.Trim(); // Mapped to User.City without requiring DB migration

        if (profileImageFile != null)
        {
            var (isValid, errorMessage, relativePath) = await SaveProfileImageAsync(profileImageFile);
            if (!isValid)
                return BadRequest(new { message = errorMessage });

            user.ProfileImageUrl = relativePath;
        }
        else if (!string.IsNullOrWhiteSpace(profileImageUrl))
        {
            user.ProfileImageUrl = profileImageUrl.Trim();
        }

        await _context.SaveChangesAsync();

        var response = new UserProfileDto
        {
            UserId = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Location = user.City,
            ProfileImage = user.ProfileImageUrl,
            WalletBalance = user.WalletBalance,
            WalletStatus = user.WalletStatus
        };

        return Ok(response);
    }

    private async Task<(bool IsValid, string? ErrorMessage, string? RelativePath)> SaveProfileImageAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return (false, "File is empty.", null);

        if (file.Length > MaxFileSizeBytes)
            return (false, "File size exceeds the 5MB limit.", null);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return (false, "Invalid file format. Allowed extensions: .jpg, .jpeg, .png, .webp, .gif", null);

        if (string.IsNullOrWhiteSpace(file.ContentType) || !AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            return (false, "Invalid content type. Only image files are allowed.", null);

        var webRootPath = _environment?.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var uploadsFolder = Path.Combine(webRootPath, "profile-images");

        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return (true, null, $"/profile-images/{fileName}");
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        userId = 0;

        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        return int.TryParse(userIdValue, out userId);
    }
}
