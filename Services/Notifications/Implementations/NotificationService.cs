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
        private readonly PickNBook.Api.Models.Config.NotificationRoutingSettings _routingSettings;

        public NotificationService(
            AppDbContext dbContext, 
            IServiceProvider serviceProvider, 
            Microsoft.Extensions.Options.IOptions<PickNBook.Api.Models.Config.NotificationRoutingSettings> routingOptions)
        {
            _dbContext = dbContext;
            _serviceProvider = serviceProvider;
            _routingSettings = routingOptions.Value;
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

        public async Task<(bool IsSuccess, string? ErrorMessage)> SendImmediateAsync(string eventType, string channel, string recipient, string templateKey, object payload)
        {
            var template = await _dbContext.NotificationTemplates
                .FirstOrDefaultAsync(t => t.TemplateKey == templateKey && t.Channel == channel && t.IsActive);

            if (template == null)
            {
                var logger = _serviceProvider.GetService<ILogger<NotificationService>>();
                logger?.LogWarning("Notification template '{TemplateKey}' for channel '{Channel}' not found or inactive. Notification dispatch aborted.", templateKey, channel);
                return (false, $"Active notification template '{templateKey}' for channel '{channel}' not found.");
            }

            string content = ReplaceVariables(template.Body, payload);
            string? subject = !string.IsNullOrEmpty(template.Subject) ? ReplaceVariables(template.Subject, payload) : null;

            INotificationProvider? provider = null;
            if (channel == "SMS")
            {
                var providers = _serviceProvider.GetServices<ISmsProvider>();
                if (_routingSettings.SmsProviderRoutes.TryGetValue(eventType, out var providerName))
                {
                    provider = providers.FirstOrDefault(p => p.ProviderName == providerName);
                    if (provider == null)
                    {
                        throw new InvalidOperationException($"Configured SMS provider '{providerName}' not found for event '{eventType}'.");
                    }
                }
                else
                {
                    provider = providers.FirstOrDefault(p => p.ProviderName == "Mock");
                }
            }
            else if (channel == "WhatsApp")
            {
                provider = _serviceProvider.GetService<IWhatsAppProvider>();
            }
            else if (channel == "Email")
            {
                provider = _serviceProvider.GetService<IEmailProvider>();
            }

            if (provider == null) return (false, "No provider found");

            var result = provider is ISmsProvider smsProvider
                ? await smsProvider.SendSmsAsync(recipient, content, template?.ProviderTemplateId, template?.Subject)
                : await provider.SendAsync(recipient, content, subject);
            
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

            return (result.IsSuccess, result.ErrorMessage);
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
                    result = result.Replace($"{{{kvp.Key}}}", kvp.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                }

                // Common interchangeable aliases
                if (dict.TryGetValue("Pnr", out var pnrVal))
                {
                    result = result.Replace("{Reference}", pnrVal.ToString(), StringComparison.OrdinalIgnoreCase);
                }
                else if (dict.TryGetValue("Reference", out var refVal))
                {
                    result = result.Replace("{Pnr}", refVal.ToString(), StringComparison.OrdinalIgnoreCase);
                }

                // Also handle DLT provider-style ${varN} placeholders
                foreach (var kvp in dict)
                {
                    if (kvp.Key.StartsWith("Var", StringComparison.OrdinalIgnoreCase) &&
                        kvp.Key.Length > 3 &&
                        int.TryParse(kvp.Key.AsSpan(3), out _))
                    {
                        string placeholder = "${" + kvp.Key.ToLowerInvariant() + "}"; // e.g. ${var1}
                        result = result.Replace(placeholder, kvp.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                    }
                }

                // Fallback: If template still contains ${varN} placeholders, map remaining non-Var payload entries sequentially
                if (result.Contains("${var", StringComparison.OrdinalIgnoreCase))
                {
                    int varIdx = 1;
                    foreach (var kvp in dict)
                    {
                        if (!kvp.Key.StartsWith("Var", StringComparison.OrdinalIgnoreCase))
                        {
                            string placeholder = $"${{var{varIdx}}}";
                            if (result.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                            {
                                result = result.Replace(placeholder, kvp.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                            }
                            varIdx++;
                        }
                    }
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
