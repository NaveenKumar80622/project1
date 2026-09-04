using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Infrastructure.Logging;
using PickNBook.Api.Models.Config;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace PickNBook.Api.Middleware;

public class HotelRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HotelRequestLoggingMiddleware> _logger;
    private readonly IOptionsMonitor<PayloadLoggingOptions> _optionsMonitor;

    public HotelRequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<HotelRequestLoggingMiddleware> logger,
        IOptionsMonitor<PayloadLoggingOptions> optionsMonitor)
    {
        _next = next;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _optionsMonitor.CurrentValue;

        // 1. Generate or retrieve Correlation ID
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var cid) 
            ? cid.ToString() 
            : Guid.NewGuid().ToString();

        CorrelationIdContext.CorrelationId = correlationId;

        // 2. Read Request Body
        context.Request.EnableBuffering();
        var requestBody = await ReadStreamAsync(context.Request.Body);

        var stopwatch = Stopwatch.StartNew();
        
        // T1: Request Received
        string formattedRequestBody = JsonPayloadFormatter.Format(requestBody, options.Mode, options.MaxPayloadLength);
        _logger.LogInformation(
            "[{CorrelationId}] [T1] Hotel Request Received:\nMethod: {Method}\nPath: {Path}\nPayload:\n{Payload}\n--------------------------------------------------", 
            correlationId, context.Request.Method, context.Request.Path, formattedRequestBody);

        // 3. Intercept Response Body
        bool isLargePayloadEndpoint = context.Request.Path.Value != null && 
            (context.Request.Path.Value.Contains("search", StringComparison.OrdinalIgnoreCase) || 
             context.Request.Path.Value.Contains("gethotelroom", StringComparison.OrdinalIgnoreCase) ||
             context.Request.Path.Value.Contains("gethotelinfo", StringComparison.OrdinalIgnoreCase));

        bool shouldOmit = isLargePayloadEndpoint && options.Mode == PayloadLoggingMode.Omit;

        var originalBodyStream = context.Response.Body;
        MemoryStream? responseBodyStream = null;
        
        if (!shouldOmit)
        {
            responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;
        }

        try
        {
            await _next(context);
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[{CorrelationId}] Exception occurred during hotel request processing after {ElapsedMs}ms", 
                correlationId, stopwatch.ElapsedMilliseconds);
            
            if (!shouldOmit)
            {
                context.Response.Body = originalBodyStream;
            }
            throw; 
        }

        // T4: Response Sent
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        string formattedResponseBody;

        if (!shouldOmit && responseBodyStream != null)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseBodyText = await new StreamReader(context.Response.Body).ReadToEndAsync();
            context.Response.Body.Seek(0, SeekOrigin.Begin);

            await responseBodyStream.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;

            formattedResponseBody = JsonPayloadFormatter.Format(responseBodyText, options.Mode, options.MaxPayloadLength);
        }
        else
        {
            formattedResponseBody = "[Response payload omitted for performance]";
        }

        _logger.LogInformation(
            "[{CorrelationId}] [T4] Hotel Response Sent:\nStatus: {StatusCode}\nBackend Processing Time: {ElapsedMs}ms\nPayload:\n{Payload}\n==================================================", 
            correlationId, context.Response.StatusCode, elapsedMs, formattedResponseBody);

        _logger.LogInformation("[{CorrelationId}] Total End-to-End Hotel Time (T4 - T1): {ElapsedMs}ms", correlationId, elapsedMs);
    }

    private static async Task<string> ReadStreamAsync(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        stream.Position = 0;
        return content;
    }
}
