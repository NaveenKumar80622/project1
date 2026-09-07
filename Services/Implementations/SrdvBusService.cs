using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Services
{
    public class SrdvBusService : ISrdvBusService
    {
        private readonly HttpClient _httpClient;
        private readonly SrdvSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly IServiceScopeFactory _scopeFactory;
        
        private string ClientId => !string.IsNullOrEmpty(_settings.BusClientId) ? _settings.BusClientId : _settings.ClientId;
        private string UserName => !string.IsNullOrEmpty(_settings.BusUserName) ? _settings.BusUserName : _settings.UserName;
        private string Password => !string.IsNullOrEmpty(_settings.BusPassword) ? _settings.BusPassword : _settings.Password;
        private string ApiToken => !string.IsNullOrEmpty(_settings.BusApiToken) ? _settings.BusApiToken : _settings.ApiToken;

        private string? _tokenId;
        private DateTime _tokenExpiry;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        private static Dictionary<string, string>? _cityMapping;
        private static List<BusCityDto>? _busCitiesList;
        private static readonly object _lock = new object();

        private void EnsureCityMappingLoaded()
        {
            if (_cityMapping == null)
            {
                lock (_lock)
                {
                    if (_cityMapping == null)
                    {
                        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var cityList = new List<BusCityDto>();
                        try
                        {
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var dbContext = scope.ServiceProvider.GetService<AppDbContext>();
                                if (dbContext != null)
                                {
                                    var dbCities = dbContext.BusCities.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.CityName).ToList();
                                    if (dbCities.Count > 0)
                                    {
                                        foreach (var city in dbCities)
                                        {
                                            if (!string.IsNullOrEmpty(city.CityName))
                                            {
                                                var cityIdStr = city.CityId.ToString();
                                                if (!mapping.ContainsKey(city.CityName))
                                                {
                                                    cityList.Add(new BusCityDto { CityId = cityIdStr, CityName = city.CityName, StateName = city.StateName ?? string.Empty });
                                                }

                                                mapping[city.CityName] = cityIdStr;
                                                var cleanName = city.CityName.Split('(')[0].Trim();
                                                if (!mapping.ContainsKey(cleanName))
                                                {
                                                    mapping[cleanName] = cityIdStr;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignore
                        }

                        _cityMapping = mapping;
                        _busCitiesList = cityList;
                    }
                }
            }
        }

        private readonly BusCityCacheService? _cityCache;

        private string MapCityNameToCode(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName)) return cityName;
            if (_cityCache != null) return _cityCache.MapCityNameToCode(cityName);
            EnsureCityMappingLoaded();

            if (_cityMapping != null && _cityMapping.TryGetValue(cityName, out var code))
            {
                return code;
            }

            return cityName;
        }

        public string MapCityCodeToName(string cityCode)
        {
            if (string.IsNullOrWhiteSpace(cityCode)) return cityCode;
            if (_cityCache != null) return _cityCache.MapCityCodeToName(cityCode);
            EnsureCityMappingLoaded();

            if (_busCitiesList != null)
            {
                var city = _busCitiesList.FirstOrDefault(c => c.CityId == cityCode);
                if (city != null) return city.CityName;
            }

            return cityCode;
        }

        public SrdvBusService(HttpClient httpClient, IOptions<SrdvSettings> settings, IMemoryCache cache, IServiceScopeFactory scopeFactory, BusCityCacheService? cityCache = null)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(180); // Increased from 60s to handle slow responses
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _settings = settings.Value;
            _cache = cache;
            _scopeFactory = scopeFactory;
            _cityCache = cityCache;

            if (!string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Remove("Api-Token");
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }
        }

        public Task<string> AuthenticateAsync()
        {
            return Task.FromResult(ApiToken);
        }

        public Task<List<BusCityDto>> SearchBusCitiesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult(new List<BusCityDto>());
            }

            EnsureCityMappingLoaded();

            if (_busCitiesList == null)
            {
                return Task.FromResult(new List<BusCityDto>());
            }

            var trimmedQuery = query.Trim();
            var results = _busCitiesList
                .Where(c => c.CityName.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.CityName.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.CityName.StartsWith(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                .ThenBy(c => c.CityName.Length)
                .ThenBy(c => c.CityName)
                .Take(20)
                .ToList();

            return Task.FromResult(results);
        }

        public async Task<string> SearchBusesProxyAsync(BusSearchProxyRequestDto request)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var requestBody = new
            {
                FromCityCode = request.FromCityCode,
                ToCityCode = request.ToCityCode,
                DepartDate = request.DepartDate
            };

            var searchUrl = $"{_settings.BusBaseUrl.TrimEnd('/')}/Search";
            var response = await _httpClient.PostAsJsonAsync(searchUrl, requestBody, _jsonOptions);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<List<SrdvBusOfferDto>> SearchBusesAsync(string originId, string destinationId, string journeyDate)
        {
            var cacheKey = $"Bus_Search_{originId}_{destinationId}_{journeyDate}";
            if (!_cache.TryGetValue(cacheKey, out List<SrdvBusOfferDto>? cachedBuses))
            {
                var (_, buses) = await SearchBusesWithRawAsync(originId, destinationId, journeyDate);
                cachedBuses = buses;
                _cache.Set(cacheKey, cachedBuses, TimeSpan.FromMinutes(15));
            }
            
            // Dynamic Time Filtering for expired buses
            var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            var cutoffTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone).AddMinutes(-5);
            DateTime.TryParseExact(journeyDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime parsedJourneyDate);

            var validBuses = cachedBuses!.Where(bus => {
                if (DateTime.TryParse(bus.DepartureTime, out DateTime deptTime))
                {
                    var fullDeptTime = new DateTime(parsedJourneyDate.Year, parsedJourneyDate.Month, parsedJourneyDate.Day, deptTime.Hour, deptTime.Minute, deptTime.Second);
                    return fullDeptTime >= cutoffTime;
                }
                return true;
            }).ToList();

            return validBuses;
        }

        public async Task<(string RawJson, List<SrdvBusOfferDto> Buses)> SearchBusesWithRawAsync(string originId, string destinationId, string journeyDate)
        {
            var fromCodeStr = MapCityNameToCode(originId);
            var toCodeStr = MapCityNameToCode(destinationId);
            _ = long.TryParse(fromCodeStr, out var fromCode);
            _ = long.TryParse(toCodeStr, out var toCode);

            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var requestBody = new
            {
                FromCityCode = fromCode,
                ToCityCode = toCode,
                DepartDate = journeyDate
            };

            var searchUrl = $"{_settings.BusBaseUrl.TrimEnd('/')}/Search";
            var response = await _httpClient.PostAsJsonAsync(searchUrl, requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            var json = await JsonDocument.ParseAsync(contentStream);
            
            var res = new List<SrdvBusOfferDto>();

            int errorCode = -1;
            string errorMessage = "Unknown SRDV error";

            if (json.RootElement.TryGetProperty("Error", out var errorProp))
            {
                if (errorProp.TryGetProperty("ErrorCode", out var codeProp))
                {
                    errorCode = codeProp.GetInt32();
                }
                if (errorProp.TryGetProperty("ErrorMessage", out var msgProp))
                {
                    errorMessage = msgProp.GetString() ?? errorMessage;
                }
            }

            if (errorCode == 0)
            {
                var traceIdProp = json.RootElement.GetProperty("TraceId");
                var traceId = traceIdProp.ValueKind == JsonValueKind.Number 
                    ? traceIdProp.GetInt32().ToString() 
                    : traceIdProp.GetString() ?? string.Empty;

                if (json.RootElement.TryGetProperty("Result", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
                    var cutoffTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone).AddMinutes(-5);
                    DateTime.TryParseExact(journeyDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime parsedJourneyDate);

                    foreach (var bus in results.EnumerateArray())
                    {
                        var operatorName = bus.TryGetProperty("TravelsName", out var tnProp) ? tnProp.GetString() ?? string.Empty : string.Empty;
                        var operatorId = bus.TryGetProperty("OperatorId", out var oiProp) ? oiProp.GetString() ?? string.Empty : string.Empty;
                        var busType = bus.TryGetProperty("BusType", out var btProp) ? btProp.GetString() ?? string.Empty : string.Empty;
                        var departureTime = bus.TryGetProperty("DepartureTime", out var dtProp) ? dtProp.GetString() ?? string.Empty : string.Empty;

                        if (DateTime.TryParse(departureTime, out DateTime deptTime))
                        {
                            // Combine parsed journey date with the time to correctly evaluate tomorrow's buses
                            var fullDeptTime = new DateTime(parsedJourneyDate.Year, parsedJourneyDate.Month, parsedJourneyDate.Day, deptTime.Hour, deptTime.Minute, deptTime.Second);
                            if (fullDeptTime < cutoffTime)
                            {
                                continue; // Skip buses that departed over 5 mins ago
                            }
                        }

                        var arrivalTime = bus.TryGetProperty("ArrivalTime", out var atProp) ? atProp.GetString() ?? string.Empty : string.Empty;
                        
                        decimal price = 0;
                        if (bus.TryGetProperty("DisplayFare", out var fareProp))
                        {
                            decimal.TryParse(fareProp.GetString(), out price);
                        }
                        
                        var availableSeats = 0;
                        if (bus.TryGetProperty("AvailableSeats", out var seatsProp))
                        {
                            if (seatsProp.ValueKind == JsonValueKind.Number)
                            {
                                availableSeats = seatsProp.GetInt32();
                            }
                            else if (seatsProp.ValueKind == JsonValueKind.String)
                            {
                                int.TryParse(seatsProp.GetString(), out availableSeats);
                            }
                        }

                        res.Add(new SrdvBusOfferDto
                        {
                            OperatorName = operatorName,
                            OperatorId = operatorId,
                            BusType = busType,
                            DepartureTime = departureTime,
                            ArrivalTime = arrivalTime,
                            Price = price,
                            AvailableSeats = availableSeats,
                            TraceId = traceId,
                            ResultIndex = bus.TryGetProperty("ResultIndex", out var riProp) ? riProp.GetString() : null,
                            SrdvIndex = bus.TryGetProperty("SrdvIndex", out var siProp) 
                                ? (siProp.ValueKind == JsonValueKind.Number ? siProp.GetInt64() : (long.TryParse(siProp.GetString(), out var parsedSi) ? parsedSi : 0))
                                : 0,
                            IsGSTMandatory = bus.TryGetProperty("IsGSTMandatory", out var gstProp) && gstProp.GetBoolean(),
                            IsTypeRequired = bus.TryGetProperty("IsTypeRequired", out var typeProp) && typeProp.GetBoolean(),
                            IsDropPointMandatory = bus.TryGetProperty("IsDropPointMandatory", out var dropProp) && dropProp.GetBoolean()
                        });
                    }
                }
            }
            else
            {
                throw new Exception($"SRDV Search failed. ErrorCode: {errorCode}. ErrorMessage: {errorMessage}. Raw Response: [Omitted]");
            }

            return (string.Empty, res);
        }

        public async Task<string> BlockBusProxyAsync(SrdvBusBookingRequestDto request)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var compositeResultIndex = BuildCompositeResultIndex(request.ResultIndex, request.SrdvIndex.ToString());
            var parsedTraceId = long.TryParse(request.TraceId, out var tid) ? (object)tid : request.TraceId;

            var refId = !string.IsNullOrWhiteSpace(request.RefId)
                ? request.RefId.Trim()
                : $"PB-{request.TraceId}-{Guid.NewGuid():N}";

            // Ensure exactly one lead passenger
            int leadIndex = request.Passengers.FindIndex(p => p.LeadPassenger == true);
            if (leadIndex < 0) leadIndex = 0; // Default first passenger if not explicitly marked

            var passengersList = request.Passengers.Select((p, idx) =>
            {
                var genderStr = p.Gender.ToString().Trim();
                if (genderStr.Equals("Male", StringComparison.OrdinalIgnoreCase) || genderStr == "1") genderStr = "1";
                else if (genderStr.Equals("Female", StringComparison.OrdinalIgnoreCase) || genderStr == "2") genderStr = "2";
                else genderStr = "1";

                var hasGst = !string.IsNullOrWhiteSpace(p.GSTNumber);

                var pax = new Dictionary<string, object?>
                {
                    ["Title"] = !string.IsNullOrWhiteSpace(p.Title) ? p.Title.Trim() : "Mr",
                    ["FirstName"] = !string.IsNullOrWhiteSpace(p.FirstName) ? p.FirstName.Trim() : "Passenger",
                    ["LastName"] = !string.IsNullOrWhiteSpace(p.LastName) ? p.LastName.Trim() : "Passenger",
                    ["Gender"] = genderStr,
                    ["Age"] = p.Age > 0 ? p.Age : 30,
                    ["Email"] = !string.IsNullOrWhiteSpace(p.Email) ? p.Email.Trim() : "passenger@example.com",
                    ["PhoneNo"] = !string.IsNullOrWhiteSpace(p.ContactNo) ? p.ContactNo.Trim() : "9876543210",
                    ["LeadPassenger"] = (idx == leadIndex),
                    ["IdNumber"] = p.IdNumber?.Trim() ?? string.Empty,
                    ["IdType"] = p.IdType?.Trim() ?? string.Empty,
                    ["Address"] = !string.IsNullOrWhiteSpace(p.Address) ? p.Address.Trim() : "India",
                    ["SeatName"] = p.SeatName?.Trim() ?? string.Empty,
                    ["GSTCompanyName"] = hasGst ? p.GSTCompanyName?.Trim() : null,
                    ["GSTNumber"] = hasGst ? p.GSTNumber?.Trim() : null,
                    ["GSTCompanyAddress"] = hasGst ? p.GSTCompanyAddress?.Trim() : null,
                    ["GSTCompanyEmail"] = hasGst ? p.GSTCompanyEmail?.Trim() : null
                };

                return pax;
            }).ToList();

            var blockRequestBody = new
            {
                TraceId = parsedTraceId,
                ResultIndex = compositeResultIndex,
                BoardingPointId = request.BoardingPointId?.Trim() ?? string.Empty,
                DroppingPointId = request.DroppingPointId?.Trim() ?? string.Empty,
                RefId = refId,
                Passengers = passengersList
            };

            var blockUrl = $"{_settings.BusBaseUrl.TrimEnd('/')}/Block";
            var blockResponse = await _httpClient.PostAsJsonAsync(blockUrl, blockRequestBody, _jsonOptions);
            blockResponse.EnsureSuccessStatusCode();

            var rawJson = await blockResponse.Content.ReadAsStringAsync();

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("Passengers", out var passengersElement) && passengersElement.ValueKind == JsonValueKind.Array)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    foreach (var passenger in passengersElement.EnumerateArray())
                    {
                        if (passenger.TryGetProperty("Seat", out var seatElement) &&
                            seatElement.TryGetProperty("Price", out var priceElement))
                        {
                            var seatName = seatElement.TryGetProperty("SeatName", out var sn) ? sn.GetString() : null;
                            if (string.IsNullOrEmpty(seatName)) continue;

                            decimal publishedFare = 0;
                            decimal gstAmount = 0;
                            decimal baseFare = 0;

                            if (priceElement.TryGetProperty("PublishedFare", out var pubFareEl))
                                _ = decimal.TryParse(pubFareEl.ToString(), out publishedFare);
                            
                            if (priceElement.TryGetProperty("GstAmount", out var gstEl) || 
                                priceElement.TryGetProperty("GSTAmount", out gstEl) || 
                                priceElement.TryGetProperty("gstAmount", out gstEl) ||
                                priceElement.TryGetProperty("Tax", out gstEl))
                            {
                                _ = decimal.TryParse(gstEl.ToString(), out gstAmount);
                            }
                                
                            if (priceElement.TryGetProperty("BaseFare", out var baseFareEl))
                                _ = decimal.TryParse(baseFareEl.ToString(), out baseFare);

                            // The pricing engine now correctly uses baseFare to compute markups.
                            // We save the pure SRDV PublishedFare and BaseFare without overrides.
                            var record = new BusBlockedSeatPrice
                            {
                                TraceId = request.TraceId,
                                SeatName = seatName,
                                BaseFare = baseFare,
                                GstAmount = gstAmount,
                                PublishedFare = publishedFare,
                                CreatedAtUtc = DateTime.UtcNow
                            };
                            db.BusBlockedSeatPrices.Add(record);
                        }
                    }
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                // Silently swallow parse/db errors here to not block the booking flow if JSON structure varies
            }

            return rawJson;
        }

        public async Task<SrdvBusBookingResponseDto> BookBusAsync(SrdvBusBookingRequestDto request, string blockKey)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var compositeResultIndex = BuildCompositeResultIndex(request.ResultIndex, request.SrdvIndex.ToString());
            var parsedTraceId = long.TryParse(request.TraceId, out var tid) ? (object)tid : request.TraceId;

            // The NEW /v9/Book provider request body contains ONLY TraceId and composite ResultIndex
            var bookRequestBody = new
            {
                TraceId = parsedTraceId,
                ResultIndex = compositeResultIndex
            };

            var bookUrl = $"{_settings.BusBaseUrl.TrimEnd('/')}/Book";
            var bookResponse = await _httpClient.PostAsJsonAsync(bookUrl, bookRequestBody, _jsonOptions);
            bookResponse.EnsureSuccessStatusCode();

            var bookContent = await bookResponse.Content.ReadAsStringAsync();
            var bookJson = JsonDocument.Parse(bookContent);

            var dto = new SrdvBusBookingResponseDto
            {
                ResponseJson = bookContent
            };

            int bookErrorCode = 0;
            string bookErrorMessage = "Unknown booking error";

            if (bookJson.RootElement.TryGetProperty("Error", out var bookErrorProp))
            {
                if (bookErrorProp.TryGetProperty("ErrorCode", out var codeProp))
                {
                    if (codeProp.ValueKind == JsonValueKind.Number)
                        bookErrorCode = codeProp.GetInt32();
                    else if (codeProp.ValueKind == JsonValueKind.String)
                        int.TryParse(codeProp.GetString(), out bookErrorCode);
                }
                if (bookErrorProp.TryGetProperty("ErrorMessage", out var msgProp))
                {
                    bookErrorMessage = msgProp.GetString() ?? bookErrorMessage;
                }
            }

            if (bookErrorCode == 0)
            {
                dto.Success = true;
                dto.ErrorCode = 0;
                if (bookJson.RootElement.TryGetProperty("BookingId", out var bookingIdProp))
                {
                    dto.SrdvBookingId = bookingIdProp.ValueKind == JsonValueKind.Number 
                        ? bookingIdProp.GetRawText() 
                        : bookingIdProp.GetString();
                }

                if (bookJson.RootElement.TryGetProperty("Result", out var resultProp))
                {
                    if (resultProp.TryGetProperty("TicketNo", out var ticketProp))
                    {
                        dto.TicketNo = ticketProp.ValueKind == JsonValueKind.Number ? ticketProp.GetRawText() : ticketProp.GetString();
                    }
                    if (resultProp.TryGetProperty("TravelOperatorPNR", out var pnrProp))
                    {
                        dto.TravelOperatorPNR = pnrProp.ValueKind == JsonValueKind.Number ? pnrProp.GetRawText() : pnrProp.GetString();
                    }
                    if (resultProp.TryGetProperty("BookingId", out var resultBookIdProp) && string.IsNullOrEmpty(dto.SrdvBookingId))
                    {
                        dto.SrdvBookingId = resultBookIdProp.ValueKind == JsonValueKind.Number ? resultBookIdProp.GetRawText() : resultBookIdProp.GetString();
                    }
                }
                else
                {
                    if (bookJson.RootElement.TryGetProperty("TicketNo", out var ticketProp))
                    {
                        dto.TicketNo = ticketProp.ValueKind == JsonValueKind.Number ? ticketProp.GetRawText() : ticketProp.GetString();
                    }
                    if (bookJson.RootElement.TryGetProperty("TravelOperatorPNR", out var pnrProp))
                    {
                        dto.TravelOperatorPNR = pnrProp.ValueKind == JsonValueKind.Number ? pnrProp.GetRawText() : pnrProp.GetString();
                    }
                }
            }
            else
            {
                dto.Success = false;
                dto.ErrorCode = bookErrorCode;
                dto.ErrorMessage = bookErrorMessage;
            }

            return dto;
        }

        public async Task<SrdvBoardingDroppingDetailsDto> GetBoardingPointDetailsAsync(string traceId, long srdvIndex, string resultIndex)
        {
            var compositeResultIndex = BuildCompositeResultIndex(resultIndex, srdvIndex.ToString());
            var parsedTraceId = long.TryParse(traceId, out var tid) ? (object)tid : traceId;

            var requestBody = new
            {
                TraceId = parsedTraceId,
                ResultIndex = compositeResultIndex
            };

            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var url = $"{_settings.BusBaseUrl.TrimEnd('/')}/GetBoardingPointDetails";
            var response = await _httpClient.PostAsJsonAsync(url, requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            var json = await JsonDocument.ParseAsync(contentStream);
            var result = new SrdvBoardingDroppingDetailsDto();

            if (json.RootElement.TryGetProperty("BoardingPoints", out var bpProp) && bpProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var bp in bpProp.EnumerateArray())
                {
                    result.BoardingPoints.Add(new BusPointDto
                    {
                        Id = bp.GetProperty("Id").GetString() ?? string.Empty,
                        Name = bp.GetProperty("Name").GetString() ?? string.Empty,
                        Address = bp.TryGetProperty("Address", out var a) ? a.GetString() ?? string.Empty : string.Empty,
                        Time = bp.TryGetProperty("Time", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                        Landmark = bp.TryGetProperty("Landmark", out var l) ? l.GetString() ?? string.Empty : string.Empty,
                        ContactNumber = bp.TryGetProperty("ContactNumber", out var c) ? c.GetString() ?? string.Empty : string.Empty
                    });
                }
            }

            if (json.RootElement.TryGetProperty("DroppingPoints", out var dpProp) && dpProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var dp in dpProp.EnumerateArray())
                {
                    result.DroppingPoints.Add(new BusPointDto
                    {
                        Id = dp.GetProperty("Id").GetString() ?? string.Empty,
                        Name = dp.GetProperty("Name").GetString() ?? string.Empty,
                        Address = dp.TryGetProperty("Address", out var a) ? a.GetString() ?? string.Empty : string.Empty,
                        Time = dp.TryGetProperty("Time", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                        Landmark = dp.TryGetProperty("Landmark", out var l) ? l.GetString() ?? string.Empty : string.Empty,
                        ContactNumber = dp.TryGetProperty("ContactNumber", out var c) ? c.GetString() ?? string.Empty : string.Empty
                    });
                }
            }

            return result;
        }

        public static string BuildCompositeResultIndex(string? resultIndex, string? srdvIndex)
        {
            var resIdx = resultIndex?.Trim() ?? string.Empty;
            var sIdx = srdvIndex?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(sIdx) || sIdx == "0")
            {
                return resIdx;
            }

            if (resIdx.StartsWith($"{sIdx}_", StringComparison.OrdinalIgnoreCase))
            {
                return resIdx;
            }

            return $"{sIdx}_{resIdx}";
        }

        public async Task<List<SrdvSeatDto>> GetSeatLayoutAsync(
            string traceId,
            long srdvIndex,
            string resultIndex,
            string? boardingPointId = null,
            string? droppingPointId = null)
        {
            var compositeResultIndex = BuildCompositeResultIndex(resultIndex, srdvIndex.ToString());
            var parsedTraceId = long.TryParse(traceId, out var tid) ? (object)tid : traceId;

            object requestBody;
            if (!string.IsNullOrWhiteSpace(boardingPointId) && !string.IsNullOrWhiteSpace(droppingPointId))
            {
                requestBody = new
                {
                    TraceId = parsedTraceId,
                    ResultIndex = compositeResultIndex,
                    BoardingPointId = boardingPointId,
                    DroppingPointId = droppingPointId
                };
            }
            else
            {
                requestBody = new
                {
                    TraceId = parsedTraceId,
                    ResultIndex = compositeResultIndex
                };
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var url = $"{_settings.BusBaseUrl.TrimEnd('/')}/GetSeatLayOut";
            var response = await _httpClient.PostAsJsonAsync(url, requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            var json = await JsonDocument.ParseAsync(contentStream);
            var res = new List<SrdvSeatDto>();

            if (json.RootElement.TryGetProperty("Result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var rowProperty in resultProp.EnumerateObject())
                {
                    var seatsList = new List<JsonElement>();
                    if (rowProperty.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var seat in rowProperty.Value.EnumerateArray())
                        {
                            seatsList.Add(seat);
                        }
                    }
                    else if (rowProperty.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var colProperty in rowProperty.Value.EnumerateObject())
                        {
                            seatsList.Add(colProperty.Value);
                        }
                    }

                    foreach (var seat in seatsList)
                    {
                        var seatName = seat.GetProperty("SeatName").GetString() ?? string.Empty;
                        var seatStatus = seat.GetProperty("SeatStatus").GetString() ?? string.Empty;
                        var seatType = seat.GetProperty("SeatType").GetString() ?? string.Empty;
                        
                        decimal fare = 0;
                        if (seat.TryGetProperty("SeatFare", out var fareProp))
                        {
                            if (fareProp.ValueKind == JsonValueKind.Number)
                                fare = fareProp.GetDecimal();
                            else if (fareProp.ValueKind == JsonValueKind.String)
                                decimal.TryParse(fareProp.GetString(), out fare);
                        }

                        int rowNo = 0;
                        if (seat.TryGetProperty("RowNo", out var rowProp))
                        {
                            if (rowProp.ValueKind == JsonValueKind.Number)
                                rowNo = rowProp.GetInt32();
                            else if (rowProp.ValueKind == JsonValueKind.String)
                                int.TryParse(rowProp.GetString(), out rowNo);
                        }

                        int colNo = 0;
                        if (seat.TryGetProperty("ColumnNo", out var colProp))
                        {
                            if (colProp.ValueKind == JsonValueKind.Number)
                                colNo = colProp.GetInt32();
                            else if (colProp.ValueKind == JsonValueKind.String)
                                int.TryParse(colProp.GetString(), out colNo);
                        }

                        bool isUpper = false;
                        if (seat.TryGetProperty("IsUpper", out var upperProp))
                        {
                            if (upperProp.ValueKind == JsonValueKind.True || upperProp.ValueKind == JsonValueKind.False)
                                isUpper = upperProp.GetBoolean();
                            else if (upperProp.ValueKind == JsonValueKind.String)
                                bool.TryParse(upperProp.GetString(), out isUpper);
                        }

                        res.Add(new SrdvSeatDto
                        {
                            SeatName = seatName,
                            SeatStatus = seatStatus,
                            SeatType = seatType,
                            SeatFare = fare,
                            RowNo = rowNo,
                            ColumnNo = colNo,
                            IsUpper = isUpper
                        });
                    }
                }
            }

            return res;
        }

        public async Task<string> GetSeatLayoutRawAsync(
            string traceId,
            long srdvIndex,
            string resultIndex,
            string? boardingPointId = null,
            string? droppingPointId = null)
        {
            var compositeResultIndex = BuildCompositeResultIndex(resultIndex, srdvIndex.ToString());
            var parsedTraceId = long.TryParse(traceId, out var tid) ? (object)tid : traceId;

            object requestBody;
            if (!string.IsNullOrWhiteSpace(boardingPointId) && !string.IsNullOrWhiteSpace(droppingPointId))
            {
                requestBody = new
                {
                    TraceId = parsedTraceId,
                    ResultIndex = compositeResultIndex,
                    BoardingPointId = boardingPointId,
                    DroppingPointId = droppingPointId
                };
            }
            else
            {
                requestBody = new
                {
                    TraceId = parsedTraceId,
                    ResultIndex = compositeResultIndex
                };
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var url = $"{_settings.BusBaseUrl.TrimEnd('/')}/GetSeatLayOut";
            var response = await _httpClient.PostAsJsonAsync(url, requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetSeatLayoutProxyAsync(BusSeatLayoutProxyRequestDto request)
        {
            var compositeResultIndex = BuildCompositeResultIndex(request.ResultIndex, request.SrdvIndex);
            var parsedTraceId = long.TryParse(request.TraceId, out var tid) ? (object)tid : request.TraceId;

            object requestBody;
            if (!string.IsNullOrWhiteSpace(request.BoardingPointId) && !string.IsNullOrWhiteSpace(request.DroppingPointId))
            {
                requestBody = new
                {
                    TraceId = parsedTraceId,
                    ResultIndex = compositeResultIndex,
                    BoardingPointId = request.BoardingPointId,
                    DroppingPointId = request.DroppingPointId
                };
            }
            else
            {
                requestBody = new
                {
                    TraceId = parsedTraceId,
                    ResultIndex = compositeResultIndex
                };
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var url = $"{_settings.BusBaseUrl.TrimEnd('/')}/GetSeatLayOut";
            var response = await _httpClient.PostAsJsonAsync(url, requestBody, _jsonOptions);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetBoardingPointDetailsProxyAsync(BusBoardingPointsProxyRequestDto request)
        {
            var compositeResultIndex = BuildCompositeResultIndex(request.ResultIndex, request.SrdvIndex);
            var parsedTraceId = long.TryParse(request.TraceId, out var tid) ? (object)tid : request.TraceId;

            var requestBody = new
            {
                TraceId = parsedTraceId,
                ResultIndex = compositeResultIndex
            };

            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var url = $"{_settings.BusBaseUrl.TrimEnd('/')}/GetBoardingPointDetails";
            var response = await _httpClient.PostAsJsonAsync(url, requestBody, _jsonOptions);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<SrdvBusCancelResponseDto> CancelTicketV9Async(long traceId, List<string> seatNames, string remarks)
        {
            // 1. Validation
            if (traceId <= 0)
            {
                return new SrdvBusCancelResponseDto
                {
                    Success = false,
                    IsExplicitSupplierRejection = true,
                    ErrorCode = 400,
                    ErrorMessage = "TraceId must be greater than 0."
                };
            }

            var cleanedSeats = (seatNames ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanedSeats.Count == 0)
            {
                return new SrdvBusCancelResponseDto
                {
                    Success = false,
                    IsExplicitSupplierRejection = true,
                    ErrorCode = 400,
                    ErrorMessage = "SeatName must contain at least 1 valid seat."
                };
            }

            if (cleanedSeats.Count > 10)
            {
                return new SrdvBusCancelResponseDto
                {
                    Success = false,
                    IsExplicitSupplierRejection = true,
                    ErrorCode = 400,
                    ErrorMessage = "SeatName cannot contain more than 10 seats."
                };
            }

            if (cleanedSeats.Any(s => s.Length > 100))
            {
                return new SrdvBusCancelResponseDto
                {
                    Success = false,
                    IsExplicitSupplierRejection = true,
                    ErrorCode = 400,
                    ErrorMessage = "SeatName items must not exceed 100 characters."
                };
            }

            var cleanRemarks = remarks?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cleanRemarks))
            {
                return new SrdvBusCancelResponseDto
                {
                    Success = false,
                    IsExplicitSupplierRejection = true,
                    ErrorCode = 400,
                    ErrorMessage = "Remarks is mandatory and cannot be empty."
                };
            }

            if (cleanRemarks.Length > 1000)
            {
                cleanRemarks = cleanRemarks.Substring(0, 1000);
            }

            // 2. Canonical V9 Provider Request
            var url = $"{_settings.BusBaseUrl.TrimEnd('/')}/Cancel";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(ApiToken))
            {
                httpRequest.Headers.Add("Api-Token", ApiToken);
            }

            var payload = new SrdvBusCancelRequestDto
            {
                TraceId = traceId,
                SeatName = cleanedSeats,
                Remarks = cleanRemarks
            };
            httpRequest.Content = JsonContent.Create(payload, options: _jsonOptions);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(httpRequest);
            }
            catch (Exception ex)
            {
                return new SrdvBusCancelResponseDto
                {
                    Success = false,
                    IsAmbiguous = true,
                    IsExplicitSupplierRejection = false,
                    ErrorCode = -1,
                    ErrorMessage = $"Network/HTTP Exception: {ex.Message}"
                };
            }

            string content = string.Empty;
            try
            {
                content = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return new SrdvBusCancelResponseDto
                {
                    Success = false,
                    IsAmbiguous = true,
                    IsExplicitSupplierRejection = false,
                    ErrorCode = -1,
                    ErrorMessage = $"Failed reading SRDV response: {ex.Message}"
                };
            }

            var dto = new SrdvBusCancelResponseDto
            {
                ResponseJson = content
            };

            JsonDocument? json = null;
            try
            {
                json = JsonDocument.Parse(content);
            }
            catch
            {
                if ((int)response.StatusCode >= 500)
                {
                    dto.Success = false;
                    dto.IsAmbiguous = true;
                    dto.ErrorCode = (int)response.StatusCode;
                    dto.ErrorMessage = $"Supplier returned HTTP {(int)response.StatusCode} with non-JSON body.";
                    return dto;
                }
                else
                {
                    dto.Success = false;
                    dto.IsExplicitSupplierRejection = true;
                    dto.ErrorCode = (int)response.StatusCode;
                    dto.ErrorMessage = $"Supplier returned HTTP {(int)response.StatusCode}: {content}";
                    return dto;
                }
            }

            using (json)
            {
                var root = json.RootElement;
                string? status = null;
                long? cancelId = null;
                int errCode = 0;
                string? errMsg = null;
                decimal cancellationCharge = 0m;
                decimal refundAmount = 0m;

                if (root.TryGetProperty("Status", out var statusProp))
                {
                    status = statusProp.GetString();
                }

                if (root.TryGetProperty("CancelId", out var cidProp))
                {
                    if (cidProp.ValueKind == JsonValueKind.Number)
                        cancelId = cidProp.GetInt64();
                    else if (cidProp.ValueKind == JsonValueKind.String && long.TryParse(cidProp.GetString(), out var parsedCid))
                        cancelId = parsedCid;
                }

                string? supplierCancelId = null;
                if (root.TryGetProperty("SupplierCancelId", out var scidProp))
                {
                    supplierCancelId = scidProp.ValueKind == JsonValueKind.String ? scidProp.GetString() : scidProp.ToString();
                }

                if (root.TryGetProperty("Error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
                {
                    if (errorProp.TryGetProperty("ErrorCode", out var codeProp))
                    {
                        if (codeProp.ValueKind == JsonValueKind.Number) errCode = codeProp.GetInt32();
                        else if (codeProp.ValueKind == JsonValueKind.String && int.TryParse(codeProp.GetString(), out var ec)) errCode = ec;
                    }
                    if (errorProp.TryGetProperty("ErrorMessage", out var msgProp))
                    {
                        errMsg = msgProp.GetString();
                    }
                }
                else if (root.TryGetProperty("ErrorCode", out var topErrProp))
                {
                    if (topErrProp.ValueKind == JsonValueKind.Number) errCode = topErrProp.GetInt32();
                    else if (topErrProp.ValueKind == JsonValueKind.String && int.TryParse(topErrProp.GetString(), out var ec)) errCode = ec;
                    if (root.TryGetProperty("ErrorMessage", out var topMsgProp))
                    {
                        errMsg = topMsgProp.GetString();
                    }
                }

                if (root.TryGetProperty("CancellationCharge", out var ccProp))
                {
                    if (ccProp.ValueKind == JsonValueKind.Number) cancellationCharge = ccProp.GetDecimal();
                    else if (ccProp.ValueKind == JsonValueKind.String && decimal.TryParse(ccProp.GetString(), out var cc)) cancellationCharge = cc;
                }

                if (root.TryGetProperty("RefundAmount", out var raProp))
                {
                    if (raProp.ValueKind == JsonValueKind.Number) refundAmount = raProp.GetDecimal();
                    else if (raProp.ValueKind == JsonValueKind.String && decimal.TryParse(raProp.GetString(), out var ra)) refundAmount = ra;
                }

                dto.Status = status;
                dto.CancelId = cancelId;
                dto.SupplierCancelId = supplierCancelId;
                dto.ErrorCode = errCode;
                dto.ErrorMessage = errMsg;
                dto.CancellationCharge = cancellationCharge;
                dto.RefundAmount = refundAmount;

                bool isInProcess = string.Equals(status, "In Process", StringComparison.OrdinalIgnoreCase);
                bool hasCancelId = cancelId.HasValue && cancelId.Value > 0;

                if (isInProcess || hasCancelId || (response.IsSuccessStatusCode && errCode == 0 && string.IsNullOrWhiteSpace(errMsg) && (status == null || !string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))))
                {
                    dto.Success = true;
                    dto.IsExplicitSupplierRejection = false;
                    dto.IsAmbiguous = false;
                    if (string.IsNullOrEmpty(dto.Status)) dto.Status = isInProcess ? "In Process" : "Success";
                }
                else
                {
                    dto.Success = false;
                    dto.IsExplicitSupplierRejection = true;
                    dto.IsAmbiguous = false;
                    if (string.IsNullOrWhiteSpace(dto.ErrorMessage))
                    {
                        dto.ErrorMessage = !string.IsNullOrWhiteSpace(status) ? $"Supplier cancellation status: {status}" : "Unknown SRDV Cancellation Error";
                    }
                }

                return dto;
            }
        }

        public async Task<(bool Success, string ErrorMessage, decimal CancellationCharge, decimal RefundAmount)> CancelTicketAsync(string traceId, string seatName, string remark)
        {
            long parsedTraceId = 0;
            if (!long.TryParse(traceId, out parsedTraceId) || parsedTraceId <= 0)
            {
                return (false, "TraceId must be present, numeric, and greater than 0.", 0m, 0m);
            }

            var seats = new List<string>();
            if (!string.IsNullOrWhiteSpace(seatName))
            {
                var split = seatName.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var s in split)
                {
                    var trimmed = s.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed)) seats.Add(trimmed);
                }
            }

            var remarks = string.IsNullOrWhiteSpace(remark) ? "Cancelled by user" : remark.Trim();

            var v9Res = await CancelTicketV9Async(parsedTraceId, seats, remarks);
            return (v9Res.Success, v9Res.ErrorMessage ?? string.Empty, v9Res.CancellationCharge, v9Res.RefundAmount);
        }
        public async Task<string> GetSrdvMasterWalletBalanceAsync(string endUserIp)
        {
            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = ClientId,
                UserName = UserName,
                Password = Password
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.BusBaseUrl}/Balance", requestBody, _jsonOptions);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetSrdvMasterWalletLogAsync(string endUserIp)
        {
            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = ClientId,
                UserName = UserName,
                Password = Password
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.BusBaseUrl}/BalanceLog", requestBody, _jsonOptions);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<SrdvBusBookingDetailsResponseDto> GetBookingDetailsAsync(string traceId)
        {
            var dto = new SrdvBusBookingDetailsResponseDto();

            if (!_httpClient.DefaultRequestHeaders.Contains("Api-Token") && !string.IsNullOrEmpty(ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Api-Token", ApiToken);
            }

            var parsedTraceId = long.TryParse(traceId, out var tid) ? (object)tid : traceId;
            var requestBody = new
            {
                TraceId = parsedTraceId
            };

            var url = $"{_settings.BusBaseUrl.TrimEnd('/')}/BookingDetails";
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync(url, requestBody, _jsonOptions);
            }
            catch (Exception ex)
            {
                dto.Success = false;
                dto.Error = new SrdvBusBookingDetailsErrorDto
                {
                    ErrorCode = -1,
                    ErrorMessage = $"HTTP Request Exception: {ex.Message}"
                };
                return dto;
            }

            var content = await response.Content.ReadAsStringAsync();
            dto.ResponseJson = content;

            if (!response.IsSuccessStatusCode)
            {
                dto.Success = false;
                dto.Error = new SrdvBusBookingDetailsErrorDto
                {
                    ErrorCode = (int)response.StatusCode,
                    ErrorMessage = $"Supplier returned HTTP {(int)response.StatusCode}"
                };
                return dto;
            }

            try
            {
                using var json = JsonDocument.Parse(content);
                var root = json.RootElement;

                // Error
                if (root.TryGetProperty("Error", out var errProp) && errProp.ValueKind == JsonValueKind.Object)
                {
                    int errCode = 0;
                    string? errMsg = null;
                    if (errProp.TryGetProperty("ErrorCode", out var ec))
                    {
                        if (ec.ValueKind == JsonValueKind.Number) errCode = ec.GetInt32();
                        else if (ec.ValueKind == JsonValueKind.String) int.TryParse(ec.GetString(), out errCode);
                    }
                    if (errProp.TryGetProperty("ErrorMessage", out var em))
                    {
                        errMsg = em.GetString();
                    }

                    if (errCode != 0 || !string.IsNullOrWhiteSpace(errMsg))
                    {
                        dto.Error = new SrdvBusBookingDetailsErrorDto
                        {
                            ErrorCode = errCode,
                            ErrorMessage = errMsg
                        };
                    }
                }

                // TraceId
                if (root.TryGetProperty("TraceId", out var traceProp))
                {
                    if (traceProp.ValueKind == JsonValueKind.Number) dto.TraceId = traceProp.GetInt64();
                    else if (traceProp.ValueKind == JsonValueKind.String && long.TryParse(traceProp.GetString(), out var parsedTid)) dto.TraceId = parsedTid;
                }

                // Result
                if (root.TryGetProperty("Result", out var resProp) && resProp.ValueKind == JsonValueKind.Object)
                {
                    var resultDto = new SrdvBusBookingDetailsResultDto();

                    if (resProp.TryGetProperty("SrdvIndex", out var sIdx))
                    {
                        if (sIdx.ValueKind == JsonValueKind.Number) resultDto.SrdvIndex = sIdx.GetInt64();
                        else if (sIdx.ValueKind == JsonValueKind.String && long.TryParse(sIdx.GetString(), out var parsedSIdx)) resultDto.SrdvIndex = parsedSIdx;
                    }
                    if (resProp.TryGetProperty("ResultIndex", out var rIdx)) resultDto.ResultIndex = rIdx.GetString();
                    if (resProp.TryGetProperty("BookingId", out var bId))
                    {
                        if (bId.ValueKind == JsonValueKind.Number) resultDto.BookingId = bId.GetInt64();
                        else if (bId.ValueKind == JsonValueKind.String && long.TryParse(bId.GetString(), out var parsedBId)) resultDto.BookingId = parsedBId;
                    }
                    if (resProp.TryGetProperty("RefId", out var refId)) resultDto.RefId = refId.GetString();
                    if (resProp.TryGetProperty("BookingStatus", out var bStatus)) resultDto.BookingStatus = bStatus.GetString();
                    if (resProp.TryGetProperty("TicketNo", out var tNo)) resultDto.TicketNo = tNo.ValueKind == JsonValueKind.Number ? tNo.GetRawText() : tNo.GetString();
                    if (resProp.TryGetProperty("TravelOperatorPNR", out var pnr)) resultDto.TravelOperatorPNR = pnr.ValueKind == JsonValueKind.Number ? pnr.GetRawText() : pnr.GetString();
                    
                    if (resProp.TryGetProperty("DsaFare", out var dsaFare))
                    {
                        if (dsaFare.ValueKind == JsonValueKind.Number) resultDto.DsaFare = dsaFare.GetDecimal();
                        else if (dsaFare.ValueKind == JsonValueKind.String && decimal.TryParse(dsaFare.GetString(), out var df)) resultDto.DsaFare = df;
                    }
                    if (resProp.TryGetProperty("CurrencyCode", out var cur)) resultDto.CurrencyCode = cur.GetString();
                    if (resProp.TryGetProperty("CancelStatus", out var cStatus)) resultDto.CancelStatus = cStatus.GetString();
                    if (resProp.TryGetProperty("RefundStatus", out var rStatus)) resultDto.RefundStatus = rStatus.GetString();

                    if (resProp.TryGetProperty("ErrorCode", out var resErrCode))
                    {
                        if (resErrCode.ValueKind == JsonValueKind.Number) resultDto.ErrorCode = resErrCode.GetInt32();
                        else if (resErrCode.ValueKind == JsonValueKind.String && int.TryParse(resErrCode.GetString(), out var rec)) resultDto.ErrorCode = rec;
                    }
                    if (resProp.TryGetProperty("ErrorMessage", out var resErrMsg)) resultDto.ErrorMessage = resErrMsg.GetString();

                    if (resProp.TryGetProperty("CompletedAt", out var compAt))
                    {
                        if (compAt.ValueKind == JsonValueKind.String && DateTime.TryParse(compAt.GetString(), out var cat)) resultDto.CompletedAt = cat;
                    }

                    // Passengers
                    if (resProp.TryGetProperty("Passengers", out var paxArray) && paxArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var px in paxArray.EnumerateArray())
                        {
                            var pDto = new SrdvBusBookingDetailsPassengerDto();
                            if (px.TryGetProperty("SeatName", out var sn)) pDto.SeatName = sn.GetString();
                            if (px.TryGetProperty("SeatIndex", out var si))
                            {
                                if (si.ValueKind == JsonValueKind.Number) pDto.SeatIndex = si.GetInt32();
                                else if (si.ValueKind == JsonValueKind.String && int.TryParse(si.GetString(), out var psi)) pDto.SeatIndex = psi;
                            }
                            if (px.TryGetProperty("IsUpper", out var iu))
                            {
                                if (iu.ValueKind == JsonValueKind.True || iu.ValueKind == JsonValueKind.False) pDto.IsUpper = iu.GetBoolean();
                                else if (iu.ValueKind == JsonValueKind.String && bool.TryParse(iu.GetString(), out var piu)) pDto.IsUpper = piu;
                            }
                            if (px.TryGetProperty("Title", out var title)) pDto.Title = title.GetString();
                            if (px.TryGetProperty("FirstName", out var fn)) pDto.FirstName = fn.GetString();
                            if (px.TryGetProperty("LastName", out var ln)) pDto.LastName = ln.GetString();
                            if (px.TryGetProperty("Gender", out var g)) pDto.Gender = g.ValueKind == JsonValueKind.Number ? g.GetRawText() : g.GetString();
                            if (px.TryGetProperty("Age", out var age))
                            {
                                if (age.ValueKind == JsonValueKind.Number) pDto.Age = age.GetInt32();
                                else if (age.ValueKind == JsonValueKind.String && int.TryParse(age.GetString(), out var page)) pDto.Age = page;
                            }
                            if (px.TryGetProperty("LeadPassenger", out var lp))
                            {
                                if (lp.ValueKind == JsonValueKind.True || lp.ValueKind == JsonValueKind.False) pDto.LeadPassenger = lp.GetBoolean();
                                else if (lp.ValueKind == JsonValueKind.String && bool.TryParse(lp.GetString(), out var plp)) pDto.LeadPassenger = plp;
                            }
                            if (px.TryGetProperty("CurrencyCode", out var cc)) pDto.CurrencyCode = cc.GetString();
                            if (px.TryGetProperty("BaseFare", out var bf))
                            {
                                if (bf.ValueKind == JsonValueKind.Number) pDto.BaseFare = bf.GetDecimal();
                                else if (bf.ValueKind == JsonValueKind.String && decimal.TryParse(bf.GetString(), out var pbf)) pDto.BaseFare = pbf;
                            }
                            if (px.TryGetProperty("Tax", out var tax))
                            {
                                if (tax.ValueKind == JsonValueKind.Number) pDto.Tax = tax.GetDecimal();
                                else if (tax.ValueKind == JsonValueKind.String && decimal.TryParse(tax.GetString(), out var ptax)) pDto.Tax = ptax;
                            }
                            if (px.TryGetProperty("PublishedFare", out var pf))
                            {
                                if (pf.ValueKind == JsonValueKind.Number) pDto.PublishedFare = pf.GetDecimal();
                                else if (pf.ValueKind == JsonValueKind.String && decimal.TryParse(pf.GetString(), out var ppf)) pDto.PublishedFare = ppf;
                            }
                            if (px.TryGetProperty("OfferedFare", out var of))
                            {
                                if (of.ValueKind == JsonValueKind.Number) pDto.OfferedFare = of.GetDecimal();
                                else if (of.ValueKind == JsonValueKind.String && decimal.TryParse(of.GetString(), out var pof)) pDto.OfferedFare = pof;
                            }
                            if (px.TryGetProperty("GstRate", out var gr))
                            {
                                if (gr.ValueKind == JsonValueKind.Number) pDto.GstRate = gr.GetDecimal();
                                else if (gr.ValueKind == JsonValueKind.String && decimal.TryParse(gr.GetString(), out var pgr)) pDto.GstRate = pgr;
                            }
                            if (px.TryGetProperty("GSTAmount", out var ga))
                            {
                                if (ga.ValueKind == JsonValueKind.Number) pDto.GSTAmount = ga.GetDecimal();
                                else if (ga.ValueKind == JsonValueKind.String && decimal.TryParse(ga.GetString(), out var pga)) pDto.GSTAmount = pga;
                            }
                            if (px.TryGetProperty("CancelStatus", out var cs)) pDto.CancelStatus = cs.GetString();
                            if (px.TryGetProperty("CancelledAt", out var ca))
                            {
                                if (ca.ValueKind == JsonValueKind.String && DateTime.TryParse(ca.GetString(), out var pca)) pDto.CancelledAt = pca;
                            }
                            resultDto.Passengers.Add(pDto);
                        }
                    }

                    // Cancellations
                    if (resProp.TryGetProperty("Cancellations", out var cancelArray) && cancelArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cx in cancelArray.EnumerateArray())
                        {
                            var cDto = new SrdvBusBookingDetailsCancellationDto();
                            if (cx.TryGetProperty("CancelId", out var cid))
                            {
                                if (cid.ValueKind == JsonValueKind.Number) cDto.CancelId = cid.GetInt64();
                                else if (cid.ValueKind == JsonValueKind.String && long.TryParse(cid.GetString(), out var parsedCid)) cDto.CancelId = parsedCid;
                            }
                            if (cx.TryGetProperty("Status", out var cst)) cDto.Status = cst.GetString();
                            if (cx.TryGetProperty("CancellationType", out var ct)) cDto.CancellationType = ct.GetString();

                            // SeatName[]: can be array of strings or single string
                            if (cx.TryGetProperty("SeatName", out var csn))
                            {
                                if (csn.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var seatItem in csn.EnumerateArray())
                                    {
                                        var seatStr = seatItem.GetString();
                                        if (!string.IsNullOrWhiteSpace(seatStr)) cDto.SeatName.Add(seatStr.Trim());
                                    }
                                }
                                else if (csn.ValueKind == JsonValueKind.String)
                                {
                                    var seatStr = csn.GetString();
                                    if (!string.IsNullOrWhiteSpace(seatStr))
                                    {
                                        var splitSeats = seatStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                                        foreach (var s in splitSeats) cDto.SeatName.Add(s.Trim());
                                    }
                                }
                            }

                            if (cx.TryGetProperty("SupplierCancelId", out var scid)) cDto.SupplierCancelId = scid.ValueKind == JsonValueKind.Number ? scid.GetRawText() : scid.GetString();
                            if (cx.TryGetProperty("RefundAmount", out var ra))
                            {
                                if (ra.ValueKind == JsonValueKind.Number) cDto.RefundAmount = ra.GetDecimal();
                                else if (ra.ValueKind == JsonValueKind.String && decimal.TryParse(ra.GetString(), out var pra)) cDto.RefundAmount = pra;
                            }
                            if (cx.TryGetProperty("CancellationCharge", out var cc))
                            {
                                if (cc.ValueKind == JsonValueKind.Number) cDto.CancellationCharge = cc.GetDecimal();
                                else if (cc.ValueKind == JsonValueKind.String && decimal.TryParse(cc.GetString(), out var pcc)) cDto.CancellationCharge = pcc;
                            }
                            if (cx.TryGetProperty("RefundStatus", out var rs)) cDto.RefundStatus = rs.GetString();
                            if (cx.TryGetProperty("ErrorCode", out var cec))
                            {
                                if (cec.ValueKind == JsonValueKind.Number) cDto.ErrorCode = cec.GetInt32();
                                else if (cec.ValueKind == JsonValueKind.String && int.TryParse(cec.GetString(), out var pcec)) cDto.ErrorCode = pcec;
                            }
                            if (cx.TryGetProperty("ErrorMessage", out var cem)) cDto.ErrorMessage = cem.GetString();
                            if (cx.TryGetProperty("CompletedAt", out var cca))
                            {
                                if (cca.ValueKind == JsonValueKind.String && DateTime.TryParse(cca.GetString(), out var pcca)) cDto.CompletedAt = pcca;
                            }

                            resultDto.Cancellations.Add(cDto);
                        }
                    }

                    dto.Result = resultDto;
                }

                dto.Success = dto.Error == null || dto.Error.ErrorCode == 0;
            }
            catch (Exception ex)
            {
                dto.Success = false;
                dto.Error = new SrdvBusBookingDetailsErrorDto
                {
                    ErrorCode = -1,
                    ErrorMessage = $"JSON Deserialization Exception: {ex.Message}"
                };
            }

            return dto;
        }
    }
}
