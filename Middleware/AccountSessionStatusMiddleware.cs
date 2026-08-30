using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PickNBook.Api.Data;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace PickNBook.Api.Middleware
{
    public class AccountSessionStatusMiddleware
    {
        private readonly RequestDelegate _next;

        public AccountSessionStatusMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider, IMemoryCache memoryCache)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var sessionId = context.User.FindFirst("SessionId")?.Value;
                var role = context.User.FindFirst(ClaimTypes.Role)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    using var scope = serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;

                    // 1. Check Account Lock Status
                    var lockout = await db.UserLockouts
                        .AsNoTracking()
                        .Where(l => l.UserId == userId && l.Status == "Locked")
                        .OrderByDescending(l => l.LockedOn)
                        .FirstOrDefaultAsync();

                    if (lockout != null)
                    {
                        if (lockout.UnlockAt > now)
                        {
                            context.Response.StatusCode = 403;
                            context.Response.ContentType = "application/json";
                            var errorPayload = new
                            {
                                success = false,
                                statusCode = 403,
                                code = "ACCOUNT_LOCKED",
                                message = "Your account is locked due to multiple failed login attempts.",
                                unlockAt = lockout.UnlockAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                            };
                            await context.Response.WriteAsync(JsonSerializer.Serialize(errorPayload));
                            return;
                        }
                        else
                        {
                            // Auto unlock
                            // In real scenario we would update the status, but here we can let the background task do it or just treat it as unlocked.
                        }
                    }

                    // 2. Check Session Expiry/Inactivity
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        bool isSessionValid = false;
                        if (role == "ADMIN" || role == "SUPER_ADMIN")
                        {
                            var session = await db.AdminSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
                            if (session != null && session.Status == "ACTIVE" && session.ExpiresAt > now)
                            {
                                isSessionValid = true;
                                session.LastActivityAt = now;
                                // Ideally update expiresAt based on settings, but we'll leave it as is or extend it
                                db.Update(session);
                                await db.SaveChangesAsync();
                            }
                        }
                        else
                        {
                            var session = await db.UserSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
                            if (session != null && session.Status == "ACTIVE" && session.ExpiresAt > now)
                            {
                                isSessionValid = true;
                                session.LastActivityAt = now;
                                db.Update(session);
                                await db.SaveChangesAsync();
                            }
                        }

                        if (!isSessionValid)
                        {
                            context.Response.StatusCode = 401;
                            context.Response.ContentType = "application/json";
                            var errorPayload = new
                            {
                                success = false,
                                statusCode = 401,
                                code = "SESSION_EXPIRED",
                                message = "Your session is inactive or expired.",
                                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                            };
                            await context.Response.WriteAsync(JsonSerializer.Serialize(errorPayload));
                            return;
                        }
                    }
                }
            }

            await _next(context);
        }
    }
}
