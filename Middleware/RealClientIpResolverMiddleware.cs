using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Middleware
{
    public class RealClientIpResolverMiddleware
    {
        private readonly RequestDelegate _next;

        public RealClientIpResolverMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var ipAddress = context.Connection.RemoteIpAddress?.ToString();

            // Cloudflare
            if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
            {
                ipAddress = cfIp.FirstOrDefault();
            }
            // X-Forwarded-For
            else if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffIp))
            {
                ipAddress = xffIp.FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim();
            }

            if (!string.IsNullOrEmpty(ipAddress))
            {
                context.Items["RealIpAddress"] = ipAddress;
            }

            await _next(context);
        }
    }
}
