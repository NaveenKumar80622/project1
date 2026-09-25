using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace PickNBook.Api.Services
{
    public class SecurityNotificationHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SecurityNotificationHostedService> _logger;

        public SecurityNotificationHostedService(IServiceProvider serviceProvider, ILogger<SecurityNotificationHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessNotificationsAsync(stoppingToken);
                    // Expire old IP rules
                    await ExpireIpBlacklistsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing security background tasks.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task ProcessNotificationsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var pendingNotifications = await db.SecurityNotifications
                .Where(n => n.Status == "PENDING")
                .OrderBy(n => n.CreatedAt)
                .Take(50)
                .ToListAsync(stoppingToken);

            foreach (var notification in pendingNotifications)
            {
                try
                {
                    // Basic placeholder for body generation since template logic is complex
                    // In real app, we'd fetch actual template from DB, replace vars, etc.
                    string body = $"<h1>Security Notice</h1><p>Event: {notification.EventType}</p>";
                    
                    await emailService.SendEmailAsync(notification.Recipient, notification.Subject, body);
                    
                    notification.Status = "SENT";
                    notification.SentAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    notification.Status = "FAILED";
                    notification.FailedAt = DateTime.UtcNow;
                    notification.ErrorMessage = ex.Message;
                    notification.RetryCount++;
                }
            }

            if (pendingNotifications.Any())
            {
                await db.SaveChangesAsync(stoppingToken);
            }
        }

        private async Task ExpireIpBlacklistsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var securityService = scope.ServiceProvider.GetRequiredService<ISecurityService>();

            var now = DateTime.UtcNow;
            var expiredRules = await db.IpAccessRules
                .Where(r => r.ListType == "BLACKLIST" && r.Status == "ACTIVE" && !r.IsPermanent && r.ExpiresAt.HasValue && r.ExpiresAt.Value <= now)
                .ToListAsync(stoppingToken);

            foreach (var rule in expiredRules)
            {
                rule.ListType = "WHITELIST";
                rule.Status = "ACTIVE";
                rule.Reason = "Auto-unblocked (Duration expired)";
                rule.ExpiresAt = null;
                rule.IsPermanent = true;
                
                // Add an audit log and queue a notification
                await securityService.LogAuditAsync("IP_AUTO_UNBLOCKED", "ExpireIpBlacklist", "SUCCESS", rule.IpAddress, reason: "Temporary block expired.");
                await securityService.QueueNotificationAsync("IP_AUTO_UNBLOCKED", "ADMIN", "admin@picknbook.com", $"Security Update: IP Block Expired for {rule.IpAddress}", ipAddress: rule.IpAddress);
            }

            if (expiredRules.Any())
            {
                await db.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
