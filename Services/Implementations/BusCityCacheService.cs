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

        public BusCityCacheService(ILogger<BusCityCacheService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Loading SRDV Bus City Code Cache from database...");

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cities = await dbContext.BusCities
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.CityName)
                    .Select(c => new PlaceSuggestionDto
                    {
                        CityName = c.CityName,
                        CityCode = c.CityId.ToString(),
                        StateName = c.StateName,
                        CountryCode = c.CountryCode,
                        CountryName = c.CountryName,
                        TripType = "bus",
                        UsageCount = 1
                    })
                    .ToListAsync(cancellationToken);

                BusCities = cities;
                _logger.LogInformation($"Loaded {BusCities.Count} Bus City Codes from Database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load bus city code cache from database.");
            }
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
