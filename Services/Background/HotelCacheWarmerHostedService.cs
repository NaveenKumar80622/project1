using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Background
{
    public class HotelCacheWarmerHostedService : BackgroundService
    {
        private readonly ILogger<HotelCacheWarmerHostedService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SrdvSettings _srdvSettings;

        // Top searched City IDs (Hyderabad, Mumbai, Delhi, Bangalore, Chennai, Jaipur, Kolkata, Goa Calangute)
        private readonly List<long> _topCityIds = new() { 2947514, 2948193, 2936442, 2948216, 2946990, 2947412, 2948092, 2920224 };
        private readonly List<string> _topCityNames = new() { "Hyderabad", "Mumbai", "New Delhi", "Bengaluru", "Chennai", "Jaipur", "Kolkata", "Goa" };

        public HotelCacheWarmerHostedService(
            ILogger<HotelCacheWarmerHostedService> logger,
            IServiceScopeFactory scopeFactory,
            IOptions<SrdvSettings> srdvSettings)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _srdvSettings = srdvSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("HotelCacheWarmerHostedService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting proactive Hotel Cache Warming for popular destinations.");

                    using var scope = _scopeFactory.CreateScope();
                    var hotelService = scope.ServiceProvider.GetRequiredService<IHotelService>();

                    // Determine upcoming weekend dates (Friday to Sunday)
                    var today = DateTime.UtcNow.Date;
                    var daysUntilFriday = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;
                    if (daysUntilFriday == 0) daysUntilFriday = 7; // If today is Friday, warm up next weekend

                    var checkInDate = today.AddDays(daysUntilFriday);
                    var checkOutDate = checkInDate.AddDays(2); // Sunday

                    var targetCityIds = await ResolveWarmupCityIdsAsync(hotelService, stoppingToken);

                    foreach (var cityId in targetCityIds)
                    {
                        var request = new SrdvHotelSearchRequestDto
                        {
                            CityId = cityId,
                            CheckInDate = checkInDate.ToString("yyyy-MM-dd"),
                            CheckOutDate = checkOutDate.ToString("yyyy-MM-dd"),
                            NoOfNights = 2,
                            NoOfRooms = "1",
                            RoomGuests = new List<RoomGuestDto>
                            {
                                new RoomGuestDto { NoOfAdults = 2, NoOfChild = 0, ChildAge = new List<int>() }
                            },
                            MinRating = "0",
                            MaxRating = "7"
                        };

                        _logger.LogInformation("Warming cache for CityId: {CityId} from {CheckIn} to {CheckOut}", cityId, request.CheckInDate, request.CheckOutDate);

                        // This will hit SrdvHotelService.SearchHotelsMultiLevelAsync which naturally caches the response in IMemoryCache
                        await hotelService.SearchHotelsMultiLevelAsync(request);

                        // Wait 10 seconds between API calls to avoid hitting rate limits
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }

                    _logger.LogInformation("Hotel Cache Warming completed successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while warming up the hotel cache.");
                }

                // Wait 10 minutes before the next cache warmup cycle since cache expires in 15 mins by default.
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task<List<long>> ResolveWarmupCityIdsAsync(IHotelService hotelService, CancellationToken stoppingToken)
        {
            var isAutoSuggestMode = _srdvSettings.EnableCityAutoSuggest ||
                string.Equals(_srdvSettings.HotelLocationMode, "AutoSuggest", StringComparison.OrdinalIgnoreCase);

            if (!isAutoSuggestMode)
            {
                return _topCityIds;
            }

            var resolvedIds = new List<long>();
            foreach (var cityName in _topCityNames)
            {
                if (stoppingToken.IsCancellationRequested) break;
                var suggest = await hotelService.CityAutoSuggestAsync(
                    new SrdvCityAutoSuggestRequestDto { Search = cityName, Limit = 5 },
                    forceProbe: false,
                    cancellationToken: stoppingToken);

                var match = suggest.Result.FirstOrDefault(r =>
                    string.Equals(r.Kind, "CITY", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(r.CityId, out var cid) && cid > 0);

                if (match != null && long.TryParse(match.CityId, out var parsedId))
                {
                    resolvedIds.Add(parsedId);
                }
            }

            return resolvedIds;
        }
    }
}
