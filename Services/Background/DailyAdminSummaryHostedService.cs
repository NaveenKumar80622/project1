using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Services.Implementations;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Background
{
    public class DailyAdminSummaryHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DailyAdminSummaryHostedService> _logger;

        public DailyAdminSummaryHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<DailyAdminSummaryHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DailyAdminSummaryHostedService started. Target schedule: 23:59 IST daily.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var istZone = DailyAdminSummaryService.GetIstTimeZoneStatic();
                var nowUtc = DateTime.UtcNow;
                var nowIst = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, istZone);

                // Target time today at 23:59:00 IST
                var scheduledTodayIst = nowIst.Date.AddHours(23).AddMinutes(59);

                DateTime nextRunIst;
                DateOnly targetBusinessDate;

                if (nowIst < scheduledTodayIst)
                {
                    nextRunIst = scheduledTodayIst;
                    targetBusinessDate = DateOnly.FromDateTime(nowIst);
                }
                else
                {
                    // Already past 23:59 IST today; schedule for tomorrow at 23:59 IST
                    nextRunIst = scheduledTodayIst.AddDays(1);
                    targetBusinessDate = DateOnly.FromDateTime(nextRunIst);
                }

                var nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(nextRunIst, istZone);
                var delay = nextRunUtc - nowUtc;
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                _logger.LogInformation("DailyAdminSummaryHostedService scheduled next run for {NextRunIst} IST (UTC: {NextRunUtc}, Delay: {Delay}) targeting business date {BusinessDate}",
                    nextRunIst, nextRunUtc, delay, targetBusinessDate);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    // Create a fresh DI scope per execution — never keep AppDbContext alive across iterations
                    using var scope = _scopeFactory.CreateScope();
                    var summaryService = scope.ServiceProvider.GetRequiredService<IDailyAdminSummaryService>();
                    await summaryService.GenerateAndSendDailySummaryAsync(targetBusinessDate, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Safety: One failed execution does not stop future daily runs
                    _logger.LogError(ex, "Unexpected error executing daily admin summary for {BusinessDate}. Service will proceed to next day's schedule.", targetBusinessDate);
                }

                // Advance slightly past 23:59 to prevent immediate re-trigger within the same minute
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(65), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("DailyAdminSummaryHostedService stopped.");
        }
    }
}
