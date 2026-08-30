using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;

namespace PickNBook.Api.Services
{
    public class SecurityBackgroundService : BackgroundService
    {
        private readonly ILogger<SecurityBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public SecurityBackgroundService(ILogger<SecurityBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Security Background Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSecurityJobsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing Security Background Jobs.");
                }

                // Run every 1 minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task ProcessSecurityJobsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var securityService = scope.ServiceProvider.GetRequiredService<ISecurityService>();

            var now = DateTime.UtcNow;
            
            // Job 1: Auto-Expire Temporary IP Blocks
            var expiredIpRules = await dbContext.SecurityIpRules
                .Where(r => r.BlockType == "TEMPORARY" && r.Status != "EXPIRED" && r.ExpiryTime <= now)
                .ToListAsync();

            foreach (var rule in expiredIpRules)
            {
                rule.Status = "EXPIRED";
                rule.UpdatedAt = now;
                await securityService.LogAuditAsync("IP_BLOCK_EXPIRED", "Auto-expire IP Block", "Success", rule.IpAddress, reason: "Block duration elapsed");
            }

            // Job 2: Auto-Expire Account Locks
            var expiredLocks = await dbContext.SecurityAccountLocks
                .Where(l => l.LockStatus == "TEMPORARILY_LOCKED" && l.ExpiresAt <= now)
                .ToListAsync();

            foreach (var lck in expiredLocks)
            {
                lck.LockStatus = "UNLOCKED";
                lck.UpdatedAt = now;
                lck.UnlockedAt = now;
                lck.UnlockedBy = "System";
                await securityService.LogAuditAsync("ACCOUNT_LOCK_EXPIRED", "Auto-expire Account Lock", "Success", "", userId: lck.AccountId.ToString(), reason: "Lock duration elapsed");
            }

            // Job 4: Daily Counter Reset (Assuming we reset at midnight UTC)
            // Or we just rely on `ResetAt` or time-based sliding windows.
            // Since limits have `TimePeriodValue`, a true sliding window is complex. 
            // We'll reset any counter that hasn't been seen in 24 hours to clean up DB space.
            var staleCounters = await dbContext.SecurityCounters
                .Where(c => c.CurrentCount > 0 && c.LastAttemptAt < now.AddDays(-1))
                .ToListAsync();

            foreach (var counter in staleCounters)
            {
                counter.CurrentCount = 0;
                counter.PeriodEnd = now;
            }

            // Job 3: Email Reminder Sender
            var dueReminders = await dbContext.EmailReminders
                .Where(r => r.Status == "PENDING" && r.ScheduledTime <= now)
                .ToListAsync();

            foreach (var reminder in dueReminders)
            {
                // Simple logic: send email notification and update status
                await securityService.QueueNotificationAsync("REMINDER", "USER", reminder.RecipientEmail, reminder.ReminderName, cooldownKey: $"REMINDER_{reminder.Id}");
                reminder.Status = "SENT";
                reminder.UpdatedAt = now;
            }

            // Job 5: Email Retry
            var failedEmails = await dbContext.EmailHistory
                .Where(h => h.DeliveryStatus == "FAILED" && h.RetryCount < 3)
                .ToListAsync();

            foreach (var email in failedEmails)
            {
                // In reality, this would try to resend using an email service
                // For now we just log it and increment retry count
                email.RetryCount++;
                if (email.RetryCount >= 3)
                {
                    email.DeliveryStatus = "CANCELLED";
                }
                else
                {
                    email.DeliveryStatus = "PENDING";
                }
            }

            if (expiredIpRules.Any() || expiredLocks.Any() || staleCounters.Any() || dueReminders.Any() || failedEmails.Any())
            {
                await dbContext.SaveChangesAsync();
                _logger.LogInformation($"SecurityBackgroundService: Expired {expiredIpRules.Count} IP rules, {expiredLocks.Count} account locks, reset {staleCounters.Count} counters, sent {dueReminders.Count} reminders, retried {failedEmails.Count} emails.");
            }
        }
    }
}
