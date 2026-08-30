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
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Utils;

namespace PickNBook.Api.Services
{
    public class BusCityCacheService : IHostedService
    {
        private readonly ILogger<BusCityCacheService> _logger;
        private readonly IWebHostEnvironment _env;

        public List<PlaceSuggestionDto> BusCities { get; private set; } = new();

        public BusCityCacheService(ILogger<BusCityCacheService> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Loading SRDV Bus City Code Cache...");

            try
            {
                var filePath = Path.Combine(_env.ContentRootPath, "Data", "srdv_bus_city_codes.json");
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                    BusCities = ParsePhpMyAdminJson(json);
                    _logger.LogInformation($"Loaded {BusCities.Count} Bus City Codes.");
                }
                else
                {
                    _logger.LogWarning($"Bus city codes JSON not found at {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load bus city code cache.");
            }
        }

        private List<PlaceSuggestionDto> ParsePhpMyAdminJson(string json)
        {
            var cities = new List<PlaceSuggestionDto>();
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
                                foreach (var row in dataProp.EnumerateArray())
                                {
                                    var cityName = row.TryGetProperty("cico_city_name", out var nameProp) ? nameProp.GetString() : null;
                                    var cityId = row.TryGetProperty("cico_id", out var idProp) ? idProp.GetString() : null;
                                    var stateName = row.TryGetProperty("cico_state_name", out var stateProp) ? stateProp.GetString() : null;

                                    if (!string.IsNullOrEmpty(cityName) && !string.IsNullOrEmpty(cityId))
                                    {
                                        cities.Add(new PlaceSuggestionDto
                                        {
                                            CityName = cityName,
                                            CityCode = cityId,
                                            StateName = stateName,
                                            CountryCode = "IN",
                                            CountryName = "India",
                                            TripType = "bus",
                                            UsageCount = 1
                                        });
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse bus city codes phpMyAdmin JSON.");
            }
            return cities;
        }

        public List<PlaceSuggestionDto> SearchCities(string query, int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BusCities.Take(limit).ToList();

            var strictMatches = BusCities
                .Where(c => c.CityName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();

            if (strictMatches.Any())
                return strictMatches;

            // Fallback to fuzzy match (tolerate up to 2 character typos, e.g., "Banglore" -> "Bangalore")
            var queryLower = query.ToLower();
            return BusCities
                .Select(c => new { 
                    City = c, 
                    Distance = FuzzyMatcher.ComputeLevenshteinDistance(queryLower, c.CityName.ToLower()) 
                })
                .Where(x => x.Distance <= 2)
                .OrderBy(x => x.Distance)
                .Select(x => x.City)
                .Take(limit)
                .ToList();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
