using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PickNBook.Api.Middleware;

/// <summary>
/// Traces request duration and logs warnings if the execution time of any API endpoint exceeds 500ms.
/// </summary>
public class RequestProfilingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestProfilingMiddleware> _logger;

    public RequestProfilingMiddleware(RequestDelegate next, ILogger<RequestProfilingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        await _next(context);

        stopwatch.Stop();
        var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        // Standard MNC threshold: Log warning for requests exceeding 500ms
        if (elapsedMilliseconds > 500)
        {
            _logger.LogWarning("Slow request: {Method} {Path} took {ElapsedMs}ms. TraceIdentifier: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                elapsedMilliseconds,
                context.TraceIdentifier);
        }
    }
}
