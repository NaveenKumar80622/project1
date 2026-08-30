using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BDashboardController(AppDbContext dbContext) : BaseApiController
    {
        //private const string UserIdHeaderName = "X-User-Id";

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] int recentLimit = 10, [FromQuery] int travelerPendingDays = 7)
        {
            if (recentLimit <= 0)
            {
                return BadRequest("recentLimit must be greater than 0.");
            }

            if (travelerPendingDays <= 0)
            {
                return BadRequest("travelerPendingDays must be greater than 0.");
            }

            recentLimit = Math.Min(recentLimit, 50);
            travelerPendingDays = Math.Min(travelerPendingDays, 60);

            var nowUtc = DateTime.UtcNow;
            var travelerPendingFromUtc = nowUtc.AddDays(-travelerPendingDays);
            var recentCancellationFromUtc = nowUtc.AddDays(-7);
            var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(idClaim))
            {
                return Unauthorized("User is not authenticated.");
            }
            var userId = idClaim;

            var flightTotal = await dbContext.FlightReservations.AsNoTracking()
                .CountAsync(x => x.UserId == userId);
            var flightCancelled = await dbContext.FlightReservations.AsNoTracking()
                .CountAsync(x => x.Status == "Cancelled" && x.UserId == userId);
 
            var flightCompleted = await dbContext.FlightReservations.AsNoTracking()
                .CountAsync(x =>
                    x.Status != "Cancelled" &&
                    x.DepartureTime <= nowUtc &&
                    x.UserId == userId);
 
            var flightUpcoming = await dbContext.FlightReservations.AsNoTracking()
                .CountAsync(x =>
                    x.Status != "Cancelled" &&
                    x.DepartureTime > nowUtc &&
                    x.UserId == userId);
 
            var busTotal = await dbContext.BusReservations.AsNoTracking()
                .CountAsync(x => x.UserId == userId);
 
            var busCancelled = await dbContext.BusReservations.AsNoTracking()
                .CountAsync(x => x.Status == "Cancelled" && x.UserId == userId);
 
            var busCompleted = await dbContext.BusReservations.AsNoTracking()
                .CountAsync(x =>
                    x.Status != "Cancelled" &&
                    x.BusBooking != null &&
                    x.BusBooking.DepartureTime <= nowUtc &&
                    x.UserId == userId);
 
            var busUpcoming = await dbContext.BusReservations.AsNoTracking()
                .CountAsync(x =>
                    x.Status != "Cancelled" &&
                    x.BusBooking != null &&
                    x.BusBooking.DepartureTime > nowUtc &&
                    x.UserId == userId);
 
            var pendingFlightCancellations = await dbContext.FlightReservations.AsNoTracking()
                .CountAsync(x =>
                    x.Status == "Cancelled" &&
                    x.CancelledAtUtc != null &&
                    x.CancelledAtUtc >= recentCancellationFromUtc &&
                    x.UserId == userId);
 
            var pendingBusCancellations = await dbContext.BusReservations.AsNoTracking()
                .CountAsync(x => x.Status == "Cancelled" && x.CancelledAtUtc != null && x.CancelledAtUtc >= recentCancellationFromUtc && x.UserId == userId);
 
            var travelerPendingQuery = dbContext.Travelers.AsNoTracking()
                .Where(x => x.UpdatedAtUtc >= travelerPendingFromUtc && x.UserId == userId);
 
            var pendingTravelerUpdates = await travelerPendingQuery.CountAsync();
 
            var flightRevenue = await dbContext.FlightReservations.AsNoTracking()
                .Where(x => x.Status != "Cancelled" && x.UserId == userId)
                .Select(x => (decimal?)x.TotalPriceInr)
                .SumAsync();
            var busRevenue = await dbContext.BusReservations.AsNoTracking()
                .Where(x => x.Status != "Cancelled" && x.UserId == userId)
                .Select(x => (decimal?)x.TotalPriceInr)
                .SumAsync();
 
            var flightCancelledValue = await dbContext.FlightReservations.AsNoTracking()
                .Where(x => x.Status == "Cancelled" && x.UserId == userId)
                .Select(x => (decimal?)x.TotalPriceInr)
                .SumAsync();
            var busCancelledValue = await dbContext.BusReservations.AsNoTracking()
                .Where(x => x.Status == "Cancelled" && x.UserId == userId)
                .Select(x => (decimal?)x.TotalPriceInr)
                .SumAsync();
 
            var flightBookedEvents = await dbContext.FlightReservations.AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.BookedAtUtc)
                .Take(recentLimit)
                .Select(x => new { x.BookingReference, x.PassengerName, x.BookedAtUtc, x.TotalPriceInr })
                .ToListAsync();
 
            var flightCancelledEvents = await dbContext.FlightReservations.AsNoTracking()
                .Where(x => x.CancelledAtUtc != null && x.UserId == userId)
                .OrderByDescending(x => x.CancelledAtUtc)
                .Take(recentLimit)
                .Select(x => new { x.BookingReference, x.PassengerName, CancelledAtUtc = x.CancelledAtUtc!.Value })
                .ToListAsync();
 
            var busBookedEvents = await dbContext.BusReservations.AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.BookedAtUtc)
                .Take(recentLimit)
                .Select(x => new { x.BookingReference, x.PassengerName, x.BookedAtUtc, x.TotalPriceInr })
                .ToListAsync();
 
            var busCancelledEvents = await dbContext.BusReservations.AsNoTracking()
                .Where(x => x.CancelledAtUtc != null && x.UserId == userId)
                .OrderByDescending(x => x.CancelledAtUtc)
                .Take(recentLimit)
                .Select(x => new { x.BookingReference, x.PassengerName, CancelledAtUtc = x.CancelledAtUtc!.Value })
                .ToListAsync();
 
            var travelerRecentQuery = dbContext.Travelers.AsNoTracking()
                .Where(x => x.UserId == userId);

            var travelerEvents = await travelerRecentQuery
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Take(recentLimit)
                .Select(x => new
                {
                    x.UserId,
                    x.FirstName,
                    x.LastName,
                    x.CreatedAtUtc,
                    x.UpdatedAtUtc
                })
                .ToListAsync();

            var flightRoutes = await dbContext.FlightRouteStats.AsNoTracking()
                .OrderByDescending(x => x.BookingCount)
                .ThenByDescending(x => x.SearchCount)
                .Take(10)
                .ToListAsync();

            var busRoutes = await dbContext.BusRouteStats.AsNoTracking()
                .OrderByDescending(x => x.BookingCount)
                .ThenByDescending(x => x.SearchCount)
                .Take(10)
                .ToListAsync();

            var totalBookings = flightTotal + busTotal;
            var totalCompleted = flightCompleted + busCompleted;
            var completionRate = totalBookings == 0
                ? 0m
                : decimal.Round((totalCompleted * 100m) / totalBookings, 2, MidpointRounding.AwayFromZero);

            var pendingCancellations = pendingFlightCancellations + pendingBusCancellations;

            var totalRevenueInr = (flightRevenue ?? 0m) + (busRevenue ?? 0m);
            var totalCancelledValueInr = (flightCancelledValue ?? 0m) + (busCancelledValue ?? 0m);

            var recentUpdates = new List<DashboardRecentUpdateDto>();

            recentUpdates.AddRange(flightBookedEvents.Select(x => new DashboardRecentUpdateDto
            {
                Type = "FlightBooking",
                Message = $"Flight booked ({x.BookingReference}) by {x.PassengerName}",
                OccurredAtUtc = x.BookedAtUtc
            }));

            recentUpdates.AddRange(flightCancelledEvents.Select(x => new DashboardRecentUpdateDto
            {
                Type = "FlightCancellation",
                Message = $"Flight cancelled ({x.BookingReference}) by {x.PassengerName}",
                OccurredAtUtc = x.CancelledAtUtc
            }));

            recentUpdates.AddRange(busBookedEvents.Select(x => new DashboardRecentUpdateDto
            {
                Type = "BusBooking",
                Message = $"Bus booked ({x.BookingReference}) by {x.PassengerName}",
                OccurredAtUtc = x.BookedAtUtc
            }));

            recentUpdates.AddRange(busCancelledEvents.Select(x => new DashboardRecentUpdateDto
            {
                Type = "BusCancellation",
                Message = $"Bus cancelled ({x.BookingReference}) by {x.PassengerName}",
                OccurredAtUtc = x.CancelledAtUtc
            }));

            recentUpdates.AddRange(travelerEvents.Select(x =>
            {
                var createdEvent = Math.Abs((x.UpdatedAtUtc - x.CreatedAtUtc).TotalSeconds) < 1;
                var eventType = createdEvent ? "TravelerAdded" : "TravelerUpdated";
                return new DashboardRecentUpdateDto
                {
                    Type = eventType,
                    Message = $"{eventType} for {x.FirstName} {x.LastName}",
                    OccurredAtUtc = x.UpdatedAtUtc
                };
            }));

            var topRoutes = new List<DashboardTopRouteDto>();
            topRoutes.AddRange(flightRoutes.Select(x => new DashboardTopRouteDto
            {
                TripType = "Flight",
                FromCity = x.FromCity,
                ToCity = x.ToCity,
                SearchCount = x.SearchCount,
                BookingCount = x.BookingCount,
                Score = x.SearchCount + (x.BookingCount * 3)
            }));

            topRoutes.AddRange(busRoutes.Select(x => new DashboardTopRouteDto
            {
                TripType = "Bus",
                FromCity = x.FromCity,
                ToCity = x.ToCity,
                SearchCount = x.SearchCount,
                BookingCount = x.BookingCount,
                Score = x.SearchCount + (x.BookingCount * 3)
            }));

            var response = new DashboardSummaryDto
            {
                TotalBookings = totalBookings,
                CompletionRatePercent = completionRate,
                PendingActions = new DashboardPendingActionsDto
                {
                    Cancellations = pendingCancellations,
                    Deposits = 0,
                    TravelerUpdates = pendingTravelerUpdates,
                    Total = pendingCancellations + pendingTravelerUpdates
                },
                RevenueSnapshot = new DashboardRevenueSnapshotDto
                {
                    TotalRevenueInr = totalRevenueInr,
                    TotalSavingsInr = 0m,
                    CancelledValueInr = totalCancelledValueInr
                },
                FlightBookings = new DashboardBookingBreakdownDto
                {
                    Completed = flightCompleted,
                    Upcoming = flightUpcoming,
                    Cancelled = flightCancelled,
                    Total = flightTotal
                },
                BusBookings = new DashboardBookingBreakdownDto
                {
                    Completed = busCompleted,
                    Upcoming = busUpcoming,
                    Cancelled = busCancelled,
                    Total = busTotal
                },
                RecentUpdates = recentUpdates
                    .OrderByDescending(x => x.OccurredAtUtc)
                    .Take(recentLimit)
                    .ToList(),
                RecentUpdateCounters = new DashboardRecentUpdateCounterDto
                {
                    BookingUpdates = flightBookedEvents.Count + busBookedEvents.Count,
                    CancellationUpdates = flightCancelledEvents.Count + busCancelledEvents.Count,
                    TravelerUpdates = travelerEvents.Count,
                    WalletPaymentUpdates = 0,
                    BankAddUpdates = 0
                },
                TopRoutes = new List<DashboardTopRouteDto>()
            };

            return Ok(response);
        }
    }


}
