using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Services.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Background
{
    public class EmailReminderHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EmailReminderHostedService> _logger;

        public EmailReminderHostedService(IServiceProvider serviceProvider, ILogger<EmailReminderHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Email Reminder Hosted Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRemindersAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing Email Reminder Hosted Service.");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("Email Reminder Hosted Service is stopping.");
        }

        private async Task ProcessRemindersAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var templateService = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();

            var now = DateTime.UtcNow;

            var pendingReminders = await context.EmailReminders
                .Where(r => r.Status == "Pending" && r.ScheduledTime <= now)
                .ToListAsync();

            if (!pendingReminders.Any()) return;

            foreach (var reminder in pendingReminders)
            {
                try
                {
                    if (reminder.TemplateId.HasValue)
                    {
                        await templateService.SendManualEmailAsync(
                            reminder.RecipientEmail,
                            reminder.TemplateId.Value,
                            reminder.Subject,
                            reminder.Message,
                            reminder.IncludeLoginLink);
                    }
                    else if (!string.IsNullOrEmpty(reminder.Subject) && !string.IsNullOrEmpty(reminder.Message))
                    {
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        await emailService.SendEmailAsync(reminder.RecipientEmail, reminder.Subject, reminder.Message);
                    }

                    reminder.Status = "Sent";
                    reminder.SentAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to send reminder {reminder.Id}: {ex.Message}");
                    reminder.Status = "Failed";
                }
            }

            await context.SaveChangesAsync();
        }
    }
}
