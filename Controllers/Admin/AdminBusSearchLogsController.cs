using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Config;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/bus-search-logs")]
    [Authorize(Roles = AuthRoles.Admin)]
    public class AdminBusSearchLogsController : ControllerBase
    {
        private readonly AppDbContext dbContext;

        public AdminBusSearchLogsController(AppDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetBusSearchLogs([FromQuery] int limit = 100)
        {
            if (limit <= 0 || limit > 500) limit = 100; // Hard cap

            var logs = await dbContext.BusSearchLogs
                .AsNoTracking()
                .OrderByDescending(x => x.SearchedAtUtc)
                .Take(limit)
                .Select(x => new
                {
                    x.Id,
                    x.UserId,
                    x.UserOrGuestId,
                    x.IsGuest,
                    x.FromCity,
                    x.ToCity,
                    x.JourneyDate,
                    SearchedAtUtc = DateTime.SpecifyKind(x.SearchedAtUtc, DateTimeKind.Utc),
                    SearchedAtIst = DateTime.SpecifyKind(x.SearchedAtUtc, DateTimeKind.Utc).AddHours(5.5)
                })
                .ToListAsync();

            return Ok(logs);
        }
    }
}
