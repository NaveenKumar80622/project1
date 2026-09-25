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

        public Task<(bool IsSuccess, string? ProviderMessageId, string? ErrorMessage)> SendAsync(string recipient, string content, string? subject = null)
        {
            return SendSmsAsync(recipient, content, dltContentId: subject);
        }

        public async Task<(bool IsSuccess, string? ProviderMessageId, string? ErrorMessage)> SendSmsAsync(
            string recipient, 
            string content, 
            string? dltContentId = null, 
            string? senderId = null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(nameof(PointerItSmsProvider));

                string baseUrl = _settings.Url.Contains('?') 
                    ? _settings.Url.Split('?')[0] 
                    : _settings.Url;

                var builder = new UriBuilder(baseUrl);
                var query = HttpUtility.ParseQueryString(builder.Query);

                query["username"] = _settings.Username;
                query["password"] = _settings.Password;
                query["unicode"] = "false";
                query["from"] = !string.IsNullOrWhiteSpace(senderId) ? senderId : _settings.SenderId;
                
                string formattedRecipient = recipient.Trim();
                if (formattedRecipient.StartsWith("+"))
                {
                    formattedRecipient = formattedRecipient.Substring(1); // PointerIT expects numbers without +
                }
                
                query["to"] = formattedRecipient;

                if (!string.IsNullOrWhiteSpace(_settings.PrincipalEntityId))
                {
                    query["dltPrincipalEntityId"] = _settings.PrincipalEntityId;
                }

                string effectiveContentId = !string.IsNullOrWhiteSpace(dltContentId) 
                    ? dltContentId 
                    : _settings.DefaultContentId;

                query["dltContentId"] = effectiveContentId;
                query["text"] = content;

                var correlationId = PickNBook.Api.Infrastructure.Logging.CorrelationIdContext.CorrelationId;
                if (!string.IsNullOrWhiteSpace(correlationId))
                {
                    query["corelationId"] = correlationId;
                }

                builder.Query = query.ToString();
                var requestUrl = builder.ToString();

                var response = await client.PostAsync(requestUrl, null);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"PointerIT HTTP Failure: {response.StatusCode} - {responseContent}");
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
                    return (true, txId, null);
                }

                _logger.LogWarning("PointerIT rejected SMS. StatusCode: {StatusCode}, State: {State}, Description: {Description}", statusCode, state, description);
                return (false, null, string.IsNullOrWhiteSpace(description) ? $"PointerIT error {statusCode}: {state}" : description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PointerIT SMS transmission failed.");
                return (false, null, ex.Message);
            }
        }
    }
}
