using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;

namespace PickNBook.Api.Controllers;

[Authorize]
public class ProfileController : BaseApiController
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;

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
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.PhoneNumber,
                u.ProfileImageUrl,
                u.Status,
                u.WalletStatus,
                u.WalletBalance
            })
            .FirstOrDefaultAsync();

        if (profile == null)
            return NotFound("User not found.");

        return Ok(profile);
    }
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUserById(int id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.PhoneNumber,
                u.ProfileImageUrl,
                u.Status,
                u.WalletStatus,
                u.WalletBalance
            })
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound("User not found.");

        return Ok(user);
    }

    [HttpPut("edit")]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    public Task<IActionResult> EditProfileFromForm([FromForm] EditProfileRequest request)
        => EditProfileCore(request.FirstName, request.LastName, request.PhoneNumber, request.ProfileImage);

    [HttpPut("edit")]
    [Consumes("application/json", "text/json", "application/*+json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<IActionResult> EditProfileFromJson([FromBody] EditProfileJsonRequest request)
        => EditProfileCore(request.FirstName, request.LastName, request.PhoneNumber, null);

    private async Task<IActionResult> EditProfileCore(
        string? firstName,
        string? lastName,
        string? phoneNumber,
        IFormFile? profileImage)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized("Invalid token");

        var user = await _context.Users.FindAsync(userId);

        if (user == null)
            return NotFound("User not found.");

        if (firstName is not null)
            user.FirstName = firstName;

        if (lastName is not null)
            user.LastName = lastName;

        if (phoneNumber is not null)
            user.PhoneNumber = phoneNumber;

        if (profileImage != null)
        {
            var webRootPath = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadsFolder = Path.Combine(webRootPath, "profile-images");

            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(profileImage.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await profileImage.CopyToAsync(stream);

            user.ProfileImageUrl = $"/profile-images/{fileName}";
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.PhoneNumber,
            user.ProfileImageUrl
        });
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        userId = 0;

        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        return int.TryParse(userIdValue, out userId);
    }
}
