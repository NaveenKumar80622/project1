using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Services
{
    public class AgentWalletService : IAgentWalletService
    {
        private readonly AppDbContext _context;

        public AgentWalletService(AppDbContext context)
        {
            _context = context;
        }

        public async Task DebitWalletForBookingAsync(int agentId, decimal amount, string bookingReference, string serviceType, string description)
        {
            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == agentId);
            if (agent == null)
            {
                throw new Exception("Agent not found.");
            }

            if (!string.Equals(agent.Role, AuthRoles.Agent, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("User is not an authorized agent.");
            }

            if (!string.Equals(agent.WalletStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Agent wallet is inactive.");
            }

            // Idempotency check: prevent double-debit on retries
            var existingDebit = await _context.AgentLedgerEntries
                .FirstOrDefaultAsync(e => e.AgentId == agentId &&
                                          e.ReferenceId == bookingReference &&
                                          e.TransactionType == "Booking");
            if (existingDebit != null)
            {
                return; // Already debited for this booking
            }

            // Atomic conditional update to prevent negative balance under concurrency
            if (_context.Database.IsRelational())
            {
                int affectedRows = await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE users SET WalletBalance = WalletBalance - {amount} WHERE Id = {agentId} AND WalletBalance >= {amount}");

                if (affectedRows == 0)
                {
                    throw new Exception("Insufficient wallet balance.");
                }

                await _context.Entry(agent).ReloadAsync();
            }
            else
            {
                if (agent.WalletBalance < amount)
                {
                    throw new Exception("Insufficient wallet balance.");
                }
                agent.WalletBalance -= amount;
            }

            // Save ledger entry
            var ledgerEntry = new AgentLedgerEntry
            {
                AgentId = agentId,
                TransactionType = "Booking",
                ReferenceId = bookingReference,
                DebitAmount = amount,
                CreditAmount = 0m,
                RunningBalance = agent.WalletBalance,
                Description = description,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.AgentLedgerEntries.Add(ledgerEntry);
            await _context.SaveChangesAsync();
        }
        public async Task CreditWalletForRefundAsync(int agentId, decimal amount, string bookingReference, string serviceType, string description)
        {
            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == agentId);
            if (agent == null)
            {
                throw new Exception("Agent not found.");
            }

            if (!string.Equals(agent.Role, AuthRoles.Agent, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("User is not an authorized agent.");
            }

            // Idempotency check: prevent double-credit on retries
            var existingCredit = await _context.AgentLedgerEntries
                .FirstOrDefaultAsync(e => e.AgentId == agentId &&
                                          e.ReferenceId == bookingReference &&
                                          e.TransactionType == "Refund");
            if (existingCredit != null)
            {
                return; // Already credited for this booking refund
            }

            // Atomic database-level balance update
            if (_context.Database.IsRelational())
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE users SET WalletBalance = WalletBalance + {amount} WHERE Id = {agentId}");

                await _context.Entry(agent).ReloadAsync();
            }
            else
            {
                agent.WalletBalance += amount;
            }

            // Save ledger entry
            var ledgerEntry = new AgentLedgerEntry
            {
                AgentId = agentId,
                TransactionType = "Refund",
                ReferenceId = bookingReference,
                DebitAmount = 0m,
                CreditAmount = amount,
                RunningBalance = agent.WalletBalance,
                Description = description,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.AgentLedgerEntries.Add(ledgerEntry);
            await _context.SaveChangesAsync();
        }
    }
}
