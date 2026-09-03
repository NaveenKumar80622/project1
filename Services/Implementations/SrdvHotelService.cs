using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PickNBook.Api.Models;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace PickNBook.Api.Services
{
    public class SrdvHotelService : IHotelService
    {
        private readonly HttpClient _httpClient;
        private readonly SrdvSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SrdvHotelService>? _logger;
        private readonly IServiceProvider _serviceProvider;
        
        public SrdvHotelService(HttpClient httpClient, IOptions<SrdvSettings> settings, IMemoryCache cache, IServiceProvider serviceProvider, ILogger<SrdvHotelService>? logger = null)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(180); // Increased from 60s to handle slow responses
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _settings = settings.Value;
            _cache = cache;
            _serviceProvider = serviceProvider;
            _logger = logger;

            if (!string.IsNullOrEmpty(_settings.ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Remove("Api-Token");
                _httpClient.DefaultRequestHeaders.Add("Api-Token", _settings.ApiToken);
            }
        }

        public async Task<PickNBookHotelSearchResponseDto> SearchHotelsAsync(
            string cityCode,
            DateTime checkInDate,
            DateTime checkOutDate,
            int adults = 1,
            int rooms = 1,
            int children = 0,
            int[]? childAges = null,
            string preferredHotel = "",
            int minRating = 1,
            int maxRating = 5,
            string bookingMode = "5",
            string guestNationality = "IN")
        {
            int noOfNights = Math.Max(1, (checkOutDate - checkInDate).Days);
            var req = new SrdvHotelSearchRequestDto
            {
                EndUserIp = "127.0.0.1",
                ClientId = _settings.ClientId,
                UserName = _settings.UserName,
                Password = _settings.Password,
                CheckInDate = checkInDate.ToString("yyyy-MM-dd"),
                CheckOutDate = checkOutDate.ToString("yyyy-MM-dd"),
                NoOfNights = noOfNights.ToString(),
                BookingMode = string.IsNullOrWhiteSpace(bookingMode) ? "5" : bookingMode,
                CountryCode = "IN",
                CityId = cityCode,
                ResultCount = "500",
                PreferredCurrency = "INR",
                GuestNationality = string.IsNullOrWhiteSpace(guestNationality) ? "IN" : guestNationality,
                NoOfRooms = Math.Max(1, rooms).ToString(),
                RoomGuests = new List<RoomGuestDto>
                {
                    new RoomGuestDto
                    {
                        NoOfAdults = Math.Max(1, adults).ToString(),
                        NoOfChild = Math.Max(0, children).ToString(),
                        ChildAge = childAges != null ? new List<int>(childAges) : new List<int>()
                    }
                },
                PreferredHotel = preferredHotel ?? "",
                MaxRating = maxRating.ToString(),
                MinRating = minRating.ToString(),
                ReviewScore = null,
                IsNearBySearchAllowed = false
            };

            return await SearchHotelsMultiLevelAsync(req);
        }

        public async Task<PickNBookHotelSearchResponseDto> SearchHotelsMultiLevelAsync(SrdvHotelSearchRequestDto request)
        {
            string guestDetails = "";
            if (request.RoomGuests != null)
            {
                guestDetails = string.Join("_", request.RoomGuests.Select(rg => $"{rg.NoOfAdults}-{rg.NoOfChild}"));
            }
            var cacheKey = $"HotelSearch_{request.CityId}_{request.CheckInDate}_{request.CheckOutDate}_{request.NoOfRooms}_{guestDetails}";

            if (_cache.TryGetValue(cacheKey, out PickNBookHotelSearchResponseDto? cachedResponse))
            {
                return cachedResponse!;
            }

            var response = await SearchHotelsMultiLevelRawAsync(request);
            
            // Only cache if there's no error
            if (response != null && (response.Error == null || response.Error.ErrorCode == 0))
            {
                _cache.Set(cacheKey, response, TimeSpan.FromMinutes(15));
            }

            return response ?? new PickNBookHotelSearchResponseDto();
        }

        private async Task<PickNBookHotelSearchResponseDto> SearchHotelsMultiLevelRawAsync(SrdvHotelSearchRequestDto request)
        {
            request.ClientId = _settings.ClientId;
            request.UserName = _settings.UserName;
            request.Password = _settings.Password;
            
            if (string.IsNullOrWhiteSpace(request.CountryCode)) request.CountryCode = "IN";
            if (string.IsNullOrWhiteSpace(request.ResultCount)) request.ResultCount = "500";
            if (string.IsNullOrWhiteSpace(request.PreferredCurrency)) request.PreferredCurrency = "INR";
            if (string.IsNullOrWhiteSpace(request.GuestNationality)) request.GuestNationality = "IN";
            if (request.RoomGuests == null || request.RoomGuests.Count == 0)
            {
                request.RoomGuests = new List<RoomGuestDto> { new RoomGuestDto { NoOfAdults = "1", NoOfChild = "0", ChildAge = new List<int>() } };
            }

            if (string.IsNullOrWhiteSpace(request.RequestType) && !string.IsNullOrWhiteSpace(request.CityId))
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetService<AppDbContext>();
                    if (db != null)
                    {
                        var match = db.HotelCities.AsNoTracking().FirstOrDefault(h => h.CityCode == request.CityId && h.IsActive);
                        if (match != null && !string.IsNullOrWhiteSpace(match.RequestType))
                        {
                            request.RequestType = match.RequestType;
                        }
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(request.RequestType))
                {
                    var cacheSvc = _serviceProvider.GetService<HotelCityCacheService>();
                    if (cacheSvc != null)
                    {
                        if (cacheSvc.SpecialCityIds.Contains(request.CityId))
                        {
                            request.RequestType = "Special";
                        }
                        else if (cacheSvc.InternationalCityIds.Contains(request.CityId))
                        {
                            request.RequestType = "International";
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(_settings.ApiToken))
            {
                _httpClient.DefaultRequestHeaders.Remove("Api-Token");
                _httpClient.DefaultRequestHeaders.Add("Api-Token", _settings.ApiToken);
            }

            try
            {
                // ── [SUPPLIER-DEBUG] Log the EXACT outbound payload ──
                var outboundJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
                _logger?.LogWarning("[SUPPLIER-DEBUG] Outbound request to {Url}:\n{Payload}", $"{_settings.HotelBaseUrl}/Search", outboundJson);

                var response = await _httpClient.PostAsJsonAsync($"{_settings.HotelBaseUrl}/Search", request);
                response.EnsureSuccessStatusCode();

                // ── [SUPPLIER-DEBUG] Streaming deserialization ──
                using var stream = await response.Content.ReadAsStreamAsync();
                var serializerOptions = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString 
                };
                serializerOptions.Converters.Add(new PickNBook.Api.Models.DTOs.SafeListConverterFactory());
                
                var srdvResponse = await JsonSerializer.DeserializeAsync<SrdvRawHotelSearchResponse>(stream, serializerOptions);

                if (srdvResponse == null)
                    return new PickNBookHotelSearchResponseDto();

                var responseDto = new PickNBookHotelSearchResponseDto
                {
                    SrdvType = srdvResponse.SrdvType,
                    CityId = string.IsNullOrEmpty(srdvResponse.CityId) ? request.CityId : srdvResponse.CityId,
                    Remarks = srdvResponse.Remarks,
                    CheckInDate = string.IsNullOrEmpty(srdvResponse.CheckInDate) ? request.CheckInDate : srdvResponse.CheckInDate,
                    CheckOutDate = string.IsNullOrEmpty(srdvResponse.CheckOutDate) ? request.CheckOutDate : srdvResponse.CheckOutDate,
                    PreferredCurrency = srdvResponse.PreferredCurrency,
                    TraceId = long.TryParse(srdvResponse.TraceId, out var tid) ? tid : (srdvResponse.TraceId != null ? srdvResponse.TraceId.GetHashCode() : 0)
                };

                if (srdvResponse.Error != null)
                {
                    responseDto.Error.ErrorCode = srdvResponse.Error.ErrorCode;
                    responseDto.Error.ErrorMessage = srdvResponse.Error.ErrorMessage;
                    if (srdvResponse.Error.ErrorCode != 0)
                    {
                        _logger?.LogWarning("SRDV Hotel Search API returned supplier error ({ErrorCode}): {ErrorMessage}", srdvResponse.Error.ErrorCode, srdvResponse.Error.ErrorMessage);
                    }
                }

                if (srdvResponse.NoOfRooms != null)
                {
                    foreach (var nr in srdvResponse.NoOfRooms)
                    {
                        var nrDto = new HotelSearchNoOfRoomsDto
                        {
                            NoOfAdults = nr.NoOfAdults,
                            NoOfChild = nr.NoOfChild
                        };
                        if (nr.ChildAge != null)
                        {
                            foreach (var c in nr.ChildAge)
                            {
                                if (c.ValueKind == JsonValueKind.Number) nrDto.ChildAge.Add(c.GetInt32());
                            }
                        }
                        responseDto.NoOfRooms.Add(nrDto);
                    }
                }
                else
                {
                    foreach (var rg in request.RoomGuests)
                    {
                        responseDto.NoOfRooms.Add(new HotelSearchNoOfRoomsDto { NoOfAdults = rg.NoOfAdults, NoOfChild = rg.NoOfChild, ChildAge = new List<int>(rg.ChildAge) });
                    }
                }

                using var markupScope = _serviceProvider.CreateScope();
                var markupService = markupScope.ServiceProvider.GetService<IHotelMarkupService>();

                if (srdvResponse.Results != null)
                {
                    foreach (var hotel in srdvResponse.Results)
                    {
                        var item = new HotelSearchResultItemDto
                        {
                            SrdvIndex = hotel.SrdvIndex,
                            ResultIndex = hotel.ResultIndex,
                            OfferedFare = hotel.OfferedFare,
                            HotelCode = hotel.HotelCode,
                            HotelName = hotel.HotelName,
                            HotelCategory = hotel.HotelCategory,
                            StarRating = hotel.StarRating,
                            HotelDescription = hotel.HotelDescription,
                            HotelPromotion = hotel.HotelPromotion,
                            HotelPolicy = hotel.HotelPolicy,
                            HotelPicture = hotel.HotelPicture,
                            HotelAddress = hotel.HotelAddress,
                            City = hotel.City,
                            State = hotel.State,
                            PinCode = hotel.PinCode,
                            Country = hotel.Country,
                            HotelContactNo = hotel.HotelContactNo,
                            HotelMap = hotel.HotelMap,
                            Latitude = hotel.Latitude,
                            Longitude = hotel.Longitude,
                            HotelLocation = hotel.HotelLocation,
                            SupplierPrice = hotel.SupplierPrice
                        };

                        if (hotel.Facilities != null)
                        {
                            foreach (var fac in hotel.Facilities)
                            {
                                var facItem = new HotelSearchFacilityItemDto { RoomPrice = fac.RoomPrice };
                                if (fac.FacilitiesNames != null)
                                {
                                    foreach (var fn in fac.FacilitiesNames)
                                    {
                                        if (fn.ValueKind == JsonValueKind.String) facItem.FacilitiesNames.Add(fn.GetString() ?? "");
                                    }
                                }
                                item.Facilities.Add(facItem);
                            }
                        }

                        if (hotel.Rooms != null)
                        {
                            foreach (var rm in hotel.Rooms)
                            {
                                item.Rooms.Add(new HotelSearchRoomCategoryDto { Cateogry = string.IsNullOrEmpty(rm.Category) ? rm.Cateogry : rm.Category });
                            }
                        }

                        var rawPrice = hotel.Price;
                        if (rawPrice != null)
                        {
                            item.Price = new HotelSearchPriceDto
                            {
                                CurrencyCode = rawPrice.CurrencyCode,
                                RoomPrice = rawPrice.RoomPrice,
                                Tax = rawPrice.Tax,
                                ExtraGuestCharge = rawPrice.ExtraGuestCharge,
                                ChildCharge = rawPrice.ChildCharge,
                                OtherCharges = rawPrice.OtherCharges,
                                Discount = rawPrice.Discount,
                                PublishedPrice = rawPrice.PublishedPrice,
                                PublishedPriceRoundedOff = rawPrice.PublishedPriceRoundedOff,
                                OfferedPrice = rawPrice.OfferedPrice,
                                OfferedPriceRoundedOff = rawPrice.OfferedPriceRoundedOff,
                                ServiceTax = rawPrice.ServiceTax,
                                TDS = rawPrice.TDS,
                                ServiceCharge = rawPrice.ServiceCharge,
                                TotalGSTAmount = rawPrice.TotalGSTAmount,
                                B2CTotalPrice = rawPrice.OfferedPrice,
                                B2CBasePrice = Math.Max(0m, rawPrice.OfferedPrice - rawPrice.TotalGSTAmount)
                            };
                            
                            if (rawPrice.GST != null)
                            {
                                item.Price.GST = new HotelSearchGstDto
                                {
                                    CGSTAmount = rawPrice.GST.CGSTAmount,
                                    CGSTRate = rawPrice.GST.CGSTRate,
                                    CessAmount = rawPrice.GST.CessAmount,
                                    CessRate = rawPrice.GST.CessRate,
                                    IGSTAmount = rawPrice.GST.IGSTAmount,
                                    IGSTRate = rawPrice.GST.IGSTRate,
                                    SGSTAmount = rawPrice.GST.SGSTAmount,
                                    SGSTRate = rawPrice.GST.SGSTRate,
                                    TaxableAmount = rawPrice.GST.TaxableAmount
                                };
                            }
                        }

                        var offerDto = new HotelOfferDto
                        {
                            OfferId = item.ResultIndex,
                            HotelId = item.HotelCode,
                            HotelName = item.HotelName,
                            CityCode = request.CityId,
                            Latitude = double.TryParse(item.Latitude, out var ltVal) ? ltVal : null,
                            Longitude = double.TryParse(item.Longitude, out var lgVal) ? lgVal : null,
                            Address = item.HotelAddress,
                            CheckInDate = request.CheckInDate,
                            CheckOutDate = request.CheckOutDate,
                            RoomQuantity = int.TryParse(request.NoOfRooms, out var rmQty) ? rmQty : 1,
                            AdultQuantity = request.RoomGuests.Count > 0 && int.TryParse(request.RoomGuests[0].NoOfAdults, out var aq) ? aq : 1,
                            ChildQuantity = request.RoomGuests.Count > 0 && int.TryParse(request.RoomGuests[0].NoOfChild, out var cq) ? cq : 0,
                            Price = item.OfferedFare,
                            Currency = item.Price?.CurrencyCode ?? "INR",
                            SrdvIndex = int.TryParse(item.SrdvIndex, out var siVal) ? siVal : 0,
                            TraceId = responseDto.TraceId.ToString(),
                            ResultIndex = item.ResultIndex
                        };
                        _cache.Set(item.HotelCode, offerDto, TimeSpan.FromMinutes(30));
                        _cache.Set(item.ResultIndex, offerDto, TimeSpan.FromMinutes(30));

                        // Calculate & Apply Hotel Markup
                        try
                        {
                            if (markupService != null && item.Price != null)
                            {
                                await ApplyMarkupAndGstAsync(markupService, item.Price, request.CityId, item.HotelCode, "B2C");
                                item.OfferedFare = item.Price.OfferedPrice;
                                offerDto.Price = item.OfferedFare;
                            }
                        }
                        catch (Exception mkEx)
                        {
                            _logger?.LogWarning(mkEx, "Failed to apply hotel search markup for HotelCode {HotelCode}", item.HotelCode);
                        }

                        responseDto.Results.Add(item);
                    }
                }


                return responseDto;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SRDV API unreachable or error occurred during SearchHotels: {Error}", ex.Message);
                return new PickNBookHotelSearchResponseDto();
            }
        }

        public async Task<HotelOfferDto?> GetOfferDetailsAsync(string offerId)
        {
            if (string.IsNullOrEmpty(offerId)) return null;

            if (_logger != null) _logger.LogDebug("GetOfferDetailsAsync invoked with offerId: '{OfferId}'", offerId);

            if (offerId.StartsWith("offer-"))
            {
                offerId = offerId.Substring(6);
            }
            
            if (_cache.TryGetValue(offerId, out HotelOfferDto? offer))
            {
                if (_logger != null) _logger.LogDebug("Found EXACT match in cache for '{OfferId}'", offerId);
                return await Task.FromResult(offer);
            }

            // Fallback for composite OfferId: TraceId_ResultIndex_HotelCode...
            var parts = offerId.Split('_');
            foreach (var part in parts)
            {
                // Remove trailing -roomIndex if it exists to match the cache key
                var cleanPart = part;
                if (cleanPart.Contains("-") && cleanPart.LastIndexOf('-') > 0)
                {
                    var possibleIdx = cleanPart.Substring(cleanPart.LastIndexOf('-') + 1);
                    if (int.TryParse(possibleIdx, out _))
                    {
                        cleanPart = cleanPart.Substring(0, cleanPart.LastIndexOf('-'));
                    }
                }

                if (_logger != null) _logger.LogDebug("Checking cache for part: '{Part}' and cleanPart: '{CleanPart}'", part, cleanPart);

                if (!string.IsNullOrEmpty(cleanPart) && _cache.TryGetValue(cleanPart, out HotelOfferDto? fallbackOffer))
                {
                    if (_logger != null) _logger.LogDebug("Found match for cleanPart: '{CleanPart}'", cleanPart);
                    return await Task.FromResult(fallbackOffer);
                }
                if (!string.IsNullOrEmpty(part) && _cache.TryGetValue(part, out HotelOfferDto? fallbackOfferExact))
                {
                    if (_logger != null) _logger.LogDebug("Found match for part: '{Part}'", part);
                    return await Task.FromResult(fallbackOfferExact);
                }
            }

            if (_logger != null) _logger.LogDebug("No match found in cache for any part of '{OfferId}'", offerId);
            return null;
        }

        public async Task<HotelBookingResponseDto> BookHotelAsync(
            string offerId,
            string guestName,
            string guestEmail,
            string guestPhone,
            string userId)
        {
            var offer = await GetOfferDetailsAsync(offerId);
            if (offer == null || string.IsNullOrEmpty(offer.TraceId) || string.IsNullOrEmpty(offer.ResultIndex))
                throw new Exception("Invalid or expired offer.");

            PickNBookBlockRoomResponseDto? blockResponse = null;
            if (_cache.TryGetValue($"block_{offer.ResultIndex}", out PickNBookBlockRoomResponseDto? cachedBlock) && cachedBlock != null)
            {
                _logger?.LogInformation("Found BlockRoomResult in cache for ResultIndex {ResultIndex}", offer.ResultIndex);
                blockResponse = cachedBlock;
            }
            else
            {
                var blockReq = new BlockRoomRequestDto
                {
                    TraceId = offer.TraceId,
                    ResultIndex = offer.ResultIndex,
                    HotelCode = offer.HotelCode!,
                    HotelName = offer.HotelName,
                    NoOfRooms = offer.RoomQuantity,
                    Price = offer.Price
                };
                blockResponse = await BlockRoomAsync(blockReq);
            }
            var blockResult = blockResponse?.BlockRoomResult;

            var nameParts = guestName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string firstName = nameParts.Length > 0 ? nameParts[0] : "Guest";
            string lastName  = nameParts.Length > 1 ? nameParts[1] : "User";

            var passenger = new
            {
                Title = "Mr", FirstName = firstName, MiddleName = (string?)null, LastName = lastName,
                Phoneno = guestPhone, Email = guestEmail, PaxType = "1", LeadPassenger = true,
                PassportNo = (string?)null, PassportIssueDate = (string?)null, PassportExpDate = (string?)null,
                PAN = (string?)null, GSTCompanyAddress = "", GSTCompanyContactNumber = "",
                GSTCompanyName = "", GSTNumber = "", GSTCompanyEmail = ""
            };

            List<object> hotelRoomsDetails;
            if (blockResult?.HotelRoomsDetails != null && blockResult.HotelRoomsDetails.Count > 0)
            {
                hotelRoomsDetails = blockResult.HotelRoomsDetails.Select(room => (object)new
                {
                    ChildCount = room.ChildCount, RequireAllPaxDetails = room.RequireAllPaxDetails,
                    RoomId = room.RoomId, RoomStatus = room.RoomStatus, RoomIndex = room.RoomIndex,
                    RoomTypeCode = room.RoomTypeCode, RoomTypeName = room.RoomTypeName,
                    RatePlan = room.RatePlan, RatePlanCode = room.RatePlanCode,
                    InfoSource = room.InfoSource, SequenceNo = room.SequenceNo,
                    DayRates = room.DayRates.Select(d => new { d.Amount, d.Date }).ToList(),
                    SupplierPrice = room.SupplierPrice,
                    Price = new
                    {
                        CurrencyCode = room.Price.CurrencyCode, RoomPrice = room.Price.RoomPrice,
                        Tax = room.Price.Tax, ExtraGuestCharge = room.Price.ExtraGuestCharge,
                        ChildCharge = room.Price.ChildCharge, OtherCharges = room.Price.OtherCharges,
                        Discount = room.Price.Discount, PublishedPrice = room.Price.PublishedPrice,
                        PublishedPriceRoundedOff = room.Price.PublishedPriceRoundedOff,
                        OfferedPrice = room.Price.OfferedPrice, OfferedPriceRoundedOff = room.Price.OfferedPriceRoundedOff,
                        AgentCommission = room.Price.AgentCommission, AgentMarkUp = room.Price.AgentMarkUp,
                        ServiceTax = room.Price.ServiceTax, TDS = room.Price.TDS,
                        ServiceCharge = room.Price.ServiceCharge, TotalGSTAmount = room.Price.TotalGSTAmount,
                        GST = new
                        {
                            CGSTAmount = room.Price.GST.CGSTAmount, CGSTRate = room.Price.GST.CGSTRate,
                            CessAmount = room.Price.GST.CessAmount, CessRate = room.Price.GST.CessRate,
                            IGSTAmount = room.Price.GST.IGSTAmount, IGSTRate = room.Price.GST.IGSTRate,
                            SGSTAmount = room.Price.GST.SGSTAmount, SGSTRate = room.Price.GST.SGSTRate,
                            TaxableAmount = room.Price.GST.TaxableAmount
                        }
                    },
                    HotelPassenger = new[] { passenger },
                    RoomPromotion = room.RoomPromotion,
                    Amenities = room.Amenities.Select(a => new { a.Name, a.FontAwesome, a.IcoFont }).ToList(),
                    SmokingPreference = room.SmokingPreference, BedTypes = room.BedTypes,
                    HotelSupplements = room.HotelSupplements, LastCancellationDate = room.LastCancellationDate,
                    CancellationPolicies = room.CancellationPolicies.Select(cp => new
                    { cp.Charge, cp.ChargeType, cp.Currency, cp.FromDate, cp.ToDate }).ToList(),
                    BedTypeCode = (string?)null, Supplements = (string?)null
                }).ToList();
            }
            else
            {
                throw new Exception("BlockRoom data is required before booking. Please call BlockRoom first.");
            }

            var bookPayload = new
            {
                EndUserIp = "127.0.0.1",
                ClientId = _settings.ClientId,
                UserName = _settings.UserName,
                Password = _settings.Password,
                TokenId = "",
                TraceId = int.TryParse(offer.TraceId, out var tid) ? (object)tid : offer.TraceId,
                SrdvType = "MixAPI",
                SrdvIndex = offer.SrdvIndex > 0 ? offer.SrdvIndex.ToString() : "",
                ResultIndex = offer.ResultIndex,
                HotelCode = offer.HotelCode,
                HotelName = offer.HotelName,
                GuestNationality = "IN",
                NoOfRooms = (offer.RoomQuantity > 0 ? offer.RoomQuantity : 1).ToString(),
                ClientReferenceNo = 0,
                IsVoucherBooking = true,
                HotelRoomsDetails = hotelRoomsDetails
            };

            _logger?.LogInformation("Calling SRDV /Book HotelCode: {HotelCode}, TraceId: {TraceId}", offer.HotelCode, offer.TraceId);


            PickNBookBookRoomResponseDto bookRes;
            try
            {
                var httpRes = await _httpClient.PostAsJsonAsync($"{_settings.HotelBaseUrl}/Book", bookPayload);
                var contentStr = await httpRes.Content.ReadAsStringAsync();
                _logger?.LogInformation("SRDV /Book HTTP {Status}: {Preview}", httpRes.StatusCode,
                    contentStr.Length > 300 ? contentStr.Substring(0, 300) : contentStr);

                if (!httpRes.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("SRDV /Book HTTP {Status}. Failing request.", httpRes.StatusCode);
                    throw new Exception($"SRDV Provider returned HTTP {httpRes.StatusCode} during Book");
                }
                else
                {
                    using var jsonDoc = JsonDocument.Parse(contentStr);
                    var root = jsonDoc.RootElement;
                    var responseDto = new PickNBookBookRoomResponseDto();
                    var resDto = responseDto.BookResult;
                    var target = root.TryGetProperty("BookResult", out var brProp) && brProp.ValueKind == JsonValueKind.Object ? brProp : root;

                    if (target.TryGetProperty("Error", out var errProp) && errProp.ValueKind == JsonValueKind.Object)
                    {
                        if (errProp.TryGetProperty("ErrorCode", out var ec))
                        {
                            if (ec.ValueKind == JsonValueKind.Number) 
                                resDto.Error.ErrorCode = ec.GetInt32();
                            else if (ec.ValueKind == JsonValueKind.String && int.TryParse(ec.GetString(), out int parsedCode)) 
                                resDto.Error.ErrorCode = parsedCode;
                        }
                        if (errProp.TryGetProperty("ErrorMessage", out var em)) resDto.Error.ErrorMessage = em.GetString() ?? "";
                    }
                    if (resDto.Error.ErrorCode != 0 || !string.IsNullOrEmpty(resDto.Error.ErrorMessage))
                    {
                        _logger?.LogWarning("SRDV API Book returned error: {ErrorCode} - {ErrorMessage}", resDto.Error.ErrorCode, resDto.Error.ErrorMessage);
                        bookRes = responseDto;
                    }
                    else
                    {
                        if (target.TryGetProperty("VoucherStatus", out var vs)) resDto.VoucherStatus = vs.ValueKind == JsonValueKind.True;
                        if (target.TryGetProperty("ResponseStatus", out var rs) && rs.ValueKind == JsonValueKind.Number) resDto.ResponseStatus = rs.GetInt32();
                        if (target.TryGetProperty("TraceId", out var ti)) resDto.TraceId = ti.ValueKind == JsonValueKind.Number ? ti.GetRawText() : (ti.GetString() ?? "");
                        if (target.TryGetProperty("Status", out var st)) resDto.Status = st.GetString() ?? "Confirmed";
                        if (target.TryGetProperty("HotelBookingStatus", out var hbs)) resDto.HotelBookingStatus = hbs.GetString() ?? "Confirmed";
                        if (target.TryGetProperty("InvoiceNumber", out var inv)) resDto.InvoiceNumber = inv.GetString() ?? "";
                        if (target.TryGetProperty("ConfirmationNo", out var cno)) resDto.ConfirmationNo = cno.GetString() ?? "";
                        if (target.TryGetProperty("BookingRefNo", out var brn)) resDto.BookingRefNo = brn.GetString() ?? "";
                        if (target.TryGetProperty("BookingId", out var bid) && bid.ValueKind == JsonValueKind.Number) resDto.BookingId = bid.GetInt32();
                        if (target.TryGetProperty("IsPriceChanged", out var ipc)) resDto.IsPriceChanged = ipc.ValueKind == JsonValueKind.True;
                        if (target.TryGetProperty("IsCancellationPolicyChanged", out var icpc)) resDto.IsCancellationPolicyChanged = icpc.ValueKind == JsonValueKind.True;
                        bookRes = responseDto;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception calling SRDV /Book.");
                throw;
            }

            var res = bookRes.BookResult;
            var confirmationNo = !string.IsNullOrEmpty(res.ConfirmationNo) ? res.ConfirmationNo
                               : !string.IsNullOrEmpty(res.BookingRefNo) ? res.BookingRefNo
                               : "";

            var finalStatus = "Confirmed";
            if (res.IsPriceChanged || res.IsCancellationPolicyChanged)
            {
                finalStatus = "VerifyPrice";
            }
            else if (!string.IsNullOrEmpty(res.Status))
            {
                finalStatus = res.Status;
            }
            else if (!string.IsNullOrEmpty(res.HotelBookingStatus))
            {
                finalStatus = res.HotelBookingStatus;
            }

            return new HotelBookingResponseDto
            {
                BookingId = res.BookingId > 0 ? res.BookingId.ToString() : "0",
                BookingReference = confirmationNo,
                ProviderBookingId = res.BookingId.ToString(),
                HotelId = offer.HotelCode!, HotelName = offer.HotelName,
                OfferId = offerId, UserId = userId,
                GuestName = guestName, GuestEmail = guestEmail, GuestPhone = guestPhone,
                CheckInDate = offer.CheckInDate, CheckOutDate = offer.CheckOutDate,
                Adults = 1, Rooms = offer.RoomQuantity,
                Price = offer.Price, NetPrice = offer.Price, Amount = offer.Price, TotalPrice = offer.Price,
                Currency = "INR",
                Status = finalStatus,
                Error = !string.IsNullOrEmpty(res.Error?.ErrorMessage) ? res.Error.ErrorMessage : (res.Error?.ErrorCode != 0 ? $"Error Code: {res.Error?.ErrorCode}" : null),
                CreatedAt = DateTime.UtcNow
            };
        }

        public async Task<bool> CancelBookingAsync(HotelReservation reservation, string endUserIp)
        {
            int bookingIdVal = 0;
            int.TryParse(reservation.ProviderBookingId, out bookingIdVal);
            var req = new HotelCancelRequestDto
            {
                BookingId = bookingIdVal,
                TraceId = reservation.TraceId,
                Remarks = "Cancellation requested by client",
                RequestType = 4, BookingMode = 5, 
                SrdvType = reservation.SrdvType ?? string.Empty, 
                SrdvIndex = reservation.SrdvIndex ?? string.Empty,
                EndUserIp = endUserIp,
            };
            var resDto = await CancelRoomAsync(req);
            return resDto.ResponseStatus == 1;
        }

        public async Task<PickNBookBalanceResponseDto> GetApiBalanceAsync(string endUserIp)
        {
            var requestBody = new
            {
                EndUserIp = "127.0.0.1",
                ClientId = _settings.ClientId,
                UserName = _settings.UserName,
                Password = _settings.Password
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.HotelBaseUrl}/Balance", requestBody, new JsonSerializerOptions { PropertyNamingPolicy = null });
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(contentStream);
            
            var root = json.RootElement;
            var balanceDto = new PickNBookBalanceResponseDto();
            
            if (root.TryGetProperty("Balance", out var balanceProp))
            {
                if (balanceProp.ValueKind == JsonValueKind.Number)
                    balanceDto.Balance = balanceProp.GetDecimal();
                else if (balanceProp.ValueKind == JsonValueKind.String && decimal.TryParse(balanceProp.GetString(), out var val))
                    balanceDto.Balance = val;
            }
            
            if (root.TryGetProperty("CreditLimit", out var creditProp))
            {
                if (creditProp.ValueKind == JsonValueKind.Number)
                    balanceDto.CreditLimit = creditProp.GetDecimal();
                else if (creditProp.ValueKind == JsonValueKind.String && decimal.TryParse(creditProp.GetString(), out var val))
                    balanceDto.CreditLimit = val;
            }

            if (root.TryGetProperty("Error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
            {
                if (errorProp.TryGetProperty("ErrorCode", out var codeProp))
                    balanceDto.ErrorCode = codeProp.GetString();
                if (errorProp.TryGetProperty("ErrorMessage", out var msgProp))
                    balanceDto.ErrorMessage = msgProp.GetString();
            }
            
            return balanceDto;
        }

        public async Task<PickNBookBalanceLogResponseDto> GetApiBalanceLogAsync(string endUserIp)
        {
            var requestBody = new
            {
                EndUserIp = "127.0.0.1",
                ClientId = _settings.ClientId,
                UserName = _settings.UserName,
                Password = _settings.Password
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.HotelBaseUrl}/BalanceLog", requestBody, new JsonSerializerOptions { PropertyNamingPolicy = null });
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(contentStream);
            var root = json.RootElement;

            var balanceLogDto = new PickNBookBalanceLogResponseDto { Success = true };

            if (root.TryGetProperty("Logs", out var logsProp) && logsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var log in logsProp.EnumerateArray())
                {
                    var entry = new BalanceLogEntryDto();
                    if (log.TryGetProperty("TransactionId", out var txProp)) entry.TransactionId = txProp.GetString() ?? string.Empty;
                    if (log.TryGetProperty("Date", out var dtProp)) entry.Date = dtProp.GetString() ?? string.Empty;
                    if (log.TryGetProperty("Description", out var descProp)) entry.Description = descProp.GetString() ?? string.Empty;
                    if (log.TryGetProperty("Amount", out var amtProp))
                    {
                        entry.Amount = amtProp.ValueKind == JsonValueKind.Number ? amtProp.GetDecimal() : (decimal.TryParse(amtProp.GetString(), out var val) ? val : 0m);
                    }
                    if (log.TryGetProperty("Type", out var typeProp)) entry.Type = typeProp.GetString() ?? string.Empty;
                    if (log.TryGetProperty("RunningBalance", out var rbProp))
                    {
                        entry.RunningBalance = rbProp.ValueKind == JsonValueKind.Number ? rbProp.GetDecimal() : (decimal.TryParse(rbProp.GetString(), out var val) ? val : 0m);
                    }
                    balanceLogDto.Logs.Add(entry);
                }
            }

            if (root.TryGetProperty("Error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
            {
                if (errorProp.TryGetProperty("ErrorCode", out var codeProp)) balanceLogDto.ErrorCode = codeProp.GetString();
                if (errorProp.TryGetProperty("ErrorMessage", out var msgProp)) balanceLogDto.ErrorMessage = msgProp.GetString();
                balanceLogDto.Success = false;
            }

            return balanceLogDto;
        }

        public async Task<PickNBookHotelInfoResponseDto> GetHotelInfoAsync(string traceId, string resultIndex, string hotelCode)
        {
            return await GetHotelInfoAsync(new HotelInfoRequestDto
            {
                TraceId = traceId ?? "",
                ResultIndex = resultIndex ?? "",
                HotelCode = hotelCode ?? ""
            });
        }

        public async Task<PickNBookHotelInfoResponseDto> GetHotelInfoAsync(HotelInfoRequestDto request)
        {
            request.ClientId = _settings.ClientId;
            request.UserName = _settings.UserName;
            request.Password = _settings.Password;


            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 1. Check cache first
            var cachedInfo = await dbContext.HotelInfoCaches.FirstOrDefaultAsync(h => h.HotelCode == request.HotelCode);
            if (cachedInfo != null && cachedInfo.LastUpdated > DateTime.UtcNow.AddDays(-30))
            {
                var cachedDto = new PickNBookHotelInfoResponseDto();
                var cRes = cachedDto.HotelInfoResult;
                cRes.SrdvType = request.SrdvType;
                cRes.ResultIndex = request.ResultIndex;
                cRes.SrdvIndex = request.SrdvIndex;
                cRes.TraceId = request.TraceId;
                
                var cDetails = cRes.HotelDetails;
                cDetails.HotelCode = cachedInfo.HotelCode;
                cDetails.HotelName = cachedInfo.HotelName;
                cDetails.StarRating = cachedInfo.StarRating;
                cDetails.HotelURL = cachedInfo.HotelURL;
                cDetails.HotelPicture = cachedInfo.HotelPicture;
                cDetails.Address = cachedInfo.Address;
                cDetails.City = cachedInfo.City;
                cDetails.State = cachedInfo.State;
                cDetails.PinCode = cachedInfo.PinCode;
                cDetails.CountryName = cachedInfo.CountryName;
                cDetails.HotelContactNo = cachedInfo.HotelContactNo;
                cDetails.FaxNumber = cachedInfo.FaxNumber;
                cDetails.Email = cachedInfo.Email;
                cDetails.Latitude = cachedInfo.Latitude;
                cDetails.Longitude = cachedInfo.Longitude;
                cDetails.OtherDetails = cachedInfo.OtherDetails;
                cDetails.HotelPolicy = cachedInfo.HotelPolicy;
                cDetails.SpecialInstructions = cachedInfo.SpecialInstructions;
                cDetails.RoomData = cachedInfo.RoomData;
                cDetails.RoomFacilities = cachedInfo.RoomFacilities;
                cDetails.Services = cachedInfo.Services;
                
                if (!string.IsNullOrWhiteSpace(cachedInfo.DescriptionJson))
                    cDetails.Description = JsonSerializer.Deserialize<List<HotelInfoDescriptionDto>>(cachedInfo.DescriptionJson) ?? new List<HotelInfoDescriptionDto>();
                if (!string.IsNullOrWhiteSpace(cachedInfo.PolicyAndInstructionJson))
                    cDetails.PolicyAndInstruction = JsonSerializer.Deserialize<List<HotelInfoPolicyAndInstructionDto>>(cachedInfo.PolicyAndInstructionJson) ?? new List<HotelInfoPolicyAndInstructionDto>();
                if (!string.IsNullOrWhiteSpace(cachedInfo.AttractionsJson))
                    cDetails.Attractions = JsonSerializer.Deserialize<List<string>>(cachedInfo.AttractionsJson) ?? new List<string>();
                if (!string.IsNullOrWhiteSpace(cachedInfo.HotelFacilitiesJson))
                {
                    cDetails.HotelFacilities = JsonSerializer.Deserialize<List<HotelInfoFacilityDto>>(cachedInfo.HotelFacilitiesJson) ?? new List<HotelInfoFacilityDto>();
                    foreach (var fac in cDetails.HotelFacilities) { cachedDto.Facilities.Add(fac.Name); }
                }
                if (!string.IsNullOrWhiteSpace(cachedInfo.ImagesJson))
                    cDetails.Images = JsonSerializer.Deserialize<List<string>>(cachedInfo.ImagesJson) ?? new List<string>();

                return cachedDto;
            }

            JsonElement root;
            JsonDocument? jsonDoc = null;
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_settings.HotelBaseUrl}/GetHotelInfo", request);
                response.EnsureSuccessStatusCode();

                using var contentStream = await response.Content.ReadAsStreamAsync();
            jsonDoc = await JsonDocument.ParseAsync(contentStream);
                root = jsonDoc.RootElement;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "SRDV API GetHotelInfo failed for HotelCode {HotelCode}.", request.HotelCode);
                // No mock logic anymore, return error DTO
                var errDto = new PickNBookHotelInfoResponseDto();
                errDto.HotelInfoResult.Error.ErrorCode = 1;
                errDto.HotelInfoResult.Error.ErrorMessage = $"Failed to fetch hotel info from SRDV. Exception: {ex.Message}";
                return errDto;
            }

            try
            {
                var infoDto = new PickNBookHotelInfoResponseDto();
                var resDto = infoDto.HotelInfoResult;
                var target = root.TryGetProperty("HotelInfoResult", out var hirProp) && hirProp.ValueKind == JsonValueKind.Object ? hirProp : root;

                if (target.TryGetProperty("Error", out var errProp) && errProp.ValueKind == JsonValueKind.Object)
                {
                    if (errProp.TryGetProperty("ErrorCode", out var ecProp) && ecProp.ValueKind == JsonValueKind.Number) resDto.Error.ErrorCode = ecProp.GetInt32();
                    else if (errProp.TryGetProperty("ErrorCode", out var ecStr) && ecStr.ValueKind == JsonValueKind.String && int.TryParse(ecStr.GetString(), out var ecInt)) resDto.Error.ErrorCode = ecInt;
                    if (errProp.TryGetProperty("ErrorMessage", out var emProp)) resDto.Error.ErrorMessage = emProp.GetString() ?? "";
                }
                
                if (resDto.Error.ErrorCode != 0)
                {
                    return infoDto;
                }

                if (target.TryGetProperty("SrdvType", out var stProp)) resDto.SrdvType = stProp.GetString() ?? request.SrdvType;
                if (target.TryGetProperty("ResultIndex", out var riProp)) resDto.ResultIndex = riProp.ValueKind == JsonValueKind.Number ? riProp.GetRawText() : (riProp.GetString() ?? request.ResultIndex);
                if (target.TryGetProperty("SrdvIndex", out var siProp)) resDto.SrdvIndex = siProp.ValueKind == JsonValueKind.Number ? siProp.GetRawText() : (siProp.GetString() ?? request.SrdvIndex);
                if (target.TryGetProperty("TraceId", out var tiProp)) resDto.TraceId = tiProp.ValueKind == JsonValueKind.Number ? tiProp.GetRawText() : (tiProp.GetString() ?? request.TraceId);

                if (target.TryGetProperty("HotelDetails", out var detailsProp) && detailsProp.ValueKind == JsonValueKind.Object)
                {
                    var hd = resDto.HotelDetails;
                    if (detailsProp.TryGetProperty("HotelCode", out var codeProp)) hd.HotelCode = codeProp.GetString() ?? request.HotelCode;
                    if (detailsProp.TryGetProperty("HotelName", out var nameProp)) hd.HotelName = nameProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("StarRating", out var srProp))
                    {
                        if (srProp.ValueKind == JsonValueKind.Number) hd.StarRating = srProp.GetDouble();
                        else if (srProp.ValueKind == JsonValueKind.String && double.TryParse(srProp.GetString(), out var d)) hd.StarRating = d;
                    }
                    if (detailsProp.TryGetProperty("HotelURL", out var urlProp)) hd.HotelURL = urlProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("OtherDetails", out var odProp)) hd.OtherDetails = odProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("HotelPolicy", out var hpProp)) hd.HotelPolicy = hpProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("SpecialInstructions", out var si2Prop)) hd.SpecialInstructions = si2Prop.GetString() ?? "";
                    if (detailsProp.TryGetProperty("HotelPicture", out var picProp)) hd.HotelPicture = picProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("Address", out var addrProp)) hd.Address = addrProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("City", out var cProp)) hd.City = cProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("State", out var st2Prop)) hd.State = st2Prop.GetString() ?? "";
                    if (detailsProp.TryGetProperty("PinCode", out var pinProp)) hd.PinCode = pinProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("CountryName", out var cntProp)) hd.CountryName = cntProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("HotelContactNo", out var hcProp)) hd.HotelContactNo = hcProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("FaxNumber", out var fxProp)) hd.FaxNumber = fxProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("Email", out var em2Prop)) hd.Email = em2Prop.GetString() ?? "";
                    if (detailsProp.TryGetProperty("Latitude", out var latProp)) hd.Latitude = latProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("Longitude", out var lngProp)) hd.Longitude = lngProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("RoomData", out var rdProp)) hd.RoomData = rdProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("RoomFacilities", out var rfProp)) hd.RoomFacilities = rfProp.GetString() ?? "";
                    if (detailsProp.TryGetProperty("Services", out var srvProp)) hd.Services = srvProp.GetString() ?? "";

                    if (detailsProp.TryGetProperty("Images", out var imgsProp) && imgsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var img in imgsProp.EnumerateArray()) { var s = img.GetString(); if (!string.IsNullOrWhiteSpace(s)) hd.Images.Add(s); }
                    }

                    if (detailsProp.TryGetProperty("Description", out var descArrProp) && descArrProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var desc in descArrProp.EnumerateArray())
                        {
                            if (desc.ValueKind == JsonValueKind.Object)
                            {
                                var descDto = new HotelInfoDescriptionDto();
                                if (desc.TryGetProperty("Name", out var dnProp)) descDto.Name = dnProp.GetString() ?? "";
                                if (desc.TryGetProperty("Detail", out var detProp) && detProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var dItem in detProp.EnumerateArray()) { descDto.Detail.Add(dItem.GetString() ?? ""); }
                                }
                                else if (desc.TryGetProperty("Detail", out var detStr) && detStr.ValueKind == JsonValueKind.String)
                                {
                                    var strVal = detStr.GetString(); if (!string.IsNullOrWhiteSpace(strVal)) descDto.Detail.Add(strVal);
                                }
                                hd.Description.Add(descDto);
                            }
                        }
                    }

                    if (detailsProp.TryGetProperty("PolicyAndInstruction", out var piArrProp) && piArrProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pi in piArrProp.EnumerateArray())
                        {
                            if (pi.ValueKind == JsonValueKind.Object)
                            {
                                var piDto = new HotelInfoPolicyAndInstructionDto();
                                if (pi.TryGetProperty("Name", out var piNameProp)) piDto.Name = piNameProp.GetString() ?? "";
                                if (pi.TryGetProperty("Data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var dItem in dataProp.EnumerateArray())
                                    {
                                        if (dItem.ValueKind == JsonValueKind.Object)
                                        {
                                            var pDataDto = new HotelInfoPolicyDataDto();
                                            if (dItem.TryGetProperty("SubName", out var snProp)) pDataDto.SubName = snProp.GetString() ?? "";
                                            if (dItem.TryGetProperty("Detail", out var detProp) && detProp.ValueKind == JsonValueKind.Array)
                                            {
                                                foreach (var det in detProp.EnumerateArray()) { pDataDto.Detail.Add(det.GetString() ?? ""); }
                                            }
                                            piDto.Data.Add(pDataDto);
                                        }
                                    }
                                }
                                hd.PolicyAndInstruction.Add(piDto);
                            }
                        }
                    }

                    if (detailsProp.TryGetProperty("Attractions", out var attrProp) && attrProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var attr in attrProp.EnumerateArray())
                        {
                            if (attr.ValueKind == JsonValueKind.String) { var s = attr.GetString(); if (!string.IsNullOrWhiteSpace(s)) hd.Attractions.Add(s); }
                            else if (attr.ValueKind == JsonValueKind.Object)
                            {
                                if (attr.TryGetProperty("Name", out var anProp)) hd.Attractions.Add(anProp.GetString() ?? "");
                                else if (attr.TryGetProperty("Attraction", out var aProp)) hd.Attractions.Add(aProp.GetString() ?? "");
                                else hd.Attractions.Add(attr.GetRawText());
                            }
                        }
                    }

                    if (detailsProp.TryGetProperty("HotelFacilities", out var facProp) && facProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var fac in facProp.EnumerateArray())
                        {
                            if (fac.ValueKind == JsonValueKind.Object)
                            {
                                var facDto = new HotelInfoFacilityDto();
                                if (fac.TryGetProperty("Name", out var fnProp)) facDto.Name = fnProp.GetString() ?? "";
                                if (fac.TryGetProperty("FontAwesome", out var faProp)) facDto.FontAwesome = faProp.GetString() ?? "";
                                if (fac.TryGetProperty("IcoFont", out var icProp)) facDto.IcoFont = icProp.GetString() ?? "";
                                hd.HotelFacilities.Add(facDto);
                                if (!string.IsNullOrWhiteSpace(facDto.Name)) infoDto.Facilities.Add(facDto.Name);
                            }
                            else if (fac.ValueKind == JsonValueKind.String)
                            {
                                var s = fac.GetString() ?? "";
                                hd.HotelFacilities.Add(new HotelInfoFacilityDto { Name = s });
                                infoDto.Facilities.Add(s);
                            }
                        }
                    }
                    
                    // Upsert to DB
                    if (!string.IsNullOrWhiteSpace(hd.HotelCode) && hd.HotelCode != "None")
                    {
                        try
                        {
                            var entity = cachedInfo ?? new HotelInfoCache { HotelCode = hd.HotelCode };
                            entity.HotelName = hd.HotelName;
                            entity.StarRating = hd.StarRating;
                            entity.HotelURL = hd.HotelURL;
                            entity.HotelPicture = hd.HotelPicture;
                            entity.Address = hd.Address;
                            entity.City = hd.City;
                            entity.State = hd.State;
                            entity.PinCode = hd.PinCode;
                            entity.CountryName = hd.CountryName;
                            entity.HotelContactNo = hd.HotelContactNo;
                            entity.FaxNumber = hd.FaxNumber;
                            entity.Email = hd.Email;
                            entity.Latitude = hd.Latitude;
                            entity.Longitude = hd.Longitude;
                            entity.OtherDetails = hd.OtherDetails;
                            entity.HotelPolicy = hd.HotelPolicy;
                            entity.SpecialInstructions = hd.SpecialInstructions;
                            entity.RoomData = hd.RoomData;
                            entity.RoomFacilities = hd.RoomFacilities;
                            entity.Services = hd.Services;

                            entity.DescriptionJson = JsonSerializer.Serialize(hd.Description);
                            entity.PolicyAndInstructionJson = JsonSerializer.Serialize(hd.PolicyAndInstruction);
                            entity.AttractionsJson = JsonSerializer.Serialize(hd.Attractions);
                            entity.HotelFacilitiesJson = JsonSerializer.Serialize(hd.HotelFacilities);
                            entity.ImagesJson = JsonSerializer.Serialize(hd.Images);
                            
                            entity.LastUpdated = DateTime.UtcNow;

                            if (cachedInfo == null)
                            {
                                dbContext.HotelInfoCaches.Add(entity);
                            }
                            await dbContext.SaveChangesAsync();
                        }
                        catch (Exception dbEx)
                        {
                            _logger?.LogWarning(dbEx, "Failed to cache HotelInfo for HotelCode {HotelCode}", hd.HotelCode);
                        }
                    }
                }

                return infoDto;
            }
            finally
            {
                jsonDoc?.Dispose();
            }
        }


        public async Task<PickNBookHotelRoomResponseDto> GetHotelRoomAsync(string traceId, string resultIndex, string hotelCode, string srdvIndex, string endUserIp)
        {
            return await GetHotelRoomAsync(new HotelRoomRequestDto
            {
                TraceId = traceId,
                ResultIndex = resultIndex,
                HotelCode = hotelCode,
                SrdvIndex = srdvIndex,
                EndUserIp = endUserIp
            });
        }

        public async Task<PickNBookHotelRoomResponseDto> GetHotelRoomAsync(HotelRoomRequestDto request)
        {
            JsonElement root;
            JsonDocument? jsonDoc = null;
            try
            {
                request.ClientId = _settings.ClientId;
                request.UserName = _settings.UserName;
                request.Password = _settings.Password;


                var response = await _httpClient.PostAsJsonAsync($"{_settings.HotelBaseUrl}/GetHotelRoom", request);
                response.EnsureSuccessStatusCode();

                using var contentStream = await response.Content.ReadAsStreamAsync();
            jsonDoc = await JsonDocument.ParseAsync(contentStream);
                root = jsonDoc.RootElement;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SRDV API GetHotelRoom failed for HotelCode {HotelCode}.", request.HotelCode);
                var errDto = new PickNBookHotelRoomResponseDto();
                errDto.GetHotelRoomResult.Error.ErrorCode = 1;
                errDto.GetHotelRoomResult.Error.ErrorMessage = $"Failed to fetch hotel rooms from SRDV. Exception: {ex.Message}";
                return errDto;
            }

            try
            {
                var responseDto = new PickNBookHotelRoomResponseDto();
                var resDto = responseDto.GetHotelRoomResult;

                JsonElement target = root;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("GetHotelRoomResult", out var ghrProp))
                    {
                        target = ghrProp;
                    }
                }

                if (target.ValueKind == JsonValueKind.Object)
                {
                    if (target.TryGetProperty("Error", out var errProp) && errProp.ValueKind == JsonValueKind.Object)
                    {
                        if (errProp.TryGetProperty("ErrorCode", out var ecProp) && ecProp.ValueKind == JsonValueKind.Number) resDto.Error.ErrorCode = ecProp.GetInt32();
                        if (errProp.TryGetProperty("ErrorMessage", out var emProp)) resDto.Error.ErrorMessage = emProp.GetString() ?? "";
                    }

                    if (resDto.Error.ErrorCode != 0)
                    {
                        return responseDto;
                    }

                    if (target.TryGetProperty("SrdvType", out var stProp)) resDto.SrdvType = stProp.GetString() ?? "MixAPI";
                    if (target.TryGetProperty("ResultIndex", out var riProp)) resDto.ResultIndex = riProp.GetString() ?? request.ResultIndex;
                    if (target.TryGetProperty("SrdvIndex", out var siProp)) resDto.SrdvIndex = siProp.GetString() ?? "15";
                    if (target.TryGetProperty("TraceId", out var tiProp))
                    {
                        resDto.TraceId = tiProp.ValueKind == JsonValueKind.Number ? tiProp.GetRawText() : (tiProp.GetString() ?? request.TraceId);
                    }
                    if (target.TryGetProperty("IsPolicyPerStay", out var ipProp) && (ipProp.ValueKind == JsonValueKind.True || ipProp.ValueKind == JsonValueKind.False)) resDto.IsPolicyPerStay = ipProp.GetBoolean();
                    if (target.TryGetProperty("IsUnderCancellationAllowed", out var icProp) && (icProp.ValueKind == JsonValueKind.True || icProp.ValueKind == JsonValueKind.False)) resDto.IsUnderCancellationAllowed = icProp.GetBoolean();
                }

                JsonElement roomsDetailsSource = target;
                if (target.ValueKind == JsonValueKind.Object)
                {
                    if (target.TryGetProperty("HotelRoomsDetails", out var roomsDetailsProp))
                    {
                        roomsDetailsSource = roomsDetailsProp;
                    }
                    else if (target.TryGetProperty("HotelRoomDetails", out var roomDetailsProp))
                    {
                        roomsDetailsSource = roomDetailsProp;
                    }
                }

                HotelRoomDetailItemDto ParseRoomItem(JsonElement rmElem)
                {
                    var rmDto = new HotelRoomDetailItemDto();
                    if (rmElem.ValueKind != JsonValueKind.Object) return rmDto;

                    if (rmElem.TryGetProperty("ChildCount", out var ccProp) && ccProp.ValueKind == JsonValueKind.Number) rmDto.ChildCount = ccProp.GetInt32();
                    if (rmElem.TryGetProperty("RequireAllPaxDetails", out var rapProp) && (rapProp.ValueKind == JsonValueKind.True || rapProp.ValueKind == JsonValueKind.False)) rmDto.RequireAllPaxDetails = rapProp.GetBoolean();
                    if (rmElem.TryGetProperty("RoomId", out var ridProp)) rmDto.RoomId = ridProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("RoomStatus", out var rsProp)) rmDto.RoomStatus = rsProp.GetString() ?? "Active";
                    if (rmElem.TryGetProperty("RoomIndex", out var ridxProp)) rmDto.RoomIndex = ridxProp.ValueKind == JsonValueKind.Number ? ridxProp.GetRawText() : (ridxProp.GetString() ?? "");
                    if (rmElem.TryGetProperty("RoomTypeCode", out var rtcProp)) rmDto.RoomTypeCode = rtcProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("RoomTypeName", out var rtnProp)) rmDto.RoomTypeName = rtnProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("RoomTypeCategory", out var rtcatProp)) rmDto.RoomTypeCategory = rtcatProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("RatePlanCode", out var rpcProp)) rmDto.RatePlanCode = rpcProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("RatePlan", out var rpProp)) rmDto.RatePlan = rpProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("InfoSource", out var isProp)) rmDto.InfoSource = isProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("SequenceNo", out var snProp)) rmDto.SequenceNo = snProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("SupplierPrice", out var spProp)) rmDto.SupplierPrice = spProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("RoomPromotion", out var rp2Prop)) rmDto.RoomPromotion = rp2Prop.GetString() ?? "";
                    if (rmElem.TryGetProperty("SmokingPreference", out var smkProp)) rmDto.SmokingPreference = smkProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("BedTypes", out var btProp)) rmDto.BedTypes = btProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("HotelSupplements", out var hsProp)) rmDto.HotelSupplements = hsProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("LastCancellationDate", out var lcdProp)) rmDto.LastCancellationDate = lcdProp.GetString() ?? "";
                    if (rmElem.TryGetProperty("IsPassportMandatory", out var ipmProp) && (ipmProp.ValueKind == JsonValueKind.True || ipmProp.ValueKind == JsonValueKind.False)) rmDto.IsPassportMandatory = ipmProp.GetBoolean();
                    if (rmElem.TryGetProperty("IsPANMandatory", out var ipanProp) && (ipanProp.ValueKind == JsonValueKind.True || ipanProp.ValueKind == JsonValueKind.False)) rmDto.IsPANMandatory = ipanProp.GetBoolean();
                    if (rmElem.TryGetProperty("FullRefundAllowed", out var fraProp) && (fraProp.ValueKind == JsonValueKind.True || fraProp.ValueKind == JsonValueKind.False)) rmDto.FullRefundAllowed = fraProp.GetBoolean();
                    if (rmElem.TryGetProperty("OfferedPrice", out var rmOpProp))
                    {
                        rmDto.OfferedPrice = rmOpProp.ValueKind == JsonValueKind.Number ? rmOpProp.GetDecimal() : (decimal.TryParse(rmOpProp.GetString(), out var v2) ? v2 : 0m);
                    }

                    if (rmElem.TryGetProperty("Description", out var descProp))
                    {
                        if (descProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var d in descProp.EnumerateArray()) if (d.GetString() != null) rmDto.Description.Add(d.GetString()!);
                        }
                        else if (descProp.ValueKind == JsonValueKind.String && descProp.GetString() != null)
                        {
                            rmDto.Description.Add(descProp.GetString()!);
                        }
                    }

                    if (rmElem.TryGetProperty("RoomImages", out var imgsProp) && imgsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var img in imgsProp.EnumerateArray())
                        {
                            if (img.ValueKind == JsonValueKind.Object)
                            {
                                rmDto.RoomImages.Add(new HotelRoomImageDto
                                {
                                    Name = img.TryGetProperty("Name", out var n) ? (n.GetString() ?? "Main") : "Main",
                                    Image = img.TryGetProperty("Image", out var i) ? (i.GetString() ?? "") : ""
                                });
                            }
                            else if (img.ValueKind == JsonValueKind.String && img.GetString() != null)
                            {
                                rmDto.RoomImages.Add(new HotelRoomImageDto { Name = "Main", Image = img.GetString()! });
                            }
                        }
                    }

                    if (rmElem.TryGetProperty("DayRates", out var drProp) && drProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dr in drProp.EnumerateArray())
                        {
                            if (dr.ValueKind == JsonValueKind.Object)
                            {
                                rmDto.DayRates.Add(new HotelRoomDayRateDto
                                {
                                    Date = dr.TryGetProperty("Date", out var dt) ? (dt.GetString() ?? "") : "",
                                    Amount = dr.TryGetProperty("Amount", out var amt) ? (amt.ValueKind == JsonValueKind.Number ? amt.GetDecimal() : 0m) : 0m
                                });
                            }
                        }
                    }

                    if (rmElem.TryGetProperty("Amenities", out var amenProp) && amenProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var am in amenProp.EnumerateArray())
                        {
                            if (am.ValueKind == JsonValueKind.Object)
                            {
                                rmDto.Amenities.Add(new HotelRoomAmenityDto
                                {
                                    Name = am.TryGetProperty("Name", out var an) ? (an.GetString() ?? "") : "",
                                    FontAwesome = am.TryGetProperty("FontAwesome", out var fa) ? (fa.GetString() ?? "") : "",
                                    IcoFont = am.TryGetProperty("IcoFont", out var ifo) ? (ifo.GetString() ?? "") : ""
                                });
                            }
                            else if (am.ValueKind == JsonValueKind.String && am.GetString() != null)
                            {
                                rmDto.Amenities.Add(new HotelRoomAmenityDto { Name = am.GetString()! });
                            }
                        }
                    }

                    if (rmElem.TryGetProperty("ServicesStatus", out var ssProp) && ssProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ss in ssProp.EnumerateArray())
                        {
                            if (ss.ValueKind == JsonValueKind.Object)
                            {
                                rmDto.ServicesStatus.Add(new HotelRoomServiceStatusDto
                                {
                                    Name = ss.TryGetProperty("Name", out var sn) ? (sn.GetString() ?? "") : "",
                                    Value = ss.TryGetProperty("Value", out var sv) ? (sv.GetString() ?? "") : ""
                                });
                            }
                        }
                    }

                    if (rmElem.TryGetProperty("CancellationPolicies", out var cpProp) && cpProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cp in cpProp.EnumerateArray())
                        {
                            if (cp.ValueKind == JsonValueKind.Object)
                            {
                                rmDto.CancellationPolicies.Add(new HotelRoomCancellationPolicyDto
                                {
                                    Charge = cp.TryGetProperty("Charge", out var chg) ? (chg.ValueKind == JsonValueKind.Number ? chg.GetDecimal() : 0m) : 0m,
                                    ChargeType = cp.TryGetProperty("ChargeType", out var ct) && ct.ValueKind == JsonValueKind.Number ? ct.GetInt32() : 1,
                                    Currency = cp.TryGetProperty("Currency", out var cur) ? (cur.GetString() ?? "INR") : "INR",
                                    FromDate = cp.TryGetProperty("FromDate", out var fd) ? (fd.GetString() ?? "") : "",
                                    ToDate = cp.TryGetProperty("ToDate", out var td) ? (td.GetString() ?? "") : ""
                                });
                            }
                        }
                    }

                    if (rmElem.TryGetProperty("Price", out var prProp) && prProp.ValueKind == JsonValueKind.Object)
                    {
                        var pr = rmDto.Price;
                        if (prProp.TryGetProperty("CurrencyCode", out var cc) && cc.GetString() != null) pr.CurrencyCode = cc.GetString()!;
                        if (prProp.TryGetProperty("RoomPrice", out var rp) && rp.ValueKind == JsonValueKind.Number) pr.RoomPrice = rp.GetDecimal();
                        if (prProp.TryGetProperty("Tax", out var tax) && tax.ValueKind == JsonValueKind.Number) pr.Tax = tax.GetDecimal();
                        if (prProp.TryGetProperty("ExtraGuestCharge", out var egc) && egc.ValueKind == JsonValueKind.Number) pr.ExtraGuestCharge = egc.GetDecimal();
                        if (prProp.TryGetProperty("ChildCharge", out var chd) && chd.ValueKind == JsonValueKind.Number) pr.ChildCharge = chd.GetDecimal();
                        if (prProp.TryGetProperty("OtherCharges", out var oc) && oc.ValueKind == JsonValueKind.Number) pr.OtherCharges = oc.GetDecimal();
                        if (prProp.TryGetProperty("Discount", out var disc) && disc.ValueKind == JsonValueKind.Number) pr.Discount = disc.GetDecimal();
                        if (prProp.TryGetProperty("PublishedPrice", out var pp) && pp.ValueKind == JsonValueKind.Number) pr.PublishedPrice = pp.GetDecimal();
                        if (prProp.TryGetProperty("PublishedPriceRoundedOff", out var ppro) && ppro.ValueKind == JsonValueKind.Number) pr.PublishedPriceRoundedOff = ppro.GetDecimal();
                        if (prProp.TryGetProperty("OfferedPrice", out var op) && op.ValueKind == JsonValueKind.Number) pr.OfferedPrice = op.GetDecimal();
                        if (prProp.TryGetProperty("OfferedPriceRoundedOff", out var opro) && opro.ValueKind == JsonValueKind.Number) pr.OfferedPriceRoundedOff = opro.GetDecimal();
                        if (prProp.TryGetProperty("AgentCommission", out var ac) && ac.ValueKind == JsonValueKind.Number) pr.AgentCommission = ac.GetDecimal();
                        if (prProp.TryGetProperty("AgentMarkUp", out var amk) && amk.ValueKind == JsonValueKind.Number) pr.AgentMarkUp = amk.GetDecimal();
                        if (prProp.TryGetProperty("ServiceTax", out var stx) && stx.ValueKind == JsonValueKind.Number) pr.ServiceTax = stx.GetDecimal();
                        if (prProp.TryGetProperty("TDS", out var tds) && tds.ValueKind == JsonValueKind.Number) pr.TDS = tds.GetDecimal();
                        if (prProp.TryGetProperty("ServiceCharge", out var sc) && sc.ValueKind == JsonValueKind.Number) pr.ServiceCharge = sc.GetDecimal();
                        if (prProp.TryGetProperty("TotalGSTAmount", out var tga) && tga.ValueKind == JsonValueKind.Number) pr.TotalGSTAmount = tga.GetDecimal();

                        if (prProp.TryGetProperty("GST", out var gstProp) && gstProp.ValueKind == JsonValueKind.Object)
                        {
                            if (pr.GST == null) pr.GST = new HotelSearchGstDto();
                            if (gstProp.TryGetProperty("CGSTAmount", out var cgsta) && cgsta.ValueKind == JsonValueKind.Number) pr.GST.CGSTAmount = cgsta.GetDecimal();
                            if (gstProp.TryGetProperty("CGSTRate", out var cgstr) && cgstr.ValueKind == JsonValueKind.Number) pr.GST.CGSTRate = cgstr.GetDecimal();
                            if (gstProp.TryGetProperty("CessAmount", out var cessa) && cessa.ValueKind == JsonValueKind.Number) pr.GST.CessAmount = cessa.GetDecimal();
                            if (gstProp.TryGetProperty("CessRate", out var cessr) && cessr.ValueKind == JsonValueKind.Number) pr.GST.CessRate = cessr.GetDecimal();
                            if (gstProp.TryGetProperty("IGSTAmount", out var igsta) && igsta.ValueKind == JsonValueKind.Number) pr.GST.IGSTAmount = igsta.GetDecimal();
                            if (gstProp.TryGetProperty("IGSTRate", out var igstr) && igstr.ValueKind == JsonValueKind.Number) pr.GST.IGSTRate = igstr.GetDecimal();
                            if (gstProp.TryGetProperty("SGSTAmount", out var sgsta) && sgsta.ValueKind == JsonValueKind.Number) pr.GST.SGSTAmount = sgsta.GetDecimal();
                            if (gstProp.TryGetProperty("SGSTRate", out var sgstr) && sgstr.ValueKind == JsonValueKind.Number) pr.GST.SGSTRate = sgstr.GetDecimal();
                            if (gstProp.TryGetProperty("TaxableAmount", out var taxa) && taxa.ValueKind == JsonValueKind.Number) pr.GST.TaxableAmount = taxa.GetDecimal();
                        }
                    }

                    return rmDto;
                }

                if (roomsDetailsSource.ValueKind == JsonValueKind.Array)
                {
                    var defaultCategory = new HotelRoomCategoryDetailsDto { CategoryName = "Standard" };

                    foreach (var elem in roomsDetailsSource.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var rmElem in elem.EnumerateArray())
                            {
                                var rmDto = ParseRoomItem(rmElem);
                                defaultCategory.Rooms.Add(rmDto);
                            }
                        }
                        else if (elem.ValueKind == JsonValueKind.Object)
                        {
                            if (elem.TryGetProperty("Rooms", out var roomsProp) && roomsProp.ValueKind == JsonValueKind.Array)
                            {
                                var catDto = new HotelRoomCategoryDetailsDto();
                                if (elem.TryGetProperty("CategoryName", out var cnProp)) catDto.CategoryName = cnProp.GetString() ?? "";
                                if (elem.TryGetProperty("OfferedPrice", out var opProp))
                                {
                                    catDto.OfferedPrice = opProp.ValueKind == JsonValueKind.Number ? opProp.GetDecimal() : (decimal.TryParse(opProp.GetString(), out var v) ? v : 0m);
                                }

                                foreach (var rmElem in roomsProp.EnumerateArray())
                                {
                                    var rmDto = ParseRoomItem(rmElem);
                                    catDto.Rooms.Add(rmDto);
                                }
                                resDto.HotelRoomsDetails.Add(catDto);
                            }
                            else
                            {
                                var rmDto = ParseRoomItem(elem);
                                defaultCategory.Rooms.Add(rmDto);
                            }
                        }
                    }

                    if (defaultCategory.Rooms.Count > 0)
                    {
                        resDto.HotelRoomsDetails.Add(defaultCategory);
                    }
                }

                void ParseRoomCombinationsList(JsonElement rccList, List<HotelRoomCombinationItemDto> targetList)
                {
                    if (rccList.ValueKind != JsonValueKind.Array) return;

                    foreach (var item in rccList.EnumerateArray())
                    {
                        var comboItem = new HotelRoomCombinationItemDto();
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            if (item.TryGetProperty("RoomIndex", out var rIdxList))
                            {
                                if (rIdxList.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var idxVal in rIdxList.EnumerateArray())
                                    {
                                        if (idxVal.ValueKind == JsonValueKind.Number) comboItem.RoomIndex.Add(idxVal.GetInt32());
                                        else if (int.TryParse(idxVal.GetString(), out var iv)) comboItem.RoomIndex.Add(iv);
                                    }
                                }
                                else if (rIdxList.ValueKind == JsonValueKind.Number) comboItem.RoomIndex.Add(rIdxList.GetInt32());
                                else if (int.TryParse(rIdxList.GetString(), out var iv)) comboItem.RoomIndex.Add(iv);
                            }
                        }
                        else if (item.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var idxVal in item.EnumerateArray())
                            {
                                if (idxVal.ValueKind == JsonValueKind.Number) comboItem.RoomIndex.Add(idxVal.GetInt32());
                                else if (int.TryParse(idxVal.GetString(), out var iv)) comboItem.RoomIndex.Add(iv);
                            }
                        }
                        else if (item.ValueKind == JsonValueKind.Number)
                        {
                            comboItem.RoomIndex.Add(item.GetInt32());
                        }
                        else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var iv))
                        {
                            comboItem.RoomIndex.Add(iv);
                        }

                        if (comboItem.RoomIndex.Count > 0)
                        {
                            targetList.Add(comboItem);
                        }
                    }
                }

                if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty("RoomCombinations", out var rcProp))
                {
                    if (rcProp.ValueKind == JsonValueKind.Object)
                    {
                        if (rcProp.TryGetProperty("InfoSource", out var rciProp)) resDto.RoomCombinations.InfoSource = rciProp.GetString() ?? "";
                        if (rcProp.TryGetProperty("RoomCombination", out var rccList))
                        {
                            ParseRoomCombinationsList(rccList, resDto.RoomCombinations.RoomCombination);
                        }
                    }
                    else if (rcProp.ValueKind == JsonValueKind.Array)
                    {
                        ParseRoomCombinationsList(rcProp, resDto.RoomCombinations.RoomCombination);
                    }
                }

                using var scope = _serviceProvider.CreateScope();
                var markupService = scope.ServiceProvider.GetRequiredService<IHotelMarkupService>();
                if (markupService != null)
                {
                    foreach (var cat in resDto.HotelRoomsDetails)
                    {
                        foreach (var rm in cat.Rooms)
                        {
                            if (rm.Price != null)
                            {
                                await ApplyMarkupAndGstAsync(markupService, rm.Price, null, request.HotelCode, "B2C");
                                rm.OfferedPrice = rm.Price.OfferedPrice;
                                rm.B2CBasePrice = rm.Price.B2CBasePrice;
                                rm.B2CTotalPrice = rm.Price.B2CTotalPrice;
                            }
                        }
                    }
                }

                return responseDto;
            }
            finally
            {
                jsonDoc?.Dispose();
            }
        }


        public async Task<PickNBookBlockRoomResponseDto> BlockRoomAsync(BlockRoomRequestDto request)
        {
            JsonElement root;
            JsonDocument? jsonDoc = null;
            try
            {
                var cleanTraceId = request.TraceId?.TrimEnd('.') ?? "";
                var blockReq = new
                {
                    EndUserIp = request.EndUserIp,
                    ClientId = _settings.ClientId,
                    UserName = _settings.UserName,
                    Password = _settings.Password,
                    TokenId = "",
                    TraceId = int.TryParse(cleanTraceId, out var tid) ? (object)tid : cleanTraceId,
                    SrdvType = request.SrdvType,
                    SrdvIndex = request.SrdvIndex,
                    ResultIndex = int.TryParse(request.ResultIndex, out var ri) ? (object)ri : request.ResultIndex,
                    HotelCode = request.HotelCode,
                    HotelName = request.HotelName,
                    GuestNationality = request.GuestNationality,
                    NoOfRooms = request.NoOfRooms > 0 ? request.NoOfRooms : 1,
                    ClientReferenceNo = request.ClientReferenceNo,
                    IsVoucherBooking = request.IsVoucherBooking,
                    HotelRoomsDetails = request.HotelRoomsDetails
                };

                var jsonStr = JsonSerializer.Serialize(blockReq, new JsonSerializerOptions { PropertyNamingPolicy = null });
                _logger?.LogWarning("BLOCK REQUEST PAYLOAD SENT TO SRDV: {JsonStr}", jsonStr);

                var response = await _httpClient.PostAsJsonAsync($"{_settings.HotelBaseUrl}/BlockRoom", blockReq, new JsonSerializerOptions { PropertyNamingPolicy = null });
                response.EnsureSuccessStatusCode();

                using var contentStream = await response.Content.ReadAsStreamAsync();
            jsonDoc = await JsonDocument.ParseAsync(contentStream);
                root = jsonDoc.RootElement;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SRDV API BlockRoom failed for HotelCode {HotelCode}.", request.HotelCode);
                var errDto = new PickNBookBlockRoomResponseDto();
                errDto.BlockRoomResult.Error.ErrorCode = 1;
                errDto.BlockRoomResult.Error.ErrorMessage = $"Failed to block room on SRDV. Exception: {ex.Message}";
                return errDto;
            }

            try
            {
                var responseDto = new PickNBookBlockRoomResponseDto();
                var resDto = responseDto.BlockRoomResult;

                JsonElement target = root.TryGetProperty("BlockRoomResult", out var brProp) && brProp.ValueKind == JsonValueKind.Object ? brProp : root;

                if (target.TryGetProperty("Error", out var errProp) && errProp.ValueKind == JsonValueKind.Object)
                {
                    if (errProp.TryGetProperty("ErrorCode", out var ecProp) && ecProp.ValueKind == JsonValueKind.Number) resDto.Error.ErrorCode = ecProp.GetInt32();
                    if (errProp.TryGetProperty("ErrorMessage", out var emProp)) resDto.Error.ErrorMessage = emProp.GetString() ?? "";
                }

                if (resDto.Error.ErrorCode != 0)
                {
                    return responseDto;
                }
                if (target.TryGetProperty("AvailabilityType", out var atProp)) resDto.AvailabilityType = atProp.GetString() ?? "Confirm";
                if (target.TryGetProperty("TraceId", out var tiProp)) resDto.TraceId = tiProp.ValueKind == JsonValueKind.Number ? tiProp.GetRawText() : (tiProp.GetString() ?? request.TraceId);
                if (target.TryGetProperty("ResponseStatus", out var rsProp) && rsProp.ValueKind == JsonValueKind.Number) resDto.ResponseStatus = rsProp.GetInt32();
                if (target.TryGetProperty("GSTAllowed", out var gaProp) && (gaProp.ValueKind == JsonValueKind.True || gaProp.ValueKind == JsonValueKind.False)) resDto.GSTAllowed = gaProp.GetBoolean();
                if (target.TryGetProperty("IsPackageDetailsMandatory", out var pdmProp) && (pdmProp.ValueKind == JsonValueKind.True || pdmProp.ValueKind == JsonValueKind.False)) resDto.IsPackageDetailsMandatory = pdmProp.GetBoolean();
                if (target.TryGetProperty("IsPackageFare", out var pfProp) && (pfProp.ValueKind == JsonValueKind.True || pfProp.ValueKind == JsonValueKind.False)) resDto.IsPackageFare = pfProp.GetBoolean();
                if (target.TryGetProperty("IsPriceChanged", out var pcProp) && (pcProp.ValueKind == JsonValueKind.True || pcProp.ValueKind == JsonValueKind.False)) resDto.IsPriceChanged = pcProp.GetBoolean();
                if (target.TryGetProperty("IsCancellationPolicyChanged", out var cpcProp) && (cpcProp.ValueKind == JsonValueKind.True || cpcProp.ValueKind == JsonValueKind.False)) resDto.IsCancellationPolicyChanged = cpcProp.GetBoolean();
                if (target.TryGetProperty("IsHotelPolicyChanged", out var hpcProp) && (hpcProp.ValueKind == JsonValueKind.True || hpcProp.ValueKind == JsonValueKind.False)) resDto.IsHotelPolicyChanged = hpcProp.GetBoolean();

                if (target.TryGetProperty("HotelNorms", out var hnProp)) resDto.HotelNorms = hnProp.GetString() ?? "";
                if (target.TryGetProperty("HotelName", out var hnameProp)) resDto.HotelName = hnameProp.GetString() ?? request.HotelName;
                if (target.TryGetProperty("AddressLine1", out var ad1Prop)) resDto.AddressLine1 = ad1Prop.GetString() ?? "";
                if (target.TryGetProperty("AddressLine2", out var ad2Prop)) resDto.AddressLine2 = ad2Prop.GetString() ?? "";
                if (target.TryGetProperty("City", out var cityProp)) resDto.City = cityProp.GetString() ?? "";
                if (target.TryGetProperty("State", out var stProp)) resDto.State = stProp.GetString() ?? "";
                if (target.TryGetProperty("PinCode", out var pinProp)) resDto.PinCode = pinProp.GetString() ?? "";
                if (target.TryGetProperty("CountryName", out var cntProp)) resDto.CountryName = cntProp.GetString() ?? "";
                if (target.TryGetProperty("HotelContactNo", out var hcProp)) resDto.HotelContactNo = hcProp.GetString() ?? "";
                if (target.TryGetProperty("StarRating", out var srProp) && srProp.ValueKind == JsonValueKind.Number) resDto.StarRating = srProp.GetInt32();
                if (target.TryGetProperty("HotelPolicyDetail", out var hpdProp)) resDto.HotelPolicyDetail = hpdProp.GetString() ?? "";
                if (target.TryGetProperty("Latitude", out var latProp)) resDto.Latitude = latProp.GetString() ?? "";
                if (target.TryGetProperty("Longitude", out var lonProp)) resDto.Longitude = lonProp.GetString() ?? "";
                if (target.TryGetProperty("BookingAllowedForRoamer", out var barProp) && (barProp.ValueKind == JsonValueKind.True || barProp.ValueKind == JsonValueKind.False)) resDto.BookingAllowedForRoamer = barProp.GetBoolean();

                if (target.TryGetProperty("HotelRoomsDetails", out var roomsDetailsProp) && roomsDetailsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rmElem in roomsDetailsProp.EnumerateArray())
                    {
                        var rmDto = new BlockRoomDetailItemDto();
                        if (rmElem.TryGetProperty("ChildCount", out var ccProp) && ccProp.ValueKind == JsonValueKind.Number) rmDto.ChildCount = ccProp.GetInt32();
                        if (rmElem.TryGetProperty("RequireAllPaxDetails", out var rapProp) && (rapProp.ValueKind == JsonValueKind.True || rapProp.ValueKind == JsonValueKind.False)) rmDto.RequireAllPaxDetails = rapProp.GetBoolean();
                        if (rmElem.TryGetProperty("RoomId", out var ridProp)) rmDto.RoomId = ridProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("RoomStatus", out var rStatProp)) rmDto.RoomStatus = rStatProp.GetString() ?? "Active";
                        if (rmElem.TryGetProperty("RoomIndex", out var ridxProp)) rmDto.RoomIndex = ridxProp.ValueKind == JsonValueKind.Number ? ridxProp.GetRawText() : (ridxProp.GetString() ?? "");
                        if (rmElem.TryGetProperty("RoomTypeCode", out var rtcProp)) rmDto.RoomTypeCode = rtcProp.ValueKind == JsonValueKind.Number ? rtcProp.GetRawText() : (rtcProp.GetString() ?? "1");
                        if (rmElem.TryGetProperty("RoomTypeName", out var rtnProp)) rmDto.RoomTypeName = rtnProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("RatePlanCode", out var rpcProp)) rmDto.RatePlanCode = rpcProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("RatePlan", out var rpProp)) rmDto.RatePlan = rpProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("InfoSource", out var isProp)) rmDto.InfoSource = isProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("SequenceNo", out var snProp)) rmDto.SequenceNo = snProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("SupplierPrice", out var spProp)) rmDto.SupplierPrice = spProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("RoomPromotion", out var rp2Prop)) rmDto.RoomPromotion = rp2Prop.GetString() ?? "";
                        if (rmElem.TryGetProperty("SmokingPreference", out var smkProp)) rmDto.SmokingPreference = smkProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("BedTypes", out var btProp)) rmDto.BedTypes = btProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("HotelSupplements", out var hsProp)) rmDto.HotelSupplements = hsProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("LastCancellationDate", out var lcdProp)) rmDto.LastCancellationDate = lcdProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("BedTypeCode", out var btcProp)) rmDto.BedTypeCode = btcProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("Supplements", out var supProp)) rmDto.Supplements = supProp.GetString() ?? "";
                        if (rmElem.TryGetProperty("IsPassportMandatory", out var ipmProp) && (ipmProp.ValueKind == JsonValueKind.True || ipmProp.ValueKind == JsonValueKind.False)) rmDto.IsPassportMandatory = ipmProp.GetBoolean();
                        if (rmElem.TryGetProperty("IsPANMandatory", out var ipanProp) && (ipanProp.ValueKind == JsonValueKind.True || ipanProp.ValueKind == JsonValueKind.False)) rmDto.IsPANMandatory = ipanProp.GetBoolean();
                        if (rmElem.TryGetProperty("FullRefundAllowed", out var fraProp) && (fraProp.ValueKind == JsonValueKind.True || fraProp.ValueKind == JsonValueKind.False)) rmDto.FullRefundAllowed = fraProp.GetBoolean();
                        if (rmElem.TryGetProperty("CancellationPolicy", out var cpStrProp)) rmDto.CancellationPolicy = cpStrProp.GetString() ?? "";

                        if (rmElem.TryGetProperty("DayRates", out var drProp) && drProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var dr in drProp.EnumerateArray())
                            {
                                rmDto.DayRates.Add(new HotelRoomDayRateDto
                                {
                                    Date = dr.TryGetProperty("Date", out var dt) ? (dt.GetString() ?? "") : "",
                                    Amount = dr.TryGetProperty("Amount", out var amt) ? (amt.ValueKind == JsonValueKind.Number ? amt.GetDecimal() : 0m) : 0m
                                });
                            }
                        }

                        if (rmElem.TryGetProperty("Amenities", out var amenProp) && amenProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var am in amenProp.EnumerateArray())
                            {
                                rmDto.Amenities.Add(new HotelRoomAmenityDto
                                {
                                    Name = am.TryGetProperty("Name", out var an) ? (an.GetString() ?? "") : "",
                                    FontAwesome = am.TryGetProperty("FontAwesome", out var fa) ? (fa.GetString() ?? "") : "",
                                    IcoFont = am.TryGetProperty("IcoFont", out var ifo) ? (ifo.GetString() ?? "") : ""
                                });
                            }
                        }

                        if (rmElem.TryGetProperty("CancellationPolicies", out var cpProp) && cpProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var cp in cpProp.EnumerateArray())
                            {
                                rmDto.CancellationPolicies.Add(new HotelRoomCancellationPolicyDto
                                {
                                    Charge = cp.TryGetProperty("Charge", out var chg) ? (chg.ValueKind == JsonValueKind.Number ? chg.GetDecimal() : 0m) : 0m,
                                    ChargeType = cp.TryGetProperty("ChargeType", out var ct) && ct.ValueKind == JsonValueKind.Number ? ct.GetInt32() : 1,
                                    Currency = cp.TryGetProperty("Currency", out var cur) ? (cur.GetString() ?? "INR") : "INR",
                                    FromDate = cp.TryGetProperty("FromDate", out var fd) ? (fd.GetString() ?? "") : "",
                                    ToDate = cp.TryGetProperty("ToDate", out var td) ? (td.GetString() ?? "") : ""
                                });
                            }
                        }

                        if (rmElem.TryGetProperty("Inclusion", out var incProp) && incProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var inc in incProp.EnumerateArray()) if (inc.GetString() != null) rmDto.Inclusion.Add(inc.GetString()!);
                        }

                        if (rmElem.TryGetProperty("Price", out var prProp) && prProp.ValueKind == JsonValueKind.Object)
                        {
                            var pr = rmDto.Price;
                            if (prProp.TryGetProperty("CurrencyCode", out var cc) && cc.GetString() != null) pr.CurrencyCode = cc.GetString()!;
                            if (prProp.TryGetProperty("RoomPrice", out var rp) && rp.ValueKind == JsonValueKind.Number) pr.RoomPrice = rp.GetDecimal();
                            if (prProp.TryGetProperty("Tax", out var tx) && tx.ValueKind == JsonValueKind.Number) pr.Tax = tx.GetDecimal();
                            if (prProp.TryGetProperty("ExtraGuestCharge", out var egc) && egc.ValueKind == JsonValueKind.Number) pr.ExtraGuestCharge = egc.GetDecimal();
                            if (prProp.TryGetProperty("ChildCharge", out var chc) && chc.ValueKind == JsonValueKind.Number) pr.ChildCharge = chc.GetDecimal();
                            if (prProp.TryGetProperty("OtherCharges", out var oc) && oc.ValueKind == JsonValueKind.Number) pr.OtherCharges = oc.GetDecimal();
                            if (prProp.TryGetProperty("Discount", out var disc) && disc.ValueKind == JsonValueKind.Number) pr.Discount = disc.GetDecimal();
                            if (prProp.TryGetProperty("PublishedPrice", out var pp) && pp.ValueKind == JsonValueKind.Number) pr.PublishedPrice = pp.GetDecimal();
                            if (prProp.TryGetProperty("PublishedPriceRoundedOff", out var ppro) && ppro.ValueKind == JsonValueKind.Number) pr.PublishedPriceRoundedOff = ppro.GetDecimal();
                            if (prProp.TryGetProperty("OfferedPrice", out var op) && op.ValueKind == JsonValueKind.Number) pr.OfferedPrice = op.GetDecimal();
                            if (prProp.TryGetProperty("OfferedPriceRoundedOff", out var opro) && opro.ValueKind == JsonValueKind.Number) pr.OfferedPriceRoundedOff = opro.GetDecimal();
                            if (prProp.TryGetProperty("AgentCommission", out var ac) && ac.ValueKind == JsonValueKind.Number) pr.AgentCommission = ac.GetDecimal();
                            if (prProp.TryGetProperty("AgentMarkUp", out var am) && am.ValueKind == JsonValueKind.Number) pr.AgentMarkUp = am.GetDecimal();
                            if (prProp.TryGetProperty("ServiceTax", out var stx) && stx.ValueKind == JsonValueKind.Number) pr.ServiceTax = stx.GetDecimal();
                            if (prProp.TryGetProperty("TDS", out var tds) && tds.ValueKind == JsonValueKind.Number) pr.TDS = tds.GetDecimal();
                            if (prProp.TryGetProperty("ServiceCharge", out var sc) && sc.ValueKind == JsonValueKind.Number) pr.ServiceCharge = sc.GetDecimal();
                            if (prProp.TryGetProperty("TotalGSTAmount", out var tga) && tga.ValueKind == JsonValueKind.Number) pr.TotalGSTAmount = tga.GetDecimal();

                            if (prProp.TryGetProperty("GST", out var gstProp) && gstProp.ValueKind == JsonValueKind.Object)
                            {
                                if (pr.GST == null) pr.GST = new HotelSearchGstDto();
                                if (gstProp.TryGetProperty("CGSTAmount", out var cgsta) && cgsta.ValueKind == JsonValueKind.Number) pr.GST.CGSTAmount = cgsta.GetDecimal();
                                if (gstProp.TryGetProperty("CGSTRate", out var cgstr) && cgstr.ValueKind == JsonValueKind.Number) pr.GST.CGSTRate = cgstr.GetDecimal();
                                if (gstProp.TryGetProperty("CessAmount", out var cessa) && cessa.ValueKind == JsonValueKind.Number) pr.GST.CessAmount = cessa.GetDecimal();
                                if (gstProp.TryGetProperty("CessRate", out var cessr) && cessr.ValueKind == JsonValueKind.Number) pr.GST.CessRate = cessr.GetDecimal();
                                if (gstProp.TryGetProperty("IGSTAmount", out var igsta) && igsta.ValueKind == JsonValueKind.Number) pr.GST.IGSTAmount = igsta.GetDecimal();
                                if (gstProp.TryGetProperty("IGSTRate", out var igstr) && igstr.ValueKind == JsonValueKind.Number) pr.GST.IGSTRate = igstr.GetDecimal();
                                if (gstProp.TryGetProperty("SGSTAmount", out var sgsta) && sgsta.ValueKind == JsonValueKind.Number) pr.GST.SGSTAmount = sgsta.GetDecimal();
                                if (gstProp.TryGetProperty("SGSTRate", out var sgstr) && sgstr.ValueKind == JsonValueKind.Number) pr.GST.SGSTRate = sgstr.GetDecimal();
                                if (gstProp.TryGetProperty("TaxableAmount", out var txa) && txa.ValueKind == JsonValueKind.Number) pr.GST.TaxableAmount = txa.GetDecimal();
                            }
                        }

                        resDto.HotelRoomsDetails.Add(rmDto);
                    }
                }

                using var scope = _serviceProvider.CreateScope();
                var markupService = scope.ServiceProvider.GetRequiredService<IHotelMarkupService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

                if (markupService != null)
                {
                    decimal totalBaseFare = 0m;
                    foreach (var rm in resDto.HotelRoomsDetails)
                    {
                        if (rm.Price != null)
                        {
                            await ApplyMarkupAndGstAsync(markupService, rm.Price, null, request.HotelCode, "B2C");
                            rm.OfferedPrice = rm.Price.OfferedPrice;
                            rm.B2CBasePrice = rm.Price.B2CBasePrice;
                            rm.B2CTotalPrice = rm.Price.B2CTotalPrice;

                            // Securely save the HotelBlockedPrice to DB
                            var blockedPrice = new PickNBook.Api.Models.Entities.HotelBlockedPrice
                            {
                                ResultIndex = request.ResultIndex,
                                HotelCode = request.HotelCode,
                                TraceId = request.TraceId,
                                OfferedPrice = rm.Price.OfferedPrice,
                                Tax = rm.Price.Tax + rm.Price.TotalGSTAmount, // Combined tax
                                MarkupAmount = rm.Price.AgentMarkUp,
                                DiscountAmount = rm.Price.Discount,
                                GrandTotal = rm.Price.B2CTotalPrice,
                                CreatedAt = DateTime.UtcNow
                            };
                            
                            dbContext.HotelBlockedPrices.Add(blockedPrice);
                        }
                    }
                    await dbContext.SaveChangesAsync();
                }
                
                if (responseDto.BlockRoomResult != null && responseDto.BlockRoomResult.Error.ErrorCode == 0)
                {
                    _cache.Set($"block_{request.ResultIndex}", responseDto, TimeSpan.FromMinutes(30));
                }

                return responseDto;
            }
            finally
            {
                jsonDoc?.Dispose();
            }
        }

        public async Task<PickNBookBookRoomResponseDto> BookRoomAsync(HotelBookRequestDto request)
        {
            JsonDocument? jsonDoc = null;
            try
            {
                if (string.IsNullOrWhiteSpace(request.TraceId) || string.IsNullOrWhiteSpace(request.ResultIndex))
                {
                    var errDto = new PickNBookBookRoomResponseDto();
                    errDto.BookResult.Error.ErrorCode = 1;
                    errDto.BookResult.Error.ErrorMessage = "TraceId and ResultIndex are required for booking.";
                    return errDto;
                }

                int resultIndexVal = 0;

                int.TryParse(request.ResultIndex, out resultIndexVal);

                var parts = request.GuestName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                string firstName = parts.Length > 0 ? parts[0] : "Guest";
                string lastName = parts.Length > 1 ? parts[1] : "User";

                string leadPaxPan = request.HotelRoomsDetails?
                    .SelectMany(r => r.HotelPassenger ?? new List<HotelPassengerDto>())
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.PAN))?.PAN?.Trim() ?? "";

                var payload = new
                {
                    EndUserIp = request.EndUserIp,
                    ClientId = _settings.ClientId,
                    UserName = _settings.UserName,
                    Password = _settings.Password,
                    TokenId = "",
                    TraceId = int.TryParse(request.TraceId, out var tid) ? (object)tid : request.TraceId,
                    SrdvType = request.SrdvType,
                    SrdvIndex = request.SrdvIndex,
                    ResultIndex = resultIndexVal != 0 ? (object)resultIndexVal : request.ResultIndex,
                    HotelCode = request.HotelCode,
                    HotelName = request.HotelName,
                    GuestNationality = request.GuestNationality,
                    NoOfRooms = (request.NoOfRooms <= 0 ? 1 : request.NoOfRooms).ToString(),
                    ClientReferenceNo = request.ClientReferenceNo,
                    IsVoucherBooking = request.IsVoucherBooking,
                    PAN = leadPaxPan ?? "",
                    HotelRoomsDetails = request.HotelRoomsDetails.Select(room => new
                    {
                        room.ChildCount,
                        room.RequireAllPaxDetails,
                        room.RoomId,
                        room.RoomStatus,
                        room.RoomIndex,
                        room.RoomTypeCode,
                        room.RoomTypeName,
                        room.RatePlanCode,
                        room.RatePlan,
                        room.InfoSource,
                        DayRates = room.DayRates != null ? (object)room.DayRates.Select(d => new { d.Amount, d.Date }).ToList() : new object[] { },
                        room.SupplierPrice,
                        room.Price,
                        PAN = leadPaxPan ?? "",
                        HotelPassenger = room.HotelPassenger != null ? (object)room.HotelPassenger.Select(p => new
                        {
                            Title = NormalizeTitle(p.Title),
                            p.FirstName,
                            MiddleName = string.IsNullOrWhiteSpace(p.MiddleName) ? null : p.MiddleName,
                            p.LastName,
                            p.Phoneno,
                            p.Email,
                            p.PaxType,
                            p.LeadPassenger,
                            PassportNo = string.IsNullOrWhiteSpace(p.PassportNo) ? null : p.PassportNo,
                            PassportIssueDate = string.IsNullOrWhiteSpace(p.PassportIssueDate) ? null : p.PassportIssueDate,
                            PassportExpDate = string.IsNullOrWhiteSpace(p.PassportExpDate) ? null : p.PassportExpDate,
                            PAN = !string.IsNullOrWhiteSpace(p.PAN) ? p.PAN : (leadPaxPan ?? ""),
                            GSTCompanyAddress = string.IsNullOrWhiteSpace(p.GSTCompanyAddress) ? null : p.GSTCompanyAddress,
                            GSTCompanyContactNumber = string.IsNullOrWhiteSpace(p.GSTCompanyContactNumber) ? null : p.GSTCompanyContactNumber,
                            GSTCompanyName = string.IsNullOrWhiteSpace(p.GSTCompanyName) ? null : p.GSTCompanyName,
                            GSTNumber = string.IsNullOrWhiteSpace(p.GSTNumber) ? null : p.GSTNumber,
                            GSTCompanyEmail = string.IsNullOrWhiteSpace(p.GSTCompanyEmail) ? null : p.GSTCompanyEmail
                        }).ToList() : new object[] { },
                        room.RoomPromotion,
                        Amenities = room.Amenities != null ? (object)room.Amenities.Select(a => new { a.Name, a.FontAwesome, a.IcoFont }).ToList() : new object[] { },
                        room.SmokingPreference,
                        room.BedTypes,
                        room.HotelSupplements,
                        room.LastCancellationDate,
                        CancellationPolicies = room.CancellationPolicies != null ? (object)room.CancellationPolicies.Select(cp => new { cp.Charge, cp.ChargeType, cp.Currency, cp.FromDate, cp.ToDate }).ToList() : new object[] { },
                        room.BedTypeCode,
                        room.Supplements
                    }).ToList()
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                var initialJsonStr = JsonSerializer.Serialize(payload, jsonOptions);
                var rootObj = System.Text.Json.Nodes.JsonNode.Parse(initialJsonStr) as System.Text.Json.Nodes.JsonObject;
                
                if (rootObj != null)
                {
                    var rootPan = rootObj["PAN"]?.ToString() ?? "";
                    rootObj["Pan"] = rootPan;
                    rootObj["PANNo"] = rootPan;
                    rootObj["PanNo"] = rootPan;

                    if (rootObj["HotelRoomsDetails"] is System.Text.Json.Nodes.JsonArray roomsArr)
                    {
                        foreach (var rNode in roomsArr)
                        {
                            if (rNode is System.Text.Json.Nodes.JsonObject rObj)
                            {
                                var rPan = rObj["PAN"]?.ToString() ?? "";
                                rObj["Pan"] = rPan;
                                rObj["PANNo"] = rPan;
                                rObj["PanNo"] = rPan;
                                
                                if (rObj["HotelPassenger"] is System.Text.Json.Nodes.JsonArray paxArr)
                                {
                                    foreach (var pNode in paxArr)
                                    {
                                        if (pNode is System.Text.Json.Nodes.JsonObject pObj)
                                        {
                                            var pPan = pObj["PAN"]?.ToString() ?? "";
                                            pObj["Pan"] = pPan;
                                            pObj["PANNo"] = pPan;
                                            pObj["PanNo"] = pPan;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                var jsonStr = rootObj?.ToJsonString() ?? initialJsonStr;
                _logger?.LogWarning("BOOK REQUEST PAYLOAD SENT TO SRDV: {JsonStr}", jsonStr);

                var httpRes = await _httpClient.PostAsync($"{_settings.HotelBaseUrl}/Book", new StringContent(jsonStr, System.Text.Encoding.UTF8, "application/json"));
                if (!httpRes.IsSuccessStatusCode)
                {
                    var errDto = new PickNBookBookRoomResponseDto();
                    errDto.BookResult.Error.ErrorCode = (int)httpRes.StatusCode;
                    errDto.BookResult.Error.ErrorMessage = $"SRDV API returned {httpRes.StatusCode}";
                    return errDto;
                }

                using var contentStrStream = await httpRes.Content.ReadAsStreamAsync();
            jsonDoc = await JsonDocument.ParseAsync(contentStrStream);
                var root = jsonDoc.RootElement;

                var responseDto = new PickNBookBookRoomResponseDto();
                var resDto = responseDto.BookResult;

                if (!root.TryGetProperty("BookResult", out var target))
                {
                    target = root; // fallback
                }

                if (target.TryGetProperty("Error", out var errProp) && errProp.ValueKind == JsonValueKind.Object)
                {
                    if (errProp.TryGetProperty("ErrorCode", out var ec))
                    {
                        if (ec.ValueKind == JsonValueKind.Number) resDto.Error.ErrorCode = ec.GetInt32();
                        else if (ec.ValueKind == JsonValueKind.String && int.TryParse(ec.GetString(), out int parsedCode)) resDto.Error.ErrorCode = parsedCode;
                    }
                    if (errProp.TryGetProperty("ErrorMessage", out var em)) resDto.Error.ErrorMessage = em.GetString() ?? "";
                }

                if (resDto.Error.ErrorCode != 0 || !string.IsNullOrEmpty(resDto.Error.ErrorMessage))
                {
                    return responseDto;
                }

                if (target.TryGetProperty("VoucherStatus", out var vsProp) && (vsProp.ValueKind == JsonValueKind.True || vsProp.ValueKind == JsonValueKind.False)) resDto.VoucherStatus = vsProp.GetBoolean();
                if (target.TryGetProperty("ResponseStatus", out var rsProp) && rsProp.ValueKind == JsonValueKind.Number) resDto.ResponseStatus = rsProp.GetInt32();
                if (target.TryGetProperty("TraceId", out var tidProp)) resDto.TraceId = tidProp.ValueKind == JsonValueKind.Number ? tidProp.GetRawText() : (tidProp.GetString() ?? "");
                if (target.TryGetProperty("Status", out var stProp)) resDto.Status = stProp.GetString() ?? "Confirmed";
                if (target.TryGetProperty("HotelBookingStatus", out var hbsProp)) resDto.HotelBookingStatus = hbsProp.GetString() ?? "Confirmed";
                if (target.TryGetProperty("InvoiceNumber", out var invProp)) resDto.InvoiceNumber = invProp.GetString() ?? "";
                if (target.TryGetProperty("ConfirmationNo", out var cnoProp)) resDto.ConfirmationNo = cnoProp.GetString() ?? "";
                if (target.TryGetProperty("BookingRefNo", out var brnProp)) resDto.BookingRefNo = brnProp.GetString() ?? "";
                if (target.TryGetProperty("BookingId", out var bidProp) && bidProp.ValueKind == JsonValueKind.Number) resDto.BookingId = bidProp.GetInt32();
                if (target.TryGetProperty("IsPriceChanged", out var ipcProp) && (ipcProp.ValueKind == JsonValueKind.True || ipcProp.ValueKind == JsonValueKind.False)) resDto.IsPriceChanged = ipcProp.GetBoolean();
                if (target.TryGetProperty("IsCancellationPolicyChanged", out var icpcProp) && (icpcProp.ValueKind == JsonValueKind.True || icpcProp.ValueKind == JsonValueKind.False)) resDto.IsCancellationPolicyChanged = icpcProp.GetBoolean();


                return responseDto;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during BookRoomAsync for HotelCode {HotelCode}.", request.HotelCode);
                var errDto = new PickNBookBookRoomResponseDto();
                errDto.BookResult.Error.ErrorCode = 1;
                errDto.BookResult.Error.ErrorMessage = $"Failed to book room on SRDV. Exception: {ex.Message}";
                return errDto;
            }
            finally
            {
                jsonDoc?.Dispose();
            }
        }


        public async Task<SendChangeResponseDto> CancelRoomAsync(HotelCancelRequestDto request)
        {
            System.Text.Json.JsonDocument? jsonDoc = null;
            try
            {
                if (request.BookingId <= 0 || string.IsNullOrWhiteSpace(request.SrdvType) || string.IsNullOrWhiteSpace(request.SrdvIndex))
                {
                    var errDto = new SendChangeResponseDto();
                    errDto.Error.ErrorCode = 1;
                    errDto.Error.ErrorMessage = "Invalid cancellation request parameters.";
                    return errDto;
                }

                var payload = new
                {
                    BookingId = request.BookingId,
                    ChangeRequestId = request.ChangeRequestId > 0 ? request.ChangeRequestId : 0,
                    RequestType = request.RequestType <= 0 ? 4 : request.RequestType,
                    BookingMode = request.BookingMode <= 0 ? 5 : request.BookingMode,
                    Remarks = !string.IsNullOrWhiteSpace(request.Remarks) ? request.Remarks : "Hotel Cancellation Request",
                    SrdvType = request.SrdvType ?? "",
                    SrdvIndex = request.SrdvIndex ?? "",
                    EndUserIp = request.EndUserIp,
                    ClientId = _settings.ClientId,
                    UserName = _settings.UserName,
                    Password = _settings.Password,
                    TokenId = "",
                    TraceId = request.TraceId ?? ""
                };

                var httpRes = await _httpClient.PostAsJsonAsync($"{_settings.HotelBaseUrl}/Cancel", payload, new JsonSerializerOptions { PropertyNamingPolicy = null });
                if (!httpRes.IsSuccessStatusCode)
                {
                    var errDto = new SendChangeResponseDto();
                    errDto.Error.ErrorCode = (int)httpRes.StatusCode;
                    errDto.Error.ErrorMessage = $"SRDV API returned {httpRes.StatusCode}";
                    return errDto;
                }

                using var contentStrStream = await httpRes.Content.ReadAsStreamAsync();
            jsonDoc = await JsonDocument.ParseAsync(contentStrStream);
                var root = jsonDoc.RootElement;

                var resDto = new SendChangeResponseDto();
                
                if (root.TryGetProperty("Error", out var rootErrProp) && rootErrProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (rootErrProp.TryGetProperty("ErrorCode", out var ec))
                    {
                        resDto.Error.ErrorCode = ec.ValueKind == System.Text.Json.JsonValueKind.Number ? ec.GetInt32() : (int.TryParse(ec.GetString(), out var ev) ? ev : 0);
                    }
                    if (rootErrProp.TryGetProperty("ErrorMessage", out var em) && em.GetString() != null) resDto.Error.ErrorMessage = em.GetString()!;
                }

                if (resDto.Error.ErrorCode != 0)
                {
                    return resDto;
                }

                System.Text.Json.JsonElement target = root;
                if (root.TryGetProperty("SendChangeResponse", out var scr)) target = scr;
                else if (root.TryGetProperty("sendChangeResponse", out var scrl)) target = scrl;
                else if (root.TryGetProperty("CancelResult", out var cr)) target = cr;
                else if (root.TryGetProperty("cancelResult", out var crl)) target = crl;

                if (target.TryGetProperty("Error", out var errProp) && errProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (errProp.TryGetProperty("ErrorCode", out var ec))
                    {
                        resDto.Error.ErrorCode = ec.ValueKind == System.Text.Json.JsonValueKind.Number ? ec.GetInt32() : (int.TryParse(ec.GetString(), out var ev) ? ev : 0);
                    }
                    if (errProp.TryGetProperty("ErrorMessage", out var em)) resDto.Error.ErrorMessage = em.GetString() ?? "";
                }

                if (resDto.Error.ErrorCode != 0)
                {
                    return resDto;
                }

                if ((target.TryGetProperty("ResponseStatus", out var rsProp) || target.TryGetProperty("responseStatus", out rsProp)) && rsProp.ValueKind == System.Text.Json.JsonValueKind.Number) resDto.ResponseStatus = rsProp.GetInt32();
                if (target.TryGetProperty("SrdvType", out var stProp) || target.TryGetProperty("srdvType", out stProp)) resDto.SrdvType = stProp.GetString() ?? (request.SrdvType ?? "MixAPI");
                if (target.TryGetProperty("SrdvIndex", out var siProp) || target.TryGetProperty("srdvIndex", out siProp)) resDto.SrdvIndex = siProp.GetString() ?? (request.SrdvIndex ?? "");
                if (target.TryGetProperty("TraceId", out var tidProp) || target.TryGetProperty("traceId", out tidProp)) resDto.TraceId = tidProp.ValueKind == System.Text.Json.JsonValueKind.Number ? tidProp.GetRawText() : (tidProp.GetString() ?? request.TraceId);
                if ((target.TryGetProperty("ChangeRequestId", out var cridProp) || target.TryGetProperty("changeRequestId", out cridProp)) && cridProp.ValueKind == System.Text.Json.JsonValueKind.Number) resDto.ChangeRequestId = cridProp.GetInt32();
                if ((target.TryGetProperty("ChangeRequestStatus", out var crsProp) || target.TryGetProperty("changeRequestStatus", out crsProp)) && crsProp.ValueKind == System.Text.Json.JsonValueKind.Number) resDto.ChangeRequestStatus = crsProp.GetInt32();
                if ((target.TryGetProperty("RefundedAmount", out var refProp) || target.TryGetProperty("refundedAmount", out refProp)) && refProp.ValueKind == System.Text.Json.JsonValueKind.Number) resDto.RefundedAmount = refProp.GetDecimal();
                if ((target.TryGetProperty("CancellationCharge", out var canProp) || target.TryGetProperty("cancellationCharge", out canProp)) && canProp.ValueKind == System.Text.Json.JsonValueKind.Number) resDto.CancellationCharge = canProp.GetDecimal();
                return resDto;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during CancelRoomAsync for BookingId {BookingId}.", request.BookingId);
                var errDto = new SendChangeResponseDto();
                errDto.Error.ErrorCode = 1;
                errDto.Error.ErrorMessage = $"Failed to cancel room on SRDV. Exception: {ex.Message}";
                return errDto;
            }
            finally
            {
                jsonDoc?.Dispose();
            }
        }

        public async Task<BalanceResponseDto> GetBalanceAsync(BalanceRequestDto request)
        {
            try
            {
                var payload = new
                {
                    EndUserIp = request.EndUserIp,
                    ClientId = _settings.ClientId,
                    UserName = _settings.UserName,
                    Password = _settings.Password,
                };

                var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
                var httpRes = await _httpClient.PostAsync($"{_settings.HotelBaseUrl}/Balance", content);
                
                if (!httpRes.IsSuccessStatusCode)
                {
                    return new BalanceResponseDto { Error = new HotelSearchErrorDto { ErrorCode = (int)httpRes.StatusCode, ErrorMessage = "HTTP Request Failed" } };
                }

                var resStr = await httpRes.Content.ReadAsStringAsync();
                var options = new System.Text.Json.JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                };
                var result = System.Text.Json.JsonSerializer.Deserialize<BalanceResponseDto>(resStr, options);
                return result ?? new BalanceResponseDto { Error = new HotelSearchErrorDto { ErrorCode = 1, ErrorMessage = "Deserialization Failed" } };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Balance");
                return new BalanceResponseDto { Error = new HotelSearchErrorDto { ErrorCode = 500, ErrorMessage = ex.Message } };
            }
        }

        public async Task<BalanceLogResponseDto> GetBalanceLogAsync(BalanceLogRequestDto request)
        {
            try
            {
                var payload = new
                {
                    EndUserIp = request.EndUserIp,
                    ClientId = _settings.ClientId,
                    UserName = _settings.UserName,
                    Password = _settings.Password,
                };

                var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
                var httpRes = await _httpClient.PostAsync($"{_settings.HotelBaseUrl}/BalanceLog", content);
                
                if (!httpRes.IsSuccessStatusCode)
                {
                    return new BalanceLogResponseDto { Error = new HotelSearchErrorDto { ErrorCode = (int)httpRes.StatusCode, ErrorMessage = "HTTP Request Failed" } };
                }

                var resStr = await httpRes.Content.ReadAsStringAsync();
                var options = new System.Text.Json.JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                };
                var result = System.Text.Json.JsonSerializer.Deserialize<BalanceLogResponseDto>(resStr, options);
                
                if (result != null && result.Result != null)
                {
                    // Filter logs to only include Hotel-related logs
                    result.Result = result.Result.Where(r => r.Module != null && r.Module.Contains("Hotel", StringComparison.OrdinalIgnoreCase)).ToList();
                    return result;
                }

                return new BalanceLogResponseDto { Error = new HotelSearchErrorDto { ErrorCode = 1, ErrorMessage = "Deserialization Failed" } };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Balance Log");
                return new BalanceLogResponseDto { Error = new HotelSearchErrorDto { ErrorCode = 500, ErrorMessage = ex.Message } };
            }
        }

        public static (decimal CancellationCharges, decimal RefundAmount) EvaluateCancellationFee(HotelReservation booking)
        {
            if (booking == null) return (0m, 0m);

            if (!string.IsNullOrWhiteSpace(booking.CancellationPolicyJson))
            {
                try
                {
                    var policies = JsonSerializer.Deserialize<List<HotelRoomCancellationPolicyDto>>(booking.CancellationPolicyJson);
                    if (policies != null && policies.Count > 0)
                    {
                        var now = DateTime.UtcNow;

                        if (booking.LastCancellationDate.HasValue && now <= booking.LastCancellationDate.Value)
                        {
                            return (0m, booking.Price);
                        }

                        HotelRoomCancellationPolicyDto? matchingPolicy = null;
                        foreach (var p in policies)
                        {
                            if (DateTime.TryParse(p.FromDate, out var fromDt))
                            {
                                if (DateTime.TryParse(p.ToDate, out var toDt))
                                {
                                    if (now >= fromDt && now <= toDt)
                                    {
                                        matchingPolicy = p;
                                        break;
                                    }
                                }
                                else if (now >= fromDt)
                                {
                                    matchingPolicy = p;
                                }
                            }
                        }

                        if (matchingPolicy == null)
                        {
                            matchingPolicy = policies.LastOrDefault();
                        }

                        if (matchingPolicy != null)
                        {
                            decimal penalty = 0m;
                            if (matchingPolicy.ChargeType == 1) // Percentage
                            {
                                penalty = booking.Price * (matchingPolicy.Charge / 100m);
                            }
                            else // Flat Amount
                            {
                                penalty = matchingPolicy.Charge;
                            }

                            penalty = Math.Min(booking.Price, Math.Max(0m, penalty));
                            penalty = decimal.Round(penalty, 2, MidpointRounding.AwayFromZero);
                            decimal refund = Math.Max(0m, booking.Price - penalty);

                            return (penalty, refund);
                        }
                    }
                }
                catch
                {
                    // Fallback on parse exception
                }
            }

            if (booking.LastCancellationDate.HasValue && DateTime.UtcNow <= booking.LastCancellationDate.Value)
            {
                return (0m, booking.Price);
            }
            if (DateTime.UtcNow < booking.CheckInDate)
            {
                return (0m, booking.Price);
            }

            return (booking.Price, 0m);
        }

        private static string NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "Mr";
            var t = title.Trim().ToLowerInvariant();
            return t switch
            {
                "mrs" or "mrs." => "Mrs",
                "ms" or "ms." => "Ms",
                "miss" => "Miss",
                "master" or "mstr" => "Master",
                "dr" or "dr." => "Dr",
                _ => "Mr"
            };
        }

        private async Task ApplyMarkupAndGstAsync(IHotelMarkupService markupService, HotelSearchPriceDto price, string? cityId, string? hotelCode, string userType)
        {
            if (price == null || markupService == null) return;

            decimal supplierBase = price.OfferedPrice > 0 ? price.OfferedPrice : (price.RoomPrice > 0 ? price.RoomPrice : 0);
            if (supplierBase == 0) return;

            decimal markup = await markupService.CalculateMarkupAsync(supplierBase, cityId, hotelCode, userType);
            
            if (markup >= 0)
            {
                price.AgentMarkUp = markup;
                
                price.B2CTotalPrice = price.OfferedPrice + markup;
                price.B2CBasePrice = Math.Max(0m, price.B2CTotalPrice - price.TotalGSTAmount);
                price.B2CFinalFare = price.B2CTotalPrice;
            }
        }
    }
}






