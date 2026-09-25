using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Payments;
using System;
using System.Linq;
using System.Threading.Tasks;

using PickNBook.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace PickNBook.Api.Controllers.Admin
{
    [Route("api/admin/payments")]
    public class AdminPaymentsController : AdminApiController
    {
        private readonly AppDbContext _dbContext;
        private readonly ICashfreeService _cashfreeService;
        private readonly ILogger<AdminPaymentsController> _logger;

        public AdminPaymentsController(
            AppDbContext dbContext,
            ICashfreeService cashfreeService,
            ILogger<AdminPaymentsController> logger)
        {
            _dbContext = dbContext;
            _cashfreeService = cashfreeService;
            _logger = logger;
        }

        /// <summary>
        /// Get high-level summary metrics for payments dashboard.
        /// </summary>
        [HttpGet("metrics")]
        public async Task<IActionResult> GetPaymentMetrics()
        {
            var payments = await _dbContext.Payments.AsNoTracking().ToListAsync();

            var totalRevenue = payments.Where(p => p.Status == "SUCCESS" || p.Status == "Success").Sum(p => p.FinalPayableAmount);
            var totalPayments = payments.Count;
            var successfulPayments = payments.Count(p => p.Status == "SUCCESS" || p.Status == "Success");
            var failedPayments = payments.Count(p => p.Status != "SUCCESS" && p.Status != "Success" && p.Status != "PENDING" && p.Status != "Pending" && p.Status != "Created");
            var pendingPayments = payments.Count(p => p.Status == "PENDING" || p.Status == "Pending" || p.Status == "Created");
            var pendingRefunds = payments.Count(p => p.RefundStatus == "PENDING" || p.RefundStatus == "PROCESSING" || p.RefundStatus == "RefundProcessing" || p.RefundStatus == "RefundOnHold");
            var completedRefunds = payments.Count(p => p.RefundStatus == "COMPLETED" || p.RefundStatus == "SUCCESS" || p.RefundStatus == "Refunded");

            return Ok(new
            {
                success = true,
                data = new
                {
                    totalRevenue = Math.Round(totalRevenue, 2, MidpointRounding.AwayFromZero),
                    totalPayments,
                    successfulPayments,
                    failedPayments,
                    pendingPayments,
                    pendingRefunds,
                    completedRefunds
                }
            });
        }

        /// <summary>
        /// Get paginated and filtered payments list for Admin Portal table.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPayments(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? bookingType = null,
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 100) pageSize = 20;

            var query = _dbContext.Payments.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p => p.Status.ToLower() == status.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(bookingType) && !bookingType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p => p.BookingType.ToLower() == bookingType.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(p => p.PaymentReference.ToLower().Contains(s) ||
                                         p.CashfreeOrderId.ToLower().Contains(s) ||
                                         p.UserId.ToLower().Contains(s) ||
                                         (p.CustomerName != null && p.CustomerName.ToLower().Contains(s)) ||
                                         (p.CustomerPhone != null && p.CustomerPhone.Contains(s)) ||
                                         (p.CustomerEmail != null && p.CustomerEmail.ToLower().Contains(s)) ||
                                         (p.CashfreePaymentId != null && p.CashfreePaymentId.ToLower().Contains(s)));
            }

            if (fromDate.HasValue)
            {
                query = query.Where(p => p.CreatedAt >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(p => p.CreatedAt <= endOfDay);
            }

            var totalRecords = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

            var items = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    p.PaymentReference,
                    p.CashfreeOrderId,
                    p.CashfreePaymentId,
                    p.UserId,
                    p.CustomerName,
                    p.CustomerEmail,
                    p.CustomerPhone,
                    p.PassengerCount,
                    p.BookingType,
                    p.BookingId,
                    p.OriginalAmount,
                    p.MarkupAmount,
                    p.ConvenienceFee,
                    p.DiscountAmount,
                    p.FinalPayableAmount,
                    p.Currency,
                    p.Status,
                    p.FulfillmentStatus,
                    p.PaymentMethod,
                    p.RefundStatus,
                    p.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                page,
                pageSize,
                totalRecords,
                totalPages,
                data = items
            });
        }

        /// <summary>
        /// Get single payment breakdown details by ID.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetPaymentById(int id)
        {
            var payment = await _dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null)
            {
                return NotFound(new { success = false, message = "Payment record not found." });
            }

            return Ok(new
            {
                success = true,
                data = payment
            });
        }

        /// <summary>
        /// Dispatch refund request to Cashfree gateway or check live refund status for a payment record.
        /// </summary>
        [HttpPost("{id:int}/refund")]
        public async Task<IActionResult> InitiateRefund(int id, [FromBody] AdminRefundRequestDto req)
        {
            var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null)
            {
                return NotFound(new { success = false, message = "Payment record not found." });
            }

            if (payment.RefundStatus == "Refunded" || payment.Status == "REFUNDED")
            {
                return BadRequest(new { success = false, message = "Payment has already been refunded." });
            }

            if (payment.Status != "Success" && payment.Status != "PAID" && payment.Status != "SUCCESS")
            {
                return BadRequest(new { success = false, message = "Cannot refund an unpaid or failed payment." });
            }

            decimal refundAmount = req.RefundAmount.HasValue && req.RefundAmount.Value > 0
                ? req.RefundAmount.Value
                : payment.FinalPayableAmount;

            string refundReason = !string.IsNullOrWhiteSpace(req.RefundReason)
                ? req.RefundReason.Trim()
                : (payment.RefundReason ?? "Admin initiated refund");

            string refundId = payment.RefundId ?? $"REF-{payment.CashfreeOrderId}";

            try
            {
                System.Text.Json.JsonDocument? refundResponse = null;
                try
                {
                    refundResponse = await _cashfreeService.InitiateRefundAsync(
                        payment.CashfreeOrderId,
                        refundAmount,
                        refundId,
                        refundReason);
                }
                catch (Exception initEx)
                {
                    _logger.LogWarning(initEx, "InitiateRefund on Cashfree returned error, attempting to check live refund status for order {OrderId}", payment.CashfreeOrderId);
                    try
                    {
                        refundResponse = await _cashfreeService.GetRefundStatusAsync(payment.CashfreeOrderId, refundId);
                    }
                    catch
                    {
                        throw initEx;
                    }
                }

                string cashfreeRefundStatus = "PENDING";
                string? statusDescription = null;
                if (refundResponse.RootElement.TryGetProperty("refund_status", out var stEl))
                {
                    cashfreeRefundStatus = stEl.GetString() ?? "PENDING";
                }
                if (refundResponse.RootElement.TryGetProperty("status_description", out var descEl))
                {
                    statusDescription = descEl.GetString();
                }

                payment.RefundId = refundId;
                payment.RefundReason = refundReason;
                payment.UpdatedAt = DateTime.UtcNow;

                if (cashfreeRefundStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                {
                    payment.RefundStatus = "Refunded";
                    payment.Status = "REFUNDED";
                    payment.LastError = null;
                }
                else if (cashfreeRefundStatus.Equals("ONHOLD", StringComparison.OrdinalIgnoreCase))
                {
                    payment.RefundStatus = "RefundOnHold";
                    payment.LastError = statusDescription ?? "Refund on hold because of insufficient account balance";
                }
                else if (cashfreeRefundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) || cashfreeRefundStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    payment.RefundStatus = "RefundFailed";
                    payment.LastError = statusDescription ?? "Cashfree refund failed/cancelled.";
                }
                else
                {
                    payment.RefundStatus = "RefundProcessing";
                    payment.LastError = statusDescription;
                }

                await _dbContext.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = payment.RefundStatus == "RefundOnHold"
                        ? "Refund initiated but placed ONHOLD by Cashfree due to insufficient merchant account balance. Please recharge your Cashfree account."
                        : "Refund processed with Cashfree.",
                    data = new
                    {
                        paymentId = payment.Id,
                        refundId = payment.RefundId,
                        refundStatus = payment.RefundStatus,
                        refundReason = payment.RefundReason,
                        statusDescription = payment.LastError,
                        gatewayStatus = cashfreeRefundStatus
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin failed to dispatch refund for Payment {PaymentId}", id);
                payment.LastError = ex.Message;
                payment.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to dispatch refund to Cashfree: " + ex.Message
                });
            }
        }
    }

    public class AdminRefundRequestDto
    {
        public decimal? RefundAmount { get; set; }
        public string? RefundReason { get; set; }
    }
}
