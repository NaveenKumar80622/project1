using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class SecurityCounterService : ISecurityCounterService
    {
        private readonly AppDbContext _context;

        public SecurityCounterService(AppDbContext context)
        {
            _context = context;
        }

        public async Task HandleLoginFailureAsync(string ipAddress, long accountId, string accountEmail, string scope)
        {
            await IncrementCounterAsync("LOGIN_FAILURE", "IP_ACCOUNT", $"{ipAddress}:{accountId}", scope, ipAddress, accountId, accountEmail);
        }

        public async Task HandleOtpMismatchAsync(string ipAddress, long accountId, string accountEmail, string scope)
        {
            await IncrementCounterAsync("OTP_MISMATCH", "IP_ACCOUNT", $"{ipAddress}:{accountId}", scope, ipAddress, accountId, accountEmail);
        }

        public async Task HandleLoginSuccessAsync(string ipAddress, long accountId, string scope)
        {
            // Reset failure counters
            await ResetCounterAsync(accountId, scope, "LOGIN_FAILURE");
            await ResetCounterAsync(accountId, scope, "INVALID_PASSWORD");

            // Increment daily login (simplified logic here)
            var dailyLimit = await _context.SecurityLimits.FirstOrDefaultAsync(l => l.Scope == scope && l.RuleKey == "DAILY_LOGIN");
            if (dailyLimit != null && dailyLimit.IsEnabled)
            {
                var counter = await GetOrCreateCounterAsync("DAILY_LOGIN", "ACCOUNT", accountId.ToString(), scope, dailyLimit.LimitValue, dailyLimit.TimePeriodValue ?? 1440, dailyLimit.TimePeriodUnit ?? "MINUTES");
                counter.CurrentCount++;
                counter.LastAttemptAt = DateTime.UtcNow;
                
                if (counter.CurrentCount >= counter.LimitValue)
                {
                    // Apply daily login limit restriction logic here
                    // e.g. Temporary account lock
                }
                await _context.SaveChangesAsync();
            }
        }

        public async Task ResetCounterAsync(long accountId, string scope, string counterKey)
        {
            var counters = await _context.SecurityCounters
                .Where(c => c.Scope == scope && c.CounterKey == counterKey && c.DimensionValue.EndsWith(accountId.ToString()))
                .ToListAsync();

            if (counters.Any())
            {
                _context.SecurityCounters.RemoveRange(counters);
                await _context.SaveChangesAsync();
            }
        }

        private async Task IncrementCounterAsync(string ruleKey, string dimensionType, string dimensionValue, string scope, string ipAddress, long accountId, string accountEmail)
        {
            var limit = await _context.SecurityLimits.FirstOrDefaultAsync(l => l.Scope == scope && l.RuleKey == ruleKey);
            if (limit == null || !limit.IsEnabled) return;

            var counter = await GetOrCreateCounterAsync(ruleKey, dimensionType, dimensionValue, scope, limit.LimitValue, limit.TimePeriodValue ?? 10, limit.TimePeriodUnit ?? "MINUTES");
            
            counter.CurrentCount++;
            counter.LastAttemptAt = DateTime.UtcNow;

            if (counter.CurrentCount >= counter.LimitValue)
            {
                // Threshold reached, apply actions
                if (limit.AccountAction == "TEMPORARY_LOCK" || limit.AccountAction == "PERMANENT_LOCK")
                {
                    var lockStatus = limit.AccountAction == "TEMPORARY_LOCK" ? "TEMPORARILY_LOCKED" : "PERMANENTLY_LOCKED";
                    var expiresAt = lockStatus == "TEMPORARILY_LOCKED" ? DateTime.UtcNow.AddMinutes(limit.BlockDurationValue ?? 60) : (DateTime?)null;

                    _context.SecurityAccountLocks.Add(new SecurityAccountLock
                    {
                        AccountId = accountId,
                        AccountEmail = accountEmail,
                        Scope = scope,
                        LockStatus = lockStatus,
                        LockReason = $"{limit.RuleName} Threshold Exceeded",
                        SecurityTrigger = ruleKey,
                        ExpiresAt = expiresAt,
                        IpAddress = ipAddress
                    });
                }

                if (limit.IpAction == "BLACKLIST" || limit.IpAction == "BLOCK")
                {
                    var blockType = "TEMPORARY";
                    var expiresAt = DateTime.UtcNow.AddMinutes(limit.BlockDurationValue ?? 60);

                    _context.SecurityIpRules.Add(new SecurityIpRule
                    {
                        IpAddress = ipAddress,
                        Action = limit.IpAction,
                        Scope = scope,
                        Status = "BLOCKED",
                        Source = "AUTOMATIC",
                        Reason = $"{limit.RuleName} Limit",
                        BlockType = blockType,
                        DurationMinutes = limit.BlockDurationValue,
                        ExpiryTime = expiresAt,
                        SecurityTrigger = ruleKey,
                        AccountId = accountId,
                        AccountEmail = accountEmail
                    });
                }

                // Note: Email sending and audit logging logic should be hooked up here
            }

            await _context.SaveChangesAsync();
        }

        private async Task<SecurityCounter> GetOrCreateCounterAsync(string counterKey, string dimensionType, string dimensionValue, string scope, int limitValue, int periodValue, string periodUnit)
        {
            var now = DateTime.UtcNow;
            var counter = await _context.SecurityCounters
                .FirstOrDefaultAsync(c => c.CounterKey == counterKey && c.DimensionValue == dimensionValue && c.Scope == scope && c.PeriodEnd > now);

            if (counter == null)
            {
                var periodEnd = now;
                if (periodUnit == "MINUTES") periodEnd = now.AddMinutes(periodValue);
                else if (periodUnit == "HOURS") periodEnd = now.AddHours(periodValue);
                else if (periodUnit == "DAY") periodEnd = now.AddDays(periodValue);

                counter = new SecurityCounter
                {
                    CounterKey = counterKey,
                    Scope = scope,
                    DimensionType = dimensionType,
                    DimensionValue = dimensionValue,
                    LimitValue = limitValue,
                    PeriodStart = now,
                    PeriodEnd = periodEnd,
                    CurrentCount = 0
                };
                _context.SecurityCounters.Add(counter);
            }

            return counter;
        }
    }
}
