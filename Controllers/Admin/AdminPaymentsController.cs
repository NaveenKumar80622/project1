using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Payments;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers.Admin
{
    [Route("api/admin/payments")]
    public class AdminPaymentsController : AdminApiController
    {
        private readonly AppDbContext _dbContext;

        public AdminPaymentsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Get high-level summary metrics for payments dashboard.
        /// </summary>
        [HttpGet("metrics")]
        public async Task<IActionResult> GetPaymentMetrics()
        {
            var payments = await _dbContext.Payments.AsNoTracking().ToListAsync();

            var totalRevenue = payments.Where(p => p.Status == "SUCCESS").Sum(p => p.FinalPayableAmount);
            var totalPayments = payments.Count;
            var successfulPayments = payments.Count(p => p.Status == "SUCCESS");
            var failedPayments = payments.Count(p => p.Status != "SUCCESS" && p.Status != "PENDING");
            var pendingPayments = payments.Count(p => p.Status == "PENDING" || p.Status == "Created");
            var pendingRefunds = payments.Count(p => p.RefundStatus == "PENDING" || p.RefundStatus == "PROCESSING");
            var completedRefunds = payments.Count(p => p.RefundStatus == "COMPLETED" || p.RefundStatus == "SUCCESS");

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

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status.ToLower() == status.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(bookingType))
            {
                query = query.Where(p => p.BookingType.ToLower() == bookingType.Trim().ToLower());
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(p => p.PaymentReference.ToLower().Contains(s) ||
                                         p.CashfreeOrderId.ToLower().Contains(s) ||
                                         p.UserId.ToLower().Contains(s) ||
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
        /// Update refund status or initiate refund for a payment record.
        /// </summary>
        [HttpPost("{id:int}/refund")]
        public async Task<IActionResult> InitiateRefund(int id, [FromBody] AdminRefundRequestDto req)
        {
            var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null)
            {
                return NotFound(new { success = false, message = "Payment record not found." });
            }

            payment.RefundStatus = "PENDING";
            payment.RefundReason = req.RefundReason?.Trim() ?? "Admin initiated refund";
            payment.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Refund status updated successfully.",
                data = new
                {
                    paymentId = payment.Id,
                    refundStatus = payment.RefundStatus,
                    refundReason = payment.RefundReason
                }
            });
        }
    }

    public class AdminRefundRequestDto
    {
        public decimal? RefundAmount { get; set; }
        public string? RefundReason { get; set; }
    }
}
