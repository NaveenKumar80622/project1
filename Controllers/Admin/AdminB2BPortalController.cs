using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    public class AdminB2BPortalController : AdminApiController
    {
        private readonly AppDbContext _context;

        public AdminB2BPortalController(AppDbContext context)
        {
            _context = context;
        }

        // ---------------- 1. DASHBOARD STATISTICS ----------------
        [HttpGet]
        [Route("~/api/admin/b2b/dashboard/stats")]
        public async Task<IActionResult> GetDashboardStats()
        {
            var totalAgents = await _context.Users.CountAsync(x => x.Role == AuthRoles.Agent);
            var activeAgents = await _context.Users.CountAsync(x => x.Role == AuthRoles.Agent && x.Status == "Active");
            
            var approvedDepositsTotal = await _context.DepositRequests
                .Where(x => x.Status == "Approved" && x.User != null && x.User.Role == AuthRoles.Agent)
                .SumAsync(x => x.Amount);

            // Fetch agent IDs to filter bookings
            var agentUserIds = await _context.Users
                .Where(x => x.Role == AuthRoles.Agent)
                .Select(x => x.Id)
                .ToListAsync();
            var agentUserIdsStr = agentUserIds.Select(id => id.ToString()).ToList();

            var flightBookingsCount = await _context.FlightReservations
                .CountAsync(x => agentUserIdsStr.Contains(x.UserId));
            var busBookingsCount = await _context.BusReservations
                .CountAsync(x => agentUserIdsStr.Contains(x.UserId));
            var totalBookings = flightBookingsCount + busBookingsCount;

            var flightRevenue = await _context.FlightReservations
                .Where(x => agentUserIdsStr.Contains(x.UserId))
                .SumAsync(x => x.FinalAmount > 0 ? x.FinalAmount : x.TotalPriceInr);
            var busRevenue = await _context.BusReservations
                .Where(x => agentUserIdsStr.Contains(x.UserId))
                .SumAsync(x => x.TotalPriceInr);
            var totalRevenue = flightRevenue + busRevenue;

            return Ok(new
            {
                totalAgents,
                activeAgents,
                totalBookings,
                totalRevenue = decimal.Round(totalRevenue, 2, MidpointRounding.AwayFromZero),
                totalDepositsApproved = decimal.Round(approvedDepositsTotal, 2, MidpointRounding.AwayFromZero)
            });
        }

        [HttpGet]
        [Route("~/api/admin/b2b/dashboard/activities")]
        public async Task<IActionResult> GetRecentActivities()
        {
            // Fetch recent deposits
            var recentDeposits = await _context.DepositRequests
                .Include(x => x.User)
                .Where(x => x.User != null && x.User.Role == AuthRoles.Agent)
                .OrderByDescending(x => x.Id)
                .Take(5)
                .Select(x => new
                {
                    ActivityType = "Deposit",
                    Description = $"Agent {x.User!.CompanyName} requested deposit of {x.Amount} ({x.Status})",
                    Date = x.EntryDateUtc
                })
                .ToListAsync();

            // Fetch recent signups
            var recentSignups = await _context.Users
                .Where(x => x.Role == AuthRoles.Agent)
                .OrderByDescending(x => x.Id)
                .Take(5)
                .Select(x => new
                {
                    ActivityType = "Signup",
                    Description = $"Agent {x.CompanyName} registered ({x.Status})",
                    Date = x.CreatedAt
                })
                .ToListAsync();

            var combined = recentDeposits.Concat(recentSignups)
                .OrderByDescending(x => x.Date)
                .Take(5)
                .ToList();

            return Ok(combined);
        }

        // ---------------- 2. MANUAL AGENT CREATION ----------------
        [HttpPost]
        [Route("~/api/admin/agents")]
        public async Task<IActionResult> CreateAgent([FromBody] AdminCreateAgentDto request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var emailExists = await _context.Users.AnyAsync(x => x.Email.ToLower() == normalizedEmail);
            if (emailExists)
            {
                return BadRequest("Email already registered.");
            }

            var agent = new User
            {
                Email = normalizedEmail,
                LoginId = normalizedEmail,
                PhoneNumber = request.PhoneNumber.Trim(),
                FirstName = request.ContactName.Trim(),
                LastName = string.Empty,
                CompanyName = request.CompanyName.Trim(),
                BusinessType = request.BusinessType.Trim(),
                Gstin = request.Gstin?.Trim(),
                City = request.City?.Trim(),
                Role = AuthRoles.Agent,
                Status = "Active", // Direct admin creation defaults to Active
                WalletStatus = "Active",
                WalletBalance = 0.00m,
                CreditLimit = request.CreditLimit,
                MembershipTier = request.MembershipTier.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            // Use BCrypt to hash default password
            agent.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            _context.Users.Add(agent);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Agent created successfully by Admin.",
                agentId = agent.Id,
                email = agent.Email
            });
        }

        // ---------------- 3. EDIT AGENT PROFILE ----------------
        [HttpPut]
        [Route("~/api/admin/agents/{id:int}")]
        public async Task<IActionResult> EditAgent(int id, [FromBody] AdminEditAgentDto request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.Agent);
            if (agent == null)
            {
                return NotFound("Agent not found.");
            }

            agent.FirstName = request.ContactName.Trim();
            agent.PhoneNumber = request.PhoneNumber.Trim();
            agent.CompanyName = request.CompanyName.Trim();
            agent.BusinessType = request.BusinessType.Trim();
            agent.Gstin = request.Gstin?.Trim();
            agent.City = request.City?.Trim();

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Agent details updated successfully."
            });
        }

        [HttpDelete]
        [Route("~/api/admin/agents/{id:int}")]
        public async Task<IActionResult> DeleteAgent(int id)
        {
            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.Agent);
            if (agent == null)
            {
                return NotFound("Agent not found.");
            }

            // Soft-suspend by setting status to Inactive
            agent.Status = "Inactive";
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Agent suspended successfully."
            });
        }

        // ---------------- 4. CONFIG SETTINGS (TIER & LIMIT) ----------------
        [HttpPut]
        [Route("~/api/admin/agents/{id:int}/membership")]
        public async Task<IActionResult> UpdateMembership(int id, [FromBody] UpdateMembershipDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MembershipTier))
            {
                return BadRequest("Invalid membership tier.");
            }

            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.Agent);
            if (agent == null)
            {
                return NotFound("Agent not found.");
            }

            agent.MembershipTier = request.MembershipTier.Trim();
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Membership tier updated to {agent.MembershipTier}."
            });
        }

        [HttpPut]
        [Route("~/api/admin/agents/{id:int}/credit-limit")]
        public async Task<IActionResult> UpdateCreditLimit(int id, [FromBody] UpdateCreditLimitDto request)
        {
            if (request == null || request.CreditLimit < 0)
            {
                return BadRequest("Credit limit must be 0 or positive.");
            }

            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.Agent);
            if (agent == null)
            {
                return NotFound("Agent not found.");
            }

            agent.CreditLimit = request.CreditLimit;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Credit limit updated to {agent.CreditLimit}."
            });
        }

        // ---------------- 5. MANUAL WALLET ADJUSTMENTS ----------------
        [HttpPost]
        [Route("~/api/admin/agents/{id:int}/wallet/adjust")]
        public async Task<IActionResult> AdjustWallet(int id, [FromBody] AdjustWalletDto request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be positive.");
            }

            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.Agent);
            if (agent == null)
            {
                return NotFound("Agent not found.");
            }

            var isCredit = string.Equals(request.Action, "Credit", StringComparison.OrdinalIgnoreCase);
            var isDebit = string.Equals(request.Action, "Debit", StringComparison.OrdinalIgnoreCase);

            if (!isCredit && !isDebit)
            {
                return BadRequest("Action must be 'Credit' or 'Debit'.");
            }

            decimal debitAmount = 0m;
            decimal creditAmount = 0m;

            if (isCredit)
            {
                agent.WalletBalance += request.Amount;
                creditAmount = request.Amount;
            }
            else
            {
                agent.WalletBalance -= request.Amount;
                debitAmount = request.Amount;
            }

            // Create ledger entry
            var ledger = new AgentLedgerEntry
            {
                AgentId = agent.Id,
                TransactionType = "Adjustment",
                ReferenceId = $"ADJ-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}",
                DebitAmount = debitAmount,
                CreditAmount = creditAmount,
                RunningBalance = agent.WalletBalance,
                Description = request.Remark.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.AgentLedgerEntries.Add(ledger);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Wallet adjusted successfully. New Balance: {agent.WalletBalance}",
                runningBalance = agent.WalletBalance
            });
        }

        // ---------------- 6. LEDGER VIEW ----------------
        [HttpGet]
        [Route("~/api/admin/agents/{id:int}/ledger")]
        public async Task<IActionResult> GetAgentLedger(int id)
        {
            var agent = await _context.Users.AnyAsync(x => x.Id == id && x.Role == AuthRoles.Agent);
            if (!agent)
            {
                return NotFound("Agent not found.");
            }

            var ledger = await _context.AgentLedgerEntries
                .Where(x => x.AgentId == id)
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            return Ok(ledger.Select(x => new
            {
                x.Id,
                x.TransactionType,
                x.ReferenceId,
                x.DebitAmount,
                x.CreditAmount,
                x.RunningBalance,
                x.Description,
                x.CreatedAtUtc
            }));
        }

        // ---------------- 7. CUSTOM MARKUP VIEW ----------------
        [HttpGet]
        [Route("~/api/admin/agents/{id:int}/markups")]
        public async Task<IActionResult> GetAgentMarkups(int id)
        {
            var agent = await _context.Users.AnyAsync(x => x.Id == id && x.Role == AuthRoles.Agent);
            if (!agent)
            {
                return NotFound("Agent not found.");
            }

            var markups = await _context.AgentMarkupSettings
                .Where(x => x.AgentId == id)
                .ToListAsync();

            return Ok(markups.Select(x => new
            {
                x.ServiceType,
                x.MarkupType,
                x.MarkupValue,
                x.UpdatedAtUtc
            }));
        }

        // ---------------- 8. CONSOLIDATED BOOKINGS ----------------
        [HttpGet]
        [Route("~/api/admin/b2b/bookings")]
        public async Task<IActionResult> GetB2BBookings([FromQuery] string? agentName)
        {
            var flightReservations = await _context.FlightReservations
                .AsNoTracking()
                .ToListAsync();

            var busReservations = await _context.BusReservations
                .AsNoTracking()
                .ToListAsync();

            // Fetch agents mapped by string ID
            var agents = await _context.Users
                .Where(x => x.Role == AuthRoles.Agent)
                .ToDictionaryAsync(x => x.Id.ToString(), x => x.CompanyName ?? x.FirstName);

            var flightList = flightReservations
                .Where(x => agents.ContainsKey(x.UserId))
                .Select(x => new
                {
                    BookingId = x.Id.ToString(),
                    BookingReference = x.BookingReference,
                    Pnr = x.Pnr,
                    ServiceType = "Flight",
                    AgentName = agents[x.UserId],
                    PassengerName = x.PassengerName,
                    Amount = x.FinalAmount > 0 ? x.FinalAmount : x.TotalPriceInr,
                    Status = x.Status,
                    BookedAt = x.BookedAtUtc
                });

            var busList = busReservations
                .Where(x => agents.ContainsKey(x.UserId))
                .Select(x => new
                {
                    BookingId = x.Id.ToString(),
                    BookingReference = x.BookingReference,
                    Pnr = x.Pnr,
                    ServiceType = "Bus",
                    AgentName = agents[x.UserId],
                    PassengerName = x.PassengerName,
                    Amount = x.TotalPriceInr,
                    Status = x.Status,
                    BookedAt = x.BookedAtUtc
                });

            var combined = flightList.Concat(busList).OrderByDescending(x => x.BookedAt).ToList();

            if (!string.IsNullOrWhiteSpace(agentName))
            {
                var clean = agentName.Trim().ToLowerInvariant();
                combined = combined.Where(x => x.AgentName.ToLower().Contains(clean)).ToList();
            }

            return Ok(combined);
        }

        // ---------------- 9. B2B COMMISSIONS RULES MANAGEMENT ----------------
        [HttpGet]
        [Route("~/api/admin/b2b/commissions")]
        public async Task<IActionResult> GetCommissions()
        {
            var rules = await _context.B2BCommissionRules.ToListAsync();
            return Ok(rules);
        }

        [HttpPost]
        [Route("~/api/admin/b2b/commissions")]
        public async Task<IActionResult> CreateCommission([FromBody] B2BCommissionRule rule)
        {
            if (rule == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            rule.UpdatedAtUtc = DateTime.UtcNow;
            _context.B2BCommissionRules.Add(rule);
            await _context.SaveChangesAsync();

            return Ok(rule);
        }

        [HttpPut]
        [Route("~/api/admin/b2b/commissions/{id:int}")]
        public async Task<IActionResult> UpdateCommission(int id, [FromBody] B2BCommissionRule request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var rule = await _context.B2BCommissionRules.FirstOrDefaultAsync(x => x.Id == id);
            if (rule == null)
            {
                return NotFound("Rule not found.");
            }

            rule.MembershipTier = request.MembershipTier.Trim();
            rule.ServiceType = request.ServiceType.Trim();
            rule.CommissionType = request.CommissionType.Trim();
            rule.CommissionValue = request.CommissionValue;
            rule.IsActive = request.IsActive;
            rule.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(rule);
        }

        [HttpDelete]
        [Route("~/api/admin/b2b/commissions/{id:int}")]
        public async Task<IActionResult> DeleteCommission(int id)
        {
            var rule = await _context.B2BCommissionRules.FirstOrDefaultAsync(x => x.Id == id);
            if (rule == null)
            {
                return NotFound("Rule not found.");
            }

            _context.B2BCommissionRules.Remove(rule);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Commission rule deleted." });
        }
    }

    public class AdminCreateAgentDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string BusinessType { get; set; } = string.Empty;
        public string? Gstin { get; set; }
        public string? City { get; set; }
        public decimal CreditLimit { get; set; }
        public string MembershipTier { get; set; } = "Bronze";
    }

    public class AdminEditAgentDto
    {
        public string ContactName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string BusinessType { get; set; } = string.Empty;
        public string? Gstin { get; set; }
        public string? City { get; set; }
    }

    public class UpdateMembershipDto
    {
        public string MembershipTier { get; set; } = string.Empty;
    }

    public class UpdateCreditLimitDto
    {
        public decimal CreditLimit { get; set; }
    }

    public class AdjustWalletDto
    {
        public decimal Amount { get; set; }
        public string Action { get; set; } = string.Empty; // Credit, Debit
        public string Remark { get; set; } = string.Empty;
    }
}
