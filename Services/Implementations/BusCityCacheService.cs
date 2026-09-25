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
        private List<CachedBusCity> _cachedCities = new();
        private HashSet<long> _validCityIds = new();
        private Dictionary<long, string> _cityIdToName = new();
        private Dictionary<string, long> _cityNameToId = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<long, List<long>> _cityAliases = new();

        private class CachedBusCity
        {
            public PlaceSuggestionDto Dto { get; set; } = null!;
            public string CityCode { get; set; } = string.Empty;
            public string CityNameLower { get; set; } = string.Empty;
            public string BaseCityNameLower { get; set; } = string.Empty;
            public string NormalizedBaseCityLower { get; set; } = string.Empty;
            public string? SubAreaLower { get; set; }
            public string StateNameLower { get; set; } = string.Empty;
            public string[] SearchTokens { get; set; } = Array.Empty<string>();
            public bool IsCity { get; set; }
            public long? ParentCityId { get; set; }
            public string? ParentCityName { get; set; }
            public int ChildStopCount { get; set; }
        }

        private static readonly Dictionary<long, List<long>> StaticMetroClusters = new()
        {
            // Hyderabad (9) <-> Secunderabad (26130, 6216)
            { 9, new List<long> { 26130, 6216 } },
            { 26130, new List<long> { 9, 6216 } },
            { 6216, new List<long> { 9, 26130 } },

            // Anantapur (7) <-> Ananthapur (Sagara) (22064)
            { 7, new List<long> { 22064 } },
            { 22064, new List<long> { 7 } },

            // Bangalore (4) <-> Hosur (1625)
            { 4, new List<long> { 1625 } },
            { 1625, new List<long> { 4 } },

            // Hubli (1501) <-> Dharwad (1180)
            { 1501, new List<long> { 1180 } },
            { 1180, new List<long> { 1501 } },

            // Delhi (2754) <-> Noida (157) <-> Gurgaon (158)
            { 2754, new List<long> { 157, 158 } },
            { 157, new List<long> { 2754, 158 } },
            { 158, new List<long> { 2754, 157 } }
        };

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
                var cached = new List<CachedBusCity>(dbCities.Count);
                var validIds = new HashSet<long>(dbCities.Count);
                var idToName = new Dictionary<long, string>(dbCities.Count);
                var nameToId = new Dictionary<string, long>(dbCities.Count * 2, StringComparer.OrdinalIgnoreCase);
                var clusterMap = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
                var childCounts = new Dictionary<long, int>();
                var cityIdToEntity = dbCities.ToDictionary(c => c.CityId, c => c);

                foreach (var c in dbCities)
                {
                    if (c.ParentCityId != null && c.ParentCityId > 0)
                    {
                        childCounts[c.ParentCityId.Value] = childCounts.GetValueOrDefault(c.ParentCityId.Value, 0) + 1;
                    }
                }

                foreach (var c in dbCities)
                {
                    validIds.Add(c.CityId);
                    idToName[c.CityId] = c.CityName;
                    
                    string fullName = c.CityName?.Trim() ?? string.Empty;
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

                    var tokens = Tokenize($"{fullName} {c.StateName}");

                    string? parentName = null;
                    if (c.ParentCityId != null && c.ParentCityId > 0 && cityIdToEntity.TryGetValue(c.ParentCityId.Value, out var parentEntity))
                    {
                        parentName = parentEntity.CityName?.Split('(')[0].Trim();
                    }

                    int childCount = childCounts.GetValueOrDefault(c.CityId, 0);
                    string searchCityId = (c.ParentCityId != null && c.ParentCityId > 0) ? c.ParentCityId.Value.ToString() : c.CityId.ToString();

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

                        // Cluster main CITY entities sharing the normalized phonetic name and state
                        if (string.Equals(c.Type, "CITY", StringComparison.OrdinalIgnoreCase) || c.ParentCityId == null || c.ParentCityId == 0)
                        {
                            var normalizedClean = NormalizePhonetic(cleanName);
                            var clusterKey = $"{normalizedClean}|{c.StateName}".Trim().ToLowerInvariant();
                            if (!clusterMap.TryGetValue(clusterKey, out var aliasList))
                            {
                                aliasList = new List<long>();
                                clusterMap[clusterKey] = aliasList;
                            }
                            if (!aliasList.Contains(c.CityId))
                            {
                                aliasList.Add(c.CityId);
                            }
                        }
                    }

                    var dto = new PlaceSuggestionDto
                    {
                        CityName = fullName,
                        BaseCityName = baseCityName,
                        SubArea = subArea,
                        DisplayName = !string.IsNullOrWhiteSpace(c.StateName) ? $"{fullName}, {c.StateName}" : fullName,
                        CityCode = c.CityId.ToString(),
                        CityId = c.CityId.ToString(),
                        LocationType = c.Type ?? "CITY",
                        ParentCityId = c.ParentCityId,
                        ParentCityName = parentName,
                        SearchCityId = searchCityId,
                        ChildStopCount = childCount,
                        StateName = c.StateName,
                        CountryCode = c.CountryCode ?? "IN",
                        CountryName = c.CountryName ?? "India",
                        TripType = "bus",
                        UsageCount = 1
                    };

                    cities.Add(dto);

                    cached.Add(new CachedBusCity
                    {
                        Dto = dto,
                        CityCode = c.CityId.ToString(),
                        CityNameLower = fullName.ToLowerInvariant(),
                        BaseCityNameLower = baseCityName.ToLowerInvariant(),
                        NormalizedBaseCityLower = NormalizePhonetic(baseCityName),
                        SubAreaLower = subArea?.ToLowerInvariant(),
                        StateNameLower = (c.StateName ?? string.Empty).ToLowerInvariant(),
                        SearchTokens = tokens,
                        IsCity = string.Equals(c.Type, "CITY", StringComparison.OrdinalIgnoreCase) || c.ParentCityId == null || c.ParentCityId == 0,
                        ParentCityId = c.ParentCityId,
                        ParentCityName = parentName,
                        ChildStopCount = childCount
                    });
                }

                // Build aliases dictionary
                var aliases = new Dictionary<long, List<long>>();

                // 1. Reciprocal aliases for phonetic clusters within same state
                foreach (var group in clusterMap.Values)
                {
                    if (group.Count > 1)
                    {
                        foreach (var id in group)
                        {
                            if (!aliases.TryGetValue(id, out var list))
                            {
                                list = new List<long>();
                                aliases[id] = list;
                            }
                            foreach (var peer in group)
                            {
                                if (!list.Contains(peer)) list.Add(peer);
                            }
                        }
                    }
                }

                // 2. Child AREA -> Parent Hub one-way alias expansion (Guardrail 1: Child expands to parent, parent does NOT fan out to 50 stops)
                foreach (var c in dbCities)
                {
                    if (c.ParentCityId != null && c.ParentCityId > 0 && validIds.Contains(c.ParentCityId.Value))
                    {
                        if (!aliases.TryGetValue(c.CityId, out var list))
                        {
                            list = new List<long> { c.CityId };
                            aliases[c.CityId] = list;
                        }
                        if (!list.Contains(c.ParentCityId.Value))
                        {
                            list.Add(c.ParentCityId.Value);
                        }
                    }
                }

                // 3. Static Metro / Twin Hubs
                foreach (var kvp in StaticMetroClusters)
                {
                    if (!aliases.TryGetValue(kvp.Key, out var list))
                    {
                        list = new List<long> { kvp.Key };
                        aliases[kvp.Key] = list;
                    }
                    foreach (var target in kvp.Value)
                    {
                        if (validIds.Contains(target) && !list.Contains(target))
                        {
                            list.Add(target);
                        }
                    }
                }

                BusCities = cities;
                _cachedCities = cached;
                _validCityIds = validIds;
                _cityIdToName = idToName;
                _cityNameToId = nameToId;
                _cityAliases = aliases;

                _logger.LogInformation($"Loaded {BusCities.Count} Bus City Codes and {_cityAliases.Count} aliased cluster IDs from Database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load bus city code cache from database.");
            }
        }

        public static string NormalizePhonetic(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var lower = text.Trim().ToLowerInvariant();
            return lower
                .Replace("th", "t")
                .Replace("oo", "u")
                .Replace("ee", "i")
                .Replace("w", "v")
                .Replace(" ", "")
                .Replace("-", "")
                .Replace(".", "");
        }

        private static string[] Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            return text
                .Split(new[] { ' ', '(', ')', ',', '-', '/', '.', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct()
                .ToArray();
        }

        public IReadOnlyList<long> GetCityAliases(long cityId)
        {
            if (_cityAliases.TryGetValue(cityId, out var aliases) && aliases.Count > 0)
            {
                return aliases;
            }
            return new[] { cityId };
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

        public List<PlaceSuggestionDto> SearchBusCities(
            string? query,
            IReadOnlyDictionary<string, long>? popularityMap = null,
            int limit = 50)
        {
            limit = Math.Clamp(limit, 1, 100);

            if (string.IsNullOrWhiteSpace(query))
            {
                return _cachedCities
                    .Select(c => {
                        var usage = popularityMap != null && popularityMap.TryGetValue(c.Dto.CityName, out var pop) ? (int)pop : 1;
                        return new PlaceSuggestionDto
                        {
                            CityName = c.Dto.CityName,
                            BaseCityName = c.Dto.BaseCityName,
                            SubArea = c.Dto.SubArea,
                            DisplayName = c.Dto.DisplayName,
                            CityCode = c.Dto.CityCode,
                            CityId = c.Dto.CityId,
                            LocationType = c.Dto.LocationType,
                            ParentCityId = c.Dto.ParentCityId,
                            ParentCityName = c.Dto.ParentCityName,
                            SearchCityId = c.Dto.SearchCityId,
                            ChildStopCount = c.Dto.ChildStopCount,
                            StateName = c.Dto.StateName,
                            CountryCode = c.Dto.CountryCode,
                            CountryName = c.Dto.CountryName,
                            TripType = "bus",
                            UsageCount = usage,
                            MatchTier = 1
                        };
                    })
                    .OrderByDescending(x => x.UsageCount)
                    .ThenBy(x => (string.IsNullOrEmpty(x.SubArea) || string.Equals(x.SubArea, x.StateName, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
                    .ThenByDescending(x => x.ChildStopCount)
                    .ThenByDescending(x => string.Equals(x.LocationType, "CITY", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenBy(x => x.CityName.Length)
                    .ThenBy(x => x.CityName)
                    .Take(limit)
                    .ToList();
            }

            var trimmed = query.Trim();
            var queryLower = trimmed.ToLowerInvariant();
            var queryPhonetic = NormalizePhonetic(trimmed);
            var queryTokens = Tokenize(queryLower);
            var matches = new List<PlaceSuggestionDto>();

            foreach (var item in _cachedCities)
            {
                int tier = 999;

                // Tier 1: Exact match on CityCode or full CityName
                if (item.CityCode == queryLower || item.CityNameLower == queryLower)
                {
                    tier = 1;
                }
                // Tier 2: Base city exact match (raw or phonetic transliteration, e.g. "ananthapur" matches "Anantapur")
                else if (item.BaseCityNameLower == queryLower || (!string.IsNullOrEmpty(queryPhonetic) && item.NormalizedBaseCityLower == queryPhonetic))
                {
                    tier = 2;
                }
                // Tier 3: Base city prefix match (raw or phonetic)
                else if (item.BaseCityNameLower.StartsWith(queryLower) || (!string.IsNullOrEmpty(queryPhonetic) && item.NormalizedBaseCityLower.StartsWith(queryPhonetic)))
                {
                    tier = 3;
                }
                // Tier 4: Qualifier / Parenthesis exact or prefix match (e.g. "sagara" or "sag" -> "Ananthapur (Sagara)")
                else if (!string.IsNullOrEmpty(item.SubAreaLower) &&
                         (item.SubAreaLower == queryLower || item.SubAreaLower.StartsWith(queryLower)))
                {
                    tier = 4;
                }
                // Tier 5: Multi-token match across full name + state
                else if (queryTokens.Length > 1 && queryTokens.All(qt => item.SearchTokens.Any(st => st.StartsWith(qt))))
                {
                    tier = 5;
                }
                // Tier 6: Substring match in CityName or StateName (for queries >= 2 chars)
                else if (queryLower.Length >= 2 && (item.CityNameLower.Contains(queryLower) || item.StateNameLower.Contains(queryLower)))
                {
                    tier = 6;
                }
                // Tier 7: Fuzzy match (Levenshtein distance <= 2, queries >= 3 chars)
                else if (queryLower.Length >= 3)
                {
                    var distBase = FuzzyMatcher.ComputeLevenshteinDistance(queryLower, item.BaseCityNameLower);
                    if (distBase <= 2)
                    {
                        tier = 7;
                    }
                    else if (!string.IsNullOrEmpty(item.SubAreaLower))
                    {
                        var distSub = FuzzyMatcher.ComputeLevenshteinDistance(queryLower, item.SubAreaLower);
                        if (distSub <= 2)
                        {
                            tier = 7;
                        }
                    }
                }

                if (tier <= 7)
                {
                    var usage = popularityMap != null && popularityMap.TryGetValue(item.Dto.CityName, out var pop) ? (int)pop : 1;
                    matches.Add(new PlaceSuggestionDto
                    {
                        CityName = item.Dto.CityName,
                        BaseCityName = item.Dto.BaseCityName,
                        SubArea = item.Dto.SubArea,
                        DisplayName = item.Dto.DisplayName,
                        CityCode = item.Dto.CityCode,
                        CityId = item.Dto.CityId,
                        LocationType = item.Dto.LocationType,
                        ParentCityId = item.Dto.ParentCityId,
                        ParentCityName = item.Dto.ParentCityName,
                        SearchCityId = item.Dto.SearchCityId,
                        ChildStopCount = item.Dto.ChildStopCount,
                        StateName = item.Dto.StateName,
                        CountryCode = item.Dto.CountryCode,
                        CountryName = item.Dto.CountryName,
                        TripType = "bus",
                        UsageCount = usage,
                        MatchTier = tier
                    });
                }
            }

            return matches
                .OrderBy(x => x.MatchTier)
                .ThenByDescending(x => x.UsageCount)
                // Primary Hub Priority: A pure city or state-qualified city ranks above a localized sub-area (e.g. Anantapur > Ananthapur (Sagara))
                .ThenBy(x => (string.IsNullOrEmpty(x.SubArea) || string.Equals(x.SubArea, x.StateName, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
                // Major District Hub Priority: Cities with child boarding stops rank above 0-stop locations
                .ThenByDescending(x => x.ChildStopCount)
                .ThenByDescending(x => string.Equals(x.LocationType, "CITY", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(x => x.CityName.Length)
                .ThenBy(x => x.CityName)
                .Take(limit)
                .ToList();
        }

        public List<PlaceSuggestionDto> SearchCities(string query, int limit = 20)
        {
            return SearchBusCities(query, null, limit);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
