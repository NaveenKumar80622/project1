using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Background
{
    public class SrdvBusWalletMonitorHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SrdvWalletMonitoringSettings _settings;
        private readonly ILogger<SrdvBusWalletMonitorHostedService> _logger;

        public SrdvBusWalletMonitorHostedService(
            IServiceScopeFactory scopeFactory,
            IOptions<SrdvWalletMonitoringSettings> settings,
            ILogger<SrdvBusWalletMonitorHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "SrdvBusWalletMonitorHostedService started. Check interval: {Interval}m, Threshold: ₹{Threshold:N2}, Cooldown: {Cooldown}m.",
                _settings.CheckIntervalMinutes, _settings.LowBalanceThreshold, _settings.AlertCooldownMinutes);

            // Stagger initial check by 15 seconds to allow application bootstrap
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_settings.Enabled)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var srdvBusService = scope.ServiceProvider.GetRequiredService<ISrdvBusService>();
                        var alertService = scope.ServiceProvider.GetRequiredService<ISrdvWalletAlertService>();

                        var balanceDto = await srdvBusService.GetSrdvMasterWalletBalanceAsync();
                        if (balanceDto != null && balanceDto.IsSuccess && balanceDto.AvailableBalance.HasValue)
                        {
                            await alertService.EvaluateAndAlertAsync(balanceDto.AvailableBalance.Value, stoppingToken);
                        }
                        else if (balanceDto != null && (!balanceDto.IsSuccess || !balanceDto.AvailableBalance.HasValue))
                        {
                            decimal available = balanceDto.AvailableBalance ?? 0m;
                            await alertService.EvaluateAndAlertAsync(available, stoppingToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred during periodic SRDV bus wallet monitoring check.");
                    }
                }

                var delayMinutes = Math.Max(1, _settings.CheckIntervalMinutes);
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("SrdvBusWalletMonitorHostedService stopped.");
        }
    }
}
