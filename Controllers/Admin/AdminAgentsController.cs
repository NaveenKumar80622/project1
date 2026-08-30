using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    [Route("api/admin/agents")]
    public class AdminAgentsController(AppDbContext dbContext) : AdminApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetAgents(
            [FromQuery] string? status,
            [FromQuery] string? search)
        {
            var query = dbContext.Users
                .AsNoTracking()
                .Where(x => x.Role == AuthRoles.Agent);

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var cleanSearch = search.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    (x.CompanyName != null && x.CompanyName.ToLower().Contains(cleanSearch)) ||
                    x.Email.ToLower().Contains(cleanSearch) ||
                    x.PhoneNumber.Contains(cleanSearch) ||
                    (x.FirstName + " " + x.LastName).ToLower().Contains(cleanSearch)
                );
            }

            var agents = await query
                .OrderByDescending(x => x.Id)
                .Select(x => new AgentResponseDto
                {
                    Id = x.Id,
                    CompanyName = x.CompanyName ?? string.Empty,
                    BusinessType = x.BusinessType ?? string.Empty,
                    ContactName = $"{x.FirstName} {x.LastName}".Trim(),
                    Email = x.Email,
                    PhoneNumber = x.PhoneNumber,
                    Gstin = x.Gstin ?? string.Empty,
                    City = x.City ?? string.Empty,
                    Status = x.Status,
                    WalletStatus = x.WalletStatus,
                    WalletBalance = x.WalletBalance,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            return Ok(agents);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAgentById(int id)
        {
            var user = await dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.Agent);

            if (user == null)
            {
                return NotFound("Agent not found.");
            }

            var dto = new AgentResponseDto
            {
                Id = user.Id,
                CompanyName = user.CompanyName ?? string.Empty,
                BusinessType = user.BusinessType ?? string.Empty,
                ContactName = $"{user.FirstName} {user.LastName}".Trim(),
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Gstin = user.Gstin ?? string.Empty,
                City = user.City ?? string.Empty,
                Status = user.Status,
                WalletStatus = user.WalletStatus,
                WalletBalance = user.WalletBalance,
                CreatedAt = user.CreatedAt
            };

            return Ok(dto);
        }

        [HttpPut("{id:int}/status")]
        public async Task<IActionResult> UpdateAgentStatus(int id, [FromBody] UpdateAgentStatusRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await dbContext.Users
                .FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.Agent);

            if (user == null)
            {
                return NotFound("Agent not found.");
            }

            var newStatus = request.Status.Trim();
            if (string.Equals(newStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                user.Status = "Active";
            }
            else if (string.Equals(newStatus, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                user.Status = "Inactive";
            }
            else if (string.Equals(newStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                user.Status = "Rejected";
            }
            else if (string.Equals(newStatus, "PendingApproval", StringComparison.OrdinalIgnoreCase))
            {
                user.Status = "PendingApproval";
            }
            else
            {
                return BadRequest("Invalid status. Supported values are Active, Inactive, Rejected, PendingApproval.");
            }

            await dbContext.SaveChangesAsync();

            return Ok(new { message = $"Agent status updated to {user.Status}.", status = user.Status });
        }

        [HttpPut("{id:int}/wallet-status")]
        public async Task<IActionResult> UpdateAgentWalletStatus(int id, [FromBody] UpdateAgentWalletStatusRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await dbContext.Users
                .FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.Agent);

            if (user == null)
            {
                return NotFound("Agent not found.");
            }

            var newWalletStatus = request.WalletStatus.Trim();
            if (string.Equals(newWalletStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                user.WalletStatus = "Active";
            }
            else if (string.Equals(newWalletStatus, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                user.WalletStatus = "Inactive";
            }
            else
            {
                return BadRequest("Invalid wallet status. Supported values are Active, Inactive.");
            }

            await dbContext.SaveChangesAsync();

            return Ok(new { message = $"Agent wallet status updated to {user.WalletStatus}.", walletStatus = user.WalletStatus });
        }
    }
}
