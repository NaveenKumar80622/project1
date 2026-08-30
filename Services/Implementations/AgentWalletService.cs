using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using System;
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

            if (agent.WalletBalance < amount)
            {
                throw new Exception("Insufficient wallet balance.");
            }

            // Deduct balance
            agent.WalletBalance -= amount;

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

            // Credit balance
            agent.WalletBalance += amount;

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
