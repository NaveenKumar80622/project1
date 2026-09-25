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
        public static readonly Dictionary<string, (string CityName, string CountryCode, string CountryName, string Description, string[] AirportCodes)> KnownMetroAirportClusters =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["GOA"] = ("Goa", "IN", "India", "All Airports (GOI, GOX)", new[] { "GOI", "GOX" }),
                ["LON"] = ("London", "GB", "United Kingdom", "All Airports (LHR, LGW, STN, LTN, LCY)", new[] { "LHR", "LGW", "STN", "LTN", "LCY" }),
                ["NYC"] = ("New York", "US", "United States", "All Airports (JFK, EWR, LGA)", new[] { "JFK", "EWR", "LGA" }),
                ["DXB"] = ("Dubai", "AE", "United Arab Emirates", "All Airports (DXB, DWC)", new[] { "DXB", "DWC" }),
                ["TYO"] = ("Tokyo", "JP", "Japan", "All Airports (HND, NRT)", new[] { "HND", "NRT" })
            };

        private readonly AppDbContext _dbContext;
        private readonly IMemoryCache _cache;
        private readonly SrdvMasterDataSettings _settings;
        private readonly ILogger<PlacesService> _logger;
        private readonly BusCityCacheService? _busCityCacheService;

        public PlacesService(
            AppDbContext dbContext,
            IMemoryCache cache,
            IOptions<SrdvMasterDataSettings> settings,
            ILogger<PlacesService> logger,
            BusCityCacheService? busCityCacheService = null)
        {
            _dbContext = dbContext;
            _cache = cache;
            _settings = settings.Value;
            _logger = logger;
            _busCityCacheService = busCityCacheService;
        }

        public async Task<List<PlaceSuggestionDto>> GetPlacesAsync(
            string? query,
            string tripType = "all",
            string field = "all",
            string? requestType = null,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 100);
            var normalizedTripType = tripType.Trim().ToLowerInvariant();
            var normalizedField = field.Trim().ToLowerInvariant();
            var trimmedQuery = query?.Trim() ?? string.Empty;
            var queryLower = trimmedQuery.ToLowerInvariant();

            var isSingleChar = queryLower.Length == 1;
            var cacheKey = isSingleChar
                ? $"places:alpha:{normalizedTripType}:{requestType?.ToLowerInvariant() ?? "all"}:{normalizedField}:{queryLower}:{limit}"
                : $"places:{normalizedTripType}:{requestType?.ToLowerInvariant() ?? "all"}:{normalizedField}:{queryLower}:{limit}";

            if (_cache.TryGetValue(cacheKey, out List<PlaceSuggestionDto>? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            var candidateLimit = Math.Max(_settings.CandidateLookupLimit, limit * 3);
            var cityCandidates = new List<PlaceSuggestionDto>();

            // 1. Bus Popularity Stats
            var busPlacePopularity = await GetBusPopularityAsync(cancellationToken);

            // 2. Query Flight Airports from DB (New SRDV Table)
            if (normalizedTripType is "all" or "flight")
            {
                var newAirportQuery = _dbContext.FlightAirports.AsNoTracking().Where(a => a.IsActive);

                if (!string.IsNullOrWhiteSpace(queryLower))
                {
                    if (queryLower.Length < 3)
                    {
                        // Single/double character: strict prefix match on CityName or AirportCode
                        var prefix = $"{queryLower}%";
                        newAirportQuery = newAirportQuery.Where(a =>
                            EF.Functions.Like(a.CityName, prefix) ||
                            EF.Functions.Like(a.AirportCode, prefix));
                    }
                    else
                    {
                        newAirportQuery = newAirportQuery.Where(a =>
                            a.AirportCode == queryLower ||
                            a.CityName.StartsWith(queryLower) ||
                            a.CityName.Contains(queryLower) ||
                            a.AirportName.StartsWith(queryLower) ||
                            a.AirportName.Contains(queryLower));
                    }
                }

                // Prioritize domestic Indian airports first, then alphabetical by CityName
                var flightAirports = await newAirportQuery
                    .OrderByDescending(a => a.CountryCode == "IN" ? 1 : 0)
                    .ThenBy(a => a.CityName)
                    .Take(candidateLimit)
                    .Select(a => new PlaceSuggestionDto
                    {
                        CityName = a.CityName,
                        AirportCode = a.AirportCode,
                        AirportName = a.AirportName,
                        CityCode = a.AirportCode,
                        CountryCode = a.CountryCode,
                        CountryName = a.CountryCode,
                        TripType = "flight",
                        UsageCount = 1
                    })
                    .ToListAsync(cancellationToken);

                cityCandidates.AddRange(flightAirports);

                // Inject Metro Area Clusters matching the query
                foreach (var (code, cluster) in KnownMetroAirportClusters)
                {
                    if (string.IsNullOrWhiteSpace(queryLower) ||
                        code.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase) ||
                        cluster.CityName.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase) ||
                        cluster.CityName.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                    {
                        cityCandidates.Add(new PlaceSuggestionDto
                        {
                            CityName = cluster.CityName,
                            AirportCode = code,
                            AirportName = cluster.Description,
                            CityCode = code,
                            CountryCode = cluster.CountryCode,
                            CountryName = cluster.CountryName,
                            TripType = "flight",
                            UsageCount = 1000
                        });
                    }
                }
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
                    if (queryLower.Length < 3)
                    {
                        // Single/double character: strict prefix match on CityName
                        var prefix = $"{queryLower}%";
                        hotelQuery = hotelQuery.Where(h =>
                            EF.Functions.Like(h.CityName, prefix) ||
                            h.CityCode == queryLower);
                    }
                    else
                    {
                        hotelQuery = hotelQuery.Where(h =>
                            h.CityCode == queryLower ||
                            h.CityName.StartsWith(queryLower) ||
                            h.CityName.Contains(queryLower) ||
                            h.FullName.Contains(queryLower) ||
                            (h.StateName != null && h.StateName.StartsWith(queryLower)) ||
                            (h.DistrictName != null && h.DistrictName.StartsWith(queryLower)));
                    }
                }

                var hotelCities = await hotelQuery
                    .OrderByDescending(h => h.CityName.StartsWith(queryLower) ? 1 : 0)
                    .ThenByDescending(h => h.HotelCount)
                    .ThenBy(h => h.CityName)
                    .Take(candidateLimit)
                    .Select(h => new PlaceSuggestionDto
                    {
                        CityName = h.CityName,
                        CityId = h.CityId > 0 ? h.CityId.ToString() : h.CityCode,
                        CityCode = h.CityCode,
                        CountryCode = h.CountryCode ?? "",
                        CountryName = h.CountryName ?? h.StateName ?? "",
                        StateName = h.StateName,
                        DistrictName = h.DistrictName,
                        FullName = h.FullName,
                        Type = h.Type,
                        HotelCount = h.HotelCount,
                        TripType = "hotel",
                        UsageCount = h.HotelCount
                    })
                    .ToListAsync(cancellationToken);

                cityCandidates.AddRange(hotelCities);
            }

            // 4. Query Bus Cities from In-Memory Cache or DB Fallback
            if (normalizedTripType is "all" or "bus")
            {
                List<PlaceSuggestionDto> busCities;

                if (_busCityCacheService != null && _busCityCacheService.BusCities.Count > 0)
                {
                    busCities = _busCityCacheService.SearchBusCities(trimmedQuery, busPlacePopularity, candidateLimit);
                }
                else
                {
                    var busQuery = _dbContext.BusCities.AsNoTracking().Where(b => b.IsActive);
                    var terms = queryLower.Split(new[] { ' ', '(', ')', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);

                    if (!string.IsNullOrWhiteSpace(queryLower))
                    {
                        if (queryLower.Length < 3)
                        {
                            // Single/double character: strict prefix match on CityName or CityCode, or qualifier
                            var prefix = $"{queryLower}%";
                            var parenPrefix = $"%({queryLower}%";
                            busQuery = busQuery.Where(b =>
                                EF.Functions.Like(b.CityName, prefix) ||
                                EF.Functions.Like(b.CityName, parenPrefix) ||
                                b.CityCode == queryLower);
                        }
                        else
                        {
                            foreach (var term in terms)
                            {
                                var pat = $"%{term}%";
                                busQuery = busQuery.Where(b =>
                                    b.CityCode == term ||
                                    EF.Functions.Like(b.CityName, pat) ||
                                    (b.StateName != null && EF.Functions.Like(b.StateName, pat)));
                            }
                        }
                    }

                    var rawBusCities = await busQuery
                        .OrderBy(b => b.CityName)
                        .Take(candidateLimit)
                        .ToListAsync(cancellationToken);

                    busCities = rawBusCities.Select(b => {
                        string fullName = b.CityName?.Trim() ?? string.Empty;
                        string baseCityName = fullName;
                        string? subArea = null;

                        var parenOpen = fullName.IndexOf('(');
                        var parenClose = fullName.IndexOf(')', parenOpen > 0 ? parenOpen : 0);
                        if (parenOpen > 0 && parenClose > parenOpen)
                        {
                            baseCityName = fullName.Substring(0, parenOpen).Trim();
                            subArea = fullName.Substring(parenOpen + 1, parenClose - parenOpen - 1).Trim();
                        }
                        else if (fullName.Contains(','))
                        {
                            var commaIdx = fullName.IndexOf(',');
                            baseCityName = fullName.Substring(0, commaIdx).Trim();
                            subArea = fullName.Substring(commaIdx + 1).Trim();
                        }

                        int tier = 999;
                        var fullNameLower = fullName.ToLowerInvariant();
                        var baseLower = baseCityName.ToLowerInvariant();
                        var subLower = subArea?.ToLowerInvariant();
                        var stateLower = (b.StateName ?? string.Empty).ToLowerInvariant();

                        if (b.CityCode == queryLower || fullNameLower == queryLower) tier = 1;
                        else if (baseLower == queryLower) tier = 2;
                        else if (baseLower.StartsWith(queryLower)) tier = 3;
                        else if (!string.IsNullOrEmpty(subLower) && (subLower == queryLower || subLower.StartsWith(queryLower))) tier = 4;
                        else if (terms.Length > 1 && terms.All(t => fullNameLower.Contains(t) || stateLower.Contains(t))) tier = 5;
                        else if (fullNameLower.Contains(queryLower) || stateLower.Contains(queryLower)) tier = 6;
                        else tier = 7;

                        return new PlaceSuggestionDto
                        {
                            CityName = fullName,
                            BaseCityName = baseCityName,
                            SubArea = subArea,
                            DisplayName = !string.IsNullOrWhiteSpace(b.StateName) ? $"{fullName}, {b.StateName}" : fullName,
                            CityCode = b.CityCode,
                            CityId = b.CityCode,
                            StateName = b.StateName,
                            CountryCode = b.CountryCode ?? "IN",
                            CountryName = b.CountryName ?? "India",
                            LocationType = b.Type ?? "CITY",
                            TripType = "bus",
                            UsageCount = 1,
                            MatchTier = tier
                        };
                    }).ToList();

                    // For short bus queries (< 3 chars), ensure any top-popularity cities starting with queryLower
                    // are guaranteed to be in cityCandidates even if alphabetical Take(candidateLimit) truncated them
                    if (queryLower.Length < 3 && busPlacePopularity.Count > 0)
                    {
                        var popularMatchingNames = busPlacePopularity
                            .Where(kvp => kvp.Key.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(kvp => kvp.Value)
                            .Take(candidateLimit)
                            .Select(kvp => kvp.Key)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var existingNames = busCities.Select(c => c.CityName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var missingNames = popularMatchingNames.Where(name => !existingNames.Contains(name)).ToList();

                        if (missingNames.Count > 0)
                        {
                            var missingCities = await _dbContext.BusCities.AsNoTracking()
                                .Where(b => b.IsActive && missingNames.Contains(b.CityName))
                                .ToListAsync(cancellationToken);

                            foreach (var b in missingCities)
                            {
                                string fullName = b.CityName?.Trim() ?? string.Empty;
                                string baseCityName = fullName;
                                string? subArea = null;

                                var parenOpen = fullName.IndexOf('(');
                                var parenClose = fullName.IndexOf(')', parenOpen > 0 ? parenOpen : 0);
                                if (parenOpen > 0 && parenClose > parenOpen)
                                {
                                    baseCityName = fullName.Substring(0, parenOpen).Trim();
                                    subArea = fullName.Substring(parenOpen + 1, parenClose - parenOpen - 1).Trim();
                                }
                                else if (fullName.Contains(','))
                                {
                                    var commaIdx = fullName.IndexOf(',');
                                    baseCityName = fullName.Substring(0, commaIdx).Trim();
                                    subArea = fullName.Substring(commaIdx + 1).Trim();
                                }

                                busCities.Add(new PlaceSuggestionDto
                                {
                                    CityName = fullName,
                                    BaseCityName = baseCityName,
                                    SubArea = subArea,
                                    DisplayName = !string.IsNullOrWhiteSpace(b.StateName) ? $"{fullName}, {b.StateName}" : fullName,
                                    CityCode = b.CityCode,
                                    CityId = b.CityCode,
                                    StateName = b.StateName,
                                    CountryCode = b.CountryCode ?? "IN",
                                    CountryName = b.CountryName ?? "India",
                                    LocationType = b.Type ?? "CITY",
                                    TripType = "bus",
                                    UsageCount = 1
                                });
                            }
                        }
                    }
                }

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
                    .GroupBy(x => $"{x.CityName.Trim()}|{x.StateName?.Trim()}|{x.CountryCode?.Trim()}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.HotelCount ?? 0).First())
                    .OrderByDescending(x =>
                        !string.IsNullOrWhiteSpace(queryLower) &&
                        x.CityName.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(x => x.HotelCount ?? 0)
                    .ThenBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }
            else if (normalizedTripType is "flight")
            {
                response = cityCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x.CityName))
                    .GroupBy(x => x.AirportCode ?? x.CityName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.UsageCount).First())
                    .OrderByDescending(x =>
                        !string.IsNullOrWhiteSpace(queryLower) &&
                        (string.Equals(x.AirportCode, queryLower, StringComparison.OrdinalIgnoreCase) ||
                         x.CityName.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
                    .ThenByDescending(x => string.Equals(x.CountryCode, "IN", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(x => x.UsageCount)
                    .ThenBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }
            else if (normalizedTripType is "bus")
            {
                response = cityCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x.CityName))
                    .GroupBy(x => x.CityCode ?? x.CityName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => {
                        var best = g.OrderBy(x => x.MatchTier).ThenByDescending(x => x.UsageCount).First();
                        return new PlaceSuggestionDto
                        {
                            CityName = best.CityName,
                            BaseCityName = best.BaseCityName,
                            SubArea = best.SubArea,
                            DisplayName = best.DisplayName ?? (!string.IsNullOrWhiteSpace(best.StateName) ? $"{best.CityName}, {best.StateName}" : best.CityName),
                            CityCode = best.CityCode,
                            CityId = best.CityId ?? best.CityCode,
                            LocationType = best.LocationType ?? "CITY",
                            ParentCityId = best.ParentCityId,
                            ParentCityName = best.ParentCityName,
                            SearchCityId = best.SearchCityId ?? best.CityId ?? best.CityCode,
                            ChildStopCount = best.ChildStopCount,
                            StateName = best.StateName,
                            CountryCode = best.CountryCode,
                            CountryName = best.CountryName,
                            TripType = "bus",
                            UsageCount = best.UsageCount,
                            MatchTier = best.MatchTier
                        };
                    })
                    .OrderBy(x => x.MatchTier)
                    .ThenByDescending(x => x.UsageCount)
                    .ThenBy(x => (string.IsNullOrEmpty(x.SubArea) || string.Equals(x.SubArea, x.StateName, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
                    .ThenByDescending(x => x.ChildStopCount)
                    .ThenByDescending(x => string.Equals(x.LocationType, "CITY", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenBy(x => x.CityName.Length)
                    .ThenBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }
            else
            {
                response = cityCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x.CityName))
                    .GroupBy(x => x.CityCode ?? x.CityName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => {
                        var best = g.OrderBy(x => x.MatchTier).ThenByDescending(x => x.UsageCount).First();
                        return new PlaceSuggestionDto
                        {
                            CityName = best.CityName,
                            BaseCityName = best.BaseCityName,
                            SubArea = best.SubArea,
                            DisplayName = best.DisplayName,
                            UsageCount = g.Sum(x => x.UsageCount),
                            AirportCode = best.AirportCode,
                            AirportName = best.AirportName,
                            CityCode = best.CityCode,
                            CityId = best.CityId,
                            CountryCode = best.CountryCode,
                            CountryName = best.CountryName,
                            TripType = best.TripType,
                            MatchTier = best.MatchTier
                        };
                    })
                    .OrderBy(x => x.MatchTier)
                    .ThenByDescending(x => x.UsageCount)
                    .ThenBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }

            // Cache for 24 hours if single-letter query, else configured minutes
            var cacheDuration = isSingleChar
                ? TimeSpan.FromHours(24)
                : TimeSpan.FromMinutes(_settings.CacheExpirationMinutes);
            _cache.Set(cacheKey, response, cacheDuration);

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
                    item.MatchTier = Math.Min(item.MatchTier, 1);
                }
                else if (nameLower.Equals(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 90;
                    item.MatchTier = Math.Min(item.MatchTier, 1);
                }
                else if (!string.IsNullOrEmpty(item.BaseCityName) && item.BaseCityName.Equals(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 85;
                    item.MatchTier = Math.Min(item.MatchTier, 2);
                }
                else if (nameLower.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase) ||
                         (!string.IsNullOrEmpty(item.BaseCityName) && item.BaseCityName.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 70;
                    item.MatchTier = Math.Min(item.MatchTier, 3);
                }
                else if (!string.IsNullOrEmpty(item.SubArea) && (item.SubArea.Equals(queryLower, StringComparison.OrdinalIgnoreCase) || item.SubArea.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 65;
                    item.MatchTier = Math.Min(item.MatchTier, 4);
                }
                else if (nameLower.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 50;
                    item.MatchTier = Math.Min(item.MatchTier, 6);
                }
                else if (!string.IsNullOrEmpty(item.AirportName) && item.AirportName.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 45;
                }
                else if (!string.IsNullOrEmpty(item.FullName) && item.FullName.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 40;
                }
                else if (!string.IsNullOrEmpty(item.StateName) && item.StateName.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 35;
                    item.MatchTier = Math.Min(item.MatchTier, 6);
                }
                else if (item.MatchTier <= 7)
                {
                    score += 100 - (item.MatchTier * 10);
                }
                else if (queryLower.Length >= 3)
                {
                    // Existing Levenshtein fuzzy distance fallback (allow up to 2 typos only for 3+ chars)
                    distance = FuzzyMatcher.ComputeLevenshteinDistance(queryLower, nameLower);
                    if (distance <= 2)
                    {
                        score += (distance == 1 ? 30 : 15);
                        item.MatchTier = Math.Min(item.MatchTier, 7);
                    }
                    else if (!string.IsNullOrEmpty(item.SubArea))
                    {
                        var distSub = FuzzyMatcher.ComputeLevenshteinDistance(queryLower, item.SubArea.ToLowerInvariant());
                        if (distSub <= 2)
                        {
                            score += (distSub == 1 ? 30 : 15);
                            item.MatchTier = Math.Min(item.MatchTier, 7);
                        }
                    }
                }

                if (score > 0)
                {
                    // Bus Popularity boost
                    if (item.TripType == "bus" && item.UsageCount > 0)
                    {
                        score += Math.Min(item.UsageCount, 50);
                    }
                    // Hotel Inventory boost
                    else if (item.TripType == "hotel" && item.HotelCount > 0)
                    {
                        score += Math.Min((int)(Math.Log10(item.HotelCount.Value + 1) * 15), 50);
                    }
                    // Flight Domestic Hub boost (e.g. DEL India before DUB Ireland for domestic searches)
                    else if (item.TripType == "flight" && string.Equals(item.CountryCode, "IN", StringComparison.OrdinalIgnoreCase))
                    {
                        score += (queryLower.Length < 3 ? 25 : 5);
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
