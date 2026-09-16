using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Payments;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class PaymentService : IPaymentService
    {
        private readonly AppDbContext _dbContext;
        private readonly ICashfreeService _cashfreeService;
        private readonly ILogger<PaymentService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PickNBook.Api.Services.Notifications.Interfaces.INotificationService _notificationService;

        public PaymentService(
            AppDbContext dbContext,
            ICashfreeService cashfreeService,
            ILogger<PaymentService> logger,
            IServiceScopeFactory scopeFactory,
            PickNBook.Api.Services.Notifications.Interfaces.INotificationService notificationService)
        {
            _dbContext = dbContext;
            _cashfreeService = cashfreeService;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _notificationService = notificationService;
        }

        public async Task<Payment> CreatePaymentAsync(
            string userId, string bookingType,
            decimal originalAmount, decimal markupAmount, decimal convenienceFee,
            decimal discountAmount, string? couponCode, string? offerCode,
            decimal finalPayableAmount, string currency)
        {
            var paymentRef = $"PAY-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
            var payment = new Payment
            {
                PaymentReference = paymentRef,
                CashfreeOrderId = $"TEMP-{Guid.NewGuid()}",
                UserId = userId,
                BookingType = bookingType,
                OriginalAmount = originalAmount,
                MarkupAmount = markupAmount,
                ConvenienceFee = convenienceFee,
                DiscountAmount = discountAmount,
                CouponCode = couponCode,
                OfferCode = offerCode,
                FinalPayableAmount = finalPayableAmount,
                Currency = currency,
                Status = PaymentStatus.Created,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Payments.Add(payment);
            await _dbContext.SaveChangesAsync();
            return payment;
        }

        public async Task<PendingPaymentBooking> CreatePendingBookingAsync(
            int paymentId, string bookingType, string userId,
            decimal amount, string currency,
            string bookingPayloadJson, string? pricingSnapshotJson,
            DateTime expiresAt)
        {
            var pending = new PendingPaymentBooking
            {
                PaymentId = paymentId,
                BookingType = bookingType,
                UserId = userId,
                Amount = amount,
                Currency = currency,
                BookingPayloadJson = bookingPayloadJson,
                PricingSnapshotJson = pricingSnapshotJson,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };

            _dbContext.PendingPaymentBookings.Add(pending);
            await _dbContext.SaveChangesAsync();
            return pending;
        }

        public async Task AssociateCashfreeOrderAsync(
            int paymentId, string cashfreeOrderId, string? cfOrderId, string? paymentSessionId)
        {
            var payment = await _dbContext.Payments.FindAsync(paymentId);
            if (payment == null) throw new Exception("Payment not found");

            payment.CashfreeOrderId = cashfreeOrderId;
            payment.CashfreeCfOrderId = cfOrderId;
            payment.PaymentSessionId = paymentSessionId;
            payment.Status = PaymentStatus.Pending;
            payment.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
        }

        public async Task<Payment?> GetPaymentByCashfreeOrderIdAsync(string cashfreeOrderId)
        {
            return await _dbContext.Payments
                .FirstOrDefaultAsync(p => p.CashfreeOrderId == cashfreeOrderId);
        }

        public async Task UpdatePaymentStatusAsync(int paymentId, string status,
            string? cashfreePaymentId = null, string? paymentMethod = null,
            string? failureReason = null, DateTime? webhookReceivedAt = null)
        {
            var payment = await _dbContext.Payments.FindAsync(paymentId);
            if (payment == null) return;

            string? customerEmail = null;
            string? customerPhone = null;

            if (int.TryParse(payment.UserId, out int uid))
            {
                var user = await _dbContext.Users.FindAsync(uid);
                if (user != null)
                {
                    customerEmail = user.Email;
                    customerPhone = user.PhoneNumber;
                }
            }

            // Fallback to PendingPaymentBooking payload if user record is not found or contacts missing
            if (string.IsNullOrWhiteSpace(customerPhone) || string.IsNullOrWhiteSpace(customerEmail))
            {
                var pending = await _dbContext.PendingPaymentBookings
                    .FirstOrDefaultAsync(p => p.PaymentId == payment.Id);
                if (pending != null && !string.IsNullOrWhiteSpace(pending.BookingPayloadJson))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(pending.BookingPayloadJson);
                        var root = doc.RootElement;
                        if (string.IsNullOrWhiteSpace(customerPhone))
                        {
                            if (root.TryGetProperty("CustomerPhone", out var cp)) customerPhone = cp.GetString();
                            else if (root.TryGetProperty("customerPhone", out cp)) customerPhone = cp.GetString();
                            else if (root.TryGetProperty("ContactDetails", out var cd) && cd.TryGetProperty("Mobile", out var m)) customerPhone = m.GetString();
                            else if (root.TryGetProperty("contactDetails", out cd) && cd.TryGetProperty("mobile", out m)) customerPhone = m.GetString();
                            else if (root.TryGetProperty("Passengers", out var pax) && pax.ValueKind == System.Text.Json.JsonValueKind.Array && pax.GetArrayLength() > 0)
                            {
                                var first = pax[0];
                                if (first.TryGetProperty("ContactNo", out var pPhone)) customerPhone = pPhone.GetString();
                                else if (first.TryGetProperty("contactNo", out pPhone)) customerPhone = pPhone.GetString();
                                else if (first.TryGetProperty("PhoneNumber", out pPhone)) customerPhone = pPhone.GetString();
                            }
                        }

                        if (string.IsNullOrWhiteSpace(customerEmail))
                        {
                            if (root.TryGetProperty("CustomerEmail", out var ce)) customerEmail = ce.GetString();
                            else if (root.TryGetProperty("customerEmail", out ce)) customerEmail = ce.GetString();
                            else if (root.TryGetProperty("ContactDetails", out var cd) && cd.TryGetProperty("Email", out var e)) customerEmail = e.GetString();
                            else if (root.TryGetProperty("contactDetails", out cd) && cd.TryGetProperty("email", out e)) customerEmail = e.GetString();
                            else if (root.TryGetProperty("Passengers", out var pax) && pax.ValueKind == System.Text.Json.JsonValueKind.Array && pax.GetArrayLength() > 0)
                            {
                                var first = pax[0];
                                if (first.TryGetProperty("Email", out var pEmail)) customerEmail = pEmail.GetString();
                                else if (first.TryGetProperty("email", out pEmail)) customerEmail = pEmail.GetString();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse contact details from PendingPaymentBooking payload for Payment {PaymentId}", payment.Id);
                    }
                }
            }

            string cleanReason = string.IsNullOrWhiteSpace(failureReason)
                ? "Transaction declined"
                : failureReason.Trim();

            // Telecom DLT length constraint: keep Reason concise so total SMS stays within standard single SMS limit
            if (cleanReason.Length > 45)
            {
                cleanReason = cleanReason.Substring(0, 42) + "...";
            }

            if (status == PaymentStatus.Success && payment.Status != PaymentStatus.Success)
            {
                var emailRecipient = !string.IsNullOrWhiteSpace(customerEmail) ? customerEmail.Trim() : (payment.UserId.Contains('@') ? payment.UserId : null);
                if (!string.IsNullOrWhiteSpace(emailRecipient))
                {
                    await _notificationService.EnqueueAsync(
                        eventType: "PaymentSuccess",
                        channel: "Email",
                        recipient: emailRecipient,
                        templateKey: "PAYMENT_SUCCESS",
                        payload: new { Amount = payment.FinalPayableAmount, OrderId = payment.CashfreeOrderId },
                        bookingId: payment.PaymentReference,
                        userId: payment.UserId
                    );
                }
            }
            else if (status == PaymentStatus.Failed && payment.Status != PaymentStatus.Failed)
            {
                // 1. Enqueue SMS notification if customer mobile is available
                if (!string.IsNullOrWhiteSpace(customerPhone))
                {
                    await _notificationService.EnqueueAsync(
                        eventType: "PaymentFailed",
                        channel: "SMS",
                        recipient: customerPhone.Trim(),
                        templateKey: "PAYMENT_FAILED",
                        payload: new
                        {
                            Reference = payment.PaymentReference,
                            Reason = cleanReason,
                            Var1 = payment.PaymentReference,
                            Var2 = cleanReason,
                            Amount = payment.FinalPayableAmount,
                            OrderId = payment.PaymentReference
                        },
                        bookingId: payment.PaymentReference,
                        userId: payment.UserId
                    );
                }
                else
                {
                    _logger.LogWarning("Cannot enqueue PaymentFailed SMS for Payment {PaymentId}: No phone number available.", payment.Id);
                }

                // 2. Enqueue Email notification if customer email is available
                var emailRecipient = !string.IsNullOrWhiteSpace(customerEmail) ? customerEmail.Trim() : (payment.UserId.Contains('@') ? payment.UserId : null);
                if (!string.IsNullOrWhiteSpace(emailRecipient))
                {
                    await _notificationService.EnqueueAsync(
                        eventType: "PaymentFailed",
                        channel: "Email",
                        recipient: emailRecipient,
                        templateKey: "PAYMENT_FAILED",
                        payload: new
                        {
                            Amount = payment.FinalPayableAmount,
                            OrderId = payment.PaymentReference,
                            Reason = cleanReason,
                            Reference = payment.PaymentReference,
                            Var1 = payment.PaymentReference,
                            Var2 = cleanReason
                        },
                        bookingId: payment.PaymentReference,
                        userId: payment.UserId
                    );
                }
                else
                {
                    _logger.LogWarning("Cannot enqueue PaymentFailed Email for Payment {PaymentId}: No valid email recipient available.", payment.Id);
                }
            }

            payment.Status = status;
            payment.UpdatedAt = DateTime.UtcNow;

            if (cashfreePaymentId != null) payment.CashfreePaymentId = cashfreePaymentId;
            if (paymentMethod != null) payment.PaymentMethod = paymentMethod;
            if (failureReason != null) payment.FailureReason = failureReason;
            if (webhookReceivedAt != null) payment.WebhookReceivedAt = webhookReceivedAt;

            if (status == PaymentStatus.Success && payment.PaidAt == null)
            {
                payment.PaidAt = DateTime.UtcNow;
                if (payment.FulfillmentStatus == null) 
                {
                    payment.FulfillmentStatus = "Pending";
                }
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task<bool> ProcessWebhookAsync(string cashfreeOrderId, string eventType,
            string paymentStatus, decimal amount, string? paymentId, string? paymentMethod,
            string? failureReason = null)
        {
            var payment = await GetPaymentByCashfreeOrderIdAsync(cashfreeOrderId);
            if (payment == null)
            {
                _logger.LogWarning("Webhook received for unknown order: {OrderId}", cashfreeOrderId);
                return false;
            }

            // Idempotency check
            if (payment.Status == PaymentStatus.Success)
            {
                _logger.LogInformation("Webhook ignored, payment already successful: {OrderId}", cashfreeOrderId);
                return true;
            }

            // Amount validation
            if (Math.Round(amount, 2) != Math.Round(payment.FinalPayableAmount, 2))
            {
                _logger.LogError("Amount mismatch for {OrderId}. Expected {Expected}, got {Actual}", 
                    cashfreeOrderId, payment.FinalPayableAmount, amount);
                await UpdatePaymentStatusAsync(payment.Id, PaymentStatus.Failed, paymentId, paymentMethod, "Amount mismatch", DateTime.UtcNow);
                return false;
            }

            string newStatus = paymentStatus.ToUpperInvariant() switch
            {
                "SUCCESS" => PaymentStatus.Success,
                "FAILED" => PaymentStatus.Failed,
                "CANCELLED" => PaymentStatus.Cancelled,
                _ => PaymentStatus.Pending
            };

            await UpdatePaymentStatusAsync(payment.Id, newStatus, paymentId, paymentMethod, failureReason, DateTime.UtcNow);
            
            _logger.LogInformation("Webhook processed for {OrderId}, new status: {Status}", cashfreeOrderId, newStatus);

            if (newStatus == PaymentStatus.Success)
            {
                // Fulfillment will be picked up durably by FulfillmentRecoveryWorker 
                _logger.LogInformation("Payment {PaymentId} marked for durable fulfillment queue.", payment.Id);
            }

            return true;
        }

        public async Task<PaymentVerificationResponse> VerifyPaymentAsync(string cashfreeOrderId)
        {
            var payment = await GetPaymentByCashfreeOrderIdAsync(cashfreeOrderId);
            if (payment == null)
            {
                throw new Exception("Payment record not found");
            }

            var cfPaymentsResponse = await _cashfreeService.GetPaymentsForOrderAsync(cashfreeOrderId);
            
            bool isSuccess = false;
            string? cfPaymentId = null;
            string? paymentMethod = null;
            string? failureMsg = null;
            string? failedPaymentId = null;
            bool hasFailedAttempt = false;
            
            try 
            {
                var paymentsArray = cfPaymentsResponse.RootElement.EnumerateArray();
                foreach (var cfPayment in paymentsArray)
                {
                    if (cfPayment.TryGetProperty("payment_status", out var statusEl))
                    {
                        var statusStr = statusEl.GetString();
                        if (statusStr == "SUCCESS")
                        {
                            if (cfPayment.TryGetProperty("payment_amount", out var amtEl) && 
                                Math.Round(amtEl.GetDecimal(), 2) == Math.Round(payment.FinalPayableAmount, 2))
                            {
                                isSuccess = true;
                                if (cfPayment.TryGetProperty("cf_payment_id", out var idEl)) cfPaymentId = idEl.ToString();
                                
                                if (cfPayment.TryGetProperty("payment_method", out var methodEl))
                                {
                                    var methodDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(methodEl.GetRawText());
                                    if (methodDict != null && methodDict.Count > 0)
                                    {
                                        paymentMethod = methodDict.Keys.First();
                                    }
                                }
                                break;
                            }
                        }
                        else if (statusStr == "FAILED" || statusStr == "CANCELLED" || statusStr == "USER_DROPPED")
                        {
                            hasFailedAttempt = true;
                            if (cfPayment.TryGetProperty("cf_payment_id", out var idEl)) failedPaymentId = idEl.ToString();
                            if (cfPayment.TryGetProperty("payment_message", out var msgEl)) failureMsg = msgEl.GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Cashfree payments array for verification.");
            }

            if (isSuccess && payment.Status != PaymentStatus.Success)
            {
                await UpdatePaymentStatusAsync(payment.Id, PaymentStatus.Success, cfPaymentId, paymentMethod);
                payment.Status = PaymentStatus.Success;
                
                // Fulfillment will be picked up durably by FulfillmentRecoveryWorker
                _logger.LogInformation("Payment {PaymentId} marked for durable fulfillment queue via Verify API.", payment.Id);
            }
            else if (!isSuccess && hasFailedAttempt && payment.Status != PaymentStatus.Success && payment.Status != PaymentStatus.Failed)
            {
                await UpdatePaymentStatusAsync(payment.Id, PaymentStatus.Failed, failedPaymentId, null, failureMsg, DateTime.UtcNow);
                payment.Status = PaymentStatus.Failed;
            }

            return new PaymentVerificationResponse
            {
                PaymentReference = payment.PaymentReference,
                CashfreeOrderId = payment.CashfreeOrderId,
                Status = payment.Status,
                BookingType = payment.BookingType,
                Amount = payment.FinalPayableAmount,
                Currency = payment.Currency,
                PaymentMethod = payment.PaymentMethod,
                PaidAt = payment.PaidAt,
                FailureReason = payment.FailureReason
            };
        }

        public async Task<bool> ProcessRefundWebhookAsync(string cashfreeRefundId, string refundStatus)
        {
            var refundRecord = await _dbContext.BookingCancellations
                .FirstOrDefaultAsync(r => r.CashfreeRefundId == cashfreeRefundId);

            if (refundRecord != null)
            {
                if (refundRecord.Status == "Completed")
                {
                    _logger.LogInformation("Refund for CashfreeRefundId {RefundId} is already Completed. Ignoring webhook.", cashfreeRefundId);
                    return true;
                }

                if (refundStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                {
                    if (refundRecord.Status != "Completed")
                    {
                        await _notificationService.EnqueueAsync(
                            eventType: "RefundCompleted",
                            channel: "Email",
                            recipient: refundRecord.UserId,
                            templateKey: "REFUND_COMPLETED",
                            payload: new { Amount = refundRecord.CustomerRefundAmount, BookingId = refundRecord.BookingReference }
                        );
                    }
                    refundRecord.Status = "Completed";
                    refundRecord.CompletedAtUtc = DateTime.UtcNow;
                }
                else if (refundStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    if (refundRecord.Status != "RefundFailed")
                    {
                        await _notificationService.EnqueueAsync(
                            eventType: "RefundFailed",
                            channel: "Email",
                            recipient: refundRecord.UserId,
                            templateKey: "REFUND_FAILED",
                            payload: new { Amount = refundRecord.CustomerRefundAmount, BookingId = refundRecord.BookingReference }
                        );
                    }
                    refundRecord.Status = "RefundFailed";
                }
                else if (refundStatus.Equals("PENDING", StringComparison.OrdinalIgnoreCase))
                {
                    if (refundRecord.Status != "RefundInitiated")
                    {
                        await _notificationService.EnqueueAsync(
                            eventType: "RefundInitiated",
                            channel: "Email",
                            recipient: refundRecord.UserId,
                            templateKey: "REFUND_INITIATED",
                            payload: new { Amount = refundRecord.CustomerRefundAmount, BookingId = refundRecord.BookingReference }
                        );
                    }
                    refundRecord.Status = "RefundInitiated";
                }
                else
                {
                    refundRecord.Status = refundStatus;
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Updated refund status for CashfreeRefundId {RefundId} to {Status}", cashfreeRefundId, refundRecord.Status);
                return true;
            }

            var paymentRecord = await _dbContext.Payments
                .FirstOrDefaultAsync(p => p.RefundId == cashfreeRefundId || p.CashfreeOrderId == cashfreeRefundId);

            if (paymentRecord != null)
            {
                if (paymentRecord.RefundStatus == "Refunded" || paymentRecord.Status == "REFUNDED")
                {
                    _logger.LogInformation("Refund for Payment {PaymentId}, RefundId {RefundId} is already Refunded. Ignoring webhook.", paymentRecord.Id, cashfreeRefundId);
                    return true;
                }

                if (refundStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                {
                    paymentRecord.RefundStatus = "Refunded";
                    paymentRecord.Status = "REFUNDED";
                    paymentRecord.UpdatedAt = DateTime.UtcNow;
                    paymentRecord.LastError = null;

                    await _notificationService.EnqueueAsync(
                        eventType: "RefundCompleted",
                        channel: "Email",
                        recipient: paymentRecord.UserId,
                        templateKey: "REFUND_COMPLETED",
                        payload: new { Amount = paymentRecord.FinalPayableAmount, BookingId = paymentRecord.PaymentReference }
                    );
                }
                else if (refundStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase) || refundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    paymentRecord.RefundStatus = "RefundFailed";
                    paymentRecord.UpdatedAt = DateTime.UtcNow;
                }
                else if (refundStatus.Equals("ONHOLD", StringComparison.OrdinalIgnoreCase))
                {
                    paymentRecord.RefundStatus = "RefundOnHold";
                    paymentRecord.UpdatedAt = DateTime.UtcNow;
                    paymentRecord.LastError = "Refund on hold because of insufficient account balance";
                }
                else
                {
                    paymentRecord.RefundStatus = "RefundProcessing";
                    paymentRecord.UpdatedAt = DateTime.UtcNow;
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Updated payment refund status for Payment {PaymentId}, RefundId {RefundId} to {Status}", paymentRecord.Id, cashfreeRefundId, paymentRecord.RefundStatus);
                return true;
            }

            _logger.LogWarning("Received refund webhook for unknown CashfreeRefundId {RefundId}", cashfreeRefundId);
            return false;
        }
    }
}
