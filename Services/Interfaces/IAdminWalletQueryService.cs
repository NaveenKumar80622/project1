using System.Collections.Generic;
using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IAdminWalletQueryService
    {
        Task<(List<AdminWalletLedgerItemDto> Items, int TotalCount, int TotalPages)> GetLedgerAsync(AdminWalletLedgerRequestDto request);
        Task<AdminWalletCustomerSummaryDto?> GetCustomerSummaryAsync(int userId);
    }
}
