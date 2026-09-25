using Microsoft.Extensions.Options;
using PickNBook.Api.Models;
using System.Text;
using System.Text.Json;

namespace PickNBook.Api.Services
{
    public class SmsService : ISmsService
    {
        private readonly HttpClient _httpClient;
        private readonly SmsSettings _settings;
        private readonly ILogger<SmsService> _logger;

        public SmsService(HttpClient httpClient, IOptions<SmsSettings> settings, ILogger<SmsService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<(bool IsSent, string Message)> SendSmsAsync(string phoneNumber, string message)
        {
            if (!_settings.Enabled)
            {
                _logger.LogWarning("[SMS SERVICE MOCKED] SMS settings disabled. To: {PhoneNumber}. Message: {Message}", phoneNumber, message);
                return (true, "SMS sent successfully (Mocked Mode).");
            }

            if (string.Equals(_settings.Provider, "Twilio", StringComparison.OrdinalIgnoreCase))
            {
                var twilio = _settings.Twilio;
                if (twilio == null || string.IsNullOrWhiteSpace(twilio.AccountSid) || string.IsNullOrWhiteSpace(twilio.AuthToken) || string.IsNullOrWhiteSpace(twilio.FromPhoneNumber))
                {
                    _logger.LogWarning("[SMS SERVICE MOCKED] Twilio settings incomplete. To: {PhoneNumber}. Message: {Message}", phoneNumber, message);
                    return (true, "SMS sent successfully (Mocked Mode).");
                }

                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{twilio.AccountSid}:{twilio.AuthToken}"));
                var requestUrl = $"https://api.twilio.com/2010-04-01/Accounts/{twilio.AccountSid}/Messages.json";

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("To", phoneNumber),
                    new KeyValuePair<string, string>("From", twilio.FromPhoneNumber),
                    new KeyValuePair<string, string>("Body", message)
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                request.Content = content;

                try
                {
                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        return (true, "SMS sent successfully via Twilio.");
                    }
                    else
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        _logger.LogError("Twilio SMS send failed: {StatusCode} - {Body}", response.StatusCode, responseBody);
                        return (false, $"Twilio failed: {responseBody}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending SMS via Twilio.");
                    return (false, $"Twilio error: {ex.Message}");
                }
            }
            else if (string.Equals(_settings.Provider, "Msg91", StringComparison.OrdinalIgnoreCase))
            {
                var msg91 = _settings.Msg91;
                if (msg91 == null || string.IsNullOrWhiteSpace(msg91.AuthKey))
                {
                    _logger.LogWarning("[SMS SERVICE MOCKED] Msg91 settings incomplete. To: {PhoneNumber}. Message: {Message}", phoneNumber, message);
                    return (true, "SMS sent successfully (Mocked Mode).");
                }

                var requestUrl = "https://control.msg91.com/api/v5/flow/";
                var payload = new
                {
                    template_id = msg91.TemplateId,
                    short_url = "0",
                    recipients = new[]
                    {
                        new { mobiles = phoneNumber, var1 = message }
                    }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Add("authkey", msg91.AuthKey);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                try
                {
                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        return (true, "SMS sent successfully via Msg91.");
                    }
                    else
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        _logger.LogError("Msg91 SMS send failed: {StatusCode} - {Body}", response.StatusCode, responseBody);
                        return (false, $"Msg91 failed: {responseBody}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending SMS via Msg91.");
                    return (false, $"Msg91 error: {ex.Message}");
                }
            }

            _logger.LogWarning("[SMS SERVICE MOCKED] No active SMS provider configured. To: {PhoneNumber}. Message: {Message}", phoneNumber, message);
            return (true, "SMS sent successfully (Mocked Mode).");
        }
    }
}
