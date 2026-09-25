using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Services.Notifications.Interfaces;
using System.Text.Json;

namespace PickNBook.Api.Services.Background
{
    public class NotificationOutboxWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NotificationOutboxWorker> _logger;

        public NotificationOutboxWorker(IServiceProvider serviceProvider, ILogger<NotificationOutboxWorker> logger)
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
                    await ProcessOutboxAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing notification outbox");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private async Task ProcessOutboxAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var pendingNotifications = await dbContext.NotificationOutbox
                .Where(o => (o.Status == "Pending" || o.Status == "Failed") 
                            && (o.NextRetryAt == null || o.NextRetryAt <= DateTime.UtcNow)
                            && o.RetryCount < 3)
                .OrderBy(o => o.CreatedAt)
                .Take(50)
                .ToListAsync(stoppingToken);

            foreach (var outbox in pendingNotifications)
            {
                outbox.Status = "Processing";
            }
            await dbContext.SaveChangesAsync(stoppingToken);

            foreach (var outbox in pendingNotifications)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<object>(outbox.PayloadJson) ?? new object();
                    var (success, errorMessage) = await notificationService.SendImmediateAsync(
                        outbox.EventType,
                        outbox.Channel,
                        outbox.Recipient,
                        outbox.TemplateKey,
                        payload
                    );

                    if (success)
                    {
                        outbox.Status = "Processed";
                        outbox.ProcessedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        outbox.Status = "Failed";
                        outbox.LastError = errorMessage ?? "Unknown provider error";
                        outbox.RetryCount++;
                        outbox.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, outbox.RetryCount));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to process outbox record {outbox.Id}");
                    outbox.Status = "Failed";
                    outbox.LastError = ex.Message;
                    outbox.RetryCount++;
                    outbox.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, outbox.RetryCount));
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
