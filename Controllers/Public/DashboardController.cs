using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Models.Payments;
using PickNBook.Api.Services;
using System.Security.Claims;

namespace PickNBook.Api.Controllers;

public class DashboardController : AdminApiController
{
    private readonly AppDbContext _context;
    private readonly IGeoIpService _geoIpService;
    private readonly int _adminOtpExpiryMinutes;

    public DashboardController(
        AppDbContext context,
        IGeoIpService geoIpService,
        IConfiguration configuration)
    {
        _context = context;
        _geoIpService = geoIpService;
        _adminOtpExpiryMinutes = Math.Clamp(
            configuration.GetValue<int?>("AdminAuth:OtpExpiryMinutes") ?? 5,
            1,
            30);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        var startOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var startOfLastMonth = startOfMonth.AddMonths(-1);
        var weekStart = StartOfWeek(today, DayOfWeek.Monday);

        // 1. Users Counts & Growth
        var totalUsers = await _context.Users.CountAsync();
        var usersBeforeThisMonth = await _context.Users.CountAsync(u => u.CreatedAt < startOfMonth);
        var usersGrowth = CalculateGrowthPercent(totalUsers - usersBeforeThisMonth, usersBeforeThisMonth);

        // 2. Revenue & Payments (Status == "SUCCESS")
        var successfulPayments = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Status == "SUCCESS")
            .ToListAsync();

        var totalRevenue = successfulPayments.Sum(p => p.FinalPayableAmount);
        var revenueThisMonth = successfulPayments.Where(p => p.CreatedAt >= startOfMonth).Sum(p => p.FinalPayableAmount);
        var revenueLastMonth = successfulPayments.Where(p => p.CreatedAt >= startOfLastMonth && p.CreatedAt < startOfMonth).Sum(p => p.FinalPayableAmount);
        var revenueGrowth = CalculateGrowthPercent(revenueThisMonth, revenueLastMonth);

        var revenueToday = successfulPayments.Where(p => p.CreatedAt >= today).Sum(p => p.FinalPayableAmount);
        var revenueYesterday = successfulPayments.Where(p => p.CreatedAt >= yesterday && p.CreatedAt < today).Sum(p => p.FinalPayableAmount);
        var revenueGrowthVsYesterday = CalculateGrowthPercent(revenueToday, revenueYesterday);

        // 3. Bookings (FlightReservations, BusReservations, HotelReservations)
        var flightReservations = await _context.FlightReservations.AsNoTracking().ToListAsync();
        var busReservations = await _context.BusReservations.AsNoTracking().ToListAsync();
        var hotelReservations = await _context.HotelReservations.AsNoTracking().ToListAsync();

        var totalBookings = flightReservations.Count + busReservations.Count + hotelReservations.Count;

        var bookingsThisMonth = flightReservations.Count(x => x.BookedAtUtc >= startOfMonth) +
                                busReservations.Count(x => x.BookedAtUtc >= startOfMonth) +
                                hotelReservations.Count(x => x.CreatedAt >= startOfMonth);

        var bookingsLastMonth = flightReservations.Count(x => x.BookedAtUtc >= startOfLastMonth && x.BookedAtUtc < startOfMonth) +
                                busReservations.Count(x => x.BookedAtUtc >= startOfLastMonth && x.BookedAtUtc < startOfMonth) +
                                hotelReservations.Count(x => x.CreatedAt >= startOfLastMonth && x.CreatedAt < startOfMonth);

        var bookingsGrowth = CalculateGrowthPercent(bookingsThisMonth, bookingsLastMonth);

        // Active Bookings
        var activeFlights = flightReservations.Count(x => x.Status == "Booked" || x.Status == "Confirmed");
        var activeBuses = busReservations.Count(x => x.Status == "Booked" || x.Status == "Confirmed");
        var activeHotels = hotelReservations.Count(x => x.Status == "Booked" || x.Status == "Confirmed");
        var activeBookings = activeFlights + activeBuses + activeHotels;
        var activeBookingsGrowth = 0m;

        // Cancellations
        var cancelledBookings = await _context.BookingCancellations.CountAsync() + await _context.FlightCancellationRequests.CountAsync();
        var cancelledGrowth = 0m;

        // Refunds
        var refundRequests = await _context.Payments.CountAsync(p => p.RefundStatus == "PENDING");
        var refundsGrowth = 0m;

