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

                var builder = new UriBuilder(baseUrl);
                var query = HttpUtility.ParseQueryString(builder.Query);

                query["username"] = _settings.Username;
                query["password"] = _settings.Password;
                query["unicode"] = "false";
                query["from"] = _settings.SenderId;
                
                string formattedRecipient = recipient.Trim();
                if (formattedRecipient.StartsWith("+"))
                {
                    formattedRecipient = formattedRecipient.Substring(1); // PointerIT expects numbers without +
                }
                
                query["to"] = formattedRecipient;
                query["dltPrincipalEntityId"] = _settings.PrincipalEntityId;
                query["dltContentId"] = _settings.ContentId;
                query["text"] = content;

                builder.Query = query.ToString();
                var requestUrl = builder.ToString();

                // Sanitized log (Masking password and text)
                var logQuery = HttpUtility.ParseQueryString(builder.Query);
                logQuery["password"] = "*****";
                logQuery["text"] = "*****";
                var logBuilder = new UriBuilder(baseUrl) { Query = logQuery.ToString() };
                _logger.LogInformation($"PointerIT SMS request: POST {logBuilder}");

                var response = await client.PostAsync(requestUrl, null);
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
