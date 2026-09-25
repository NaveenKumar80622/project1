using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.Payments
{
    /// <summary>
    /// DTO to deserialize the Cashfree create-order API response.
    /// </summary>
    public class CashfreeOrderResponse
    {
        [JsonPropertyName("cf_order_id")]
        public string CfOrderId { get; set; } = string.Empty;

        [JsonPropertyName("order_id")]
        public string OrderId { get; set; } = string.Empty;

        [JsonPropertyName("payment_session_id")]
        public string PaymentSessionId { get; set; } = string.Empty;

        [JsonPropertyName("order_status")]
        public string OrderStatus { get; set; } = string.Empty;

        [JsonPropertyName("order_amount")]
        public decimal OrderAmount { get; set; }

        [JsonPropertyName("order_currency")]
        public string OrderCurrency { get; set; } = "INR";
    }
}
