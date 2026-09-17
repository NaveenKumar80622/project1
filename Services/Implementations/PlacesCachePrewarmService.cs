using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class PlacesCachePrewarmService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PlacesCachePrewarmService> _logger;

        public PlacesCachePrewarmService(
            IServiceScopeFactory scopeFactory,
            ILogger<PlacesCachePrewarmService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Non-blocking wait 2 seconds after application startup to let other initialization tasks complete
                await Task.Delay(2000, stoppingToken);

                _logger.LogInformation("Starting Places 26-letter single-character cache pre-warming...");

                using var scope = _scopeFactory.CreateScope();
                var placesService = scope.ServiceProvider.GetRequiredService<IPlacesService>();

                int warmedCount = 0;

                for (char c = 'a'; c <= 'z'; c++)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    var letter = c.ToString();

                    // Pre-warm flight, bus, and hotel for this letter
                    await placesService.GetPlacesAsync(letter, "flight", "all", null, 20, stoppingToken);
                    await placesService.GetPlacesAsync(letter, "bus", "all", null, 20, stoppingToken);
                    await placesService.GetPlacesAsync(letter, "hotel", "all", null, 20, stoppingToken);

                    warmedCount += 3;
                }

                _logger.LogInformation($"Places single-character cache pre-warmed successfully ({warmedCount} entries cached).");
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to completely pre-warm Places single-character cache.");
            }
        }
    }
}
