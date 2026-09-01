using Microsoft.Extensions.Logging;
using PickNBook.Api.Services.Notifications.Interfaces;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Notifications.Providers
{
    public class MockWhatsAppProvider : IWhatsAppProvider
    {
        private readonly ILogger<MockWhatsAppProvider> _logger;

        public MockWhatsAppProvider(ILogger<MockWhatsAppProvider> logger)
        {
            _logger = logger;
        }

        public Task<(bool IsSuccess, string? ProviderMessageId, string? ErrorMessage)> SendAsync(string recipient, string content, string? subject = null)
        {
            _logger.LogInformation("--- MOCK WHATSAPP SENT ---");
            _logger.LogInformation($"To: {recipient}");
            _logger.LogInformation($"Content: {content}");
            _logger.LogInformation("--------------------------");

            return Task.FromResult((true, $"mock-wa-{System.Guid.NewGuid()}", (string?)null));
        }
    }
}
