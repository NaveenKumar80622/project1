using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PickNBook.Api.Services
{
    public class HotelCityCacheService : IHostedService
    {
        private readonly ILogger<HotelCityCacheService> _logger;

        public HashSet<string> SpecialCityIds { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> InternationalCityIds { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HotelCityCacheService(ILogger<HotelCityCacheService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Hotel City Cache: Unified SRDV v8 cities are managed via master database/SQL dump.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
