using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PickNBook.Api.Services
{
    public class SrdvFlightService : ISrdvFlightService
    {
        private readonly HttpClient _httpClient;
        private readonly SrdvSettings _settings;
        private readonly IMemoryCache _cache;
        
        private string? _tokenId;
        private DateTime _tokenExpiry;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public SrdvFlightService(HttpClient httpClient, IOptions<SrdvSettings> settings, IMemoryCache cache)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(180); // Increased from 60s to handle long GDS searches and seat map queries
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _settings = settings.Value;
            _cache = cache;

            if (!string.IsNullOrEmpty(_settings.ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Remove("Api-Token");
                _httpClient.DefaultRequestHeaders.Add("Api-Token", _settings.ApiToken);
            }
        }

        public async Task<string> AuthenticateAsync()
        {
            if (!string.IsNullOrEmpty(_tokenId) && _tokenExpiry > DateTime.UtcNow)
            {
                return _tokenId;
            }

            var requestBody = new
            {
                ClientId = _settings.ClientId,
                UserName = _settings.UserName,
                Password = _settings.Password,
                EndUserIp = "127.0.0.1" // Replace with actual logic to fetch IP if needed
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.FlightBaseUrl}/Authenticate", requestBody);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            
            if (json.RootElement.GetProperty("Status").GetInt32() == 1)
            {
                var token = json.RootElement.GetProperty("TokenId").GetString();
                _tokenId = token;
                _tokenExpiry = DateTime.UtcNow.AddHours(23); // Typical token validity
                return token!;
            }
            
            throw new Exception("SRDV Authentication Failed.");
        }

        public async Task<string> SearchFlightsRawAsync(AirSearchRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                AdultCount = request.AdultCount,
                ChildCount = request.ChildCount,
                InfantCount = request.InfantCount,
                JourneyType = string.IsNullOrWhiteSpace(request.JourneyType) ? "1" : request.JourneyType.Trim(),
                CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "INR" : request.CurrencyCode.Trim().ToUpperInvariant(),
                FareType = string.IsNullOrWhiteSpace(request.FareType) ? "1" : request.FareType.Trim(),
                DirectFlight = request.DirectFlight ?? false,
                Segments = request.Segments.Select(s => new
                {
                    Origin = s.Origin?.Trim().ToUpperInvariant() ?? string.Empty,
                    Destination = s.Destination?.Trim().ToUpperInvariant() ?? string.Empty,
                    FlightCabinClass = string.IsNullOrWhiteSpace(s.FlightCabinClass) ? "1" : s.FlightCabinClass.Trim(),
                    PreferredDepartureTime = s.PreferredDepartureTime.ToString("yyyy-MM-ddT00:00:00"),
                    PreferredArrivalTime = s.PreferredArrivalTime.ToString("yyyy-MM-ddT00:00:00")
                }).ToArray()
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/Search")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> RecheckSearchRawAsync(AirRecheckSearchRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                TraceId = request.TraceId
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/RecheckSearch")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetFareRuleRawAsync(AirFareRuleRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                TraceId = request.TraceId,
                ResultIndex = request.ResultIndex?.Trim() ?? string.Empty
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/FareRule")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<List<FlightOfferDto>> SearchFlightsAsync(
            string origin, 
            string destination, 
            DateTime departureDate, 
            int adultCount = 1, 
            int childCount = 0, 
            int infantCount = 0)
        {
            var token = await AuthenticateAsync();

            var requestBody = new
            {
                EndUserIp = "127.0.0.1",
                TokenId = token,
                AdultCount = adultCount.ToString(),
                ChildCount = childCount.ToString(),
                InfantCount = infantCount.ToString(),
                DirectFlight = "false",
                OneStopFlight = "false",
                JourneyType = "1",
                PreferredAirlines = null as string[],
                Segments = new[]
                {
                    new
                    {
                        Origin = origin,
                        Destination = destination,
                        FlightCabinClass = "1", // Economy
                        PreferredDepartureTime = departureDate.ToString("yyyy-MM-ddT00:00:00"),
                        PreferredArrivalTime = departureDate.ToString("yyyy-MM-ddT00:00:00")
                    }
                },
                Sources = null as string[]
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.FlightBaseUrl}/Search", requestBody);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            
            var offers = new List<FlightOfferDto>();
            
            if (json.RootElement.GetProperty("Response").GetProperty("ResponseStatus").GetInt32() == 1)
            {
                var traceId = json.RootElement.GetProperty("Response").GetProperty("TraceId").GetString();
                var results = json.RootElement.GetProperty("Response").GetProperty("Results")[0];
                
                foreach (var result in results.EnumerateArray())
                {
                    var isLcc = result.GetProperty("IsLcc").GetBoolean();
                    var resultIndex = result.GetProperty("ResultIndex").GetString();
                    var srdvIndex = result.TryGetProperty("SrdvIndex", out var srdvIdxProp)
                        ? (srdvIdxProp.ValueKind == JsonValueKind.String 
                            ? srdvIdxProp.GetString() ?? "" 
                            : srdvIdxProp.GetRawText())
                        : "";
                    var srdvType = result.GetProperty("SrdvType").GetString();
                    
                    var priceObj = result.GetProperty("Fare").GetProperty("PublishedFare");
                    var price = priceObj.GetDecimal();
                    
                    var segmentsJson = result.GetProperty("Segments").GetRawText();
                    var firstSegment = result.GetProperty("Segments")[0][0];
                    var lastSegment = result.GetProperty("Segments")[0][result.GetProperty("Segments")[0].GetArrayLength() - 1];
                    
                    var airline = firstSegment.GetProperty("Airline").GetProperty("AirlineName").GetString();
                    var depTime = firstSegment.GetProperty("Origin").GetProperty("DepTime").GetDateTime();
                    var arrTime = lastSegment.GetProperty("Destination").GetProperty("ArrTime").GetDateTime();
                    
                    offers.Add(new FlightOfferDto
                    {
                        Airline = airline!,
                        Origin = origin,
                        Destination = destination,
                        DepartureTime = depTime,
                        ArrivalTime = arrTime,
                        Price = price,
                        Currency = "INR",
                        AvailableSeats = result.TryGetProperty("AvailableSeats", out var seats) ? seats.GetInt32() : 9,
                        StopsCount = result.GetProperty("Segments")[0].GetArrayLength() - 1,
                        TraceId = traceId,
                        ResultIndex = resultIndex,
                        SrdvIndex = srdvIndex,
                        IsLcc = isLcc,
                        SrdvType = srdvType,
                        SegmentsJson = segmentsJson,
                        DurationMinutes = (int)(arrTime - depTime).TotalMinutes
                    });
                }
            }
            
            return offers;
        }

        public async Task<FlightFareQuoteDto> GetFareQuoteAsync(string traceId, string resultIndex)
        {
            var token = await AuthenticateAsync();
            
            var requestBody = new
            {
                EndUserIp = "127.0.0.1",
                TokenId = token,
                TraceId = traceId,
                ResultIndex = resultIndex
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.FlightBaseUrl}/FareQuote", requestBody);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            
            var res = new FlightFareQuoteDto();
            res.ResponseJson = content;
            
            if (json.RootElement.GetProperty("Response").GetProperty("ResponseStatus").GetInt32() == 1)
            {
                res.IsAvailable = json.RootElement.GetProperty("Response").GetProperty("Results").GetProperty("IsPriceChanged").GetBoolean() == false;
                res.IsPriceChanged = json.RootElement.GetProperty("Response").GetProperty("Results").GetProperty("IsPriceChanged").GetBoolean();
                res.NewPrice = json.RootElement.GetProperty("Response").GetProperty("Results").GetProperty("Fare").GetProperty("PublishedFare").GetDecimal();
            }
            
            return res;
        }

        public async Task<string> GetFareQuoteRawAsync(AirFareRuleRequestDto request)
        {
            var clientId = string.IsNullOrWhiteSpace(request.ClientId)
                ? _settings.ClientId
                : request.ClientId.Trim();

            var userName = string.IsNullOrWhiteSpace(request.UserName)
                ? _settings.UserName
                : request.UserName.Trim();

            var password = string.IsNullOrWhiteSpace(request.Password)
                ? _settings.Password
                : request.Password.Trim();

            var endUserIp = string.IsNullOrWhiteSpace(request.EndUserIp)
                ? "127.0.0.1"
                : request.EndUserIp.Trim();

            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = clientId,
                UserName = userName,
                Password = password,
                SrdvType = request.SrdvType,
                SrdvIndex = request.SrdvIndex,
                TraceId = request.TraceId,
                ResultIndex = request.ResultIndex
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/FareQuote")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetFareQuoteRawAsync(AirFareQuoteRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                TraceId = request.TraceId,
                ResultIndex = request.ResultIndex?.Trim() ?? string.Empty
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/FareQuote")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<FlightBookingResponseDto> BookFlightAsync(FlightBookingRequestDto request)
        {
            var token = await AuthenticateAsync();
            
            var requestBody = new
            {
                EndUserIp = "127.0.0.1",
                TokenId = token,
                TraceId = request.TraceId,
                ResultIndex = request.ResultIndex,
                Passengers = request.Passengers
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.FlightBaseUrl}/Book", requestBody);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            
            var res = new FlightBookingResponseDto();
            res.ResponseJson = content;
            
            if (json.RootElement.GetProperty("Response").GetProperty("ResponseStatus").GetInt32() == 1)
            {
                res.Success = true;
                res.SrdvBookingId = json.RootElement.GetProperty("Response").GetProperty("Response").GetProperty("BookingId").GetString();
                res.Pnr = json.RootElement.GetProperty("Response").GetProperty("Response").GetProperty("PNR").GetString();
            }
            else
            {
                res.Success = false;
                res.ErrorMessage = json.RootElement.GetProperty("Response").GetProperty("Error").GetProperty("ErrorMessage").GetString();
            }
            
            return res;
        }

        public async Task<FlightBookingResponseDto> TicketLccFlightAsync(FlightBookingRequestDto request)
        {
            var token = await AuthenticateAsync();
            
            var requestBody = new
            {
                EndUserIp = "127.0.0.1",
                TokenId = token,
                TraceId = request.TraceId,
                ResultIndex = request.ResultIndex,
                Passengers = request.Passengers
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.FlightBaseUrl}/Ticket", requestBody);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            
            var res = new FlightBookingResponseDto();
            res.ResponseJson = content;
            
            if (json.RootElement.GetProperty("Response").GetProperty("ResponseStatus").GetInt32() == 1)
            {
                res.Success = true;
                
                var respObj = json.RootElement.GetProperty("Response").GetProperty("Response");
                if (respObj.TryGetProperty("BookingId", out var bid))
                {
                    res.SrdvBookingId = bid.ValueKind == JsonValueKind.Number ? bid.GetInt32().ToString() : bid.GetString();
                }
                if (respObj.TryGetProperty("PNR", out var pnr))
                {
                    res.Pnr = pnr.GetString();
                }
            }
            else
            {
                res.Success = false;
                var errObj = json.RootElement.GetProperty("Response").GetProperty("Error");
                if (errObj.TryGetProperty("ErrorMessage", out var errMsg))
                {
                    res.ErrorMessage = errMsg.GetString();
                }
            }
            
            return res;
        }

        public async Task<FlightTicketResponseDto> TicketFlightAsync(string pnr, string bookingId)
        {
            var token = await AuthenticateAsync();
            
            var requestBody = new
            {
                EndUserIp = "127.0.0.1",
                TokenId = token,
                TraceId = "", // Might need from context if required, but typically Ticket takes PNR/BookingId
                PNR = pnr,
                BookingId = bookingId
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.FlightBaseUrl}/Ticket", requestBody);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            
            var res = new FlightTicketResponseDto();
            res.TicketResponseJson = content;
            
            if (json.RootElement.GetProperty("Response").GetProperty("ResponseStatus").GetInt32() == 1)
            {
                res.Success = true;
                res.Pnr = json.RootElement.GetProperty("Response").GetProperty("Response").GetProperty("PNR").GetString();
            }
            else
            {
                res.Success = false;
                res.ErrorMessage = json.RootElement.GetProperty("Response").GetProperty("Error").GetProperty("ErrorMessage").GetString();
            }
            
            return res;
        }

        public async Task<string> GetSSRRawAsync(AirFareRuleRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                TraceId = request.TraceId,
                ResultIndex = request.ResultIndex?.Trim() ?? string.Empty
            };

            var ssrUrl = $"{_settings.FlightBaseUrl.TrimEnd('/')}/SSR";
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, ssrUrl)
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetSeatMapRawAsync(AirFareRuleRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                TraceId = request.TraceId,
                ResultIndex = request.ResultIndex?.Trim() ?? string.Empty
            };

            var seatMapUrl = $"{_settings.FlightBaseUrl.TrimEnd('/')}/SeatMap";
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, seatMapUrl)
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> TicketLCCRawAsync(TicketLCCRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                TraceId = long.TryParse(request.TraceId, out var tid) ? tid : 0L,
                ResultIndex = request.ResultIndex?.Trim() ?? string.Empty,
                RefID = request.RefID ?? string.Empty,
                Module = string.IsNullOrWhiteSpace(request.Module) ? "b2c" : request.Module,
                BookedById = request.BookedById,
                BookedByName = request.BookedByName ?? string.Empty,
                CustomerFare = request.CustomerFare,
                ReturnCustomerFare = request.ReturnCustomerFare,
                Passengers = request.Passengers?.Select(p => {
                    string gender = string.IsNullOrWhiteSpace(p.Gender) ? "" : p.Gender.ToString();
                    string title = (p.Title ?? "").Trim();
                    int paxTypeInt = p.PaxType;
                    
                    if (paxTypeInt == 1 && string.IsNullOrEmpty(title))
                        title = gender == "2" ? "Ms" : "Mr";
                    else if ((paxTypeInt == 2 || paxTypeInt == 3) && string.IsNullOrEmpty(title))
                        title = gender == "2" ? "Miss" : "Mstr";
                    else if (paxTypeInt == 2 || paxTypeInt == 3)
                    {
                        if (gender == "1" && title.Equals("Mr", StringComparison.OrdinalIgnoreCase)) title = "Mstr";
                        if (gender == "2" && (title.Equals("Ms", StringComparison.OrdinalIgnoreCase) || title.Equals("Mrs", StringComparison.OrdinalIgnoreCase))) title = "Miss";
                    }

                    return new
                    {
                        Title = string.IsNullOrEmpty(title) ? "Mr" : title,
                        FirstName = p.FirstName ?? "",
                        LastName = p.LastName ?? "",
                        MiddleName = p.MiddleName ?? "",
                        PaxType = p.PaxType.ToString(),
                        DateOfBirth = string.IsNullOrWhiteSpace(p.DateOfBirth) ? "" : (DateTime.TryParse(p.DateOfBirth, out var d) ? d.ToString("yyyy-MM-dd") : p.DateOfBirth),
                        Gender = gender,
                        PassportNo = p.PassportNo ?? "",
                        PassportExpiry = string.IsNullOrWhiteSpace(p.PassportExpiry) ? "" : (DateTime.TryParse(p.PassportExpiry, out var pe) ? pe.ToString("yyyy-MM-dd") : p.PassportExpiry),
                        PassportIssueDate = string.IsNullOrWhiteSpace(p.PassportIssueDate) ? "" : (DateTime.TryParse(p.PassportIssueDate, out var pid) ? pid.ToString("yyyy-MM-dd") : p.PassportIssueDate),
                        PassportIssueCountryCode = p.PassportIssueCountryCode ?? "",
                        CountryCode = p.CountryCode ?? "",
                        CountryName = p.CountryName ?? "",
                        AddressLine1 = p.AddressLine1 ?? "",
                        City = p.City ?? "",
                        CellCountryCode = p.CellCountryCode ?? "",
                        ContactNo = p.ContactNo ?? "",
                        Email = p.Email ?? "",
                        IsLeadPax = p.IsLeadPax,
                        DocumentType = p.DocumentType ?? "",
                        DocumentId = p.DocumentId ?? "",
                        Baggage = p.Baggage?.Where(b => !string.IsNullOrWhiteSpace(b.Code) && !b.Code.Equals("string", StringComparison.OrdinalIgnoreCase))
                            .Select(b => new { Code = b.Code, Price = (object?)b.Price }).ToList() ?? new(),
                        MealDynamic = p.MealDynamic?.Where(m => !string.IsNullOrWhiteSpace(m.Code) && !m.Code.Equals("string", StringComparison.OrdinalIgnoreCase))
                            .Select(m => new { Code = m.Code, Price = (object?)m.Price }).ToList() ?? new(),
                        Seat = p.Seat?.Where(s => !string.IsNullOrWhiteSpace(s.Code) && !s.Code.Equals("string", StringComparison.OrdinalIgnoreCase))
                            .Select(s => new { Code = s.Code, Amount = (object?)s.Amount }).ToList() ?? new(),
                        GSTNumber = p.GSTNumber ?? "",
                        GSTCompanyName = p.GSTCompanyName ?? "",
                        GSTCompanyAddress = p.GSTCompanyAddress ?? "",
                        GSTCompanyEmail = p.GSTCompanyEmail ?? "",
                        GSTCompanyContactNumber = p.GSTCompanyContactNumber ?? "",
                        EContactName = p.EContactName ?? "",
                        EContactEmail = p.EContactEmail ?? "",
                        EContactMobile = p.EContactMobile ?? ""
                    };
                }).ToList()
            };

            var ticketUrl = $"{_settings.FlightBaseUrl.TrimEnd('/')}/TicketLCC";
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, ticketUrl)
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> HoldGDSRawAsync(HoldGDSRequestDto request)
        {
            var clientId = string.IsNullOrWhiteSpace(request.ClientId) 
                ? _settings.ClientId 
                : request.ClientId.Trim();

            var userName = string.IsNullOrWhiteSpace(request.UserName) 
                ? _settings.UserName 
                : request.UserName.Trim();

            var password = string.IsNullOrWhiteSpace(request.Password) 
                ? _settings.Password 
                : request.Password.Trim();

            var endUserIp = string.IsNullOrWhiteSpace(request.EndUserIp) 
                ? "127.0.0.1" 
                : request.EndUserIp.Trim();

            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = clientId,
                UserName = userName,
                Password = password,
                SrdvType = request.SrdvType,
                SrdvIndex = request.SrdvIndex,
                TraceId = request.TraceId,
                ResultIndex = request.ResultIndex,
                PromoCode = request.PromoCode,
                Passengers = request.Passengers?.Select(p => new
                {
                    Title = p.Title,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    PaxType = p.PaxType,
                    DateOfBirth = string.IsNullOrWhiteSpace(p.DateOfBirth) ? "" : (DateTime.TryParse(p.DateOfBirth, out var d) ? d.ToString("yyyy-MM-dd") : p.DateOfBirth),
                    Gender = p.Gender.ToString(),
                    PassportNo = string.IsNullOrWhiteSpace(p.PassportNo) ? "" : p.PassportNo,
                    PassportExpiry = string.IsNullOrWhiteSpace(p.PassportExpiry) ? "" : (DateTime.TryParse(p.PassportExpiry, out var pe) ? pe.ToString("yyyy-MM-dd") : p.PassportExpiry),
                    PassportIssueDate = string.IsNullOrWhiteSpace(p.PassportIssueDate) ? "" : (DateTime.TryParse(p.PassportIssueDate, out var pid) ? pid.ToString("yyyy-MM-dd") : p.PassportIssueDate),
                    AddressLine1 = p.AddressLine1,
                    City = p.City,
                    CountryCode = p.CountryCode,
                    CountryName = p.CountryName,
                    ContactNo = p.ContactNo,
                    Email = p.Email,
                    IsLeadPax = p.IsLeadPax ? 1 : 0,
                    GSTCompanyAddress = p.GSTCompanyAddress ?? "",
                    GSTCompanyContactNumber = p.GSTCompanyContactNumber ?? "",
                    GSTCompanyName = p.GSTCompanyName ?? "",
                    GSTNumber = p.GSTNumber ?? "",
                    GSTCompanyEmail = p.GSTCompanyEmail ?? "",
                    Fare = new
                    {
                        Currency = "INR",
                        BaseFare = p.Fare?.BaseFare ?? 0,
                        Tax = p.Fare?.Tax ?? 0,
                        YQTax = p.Fare?.YQTax ?? 0,
                        OtherCharges = p.Fare?.OtherCharges ?? 0,
                        TransactionFee = p.Fare?.TransactionFee ?? 0,
                        AdditionalTxnFeeOfrd = p.Fare?.AdditionalTxnFeeOfrd ?? 0,
                        AdditionalTxnFeePub = p.Fare?.AdditionalTxnFeePub ?? 0,
                        AirTransFee = p.Fare?.AirTransFee ?? 0
                    },
                    Baggage = p.Baggage,
                    MealDynamic = p.MealDynamic,
                    Seat = p.Seat
                }).ToList()
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/Hold")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> TicketGDSRawAsync(TicketGDSRequestDto request)
        {
            var clientId = string.IsNullOrWhiteSpace(request.ClientId) 
                ? _settings.ClientId 
                : request.ClientId.Trim();

            var userName = string.IsNullOrWhiteSpace(request.UserName) 
                ? _settings.UserName 
                : request.UserName.Trim();

            var password = string.IsNullOrWhiteSpace(request.Password) 
                ? _settings.Password 
                : request.Password.Trim();

            var endUserIp = string.IsNullOrWhiteSpace(request.EndUserIp) 
                ? "127.0.0.1" 
                : request.EndUserIp.Trim();

            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = clientId,
                UserName = userName,
                Password = password,
                SrdvType = request.SrdvType,
                SrdvIndex = request.SrdvIndex,
                TraceId = request.TraceId,
                PNR = request.PNR,
                ResultIndex = request.ResultIndex,
                BookingId = request.BookingId,
                PromoCode = request.PromoCode
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/TicketGDS")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public Task<string> GetBookingDetailsRawAsync(long traceId)
        {
            return GetBookingDetailsRawAsync(new AirBookingDetailsRequestDto { TraceId = traceId });
        }

        public async Task<string> GetBookingDetailsRawAsync(AirBookingDetailsRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                TraceId = request.TraceId
            };

            var targetUrl = $"{_settings.FlightBaseUrl.TrimEnd('/')}/BookingDetails";
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetCalendarFareRawAsync(CalendarFareRequestDto request)
        {
            var clientId = string.IsNullOrWhiteSpace(request.ClientId) 
                ? _settings.ClientId 
                : request.ClientId.Trim();

            var userName = string.IsNullOrWhiteSpace(request.UserName) 
                ? _settings.UserName 
                : request.UserName.Trim();

            var password = string.IsNullOrWhiteSpace(request.Password) 
                ? _settings.Password 
                : request.Password.Trim();

            var endUserIp = string.IsNullOrWhiteSpace(request.EndUserIp) 
                ? "127.0.0.1" 
                : request.EndUserIp.Trim();

            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = clientId,
                UserName = userName,
                Password = password,
                JourneyType = request.JourneyType,
                Sources = request.Sources,
                FareType = request.FareType,
                Segments = request.Segments
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/GetCalendarFare")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> SendChangeRequestRawAsync(SendChangeRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                BookingId = request.BookingId,
                RequestType = request.RequestType,
                CancellationType = request.CancellationType,
                PNR = request.PNR?.Trim() ?? string.Empty,
                Remarks = request.Remarks?.Trim() ?? string.Empty,
                ClientRefId = request.ClientRefId ?? string.Empty,
                Sectors = (request.Sectors ?? new List<ChangeRequestSectorDto>()).Select(s => new
                {
                    Origin = s.Origin?.Trim().ToUpperInvariant() ?? string.Empty,
                    Destination = s.Destination?.Trim().ToUpperInvariant() ?? string.Empty
                }).ToList(),
                TicketData = (request.TicketData ?? new List<ChangeRequestTicketDataDto>()).Select(t => new
                {
                    FirstName = t.FirstName?.Trim() ?? string.Empty,
                    LastName = t.LastName?.Trim() ?? string.Empty,
                    TicketId = t.TicketId?.Trim() ?? string.Empty
                }).ToList()
            };

            var targetUrl = $"{_settings.FlightBaseUrl.TrimEnd('/')}/SendChangeRequest";
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetCancelStatusRawAsync(GetCancelStatusRequestDto request)
        {
            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                ChangeRequestId = request.ChangeRequestId
            };

            var targetUrl = $"{_settings.FlightBaseUrl.TrimEnd('/')}/GetCancelStatus";
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetCancellationChargesRawAsync(GetCancellationChargesRequestDto request)
        {
            var endUserIp = string.IsNullOrWhiteSpace(request.EndUserIp)
                ? "127.0.0.1"
                : request.EndUserIp.Trim();

            var clientId = string.IsNullOrWhiteSpace(request.ClientId)
                ? _settings.ClientId
                : request.ClientId.Trim();

            var userName = string.IsNullOrWhiteSpace(request.UserName)
                ? _settings.UserName
                : request.UserName.Trim();

            var password = string.IsNullOrWhiteSpace(request.Password)
                ? _settings.Password
                : request.Password.Trim();

            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = clientId,
                UserName = userName,
                Password = password,
                RequestType = request.RequestType.ToString(),
                TraceId = request.TraceId
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/GetCancellationCharges")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
        public async Task<string> GetApiBalanceCheckRawAsync(ApiBalanceRequestDto request)
        {
            var endUserIp = string.IsNullOrWhiteSpace(request.EndUserIp)
                ? "127.0.0.1"
                : request.EndUserIp.Trim();

            var clientId = string.IsNullOrWhiteSpace(request.ClientId)
                ? _settings.ClientId
                : request.ClientId.Trim();

            var userName = string.IsNullOrWhiteSpace(request.UserName)
                ? _settings.UserName
                : request.UserName.Trim();

            var password = string.IsNullOrWhiteSpace(request.Password)
                ? _settings.Password
                : request.Password.Trim();

            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = clientId,
                UserName = userName,
                Password = password
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/Balance")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetApiBalanceLogRawAsync(ApiBalanceRequestDto request)
        {
            var endUserIp = string.IsNullOrWhiteSpace(request.EndUserIp)
                ? "127.0.0.1"
                : request.EndUserIp.Trim();

            var clientId = string.IsNullOrWhiteSpace(request.ClientId)
                ? _settings.ClientId
                : request.ClientId.Trim();

            var userName = string.IsNullOrWhiteSpace(request.UserName)
                ? _settings.UserName
                : request.UserName.Trim();

            var password = string.IsNullOrWhiteSpace(request.Password)
                ? _settings.Password
                : request.Password.Trim();

            var apiToken = string.IsNullOrWhiteSpace(request.ApiToken)
                ? _settings.ApiToken
                : request.ApiToken.Trim();

            var requestBody = new
            {
                EndUserIp = endUserIp,
                ClientId = clientId,
                UserName = userName,
                Password = password
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.FlightBaseUrl}/BalanceLog")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", apiToken);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
    }
}


