using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using PickNBook.Api.Utils;
using System.Text.Json;
using System.IO;

namespace PickNBook.Api.Controllers
{
    public class PlacesController(AppDbContext dbContext, IWebHostEnvironment env, HotelCityCacheService hotelCityCache, BusCityCacheService busCityCache) : BaseApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetPlaces(
            [FromQuery] string? query,
            [FromQuery] string tripType = "all",
            [FromQuery] string field = "all",
            [FromQuery] int limit = 20)
        {
            if (limit <= 0)
            {
                return BadRequest("limit must be greater than 0.");
            }

            limit = Math.Min(limit, 100);

            var normalizedTripType = tripType.Trim().ToLowerInvariant();
            if (normalizedTripType is not ("all" or "flight" or "bus" or "hotel"))
            {
                return BadRequest("tripType must be one of: all, flight, bus, hotel.");
            }

            var normalizedField = field.Trim().ToLowerInvariant();
            if (normalizedField is not ("all" or "from" or "to"))
            {
                return BadRequest("field must be one of: all, from, to.");
            }

            var cityCandidates = new List<PlaceSuggestionDto>();

            // ===== BUILD BUS POPULARITY LOOKUP =====
            Dictionary<string, long> busPlacePopularity = new(StringComparer.OrdinalIgnoreCase);
            if (normalizedTripType is "all" or "bus")
            {
                var placeStats = await dbContext.PlaceSearchStats
                    .AsNoTracking()
                    .Where(x => x.TripType == "bus")
                    .ToListAsync();

                foreach (var stat in placeStats)
                {
                    busPlacePopularity[stat.CityName] = stat.SelectionCount;
                }
            }

            // ===== FLIGHT BRANCH =====
            if (normalizedTripType is "all" or "flight")
            {
                var airports = LoadFlightAirports();
                if (airports.Count > 0)
                {
                    cityCandidates.AddRange(airports);
                }
                else
                {
                    // Fallback to DB-based suggestions (legacy behavior)
                    if (normalizedField is "all" or "from")
                    {
                        var fromCities = await dbContext.FlightReservations
                            .AsNoTracking()
                            .Select(x => x.FromCity)
                            .ToListAsync();
                        cityCandidates.AddRange(fromCities.Select(x => new PlaceSuggestionDto
                        {
                            CityName = x,
                            UsageCount = 1,
                            TripType = "flight"
                        }));
                    }

                    if (normalizedField is "all" or "to")
                    {
                        var toCities = await dbContext.FlightReservations
                            .AsNoTracking()
                            .Select(x => x.ToCity)
                            .ToListAsync();
                        cityCandidates.AddRange(toCities.Select(x => new PlaceSuggestionDto
                        {
                            CityName = x,
                            UsageCount = 1,
                            TripType = "flight"
                        }));
                    }
                }
            }

            // ===== HOTEL BRANCH =====
            if (normalizedTripType is "hotel")
            {
                var hotelCities = LoadHotelCities();
                cityCandidates.AddRange(hotelCities);
            }

            // ===== BUS BRANCH =====
            if (normalizedTripType is "all" or "bus")
            {
                var busCities = busCityCache.BusCities;
                if (busCities.Count > 0)
                {
                    cityCandidates.AddRange(busCities);
                }
                else
                {
                    // Fallback to DB-based suggestions
                    if (normalizedField is "all" or "from")
                    {
                        var fromCities = await dbContext.BusBookings
                            .AsNoTracking()
                            .Select(x => x.FromCity)
                            .ToListAsync();
                        cityCandidates.AddRange(fromCities.Select(x => new PlaceSuggestionDto
                        {
                            CityName = x,
                            UsageCount = 1,
                            TripType = "bus"
                        }));
                    }

                    if (normalizedField is "all" or "to")
                    {
                        var toCities = await dbContext.BusBookings
                            .AsNoTracking()
                            .Select(x => x.ToCity)
                            .ToListAsync();
                        cityCandidates.AddRange(toCities.Select(x => new PlaceSuggestionDto
                        {
                            CityName = x,
                            UsageCount = 1,
                            TripType = "bus"
                        }));
                    }
                }
            }

            // ===== ASSIGN BUS POPULARITY =====
            foreach (var city in cityCandidates.Where(c => c.TripType == "bus"))
            {
                if (busPlacePopularity.TryGetValue(city.CityName, out var count))
                {
                    city.UsageCount = (int)count;
                }
            }

            // ===== FILTER BY QUERY (WITH FUZZY MATCHING) =====
            if (!string.IsNullOrWhiteSpace(query))
            {
                var keyword = query.Trim().ToLowerInvariant();
                var exactOrContains = cityCandidates
                    .Where(x => x.CityName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (exactOrContains.Count > 0)
                {
                    cityCandidates = exactOrContains;
                }
                else
                {
                    // Fallback to fuzzy search if no direct match is found (allows up to 2 typos)
                    cityCandidates = cityCandidates
                        .Select(c => new { 
                            City = c, 
                            Distance = FuzzyMatcher.ComputeLevenshteinDistance(keyword, c.CityName.ToLowerInvariant()) 
                        })
                        .Where(x => x.Distance <= 2)
                        .OrderBy(x => x.Distance)
                        .Select(x => x.City)
                        .ToList();
                }
            }

            // ===== GROUP & RETURN =====
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
                        !string.IsNullOrWhiteSpace(query) &&
                        x.CityName.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
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

            return Ok(response);
        }

        private List<PlaceSuggestionDto> LoadFlightAirports()
        {
            try
            {
                // Use IWebHostEnvironment to correctly target the root folder (fixes 'null' issue)
                var baseDir = env.ContentRootPath;
                var filePath = Path.Combine(baseDir, "Data", "srdv_airport_list.json");
                
                if (!System.IO.File.Exists(filePath))
                {
                    // Fallback to legacy
                    filePath = Path.Combine(baseDir, "Data", "airport_cities_in.json");
                    if (!System.IO.File.Exists(filePath)) return new();
                    var legacyJson = System.IO.File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<List<PlaceSuggestionDto>>(legacyJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }

                var json = System.IO.File.ReadAllText(filePath);
                var airports = new List<PlaceSuggestionDto>();

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
                                    var airportCode = row.TryGetProperty("airport_code", out var codeProp) ? codeProp.GetString() : null;
                                    var airportName = row.TryGetProperty("airport_name", out var nameProp) ? nameProp.GetString() : null;
                                    var cityCode = row.TryGetProperty("airport_city_code", out var cityCodeProp) ? cityCodeProp.GetString() : null;
                                    var cityName = row.TryGetProperty("airport_city_name", out var cityNameProp) ? cityNameProp.GetString() : null;
                                    var countryCode = row.TryGetProperty("airport_country_code", out var ccProp) ? ccProp.GetString() : null;
                                    var countryName = row.TryGetProperty("airport_country_name", out var cnProp) ? cnProp.GetString() : null;

                                    if (!string.IsNullOrEmpty(airportCode) && !string.IsNullOrEmpty(cityName))
                                    {
                                        airports.Add(new PlaceSuggestionDto
                                        {
                                            CityName = cityName,
                                            AirportCode = airportCode,
                                            AirportName = airportName ?? "",
                                            CityCode = cityCode ?? airportCode,
                                            CountryCode = countryCode ?? "",
                                            CountryName = countryName ?? "",
                                            TripType = "flight",
                                            UsageCount = 1
                                        });
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                return airports;
            }
            catch
            {
                return new();
            }
        }

        private List<PlaceSuggestionDto> LoadHotelCities()
        {
            var cities = new List<PlaceSuggestionDto>();
            try
            {
                // Use IWebHostEnvironment to correctly target the root folder (fixes '[]' issue)
                var baseDir = env.ContentRootPath;
                var specialPath = Path.Combine(baseDir, "Data", "srdv_hotel_cities_special.json");
                if (System.IO.File.Exists(specialPath)) cities.AddRange(ParseHotelCityJson(specialPath));

                var intlPath = Path.Combine(baseDir, "Data", "srdv_hotel_cities_intl.json");
                if (System.IO.File.Exists(intlPath)) cities.AddRange(ParseHotelCityJson(intlPath));
            }
            catch { }

            return cities;
        }

        private static List<PlaceSuggestionDto> ParseHotelCityJson(string filePath)
        {
            var cities = new List<PlaceSuggestionDto>();
            try
            {
                var json = System.IO.File.ReadAllText(filePath);
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
                                    var destination = row.TryGetProperty("destination", out var destProp) ? destProp.GetString() : null;
                                    var cityId = row.TryGetProperty("cityid", out var idProp) ? idProp.GetString() : null;
                                    var country = row.TryGetProperty("country", out var countryProp) ? countryProp.GetString() : null;
                                    var countryCode = row.TryGetProperty("countrycode", out var ccProp) ? ccProp.GetString() : null;

                                    if (!string.IsNullOrEmpty(destination) && !string.IsNullOrEmpty(cityId))
                                    {
                                        cities.Add(new PlaceSuggestionDto
                                        {
                                            CityName = destination,
                                            CityId = cityId,
                                            CountryCode = countryCode ?? "",
                                            CountryName = country ?? "",
                                            TripType = "hotel",
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
            catch { }
            return cities;
        }
        [HttpPost("track")]
        public async Task<IActionResult> TrackPlaceSelection([FromBody] TrackPlaceRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CityName) || string.IsNullOrWhiteSpace(request.TripType))
                return BadRequest("CityName and TripType are required.");

            var existing = await dbContext.PlaceSearchStats
                .FirstOrDefaultAsync(x => x.CityName == request.CityName && x.TripType == request.TripType);

            if (existing != null)
            {
                existing.SelectionCount++;
                existing.LastSelectedAtUtc = DateTime.UtcNow;
            }
            else
            {
                dbContext.PlaceSearchStats.Add(new PlaceSearchStat
                {
                    CityName = request.CityName,
                    CityCode = request.CityCode,
                    TripType = request.TripType,
                    SelectionCount = 1,
                    LastSelectedAtUtc = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync();
            return Ok();
        }
    }
}
