using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class SrdvMasterDataDownloader : ISrdvMasterDataDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly SrdvMasterDataSettings _settings;
        private readonly ILogger<SrdvMasterDataDownloader> _logger;
        private readonly IWebHostEnvironment _env;

        public SrdvMasterDataDownloader(
            HttpClient httpClient,
            IOptions<SrdvMasterDataSettings> settings,
            ILogger<SrdvMasterDataDownloader> logger,
            IWebHostEnvironment env)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
            _env = env;

            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        public async Task<string> DownloadAndExtractAsync(string resourceUrl, string stagingSubdir, CancellationToken cancellationToken = default)
        {
            var baseStaging = Path.IsPathRooted(_settings.DownloadDirectory)
                ? _settings.DownloadDirectory
                : Path.Combine(_env.ContentRootPath, _settings.DownloadDirectory);

            var targetDirectory = Path.Combine(baseStaging, stagingSubdir);
            Directory.CreateDirectory(targetDirectory);

            var tempZipFile = Path.Combine(targetDirectory, "source.zip");

            _logger.LogInformation("Starting download from {Url} to {Destination}", resourceUrl, tempZipFile);

            using (var response = await _httpClient.GetAsync(resourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength ?? 0;
                _logger.LogInformation("Download connected. Expected bytes: {Bytes}", contentLength);

                using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = new FileStream(tempZipFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await stream.CopyToAsync(fileStream, cancellationToken);
                }
            }

            _logger.LogInformation("Download completed: {File} ({Bytes} bytes). Extracting...", tempZipFile, new FileInfo(tempZipFile).Length);

            string? extractedSqlPath = null;

            try
            {
                using (var archive = ZipFile.OpenRead(tempZipFile))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (entry.FullName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                        {
                            var destinationPath = Path.Combine(targetDirectory, entry.Name);
                            entry.ExtractToFile(destinationPath, overwrite: true);
                            extractedSqlPath = destinationPath;
                            _logger.LogInformation("Extracted SQL file: {Path} ({Bytes} bytes)", destinationPath, entry.Length);
                            break;
                        }
                    }
                }
            }
            catch (InvalidDataException)
            {
                // In case the downloaded file is already a raw .sql file rather than a zip
                _logger.LogWarning("Download is not a standard ZIP archive. Treating as direct SQL file.");
                var destinationPath = Path.Combine(targetDirectory, "dump.sql");
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(tempZipFile, destinationPath);
                extractedSqlPath = destinationPath;
            }
            finally
            {
                if (File.Exists(tempZipFile))
                {
                    try { File.Delete(tempZipFile); } catch { }
                }
            }

            if (string.IsNullOrEmpty(extractedSqlPath) || !File.Exists(extractedSqlPath))
            {
                throw new FileNotFoundException($"No valid .sql file found after downloading from {resourceUrl}");
            }

            return extractedSqlPath;
        }

        public Task CleanupStagingAsync(string stagingSubdir)
        {
            try
            {
                var baseStaging = Path.IsPathRooted(_settings.DownloadDirectory)
                    ? _settings.DownloadDirectory
                    : Path.Combine(_env.ContentRootPath, _settings.DownloadDirectory);

                var targetDirectory = Path.Combine(baseStaging, stagingSubdir);
                if (Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, recursive: true);
                    _logger.LogInformation("Cleaned up staging directory {Directory}", targetDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up staging directory {Subdir}", stagingSubdir);
            }

            return Task.CompletedTask;
        }
    }
}
