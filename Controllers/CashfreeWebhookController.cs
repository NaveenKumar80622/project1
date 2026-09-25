using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Models.Payments;
using PickNBook.Api.Services.Interfaces;
using System.Text.Json;
using PickNBook.Api.Models;

using Microsoft.Extensions.Options;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    [Route("api/cashfree")]
    public class CashfreeWebhookController : ControllerBase
    {
        private readonly ICashfreeService _cashfreeService;
        private readonly IPaymentService _paymentService;
        private readonly ILogger<CashfreeWebhookController> _logger;
        private readonly CashfreeSettings _settings;

        public CashfreeWebhookController(
            ICashfreeService cashfreeService,
            IPaymentService paymentService,
            IOptions<CashfreeSettings> options,
            ILogger<CashfreeWebhookController> logger)
        {
            _cashfreeService = cashfreeService;
            _paymentService = paymentService;
            _settings = options.Value;
            _logger = logger;
        }

        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook()
        {
            try
            {
                Request.EnableBuffering();
                using var reader = new StreamReader(Request.Body);
                string rawBody = await reader.ReadToEndAsync();

                Request.Headers.TryGetValue("x-webhook-timestamp", out var timestampHeader);
                Request.Headers.TryGetValue("x-webhook-signature", out var signatureHeader);
                Request.Headers.TryGetValue("x-webhook-version", out var versionHeader);

                string timestamp = timestampHeader.ToString();
                string signature = signatureHeader.ToString();
                string version = versionHeader.ToString();

                if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
                {
                    _logger.LogWarning("Webhook missing Cashfree signature headers.");
                    return Unauthorized(new { message = "Missing signature headers" });
                }

                // Webhook version is intentionally NOT compared against ApiVersion 
                // since Webhook (e.g. 2023-08-01) and API (e.g. 2026-01-01) versions can be correctly divergent.

                if (!_cashfreeService.VerifyWebhookSignature(rawBody, timestamp, signature))
                {
                    _logger.LogWarning("Webhook signature verification failed.");
                    return Unauthorized(new { message = "Invalid signature" });
                }

                var payload = JsonSerializer.Deserialize<CashfreeWebhookPayload>(rawBody);
                if (payload == null)
                {
                    _logger.LogWarning("Webhook payload deserialization returned null.");
                    return Ok(new { message = "Invalid payload - ignored" });
                }

                if (payload.Type == "REFUND_STATUS_WEBHOOK")
                {
                    if (payload.Data?.Refund == null)
                    {
                        _logger.LogWarning("Refund Webhook payload missing refund data.");
                        return Ok(new { message = "Invalid refund payload - ignored" });
                    }

                    string refundId = payload.Data.Refund.RefundId;
                    string refundStatus = payload.Data.Refund.RefundStatus;

                    bool refundProcessed = await _paymentService.ProcessRefundWebhookAsync(refundId, refundStatus);
                    return Ok(new { message = refundProcessed ? "Refund webhook processed" : "Refund webhook ignored" });
                }

                if (payload.Data?.Order == null || payload.Data.Payment == null)
                {
                    _logger.LogWarning("Webhook payload missing order/payment data.");
                    return Ok(new { message = "Invalid payload - ignored" });
                }

                string orderId = payload.Data.Order.OrderId;
                string paymentStatus = payload.Data.Payment.PaymentStatus;
                decimal amount = payload.Data.Payment.PaymentAmount;
                string paymentId = payload.Data.Payment.CfPaymentId;
                string paymentMethod = payload.Data.Payment.PaymentMethod?.GetMethodName();
                string? paymentMessage = payload.Data.Payment.PaymentMessage;

                bool processed = await _paymentService.ProcessWebhookAsync(orderId, payload.Type, paymentStatus, amount, paymentId, paymentMethod, paymentMessage);
                
                // Return 200 OK regardless so Cashfree doesn't retry endlessly, as long as signature was valid.
                return Ok(new { message = processed ? "Webhook processed" : "Webhook ignored" });
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException 
                                       || ex is TimeoutException
                                       || (ex is Microsoft.EntityFrameworkCore.DbUpdateException && ex.InnerException is System.Net.Sockets.SocketException))
            {
                // Transient infrastructure failures — return 500 so Cashfree retries
                _logger.LogError(ex, "Transient error processing Cashfree webhook. Cashfree will retry.");
                return StatusCode(500, new { message = "Transient processing failure" });
            }
            catch (Exception ex)
            {
                // Permanent/business logic errors — return 200 to prevent infinite Cashfree retries
                _logger.LogError(ex, "Permanent error processing Cashfree webhook. Returning 200 to prevent retry storm.");
                return Ok(new { message = "Webhook processing error - logged for investigation" });
            }
        }
    }
}
