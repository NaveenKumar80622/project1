using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class AdminWalletQueryService : IAdminWalletQueryService
    {
        private readonly AppDbContext _context;

        public AdminWalletQueryService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(List<AdminWalletLedgerItemDto> Items, int TotalCount, int TotalPages)> GetLedgerAsync(AdminWalletLedgerRequestDto request)
        {
            // B2C ISOLATION: Only select users with Role == AuthRoles.User
            var query = _context.WalletTransactions
                .Include(t => t.User)
                .Where(t => t.User != null && t.User.Role == AuthRoles.User)
                .AsNoTracking();

            if (request.UserId.HasValue)
            {
                query = query.Where(t => t.UserId == request.UserId.Value);
            }

            if (!string.IsNullOrEmpty(request.TransactionType) && request.TransactionType != "All")
            {
                query = query.Where(t => t.TransactionType == request.TransactionType);
            }

            if (!string.IsNullOrEmpty(request.Status) && request.Status != "All")
            {
                query = query.Where(t => t.Status == request.Status);
            }

            if (!string.IsNullOrEmpty(request.ReferenceType))
            {
                query = query.Where(t => t.ReferenceType == request.ReferenceType);
            }

            if (request.FromDate.HasValue)
            {
                query = query.Where(t => t.CreatedAt >= request.FromDate.Value);
            }

            if (request.ToDate.HasValue)
            {
                query = query.Where(t => t.CreatedAt <= request.ToDate.Value);
            }

            if (!string.IsNullOrEmpty(request.Search))
            {
                var s = request.Search.ToLower();
                query = query.Where(t => t.RefCode.ToLower().Contains(s) || 
                                         t.Description.ToLower().Contains(s) || 
                                         (t.User.FirstName + " " + t.User.LastName).ToLower().Contains(s));
            }

            var totalCount = await query.CountAsync();
            var pageSize = Math.Min(request.PageSize > 0 ? request.PageSize : 20, 100);
            var page = request.Page > 0 ? request.Page : 1;
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var items = await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new AdminWalletLedgerItemDto
                {
                    Id = t.Id,
                    UserId = t.UserId,
                    CustomerName = t.User.FirstName + " " + t.User.LastName,
                    CustomerEmail = t.User.Email,
                    CustomerPhone = t.User.PhoneNumber,
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

            return (items, totalCount, totalPages);
        }

        public async Task<AdminWalletCustomerSummaryDto?> GetCustomerSummaryAsync(int userId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId && u.Role == AuthRoles.User);

            if (user == null)
            {
                return null; // Ensure 404 for non-existent or non-B2C user
            }

            var transactions = await _context.WalletTransactions
                .Where(t => t.UserId == userId && t.Status == "Completed")
                .AsNoTracking()
                .ToListAsync(); // Memory evaluation for complex aggregation

            var totalDeposits = transactions
                .Where(t => t.TransactionType == "Credit" && !t.ReferenceType.ToLower().Contains("refund") && t.ReferenceType != "AdminCredit")
                .Sum(t => t.Amount);

            var totalBookingSpend = transactions
                .Where(t => t.TransactionType == "Debit" && t.ReferenceType != "AdminReset")
                .Sum(t => t.Amount);

            var totalRefunds = transactions
                .Where(t => t.TransactionType == "Credit" && t.ReferenceType.ToLower().Contains("refund"))
                .Sum(t => t.Amount);

            var adminCredits = transactions
                .Where(t => t.TransactionType == "Credit" && t.ReferenceType == "AdminCredit")
                .Sum(t => t.Amount);
                
            var adminResets = transactions
                .Where(t => t.TransactionType == "Debit" && t.ReferenceType == "AdminReset")
                .Sum(t => t.Amount);

            var totalAdjustments = adminCredits - adminResets;

            var recent = transactions
                .OrderByDescending(t => t.CreatedAt)
                .Take(5)
                .Select(t => new AdminWalletLedgerItemDto
                {
                    Id = t.Id,
                    UserId = t.UserId,
                    CustomerName = user.FirstName + " " + user.LastName,
                    CustomerEmail = user.Email,
                    CustomerPhone = user.PhoneNumber,
                    TransactionType = t.TransactionType,
                    Amount = t.Amount,
                    RunningBalance = t.RunningBalance,
                    ReferenceType = t.ReferenceType,
                    RefCode = t.RefCode,
                    Description = t.Description,
                    Status = t.Status,
                    CreatedAt = t.CreatedAt
                })
                .ToList();

            return new AdminWalletCustomerSummaryDto
            {
                UserId = user.Id,
                CustomerName = user.FirstName + " " + user.LastName,
                WalletBalance = user.WalletBalance,
                WalletStatus = user.WalletStatus,
                PicknbookCoins = user.PicknbookCoins,
                TotalDeposits = totalDeposits,
                TotalBookingSpend = totalBookingSpend,
                TotalRefunds = totalRefunds,
                TotalAdjustments = totalAdjustments,
                RecentTransactions = recent
            };
        }
    }
}
