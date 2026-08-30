using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.Payments
{
    /// <summary>
    /// Cashfree webhook payload structure.
    /// Cashfree sends this JSON body to the notify_url when payment events occur.
    /// </summary>
    public class CashfreeWebhookPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public CashfreeWebhookData? Data { get; set; }
    }

    public class CashfreeWebhookData
    {
        [JsonPropertyName("order")]
        public CashfreeWebhookOrder? Order { get; set; }

        [JsonPropertyName("payment")]
        public CashfreeWebhookPaymentDetail? Payment { get; set; }
    }

    public class CashfreeWebhookOrder
    {
        [JsonPropertyName("order_id")]
        public string OrderId { get; set; } = string.Empty;

        [JsonPropertyName("order_amount")]
        public decimal OrderAmount { get; set; }

        [JsonPropertyName("order_currency")]
        public string OrderCurrency { get; set; } = "INR";

        [JsonPropertyName("order_status")]
        public string OrderStatus { get; set; } = string.Empty;
    }

    public class CashfreeWebhookPaymentDetail
    {
        [JsonPropertyName("cf_payment_id")]
        public string CfPaymentId { get; set; } = string.Empty;

        [JsonPropertyName("payment_status")]
        public string PaymentStatus { get; set; } = string.Empty;

        [JsonPropertyName("payment_amount")]
        public decimal PaymentAmount { get; set; }

        [JsonPropertyName("payment_currency")]
        public string PaymentCurrency { get; set; } = "INR";

        [JsonPropertyName("payment_method")]
        public CashfreePaymentMethodDetail? PaymentMethod { get; set; }

        [JsonPropertyName("payment_time")]
        public string? PaymentTime { get; set; }

        [JsonPropertyName("payment_message")]
        public string? PaymentMessage { get; set; }
    }

    public class CashfreePaymentMethodDetail
    {
        /// <summary>
        /// Contains the payment method object. The key varies by method type (e.g., "upi", "card", "netbanking").
        /// We capture the raw JSON for flexibility.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }

        /// <summary>
        /// Extracts the payment method name from the first key in the method object.
        /// </summary>
        public string GetMethodName()
        {
            if (AdditionalData != null && AdditionalData.Count > 0)
            {
                return AdditionalData.Keys.First();
            }
            return "unknown";
        }
    }
}
