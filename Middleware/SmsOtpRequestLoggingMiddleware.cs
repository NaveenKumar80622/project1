using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Infrastructure.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PickNBook.Api.Middleware;

public class SmsOtpRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SmsOtpRequestLoggingMiddleware> _logger;

    public SmsOtpRequestLoggingMiddleware(RequestDelegate next, ILogger<SmsOtpRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate or read Correlation ID
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var cid)
            ? cid.ToString()
            : Guid.NewGuid().ToString();

        CorrelationIdContext.CorrelationId = correlationId;

        // ── T1: Log inbound request ───────────────────────────────────────────
        // EnableBuffering allows downstream (AuthController) to re-read the body
        context.Request.EnableBuffering();
        var requestBody = await ReadBodyAsync(context.Request.Body);

        _logger.LogInformation(
            "[{CorrelationId}] [T1] SMS OTP Request Received:\nMethod: {Method}\nPath: {Path}\nPayload:\n{Payload}\n--------------------------------------------------",
            correlationId, context.Request.Method, context.Request.Path, requestBody);

        // ── Intercept response body ───────────────────────────────────────────
        var originalBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "[{CorrelationId}] Exception during SMS OTP request processing after {ElapsedMs}ms",
                correlationId, stopwatch.ElapsedMilliseconds);
            context.Response.Body = originalBodyStream;
            throw;
        }

        // ── T4: Log outbound response ─────────────────────────────────────────
        responseBodyStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();
        responseBodyStream.Seek(0, SeekOrigin.Begin);

        // Copy intercepted response back to original stream so client receives it
        await responseBodyStream.CopyToAsync(originalBodyStream);
        context.Response.Body = originalBodyStream;

        _logger.LogInformation(
            "[{CorrelationId}] [T4] OTP Response Sent to Client:\n  Status : {StatusCode}\n  Payload: {Payload}\n==================================================",
            correlationId, context.Response.StatusCode, responseBody);

        _logger.LogInformation(
            "[{CorrelationId}] Total End-to-End SMS OTP Time (T4-T1): {ElapsedMs}ms",
            correlationId, stopwatch.ElapsedMilliseconds);
    }

    private static async Task<string> ReadBodyAsync(Stream body)
    {
        body.Position = 0;
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        body.Position = 0; // Reset so downstream can read again
        return content;
    }
}
