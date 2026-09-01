using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Notifications.Interfaces;
using System.Text.Json;

namespace PickNBook.Api.Services.Notifications.Implementations
{
    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;

        public NotificationService(AppDbContext dbContext, IServiceProvider serviceProvider)
        {
            _dbContext = dbContext;
            _serviceProvider = serviceProvider;
        }

        public Task EnqueueAsync(string eventType, string channel, string recipient, string templateKey, object payload, string? bookingId = null, string? userId = null)
        {
            var outbox = new NotificationOutbox
            {
                EventType = eventType,
                Channel = channel,
                Recipient = recipient,
                TemplateKey = templateKey,
                PayloadJson = JsonSerializer.Serialize(payload),
                BookingId = bookingId,
                UserId = userId,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            // This ensures the Outbox entity is tracked by the SAME DbContext used by the caller
            _dbContext.NotificationOutbox.Add(outbox);
            return Task.CompletedTask;
        }

        public async Task<bool> SendImmediateAsync(string eventType, string channel, string recipient, string templateKey, object payload)
        {
            var template = await _dbContext.NotificationTemplates
                .FirstOrDefaultAsync(t => t.TemplateKey == templateKey && t.Channel == channel && t.IsActive);

            // Fallback string if template is missing, for development continuity
            string content = template?.Body ?? $"[{templateKey}] payload: {JsonSerializer.Serialize(payload)}";
            string? subject = template?.Subject;

            if (template != null)
            {
                content = ReplaceVariables(template.Body, payload);
            }

            INotificationProvider? provider = channel switch
            {
                "SMS" => _serviceProvider.GetService<ISmsProvider>(),
                "WhatsApp" => _serviceProvider.GetService<IWhatsAppProvider>(),
                "Email" => _serviceProvider.GetService<IEmailProvider>(),
                _ => null
            };

            if (provider == null) return false;

            var result = await provider.SendAsync(recipient, content, subject);
            
            var log = new NotificationLog
            {
                OutboxId = 0,
                EventType = eventType,
                Channel = channel,
                Recipient = recipient,
                TemplateKey = templateKey,
                RenderedContent = content,
                Status = result.IsSuccess ? "Success" : "Failed",
                ProviderMessageId = result.ProviderMessageId,
                ErrorMessage = result.ErrorMessage,
                SentAt = DateTime.UtcNow
            };
            
            _dbContext.NotificationLogs.Add(log);
            await _dbContext.SaveChangesAsync();

            return result.IsSuccess;
        }

        private string ReplaceVariables(string templateBody, object payload)
        {
            try 
            {
                var json = JsonSerializer.Serialize(payload);
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (dict == null) return templateBody;

                var result = templateBody;
                foreach (var kvp in dict)
                {
                    result = result.Replace($"{{{kvp.Key}}}", kvp.Value.ToString());
                }
                return result;
            } 
            catch 
            {
                return templateBody;
            }
        }
    }
}
