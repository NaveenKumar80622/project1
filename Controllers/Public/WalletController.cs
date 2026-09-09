using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Controllers.Public
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WalletController : ControllerBase
    {
        private readonly IWalletService _walletService;
        private readonly AppDbContext _context;

        public WalletController(IWalletService walletService, AppDbContext context)
        {
            _walletService = walletService;
            _context = context;
        }

        private int? GetCurrentUserId()
        {
            var rawId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value
                        ?? User.FindFirst("userId")?.Value;

            if (int.TryParse(rawId, out int id))
                return id;

            return null;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            try
            {
                var summary = await _walletService.GetSummaryAsync(userId.Value);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> GetTransactions(
            [FromQuery] string? type,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var (items, totalCount) = await _walletService.GetTransactionsAsync(userId.Value, type, page, pageSize);

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                items
            });
        }

        [HttpGet("deposits")]
        public async Task<IActionResult> GetDeposits()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            var deposits = await _context.DepositRequests
                .AsNoTracking()
                .Where(d => d.UserId == userId.Value)
                .OrderByDescending(d => d.Id)
                .Select(d => new
                {
                    d.Id,
                    d.Amount,
                    d.Type,
                    d.Status,
                    d.UserRemark,
                    d.AdminRemark,
                    EntryDate = d.EntryDateUtc,
                    d.TransactionDate
                })
                .ToListAsync();

            return Ok(deposits);
        }

        [HttpPost("deposits")]
        public async Task<IActionResult> SubmitDeposit([FromBody] SubmitDepositRequestDto request)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized(new { message = "User not identified." });

            if (request == null || request.Amount <= 0)
                return BadRequest(new { message = "Deposit amount must be greater than zero." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
            if (user == null)
                return NotFound(new { message = "User record not found." });

            var deposit = new DepositRequest
            {
                UserId = user.Id,
                Amount = request.Amount,
                Type = string.IsNullOrWhiteSpace(request.Type) ? "Bank Transfer" : request.Type.Trim(),
                Status = "Pending",
                UserRemark = request.Remark,
                EntryDateUtc = DateTime.UtcNow,
                TransactionDate = request.TransactionDate ?? DateTime.UtcNow
            };

            _context.DepositRequests.Add(deposit);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Deposit request submitted successfully. Pending Admin verification.",
                depositId = deposit.Id
            });
        }
    }
}
