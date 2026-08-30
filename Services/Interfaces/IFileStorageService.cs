using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace PickNBook.Api.Services;

/// <summary>
/// Service interface for handling physical file operations like saving and deleting uploads.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Saves an uploaded file to a local or remote directory under a unique filename.
    /// </summary>
    /// <param name="file">The uploaded file from the HTTP request.</param>
    /// <param name="folderRelativePath">The directory folder path under wwwroot (e.g. "blogs/images").</param>
    /// <returns>The relative URL path of the saved file, or null if no file was uploaded.</returns>
    Task<string?> SaveFileAsync(IFormFile? file, string folderRelativePath);

    /// <summary>
    /// Deletes a file from physical storage based on its relative URL path.
    /// </summary>
    /// <param name="relativePath">The relative URL path of the file to delete.</param>
    void DeleteFile(string? relativePath);
}
