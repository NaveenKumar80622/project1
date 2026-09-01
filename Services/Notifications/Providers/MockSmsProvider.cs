using Microsoft.Extensions.Logging;
using PickNBook.Api.Services.Notifications.Interfaces;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Notifications.Providers
{
    public class MockSmsProvider : ISmsProvider
    {
        public string ProviderName => "Mock";

        private readonly ILogger<MockSmsProvider> _logger;

        public MockSmsProvider(ILogger<MockSmsProvider> logger)
        {
            _logger = logger;
        }

        public Task<(bool IsSuccess, string? ProviderMessageId, string? ErrorMessage)> SendAsync(string recipient, string content, string? subject = null)
        {
            _logger.LogInformation("--- MOCK SMS SENT ---");
            _logger.LogInformation($"To: {recipient}");
            _logger.LogInformation($"Content: {content}");
            _logger.LogInformation("---------------------");

            return Task.FromResult((true, $"mock-sms-{System.Guid.NewGuid()}", (string?)null));
        }
    }
}
