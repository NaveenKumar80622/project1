using System.Collections.Concurrent;
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
        private readonly IWalletReservationService _walletReservationService;
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _paymentLocks = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _orderLocks = new();

        public PaymentService(
            AppDbContext dbContext,
            ICashfreeService cashfreeService,
            ILogger<PaymentService> logger,
            IServiceScopeFactory scopeFactory,
            PickNBook.Api.Services.Notifications.Interfaces.INotificationService notificationService,
            IWalletReservationService walletReservationService)
        {
            _dbContext = dbContext;
            _cashfreeService = cashfreeService;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _notificationService = notificationService;
            _walletReservationService = walletReservationService;
        }

        public async Task<Payment> CreatePaymentAsync(
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
            string? paymentReference = null)
        {
            var paymentRef = !string.IsNullOrWhiteSpace(paymentReference)
                ? paymentReference
                : $"PAY-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

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
                TotalAmount = totalAmount ?? finalPayableAmount,
                WalletUsedAmount = walletUsedAmount ?? 0m,
                GatewayPaidAmount = gatewayPaidAmount ?? finalPayableAmount,
                PaymentMethod = paymentMethod ?? "Cashfree",
                WalletReservationStatus = walletReservationStatus ?? "None",
                WalletTransactionId = walletTransactionId,
                GatewayPaymentMethod = gatewayPaymentMethod,
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
            var sem = _paymentLocks.GetOrAdd(paymentId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();
            try
            {
                var payment = await _dbContext.Payments.FindAsync(paymentId);
                if (payment == null) return;

                // Terminal status protection: Never overwrite a successful payment with a failed/cancelled/expired status
                if (payment.Status == PaymentStatus.Success)
                {
                    _logger.LogInformation("Payment {PaymentId} is already in terminal Success state. Status transition to {NewStatus} ignored.", paymentId, status);
                    return;
                }

                // Terminal failure protection: Never overwrite a failed/cancelled/expired payment with another terminal status
                if ((payment.Status == PaymentStatus.Failed || payment.Status == PaymentStatus.Cancelled || payment.Status == PaymentStatus.Expired)
                    && (status == PaymentStatus.Failed || status == PaymentStatus.Cancelled || status == PaymentStatus.Expired))
                {
                    _logger.LogInformation("Payment {PaymentId} is already in terminal state {CurrentStatus}. Duplicate transition to {NewStatus} ignored.", paymentId, payment.Status, status);
                    return;
                }

                // If payment is already Failed/Cancelled/Expired, reject late Success if reservation was already released
                if ((payment.Status == PaymentStatus.Failed || payment.Status == PaymentStatus.Cancelled || payment.Status == PaymentStatus.Expired)
                    && status == PaymentStatus.Success)
                {
                    _logger.LogWarning("Payment {PaymentId} is already terminal {CurrentStatus}. Refusing to transition to Success.", paymentId, payment.Status);
                    return;
                }

                // Wallet reservation transitions for Hybrid payment
                if (string.Equals(payment.PaymentMethod, "Hybrid", StringComparison.OrdinalIgnoreCase) && payment.WalletTransactionId.HasValue)
                {
                    if (status == PaymentStatus.Success)
                    {
                        if (payment.WalletReservationStatus == "Reserved")
                        {
                            await _walletReservationService.CommitReservationAsync(payment.WalletTransactionId.Value);
                            payment.WalletReservationStatus = "Committed";
                            _logger.LogInformation("Committed wallet reservation {TxId} for Payment {PaymentId}", payment.WalletTransactionId.Value, paymentId);
                        }
                        else if (payment.WalletReservationStatus == "Committed")
                        {
                            _logger.LogInformation("Wallet reservation {TxId} is already Committed for Payment {PaymentId}", payment.WalletTransactionId.Value, paymentId);
                        }
                        else
                        {
                            _logger.LogError("Inconsistent state: Cannot commit wallet reservation {TxId} in status {ResStatus} for Payment {PaymentId}", payment.WalletTransactionId.Value, payment.WalletReservationStatus, paymentId);
                            return;
                        }
                    }
                    else if (status == PaymentStatus.Failed || status == PaymentStatus.Cancelled || status == PaymentStatus.Expired)
                    {
                        if (payment.WalletReservationStatus == "Reserved")
                        {
                            await _walletReservationService.ReleaseReservationAsync(
                                payment.WalletTransactionId.Value,
                                $"Payment {status}: {failureReason ?? "Gateway payment not completed"}");
                            payment.WalletReservationStatus = "Released";
                            _logger.LogInformation("Released wallet reservation {TxId} for Payment {PaymentId} due to status {Status}", payment.WalletTransactionId.Value, paymentId, status);
                        }
                        else if (payment.WalletReservationStatus == "Released")
                        {
                            _logger.LogInformation("Wallet reservation {TxId} is already Released for Payment {PaymentId}", payment.WalletTransactionId.Value, paymentId);
                        }
                        else if (payment.WalletReservationStatus == "Committed")
                        {
                            _logger.LogError("Critical: Cannot release already Committed wallet reservation {TxId} for Payment {PaymentId}", payment.WalletTransactionId.Value, paymentId);
                            return;
                        }
                    }
                }

                // Resolve customer contacts for notifications
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
                    // 1. Enqueue SMS notification if customer mobile is available
                    if (!string.IsNullOrWhiteSpace(customerPhone))
                    {
                        string formattedAmount = payment.FinalPayableAmount.ToString("0.00");
                        await _notificationService.EnqueueAsync(
                            eventType: "PaymentSuccess",
                            channel: "SMS",
                            recipient: customerPhone.Trim(),
                            templateKey: "PAYMENT_SUCCESS",
                            payload: new
                            {
                                Reference = payment.PaymentReference,
                                Amount = formattedAmount,
                                Var1 = payment.PaymentReference,
                                Var2 = formattedAmount
                            },
                            bookingId: payment.PaymentReference,
                            userId: payment.UserId
                        );
                    }
                    else
                    {
                        _logger.LogWarning("Cannot enqueue PaymentSuccess SMS for Payment {PaymentId}: No phone number available.", payment.Id);
                    }

                    // 2. Enqueue Email notification if customer email is available
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
                if (paymentMethod != null) payment.GatewayPaymentMethod = paymentMethod;
                if (string.IsNullOrEmpty(payment.PaymentMethod)) payment.PaymentMethod = "Cashfree";
                if (failureReason != null) payment.FailureReason = failureReason;
                if (webhookReceivedAt != null) payment.WebhookReceivedAt = webhookReceivedAt;

                if (status == PaymentStatus.Success && payment.PaidAt == null)
                {
                    payment.PaidAt = DateTime.UtcNow;
                    if (payment.FulfillmentStatus == null || payment.FulfillmentStatus == "None") 
                    {
                        payment.FulfillmentStatus = "Pending";
                    }
                }

                await _dbContext.SaveChangesAsync();
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task<bool> ProcessWebhookAsync(string cashfreeOrderId, string eventType,
            string paymentStatus, decimal amount, string? paymentId, string? paymentMethod,
            string? failureReason = null)
        {
            var sem = _orderLocks.GetOrAdd(cashfreeOrderId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();
            try
            {
                var payment = await GetPaymentByCashfreeOrderIdAsync(cashfreeOrderId);
                if (payment == null)
                {
                    _logger.LogWarning("Webhook received for unknown order: {OrderId}", cashfreeOrderId);
                    return false;
                }

                // Wallet-only payment check (Requirement 7)
                if (string.Equals(payment.PaymentMethod, "Wallet", StringComparison.OrdinalIgnoreCase) ||
                    (payment.GatewayPaidAmount == 0 && !string.Equals(payment.PaymentMethod, "Cashfree", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Webhook ignored for wallet-only order: {OrderId}", cashfreeOrderId);
                    return true;
                }

                // Idempotency check 1: already successful
                if (payment.Status == PaymentStatus.Success)
                {
                    _logger.LogInformation("Webhook ignored, payment already successful: {OrderId}", cashfreeOrderId);
                    return true;
                }

                // Expected amount must be Payment.GatewayPaidAmount (not TotalAmount for Hybrid)
                decimal expectedAmount = payment.GatewayPaidAmount > 0 
                    ? payment.GatewayPaidAmount 
                    : payment.FinalPayableAmount;

                // Amount validation
                if (amount <= 0 || Math.Round(amount, 2) != Math.Round(expectedAmount, 2))
                {
                    _logger.LogError("Amount mismatch for {OrderId}. Expected {Expected}, got {Actual}", 
                        cashfreeOrderId, expectedAmount, amount);
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

                // Idempotency check 2: already failed or cancelled
                if ((payment.Status == PaymentStatus.Failed && newStatus == PaymentStatus.Failed) ||
                    (payment.Status == PaymentStatus.Cancelled && newStatus == PaymentStatus.Cancelled))
                {
                    _logger.LogInformation("Webhook ignored, payment {OrderId} already in terminal state: {Status}", cashfreeOrderId, payment.Status);
                    return true;
                }

                await UpdatePaymentStatusAsync(payment.Id, newStatus, paymentId, paymentMethod, failureReason, DateTime.UtcNow);
                
                _logger.LogInformation("Webhook processed for {OrderId}, new status: {Status}", cashfreeOrderId, newStatus);

                if (newStatus == PaymentStatus.Success)
                {
                    // Fulfillment will be picked up durably by FulfillmentRecoveryWorker 
                    _logger.LogInformation("Payment {PaymentId} marked for durable fulfillment queue.", payment.Id);
                }

                return true;
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task<PaymentVerificationResponse> VerifyPaymentAsync(string cashfreeOrderId)
        {
            var payment = await GetPaymentByCashfreeOrderIdAsync(cashfreeOrderId);
            if (payment == null)
            {
                throw new Exception("Payment record not found");
            }

            // Requirement 7: Wallet-only orders do not call Cashfree API
            if (string.Equals(payment.PaymentMethod, "Wallet", StringComparison.OrdinalIgnoreCase) ||
                (payment.GatewayPaidAmount == 0 && !string.Equals(payment.PaymentMethod, "Cashfree", StringComparison.OrdinalIgnoreCase)))
            {
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

            if (payment.Status == PaymentStatus.Success)
            {
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

            var cfPaymentsResponse = await _cashfreeService.GetPaymentsForOrderAsync(cashfreeOrderId);
            
            bool isSuccess = false;
            string? cfPaymentId = null;
            string? paymentMethod = null;
            string? failureMsg = null;
            string? failedPaymentId = null;
            bool hasFailedAttempt = false;
            decimal expectedAmount = payment.GatewayPaidAmount > 0 ? payment.GatewayPaidAmount : payment.FinalPayableAmount;
            
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
                            if (cfPayment.TryGetProperty("payment_amount", out var amtEl))
                            {
                                decimal actualAmount = amtEl.GetDecimal();
                                if (actualAmount > 0 && Math.Round(actualAmount, 2) == Math.Round(expectedAmount, 2))
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
                                else
                                {
                                    _logger.LogWarning("Cashfree payment amount {Actual} did not match expected {Expected} for order {OrderId}",
                                        actualAmount, expectedAmount, cashfreeOrderId);
                                }
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
                payment = await _dbContext.Payments.FindAsync(payment.Id) ?? payment;
                
                // Fulfillment will be picked up durably by FulfillmentRecoveryWorker
                _logger.LogInformation("Payment {PaymentId} marked for durable fulfillment queue via Verify API.", payment.Id);
            }
            else if (!isSuccess && hasFailedAttempt && payment.Status != PaymentStatus.Success && payment.Status != PaymentStatus.Failed)
            {
                await UpdatePaymentStatusAsync(payment.Id, PaymentStatus.Failed, failedPaymentId, null, failureMsg, DateTime.UtcNow);
                payment = await _dbContext.Payments.FindAsync(payment.Id) ?? payment;
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

        public async Task<int> ProcessExpiredReservationsAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            // Find payments where wallet reservation is still Reserved and expiry time has lapsed
            var candidatePaymentIds = await (from p in _dbContext.Payments
                                             join pb in _dbContext.PendingPaymentBookings on p.Id equals pb.PaymentId into pbGroup
                                             from pb in pbGroup.DefaultIfEmpty()
                                             where (p.Status == PaymentStatus.Created || p.Status == PaymentStatus.Pending)
                                             where p.WalletReservationStatus == "Reserved" && p.WalletTransactionId != null
                                             where (pb != null && pb.ExpiresAt <= now) || (pb == null && p.CreatedAt <= now.AddMinutes(-30))
                                             select p.Id)
                                            .Distinct()
                                            .ToListAsync(cancellationToken);

            int processedCount = 0;
            foreach (var paymentId in candidatePaymentIds)
            {
                var payment = await _dbContext.Payments.FindAsync(new object[] { paymentId }, cancellationToken);
                if (payment == null) continue;

                // Safety guard 1: Never touch already successful payments (Clarification 4)
                if (payment.Status == PaymentStatus.Success)
                {
                    _logger.LogWarning("Expiry recovery skipped for Payment {PaymentId} because status is already Success", paymentId);
                    continue;
                }

                // Safety guard 2: Only proceed if reservation is still Reserved and payment is still Pending/Created
                if (payment.WalletReservationStatus == "Reserved" &&
                    (payment.Status == PaymentStatus.Created || payment.Status == PaymentStatus.Pending))
                {
                    _logger.LogInformation("Expiring abandoned hybrid reservation for Payment {PaymentId}, TxId {TxId}",
                        payment.Id, payment.WalletTransactionId);

                    await UpdatePaymentStatusAsync(payment.Id, PaymentStatus.Expired, failureReason: "Checkout abandoned / Pending booking expired");

                    // Also mark the pending booking record expired if it exists
                    var pendingBooking = await _dbContext.PendingPaymentBookings
                        .FirstOrDefaultAsync(pb => pb.PaymentId == paymentId, cancellationToken);
                    if (pendingBooking != null && pendingBooking.Status != "Expired")
                    {
                        pendingBooking.Status = "Expired";
                    }

                    processedCount++;
                }
            }

            if (processedCount > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Processed and released {Count} expired wallet reservations", processedCount);
            }

            return processedCount;
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

                string? customerPhone = null;
                if (int.TryParse(refundRecord.UserId, out int uid))
                {
                    var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);
                    customerPhone = user?.PhoneNumber;
                }
                if (string.IsNullOrWhiteSpace(customerPhone) && refundRecord.BookingType == "Bus")
                {
                    var bus = await _dbContext.BusReservations.AsNoTracking().FirstOrDefaultAsync(b => b.BookingReference == refundRecord.BookingReference);
                    customerPhone = bus?.PassengerPhone;
                }
                else if (string.IsNullOrWhiteSpace(customerPhone) && refundRecord.BookingType == "Hotel")
                {
                    var hotel = await _dbContext.HotelReservations.AsNoTracking().FirstOrDefaultAsync(h => h.BookingReference == refundRecord.BookingReference);
                    customerPhone = hotel?.GuestPhone;
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

                        if (!string.IsNullOrWhiteSpace(customerPhone))
                        {
                            string formattedAmount = refundRecord.CustomerRefundAmount.ToString("N2");
                            await _notificationService.EnqueueAsync(
                                eventType: "RefundCompleted",
                                channel: "SMS",
                                recipient: customerPhone.Trim(),
                                templateKey: "REFUND_STATUS",
                                payload: new
                                {
                                    Status = "completed",
                                    Reference = refundRecord.BookingReference,
                                    RefundRef = cashfreeRefundId,
                                    Amount = formattedAmount,
                                    Var1 = "completed",
                                    Var2 = refundRecord.BookingReference,
                                    Var3 = cashfreeRefundId,
                                    Var4 = formattedAmount
                                },
                                bookingId: refundRecord.BookingReference,
                                userId: refundRecord.UserId
                            );
                        }
                    }
                    refundRecord.Status = "Completed";
                    refundRecord.CompletedAtUtc = DateTime.UtcNow;
                }
                else if (refundStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase) || refundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
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

                        if (!string.IsNullOrWhiteSpace(customerPhone))
                        {
                            string statusText = refundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) ? "Cancelled" : "Failed";
                            await _notificationService.EnqueueAsync(
                                eventType: "RefundFailed",
                                channel: "SMS",
                                recipient: customerPhone.Trim(),
                                templateKey: "REFUND_FAILED",
                                payload: new
                                {
                                    Status = statusText,
                                    Reference = refundRecord.BookingReference,
                                    RefundRef = cashfreeRefundId,
                                    SupportUrl = "https://www.picknbook.in/contact",
                                    Var1 = statusText,
                                    Var2 = refundRecord.BookingReference,
                                    Var3 = cashfreeRefundId,
                                    Var4 = "https://www.picknbook.in/contact"
                                },
                                bookingId: refundRecord.BookingReference,
                                userId: refundRecord.UserId
                            );
                        }
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

                        if (!string.IsNullOrWhiteSpace(customerPhone))
                        {
                            string formattedAmount = refundRecord.CustomerRefundAmount.ToString("N2");
                            await _notificationService.EnqueueAsync(
                                eventType: "RefundInitiated",
                                channel: "SMS",
                                recipient: customerPhone.Trim(),
                                templateKey: "REFUND_STATUS",
                                payload: new
                                {
                                    Status = "initiated",
                                    Reference = refundRecord.BookingReference,
                                    RefundRef = cashfreeRefundId,
                                    Amount = formattedAmount,
                                    Var1 = "initiated",
                                    Var2 = refundRecord.BookingReference,
                                    Var3 = cashfreeRefundId,
                                    Var4 = formattedAmount
                                },
                                bookingId: refundRecord.BookingReference,
                                userId: refundRecord.UserId
                            );
                        }
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

                    string? pPhone = null;
                    if (int.TryParse(paymentRecord.UserId, out int pUid))
                    {
                        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == pUid);
                        pPhone = user?.PhoneNumber;
                    }
                    if (!string.IsNullOrWhiteSpace(pPhone))
                    {
                        string formattedAmount = paymentRecord.FinalPayableAmount.ToString("N2");
                        await _notificationService.EnqueueAsync(
                            eventType: "RefundCompleted",
                            channel: "SMS",
                            recipient: pPhone.Trim(),
                            templateKey: "REFUND_STATUS",
                            payload: new
                            {
                                Status = "completed",
                                Reference = paymentRecord.PaymentReference,
                                RefundRef = cashfreeRefundId,
                                Amount = formattedAmount,
                                Var1 = "completed",
                                Var2 = paymentRecord.PaymentReference,
                                Var3 = cashfreeRefundId,
                                Var4 = formattedAmount
                            },
                            bookingId: paymentRecord.PaymentReference,
                            userId: paymentRecord.UserId
                        );
                    }
                }
                else if (refundStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase) || refundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    paymentRecord.RefundStatus = "RefundFailed";
                    paymentRecord.UpdatedAt = DateTime.UtcNow;

                    string? pPhone = null;
                    if (int.TryParse(paymentRecord.UserId, out int pUid))
                    {
                        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == pUid);
                        pPhone = user?.PhoneNumber;
                    }
                    if (!string.IsNullOrWhiteSpace(pPhone))
                    {
                        string statusText = refundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) ? "Cancelled" : "Failed";
                        await _notificationService.EnqueueAsync(
                            eventType: "RefundFailed",
                            channel: "SMS",
                            recipient: pPhone.Trim(),
                            templateKey: "REFUND_FAILED",
                            payload: new
                            {
                                Status = statusText,
                                Reference = paymentRecord.PaymentReference,
                                RefundRef = cashfreeRefundId,
                                SupportUrl = "https://www.picknbook.in/contact",
                                Var1 = statusText,
                                Var2 = paymentRecord.PaymentReference,
                                Var3 = cashfreeRefundId,
                                Var4 = "https://www.picknbook.in/contact"
                            },
                            bookingId: paymentRecord.PaymentReference,
                            userId: paymentRecord.UserId
                        );
                    }
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
