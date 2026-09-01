using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using PickNBook.Api.Services.Notifications.Interfaces;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace PickNBook.Api.Services.Notifications.Providers
{
    public class PointerItSmsProvider : ISmsProvider
    {
        public string ProviderName => "PointerIT";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PointerItSmsProvider> _logger;
        private readonly PointerItSmsSettings _settings;

        public PointerItSmsProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<PointerItSmsProvider> logger,
            IOptions<PointerItSmsSettings> options)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _settings = options.Value;
        }

        public async Task<(bool IsSuccess, string? ProviderMessageId, string? ErrorMessage)> SendAsync(string recipient, string content, string? subject = null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(nameof(PointerItSmsProvider));

                string baseUrl = _settings.Url.EndsWith("?username=") 
                    ? _settings.Url.Substring(0, _settings.Url.Length - 10) 
                    : _settings.Url;

                var parameters = new Dictionary<string, string>
                {
                    { "username", _settings.Username },
                    { "password", _settings.Password },
                    { "unicode", "false" },
                    { "from", _settings.SenderId },
                    { "to", recipient },
                    { "dltPrincipalEntityId", _settings.PrincipalEntityId },
                    { "dltContentId", _settings.ContentId },
                    { "text", content }
                };

                var formContent = new FormUrlEncodedContent(parameters);

                // Send POST with FormUrlEncodedContent instead of query string to prevent 
                // HttpClient from automatically logging the password and OTP in the URL.
                var response = await client.PostAsync(baseUrl, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"PointerIT HTTP Failure: {response.StatusCode}");
                    return (false, null, $"HTTP {response.StatusCode}");
                }

                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                
                int statusCode = root.TryGetProperty("statusCode", out var statusEl) && statusEl.ValueKind == JsonValueKind.Number ? statusEl.GetInt32() : 0;
                string state = root.TryGetProperty("state", out var stateEl) ? stateEl.GetString() ?? "" : "";
                string description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
                string txId = root.TryGetProperty("transactionId", out var txEl) ? txEl.GetRawText() : "";

                if (statusCode == 200 && state == "SUBMIT_ACCEPTED")
                {
                    _logger.LogInformation($"PointerIT SMS dispatched. TX: {txId}");
                    return (true, txId, null);
                }

                _logger.LogWarning($"PointerIT Provider Rejected: State={state}, Desc={description}");
                return (false, null, description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PointerIT SMS transmission failed.");
                return (false, null, ex.Message);
            }
        }
    }
}
