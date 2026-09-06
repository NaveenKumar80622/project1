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
                                    var dbCities = dbContext.BusCities.AsNoTracking().Where(c => c.IsActive).ToList();
                                    if (dbCities.Count > 0)
                                    {
                                        foreach (var city in dbCities)
                                        {
                                            if (!string.IsNullOrEmpty(city.CityName) && !string.IsNullOrEmpty(city.CityCode))
                                            {
                                                if (!mapping.ContainsKey(city.CityName))
                                                {
                                                    cityList.Add(new BusCityDto { CityId = city.CityCode, CityName = city.CityName, StateName = city.StateName ?? string.Empty });
                                                }

                                                mapping[city.CityName] = city.CityCode;
                                                var cleanName = city.CityName.Split('(')[0].Trim();
                                                if (!mapping.ContainsKey(cleanName))
                                                {
                                                    mapping[cleanName] = city.CityCode;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignore and fallback to file
                        }

                        if (cityList.Count == 0)
                        {
                            try
                            {
                                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "srdv_bus_cities.json");
                                if (System.IO.File.Exists(filePath))
                                {
                                    var jsonString = System.IO.File.ReadAllText(filePath);
                                    using var jsonDoc = JsonDocument.Parse(jsonString);
                                    foreach (var rootElement in jsonDoc.RootElement.EnumerateArray())
                                    {
                                        if (rootElement.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "table")
                                        {
                                            if (rootElement.TryGetProperty("data", out var dataProp))
                                            {
                                                foreach (var city in dataProp.EnumerateArray())
                                                {
                                                    var name = city.GetProperty("cico_city_name").GetString();
                                                    var id = city.GetProperty("cico_id").GetString();
                                                    var stateName = city.TryGetProperty("cico_state_name", out var s) ? s.GetString() : string.Empty;
                                                    
                                                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                                                    {
                                                        if (!mapping.ContainsKey(name))
                                                        {
                                                            cityList.Add(new BusCityDto { CityId = id, CityName = name, StateName = stateName ?? string.Empty });
                                                        }

                                                        mapping[name] = id;
                                                        
                                                        var cleanName = name.Split('(')[0].Trim();
                                                        if (!mapping.ContainsKey(cleanName))
                                                        {
                                                            mapping[cleanName] = id;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Fallback
                            }
                        }
                        _cityMapping = mapping;
                        _busCitiesList = cityList;
                    }
                }
            }
        }

        private string MapCityNameToCode(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName)) return cityName;
            if (int.TryParse(cityName, out _)) return cityName;

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
            EnsureCityMappingLoaded();

            if (_busCitiesList != null)
            {
                var city = _busCitiesList.FirstOrDefault(c => c.CityId == cityCode);
                if (city != null) return city.CityName;
            }

            return cityCode;
        }

        public SrdvBusService(HttpClient httpClient, IOptions<SrdvSettings> settings, IMemoryCache cache, IServiceScopeFactory scopeFactory)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(180); // Increased from 60s to handle slow responses
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _settings = settings.Value;
            _cache = cache;
            _scopeFactory = scopeFactory;

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

            var results = _busCitiesList
                .Where(c => c.CityName.Contains(query, StringComparison.OrdinalIgnoreCase))
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
            _ = int.TryParse(fromCodeStr, out var fromCode);
            _ = int.TryParse(toCodeStr, out var toCode);

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
                            SrdvIndex = bus.TryGetProperty("SrdvIndex", out var siProp) && siProp.ValueKind == JsonValueKind.Number ? siProp.GetInt32() : 0,
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
                dto.ErrorMessage = bookErrorMessage;
            }

            return dto;
        }

        public async Task<SrdvBoardingDroppingDetailsDto> GetBoardingPointDetailsAsync(string traceId, int srdvIndex, string resultIndex)
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
            int srdvIndex,
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
            int srdvIndex,
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

        public async Task<(bool Success, string ErrorMessage, decimal CancellationCharge, decimal RefundAmount)> CancelTicketAsync(string traceId, string seatName, string remark)
        {
            var requestBody = new
            {
                ClientId = ClientId,
                UserName = UserName,
                Password = Password,
                TraceId = traceId,
                SeatName = seatName,
                Remark = remark
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.BusBaseUrl}/Cancel", requestBody, _jsonOptions);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            var json = await JsonDocument.ParseAsync(contentStream);

            int errorCode = -1;
            string errorMessage = "Unknown SRDV Cancellation Error";

            if (json.RootElement.TryGetProperty("Error", out var errorProp))
            {
                if (errorProp.TryGetProperty("ErrorCode", out var codeProp))
                {
                    if (codeProp.ValueKind == JsonValueKind.Number)
                        errorCode = codeProp.GetInt32();
                    else if (codeProp.ValueKind == JsonValueKind.String)
                        int.TryParse(codeProp.GetString(), out errorCode);
                }
                if (errorProp.TryGetProperty("ErrorMessage", out var msgProp))
                {
                    errorMessage = msgProp.GetString() ?? errorMessage;
                }
            }

            decimal cancellationCharge = 0m;
            decimal refundAmount = 0m;

            if (json.RootElement.TryGetProperty("CancellationCharge", out var ccProp))
            {
                if (ccProp.ValueKind == JsonValueKind.Number) cancellationCharge = ccProp.GetDecimal();
                else if (ccProp.ValueKind == JsonValueKind.String && decimal.TryParse(ccProp.GetString(), out var c)) cancellationCharge = c;
            }

            if (json.RootElement.TryGetProperty("RefundAmount", out var raProp))
            {
                if (raProp.ValueKind == JsonValueKind.Number) refundAmount = raProp.GetDecimal();
                else if (raProp.ValueKind == JsonValueKind.String && decimal.TryParse(raProp.GetString(), out var r)) refundAmount = r;
            }

            return (errorCode == 0, errorMessage, cancellationCharge, refundAmount);
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
    }
}
