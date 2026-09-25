using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class SrdvMasterDataImporter : ISrdvMasterDataImporter
    {
        private readonly AppDbContext _dbContext;
        private readonly ISrdvMasterDataDownloader _downloader;
        private readonly ISrdvSqlDumpParser _parser;
        private readonly SrdvMasterDataSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SrdvMasterDataImporter> _logger;

        private const int BatchSize = 1000;

        public SrdvMasterDataImporter(
            AppDbContext dbContext,
            ISrdvMasterDataDownloader downloader,
            ISrdvSqlDumpParser parser,
            IOptions<SrdvMasterDataSettings> settings,
            IMemoryCache cache,
            ILogger<SrdvMasterDataImporter> logger)
        {
            _dbContext = dbContext;
            _downloader = downloader;
            _parser = parser;
            _settings = settings.Value;
            _cache = cache;
            _logger = logger;
        }

        public async Task<MasterDataImportResultDto> ImportBusCitiesAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var result = new MasterDataImportResultDto { EntityType = "BusCities" };
            const string stagingDir = "bus";

            _logger.LogInformation("Starting Bus Cities import from SRDV...");

            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var sqlFile = await _downloader.DownloadAndExtractAsync(_settings.BusResourceUrl, stagingDir, cancellationToken);

                var existingCities = await _dbContext.BusCities
                    .ToDictionaryAsync(x => x.CityCode.Trim(), StringComparer.OrdinalIgnoreCase, cancellationToken);

                var seenCityCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var toInsert = new List<BusCity>();
                int updatedCount = 0;
                int readCount = 0;
                int failedCount = 0;

                await foreach (var row in _parser.ParseInsertRowsAsync(sqlFile, "city_code", cancellationToken))
                {
                    readCount++;

                    row.TryGetValue("cico_id", out var cityCode);
                    row.TryGetValue("cico_city_name", out var cityName);
                    row.TryGetValue("cico_state_name", out var stateName);

                    cityCode = cityCode?.Trim();
                    cityName = cityName?.Trim();

                    if (string.IsNullOrWhiteSpace(cityCode) || string.IsNullOrWhiteSpace(cityName))
                    {
                        failedCount++;
                        continue;
                    }

                    if (!seenCityCodes.Add(cityCode))
                    {
                        continue; // Skip duplicate inside same dump
                    }

                    if (existingCities.TryGetValue(cityCode, out var existing))
                    {
                        if (existing.CityName != cityName || existing.StateName != stateName || !existing.IsActive)
                        {
                            existing.CityName = cityName;
                            existing.StateName = stateName;
                            existing.IsActive = true;
                            existing.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(new BusCity
                        {
                            CityId = long.TryParse(cityCode, out var cid) ? cid : 0,
                            CityName = cityName,
                            StateName = stateName ?? string.Empty,
                            CountryName = "India",
                            CountryCode = "IN",
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        if (toInsert.Count >= BatchSize)
                        {
                            await _dbContext.BusCities.AddRangeAsync(toInsert, cancellationToken);
                            await _dbContext.SaveChangesAsync(cancellationToken);
                            toInsert.Clear();
                        }
                    }
                }

                if (toInsert.Count > 0)
                {
                    await _dbContext.BusCities.AddRangeAsync(toInsert, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                // Deactivate records that disappeared from the latest dump
                int deactivatedCount = 0;
                foreach (var kvp in existingCities)
                {
                    if (!seenCityCodes.Contains(kvp.Key) && kvp.Value.IsActive)
                    {
                        kvp.Value.IsActive = false;
                        kvp.Value.UpdatedAt = DateTime.UtcNow;
                        deactivatedCount++;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                result.Success = true;
                result.RecordsRead = readCount;
                result.RecordsInserted = seenCityCodes.Count - (existingCities.Count - deactivatedCount);
                result.RecordsUpdated = updatedCount;
                result.RecordsDeactivated = deactivatedCount;
                result.RecordsFailed = failedCount;

                _logger.LogInformation("Bus Cities import succeeded. Read: {Read}, Inserted: {Ins}, Updated: {Upd}, Deactivated: {Deact}, Failed: {Fail}",
                    readCount, result.RecordsInserted, updatedCount, deactivatedCount, failedCount);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Bus Cities import failed! Transaction rolled back.");
            }
            finally
            {
                await _downloader.CleanupStagingAsync(stagingDir);
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
            }

            return result;
        }

        public async Task<MasterDataImportResultDto> ImportHotelCitiesAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var result = new MasterDataImportResultDto { EntityType = "HotelCities" };
            const string hotelStaging = "hotel_v8";

            _logger.LogInformation("Starting Hotel Cities import from SRDV v8 master dataset...");

            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // 1. Resolve SQL file path (prefer local Data/cities.sql if available, else download)
                string sqlFilePath = "";
                var candidatePaths = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, _settings.HotelCitiesLocalPath),
                    Path.Combine(Directory.GetCurrentDirectory(), _settings.HotelCitiesLocalPath),
                    Path.Combine(AppContext.BaseDirectory, "Data", "cities.sql"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Data", "cities.sql")
                };

                foreach (var path in candidatePaths)
                {
                    if (File.Exists(path))
                    {
                        sqlFilePath = path;
                        _logger.LogInformation("Found local Hotel Cities SQL file at {Path}", sqlFilePath);
                        break;
                    }
                }

                if (string.IsNullOrEmpty(sqlFilePath))
                {
                    _logger.LogInformation("Local SQL file not found. Downloading from {Url}...", _settings.HotelCitiesResourceUrl);
                    sqlFilePath = await _downloader.DownloadAndExtractAsync(_settings.HotelCitiesResourceUrl, hotelStaging, cancellationToken);
                }

                var existingCities = await _dbContext.HotelCities
                    .ToDictionaryAsync(x => x.CityId > 0 ? x.CityId.ToString() : x.CityCode.Trim(), cancellationToken);

                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var toInsert = new List<HotelCity>();
                int updatedCount = 0;
                int readCount = 0;
                int failedCount = 0;

                await foreach (var row in _parser.ParseInsertRowsAsync(sqlFilePath, "hotel_cities", cancellationToken))
                {
                    readCount++;

                    row.TryGetValue("cityid", out var rawCityId);
                    row.TryGetValue("cityname", out var cityName);
                    row.TryGetValue("districtname", out var districtName);
                    row.TryGetValue("statename", out var stateName);
                    row.TryGetValue("countrycode", out var countryCode);
                    row.TryGetValue("fullname", out var fullName);
                    row.TryGetValue("type", out var type);
                    row.TryGetValue("hotelcount", out var rawHotelCount);

                    rawCityId = rawCityId?.Trim();
                    cityName = cityName?.Trim();

                    if (string.IsNullOrWhiteSpace(rawCityId) || string.IsNullOrWhiteSpace(cityName) || !long.TryParse(rawCityId, out var cityId))
                    {
                        failedCount++;
                        continue;
                    }

                    int.TryParse(rawHotelCount, out var hotelCount);
                    if (hotelCount <= 0) hotelCount = 1;

                    if (!seenKeys.Add(rawCityId)) continue;

                    if (existingCities.TryGetValue(rawCityId, out var existing))
                    {
                        bool modified = false;
                        if (existing.CityId != cityId) { existing.CityId = cityId; modified = true; }
                        if (existing.CityName != cityName) { existing.CityName = cityName; modified = true; }
                        if (existing.DistrictName != districtName) { existing.DistrictName = districtName; modified = true; }
                        if (existing.StateName != stateName) { existing.StateName = stateName; modified = true; }
                        if (existing.CountryCode != countryCode) { existing.CountryCode = countryCode; modified = true; }
                        if (existing.FullName != fullName) { existing.FullName = fullName ?? cityName; modified = true; }
                        if (existing.Type != (type ?? "CITY")) { existing.Type = type ?? "CITY"; modified = true; }
                        if (existing.HotelCount != hotelCount) { existing.HotelCount = hotelCount; modified = true; }
                        if (!existing.IsActive) { existing.IsActive = true; modified = true; }

                        if (modified)
                        {
                            existing.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(new HotelCity
                        {
                            CityId = cityId,
                            CityCode = rawCityId,
                            CityName = cityName,
                            DistrictName = districtName,
                            StateName = stateName,
                            CountryCode = countryCode,
                            FullName = fullName ?? cityName,
                            Type = type ?? "CITY",
                            HotelCount = hotelCount,
                            RequestType = "V8",
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        if (toInsert.Count >= BatchSize)
                        {
                            await _dbContext.HotelCities.AddRangeAsync(toInsert, cancellationToken);
                            await _dbContext.SaveChangesAsync(cancellationToken);
                            toInsert.Clear();
                        }
                    }
                }

                if (toInsert.Count > 0)
                {
                    await _dbContext.HotelCities.AddRangeAsync(toInsert, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    toInsert.Clear();
                }

                // Soft-deactivate missing records (if any existing ones are obsolete)
                int deactivatedCount = 0;
                foreach (var kvp in existingCities)
                {
                    if (!seenKeys.Contains(kvp.Key) && kvp.Value.IsActive)
                    {
                        kvp.Value.IsActive = false;
                        kvp.Value.UpdatedAt = DateTime.UtcNow;
                        deactivatedCount++;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                result.Success = true;
                result.RecordsRead = readCount;
                result.RecordsInserted = seenKeys.Count - (existingCities.Count - deactivatedCount);
                result.RecordsUpdated = updatedCount;
                result.RecordsDeactivated = deactivatedCount;
                result.RecordsFailed = failedCount;

                _logger.LogInformation("Hotel Cities import succeeded. Read: {Read}, Inserted: {Ins}, Updated: {Upd}, Deactivated: {Deact}, Failed: {Fail}",
                    readCount, result.RecordsInserted, updatedCount, deactivatedCount, failedCount);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Hotel Cities import failed! Transaction rolled back.");
            }
            finally
            {
                await _downloader.CleanupStagingAsync(hotelStaging);
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
            }

            return result;
        }

        public async Task<MasterDataImportResultDto> ImportAirportsAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var result = new MasterDataImportResultDto { EntityType = "Airports" };
            const string stagingDir = "flight_airport";

            _logger.LogInformation("Starting Flight Airports import from SRDV...");

            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var sqlFile = await _downloader.DownloadAndExtractAsync(_settings.FlightAirportResourceUrl, stagingDir, cancellationToken);

                var existingAirports = await _dbContext.FlightAirports
                    .ToDictionaryAsync(x => x.AirportCode.Trim(), StringComparer.OrdinalIgnoreCase, cancellationToken);

                var seenIataCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var toInsert = new List<PickNBook.Api.Models.Entities.FlightAirport>();
                int updatedCount = 0;
                int readCount = 0;
                int failedCount = 0;

                await foreach (var row in _parser.ParseInsertRowsAsync(sqlFile, "airport_list", cancellationToken))
                {
                    readCount++;

                    row.TryGetValue("airport_code", out var iataCode);
                    row.TryGetValue("airport_name", out var airportName);
                    row.TryGetValue("airport_city_code", out var cityCode);
                    row.TryGetValue("airport_city_name", out var cityName);
                    row.TryGetValue("airport_country_code", out var countryCode);
                    row.TryGetValue("airport_country_name", out var countryName);

                    iataCode = iataCode?.Trim().ToUpperInvariant();
                    airportName = airportName?.Trim();
                    cityName = cityName?.Trim();

                    if (string.IsNullOrWhiteSpace(iataCode) || string.IsNullOrWhiteSpace(airportName))
                    {
                        failedCount++;
                        continue;
                    }

                    if (!seenIataCodes.Add(iataCode)) continue;

                    if (existingAirports.TryGetValue(iataCode, out var existing))
                    {
                        if (existing.AirportName != airportName || existing.CityName != cityName)
                        {
                            existing.AirportName = airportName;
                            existing.CityName = cityName ?? airportName;
                            existing.CountryCode = countryCode?.Trim() ?? "";
                            existing.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(new PickNBook.Api.Models.Entities.FlightAirport
                        {
                            AirportCode = iataCode,
                            AirportName = airportName,
                            CityName = cityName ?? airportName,
                            CountryCode = countryCode?.Trim() ?? "",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        if (toInsert.Count >= BatchSize)
                        {
                            await _dbContext.FlightAirports.AddRangeAsync(toInsert, cancellationToken);
                            await _dbContext.SaveChangesAsync(cancellationToken);
                            toInsert.Clear();
                        }
                    }
                }

                if (toInsert.Count > 0)
                {
                    await _dbContext.FlightAirports.AddRangeAsync(toInsert, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                // Soft-deactivate missing
                int deactivatedCount = 0;
                foreach (var kvp in existingAirports)
                {
                    if (!seenIataCodes.Contains(kvp.Key) && kvp.Value.IsActive)
                    {
                        kvp.Value.IsActive = false;
                        kvp.Value.UpdatedAt = DateTime.UtcNow;
                        deactivatedCount++;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                result.Success = true;
                result.RecordsRead = readCount;
                result.RecordsInserted = seenIataCodes.Count - (existingAirports.Count - deactivatedCount);
                result.RecordsUpdated = updatedCount;
                result.RecordsDeactivated = deactivatedCount;
                result.RecordsFailed = failedCount;

                _logger.LogInformation("Airports import succeeded. Read: {Read}, Inserted: {Ins}, Updated: {Upd}, Deactivated: {Deact}, Failed: {Fail}",
                    readCount, result.RecordsInserted, updatedCount, deactivatedCount, failedCount);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Airports import failed! Transaction rolled back.");
            }
            finally
            {
                await _downloader.CleanupStagingAsync(stagingDir);
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
            }

            return result;
        }

        public async Task<MasterDataImportResultDto> ImportAirlinesAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var result = new MasterDataImportResultDto { EntityType = "Airlines" };
            const string stagingDir = "flight_airline";

            _logger.LogInformation("Starting Flight Airlines import from SRDV...");

            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var sqlFile = await _downloader.DownloadAndExtractAsync(_settings.FlightAirlineResourceUrl, stagingDir, cancellationToken);

                var existingFlightAirlines = await _dbContext.FlightAirlines
                    .ToDictionaryAsync(x => x.AirlineCode.Trim(), StringComparer.OrdinalIgnoreCase, cancellationToken);

                var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var toInsert = new List<PickNBook.Api.Models.Entities.FlightAirline>();
                int updatedCount = 0;
                int readCount = 0;
                int failedCount = 0;

                await foreach (var row in _parser.ParseInsertRowsAsync(sqlFile, "airline_list", cancellationToken))
                {
                    readCount++;

                    row.TryGetValue("airline_code", out var code);
                    row.TryGetValue("airline_name", out var name);

                    code = code?.Trim().ToUpperInvariant();
                    name = name?.Trim();

                    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                    {
                        failedCount++;
                        continue;
                    }

                    if (!seenCodes.Add(code)) continue;

                    if (existingFlightAirlines.TryGetValue(code, out var existing))
                    {
                        if (existing.AirlineName != name)
                        {
                            existing.AirlineName = name;
                            existing.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(new PickNBook.Api.Models.Entities.FlightAirline
                        {
                            AirlineCode = code,
                            AirlineName = name,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        if (toInsert.Count >= BatchSize)
                        {
                            await _dbContext.FlightAirlines.AddRangeAsync(toInsert, cancellationToken);
                            await _dbContext.SaveChangesAsync(cancellationToken);
                            toInsert.Clear();
                        }
                    }
                }

                if (toInsert.Count > 0)
                {
                    await _dbContext.FlightAirlines.AddRangeAsync(toInsert, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // Invalidate in-memory airline cache
                _cache.Remove("cache:flight:airlines:map");
                _cache.Remove("cache:flight:airlines:list");

                result.Success = true;
                result.RecordsRead = readCount;
                result.RecordsInserted = toInsert.Count;
                result.RecordsUpdated = updatedCount;
                result.RecordsFailed = failedCount;

                _logger.LogInformation("Airlines import succeeded. Read: {Read}, Inserted: {Ins}, Updated: {Upd}, Failed: {Fail}",
                    readCount, toInsert.Count, updatedCount, failedCount);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Airlines import failed! Transaction rolled back.");
            }
            finally
            {
                await _downloader.CleanupStagingAsync(stagingDir);
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
            }

            return result;
        }

        public async Task<List<MasterDataImportResultDto>> ImportAllAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<MasterDataImportResultDto>
            {
                await ImportBusCitiesAsync(cancellationToken),
                await ImportHotelCitiesAsync(cancellationToken),
                await ImportAirportsAsync(cancellationToken),
                await ImportAirlinesAsync(cancellationToken)
            };

            return results;
        }

        public async Task<Dictionary<string, int>> GetMasterDataStatusAsync(CancellationToken cancellationToken = default)
        {
            var status = new Dictionary<string, int>
            {
                ["BusCitiesTotal"] = await _dbContext.BusCities.CountAsync(cancellationToken),
                ["BusCitiesActive"] = await _dbContext.BusCities.CountAsync(x => x.IsActive, cancellationToken),
                ["HotelCitiesTotal"] = await _dbContext.HotelCities.CountAsync(cancellationToken),
                ["HotelCitiesSpecialActive"] = await _dbContext.HotelCities.CountAsync(x => x.RequestType == "Special" && x.IsActive, cancellationToken),
                ["HotelCitiesIntlActive"] = await _dbContext.HotelCities.CountAsync(x => x.RequestType == "International" && x.IsActive, cancellationToken),
                ["AirportsTotal"] = await _dbContext.FlightAirports.CountAsync(cancellationToken),
                ["AirportsActive"] = await _dbContext.FlightAirports.CountAsync(cancellationToken),
                ["AirlinesTotal"] = await _dbContext.FlightAirlines.CountAsync(cancellationToken),
                ["AirlinesActive"] = await _dbContext.FlightAirlines.CountAsync(cancellationToken)
            };

            return status;
        }
    }
}
