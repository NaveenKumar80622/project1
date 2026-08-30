using PickNBook.Api.Models.Payments;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IPaymentService
    {
        Task<Payment> CreatePaymentAsync(
            string userId, string bookingType,
            decimal originalAmount, decimal markupAmount, decimal convenienceFee,
            decimal discountAmount, string? couponCode, string? offerCode,
            decimal finalPayableAmount, string currency);
            
        Task<PendingPaymentBooking> CreatePendingBookingAsync(
            int paymentId, string bookingType, string userId,
            decimal amount, string currency,
            string bookingPayloadJson, string? pricingSnapshotJson,
            DateTime expiresAt);
            
        Task AssociateCashfreeOrderAsync(
            int paymentId, string cashfreeOrderId, string? cfOrderId, string? paymentSessionId);
            
        Task<Payment?> GetPaymentByCashfreeOrderIdAsync(string cashfreeOrderId);
        
        Task UpdatePaymentStatusAsync(int paymentId, string status,
            string? cashfreePaymentId = null, string? paymentMethod = null,
            string? failureReason = null, DateTime? webhookReceivedAt = null);
            
        Task<bool> ProcessWebhookAsync(string cashfreeOrderId, string eventType,
            string paymentStatus, decimal amount, string? paymentId, string? paymentMethod);
            
        Task<PaymentVerificationResponse> VerifyPaymentAsync(string cashfreeOrderId);
    }
}
