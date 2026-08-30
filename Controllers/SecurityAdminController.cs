using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using PickNBook.Api.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    [Route("api/v1/admin/security")]
    [ApiController]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class SecurityAdminController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ISecurityService _securityService;

        private readonly IEmailTemplateService _templateService;
        private readonly IConfiguration _config;

        public SecurityAdminController(AppDbContext context, ISecurityService securityService, IEmailTemplateService templateService, IConfiguration config)
        {
            _context = context;
            _securityService = securityService;
            _templateService = templateService;
            _config = config;
        }

        [HttpGet("metrics")]
        public async Task<IActionResult> GetMetrics()
        {
            try
            {
                var metrics = await _securityService.GetDashboardMetricsAsync();
                return Ok(new { success = true, data = metrics });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("recent-activity")]
        public async Task<IActionResult> GetRecentActivity([FromQuery] int limit = 10)
        {
            try
            {
                var data = await _securityService.GetRecentActivityAsync(limit);
                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("limits")]
        public async Task<IActionResult> GetLimits([FromQuery] string scope = "USER")
        {
            try
            {
                var data = await _securityService.GetSecurityLimitsAsync(scope);
                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPut("limits/{id}")]
        public async Task<IActionResult> UpdateLimit(long id, [FromBody] PickNBook.Api.Models.DTOs.UpdateSecurityLimitDto dto)
        {
            try
            {
                var success = await _securityService.UpdateSecurityLimitAsync(id, dto);
                if (!success) return NotFound(new { success = false, message = "Limit not found" });

                await _securityService.LogAuditAsync("LIMIT_UPDATED", "UPDATE_LIMIT", "SUCCESS", "127.0.0.1", reason: $"Updated limit ID {id}");
                return Ok(new { success = true, message = "Limit updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPut("limits/bulk")]
        public async Task<IActionResult> BulkUpdateLimits([FromBody] PickNBook.Api.Models.DTOs.BulkUpdateSecurityLimitDto dto)
        {
            try
            {
                await _securityService.BulkUpdateSecurityLimitsAsync(dto);
                await _securityService.LogAuditAsync("LIMITS_BULK_UPDATED", "UPDATE_LIMITS", "SUCCESS", "127.0.0.1", reason: $"Bulk updated limits for scope {dto.Scope}");
                return Ok(new { success = true, message = "Limits updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("b2b-wallet-restrictions")]
        public async Task<IActionResult> GetB2bWalletRestrictions()
        {
            try
            {
                var data = await _securityService.GetB2bWalletRestrictionsAsync();
                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("b2b-wallet-restrictions/{id}/unblock")]
        public async Task<IActionResult> UnblockB2bWallet(long id, [FromBody] PickNBook.Api.Models.DTOs.UnblockIpDto dto)
        {
            try
            {
                var success = await _securityService.UnblockB2bWalletAsync(id, dto.Reason, "Admin");
                if (!success) return NotFound(new { success = false, message = "Restriction not found" });

                await _securityService.LogAuditAsync("B2B_WALLET_UNBLOCKED", "UNBLOCK_WALLET", "SUCCESS", "127.0.0.1", reason: $"Unblocked wallet for lock ID {id}");
                return Ok(new { success = true, message = "B2B Wallet restriction removed successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var settings = await _securityService.GetSettingsAsync();
            if (settings == null)
            {
                return Ok(new { success = true, data = new { } });
            }

            var data = JsonSerializer.Deserialize<JsonElement>(settings.SettingsJson);
            return Ok(new { success = true, data });
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] JsonElement newSettingsJson)
        {
            var settings = await _context.SecuritySettings.FirstOrDefaultAsync(s => s.Id == 1);
            if (settings == null)
            {
                settings = new SecuritySettings { SettingsJson = "{}" };
                _context.SecuritySettings.Add(settings);
            }

            settings.SettingsJson = newSettingsJson.GetRawText();
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Settings updated successfully." });
        }

        [HttpGet("ip-rules")]
        public async Task<IActionResult> GetIpRules([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string tab = "all")
        {
            var query = _context.SecurityIpRules.AsQueryable();
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new { success = true, pagination = new { total, page, pageSize }, data = items });
        }

        [HttpPost("ip-rules/whitelist")]
        public async Task<IActionResult> AddWhitelist([FromBody] AddWhitelistDto req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.IpAddress))
                return BadRequest(new { success = false, message = "IP Address is required." });

            var rule = new SecurityIpRule
            {
                IpAddress = req.IpAddress,
                Action = "WHITELIST",
                Scope = req.Scope,
                Status = "ACTIVE",
                Source = "MANUAL",
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? "Admin Whitelist" : req.Reason,
                BlockType = req.WhitelistType,
                DurationMinutes = req.DurationMinutes,
                StartTime = DateTime.UtcNow,
                ExpiryTime = req.WhitelistType == "TEMPORARY" && req.DurationMinutes > 0 ? DateTime.UtcNow.AddMinutes(req.DurationMinutes) : null,
                CreatedBy = User.Identity?.Name ?? "Admin"
            };

            _context.SecurityIpRules.Add(rule);
            await _context.SaveChangesAsync();
            await _securityService.LogAuditAsync("WHITELIST_ADDED", "Add IP to Whitelist", "Success", req.IpAddress, reason: rule.Reason);

            return Ok(new { success = true, data = rule });
        }

        [HttpPost("ip-rules/blacklist")]
        public async Task<IActionResult> AddBlacklist([FromBody] AddBlacklistDto req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.IpAddress))
                return BadRequest(new { success = false, message = "IP Address is required." });

            var rule = new SecurityIpRule
            {
                IpAddress = req.IpAddress,
                Action = "BLACKLIST",
                Scope = req.Scope,
                Status = "BLACKLISTED",
                Source = "MANUAL",
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? "Admin Blacklist" : req.Reason,
                BlockType = req.BlockType,
                DurationMinutes = req.DurationMinutes,
                StartTime = DateTime.UtcNow,
                ExpiryTime = req.BlockType == "TEMPORARY" && req.DurationMinutes > 0 ? DateTime.UtcNow.AddMinutes(req.DurationMinutes) : null,
                CreatedBy = User.Identity?.Name ?? "Admin"
            };

            _context.SecurityIpRules.Add(rule);
            await _context.SaveChangesAsync();
            await _securityService.LogAuditAsync("BLACKLIST_ADDED", "Add IP to Blacklist", "Success", req.IpAddress, reason: rule.Reason);

            return Ok(new { success = true, data = rule });
        }

        [HttpPost("ip-rules/block")]
        public async Task<IActionResult> BlockIp([FromBody] AddBlockDto req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.IpAddress))
                return BadRequest(new { success = false, message = "IP Address is required." });

            var rule = new SecurityIpRule
            {
                IpAddress = req.IpAddress,
                Action = "BLOCK",
                Scope = req.Scope,
                Status = "BLOCKED",
                Source = "MANUAL",
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? "Admin Block" : req.Reason,
                BlockType = "TEMPORARY",
                DurationMinutes = req.DurationMinutes,
                StartTime = DateTime.UtcNow,
                ExpiryTime = req.DurationMinutes > 0 ? DateTime.UtcNow.AddMinutes(req.DurationMinutes) : null,
                CreatedBy = User.Identity?.Name ?? "Admin"
            };

            _context.SecurityIpRules.Add(rule);
            await _context.SaveChangesAsync();
            await _securityService.LogAuditAsync("IP_BLOCKED", "Immediate IP Block", "Success", req.IpAddress, reason: rule.Reason);

            return Ok(new { success = true, data = rule });
        }

        [HttpPost("ip-rules/{id}/unblock")]
        public async Task<IActionResult> UnblockIp(int id, [FromBody] UnblockIpDto req)
        {
            var rule = await _context.SecurityIpRules.FindAsync((long)id);
            if (rule == null) return NotFound();

            rule.Status = "UNBLOCKED";
            
            if (req.UnblockAction == "UNBLOCK_AND_WHITELIST")
            {
                var wl = new SecurityIpRule
                {
                    IpAddress = rule.IpAddress,
                    Action = "WHITELIST",
                    Scope = rule.Scope,
                    Status = "ACTIVE",
                    Source = "MANUAL",
                    Reason = req.Reason,
                    BlockType = "PERMANENT",
                    CreatedBy = User.Identity?.Name ?? "Admin"
                };
                _context.SecurityIpRules.Add(wl);
            }

            await _context.SaveChangesAsync();
            await _securityService.LogAuditAsync("IP_UNBLOCKED", "Unblock IP", "Success", rule.IpAddress, reason: req.Reason);

            return Ok(new { success = true });
        }

        [HttpPut("ip-rules/{id}/extend")]
        public async Task<IActionResult> ExtendBlock(int id, [FromBody] ExtendBlockDto req)
        {
            var rule = await _context.SecurityIpRules.FindAsync((long)id);
            if (rule == null) return NotFound();

            var oldExpiry = rule.ExpiryTime;
            rule.DurationMinutes = req.NewDurationMinutes;
            rule.ExpiryTime = DateTime.UtcNow.AddMinutes(req.NewDurationMinutes);
            rule.Status = "BLOCKED";
            rule.Reason = req.Reason;

            await _context.SaveChangesAsync();
            await _securityService.LogAuditAsync("BLOCK_EXTENDED", "Extend Block", "Success", rule.IpAddress, reason: req.Reason);

            return Ok(new { success = true, data = new { oldDurationMinutes = oldExpiry, newDurationMinutes = req.NewDurationMinutes, newExpiryTime = rule.ExpiryTime } });
        }

        [HttpGet("ip-rules/{id}/details")]
        public async Task<IActionResult> GetIpRuleDetails(int id)
        {
            var rule = await _context.SecurityIpRules.FindAsync((long)id);
            if (rule == null) return NotFound();

            var activities = await _context.SecurityAuditLogs
                .Where(x => x.IpAddress == rule.IpAddress)
                .OrderByDescending(x => x.CreatedAt)
                .Take(10)
                .ToListAsync();

            return Ok(new { success = true, data = new { ipInfo = rule, securityActivity = activities } });
        }

        [HttpPatch("ip-rules/{id}/status")]
        public async Task<IActionResult> UpdateIpStatus(int id, [FromBody] IpRuleStatusUpdateDto req)
        {
            var rule = await _context.SecurityIpRules.FindAsync((long)id);
            if (rule == null) return NotFound();

            rule.Status = req.Status.ToUpper();
            await _context.SaveChangesAsync();
            
            await _securityService.LogAuditAsync("IP_STATUS_CHANGED", "Change IP Status", "Success", rule.IpAddress, reason: $"Status changed to {rule.Status}");

            return Ok(new { success = true, data = rule });
        }

        [HttpDelete("ip-rules/{id}")]
        public async Task<IActionResult> DeleteIpRule(int id)
        {
            var rule = await _context.SecurityIpRules.FindAsync((long)id);
            if (rule == null) return NotFound();

            _context.SecurityIpRules.Remove(rule);
            await _context.SaveChangesAsync();
            
            await _securityService.LogAuditAsync("IP_RULE_DELETED", "Delete IP Rule", "Success", rule.IpAddress);

            return Ok(new { success = true });
        }

        [HttpGet("locked-accounts")]
        public async Task<IActionResult> GetLockedAccounts()
        {
            var items = await _context.UserLockouts.Where(x => x.Status == "Locked").ToListAsync();
            return Ok(new { success = true, data = items });
        }

        [HttpPost("account-locks")]
        public async Task<IActionResult> LockAccount([FromBody] ManualLockDto req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.AccountId))
                return BadRequest(new { success = false, message = "Account ID is required." });

            var lockout = await _context.UserLockouts.FirstOrDefaultAsync(l => l.UserId == req.AccountId);
            if (lockout == null)
            {
                var user = await _context.Users.FindAsync(int.Parse(req.AccountId));
                if (user == null) return NotFound(new { success = false, message = "User not found." });

                lockout = new UserLockout
                {
                    UserId = req.AccountId,
                    UserName = user.Email,
                    Email = user.Email,
                    FailedAttempts = 5,
                    MaxAllowedAttempts = 5
                };
                _context.UserLockouts.Add(lockout);
            }

            lockout.Status = "Locked";
            lockout.Reason = !string.IsNullOrWhiteSpace(req.LockReason) ? req.LockReason : "Manually locked by admin";
            lockout.LockedOn = DateTime.UtcNow;
            if (req.DurationMinutes > 0)
                lockout.UnlockAt = DateTime.UtcNow.AddMinutes(req.DurationMinutes);
            else
                lockout.UnlockAt = DateTime.UtcNow.AddYears(99); // "Permanent"

            await _context.SaveChangesAsync();
            await _securityService.LogAuditAsync("ACCOUNT_LOCKED", "Manual Account Lock", "Success", "", userId: req.AccountId, reason: lockout.Reason);

            return Ok(new { success = true, data = lockout });
        }

        [HttpPost("locked-accounts/{id}/unlock")]
        public async Task<IActionResult> UnlockAccount(string id)
        {
            var acc = await _context.UserLockouts.FindAsync(id);
            if (acc == null) return NotFound();

            acc.Status = "Unlocked";
            await _context.SaveChangesAsync();
            
            await _securityService.LogAuditAsync("ACCOUNT_UNLOCKED", "Unlock Account", "Success", "", userId: acc.UserId);

            // Send Email
            if (int.TryParse(acc.UserId, out int uIdInt))
            {
                var user = await _context.Users.FindAsync(uIdInt);
                if (user != null && !string.IsNullOrWhiteSpace(user.Email))
                {
                    try
                    {
                        await _templateService.SendSecurityEmailAsync("ACCOUNT_UNLOCKED", user, "", "Your account has been successfully unlocked.");
                    }
                    catch (Exception ex)
                    {
                        await _securityService.LogAuditAsync("SECURITY_EMAIL_FAILED", "Send Unlock Email", "Failed", "", email: user.Email, reason: ex.Message);
                    }
                }
            }

            return Ok(new { success = true });
        }

        [HttpGet("audit-logs")]
        public async Task<IActionResult> GetAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var query = _context.SecurityAuditLogs.AsQueryable();
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new { success = true, pagination = new { total, page, pageSize }, data = items });
        }
        
        [HttpGet("audit-logs/{id}")]
        public async Task<IActionResult> GetAuditLog(int id)
        {
            var item = await _context.SecurityAuditLogs.FindAsync(id);
            if (item == null) return NotFound();
            return Ok(new { success = true, data = item });
        }
        
        [HttpGet("audit-logs/export")]
        public async Task<IActionResult> ExportAuditLogs()
        {
            return Ok(new { success = true, message = "Export successful" });
        }

        // ---- API RATE LIMITING (Module F) ----
        [HttpGet("api-rules")]
        public async Task<IActionResult> GetApiRules()
        {
            var items = await _context.SecurityApiRules.OrderBy(x => x.UrlPattern).ToListAsync();
            return Ok(new { success = true, data = items });
        }

        [HttpPost("api-rules")]
        public async Task<IActionResult> AddApiRule([FromBody] SecurityApiRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.UrlPattern)) return BadRequest();
            _context.SecurityApiRules.Add(rule);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, data = rule });
        }

        [HttpPut("api-rules/{id}")]
        public async Task<IActionResult> UpdateApiRule(long id, [FromBody] SecurityApiRule req)
        {
            var rule = await _context.SecurityApiRules.FindAsync(id);
            if (rule == null) return NotFound();

            rule.UrlPattern = req.UrlPattern;
            rule.IsException = req.IsException;
            rule.RateLimitValue = req.RateLimitValue;
            rule.RateLimitPeriod = req.RateLimitPeriod;
            rule.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, data = rule });
        }

        [HttpDelete("api-rules/{id}")]
        public async Task<IActionResult> DeleteApiRule(long id)
        {
            var rule = await _context.SecurityApiRules.FindAsync(id);
            if (rule == null) return NotFound();
            _context.SecurityApiRules.Remove(rule);
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        // ---- B2B WALLET CONFIG (Module G) ----
        [HttpGet("b2b-wallet-config")]
        public async Task<IActionResult> GetB2bWalletConfig()
        {
            var config = await _context.SecurityB2bWalletConfigs.FirstOrDefaultAsync();
            if (config == null)
            {
                config = new SecurityB2bWalletConfig();
                _context.SecurityB2bWalletConfigs.Add(config);
                await _context.SaveChangesAsync();
            }
            return Ok(new { success = true, data = config });
        }

        [HttpPut("b2b-wallet-config")]
        public async Task<IActionResult> UpdateB2bWalletConfig([FromBody] SecurityB2bWalletConfig req)
        {
            var config = await _context.SecurityB2bWalletConfigs.FirstOrDefaultAsync();
            if (config == null) return NotFound();

            config.MinWalletAmount = req.MinWalletAmount;
            config.AutoUnblockEnabled = req.AutoUnblockEnabled;
            config.AppliesToTempBlocks = req.AppliesToTempBlocks;
            config.AppliesToAutoBlocks = req.AppliesToAutoBlocks;
            config.AppliesToRateLimit = req.AppliesToRateLimit;
            config.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, data = config });
        }

        // ---- COUNTERS (Module C) ----
        [HttpGet("counters")]
        public async Task<IActionResult> GetCounters([FromQuery] string scope, [FromQuery] string dimensionType, [FromQuery] string dimensionValue)
        {
            var query = _context.SecurityCounters.AsQueryable();
            if (!string.IsNullOrEmpty(scope)) query = query.Where(c => c.Scope == scope);
            if (!string.IsNullOrEmpty(dimensionType)) query = query.Where(c => c.DimensionType == dimensionType);
            if (!string.IsNullOrEmpty(dimensionValue)) query = query.Where(c => c.DimensionValue == dimensionValue);

            var items = await query.ToListAsync();
            return Ok(new { success = true, data = items });
        }

        [HttpPost("counters/{accountId}/reset")]
        public async Task<IActionResult> ResetCounters(string accountId)
        {
            var counters = await _context.SecurityCounters.Where(c => c.DimensionValue.Contains(accountId)).ToListAsync();
            foreach (var counter in counters)
            {
                counter.CurrentCount = 0;
                counter.PeriodEnd = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Counters reset successfully." });
        }

        [HttpGet("account-details/{accountId}")]
        public async Task<IActionResult> GetAccountDetails(string accountId)
        {
            var locks = await _context.SecurityAccountLocks.Where(l => l.AccountId.ToString() == accountId).ToListAsync();
            var counters = await _context.SecurityCounters.Where(c => c.DimensionValue.Contains(accountId)).ToListAsync();
            var logs = await _context.SecurityAuditLogs.Where(a => a.UserOrAdminId == accountId).OrderByDescending(a => a.CreatedAt).Take(20).ToListAsync();
            
            return Ok(new { success = true, data = new { locks, counters, logs } });
        }
    }
}