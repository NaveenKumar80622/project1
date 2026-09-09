using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class AirlineLookupService : IAirlineLookupService
    {
        private const string CacheMapKey = "cache:flight:airlines:map";
        private const string CacheListKey = "cache:flight:airlines:list";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        private readonly AppDbContext _dbContext;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AirlineLookupService> _logger;

        public AirlineLookupService(
            AppDbContext dbContext,
            IMemoryCache cache,
            ILogger<AirlineLookupService> logger)
        {
            _dbContext = dbContext;
            _cache = cache;
            _logger = logger;
        }

        public string GetAirlineName(string airlineCode, string? fallback = null)
        {
            if (string.IsNullOrWhiteSpace(airlineCode))
            {
                return fallback ?? string.Empty;
            }

            var cleanCode = airlineCode.Trim().ToUpperInvariant();

            if (_cache.TryGetValue(CacheMapKey, out Dictionary<string, string>? map) && map != null)
            {
                if (map.TryGetValue(cleanCode, out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
                return !string.IsNullOrWhiteSpace(fallback) ? fallback : cleanCode;
            }

            // If cache not yet initialized, load from DB
            try
            {
                var loadedMap = LoadAirlineMapFromDb();
                _cache.Set(CacheMapKey, loadedMap, CacheDuration);

                if (loadedMap.TryGetValue(cleanCode, out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load airline map from database on demand. Using fallback for {AirlineCode}", cleanCode);
            }

            return !string.IsNullOrWhiteSpace(fallback) ? fallback : cleanCode;
        }

        public async Task<string> GetAirlineNameAsync(string airlineCode, string? fallback = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(airlineCode))
            {
                return fallback ?? string.Empty;
            }

            var cleanCode = airlineCode.Trim().ToUpperInvariant();

            var map = await GetOrLoadAirlineMapAsync(cancellationToken);
            if (map.TryGetValue(cleanCode, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return !string.IsNullOrWhiteSpace(fallback) ? fallback : cleanCode;
        }

        public async Task<IReadOnlyList<FlightAirlineDto>> GetAllAirlinesAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheListKey, out IReadOnlyList<FlightAirlineDto>? cachedList) && cachedList != null)
            {
                return cachedList;
            }

            var list = await _dbContext.FlightAirlines
                .AsNoTracking()
                .OrderBy(x => x.AirlineName)
                .Select(x => new FlightAirlineDto
                {
                    AirlineCode = x.AirlineCode,
                    AirlineName = x.AirlineName
                })
                .ToListAsync(cancellationToken);

            _cache.Set(CacheListKey, list, CacheDuration);
            return list;
        }

        public void InvalidateCache()
        {
            _cache.Remove(CacheMapKey);
            _cache.Remove(CacheListKey);
            _logger.LogInformation("Invalidated in-memory flight airlines lookup cache.");
        }

        private async Task<Dictionary<string, string>> GetOrLoadAirlineMapAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue(CacheMapKey, out Dictionary<string, string>? cachedMap) && cachedMap != null)
            {
                return cachedMap;
            }

            var airlines = await _dbContext.FlightAirlines
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in airlines)
            {
                if (!string.IsNullOrWhiteSpace(a.AirlineCode))
                {
                    map[a.AirlineCode.Trim().ToUpperInvariant()] = a.AirlineName?.Trim() ?? a.AirlineCode.Trim();
                }
            }

            _cache.Set(CacheMapKey, map, CacheDuration);
            return map;
        }

        private Dictionary<string, string> LoadAirlineMapFromDb()
        {
            var airlines = _dbContext.FlightAirlines
                .AsNoTracking()
                .ToList();

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in airlines)
            {
                if (!string.IsNullOrWhiteSpace(a.AirlineCode))
                {
                    map[a.AirlineCode.Trim().ToUpperInvariant()] = a.AirlineName?.Trim() ?? a.AirlineCode.Trim();
                }
            }

            return map;
        }
    }
}
