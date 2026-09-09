using System.Collections.Generic;
using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IWalletService
    {
        Task<WalletTransaction> DebitAsync(int userId, decimal amount, string referenceType, string refCode, string description);
        Task<WalletTransaction> CreditAsync(int userId, decimal amount, string referenceType, string refCode, string description);
        Task<WalletTransaction> RefundAsync(int userId, decimal amount, string referenceType, string refCode, string description);
        Task CreditCoinsAsync(int userId, int coins, string referenceType, string refCode, string description);
        Task<WalletSummaryDto> GetSummaryAsync(int userId);
        Task<(List<WalletTransactionDto> Items, int TotalCount)> GetTransactionsAsync(int userId, string? type, int page, int pageSize);
    }
}
