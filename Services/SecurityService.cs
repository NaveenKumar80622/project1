using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace PickNBook.Api.Services
{
    public class SecurityService : ISecurityService
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _memoryCache;

        public SecurityService(AppDbContext context, IMemoryCache memoryCache)
        {
            _context = context;
            _memoryCache = memoryCache;
        }

        public async Task LogAuditAsync(string eventType, string action, string status, string ipAddress, string? userId = null, string? email = null, string? sessionId = null, string? reason = null)
        {
            var log = new SecurityAuditLog
            {
                EventType = eventType,
                Action = action,
                Status = status,
                IpAddress = ipAddress,
                UserOrAdminId = userId,
                Email = email,
                SessionId = sessionId,
                ReasonDetails = reason
            };
            _context.SecurityAuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        public async Task<SecuritySettings?> GetSettingsAsync()
        {
            return await _context.SecuritySettings.FirstOrDefaultAsync(s => s.Id == 1);
        }

        public async Task AddIpToBlacklistAsync(string ipAddress, string reason, bool isPermanent, DateTime? expiresAt, string? createdBy)
        {
            var existing = await _context.IpAccessRules.FirstOrDefaultAsync(r => r.IpAddress == ipAddress);
            if (existing != null)
            {
                existing.ListType = "BLACKLIST";
                existing.Status = "ACTIVE";
                existing.IsPermanent = isPermanent;
                existing.ExpiresAt = expiresAt;
                existing.Reason = reason;
                existing.BlockedAt = DateTime.UtcNow;
            }
            else
            {
                var rule = new IpAccessRule
                {
                    IpAddress = ipAddress,
                    ListType = "BLACKLIST",
                    Status = "ACTIVE",
                    IsPermanent = isPermanent,
                    ExpiresAt = expiresAt,
                    Reason = reason,
                    BlockedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                _context.IpAccessRules.Add(rule);
            }
            await _context.SaveChangesAsync();

            // Cache it
            var cacheOptions = new MemoryCacheEntryOptions();
            if (!isPermanent && expiresAt.HasValue)
            {
                var timeRemaining = expiresAt.Value - DateTime.UtcNow;
                cacheOptions.AbsoluteExpirationRelativeToNow = timeRemaining < TimeSpan.FromMinutes(5) ? timeRemaining : TimeSpan.FromMinutes(5);
            }
            else
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            }
            _memoryCache.Set($"Blacklist_{ipAddress}", true, cacheOptions);
        }

        public async Task RemoveIpFromBlacklistAsync(string ipAddress)
        {
            var rule = await _context.IpAccessRules.FirstOrDefaultAsync(r => r.IpAddress == ipAddress);
            if (rule != null)
            {
                _context.IpAccessRules.Remove(rule);
                await _context.SaveChangesAsync();
            }
            _memoryCache.Remove($"Blacklist_{ipAddress}");
        }

        public async Task QueueNotificationAsync(string eventType, string recipientType, string recipient, string subject, string? userId = null, string? ipAddress = null, string? cooldownKey = null)
        {
            var mapping = await _context.SecurityNotificationMappings.FirstOrDefaultAsync(m => m.EventType == eventType);
            if (mapping != null && mapping.Enabled)
            {
                // Check cooldown if we have a key
                string status = "PENDING";
                if (!string.IsNullOrEmpty(cooldownKey))
                {
                    var cooldownTime = DateTime.UtcNow.AddSeconds(-mapping.CooldownSeconds);
                    bool isSuppressed = await _context.SecurityNotifications
                        .AnyAsync(n => n.CooldownKey == cooldownKey && n.CreatedAt >= cooldownTime && (n.Status == "PENDING" || n.Status == "SENT" || n.Status == "SUPPRESSED"));
                    
                    if (isSuppressed)
                    {
                        status = "SUPPRESSED";
                    }
                }

                var notification = new SecurityNotification
                {
                    EventType = eventType,
                    TemplateId = mapping.TemplateId,
                    RecipientType = recipientType,
                    Recipient = recipient,
                    Subject = subject,
                    UserId = userId,
                    IpAddress = ipAddress,
                    Status = status,
                    CooldownKey = cooldownKey
                };
                _context.SecurityNotifications.Add(notification);
                await _context.SaveChangesAsync();
            }
        }
        
        public async Task<PickNBook.Api.Models.DTOs.SecurityMetricsDto> GetDashboardMetricsAsync()
        {
            var now = DateTime.UtcNow;
            var past24Hours = now.AddDays(-1);
 
            // 1. Account Lockouts
            var activeLockouts = await _context.SecurityAccountLocks
                .CountAsync(l => l.LockStatus != "UNLOCKED" && l.LockStatus != "EXPIRED" && (!l.ExpiresAt.HasValue || l.ExpiresAt > now));
 
            // 2. IP Rules Counts
            var blacklistedIps = await _context.SecurityIpRules
                .CountAsync(r => r.Action == "BLACKLIST" && (r.Status == "ACTIVE" || r.Status == "BLACKLISTED"));
 
            var whitelistedIps = await _context.SecurityIpRules
                .CountAsync(r => r.Action == "WHITELIST" && r.Status == "ACTIVE");
 
            var activeBlockedIps = await _context.SecurityIpRules
                .CountAsync(r => r.Action == "BLOCK" && (r.Status == "ACTIVE" || r.Status == "BLOCKED") && (!r.ExpiryTime.HasValue || r.ExpiryTime > now));
 
            var expiredBlocks = await _context.SecurityIpRules
                .CountAsync(r => (r.Action == "BLOCK" || r.Action == "BLACKLIST") && (r.Status == "EXPIRED" || (r.ExpiryTime.HasValue && r.ExpiryTime <= now)));
 
            // 3. Block Sources
            var automaticBlocks = await _context.SecurityIpRules
                .CountAsync(r => (r.Action == "BLOCK" || r.Action == "BLACKLIST") && r.Source == "AUTOMATIC");
 
            var manualBlocks = await _context.SecurityIpRules
                .CountAsync(r => (r.Action == "BLOCK" || r.Action == "BLACKLIST") && r.Source != "AUTOMATIC");
 
            // 4. Security Violations / Logs in past 24 Hours
            var failedLoginAttempts24h = await _context.SecurityAuditLogs
                .CountAsync(log => log.CreatedAt >= past24Hours && log.EventType == "FAILED_LOGIN");
 
            var otpViolations24h = await _context.SecurityAuditLogs
                .CountAsync(log => log.CreatedAt >= past24Hours && log.EventType == "OTP_VIOLATION");
 
            var passwordViolations24h = await _context.SecurityAuditLogs
                .CountAsync(log => log.CreatedAt >= past24Hours && log.EventType == "PASSWORD_EXPIRED_ATTEMPT");
 
            var registrationViolations24h = await _context.SecurityAuditLogs
                .CountAsync(log => log.CreatedAt >= past24Hours && log.EventType == "REGISTRATION_SUSPICIOUS");
 
            var apiViolations24h = await _context.SecurityAuditLogs
                .CountAsync(log => log.CreatedAt >= past24Hours && log.EventType == "API_RATE_LIMIT_EXCEEDED");
 
            // 5. Account Restrictions by Scope
            var userRestrictions = await _context.SecurityAccountLocks
                .CountAsync(l => l.LockStatus != "UNLOCKED" && l.LockStatus != "EXPIRED" && l.Scope == "USER");
 
            var adminRestrictions = await _context.SecurityAccountLocks
                .CountAsync(l => l.LockStatus != "UNLOCKED" && l.LockStatus != "EXPIRED" && l.Scope == "ADMIN");
 
            var b2bRestrictions = await _context.SecurityAccountLocks
                .CountAsync(l => l.LockStatus != "UNLOCKED" && l.LockStatus != "EXPIRED" && l.Scope == "B2B");
 
            return new PickNBook.Api.Models.DTOs.SecurityMetricsDto
            {
                ActiveLockouts = activeLockouts,
                BlacklistedIps = blacklistedIps,
                ActiveBlockedIps = activeBlockedIps,
                WhitelistedIps = whitelistedIps,
                AutomaticBlocks = automaticBlocks,
                ManualBlocks = manualBlocks,
                ExpiredBlocks = expiredBlocks,
                FailedLoginAttempts24h = failedLoginAttempts24h,
                OtpViolations24h = otpViolations24h,
                PasswordViolations24h = passwordViolations24h,
                RegistrationViolations24h = registrationViolations24h,
                ApiViolations24h = apiViolations24h,
                UserRestrictions = userRestrictions,
                AdminRestrictions = adminRestrictions,
                B2bRestrictions = b2bRestrictions
            };
        }

        public async Task<System.Collections.Generic.List<PickNBook.Api.Models.DTOs.RecentActivityDto>> GetRecentActivityAsync(int limit = 10)
        {
            var logs = await _context.SecurityAuditLogs
                .OrderByDescending(l => l.CreatedAt)
                .Take(limit)
                .ToListAsync();
            
            return logs.Select(l => new PickNBook.Api.Models.DTOs.RecentActivityDto
            {
                Id = l.Id,
                EventType = l.EventType,
                Scope = l.Scope ?? "SYSTEM",
                IpAddress = l.IpAddress,
                AccountEmail = l.AccountEmail ?? "",
                Action = l.Action,
                Status = l.Status,
                Reason = l.ReasonDetails ?? "",
                CreatedAt = l.CreatedAt
            }).ToList();
        }

        public async Task<System.Collections.Generic.List<SecurityLimit>> GetSecurityLimitsAsync(string scope)
        {
            if (scope == "ALL")
            {
                return await _context.SecurityLimits.ToListAsync();
            }
            return await _context.SecurityLimits.Where(s => s.Scope == scope).ToListAsync();
        }

        public async Task<bool> UpdateSecurityLimitAsync(long id, PickNBook.Api.Models.DTOs.UpdateSecurityLimitDto dto)
        {
            var limit = await _context.SecurityLimits.FindAsync(id);
            if (limit == null) return false;

            limit.IsEnabled = dto.IsEnabled;
            limit.LimitValue = dto.LimitValue;
            limit.TimePeriodValue = dto.TimePeriodValue;
            limit.TimePeriodUnit = dto.TimePeriodUnit;
            limit.AccountAction = dto.AccountAction;
            limit.IpAction = dto.IpAction;
            limit.BlockDurationValue = dto.BlockDurationValue;
            limit.BlockDurationUnit = dto.BlockDurationUnit;
            limit.EmailEnabled = dto.EmailEnabled;
            limit.EmailTemplateId = dto.EmailTemplateId;
            limit.ResetPeriodValue = dto.ResetPeriodValue;
            limit.ResetPeriodUnit = dto.ResetPeriodUnit;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> BulkUpdateSecurityLimitsAsync(PickNBook.Api.Models.DTOs.BulkUpdateSecurityLimitDto dto)
        {
            foreach (var rule in dto.Rules)
            {
                var limit = await _context.SecurityLimits.FindAsync(rule.Id);
                if (limit != null && limit.Scope == dto.Scope)
                {
                    limit.IsEnabled = rule.IsEnabled;
                    limit.LimitValue = rule.LimitValue;
                    limit.TimePeriodValue = rule.TimePeriodValue;
                    limit.TimePeriodUnit = rule.TimePeriodUnit;
                    limit.AccountAction = rule.AccountAction;
                    limit.IpAction = rule.IpAction;
                    limit.BlockDurationValue = rule.BlockDurationValue;
                    limit.BlockDurationUnit = rule.BlockDurationUnit;
                    limit.EmailEnabled = rule.EmailEnabled;
                    limit.EmailTemplateId = rule.EmailTemplateId;
                    limit.ResetPeriodValue = rule.ResetPeriodValue;
                    limit.ResetPeriodUnit = rule.ResetPeriodUnit;
                }
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<System.Collections.Generic.List<SecurityAccountLock>> GetB2bWalletRestrictionsAsync()
        {
            return await _context.SecurityAccountLocks
                .Where(l => l.Scope == "B2B" && l.LockStatus != "UNLOCKED" && l.LockStatus != "EXPIRED" && l.SecurityTrigger == "LOW_WALLET_BALANCE")
                .ToListAsync();
        }

        public async Task<bool> UnblockB2bWalletAsync(long id, string reason, string unblockedBy)
        {
            var lockRecord = await _context.SecurityAccountLocks.FindAsync(id);
            if (lockRecord == null || lockRecord.Scope != "B2B") return false;

            lockRecord.LockStatus = "UNLOCKED";
            lockRecord.UnlockedAt = System.DateTime.UtcNow;
            lockRecord.UnlockedBy = unblockedBy;
            lockRecord.LockReason = $"Unblocked: {reason}";

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
