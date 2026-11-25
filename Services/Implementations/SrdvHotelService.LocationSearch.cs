using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public partial class SrdvHotelService
    {
        private static readonly JsonSerializerOptions SrdvHotelJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        static SrdvHotelService()
        {
            SrdvHotelJsonOptions.Converters.Add(new SafeListConverterFactory());
        }

        private async Task<HttpResponseMessage> SendSrdvHotelPostAsync<TBody>(
            string url,
            TBody body,
            CancellationToken cancellationToken = default)
        {
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body)
            };

            if (!string.IsNullOrEmpty(_settings.ApiToken))
            {
                requestMessage.Headers.TryAddWithoutValidation("Api-Token", _settings.ApiToken);
            }

            return await _httpClient.SendAsync(requestMessage, cancellationToken);
        }

        internal static string BuildHotelSearchCacheKey(
            long? cityId,
            IEnumerable<long>? hotelCodes,
            string? checkInDate,
            int noOfNights,
            string? checkOutDate,
            string? guestNationality,
            List<RoomGuestDto>? roomGuests,
            string? minRating,
            string? maxRating)
        {
            string hotelCodesToken = "none";
            if (hotelCodes != null)
            {
                var normalizedCodes = hotelCodes.Where(h => h > 0).Distinct().OrderBy(h => h).ToList();
                if (normalizedCodes.Count > 0)
                {
                    var joined = string.Join(",", normalizedCodes);
                    if (normalizedCodes.Count <= 8)
                    {
                        hotelCodesToken = joined;
                    }
                    else
                    {
                        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
                        hotelCodesToken = $"sha256:{Convert.ToHexString(hashBytes, 0, 8)}";
                    }
                }
            }

            string guestsToken = "1-0";
            if (roomGuests != null && roomGuests.Count > 0)
            {
                guestsToken = string.Join("_", roomGuests.Select(rg =>
                {
                    var ages = rg.ChildAge != null && rg.ChildAge.Count > 0
                        ? $"[{string.Join(",", rg.ChildAge)}]"
                        : string.Empty;
                    return $"{rg.NoOfAdults}-{rg.NoOfChild}{ages}";
                }));
            }

            var nat = string.IsNullOrWhiteSpace(guestNationality) ? "IN" : guestNationality.Trim().ToUpperInvariant();
            var minR = string.IsNullOrWhiteSpace(minRating) ? "0" : minRating.Trim();
            var maxR = string.IsNullOrWhiteSpace(maxRating) ? "7" : maxRating.Trim();

            return $"srdv:hotel:search:c={cityId?.ToString() ?? "none"}:h={hotelCodesToken}:in={checkInDate}:n={noOfNights}:out={checkOutDate ?? ""}:nat={nat}:g={guestsToken}:r={minR}-{maxR}";
        }

        public async Task<PickNBookCityAutoSuggestResponseDto> CityAutoSuggestAsync(
            SrdvCityAutoSuggestRequestDto request,
            bool forceProbe = false,
            CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Search))
            {
                return new PickNBookCityAutoSuggestResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 1003, ErrorMessage = "Search query is required (3 to 100 characters)." }
                };
            }

            var trimmedSearch = request.Search.Trim();
            if (trimmedSearch.Length < 3 || trimmedSearch.Length > 100)
            {
                return new PickNBookCityAutoSuggestResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 1003, ErrorMessage = "Search must be between 3 and 100 characters." }
                };
            }

            if (request.Limit.HasValue && (request.Limit.Value < 1 || request.Limit.Value > 50))
            {
                return new PickNBookCityAutoSuggestResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 1003, ErrorMessage = "Limit must be an integer between 1 and 50." }
                };
            }

            int effectiveLimit = Math.Clamp(request.Limit ?? 50, 1, 50);

            bool autoSuggestConfigured = _settings.EnableCityAutoSuggest ||
                string.Equals(_settings.HotelLocationMode, "AutoSuggest", StringComparison.OrdinalIgnoreCase);

            if (!autoSuggestConfigured && !forceProbe)
            {
                return new PickNBookCityAutoSuggestResponseDto
                {
                    Error = new HotelSearchErrorDto
                    {
                        ErrorCode = 13005,
                        ErrorMessage = "Location suggestions are not available for this Hotel route (HotelLocationMode=CityList)."
                    }
                };
            }

            var cacheKey = $"srdv:hotel:autosuggest:{trimmedSearch.ToLowerInvariant()}:{effectiveLimit}";
            if (!forceProbe && _cache.TryGetValue(cacheKey, out PickNBookCityAutoSuggestResponseDto? cached) && cached != null)
            {
                return cached;
            }

            try
            {
                var url = $"{_settings.HotelBaseUrl.TrimEnd('/')}/CityAutoSuggest";
                var supplierReq = new SrdvSupplierCityAutoSuggestRequest
                {
                    Search = trimmedSearch,
                    Limit = effectiveLimit
                };

                using var response = await SendSrdvHotelPostAsync(url, supplierReq, cancellationToken);
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var raw = await JsonSerializer.DeserializeAsync<SrdvRawCityAutoSuggestResponse>(stream, SrdvHotelJsonOptions, cancellationToken);

                if (raw == null)
                {
                    return new PickNBookCityAutoSuggestResponseDto
                    {
                        Error = new HotelSearchErrorDto { ErrorCode = (int)response.StatusCode, ErrorMessage = "Empty response from SRDV CityAutoSuggest." }
                    };
                }

                var dto = new PickNBookCityAutoSuggestResponseDto
                {
                    ResultsCount = raw.ResultsCount,
                    Error = new HotelSearchErrorDto
                    {
                        ErrorCode = raw.Error?.ErrorCode ?? 0,
                        ErrorMessage = raw.Error?.ErrorMessage ?? string.Empty
                    }
                };

                if (dto.Error.ErrorCode != 0)
                {
                    _logger?.LogWarning("SRDV CityAutoSuggest returned error ({ErrorCode}): {ErrorMessage}", dto.Error.ErrorCode, dto.Error.ErrorMessage);
                    return dto;
                }

                var labelTtl = TimeSpan.FromMinutes(Math.Clamp(_settings.AutoSuggestCacheMinutes, 1, 1440));

                if (raw.Result != null)
                {
                    foreach (var row in raw.Result)
                    {
                        var kind = string.IsNullOrWhiteSpace(row.Kind) ? "CITY" : row.Kind.Trim().ToUpperInvariant();
                        bool isHotel = string.Equals(kind, "HOTEL", StringComparison.OrdinalIgnoreCase);

                        var item = new CityAutoSuggestItemDto
                        {
                            Kind = kind,
                            CityId = string.IsNullOrWhiteSpace(row.CityId) ? null : row.CityId.Trim(),
                            CityName = row.CityName?.Trim() ?? string.Empty,
                            StateName = row.StateName,
                            CountryCode = row.CountryCode?.Trim() ?? string.Empty,
                            Type = row.Type,
                            FullName = row.FullName,
                            ParentCityId = row.ParentCityId,
                            ParentCityName = row.ParentCityName,
                            HotelCount = row.HotelCount,
                            ParentOnPage = row.ParentOnPage,
                            HotelId = string.IsNullOrWhiteSpace(row.HotelId) ? null : row.HotelId.Trim(),
                            HotelName = row.HotelName,
                            StarRating = row.StarRating,
                            SearchTargetType = isHotel ? "HotelCodes" : "CityId",
                            SearchTargetId = isHotel ? (row.HotelId?.Trim() ?? string.Empty) : (row.CityId?.Trim() ?? string.Empty)
                        };

                        dto.Result.Add(item);

                        // Cache resolved labels briefly for HotelSearchLog enrichment (max 30d upstream; local cache is short)
                        if (isHotel && !string.IsNullOrWhiteSpace(item.HotelId))
                        {
                            var label = !string.IsNullOrWhiteSpace(item.HotelName)
                                ? $"{item.HotelName} ({item.CityName})"
                                : item.CityName;
                            _cache.Set($"srdv:hotel:label:hotel:{item.HotelId}", label, labelTtl);
                        }
                        else if (!string.IsNullOrWhiteSpace(item.CityId))
                        {
                            var label = !string.IsNullOrWhiteSpace(item.FullName) ? item.FullName : item.CityName;
                            _cache.Set($"srdv:hotel:label:city:{item.CityId}", label, labelTtl);
                        }
                    }
                }

                if (dto.ResultsCount == 0 && dto.Result.Count > 0)
                {
                    dto.ResultsCount = dto.Result.Count;
                }

                _cache.Set(cacheKey, dto, labelTtl);
                return dto;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SRDV CityAutoSuggest call failed for query '{Search}'", trimmedSearch);
                return new PickNBookCityAutoSuggestResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 502, ErrorMessage = $"SRDV CityAutoSuggest failure: {ex.Message}" }
                };
            }
        }

        public async Task<PickNBookNearbyHotelResponseDto> GetNearbyHotelsAsync(
            SrdvNearbyHotelRequestDto request,
            CancellationToken cancellationToken = default)
        {
            if (request == null ||
                double.IsNaN(request.Latitude) || double.IsInfinity(request.Latitude) ||
                request.Latitude < -90d || request.Latitude > 90d)
            {
                return new PickNBookNearbyHotelResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 1003, ErrorMessage = "Latitude must be between -90 and 90." }
                };
            }

            if (double.IsNaN(request.Longitude) || double.IsInfinity(request.Longitude) ||
                request.Longitude < -180d || request.Longitude > 180d)
            {
                return new PickNBookNearbyHotelResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 1003, ErrorMessage = "Longitude must be between -180 and 180." }
                };
            }

            if (request.RadiusMeters.HasValue && (request.RadiusMeters.Value < 100 || request.RadiusMeters.Value > 10000))
            {
                return new PickNBookNearbyHotelResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 1003, ErrorMessage = "RadiusMeters must be an integer between 100 and 10000." }
                };
            }

            if (!_settings.EnableNearbyHotel)
            {
                return new PickNBookNearbyHotelResponseDto
                {
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    RadiusMeters = request.RadiusMeters ?? 10000,
                    Error = new HotelSearchErrorDto
                    {
                        ErrorCode = 13000,
                        ErrorMessage = "NearbyHotel is disabled for this route configuration (CityAutoSuggest accounts do not support NearbyHotel HotelCodes)."
                    }
                };
            }

            int effectiveRadius = Math.Clamp(request.RadiusMeters ?? 10000, 100, 10000);
            var latKey = Math.Round(request.Latitude, 4).ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            var lngKey = Math.Round(request.Longitude, 4).ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            var cacheKey = $"srdv:hotel:nearby:{latKey}:{lngKey}:{effectiveRadius}";

            if (_cache.TryGetValue(cacheKey, out PickNBookNearbyHotelResponseDto? cached) && cached != null)
            {
                return cached;
            }

            var url = $"{_settings.HotelBaseUrl.TrimEnd('/')}/NearbyHotel";
            var supplierReq = new SrdvSupplierNearbyHotelRequest
            {
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                RadiusMeters = effectiveRadius
            };

            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var response = await SendSrdvHotelPostAsync(url, supplierReq, cancellationToken);
                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var raw = await JsonSerializer.DeserializeAsync<SrdvRawNearbyHotelResponse>(stream, SrdvHotelJsonOptions, cancellationToken);

                    if (raw == null)
                    {
                        return new PickNBookNearbyHotelResponseDto
                        {
                            Latitude = request.Latitude,
                            Longitude = request.Longitude,
                            RadiusMeters = effectiveRadius,
                            Error = new HotelSearchErrorDto { ErrorCode = (int)response.StatusCode, ErrorMessage = "Empty response from SRDV NearbyHotel." }
                        };
                    }

                    int errorCode = raw.Error?.ErrorCode ?? 0;
                    string errorMsg = raw.Error?.ErrorMessage ?? string.Empty;

                    // SRDV ErrorCode 13007: transient "the hotel list could not be read — try again"
                    if (errorCode == 13007 && attempt < maxAttempts)
                    {
                        _logger?.LogWarning("SRDV NearbyHotel returned 13007 on attempt {Attempt}; retrying once...", attempt);
                        await Task.Delay(250, cancellationToken);
                        continue;
                    }

                    var dto = new PickNBookNearbyHotelResponseDto
                    {
                        Error = new HotelSearchErrorDto { ErrorCode = errorCode, ErrorMessage = errorMsg },
                        Latitude = raw.Latitude != 0 ? raw.Latitude : request.Latitude,
                        Longitude = raw.Longitude != 0 ? raw.Longitude : request.Longitude,
                        RadiusMeters = raw.RadiusMeters > 0 ? raw.RadiusMeters : effectiveRadius,
                        ResultsCount = raw.ResultsCount,
                        Truncated = raw.Truncated
                    };

                    if (errorCode != 0)
                    {
                        _logger?.LogWarning("SRDV NearbyHotel returned error ({ErrorCode}): {ErrorMessage}", errorCode, errorMsg);
                        return dto;
                    }

                    if (raw.Result != null)
                    {
                        foreach (var row in raw.Result)
                        {
                            if (row.HotelCode <= 0) continue;
                            dto.Result.Add(new NearbyHotelItemDto
                            {
                                HotelCode = row.HotelCode,
                                CityId = row.CityId,
                                Latitude = row.Latitude,
                                Longitude = row.Longitude,
                                StarRating = row.StarRating,
                                DistanceMeters = row.DistanceMeters
                            });
                        }
                    }

                    if (dto.ResultsCount == 0 && dto.Result.Count > 0)
                    {
                        dto.ResultsCount = dto.Result.Count;
                    }

                    var cacheTtl = TimeSpan.FromMinutes(Math.Clamp(_settings.NearbyHotelCacheMinutes, 1, 1440));
                    _cache.Set(cacheKey, dto, cacheTtl);
                    return dto;
                }
                catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogWarning(ex, "SRDV NearbyHotel attempt {Attempt} failed; retrying once...", attempt);
                    await Task.Delay(250, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "SRDV NearbyHotel call failed for ({Lat}, {Lng}, {Radius}m)", request.Latitude, request.Longitude, effectiveRadius);
                    return new PickNBookNearbyHotelResponseDto
                    {
                        Latitude = request.Latitude,
                        Longitude = request.Longitude,
                        RadiusMeters = effectiveRadius,
                        Error = new HotelSearchErrorDto { ErrorCode = 502, ErrorMessage = $"SRDV NearbyHotel failure: {ex.Message}" }
                    };
                }
            }

            return new PickNBookNearbyHotelResponseDto
            {
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                RadiusMeters = effectiveRadius,
                Error = new HotelSearchErrorDto { ErrorCode = 13007, ErrorMessage = "Upstream hotel list could not be read. Please try again." }
            };
        }

        public async Task<PickNBookHotelSearchResponseDto> SearchNearbyHotelsAsync(
            SrdvNearbyHotelSearchRequestDto request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return new PickNBookHotelSearchResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 1003, ErrorMessage = "Request body is required." }
                };
            }

            int batchSize = Math.Clamp(request.BatchSize > 0 ? request.BatchSize : _settings.NearbyHotelDefaultBatchSize, 1, 200);
            int batchIndex = Math.Max(1, request.BatchIndex);
            int maxBatches = Math.Clamp(request.MaxBatches > 0 ? request.MaxBatches : 1, 1, 3);
            int effectiveRadius = Math.Clamp(request.RadiusMeters ?? 10000, 100, 10000);

            var nearby = await GetNearbyHotelsAsync(new SrdvNearbyHotelRequestDto
            {
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                RadiusMeters = effectiveRadius
            }, cancellationToken);

            if (nearby.Error != null && nearby.Error.ErrorCode != 0)
            {
                return new PickNBookHotelSearchResponseDto
                {
                    Error = new HotelSearchErrorDto
                    {
                        ErrorCode = nearby.Error.ErrorCode,
                        ErrorMessage = nearby.Error.ErrorMessage
                    },
                    NearbyMetadata = new HotelNearbyMetadataDto
                    {
                        SearchLatitude = request.Latitude,
                        SearchLongitude = request.Longitude,
                        RadiusMeters = effectiveRadius
                    }
                };
            }

            int totalNearby = nearby.Result.Count;
            int totalBatches = totalNearby == 0 ? 0 : (int)Math.Ceiling(totalNearby / (double)batchSize);

            if (totalNearby == 0 || batchIndex > totalBatches)
            {
                return new PickNBookHotelSearchResponseDto
                {
                    Error = new HotelSearchErrorDto { ErrorCode = 0, ErrorMessage = string.Empty },
                    CheckInDate = request.CheckInDate,
                    CheckOutDate = request.CheckOutDate,
                    ResultStatus = "COMPLETED",
                    NearbyMetadata = new HotelNearbyMetadataDto
                    {
                        SearchLatitude = nearby.Latitude,
                        SearchLongitude = nearby.Longitude,
                        RadiusMeters = nearby.RadiusMeters,
                        TotalNearbyHotels = totalNearby,
                        TotalBatches = totalBatches,
                        BatchIndex = batchIndex,
                        BatchSize = batchSize,
                        Truncated = nearby.Truncated,
                        PricedHotelsCount = 0
                    }
                };
            }

            // Build lookup of HotelCode -> DistanceMeters (nearest distance wins if duplicate)
            var distanceMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in nearby.Result)
            {
                var key = item.HotelCode.ToString();
                if (!distanceMap.ContainsKey(key))
                {
                    distanceMap[key] = item.DistanceMeters;
                }
            }

            // Slice requested batches starting from batchIndex (1-based)
            var batchSlices = new List<List<long>>();
            int startOffset = (batchIndex - 1) * batchSize;
            for (int b = 0; b < maxBatches; b++)
            {
                int sliceOffset = startOffset + (b * batchSize);
                if (sliceOffset >= totalNearby) break;

                var codes = nearby.Result
                    .Skip(sliceOffset)
                    .Take(batchSize)
                    .Select(r => r.HotelCode)
                    .Distinct()
                    .ToList();

                if (codes.Count > 0)
                {
                    batchSlices.Add(codes);
                }
            }

            int maxConcurrency = Math.Clamp(_settings.NearbyHotelMaxConcurrentBatches, 1, 4);
            using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            var batchTasks = batchSlices.Select(async codes =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var searchReq = new SrdvHotelSearchRequestDto
                    {
                        HotelCodes = codes,
                        CheckInDate = request.CheckInDate,
                        CheckOutDate = request.CheckOutDate,
                        NoOfNights = request.NoOfNights,
                        GuestNationality = request.GuestNationality,
                        NoOfRooms = Math.Max(1, request.RoomGuests?.Count ?? 1).ToString(),
                        RoomGuests = request.RoomGuests ?? new List<RoomGuestDto>(),
                        MinRating = request.MinRating,
                        MaxRating = request.MaxRating,
                        DestinationName = request.DestinationName,
                        SearchKind = "NEARBY"
                    };

                    return await SearchHotelsMultiLevelAsync(searchReq);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            var batchResponses = await Task.WhenAll(batchTasks);

            var merged = new PickNBookHotelSearchResponseDto
            {
                CheckInDate = request.CheckInDate,
                CheckOutDate = request.CheckOutDate,
                ResultStatus = "COMPLETED"
            };

            var byHotelCode = new Dictionary<string, HotelSearchResultItemDto>(StringComparer.OrdinalIgnoreCase);
            HotelSearchErrorDto? firstBatchError = null;
            bool anySuccess = false;

            foreach (var resp in batchResponses)
            {
                if (resp == null) continue;

                if (resp.Error != null && resp.Error.ErrorCode != 0)
                {
                    firstBatchError ??= resp.Error;
                    continue;
                }

                anySuccess = true;

                if ((merged.TraceId == null || merged.TraceId.ToString() == "0") && resp.TraceId != null)
                {
                    merged.TraceId = resp.TraceId;
                }
                if (string.IsNullOrEmpty(merged.CityId) && !string.IsNullOrEmpty(resp.CityId))
                {
                    merged.CityId = resp.CityId;
                }
                if (!string.IsNullOrEmpty(resp.CheckInDate)) merged.CheckInDate = resp.CheckInDate;
                if (!string.IsNullOrEmpty(resp.CheckOutDate)) merged.CheckOutDate = resp.CheckOutDate;
                if (!string.IsNullOrEmpty(resp.PreferredCurrency)) merged.PreferredCurrency = resp.PreferredCurrency;
                if (merged.NoOfRooms.Count == 0 && resp.NoOfRooms != null) merged.NoOfRooms = resp.NoOfRooms;
                if (string.Equals(resp.ResultStatus, "PARTIAL", StringComparison.OrdinalIgnoreCase))
                {
                    merged.ResultStatus = "PARTIAL";
                    merged.RecheckAfterMs = Math.Max(merged.RecheckAfterMs, resp.RecheckAfterMs);
                    merged.RecheckTimeoutSeconds = Math.Max(merged.RecheckTimeoutSeconds, resp.RecheckTimeoutSeconds);
                }

                if (resp.Results != null)
                {
                    foreach (var hotel in resp.Results)
                    {
                        if (string.IsNullOrWhiteSpace(hotel.HotelCode)) continue;

                        if (!hotel.TraceId.HasValue && long.TryParse(resp.TraceId?.ToString(), out var respTraceId) && respTraceId > 0)
                        {
                            hotel.TraceId = respTraceId;
                        }

                        if (distanceMap.TryGetValue(hotel.HotelCode, out var dist))
                        {
                            hotel.DistanceMeters = dist;
                        }

                        if (!byHotelCode.TryGetValue(hotel.HotelCode, out var existing) || hotel.OfferedFare < existing.OfferedFare)
                        {
                            byHotelCode[hotel.HotelCode] = hotel;
                        }
                    }
                }
            }

            if (!anySuccess && firstBatchError != null)
            {
                merged.Error = firstBatchError;
            }

            merged.Results = byHotelCode.Values
                .OrderBy(h => h.DistanceMeters ?? int.MaxValue)
                .ThenBy(h => h.OfferedFare)
                .ToList();

            merged.NearbyMetadata = new HotelNearbyMetadataDto
            {
                SearchLatitude = nearby.Latitude,
                SearchLongitude = nearby.Longitude,
                RadiusMeters = nearby.RadiusMeters,
                TotalNearbyHotels = totalNearby,
                TotalBatches = totalBatches,
                BatchIndex = batchIndex,
                BatchSize = batchSize,
                Truncated = nearby.Truncated,
                PricedHotelsCount = merged.Results.Count
            };

            return merged;
        }
    }
}
