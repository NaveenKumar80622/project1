using Microsoft.AspNetCore.Http;
using System.Linq;

namespace PickNBook.Api.Extensions
{
    public static class HttpContextExtensions
    {
        public static string GetClientIpAddress(this HttpContext context)
        {
            if (context == null) return "127.0.0.1";

            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedIp))
            {
                var ip = forwardedIp.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ip)) return ip;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        }
    }
}
