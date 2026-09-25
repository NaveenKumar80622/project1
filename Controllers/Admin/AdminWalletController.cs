using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/wallet")]
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    public class AdminWalletController : ControllerBase
    {
        private readonly IAdminWalletQueryService _queryService;

        public AdminWalletController(IAdminWalletQueryService queryService)
        {
            _queryService = queryService;
        }

        [HttpGet("ledger")]
        public async Task<IActionResult> GetLedger([FromQuery] AdminWalletLedgerRequestDto request)
        {
            var result = await _queryService.GetLedgerAsync(request);
            return Ok(new
            {
                totalCount = result.TotalCount,
                page = request.Page > 0 ? request.Page : 1,
                pageSize = request.PageSize > 0 ? (request.PageSize > 100 ? 100 : request.PageSize) : 20,
                totalPages = result.TotalPages,
                items = result.Items
            });
        }

        [HttpGet("customer/{userId}/summary")]
        public async Task<IActionResult> GetCustomerSummary(int userId)
        {
            var summary = await _queryService.GetCustomerSummaryAsync(userId);
            if (summary == null)
            {
                return NotFound(new { message = "B2C customer not found." });
            }
            return Ok(summary);
        }
    }
}