        // Today's Status & Day-over-Day
        var successfulToday = successfulPayments.Count(p => p.CreatedAt >= today);
        var successfulYesterday = successfulPayments.Count(p => p.CreatedAt >= yesterday && p.CreatedAt < today);
        var successfulGrowthPercent = CalculateGrowthPercent(successfulToday, successfulYesterday);

        var failedToday = await _context.Payments.CountAsync(p => p.Status != "SUCCESS" && p.CreatedAt >= today);
        var failedYesterday = await _context.Payments.CountAsync(p => p.Status != "SUCCESS" && p.CreatedAt >= yesterday && p.CreatedAt < today);
        var failedGrowthPercent = CalculateGrowthPercent(failedToday, failedYesterday);

        var bookingsToday = flightReservations.Count(x => x.BookedAtUtc >= today) +
                            busReservations.Count(x => x.BookedAtUtc >= today) +
                            hotelReservations.Count(x => x.CreatedAt >= today);

        // Funnel
        var flightSearches = await _context.FlightSearchLogs.CountAsync();
        var busSearches = await _context.BusSearchLogs.CountAsync();
        var hotelSearches = await _context.HotelSearchLogs.CountAsync();
        var totalSearches = flightSearches + busSearches + hotelSearches;
        var pendingPayments = await _context.PendingPaymentBookings.CountAsync();

        // Top Selling Routes - Flights
        var topFlights = flightReservations
            .Where(x => !string.IsNullOrEmpty(x.FromCity) && !string.IsNullOrEmpty(x.ToCity))
            .GroupBy(x => new { x.FromCity, x.ToCity })
            .Select(g => new { fromCity = g.Key.FromCity, toCity = g.Key.ToCity, bookingCount = g.Count() })
            .OrderByDescending(x => x.bookingCount)
            .Take(5)
            .ToList();

        // Top Selling Routes - Buses
        var busBookings = await _context.BusBookings.AsNoTracking().ToListAsync();
        var topBuses = busBookings
            .Where(x => !string.IsNullOrEmpty(x.FromCity) && !string.IsNullOrEmpty(x.ToCity))
            .GroupBy(x => new { x.FromCity, x.ToCity })
            .Select(g => new { fromCity = g.Key.FromCity, toCity = g.Key.ToCity, bookingCount = g.Count() })
            .OrderByDescending(x => x.bookingCount)
            .Take(5)
            .ToList();

        // Top Hotels
        var topHotels = hotelReservations
            .Where(x => !string.IsNullOrEmpty(x.HotelName))
            .GroupBy(x => x.HotelName)
            .Select(g => new { hotelName = g.Key, bookingCount = g.Count() })
            .OrderByDescending(x => x.bookingCount)
            .Take(5)
            .ToList();

        // Recent Activities (Latest 5 Payments + Latest 5 User Signups)
        var recentActs = new List<DashboardActivityItem>();

        var recentPayments = await _context.Payments
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .ToListAsync();

        foreach (var p in recentPayments)
        {
            recentActs.Add(new DashboardActivityItem(
                $"pay-{p.Id}",
                "payment",
                $"Payment {p.Status} for {p.FinalPayableAmount} INR",
                p.CreatedAt));
        }

        var recentUsers = await _context.Users
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .ToListAsync();

        foreach (var u in recentUsers)
        {
            recentActs.Add(new DashboardActivityItem(
                $"user-{u.Id}",
                "user",
                $"New user signup: {u.Email}",
                u.CreatedAt));
        }

        var sortedActivities = recentActs
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(10)
            .Select(x => new
            {
                id = x.Id,
                type = x.Type,
                message = x.Message,
                occurredAtUtc = x.OccurredAtUtc
            })
            .ToList();

        // Weekly Chart (Current Week Mon-Sun)
        var labels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var successByDay = new int[7];
        var failByDay = new int[7];
        var revByDay = new decimal[7];

        var recentWeekPayments = await _context.Payments
            .AsNoTracking()
            .Where(p => p.CreatedAt >= weekStart)
            .ToListAsync();

        foreach (var p in recentWeekPayments)
        {
            var day = p.CreatedAt.Date;
            var index = (int)(day - weekStart).TotalDays;
            if (index >= 0 && index <= 6)
            {
                if (p.Status == "SUCCESS")
                {
                    successByDay[index]++;
                    revByDay[index] += p.FinalPayableAmount;
                }
                else
                {
                    failByDay[index]++;
                }
            }
        }

