using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PickNBook.Api.Data;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Middleware
{
    public class SuperAdminEmergencyRecoveryMiddleware
    {
        private readonly RequestDelegate _next;

        public SuperAdminEmergencyRecoveryMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
        {
            if (context.Request.Path.StartsWithSegments("/api/v1/admin/security/superadmin-recovery", StringComparison.OrdinalIgnoreCase)
                && context.Request.Method == HttpMethods.Post)
            {
                if (context.Request.Headers.TryGetValue("X-SuperAdmin-Recovery-Key", out var headerKey))
                {
                    // This could be verified against an env variable, but based on the payload, the masterKey might also be in the body.
                    // For the sake of the requirement:
                    // "Bypasses IP blacklist middleware for emergency system recovery."
                    
                    context.Request.EnableBuffering();
                    using var reader = new System.IO.StreamReader(context.Request.Body, leaveOpen: true);
                    var bodyContent = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0; // Reset for potential later use

                    bool isValidKey = false;
                    try
                    {
                        var json = JsonSerializer.Deserialize<JsonElement>(bodyContent);
                        if (json.TryGetProperty("masterKey", out var masterKeyProp))
                        {
                            if (masterKeyProp.GetString() == "RECOVERY_MASTER_KEY_998877")
                            {
                                isValidKey = true;
                            }
                        }
                    }
                    catch { }

                    if (isValidKey)
                    {
                        using var scope = serviceProvider.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        
                        await db.Database.ExecuteSqlRawAsync("DELETE FROM ip_access_rules WHERE list_type = 'BLACKLIST'");
                        
                        var response = new
                        {
                            success = true,
                            message = "SuperAdmin emergency recovery executed. All IP blacklist entries flushed."
                        };

                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
                        return; // Short-circuit
                    }
                }
            }

            await _next(context);
        }
    }
}
