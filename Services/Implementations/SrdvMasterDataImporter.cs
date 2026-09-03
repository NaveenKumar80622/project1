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
                            CityCode = cityCode,
                            CityName = cityName,
                            StateName = stateName,
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
            const string specialStaging = "hotel_special";
            const string intlStaging = "hotel_intl";

            _logger.LogInformation("Starting Hotel Cities import (Special & International) from SRDV...");

            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var existingCities = await _dbContext.HotelCities
                    .ToDictionaryAsync(x => $"{x.RequestType}:{x.CityCode.Trim()}", StringComparer.OrdinalIgnoreCase, cancellationToken);

                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var toInsert = new List<HotelCity>();
                int updatedCount = 0;
                int readCount = 0;
                int failedCount = 0;

                // 1. Process Special Hotel Cities
                var specialSqlFile = await _downloader.DownloadAndExtractAsync(_settings.HotelSpecialResourceUrl, specialStaging, cancellationToken);
                await foreach (var row in _parser.ParseInsertRowsAsync(specialSqlFile, "hotel_city_code_special", cancellationToken))
                {
                    readCount++;

                    row.TryGetValue("cityid", out var cityCode);
                    row.TryGetValue("destination", out var cityName);
                    row.TryGetValue("country", out var country);
                    row.TryGetValue("countrycode", out var countryCode);

                    cityCode = cityCode?.Trim();
                    cityName = cityName?.Trim();

                    if (string.IsNullOrWhiteSpace(cityCode) || string.IsNullOrWhiteSpace(cityName))
                    {
                        failedCount++;
                        continue;
                    }

                    var key = $"Special:{cityCode}";
                    if (!seenKeys.Add(key)) continue;

                    if (existingCities.TryGetValue(key, out var existing))
                    {
                        if (existing.CityName != cityName || existing.CountryName != country || !existing.IsActive)
                        {
                            existing.CityName = cityName;
                            existing.CountryName = country;
                            existing.CountryCode = countryCode;
                            existing.IsActive = true;
                            existing.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(new HotelCity
                        {
                            CityCode = cityCode,
                            CityName = cityName,
                            CountryName = country,
                            CountryCode = countryCode,
                            RequestType = "Special",
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

                // 2. Process International Hotel Cities
                var intlSqlFile = await _downloader.DownloadAndExtractAsync(_settings.HotelInternationalResourceUrl, intlStaging, cancellationToken);
                await foreach (var row in _parser.ParseInsertRowsAsync(intlSqlFile, "hotel_city_code", cancellationToken))
                {
                    readCount++;

                    row.TryGetValue("cityid", out var cityCode);
                    row.TryGetValue("destination", out var cityName);
                    row.TryGetValue("country", out var country);
                    row.TryGetValue("countrycode", out var countryCode);

                    cityCode = cityCode?.Trim();
                    cityName = cityName?.Trim();

                    if (string.IsNullOrWhiteSpace(cityCode) || string.IsNullOrWhiteSpace(cityName))
                    {
                        failedCount++;
                        continue;
                    }

                    var key = $"International:{cityCode}";
                    if (!seenKeys.Add(key)) continue;

                    if (existingCities.TryGetValue(key, out var existing))
                    {
                        if (existing.CityName != cityName || existing.CountryName != country || !existing.IsActive)
                        {
                            existing.CityName = cityName;
                            existing.CountryName = country;
                            existing.CountryCode = countryCode;
                            existing.IsActive = true;
                            existing.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(new HotelCity
                        {
                            CityCode = cityCode,
                            CityName = cityName,
                            CountryName = country,
                            CountryCode = countryCode,
                            RequestType = "International",
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
                }

                // Soft-deactivate missing records
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
                await _downloader.CleanupStagingAsync(specialStaging);
                await _downloader.CleanupStagingAsync(intlStaging);
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

                var existingAirports = await _dbContext.Airports
                    .ToDictionaryAsync(x => x.IataCode.Trim(), StringComparer.OrdinalIgnoreCase, cancellationToken);

                var seenIataCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var toInsert = new List<Airport>();
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
                    row.TryGetValue("airport_lat", out var latStr);
                    row.TryGetValue("airport_lon", out var lonStr);

                    iataCode = iataCode?.Trim().ToUpperInvariant();
                    airportName = airportName?.Trim();
                    cityName = cityName?.Trim();

                    if (string.IsNullOrWhiteSpace(iataCode) || string.IsNullOrWhiteSpace(airportName))
                    {
                        failedCount++;
                        continue;
                    }

                    if (!seenIataCodes.Add(iataCode)) continue;

                    decimal? lat = decimal.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedLat) ? parsedLat : null;
                    decimal? lon = decimal.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedLon) ? parsedLon : null;

                    if (existingAirports.TryGetValue(iataCode, out var existing))
                    {
                        if (existing.AirportName != airportName || existing.CityName != cityName || !existing.IsActive)
                        {
                            existing.AirportName = airportName;
                            existing.CityCode = cityCode?.Trim();
                            existing.CityName = cityName ?? string.Empty;
                            existing.CountryCode = countryCode?.Trim();
                            existing.CountryName = countryName?.Trim();
                            existing.Latitude = lat;
                            existing.Longitude = lon;
                            existing.IsActive = true;
                            existing.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(new Airport
                        {
                            IataCode = iataCode,
                            AirportName = airportName,
                            CityCode = cityCode?.Trim(),
                            CityName = cityName ?? string.Empty,
                            CountryCode = countryCode?.Trim(),
                            CountryName = countryName?.Trim(),
                            Latitude = lat,
                            Longitude = lon,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        if (toInsert.Count >= BatchSize)
                        {
                            await _dbContext.Airports.AddRangeAsync(toInsert, cancellationToken);
                            await _dbContext.SaveChangesAsync(cancellationToken);
                            toInsert.Clear();
                        }
                    }
                }

                if (toInsert.Count > 0)
                {
                    await _dbContext.Airports.AddRangeAsync(toInsert, cancellationToken);
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

                var existingAirlines = await _dbContext.Airlines
                    .ToDictionaryAsync(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase, cancellationToken);

                var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var toInsert = new List<Airline>();
                int updatedCount = 0;
                int readCount = 0;
                int failedCount = 0;

                await foreach (var row in _parser.ParseInsertRowsAsync(sqlFile, "airline_list", cancellationToken))
                {
                    readCount++;

                    row.TryGetValue("airline_code", out var code);
                    row.TryGetValue("airline_name", out var name);
                    row.TryGetValue("Active", out var activeStr);

                    code = code?.Trim().ToUpperInvariant();
                    name = name?.Trim();

                    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                    {
                        failedCount++;
                        continue;
                    }

                    if (!seenCodes.Add(code)) continue;

                    var status = string.Equals(activeStr, "false", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";

                    if (existingAirlines.TryGetValue(code, out var existing))
                    {
                        if (existing.Name != name || existing.Status != status)
                        {
                            existing.Name = name;
                            existing.Status = status;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(new Airline
                        {
                            Code = code,
                            Name = name,
                            Status = status
                        });

                        if (toInsert.Count >= BatchSize)
                        {
                            await _dbContext.Airlines.AddRangeAsync(toInsert, cancellationToken);
                            await _dbContext.SaveChangesAsync(cancellationToken);
                            toInsert.Clear();
                        }
                    }
                }

                if (toInsert.Count > 0)
                {
                    await _dbContext.Airlines.AddRangeAsync(toInsert, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

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
                ["AirportsTotal"] = await _dbContext.Airports.CountAsync(cancellationToken),
                ["AirportsActive"] = await _dbContext.Airports.CountAsync(x => x.IsActive, cancellationToken),
                ["AirlinesTotal"] = await _dbContext.Airlines.CountAsync(cancellationToken),
                ["AirlinesActive"] = await _dbContext.Airlines.CountAsync(x => x.Status == "Active", cancellationToken)
            };

            return status;
        }
    }
}
