using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Utils;

namespace PickNBook.Api.Services
{
    public class BusCityCacheService : IHostedService
    {
        private readonly ILogger<BusCityCacheService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public List<PlaceSuggestionDto> BusCities { get; private set; } = new();
        private HashSet<long> _validCityIds = new();
        private Dictionary<long, string> _cityIdToName = new();
        private Dictionary<string, long> _cityNameToId = new(StringComparer.OrdinalIgnoreCase);

        public BusCityCacheService(ILogger<BusCityCacheService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return ReloadAsync(cancellationToken);
        }

        public async Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Loading SRDV Bus City Code Cache from database...");

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var dbCities = await dbContext.BusCities
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.CityName)
                    .ToListAsync(cancellationToken);

                var cities = new List<PlaceSuggestionDto>(dbCities.Count);
                var validIds = new HashSet<long>(dbCities.Count);
                var idToName = new Dictionary<long, string>(dbCities.Count);
                var nameToId = new Dictionary<string, long>(dbCities.Count * 2, StringComparer.OrdinalIgnoreCase);

                foreach (var c in dbCities)
                {
                    validIds.Add(c.CityId);
                    idToName[c.CityId] = c.CityName;
                    
                    if (!string.IsNullOrWhiteSpace(c.CityName))
                    {
                        if (!nameToId.ContainsKey(c.CityName))
                        {
                            nameToId[c.CityName] = c.CityId;
                        }
                        var cleanName = c.CityName.Split('(')[0].Trim();
                        if (!string.IsNullOrWhiteSpace(cleanName) && !nameToId.ContainsKey(cleanName))
                        {
                            nameToId[cleanName] = c.CityId;
                        }
                    }

                    cities.Add(new PlaceSuggestionDto
                    {
                        CityName = c.CityName,
                        CityCode = c.CityId.ToString(),
                        StateName = c.StateName,
                        CountryCode = c.CountryCode,
                        CountryName = c.CountryName,
                        TripType = "bus",
                        UsageCount = 1
                    });
                }

                BusCities = cities;
                _validCityIds = validIds;
                _cityIdToName = idToName;
                _cityNameToId = nameToId;

                _logger.LogInformation($"Loaded {BusCities.Count} Bus City Codes from Database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load bus city code cache from database.");
            }
        }

        public bool IsValidCity(long cityId)
        {
            return cityId > 0 && _validCityIds.Contains(cityId);
        }

        public string MapCityCodeToName(string cityCode)
        {
            if (string.IsNullOrWhiteSpace(cityCode)) return cityCode;
            if (long.TryParse(cityCode, out var cid) && _cityIdToName.TryGetValue(cid, out var name))
            {
                return name;
            }
            return cityCode;
        }

        public string MapCityNameToCode(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName)) return cityName;
            if (long.TryParse(cityName, out _)) return cityName;
            if (_cityNameToId.TryGetValue(cityName, out var cid))
            {
                return cid.ToString();
            }
            return cityName;
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
