using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PickNBook.Api.Data;
using System.Text.Json;

namespace PickNBook.Api.Middleware
{
    public class CentralSecurityMiddleware
    {
        private readonly RequestDelegate _next;

        public CentralSecurityMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext dbContext, IMemoryCache memoryCache)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";
            
            // Only apply to API routes
            if (!path.StartsWith("/api/"))
            {
                await _next(context);
                return;
            }

            // 1. Extract Client IP
            var ipAddress = GetClientIp(context);

            // 2. Identify Scope
            string scope = "USER";
            if (path.StartsWith("/api/v1/admin")) scope = "ADMIN";
            else if (path.StartsWith("/api/v1/b2b")) scope = "B2B";

            // 3. Identify Account from User context (Assumes JWT auth has populated User.Identity)
            string accountId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // 4, 5, 6. Check IP Restrictions using Cache
            var ipCacheKey = $"SecurityIpRules_{ipAddress}_{scope}";
            if (!memoryCache.TryGetValue(ipCacheKey, out IpSecurityStatus ipStatus))
            {
                ipStatus = new IpSecurityStatus();
                
                ipStatus.IsPermanentlyBlocked = await dbContext.SecurityIpRules
                    .AnyAsync(r => r.IpAddress == ipAddress && r.BlockType == "PERMANENT" && (r.Status == "BLOCKED" || r.Status == "BLACKLISTED") && (r.Scope == scope || r.Scope == "ADMIN_USER"));

                ipStatus.IsWhitelisted = await dbContext.SecurityIpRules
                    .AnyAsync(r => r.IpAddress == ipAddress && r.Action == "WHITELIST" && r.Status == "ACTIVE" && (r.Scope == scope || r.Scope == "ADMIN_USER"));

                if (!ipStatus.IsWhitelisted)
                {
                    var tempBlock = await dbContext.SecurityIpRules
                        .FirstOrDefaultAsync(r => r.IpAddress == ipAddress && r.BlockType == "TEMPORARY" && (r.Status == "BLOCKED" || r.Status == "BLACKLISTED") && (r.Scope == scope || r.Scope == "ADMIN_USER"));

                    if (tempBlock != null)
                    {
                        ipStatus.IsTemporarilyBlocked = true;
                        ipStatus.TempBlockExpiryTime = tempBlock.ExpiryTime;
                    }
                }

                memoryCache.Set(ipCacheKey, ipStatus, TimeSpan.FromMinutes(5));
            }

            if (ipStatus.IsPermanentlyBlocked)
            {
                await ReturnBlockedResponse(context, "IP_PERMANENTLY_BLOCKED", "Access denied by security policy.");
                return;
            }

            if (ipStatus.IsTemporarilyBlocked && !ipStatus.IsWhitelisted)
            {
                if (ipStatus.TempBlockExpiryTime.HasValue && ipStatus.TempBlockExpiryTime.Value <= DateTime.UtcNow)
                {
                    // Ignore, let background job expire it. But don't block.
                }
                else
                {
                    await ReturnBlockedResponse(context, "IP_TEMPORARILY_BLOCKED", "Access temporarily restricted.", ipStatus.TempBlockExpiryTime);
                    return;
                }
            }

            // 7. Check API Security Rules (Module F) using Cache
            var apiRuleCacheKey = $"SecurityApiRule_{path}";
            if (!memoryCache.TryGetValue(apiRuleCacheKey, out CachedApiRule cachedApiRule))
            {
                var apiRule = await dbContext.SecurityApiRules
                    .Where(r => path.StartsWith(r.UrlPattern.ToLower()))
                    .OrderByDescending(r => r.UrlPattern.Length)
                    .FirstOrDefaultAsync();

                cachedApiRule = new CachedApiRule();
                if (apiRule != null)
                {
                    cachedApiRule.HasRule = true;
                    cachedApiRule.RateLimitValue = apiRule.RateLimitValue;
                    cachedApiRule.RateLimitPeriod = apiRule.RateLimitPeriod;
                }

                memoryCache.Set(apiRuleCacheKey, cachedApiRule, TimeSpan.FromMinutes(60));
            }

            int rateLimitCount = 100;
            int rateLimitWindow = 1;

            if (cachedApiRule.HasRule)
            {
                if (cachedApiRule.RateLimitValue.HasValue) rateLimitCount = cachedApiRule.RateLimitValue.Value;
                if (cachedApiRule.RateLimitPeriod == "PER_HOUR") rateLimitWindow = 60;
                else if (cachedApiRule.RateLimitPeriod == "PER_DAY") rateLimitWindow = 1440;
                else rateLimitWindow = 1;
            }

            // 8. API Rate Limiting Check
            if (!ipStatus.IsWhitelisted)
            {
                var cacheKey = $"RateLimit_{ipAddress}_{path}";
                var requestCount = memoryCache.GetOrCreate(cacheKey, entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(rateLimitWindow);
                    return 0;
                });

                if (requestCount >= rateLimitCount)
                {
                    context.Response.StatusCode = 429;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        code = "RATE_LIMIT_EXCEEDED",
                        message = "Too many requests. Please try again later."
                    }));
                    return;
                }

                memoryCache.Set(cacheKey, requestCount + 1, TimeSpan.FromMinutes(rateLimitWindow));
            }

            // 9. Check Account Status
            if (!string.IsNullOrEmpty(accountId))
            {
                var lockRecord = await dbContext.SecurityAccountLocks
                    .FirstOrDefaultAsync(l => l.AccountId.ToString() == accountId && (l.LockStatus == "TEMPORARILY_LOCKED" || l.LockStatus == "PERMANENTLY_LOCKED"));
                
                if (lockRecord != null)
                {
                    if (lockRecord.LockStatus == "TEMPORARILY_LOCKED" && lockRecord.ExpiresAt.HasValue && lockRecord.ExpiresAt.Value <= DateTime.UtcNow)
                    {
                        // Ignore, let background job clean it up.
                    }
                    else
                    {
                        await ReturnBlockedResponse(context, "ACCOUNT_LOCKED", "Your account has been locked.", lockRecord.ExpiresAt);
                        return;
                    }
                }
            }

            // 10. ALLOW request
            await _next(context);
        }

        private string GetClientIp(HttpContext context)
        {
            var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ip))
            {
                return ip.Split(',')[0].Trim();
            }
            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        private async Task ReturnBlockedResponse(HttpContext context, string code, string message, DateTime? blockedUntil = null)
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";

            var response = new
            {
                success = false,
                code = code,
                message = message,
                blockedUntil = blockedUntil
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }

        private class IpSecurityStatus
        {
            public bool IsPermanentlyBlocked { get; set; }
            public bool IsWhitelisted { get; set; }
            public bool IsTemporarilyBlocked { get; set; }
            public DateTime? TempBlockExpiryTime { get; set; }
        }

        private class CachedApiRule
        {
            public bool HasRule { get; set; }
            public int? RateLimitValue { get; set; }
            public string RateLimitPeriod { get; set; }
        }
    }
}
