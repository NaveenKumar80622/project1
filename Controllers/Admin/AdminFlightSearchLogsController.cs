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
    [Route("api/admin/flight-search-logs")]
    [Authorize(Roles = AuthRoles.Admin)]
    public class AdminFlightSearchLogsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public AdminFlightSearchLogsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetFlightSearchLogs([FromQuery] int limit = 100)
        {
            if (limit <= 0 || limit > 500) limit = 100;

            var logs = await _dbContext.FlightSearchLogs
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
                    x.DepartDate,
                    x.ReturnDate,
                    x.Adults,
                    x.Children,
                    x.Infants,
                    x.TripType,
                    x.TraceId,
                    x.EndUserIp,
                    SearchedAtUtc = DateTime.SpecifyKind(x.SearchedAtUtc, DateTimeKind.Utc),
                    SearchedAtIst = DateTime.SpecifyKind(x.SearchedAtUtc, DateTimeKind.Utc).AddHours(5.5)
                })
                .ToListAsync();

            return Ok(logs);
        }
    }
}
