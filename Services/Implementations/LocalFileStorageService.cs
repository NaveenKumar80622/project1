using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PickNBook.Api.Services;

/// <summary>
/// Handles local file operations inside the wwwroot web hosting directory.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _environment;

    public LocalFileStorageService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    /// <summary>
    /// Saves an uploaded file physically to the local web hosting directory (wwwroot).
    /// </summary>
    /// <param name="file">The uploaded file to save.</param>
    /// <param name="folderRelativePath">The directory path within wwwroot.</param>
    /// <returns>A web-accessible URL relative path (e.g. "/blogs/images/somefile.png").</returns>
    public async Task<string?> SaveFileAsync(IFormFile? file, string folderRelativePath)
    {
        // 1. Return null immediately if no file was uploaded
        if (file == null || file.Length <= 0)
        {
            return null;
        }

        // 2. Generate a unique name for the file using a GUID to prevent overwrites
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid():N}{extension}";

        // 3. Resolve the physical folder path under wwwroot
        var webRootPath = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var targetFolder = Path.Combine(webRootPath, folderRelativePath.Replace('/', Path.DirectorySeparatorChar));

        // 4. Create the target directory if it doesn't exist yet
        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        // 5. Open a file stream and write the uploaded data to disk
        var filePath = Path.Combine(targetFolder, fileName);
        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        // 6. Return the relative URL path of the saved file
        return $"/{folderRelativePath}/{fileName}".Replace("\\", "/");
    }

    /// <summary>
    /// Deletes a file physically from the local web hosting directory (wwwroot).
    /// </summary>
    /// <param name="relativePath">The web-accessible relative path of the file.</param>
    public void DeleteFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var normalized = relativePath.Trim().Replace("\\", "/");
        if (!normalized.StartsWith('/'))
        {
            return;
        }

        // Security check: Prevent directory traversal attacks (e.g., passing "../../" to delete system files)
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            return;
        }

        // Resolve the physical location of the file to delete
        var webRootPath = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var physicalPath = Path.Combine(webRootPath, normalized.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

        try
        {
            // Delete the file if it exists
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
        catch
        {
            // Swallow exceptions to keep service calls resilient against minor I/O glitches
        }
    }
}
