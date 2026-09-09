using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class WalletService : IWalletService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<WalletService> _logger;

        public WalletService(AppDbContext context, ILogger<WalletService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<WalletTransaction> DebitAsync(int userId, decimal amount, string referenceType, string refCode, string description)
        {
            if (amount <= 0)
                throw new ArgumentException("Debit amount must be greater than zero.", nameof(amount));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                throw new InvalidOperationException($"User with ID {userId} not found.");

            if (!string.Equals(user.WalletStatus, "Active", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("User wallet is not active.");

            if (user.WalletBalance < amount)
                throw new InvalidOperationException($"Insufficient wallet balance. Available: {user.WalletBalance:N2}, Required: {amount:N2}");

            // Double-spend idempotency check
            var existingDebit = await _context.WalletTransactions
                .FirstOrDefaultAsync(t => t.UserId == userId &&
                                          t.RefCode == refCode &&
                                          t.ReferenceType == referenceType &&
                                          t.TransactionType == "Debit" &&
                                          t.Status == "Completed");

            if (existingDebit != null)
            {
                _logger.LogWarning("Duplicate debit attempt detected for User {UserId}, RefCode {RefCode}, RefType {RefType}. Returning existing transaction.",
                    userId, refCode, referenceType);
                return existingDebit;
            }

            user.WalletBalance -= amount;

            var transaction = new WalletTransaction
            {
                UserId = userId,
                TransactionType = "Debit",
                Amount = amount,
                RunningBalance = user.WalletBalance,
                ReferenceType = referenceType,
                RefCode = refCode,
                Description = description,
                Status = "Completed",
                CreatedAt = DateTime.UtcNow
            };

            _context.WalletTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully debited {Amount:N2} from User {UserId}. New Balance: {Balance:N2}. RefCode: {RefCode}",
                amount, userId, user.WalletBalance, refCode);

            return transaction;
        }

        public async Task<WalletTransaction> CreditAsync(int userId, decimal amount, string referenceType, string refCode, string description)
        {
            if (amount <= 0)
                throw new ArgumentException("Credit amount must be greater than zero.", nameof(amount));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                throw new InvalidOperationException($"User with ID {userId} not found.");

            user.WalletBalance += amount;
            user.WalletStatus = "Active";

            var transaction = new WalletTransaction
            {
                UserId = userId,
                TransactionType = "Credit",
                Amount = amount,
                RunningBalance = user.WalletBalance,
                ReferenceType = referenceType,
                RefCode = refCode,
                Description = description,
                Status = "Completed",
                CreatedAt = DateTime.UtcNow
            };

            _context.WalletTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully credited {Amount:N2} to User {UserId}. New Balance: {Balance:N2}. RefCode: {RefCode}",
                amount, userId, user.WalletBalance, refCode);

            return transaction;
        }

        public async Task<WalletTransaction> RefundAsync(int userId, decimal amount, string referenceType, string refCode, string description)
        {
            if (amount <= 0)
                throw new ArgumentException("Refund amount must be greater than zero.", nameof(amount));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                throw new InvalidOperationException($"User with ID {userId} not found.");

            // Idempotency check for refund
            var existingRefund = await _context.WalletTransactions
                .FirstOrDefaultAsync(t => t.UserId == userId &&
                                          t.RefCode == refCode &&
                                          t.ReferenceType == referenceType &&
                                          t.TransactionType == "Refund" &&
                                          t.Status == "Completed");

            if (existingRefund != null)
            {
                _logger.LogWarning("Duplicate refund attempt detected for User {UserId}, RefCode {RefCode}, RefType {RefType}. Returning existing transaction.",
                    userId, refCode, referenceType);
                return existingRefund;
            }

            user.WalletBalance += amount;
            user.WalletStatus = "Active";

            var transaction = new WalletTransaction
            {
                UserId = userId,
                TransactionType = "Refund",
                Amount = amount,
                RunningBalance = user.WalletBalance,
                ReferenceType = referenceType,
                RefCode = refCode,
                Description = description,
                Status = "Completed",
                CreatedAt = DateTime.UtcNow
            };

            _context.WalletTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully refunded {Amount:N2} to User {UserId}. New Balance: {Balance:N2}. RefCode: {RefCode}",
                amount, userId, user.WalletBalance, refCode);

            return transaction;
        }

        public async Task CreditCoinsAsync(int userId, int coins, string referenceType, string refCode, string description)
        {
            if (coins <= 0) return;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return;

            user.PicknbookCoins += coins;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Awarded {Coins} PicknbookCoins to User {UserId}. New Coin Balance: {TotalCoins}",
                coins, userId, user.PicknbookCoins);
        }

        public async Task<WalletSummaryDto> GetSummaryAsync(int userId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new InvalidOperationException($"User with ID {userId} not found.");

            var aggregates = await _context.WalletTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.Status == "Completed")
                .GroupBy(t => t.TransactionType)
                .Select(g => new
                {
                    Type = g.Key,
                    Total = g.Sum(x => x.Amount)
                })
                .ToListAsync();

            decimal totalAdded = aggregates.FirstOrDefault(x => x.Type == "Credit")?.Total ?? 0m;
            decimal totalRefunded = aggregates.FirstOrDefault(x => x.Type == "Refund")?.Total ?? 0m;
            decimal totalUsed = aggregates.FirstOrDefault(x => x.Type == "Debit")?.Total ?? 0m;

            return new WalletSummaryDto
            {
                AvailableBalance = user.WalletBalance,
                PicknbookCoins = user.PicknbookCoins,
                WalletStatus = user.WalletStatus,
                TotalAdded = totalAdded,
                TotalRefunded = totalRefunded,
                TotalUsed = totalUsed
            };
        }

        public async Task<(List<WalletTransactionDto> Items, int TotalCount)> GetTransactionsAsync(int userId, string? type, int page, int pageSize)
        {
            var query = _context.WalletTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId);

            if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.TransactionType == type);
            }

            int totalCount = await query.CountAsync();

            int skip = Math.Max(0, (page - 1) * pageSize);
            var transactions = await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(t => new WalletTransactionDto
                {
                    Id = t.Id,
                    UserId = t.UserId,
                    TransactionType = t.TransactionType,
                    Amount = t.Amount,
                    RunningBalance = t.RunningBalance,
                    ReferenceType = t.ReferenceType,
                    RefCode = t.RefCode,
                    Description = t.Description,
                    Status = t.Status,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();

            return (transactions, totalCount);
        }
    }
}
