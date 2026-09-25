using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

[Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
[Route("api/files")]
public class FilesController : BaseApiController
{
    private readonly IWebHostEnvironment _environment;

    public FilesController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file, [FromQuery] string type)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file was uploaded.");
        }

        string subFolder = type?.ToLowerInvariant() switch
        {
            "team" => "team",
            "promotions" or "coupons" or "offers" => "promotions",
            _ => "about"
        };
        string targetFolder = $"uploads/{subFolder}";

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg", ".bmp", ".avif" };
        if (Array.IndexOf(allowedExtensions, ext) < 0)
        {
            return BadRequest("Invalid image format. Allowed formats: JPG, JPEG, PNG, WEBP, GIF, SVG, BMP, AVIF.");
        }

        var root = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var uploadPath = Path.Combine(root, targetFolder.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        var uniqueName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadPath, uniqueName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new { url = $"/{targetFolder}/{uniqueName}".Replace("\\", "/") });
    }
}
