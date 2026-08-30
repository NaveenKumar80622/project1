using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

[Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
public class DepositRequestsController : AdminApiController
{
    private readonly AppDbContext _context;

    public DepositRequestsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Route("~/api/admin/deposits")]
    public async Task<IActionResult> GetDepositRequests(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? search)
    {
        var query = _context.DepositRequests
            .Include(x => x.User)
            .AsNoTracking();

        // Status Filter
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Status == status);
        }

        // Type Filter
        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Type == type);
        }

        // Search Filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchClean = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.User != null && (x.User.FirstName + " " + x.User.LastName).ToLower().Contains(searchClean)) ||
                (x.UserRemark != null && x.UserRemark.ToLower().Contains(searchClean)));
        }

        var requests = await query
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        var list = requests.Select(x => new DepositRequestDto
        {
            Id = x.Id,
            User = x.User != null ? $"{x.User.FirstName} {x.User.LastName} ({x.User.Id})" : $"User ({x.UserId})",
            Amount = x.Amount,
            Type = x.Type,
            Status = x.Status,
            UserRemark = x.UserRemark ?? "-",
            AdminRemark = x.AdminRemark ?? string.Empty,
            EntryDate = x.EntryDateUtc.ToString("hh:mm tt , dd MMM yyyy"),
            TransactionDate = x.TransactionDate.ToString("dd MMM yyyy")
        }).ToList();

        return Ok(list);
    }

    [HttpPut]
    [Route("~/api/admin/deposits/{id:long}/status")]
    public async Task<IActionResult> CycleDepositStatus(long id)
    {
        var deposit = await _context.DepositRequests
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (deposit == null)
        {
            return NotFound("Deposit request not found.");
        }

        // Cycle status: Pending -> Approved -> Rejected -> Pending
        var oldStatus = deposit.Status;
        string newStatus;

        if (string.Equals(oldStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            newStatus = "Approved";
        }
        else if (string.Equals(oldStatus, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            newStatus = "Rejected";
        }
        else
        {
            newStatus = "Pending";
        }

        deposit.Status = newStatus;

        // If Approved, credit wallet of user
        if (string.Equals(newStatus, "Approved", StringComparison.OrdinalIgnoreCase) && 
            !string.Equals(oldStatus, "Approved", StringComparison.OrdinalIgnoreCase) &&
            deposit.User != null)
        {
            deposit.User.WalletBalance += deposit.Amount;
            deposit.User.WalletStatus = "Active"; // Ensure wallet becomes active

            // Add Ledger Entry if Agent
            if (deposit.User.Role == AuthRoles.Agent)
            {
                var ledger = new AgentLedgerEntry
                {
                    AgentId = deposit.User.Id,
                    TransactionType = "Deposit",
                    ReferenceId = deposit.Id.ToString(),
                    DebitAmount = 0m,
                    CreditAmount = deposit.Amount,
                    RunningBalance = deposit.User.WalletBalance,
                    Description = $"Deposit request approved by Admin. Method: {deposit.Type}",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.AgentLedgerEntries.Add(ledger);
            }
        }
        // If transitioning away from Approved, deduct if it was previously approved
        else if (!string.Equals(newStatus, "Approved", StringComparison.OrdinalIgnoreCase) && 
                 string.Equals(oldStatus, "Approved", StringComparison.OrdinalIgnoreCase) &&
                 deposit.User != null)
        {
            deposit.User.WalletBalance -= deposit.Amount;

            // Add Ledger Entry to log reversal if Agent
            if (deposit.User.Role == AuthRoles.Agent)
            {
                var ledger = new AgentLedgerEntry
                {
                    AgentId = deposit.User.Id,
                    TransactionType = "Adjustment",
                    ReferenceId = deposit.Id.ToString(),
                    DebitAmount = deposit.Amount,
                    CreditAmount = 0m,
                    RunningBalance = deposit.User.WalletBalance,
                    Description = $"Deposit reversal. Status changed from Approved to {newStatus}",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.AgentLedgerEntries.Add(ledger);
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Status set to {newStatus}.", status = deposit.Status });
    }

    [HttpPut]
    [Route("~/api/admin/deposits/{id:long}/remark")]
    public async Task<IActionResult> UpdateAdminRemark(long id, [FromBody] UpdateAdminRemarkRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var deposit = await _context.DepositRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (deposit == null)
        {
            return NotFound("Deposit request not found.");
        }

        deposit.AdminRemark = request.AdminRemark.Trim();
        await _context.SaveChangesAsync();

        return Ok(new { message = "Admin remark updated successfully.", adminRemark = deposit.AdminRemark });
    }
}
