using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;
using PickNBook.Api.Utils;

namespace PickNBook.Api.Services.Implementations
{
    public class PlacesService : IPlacesService
    {
        private readonly AppDbContext _dbContext;
        private readonly IMemoryCache _cache;
        private readonly SrdvMasterDataSettings _settings;
        private readonly ILogger<PlacesService> _logger;

        public PlacesService(
            AppDbContext dbContext,
            IMemoryCache cache,
            IOptions<SrdvMasterDataSettings> settings,
            ILogger<PlacesService> logger)
        {
            _dbContext = dbContext;
            _cache = cache;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<List<PlaceSuggestionDto>> GetPlacesAsync(
            string? query,
            string tripType = "all",
            string field = "all",
            string? requestType = null,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 100);
            var normalizedTripType = tripType.Trim().ToLowerInvariant();
            var normalizedField = field.Trim().ToLowerInvariant();
            var trimmedQuery = query?.Trim() ?? string.Empty;
            var queryLower = trimmedQuery.ToLowerInvariant();

            var cacheKey = $"places:{normalizedTripType}:{requestType?.ToLowerInvariant() ?? "all"}:{normalizedField}:{queryLower}:{limit}";
            if (_cache.TryGetValue(cacheKey, out List<PlaceSuggestionDto>? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            var candidateLimit = Math.Max(_settings.CandidateLookupLimit, limit * 3);
            var cityCandidates = new List<PlaceSuggestionDto>();

            // 1. Bus Popularity Stats
            var busPlacePopularity = await GetBusPopularityAsync(cancellationToken);

            // 2. Query Flight Airports from DB
            if (normalizedTripType is "all" or "flight")
            {
                var airportQuery = _dbContext.Airports.AsNoTracking().Where(a => a.IsActive);

                if (!string.IsNullOrWhiteSpace(queryLower))
                {
                    airportQuery = airportQuery.Where(a =>
                        a.IataCode == queryLower ||
                        a.CityName.StartsWith(queryLower) ||
                        a.CityName.Contains(queryLower) ||
                        a.AirportName.StartsWith(queryLower) ||
                        a.AirportName.Contains(queryLower));
                }

                var airports = await airportQuery
                    .OrderBy(a => a.CityName)
                    .Take(candidateLimit)
                    .Select(a => new PlaceSuggestionDto
                    {
                        CityName = a.CityName,
                        AirportCode = a.IataCode,
                        AirportName = a.AirportName,
                        CityCode = a.CityCode ?? a.IataCode,
                        CountryCode = a.CountryCode ?? "",
                        CountryName = a.CountryName ?? "",
                        TripType = "flight",
                        UsageCount = 1
                    })
                    .ToListAsync(cancellationToken);

                cityCandidates.AddRange(airports);
            }

            // 3. Query Hotel Cities from DB
            if (normalizedTripType is "hotel")
            {
                var hotelQuery = _dbContext.HotelCities.AsNoTracking().Where(h => h.IsActive);

                if (!string.IsNullOrWhiteSpace(requestType))
                {
                    hotelQuery = hotelQuery.Where(h => h.RequestType == requestType);
                }

                if (!string.IsNullOrWhiteSpace(queryLower))
                {
                    hotelQuery = hotelQuery.Where(h =>
                        h.CityCode == queryLower ||
                        h.CityName.StartsWith(queryLower) ||
                        h.CityName.Contains(queryLower));
                }

                var hotelCities = await hotelQuery
                    .OrderBy(h => h.CityName)
                    .Take(candidateLimit)
                    .Select(h => new PlaceSuggestionDto
                    {
                        CityName = h.CityName,
                        CityId = h.CityCode,
                        CityCode = h.CityCode,
                        CountryCode = h.CountryCode ?? "",
                        CountryName = h.CountryName ?? "",
                        TripType = "hotel",
                        UsageCount = 1
                    })
                    .ToListAsync(cancellationToken);

                cityCandidates.AddRange(hotelCities);
            }

            // 4. Query Bus Cities from DB
            if (normalizedTripType is "all" or "bus")
            {
                var busQuery = _dbContext.BusCities.AsNoTracking().Where(b => b.IsActive);

                if (!string.IsNullOrWhiteSpace(queryLower))
                {
                    busQuery = busQuery.Where(b =>
                        b.CityCode == queryLower ||
                        b.CityName.StartsWith(queryLower) ||
                        b.CityName.Contains(queryLower));
                }

                var busCities = await busQuery
                    .OrderBy(b => b.CityName)
                    .Take(candidateLimit)
                    .Select(b => new PlaceSuggestionDto
                    {
                        CityName = b.CityName,
                        CityCode = b.CityCode,
                        StateName = b.StateName,
                        CountryCode = b.CountryCode ?? "IN",
                        CountryName = b.CountryName ?? "India",
                        TripType = "bus",
                        UsageCount = 1
                    })
                    .ToListAsync(cancellationToken);

                cityCandidates.AddRange(busCities);
            }

            // Assign Bus Popularity counts
            foreach (var city in cityCandidates.Where(c => c.TripType == "bus"))
            {
                if (busPlacePopularity.TryGetValue(city.CityName, out var count))
                {
                    city.UsageCount = (int)count;
                }
            }

            // 5. Apply Fuzzy Matching & Ranking on Bounded Candidate Set
            if (!string.IsNullOrWhiteSpace(queryLower))
            {
                cityCandidates = RankAndFilterCandidates(cityCandidates, queryLower);
            }

            // 6. Group & Format Results based on Trip Type
            List<PlaceSuggestionDto> response;

            if (normalizedTripType is "hotel")
            {
                response = cityCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x.CityName))
                    .GroupBy(x => x.CityId ?? x.CityName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }
            else if (normalizedTripType is "flight")
            {
                response = cityCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x.CityName))
                    .GroupBy(x => x.AirportCode ?? x.CityName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }
            else if (normalizedTripType is "bus")
            {
                response = cityCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x.CityName))
                    .GroupBy(x => x.CityCode ?? x.CityName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.UsageCount).First())
                    .OrderByDescending(x =>
                        !string.IsNullOrWhiteSpace(queryLower) &&
                        x.CityName.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(x => x.UsageCount)
                    .ThenBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }
            else
            {
                response = cityCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x.CityName))
                    .GroupBy(x => x.CityName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new PlaceSuggestionDto
                    {
                        CityName = g.OrderBy(x => x.CityName).First().CityName,
                        UsageCount = g.Sum(x => x.UsageCount),
                        AirportCode = g.First().AirportCode,
                        AirportName = g.First().AirportName,
                        CityCode = g.First().CityCode,
                        CityId = g.First().CityId,
                        CountryCode = g.First().CountryCode,
                        CountryName = g.First().CountryName,
                        TripType = g.First().TripType
                    })
                    .OrderByDescending(x => x.UsageCount)
                    .ThenBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }

            // Cache for configured minutes
            _cache.Set(cacheKey, response, TimeSpan.FromMinutes(_settings.CacheExpirationMinutes));

            return response;
        }

        private List<PlaceSuggestionDto> RankAndFilterCandidates(List<PlaceSuggestionDto> candidates, string queryLower)
        {
            var scoredCandidates = new List<(PlaceSuggestionDto Item, int Score, int Distance)>();

            foreach (var item in candidates)
            {
                var nameLower = item.CityName.ToLowerInvariant();
                int score = 0;
                int distance = 999;

                // Exact match on code/IATA
                if (string.Equals(item.AirportCode, queryLower, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.CityCode, queryLower, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.CityId, queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                }
                else if (nameLower.Equals(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 90;
                }
                else if (nameLower.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 70;
                }
                else if (nameLower.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 50;
                }
                else if (!string.IsNullOrEmpty(item.AirportName) && item.AirportName.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 45;
                }
                else
                {
                    // Existing Levenshtein fuzzy distance fallback (allow up to 2 typos)
                    distance = FuzzyMatcher.ComputeLevenshteinDistance(queryLower, nameLower);
                    if (distance <= 2)
                    {
                        score += (distance == 1 ? 30 : 15);
                    }
                }

                if (score > 0)
                {
                    // Bus Popularity boost
                    if (item.TripType == "bus" && item.UsageCount > 0)
                    {
                        score += Math.Min(item.UsageCount, 50);
                    }

                    scoredCandidates.Add((item, score, distance));
                }
            }

            return scoredCandidates
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Distance)
                .ThenByDescending(x => x.Item.UsageCount)
                .ThenBy(x => x.Item.CityName)
                .Select(x => x.Item)
                .ToList();
        }

        private async Task<Dictionary<string, long>> GetBusPopularityAsync(CancellationToken cancellationToken)
        {
            const string cacheKey = "places:bus_popularity_map";
            if (_cache.TryGetValue(cacheKey, out Dictionary<string, long>? cached) && cached != null)
            {
                return cached;
            }

            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var placeStats = await _dbContext.PlaceSearchStats
                    .AsNoTracking()
                    .Where(x => x.TripType == "bus")
                    .ToListAsync(cancellationToken);

                foreach (var stat in placeStats)
                {
                    map[stat.CityName] = stat.SelectionCount;
                }

                _cache.Set(cacheKey, map, TimeSpan.FromMinutes(10));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load bus place search stats.");
            }

            return map;
        }
    }
}
