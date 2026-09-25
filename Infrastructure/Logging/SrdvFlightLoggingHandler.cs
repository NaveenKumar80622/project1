using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickNBook.Api.Models.Config;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace PickNBook.Api.Infrastructure.Logging;

public class SrdvFlightLoggingHandler : DelegatingHandler
{
    private readonly ILogger<SrdvFlightLoggingHandler> _logger;
    private readonly IOptionsMonitor<PayloadLoggingOptions> _optionsMonitor;

    public SrdvFlightLoggingHandler(
        ILogger<SrdvFlightLoggingHandler> logger,
        IOptionsMonitor<PayloadLoggingOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;

        var correlationId = CorrelationIdContext.CorrelationId;
        if (string.IsNullOrEmpty(correlationId))
        {
            correlationId = "NO-CORRELATION-ID";
        }

        string requestPayload = string.Empty;
        if (request.Content != null)
        {
            requestPayload = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        // T2: Backend -> SRDV Request
        string formattedRequestPayload = JsonPayloadFormatter.Format(requestPayload, options.Mode, options.MaxPayloadLength);
        _logger.LogInformation(
            "[{CorrelationId}] [T2] Backend -> SRDV Flight Request:\nMethod: {Method}\nURL: {Url}\nPayload:\n{Payload}\n--------------------------------------------------", 
            correlationId, request.Method, request.RequestUri, formattedRequestPayload);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[{CorrelationId}] HttpRequestException when calling SRDV Flight API after {ElapsedMs}ms", 
                correlationId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[{CorrelationId}] Timeout/Canceled when calling SRDV Flight API after {ElapsedMs}ms", 
                correlationId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        
        stopwatch.Stop();
        
        bool isLargePayloadEndpoint = request.RequestUri != null && 
            (request.RequestUri.ToString().Contains("Search", System.StringComparison.OrdinalIgnoreCase) || 
             request.RequestUri.ToString().Contains("FareQuote", System.StringComparison.OrdinalIgnoreCase) ||
             request.RequestUri.ToString().Contains("fare-quote", System.StringComparison.OrdinalIgnoreCase));

        bool shouldOmit = isLargePayloadEndpoint && options.Mode == PayloadLoggingMode.Omit;

        string responsePayload;
        if (!shouldOmit && response.Content != null)
        {
            responsePayload = await response.Content.ReadAsStringAsync(cancellationToken);
            responsePayload = JsonPayloadFormatter.Format(responsePayload, options.Mode, options.MaxPayloadLength);
        }
        else
        {
            responsePayload = "[Response payload omitted for performance]";
        }
        
        // T3: SRDV -> Backend Response
        _logger.LogInformation(
            "[{CorrelationId}] [T3] SRDV Flight -> Backend Response:\nStatus: {StatusCode}\nTime: {ElapsedMs}ms\nPayload:\n{Payload}\n==================================================", 
            correlationId, response.StatusCode, stopwatch.ElapsedMilliseconds, responsePayload);

        return response;
    }
}
