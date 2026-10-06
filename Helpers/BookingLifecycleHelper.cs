using System;
using System.Collections.Generic;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Models.Payments;

namespace PickNBook.Api.Helpers
{
    public class UserLifecycleHierarchyDto
    {
        public string CanonicalStatus { get; set; } = string.Empty;
        public string CanonicalStatusLabel { get; set; } = string.Empty;
        public int CurrentStageIndex { get; set; }
        public string CurrentStageKey { get; set; } = string.Empty;
        public string CurrentStageName { get; set; } = string.Empty;
        public int TotalStages { get; set; } = 5;
        public bool IsTerminal { get; set; }
        public string NextActionRequired { get; set; } = string.Empty;
        public List<UserLifecycleStageNodeDto> Stages { get; set; } = new();
    }

    public class UserLifecycleStageNodeDto
    {
        public int StageIndex { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
        public string Summary { get; set; } = string.Empty;
        public Dictionary<string, object?> Meta { get; set; } = new();
    }

    public class UserBookingTimelineEventDto
    {
        public DateTime Timestamp { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class UserBookingLifecycleResult
    {
        public string CanonicalStatus { get; set; } = string.Empty;
        public string CanonicalStatusLabel { get; set; } = string.Empty;
        public UserLifecycleHierarchyDto Hierarchy { get; set; } = new();
        public List<UserBookingTimelineEventDto> Timeline { get; set; } = new();
        public object? PaymentBreakdown { get; set; }
        public object? CancellationAudit { get; set; }
    }

    public static class BookingLifecycleHelper
    {
        public static UserBookingLifecycleResult Build(
            string bookingType,
            int bookingId,
            string? bookingRef,
            string? pnr,
            string? bookingStatus,
            string? summary,
            DateTime? bookingDate,
            Payment? payment,
            SupplierFulfillmentExecution? exec,
            BookingCancellation? cancel,
            decimal totalFare,
            decimal cancellationCharges,
            decimal refundAmount)
        {
            var stages = new List<UserLifecycleStageNodeDto>();
            var timeline = new List<UserBookingTimelineEventDto>();

            decimal gwPaid = payment != null
                ? (payment.GatewayPaidAmount > 0 ? payment.GatewayPaidAmount : (payment.FinalPayableAmount - payment.WalletUsedAmount))
                : totalFare;
            decimal walletUsed = payment?.WalletUsedAmount ?? 0m;
            decimal totalPaid = payment != null ? (payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount) : totalFare;

            // ----------------------------------------------------
            // Gate 0: GATEWAY_PAYMENT
            // ----------------------------------------------------
            bool isPaymentSuccess = payment != null
                ? (string.Equals(payment.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase) ||
                   payment.PaidAt.HasValue)
                : (string.Equals(bookingStatus, "Booked", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(bookingStatus, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(bookingStatus, "Success", StringComparison.OrdinalIgnoreCase));

            bool isPaymentFailed = payment != null &&
                (string.Equals(payment.Status, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(payment.Status, "USER_DROPPED", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(payment.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase));

            string stage0Status = isPaymentSuccess ? "COMPLETED" : (isPaymentFailed ? "FAILED" : "PENDING");
            DateTime? stage0Time = isPaymentSuccess
                ? (payment?.PaidAt ?? payment?.UpdatedAt ?? bookingDate)
                : (isPaymentFailed ? (payment?.UpdatedAt ?? bookingDate) : (payment?.CreatedAt ?? bookingDate));

            string stage0Summary = isPaymentSuccess
                ? $"₹{totalPaid:F2} paid via {payment?.PaymentMethod ?? "Cashfree"} (Gateway: ₹{gwPaid:F2}, Wallet: ₹{walletUsed:F2})."
                : (isPaymentFailed
                    ? (payment?.FailureReason ?? payment?.LastError ?? "Payment declined or abandoned at gateway.")
                    : $"Order #{payment?.CashfreeOrderId ?? bookingRef ?? bookingId.ToString()} created for ₹{totalPaid:F2}. Awaiting payment.");

            stages.Add(new UserLifecycleStageNodeDto
            {
                StageIndex = 0,
                Key = "GATEWAY_PAYMENT",
                Name = "Payment Authorization",
                Status = stage0Status,
                Timestamp = stage0Time,
                Summary = stage0Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "orderId", payment?.CashfreeOrderId ?? bookingRef },
                    { "paymentId", payment?.CashfreePaymentId },
                    { "amount", totalPaid },
                    { "gatewayPaid", gwPaid },
                    { "walletUsed", walletUsed },
                    { "method", payment?.PaymentMethod ?? "Cashfree" },
                    { "isPaid", isPaymentSuccess }
                }
            });

            // Timeline: Order Created
            timeline.Add(new UserBookingTimelineEventDto
            {
                Timestamp = payment?.CreatedAt ?? bookingDate ?? DateTime.UtcNow,
                Stage = "ORDER_CREATED",
                Title = "Booking Order Created",
                Status = "COMPLETED",
                Description = $"{bookingType} booking order #{bookingRef ?? bookingId.ToString()} created for ₹{totalPaid:F2}."
            });

            // Timeline: Payment
            if (isPaymentSuccess)
            {
                timeline.Add(new UserBookingTimelineEventDto
                {
                    Timestamp = payment?.PaidAt ?? payment?.UpdatedAt ?? bookingDate ?? DateTime.UtcNow,
                    Stage = "PAYMENT_CAPTURED",
                    Title = $"Payment Verified ({payment?.PaymentMethod ?? "Cashfree"})",
                    Status = "COMPLETED",
                    Description = $"Payment of ₹{totalPaid:F2} confirmed. (Gateway: ₹{gwPaid:F2}, Wallet: ₹{walletUsed:F2})."
                });
            }
            else if (isPaymentFailed)
            {
                timeline.Add(new UserBookingTimelineEventDto
                {
                    Timestamp = payment?.UpdatedAt ?? DateTime.UtcNow,
                    Stage = "PAYMENT_FAILED",
                    Title = "Payment Failed",
                    Status = "FAILED",
                    Description = payment?.FailureReason ?? "Payment was declined or abandoned at gateway."
                });
            }

            // ----------------------------------------------------
            // Gate 1: SUPPLIER_DISPATCH
            // ----------------------------------------------------
            bool hasDispatched = exec != null ||
                                 (payment != null && !string.Equals(payment.FulfillmentStatus, "Pending", StringComparison.OrdinalIgnoreCase)) ||
                                 isPaymentSuccess;

            string stage1Status;
            DateTime? stage1Time = null;
            string stage1Summary;

            if (!isPaymentSuccess)
            {
                stage1Status = "SKIPPED";
                stage1Summary = "Awaiting payment authorization before supplier dispatch.";
            }
            else if (hasDispatched)
            {
                stage1Status = "COMPLETED";
                stage1Time = exec?.CreatedAt ?? payment?.UpdatedAt ?? bookingDate;
                stage1Summary = $"Dispatched {bookingType} reservation to SRDV supplier network.";
            }
            else
            {
                stage1Status = "IN_PROGRESS";
                stage1Time = payment?.PaidAt ?? bookingDate;
                stage1Summary = "Payment confirmed. Queued for supplier booking dispatch.";
            }

            stages.Add(new UserLifecycleStageNodeDto
            {
                StageIndex = 1,
                Key = "SUPPLIER_DISPATCH",
                Name = "Supplier Dispatch",
                Status = stage1Status,
                Timestamp = stage1Time,
                Summary = stage1Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "bookingType", bookingType },
                    { "bookingId", bookingId }
                }
            });

            // ----------------------------------------------------
            // Gate 2: SUPPLIER_CONFIRMATION
            // ----------------------------------------------------
            bool isConfirmed = string.Equals(bookingStatus, "Booked", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(bookingStatus, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(exec?.SupplierBookingStatus, "Success", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(exec?.SupplierBookingStatus, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(payment?.FulfillmentStatus, "Success", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(payment?.FulfillmentStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase);

            bool isFulfillFailed = (payment != null && payment.FulfillmentStatus != null && payment.FulfillmentStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)) ||
                                   (exec != null && exec.SupplierBookingStatus != null && exec.SupplierBookingStatus.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)) ||
                                   string.Equals(bookingStatus, "Failed", StringComparison.OrdinalIgnoreCase);

            string stage2Status;
            DateTime? stage2Time = null;
            string stage2Summary;

            if (!isPaymentSuccess)
            {
                stage2Status = "SKIPPED";
                stage2Summary = "Skipped because payment was not completed.";
            }
            else if (isConfirmed)
            {
                stage2Status = "COMPLETED";
                stage2Time = exec?.UpdatedAt ?? payment?.UpdatedAt ?? bookingDate;
                stage2Summary = $"Confirmed with supplier. PNR / Booking Ref: {pnr ?? bookingRef ?? "Confirmed"}.";

                timeline.Add(new UserBookingTimelineEventDto
                {
                    Timestamp = stage2Time.Value,
                    Stage = "SRDV_CONFIRMED",
                    Title = "Reservation Confirmed",
                    Status = "COMPLETED",
                    Description = $"Supplier confirmed booking. PNR: {pnr ?? bookingRef ?? "Confirmed"}."
                });
            }
            else if (isFulfillFailed)
            {
                stage2Status = "FAILED";
                stage2Time = exec?.UpdatedAt ?? payment?.UpdatedAt ?? bookingDate;
                stage2Summary = exec?.LastError ?? payment?.LastError ?? "Supplier fulfillment failed during confirmation.";

                timeline.Add(new UserBookingTimelineEventDto
                {
                    Timestamp = stage2Time.Value,
                    Stage = "SRDV_FAILED",
                    Title = "Supplier Fulfillment Failed",
                    Status = "FAILED",
                    Description = stage2Summary
                });
            }
            else
            {
                stage2Status = "IN_PROGRESS";
                stage2Time = exec?.CreatedAt ?? payment?.UpdatedAt ?? bookingDate;
                stage2Summary = "Supplier is confirming inventory and issuing PNR/Ticket.";
            }

            stages.Add(new UserLifecycleStageNodeDto
            {
                StageIndex = 2,
                Key = "SUPPLIER_CONFIRMATION",
                Name = "Ticket Confirmation & PNR",
                Status = stage2Status,
                Timestamp = stage2Time,
                Summary = stage2Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "pnr", pnr },
                    { "bookingRef", bookingRef },
                    { "status", bookingStatus }
                }
            });

            // ----------------------------------------------------
            // Gate 3: POST_BOOKING_LIFECYCLE
            // ----------------------------------------------------
            bool isCancelled = cancel != null ||
                               string.Equals(bookingStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(bookingStatus, "Partially Cancelled", StringComparison.OrdinalIgnoreCase) ||
                               (payment != null && (string.Equals(payment.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase) || string.Equals(payment.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase)));

            string stage3Status;
            DateTime? stage3Time = null;
            string stage3Summary;

            if (!isPaymentSuccess || isFulfillFailed)
            {
                stage3Status = "SKIPPED";
                stage3Summary = "Not applicable (booking was not established).";
            }
            else if (!isConfirmed)
            {
                stage3Status = "PENDING";
                stage3Summary = "Awaiting ticket confirmation.";
            }
            else if (isCancelled)
            {
                stage3Status = "COMPLETED";
                stage3Time = cancel?.CreatedAtUtc ?? payment?.UpdatedAt ?? DateTime.UtcNow;
                stage3Summary = $"Booking cancelled. Cancellation charge: ₹{cancellationCharges:F2}, Eligible refund: ₹{refundAmount:F2}.";

                timeline.Add(new UserBookingTimelineEventDto
                {
                    Timestamp = stage3Time.Value,
                    Stage = "CANCELLATION",
                    Title = "Booking Cancelled",
                    Status = "COMPLETED",
                    Description = $"Booking cancellation recorded. Fee: ₹{cancellationCharges:F2}, Refund eligible: ₹{refundAmount:F2}."
                });
            }
            else
            {
                stage3Status = "COMPLETED";
                stage3Time = exec?.UpdatedAt ?? payment?.PaidAt ?? bookingDate;
                stage3Summary = "Your reservation is confirmed and active for travel.";
            }

            stages.Add(new UserLifecycleStageNodeDto
            {
                StageIndex = 3,
                Key = "POST_BOOKING_LIFECYCLE",
                Name = isCancelled ? "Booking Cancellation" : "Active Reservation",
                Status = stage3Status,
                Timestamp = stage3Time,
                Summary = stage3Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "isCancelled", isCancelled },
                    { "cancellationCharges", cancellationCharges },
                    { "eligibleRefundAmount", refundAmount }
                }
            });

            // ----------------------------------------------------
            // Gate 4: REFUND_SETTLEMENT
            // ----------------------------------------------------
            bool isRefundDone = (payment != null && (string.Equals(payment.RefundStatus, "Refunded", StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(payment.RefundStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(payment.Status, "REFUNDED", StringComparison.OrdinalIgnoreCase))) ||
                                (cancel != null && string.Equals(cancel.RefundStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase));

            bool isRefundOnHold = (payment != null && string.Equals(payment.RefundStatus, "RefundOnHold", StringComparison.OrdinalIgnoreCase)) ||
                                  (cancel != null && string.Equals(cancel.RefundStatus, "ON_HOLD", StringComparison.OrdinalIgnoreCase));

            bool isRefundProcessing = (payment != null && (string.Equals(payment.RefundStatus, "RefundProcessing", StringComparison.OrdinalIgnoreCase) ||
                                                          string.Equals(payment.RefundStatus, "PROCESSING", StringComparison.OrdinalIgnoreCase) ||
                                                          string.Equals(payment.RefundStatus, "Pending", StringComparison.OrdinalIgnoreCase))) ||
                                      (cancel != null && string.Equals(cancel.RefundStatus, "PROCESSING", StringComparison.OrdinalIgnoreCase));

            bool isRefundFailed = (payment != null && string.Equals(payment.RefundStatus, "RefundFailed", StringComparison.OrdinalIgnoreCase)) ||
                                  (cancel != null && string.Equals(cancel.RefundStatus, "FAILED", StringComparison.OrdinalIgnoreCase));

            string stage4Status;
            DateTime? stage4Time = null;
            string stage4Summary;

            if (!isCancelled && !isFulfillFailed)
            {
                stage4Status = "SKIPPED";
                stage4Summary = "Not applicable (active booking, no refund requested).";
            }
            else if (isRefundDone)
            {
                stage4Status = "COMPLETED";
                stage4Time = cancel?.CompletedAtUtc ?? payment?.UpdatedAt ?? DateTime.UtcNow;
                decimal settledAmt = refundAmount > 0 ? refundAmount : totalPaid;
                stage4Summary = $"Refund of ₹{settledAmt:F2} settled to your source payment method. Ref: {payment?.RefundId ?? cancel?.CashfreeRefundId ?? "Settled"}.";

                timeline.Add(new UserBookingTimelineEventDto
                {
                    Timestamp = stage4Time.Value,
                    Stage = "REFUND_COMPLETED",
                    Title = "Refund Settled",
                    Status = "COMPLETED",
                    Description = $"Refund of ₹{settledAmt:F2} successfully settled. Gateway Ref: {payment?.RefundId ?? cancel?.CashfreeRefundId ?? "Completed"}."
                });
            }
            else if (isRefundOnHold)
            {
                stage4Status = "WARNING";
                stage4Time = payment?.UpdatedAt ?? DateTime.UtcNow;
                stage4Summary = "Refund is queued with payment gateway and will be released shortly.";

                timeline.Add(new UserBookingTimelineEventDto
                {
                    Timestamp = stage4Time.Value,
                    Stage = "REFUND_ONHOLD",
                    Title = "Refund In Queue",
                    Status = "WARNING",
                    Description = "Refund is being processed with the merchant gateway."
                });
            }
            else if (isRefundProcessing)
            {
                stage4Status = "IN_PROGRESS";
                stage4Time = payment?.UpdatedAt ?? DateTime.UtcNow;
                stage4Summary = $"Refund of ₹{refundAmount:F2} is processing with your payment provider.";

                timeline.Add(new UserBookingTimelineEventDto
                {
                    Timestamp = stage4Time.Value,
                    Stage = "REFUND_PROCESSING",
                    Title = "Refund Processing",
                    Status = "PROCESSING",
                    Description = $"Refund of ₹{refundAmount:F2} submitted to payment gateway."
                });
            }
            else if (isRefundFailed)
            {
                stage4Status = "FAILED";
                stage4Time = payment?.UpdatedAt ?? DateTime.UtcNow;
                stage4Summary = payment?.LastError ?? "Refund encountered an issue. Support team is investigating.";
            }
            else
            {
                stage4Status = "PENDING";
                stage4Time = payment?.UpdatedAt ?? DateTime.UtcNow;
                stage4Summary = $"Eligible refund amount: ₹{refundAmount:F2}. Queued for settlement.";
            }

            stages.Add(new UserLifecycleStageNodeDto
            {
                StageIndex = 4,
                Key = "REFUND_SETTLEMENT",
                Name = "Refund Settlement",
                Status = stage4Status,
                Timestamp = stage4Time,
                Summary = stage4Summary,
                Meta = new Dictionary<string, object?>
                {
                    { "refundId", payment?.RefundId ?? cancel?.CashfreeRefundId },
                    { "refundStatus", payment?.RefundStatus ?? cancel?.RefundStatus },
                    { "refundAmount", refundAmount > 0 ? refundAmount : (isRefundDone ? totalPaid : 0m) }
                }
            });

            // ----------------------------------------------------
            // Canonical Status Synthesis
            // ----------------------------------------------------
            string canonicalStatus;
            string canonicalLabel;
            int currentStageIndex;
            string currentStageKey;
            string currentStageName;
            bool isTerminal;
            string nextAction;

            if (isPaymentFailed)
            {
                canonicalStatus = "PAYMENT_FAILED";
                canonicalLabel = "Payment Failed / Dropped";
                currentStageIndex = 0;
                currentStageKey = "GATEWAY_PAYMENT";
                currentStageName = "Payment Authorization";
                isTerminal = true;
                nextAction = "None (Customer abandoned or bank declined)";
            }
            else if (!isPaymentSuccess)
            {
                canonicalStatus = "PAYMENT_PENDING";
                canonicalLabel = "Payment Pending";
                currentStageIndex = 0;
                currentStageKey = "GATEWAY_PAYMENT";
                currentStageName = "Payment Authorization";
                isTerminal = false;
                nextAction = "Awaiting customer to complete payment";
            }
            else if (isFulfillFailed)
            {
                currentStageIndex = 4;
                currentStageKey = "REFUND_SETTLEMENT";
                currentStageName = "Refund Settlement";

                if (isRefundDone)
                {
                    canonicalStatus = "BOOKING_FAILED_REFUNDED";
                    canonicalLabel = "Booking Failed & Refunded";
                    isTerminal = true;
                    nextAction = "None (Refund fully settled)";
                }
                else if (isRefundOnHold)
                {
                    canonicalStatus = "REFUND_ON_HOLD";
                    canonicalLabel = "Refund Processing (Gateway Queue)";
                    isTerminal = false;
                    nextAction = "Gateway balance replenishment in progress";
                }
                else if (isRefundProcessing)
                {
                    canonicalStatus = "BOOKING_FAILED_REFUND_PROCESSING";
                    canonicalLabel = "Booking Failed - Refund Processing";
                    isTerminal = false;
                    nextAction = "Awaiting bank / gateway settlement";
                }
                else
                {
                    canonicalStatus = "BOOKING_FAILED_REFUND_PENDING";
                    canonicalLabel = "Booking Failed - Refund Pending";
                    isTerminal = false;
                    nextAction = "Awaiting automated refund dispatch";
                }
            }
            else if (!isConfirmed)
            {
                canonicalStatus = "PAYMENT_SUCCESS_FULFILLING";
                canonicalLabel = "Paid - Fulfilling with Supplier";
                currentStageIndex = 1;
                currentStageKey = "SUPPLIER_DISPATCH";
                currentStageName = "Supplier Dispatch";
                isTerminal = false;
                nextAction = "Awaiting supplier confirmation";
            }
            else if (isCancelled)
            {
                currentStageIndex = 4;
                currentStageKey = "REFUND_SETTLEMENT";
                currentStageName = "Refund Settlement";

                if (isRefundDone)
                {
                    canonicalStatus = "CANCELLED_AND_REFUNDED";
                    canonicalLabel = "Cancelled & Refunded";
                    isTerminal = true;
                    nextAction = "None (Refund settled to source account)";
                }
                else if (isRefundOnHold)
                {
                    canonicalStatus = "REFUND_ON_HOLD";
                    canonicalLabel = "Refund Queued";
                    isTerminal = false;
                    nextAction = "Refund queued with gateway";
                }
                else if (isRefundProcessing)
                {
                    canonicalStatus = "CANCELLED_REFUND_PROCESSING";
                    canonicalLabel = "Cancelled - Refund Processing";
                    isTerminal = false;
                    nextAction = "Processing refund with gateway";
                }
                else
                {
                    canonicalStatus = "CANCELLED_REFUND_PENDING";
                    canonicalLabel = "Cancelled - Refund Pending";
                    isTerminal = false;
                    nextAction = "Queued for refund initiation";
                }
            }
            else
            {
                canonicalStatus = "BOOKING_CONFIRMED";
                canonicalLabel = "Confirmed & Active";
                currentStageIndex = 3;
                currentStageKey = "POST_BOOKING_LIFECYCLE";
                currentStageName = "Active Reservation";
                isTerminal = true;
                nextAction = "None (Ticket active for travel)";
            }

            var hierarchy = new UserLifecycleHierarchyDto
            {
                CanonicalStatus = canonicalStatus,
                CanonicalStatusLabel = canonicalLabel,
                CurrentStageIndex = currentStageIndex,
                CurrentStageKey = currentStageKey,
                CurrentStageName = currentStageName,
                TotalStages = stages.Count,
                IsTerminal = isTerminal,
                NextActionRequired = nextAction,
                Stages = stages
            };

            var paymentBreakdown = new
            {
                PaymentId = payment?.Id,
                TotalAmount = totalPaid,
                GatewayPaidAmount = gwPaid,
                WalletUsedAmount = walletUsed,
                PaymentMethod = payment?.PaymentMethod ?? "Cashfree",
                PaymentReference = payment?.PaymentReference ?? bookingRef,
                CashfreeOrderId = payment?.CashfreeOrderId,
                CashfreePaymentId = payment?.CashfreePaymentId,
                PaidAt = payment?.PaidAt
            };

            var cancellationAudit = isCancelled ? new
            {
                IsCancelled = true,
                CancellationCharges = cancellationCharges,
                CustomerRefundAmount = refundAmount,
                RefundStatus = cancel?.RefundStatus ?? payment?.RefundStatus,
                CashfreeRefundId = cancel?.CashfreeRefundId ?? payment?.RefundId,
                CancelledAt = cancel?.CreatedAtUtc
            } : null;

            return new UserBookingLifecycleResult
            {
                CanonicalStatus = canonicalStatus,
                CanonicalStatusLabel = canonicalLabel,
                Hierarchy = hierarchy,
                Timeline = timeline,
                PaymentBreakdown = paymentBreakdown,
                CancellationAudit = cancellationAudit
            };
        }
    }
}
