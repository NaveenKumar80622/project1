using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PickNBook.Api.Infrastructure.Logging;

public class PointerItLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PointerItLoggingHandler> _logger;

    public PointerItLoggingHandler(ILogger<PointerItLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = CorrelationIdContext.CorrelationId;
        if (string.IsNullOrEmpty(correlationId))
            correlationId = "NO-CORRELATION-ID";

        // ── T2: Log outbound request (sanitized) ──────────────────────────────
        var sanitizedUrl = BuildSanitizedUrl(request.RequestUri);
        _logger.LogInformation(
            "[{CorrelationId}] [T2] Backend -> PointerIT SMS Request:\n{Url}\n--------------------------------------------------",
            correlationId, sanitizedUrl);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "[{CorrelationId}] HttpRequestException calling PointerIT after {ElapsedMs}ms",
                correlationId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "[{CorrelationId}] Timeout/Canceled calling PointerIT after {ElapsedMs}ms",
                correlationId, stopwatch.ElapsedMilliseconds);
            throw;
        }

        stopwatch.Stop();

        // ── T3: Read response body, restore content, log fields ───────────────
        string rawBody = string.Empty;
        if (response.Content != null)
        {
            // Capture all original content headers before reading
            var originalHeaders = response.Content.Headers;
            rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

            // Restore content so PointerItSmsProvider can call ReadAsStringAsync() normally
            var restored = new StringContent(rawBody, Encoding.UTF8);
            // Copy ALL original content headers (Content-Type, Content-Encoding, etc.)
            restored.Headers.Clear();
            foreach (var header in originalHeaders)
            {
                restored.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            response.Content = restored;
        }

        // Parse PointerIT response fields for structured logging
        int statusCode = 0;
        string state = string.Empty;
        string description = string.Empty;
        string transactionId = string.Empty;

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;
                if (root.TryGetProperty("statusCode", out var sc) && sc.ValueKind == JsonValueKind.Number)
                    statusCode = sc.GetInt32();
                if (root.TryGetProperty("state", out var st))
                    state = st.GetString() ?? string.Empty;
                if (root.TryGetProperty("description", out var desc))
                    description = desc.GetString() ?? string.Empty;
                if (root.TryGetProperty("transactionId", out var tx))
                    transactionId = tx.GetRawText();
            }
            catch
            {
                // If JSON parse fails, log raw body (it may be plain text error)
                state = rawBody;
            }
        }

        _logger.LogInformation(
            "[{CorrelationId}] [T3] PointerIT -> Backend Response:\n" +
            "  HTTP Status  : {HttpStatus}\n" +
            "  statusCode   : {StatusCode}\n" +
            "  state        : {State}\n" +
            "  description  : {Description}\n" +
            "  transactionId: {TransactionId}\n" +
            "  Time         : {ElapsedMs}ms\n" +
            "==================================================",
            correlationId,
            (int)response.StatusCode,
            statusCode,
            state,
            description,
            transactionId,
            stopwatch.ElapsedMilliseconds);

        return response;
    }

    /// <summary>
    /// Returns a copy of the request URL with password= and text= values replaced by *****.
    /// The actual HTTP request is sent with the real values unchanged.
    /// </summary>
    private static string BuildSanitizedUrl(System.Uri? requestUri)
    {
        if (requestUri == null) return "[null URI]";

        var query = HttpUtility.ParseQueryString(requestUri.Query);
        var sb = new StringBuilder();
        sb.Append($"  POST {requestUri.GetLeftPart(System.UriPartial.Path)}\n");

        foreach (string key in query.AllKeys)
        {
            if (key == null) continue;
            var value = (key == "password" || key == "text") ? "*****" : query[key];
            sb.AppendLine($"  {key,-24}= {value}");
        }

        return sb.ToString().TrimEnd();
    }
}
