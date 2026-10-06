using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Controllers;
using PickNBook.Api.Data;
using PickNBook.Api.Models;

namespace PickNBook.Api.Controllers.Admin
{
    [Tags("Admin - Security")]
    [Route("api/v1/admin/security/locked-accounts")]
    public class SecurityAdminController : AdminApiController
    {
        private readonly AppDbContext _context;

        public SecurityAdminController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Retrieves all actively locked accounts. Expired locks are lazily unlocked.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLockedAccounts([FromQuery] string? search = null)
        {
            var now = DateTime.UtcNow;

            // 1. Auto-expire past locks
            var expiredLocks = await _context.UserLockouts
                .Where(x => x.Status == "Locked" && x.UnlockAt <= now)
                .ToListAsync();

            if (expiredLocks.Any())
            {
                foreach (var lck in expiredLocks)
                {
                    lck.Status = "Unlocked";
                    lck.FailedAttempts = 0;
                }
                await _context.SaveChangesAsync();
            }

            // 2. Query active locks
            var query = _context.UserLockouts
                .AsNoTracking()
                .Where(x => x.Status == "Locked" && x.UnlockAt > now);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(x =>
                    (x.Email != null && x.Email.ToLower().Contains(term)) ||
                    (x.UserName != null && x.UserName.ToLower().Contains(term)) ||
                    (x.UserId != null && x.UserId.Contains(term)));
            }

            var activeLocks = await query
                .OrderByDescending(x => x.LockedOn)
                .ToListAsync();

            var result = activeLocks.Select(x => new
            {
                x.Id,
                x.UserId,
                x.UserName,
                x.Email,
                x.FailedAttempts,
                x.MaxAllowedAttempts,
                x.LockedOn,
                x.UnlockAt,
                RemainingMinutes = Math.Max(0, (int)Math.Ceiling((x.UnlockAt - now).TotalMinutes)),
                LockType = (x.UnlockAt - x.LockedOn).TotalMinutes > 30 ? "24 Hours" : "15 Minutes",
                x.Reason,
                x.Status
            });

            return Ok(new
            {
                success = true,
                count = activeLocks.Count,
                data = result
            });
        }

        /// <summary>
        /// Manually unlocks an account by Lockout Record ID or Customer User ID.
        /// </summary>
        [HttpPost("{id}/unlock")]
        public async Task<IActionResult> UnlockAccount(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest(new { success = false, message = "Lockout ID or User ID is required." });
            }

            var record = await _context.UserLockouts
                .FirstOrDefaultAsync(x => x.Id == id || x.UserId == id);

            if (record == null)
            {
                return NotFound(new { success = false, message = "No lockout record found for the specified ID." });
            }

            record.Status = "Unlocked";
            record.FailedAttempts = 0;
            record.UnlockAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Account for {record.Email ?? record.UserName} has been unlocked successfully."
            });
        }
    }
}
