using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Services.Interfaces;
using PickNBook.Api.Services.Notifications.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class SrdvWalletAlertService : ISrdvWalletAlertService
    {
        private const string CooldownCacheKey = "srdv_wallet_last_alert_time";
        private readonly SrdvWalletMonitoringSettings _settings;
        private readonly AppDbContext _dbContext;
        private readonly IInAppNotificationService _inAppNotificationService;
        private readonly INotificationService _notificationService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SrdvWalletAlertService> _logger;

        public SrdvWalletAlertService(
            IOptions<SrdvWalletMonitoringSettings> settings,
            AppDbContext dbContext,
            IInAppNotificationService inAppNotificationService,
            INotificationService notificationService,
            IMemoryCache cache,
            ILogger<SrdvWalletAlertService> logger)
        {
            _settings = settings.Value;
            _dbContext = dbContext;
            _inAppNotificationService = inAppNotificationService;
            _notificationService = notificationService;
            _cache = cache;
            _logger = logger;
        }

        public async Task EvaluateAndAlertAsync(decimal availableBalance, string serviceType = "Bus", CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
            {
                return;
            }

            if (availableBalance >= _settings.LowBalanceThreshold)
            {
                return;
            }

            var normalizedService = string.IsNullOrWhiteSpace(serviceType) ? "Bus" : serviceType.Trim();
            var cooldownKey = $"{CooldownCacheKey}_{normalizedService.ToLowerInvariant()}";

            // Check Cooldown to prevent spamming notifications
            if (_cache.TryGetValue(cooldownKey, out DateTime lastAlertTime))
            {
                var elapsedMinutes = (DateTime.UtcNow - lastAlertTime).TotalMinutes;
                if (elapsedMinutes < _settings.AlertCooldownMinutes)
                {
                    _logger.LogInformation(
                        "SRDV {Service} Wallet balance is low ({Balance} < {Threshold}), but alert cooldown is active (Last alerted: {LastAlert:u}, Elapsed: {Elapsed:F1}m, Cooldown: {Cooldown}m). Suppressing duplicate alert.",
                        normalizedService, availableBalance, _settings.LowBalanceThreshold, lastAlertTime, elapsedMinutes, _settings.AlertCooldownMinutes);
                    return;
                }
            }

            _logger.LogCritical(
                "CRITICAL: SRDV {Service} Wallet balance ({Balance:N2}) is below threshold ({Threshold:N2})! Initiating admin alerts.",
                normalizedService, availableBalance, _settings.LowBalanceThreshold);

            // Record alert timestamp in cache with TTL
            _cache.Set(cooldownKey, DateTime.UtcNow, TimeSpan.FromMinutes(_settings.AlertCooldownMinutes));

            // 1. Gather all admin recipients
            var adminEmails = new List<string>();
            try
            {
                var dbAdmins = await _dbContext.Users
                    .AsNoTracking()
                    .Where(u => u.Role == "Admin" || u.Role == "SuperAdmin")
                    .Select(u => u.Email)
                    .ToListAsync(cancellationToken);

                adminEmails.AddRange(dbAdmins);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query admin users from database for SRDV wallet alert.");
            }

            if (_settings.AdminAlertEmails != null)
            {
                adminEmails.AddRange(_settings.AdminAlertEmails);
            }

            var distinctEmails = adminEmails
                .Where(e => !string.IsNullOrWhiteSpace(e) && e.Contains('@'))
                .Select(e => e.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            var actionUrl = normalizedService.Equals("Hotel", StringComparison.OrdinalIgnoreCase) 
                ? "/admin/hotel/srdv-wallet/balance" 
                : (normalizedService.Equals("Flight", StringComparison.OrdinalIgnoreCase) 
                    ? "/admin/flight/srdv-wallet/balance" 
                    : "/admin/bus/srdv-wallet/balance");

            // 2. Dispatch In-App Notification to all Admins
            try
            {
                var idempotencyKey = $"SRDV_LOW_BAL_{normalizedService.ToUpperInvariant()}_{DateTime.UtcNow:yyyyMMddHH}";
                var inAppMsg = $"Critical: SRDV {normalizedService} Wallet available balance (₹{availableBalance:N2}) is below threshold of ₹{_settings.LowBalanceThreshold:N2}. Please recharge the wallet immediately to prevent booking interruptions.";

                await _inAppNotificationService.CreateNotificationAsync(
                    type: "SupplierWallet",
                    category: "Finance",
                    title: $"SRDV {normalizedService} Wallet Low Balance Alert",
                    message: inAppMsg,
                    severity: "Critical",
                    referenceType: "SupplierWallet",
                    referenceId: $"SRDV_{normalizedService.ToUpperInvariant()}",
                    actionUrl: actionUrl,
                    idempotencyKey: idempotencyKey,
                    targetRole: "Admin",
                    cancellationToken: cancellationToken
                );
                _logger.LogInformation("Dispatched SRDV {Service} wallet low balance in-app notification to Admin role.", normalizedService);
            }
            catch (Exception inAppEx)
            {
                _logger.LogError(inAppEx, "Failed to create in-app notification for SRDV {Service} low balance alert.", normalizedService);
            }

            // 3. Dispatch Email Notifications to all Admin recipients
            foreach (var email in distinctEmails)
            {
                try
                {
                    await _notificationService.EnqueueAsync(
                        eventType: "AdminSupplierAlert",
                        channel: "Email",
                        recipient: email,
                        templateKey: "ADMIN_SRDV_LOW_BALANCE",
                        payload: new
                        {
                            ServiceType = normalizedService,
                            Balance = availableBalance,
                            Threshold = _settings.LowBalanceThreshold,
                            Deficit = _settings.LowBalanceThreshold - availableBalance,
                            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                            ActionUrl = actionUrl
                        }
                    );
                    _logger.LogInformation("Enqueued SRDV {Service} low balance email alert for {Recipient}", normalizedService, email);
                }
                catch (Exception mailEx)
                {
                    _logger.LogError(mailEx, "Failed to enqueue SRDV {Service} low balance email alert for {Recipient}", normalizedService, email);
                }
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to persist queued email notifications for SRDV low balance alert.");
            }
        }
    }
}
