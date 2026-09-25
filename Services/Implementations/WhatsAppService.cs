using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models;

namespace PickNBook.Api.Services;

public interface IWhatsAppService
{
    Task<(bool IsSent, string Message)> SendTextAsync(string phoneNumber, string message);
}

public class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly WhatsAppSettings _settings;

    public WhatsAppService(HttpClient httpClient, IOptions<WhatsAppSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<(bool IsSent, string Message)> SendTextAsync(string phoneNumber, string message)
    {
        if (!_settings.Enabled)
        {
            return (false, "WhatsApp is disabled in configuration.");
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(_settings.AccessToken) ||
            string.IsNullOrWhiteSpace(_settings.PhoneNumberId))
        {
            return (false, "WhatsApp configuration is incomplete.");
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return (false, "WhatsApp number is required.");
        }

        var endpoint = $"{_settings.ApiBaseUrl.TrimEnd('/')}/{_settings.PhoneNumberId}/messages";

        var payload = new
        {
            messaging_product = "whatsapp",
            to = NormalizePhone(phoneNumber),
            type = "text",
            text = new { body = message }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return (false, $"WhatsApp send failed: {(int)response.StatusCode} {response.ReasonPhrase}. {errorBody}");
            }

            return (true, "WhatsApp message sent.");
        }
        catch (Exception ex)
        {
            return (false, $"WhatsApp send failed: {ex.Message}");
        }
    }

    private static string NormalizePhone(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("+"))
        {
            normalized = normalized[1..];
        }

        return new string(normalized.Where(char.IsDigit).ToArray());
    }
}
