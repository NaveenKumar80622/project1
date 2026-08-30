using PickNBook.Api.Models.Payments;
using System.Text.Json;

namespace PickNBook.Api.Services.Interfaces
{
    public interface ICashfreeService
    {
        Task<CashfreeOrderResponse> CreateOrderAsync(
            string orderId, decimal amount, string currency,
            string customerId, string customerName, string customerEmail, string customerPhone,
            string returnUrl, string notifyUrl);
            
        Task<JsonDocument> GetPaymentsForOrderAsync(string orderId);
        
        Task<JsonDocument> InitiateRefundAsync(string orderId, decimal amount, string refundId, string refundNote);
        
        bool VerifyWebhookSignature(string rawBody, string timestamp, string signature);
    }
}
