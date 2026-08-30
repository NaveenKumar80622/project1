using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Background
{
    public class HotelCacheWarmerHostedService : BackgroundService
    {
        private readonly ILogger<HotelCacheWarmerHostedService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        // Top searched City IDs (e.g., Hyderabad, Delhi, Mumbai, Bangalore)
        private readonly List<string> _topCityIds = new() { "697288", "145330", "130443", "111124" };

        public HotelCacheWarmerHostedService(
            ILogger<HotelCacheWarmerHostedService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
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

                    foreach (var cityId in _topCityIds)
                    {
                        var request = new SrdvHotelSearchRequestDto
                        {
                            CityId = cityId,
                            CheckInDate = checkInDate.ToString("yyyy-MM-dd"),
                            CheckOutDate = checkOutDate.ToString("yyyy-MM-dd"),
                            NoOfRooms = "1",
                            RoomGuests = new List<RoomGuestDto>
                            {
                                new RoomGuestDto { NoOfAdults = "2", NoOfChild = "0", ChildAge = new List<int>() }
                            },
                            ResultCount = "50" // Fast load
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
    }
}
