using Microsoft.Extensions.Options;
using PickNBook.Api.Models;
using PickNBook.Api.Models.Payments;
using PickNBook.Api.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PickNBook.Api.Services.Implementations
{
    public class CashfreeService : ICashfreeService
    {
        private readonly HttpClient _httpClient;
        private readonly CashfreeSettings _settings;
        private readonly ILogger<CashfreeService> _logger;

        public CashfreeService(
            HttpClient httpClient,
            IOptions<CashfreeSettings> options,
            ILogger<CashfreeService> logger)
        {
            _httpClient = httpClient;
            _settings = options.Value;
            _logger = logger;
        }

        public async Task<CashfreeOrderResponse> CreateOrderAsync(
            string orderId, decimal amount, string currency,
            string customerId, string customerName, string customerEmail, string customerPhone,
            string returnUrl, string notifyUrl)
        {
            var requestBody = new
            {
                order_id = orderId,
                order_amount = Math.Round(amount, 2),
                order_currency = currency,
                customer_details = new
                {
                    customer_id = customerId,
                    customer_name = customerName,
                    customer_email = customerEmail,
                    customer_phone = customerPhone
                },
                order_meta = new
                {
                    return_url = returnUrl,
                    notify_url = notifyUrl,
                    payment_methods = ""
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/orders");
            request.Headers.Add("x-client-id", _settings.ClientId);
            request.Headers.Add("x-client-secret", _settings.ClientSecret);
            request.Headers.Add("x-api-version", _settings.ApiVersion);
            request.Headers.Add("x-idempotency-key", Guid.NewGuid().ToString());

            string jsonBody = JsonSerializer.Serialize(requestBody);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Cashfree CreateOrder API failed with status {Status}: {Response}", response.StatusCode, responseContent);
                throw new Exception($"Cashfree API Error: {responseContent}");
            }

            var orderResponse = JsonSerializer.Deserialize<CashfreeOrderResponse>(responseContent);
            if (orderResponse == null)
            {
                throw new Exception("Failed to deserialize Cashfree create order response.");
            }

            return orderResponse;
        }

        public async Task<JsonDocument> GetPaymentsForOrderAsync(string orderId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.BaseUrl.TrimEnd('/')}/orders/{orderId}/payments");
            request.Headers.Add("x-client-id", _settings.ClientId);
            request.Headers.Add("x-client-secret", _settings.ClientSecret);
            request.Headers.Add("x-api-version", _settings.ApiVersion);

            var response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Cashfree GetPayments API failed for order {OrderId} with status {Status}: {Response}", orderId, response.StatusCode, responseContent);
                throw new Exception($"Cashfree API Error: {responseContent}");
            }

            return JsonDocument.Parse(responseContent);
        }

        public async Task<JsonDocument> InitiateRefundAsync(string orderId, decimal amount, string refundId, string refundNote)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/orders/{orderId}/refunds");
            request.Headers.Add("x-client-id", _settings.ClientId);
            request.Headers.Add("x-client-secret", _settings.ClientSecret);
            request.Headers.Add("x-api-version", _settings.ApiVersion);
            request.Headers.Add("x-idempotency-key", refundId);

            var payload = new
            {
                refund_amount = amount,
                refund_id = refundId,
                refund_note = refundNote
            };
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Cashfree InitiateRefund API failed for order {OrderId} with status {Status}: {Response}", orderId, response.StatusCode, responseContent);
                throw new Exception($"Cashfree Refund API Error: {responseContent}");
            }

            return JsonDocument.Parse(responseContent);
        }

        public bool VerifyWebhookSignature(string rawBody, string timestamp, string signature)
        {
            try
            {
                string payload = timestamp + rawBody;
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.ClientSecret));
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                string computedSignature = Convert.ToBase64String(hash);

                byte[] computedBytes = Convert.FromBase64String(computedSignature);
                byte[] providedBytes = Convert.FromBase64String(signature);

                return CryptographicOperations.FixedTimeEquals(computedBytes, providedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying webhook signature.");
                return false;
            }
        }
    }
}