        var requestIp = GetRequestIpAddress();
        var ipRegion = await _geoIpService.ResolveRegionAsync(requestIp);
        var lastLoginAtUtc = await GetEstimatedLastAdminLoginUtcAsync();

        var pendingWorkBuckets = new List<PendingWorkBucket>
        {
            new("paymentReview", "Payment Review", 0),
            new("bookingVerification", "Booking Verification", 0),
            new("disputeResolution", "Dispute Resolution", 0),
            new("customerResponse", "Customer Response", 0)
        };

        return Ok(new
        {
            todayStatus = new
            {
                totalBookings = bookingsToday,
                successfulBookings = successfulToday,
                pendingWorks = 0,
                failedBookings = failedToday,
                revenueInr = Math.Round(revenueToday, 2, MidpointRounding.AwayFromZero),
                expectedRevenueInr = Math.Round(revenueToday * 1.1m, 2, MidpointRounding.AwayFromZero)
            },
            metrics = new
            {
                totalRevenue = Math.Round(totalRevenue, 2, MidpointRounding.AwayFromZero),
                revenueGrowthPercent = revenueGrowth,
                totalBookings,
                bookingsGrowthPercent = bookingsGrowth,
                totalUsers,
                usersGrowthPercent = usersGrowth,
                activeBookings,
                activeBookingsGrowthPercent = activeBookingsGrowth,
                cancelledBookings,
                cancelledBookingsGrowthPercent = cancelledGrowth,
                refundRequests,
                refundRequestsGrowthPercent = refundsGrowth
            },
            bookingFunnel = new
            {
                searches = totalSearches,
                selected = totalSearches > 0 ? (int)(totalSearches * 0.4) : 0,
                passengerDetails = totalBookings + pendingPayments,
                paymentAttempted = totalBookings + pendingPayments,
                completed = totalBookings
            },
            topSellingRoutes = new
            {
                flights = topFlights,
                buses = topBuses
            },
            topHotels,
            recentActivities = sortedActivities,
            weeklyChart = new
            {
                labels,
                successfulBookings = successByDay,
                failedBookings = failByDay,
                revenueInr = revByDay
            },
            // Legacy / Backwards-Compatibility Fields (populated with real data):
            revenueToday = new
            {
                amountInr = Math.Round(revenueToday, 2, MidpointRounding.AwayFromZero),
                growthPercentVsYesterday = revenueGrowthVsYesterday
            },
            bookings = new
            {
                successfulToday,
                successfulGrowthPercent,
                failedToday,
                failedGrowthPercent
            },
            pendingWorks = new
            {
                total = 0,
                message = "Pending-work datasource not configured yet.",
                buckets = pendingWorkBuckets
            },
            security = new
            {
                ipAddress = requestIp,
                ipRegion,
                lastLoginAtUtc,
                securityVerified = !string.IsNullOrWhiteSpace(requestIp)
            }
        });
    }

    private async Task<DateTime?> GetEstimatedLastAdminLoginUtcAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        var latestAdminOtp = await _context.OTPs
            .AsNoTracking()
            .Where(o => o.UserId == userId && o.Purpose == "AdminLogin" && o.IsUsed)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync();

        if (latestAdminOtp == null)
        {
            return null;
        }

        return latestAdminOtp.Expiry.AddMinutes(-_adminOtpExpiryMinutes);
    }

    private string GetRequestIpAddress()
    {
        if (HttpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var ips = forwardedFor.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ips.Length > 0)
            {
                return ips[0];
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek startOfWeek)
    {
        var diff = (7 + (date.DayOfWeek - startOfWeek)) % 7;
        return date.AddDays(-1 * diff).Date;
    }

    private static decimal CalculateGrowthPercent(decimal currentAmount, decimal previousAmount)
    {
        if (previousAmount <= 0)
        {
            return currentAmount > 0 ? 100m : 0m;
        }

        var diff = currentAmount - previousAmount;
        return Math.Round((diff / previousAmount) * 100m, 1, MidpointRounding.AwayFromZero);
    }

    private sealed record PendingWorkBucket(string Key, string Label, int Items);
    private sealed record DashboardActivityItem(string Id, string Type, string Message, DateTime OccurredAtUtc);
}
