using PickNBook.Api.Models.Payments;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IPaymentService
    {
        Task<Payment> CreatePaymentAsync(
            string userId, string bookingType,
            decimal originalAmount, decimal markupAmount, decimal convenienceFee,
            decimal discountAmount, string? couponCode, string? offerCode,
            decimal finalPayableAmount, string currency,
            decimal? totalAmount = null,
            decimal? walletUsedAmount = null,
            decimal? gatewayPaidAmount = null,
            string? paymentMethod = null,
            string? walletReservationStatus = null,
            long? walletTransactionId = null,
            string? gatewayPaymentMethod = null,
            string? paymentReference = null,
            string? customerName = null,
            string? customerEmail = null,
            string? customerPhone = null,
            int? passengerCount = null,
            string? passengerDetailsJson = null);
            
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
            string paymentStatus, decimal amount, string? paymentId, string? paymentMethod,
            string? failureReason = null);

        Task<bool> ProcessRefundWebhookAsync(string cashfreeRefundId, string refundStatus);
            
        Task<PaymentVerificationResponse> VerifyPaymentAsync(string cashfreeOrderId);
        
        Task<int> ProcessExpiredReservationsAsync(CancellationToken cancellationToken = default);
    }
}
