using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using System.Linq;

namespace PickNBook.Api.Services
{
    public class HotelCityCacheService : IHostedService
    {
        private readonly ILogger<HotelCityCacheService> _logger;
        private readonly IWebHostEnvironment _env;

        public HashSet<string> SpecialCityIds { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> InternationalCityIds { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HotelCityCacheService(ILogger<HotelCityCacheService> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Loading SRDV Hotel City Caches...");

            try
            {
                var specialPath = Path.Combine(_env.ContentRootPath, "Data", "srdv_hotel_cities_special.json");
                if (File.Exists(specialPath))
                {
                    var json = await File.ReadAllTextAsync(specialPath, cancellationToken);
                    var cities = ParsePhpMyAdminJson(json);
                    if (cities != null)
                    {
                        SpecialCityIds = new HashSet<string>(cities.Where(c => !string.IsNullOrEmpty(c.CityId)).Select(c => c.CityId!), StringComparer.OrdinalIgnoreCase);
                        _logger.LogInformation($"Loaded {SpecialCityIds.Count} Special City IDs.");
                    }
                }
                else
                {
                    _logger.LogWarning($"Special city JSON not found at {specialPath}");
                }

                var intlPath = Path.Combine(_env.ContentRootPath, "Data", "srdv_hotel_cities_intl.json");
                if (File.Exists(intlPath))
                {
                    var json = await File.ReadAllTextAsync(intlPath, cancellationToken);
                    var cities = ParsePhpMyAdminJson(json);
                    if (cities != null)
                    {
                        InternationalCityIds = new HashSet<string>(cities.Where(c => !string.IsNullOrEmpty(c.CityId)).Select(c => c.CityId!), StringComparer.OrdinalIgnoreCase);
                        _logger.LogInformation($"Loaded {InternationalCityIds.Count} International City IDs.");
                    }
                }
                else
                {
                    _logger.LogWarning($"International city JSON not found at {intlPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load hotel city caches.");
            }
        }

        private List<SrdvCityModel> ParsePhpMyAdminJson(string json)
        {
            var cities = new List<SrdvCityModel>();
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "table")
                        {
                            if (element.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                            {
                                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                                cities = JsonSerializer.Deserialize<List<SrdvCityModel>>(dataProp.GetRawText(), options) ?? new List<SrdvCityModel>();
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse phpMyAdmin JSON.");
            }
            return cities;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private class SrdvCityModel
        {
            public string? CityId { get; set; }
        }
    }
}
