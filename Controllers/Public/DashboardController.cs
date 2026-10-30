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
        var activeBuses = busReservations.Count(x => x.Status == "Booked" || x.Status == "Confirmed" || x.Status == "SUCCESS" || x.Status == "Success");
        var activeHotels = hotelReservations.Count(x => x.Status == "Booked" || x.Status == "Confirmed");
        var activeBookings = activeFlights + activeBuses + activeHotels;
        var activeBookingsGrowth = 0m;

        // ── 3.1 Profit Calculations (Commercial Margin + Markup + Fees - Discounts) ──
        static decimal CalculateBusProfit(PickNBook.Api.Models.BusReservation b)
        {
            if (string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                var custRetained = b.TotalPriceInr - (b.RefundAmountInr ?? 0m);
                var suppRetained = Math.Max(0m, b.NetFareInr - (b.RefundAmountInr ?? 0m));
                return Math.Max(0m, custRetained - suppRetained);
            }
            if (b.CustomerFareInr > 0 && b.NetFareInr > 0)
            {
                return Math.Max(0m, (b.CustomerFareInr - b.NetFareInr) + b.ConvenienceFeeInr);
            }
            if (b.TotalPriceInr > 0 && b.NetFareInr > 0)
            {
                return Math.Max(0m, b.TotalPriceInr - b.NetFareInr);
            }
            return Math.Max(0m, b.MarkupAmountInr + b.ConvenienceFeeInr);
        }

        static decimal CalculateFlightProfit(PickNBook.Api.Models.FlightReservation f)
        {
            if (string.Equals(f.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) return 0m;
            if (f.CustomerFareInr > 0 && f.NetFareInr > 0)
            {
                return Math.Max(0m, f.CustomerFareInr - f.NetFareInr);
            }
            decimal p = (f.MarkupAmount > 0 ? f.MarkupAmount : f.B2CMarkupAmountInr) - f.DiscountAmountInr;
            if (p <= 0m && f.TotalPriceInr > f.NetFareInr && f.NetFareInr > 0) p = f.TotalPriceInr - f.NetFareInr;
            return Math.Max(0m, p);
        }

        static decimal CalculateHotelProfit(PickNBook.Api.Models.HotelReservation h)
        {
            if (string.Equals(h.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) return 0m;
            decimal p = h.MarkupAmount + h.ConvenienceFee - h.CouponDiscount;
            if (p <= 0m && h.Price > h.NetPrice && h.NetPrice > 0) p = h.Price - h.NetPrice + h.ConvenienceFee;
            if (p <= 0m && h.TotalPrice > 0) p = h.TotalPrice * 0.06m;
            return Math.Max(0m, p);
        }

        var totalBusProfit = busReservations.Sum(CalculateBusProfit);
        var totalFlightProfit = flightReservations.Sum(CalculateFlightProfit);
        var totalHotelProfit = hotelReservations.Sum(CalculateHotelProfit);
        var totalProfit = totalBusProfit + totalFlightProfit + totalHotelProfit;

        var busProfitToday = busReservations.Where(x => x.BookedAtUtc >= today).Sum(CalculateBusProfit);
        var flightProfitToday = flightReservations.Where(x => x.BookedAtUtc >= today).Sum(CalculateFlightProfit);
        var hotelProfitToday = hotelReservations.Where(x => x.CreatedAt >= today).Sum(CalculateHotelProfit);
        var profitToday = busProfitToday + flightProfitToday + hotelProfitToday;

        var busProfitThisMonth = busReservations.Where(x => x.BookedAtUtc >= startOfMonth).Sum(CalculateBusProfit);
        var flightProfitThisMonth = flightReservations.Where(x => x.BookedAtUtc >= startOfMonth).Sum(CalculateFlightProfit);
        var hotelProfitThisMonth = hotelReservations.Where(x => x.CreatedAt >= startOfMonth).Sum(CalculateHotelProfit);
        var profitThisMonth = busProfitThisMonth + flightProfitThisMonth + hotelProfitThisMonth;

        var busProfitLastMonth = busReservations.Where(x => x.BookedAtUtc >= startOfLastMonth && x.BookedAtUtc < startOfMonth).Sum(CalculateBusProfit);
        var flightProfitLastMonth = flightReservations.Where(x => x.BookedAtUtc >= startOfLastMonth && x.BookedAtUtc < startOfMonth).Sum(CalculateFlightProfit);
        var hotelProfitLastMonth = hotelReservations.Where(x => x.CreatedAt >= startOfLastMonth && x.CreatedAt < startOfMonth).Sum(CalculateHotelProfit);
        var profitLastMonth = busProfitLastMonth + flightProfitLastMonth + hotelProfitLastMonth;
        var profitGrowthPercent = CalculateGrowthPercent(profitThisMonth, profitLastMonth);

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

        // ── DB-side GROUP BY: let the database aggregate — only 6 small integers travel over the wire ──
        var todayStatusGroups = await _context.Payments
            .AsNoTracking()
            .Where(p => p.CreatedAt >= today)
            .GroupBy(p => p.Status.ToUpper())   // translates to GROUP BY UPPER(Status) in SQL
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalPaymentsToday = todayStatusGroups.Sum(g => g.Count);

        // Safe per-status lookup with default 0
        int CountStatus(string normalized) =>
            todayStatusGroups.FirstOrDefault(g => g.Status == normalized)?.Count ?? 0;

        var todaySuccess   = CountStatus("SUCCESS");
        var todayFailed    = CountStatus("FAILED");
        var todayPending   = CountStatus("PENDING");
        var todayCreated   = CountStatus("CREATED");
        var todayCancelled = CountStatus("CANCELLED");
        var todayExpired   = CountStatus("EXPIRED");

        // Safety-net bucket for any status not in the 6 known values
        var todayUnknown = totalPaymentsToday
            - todaySuccess - todayFailed - todayPending
            - todayCreated - todayCancelled - todayExpired;

        // Legacy broad "failed" count (anything != SUCCESS) — kept for backward compatibility
        var failedToday = totalPaymentsToday - todaySuccess;

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

        var profitByDay = new decimal[7];
        foreach (var b in busReservations.Where(x => x.BookedAtUtc >= weekStart))
        {
            var idx = (int)(b.BookedAtUtc.Date - weekStart).TotalDays;
            if (idx >= 0 && idx <= 6) profitByDay[idx] += CalculateBusProfit(b);
        }
        foreach (var f in flightReservations.Where(x => x.BookedAtUtc >= weekStart))
        {
            var idx = (int)(f.BookedAtUtc.Date - weekStart).TotalDays;
            if (idx >= 0 && idx <= 6) profitByDay[idx] += CalculateFlightProfit(f);
        }
        foreach (var h in hotelReservations.Where(x => x.CreatedAt >= weekStart))
        {
            var idx = (int)(h.CreatedAt.Date - weekStart).TotalDays;
            if (idx >= 0 && idx <= 6) profitByDay[idx] += CalculateHotelProfit(h);
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
                // ── Legacy fields kept for backward compatibility ──────────────────────
                totalBookings      = bookingsToday,
                successfulBookings = successfulToday,       // SUCCESS payments today
                failedBookings     = failedToday,           // broad "!= SUCCESS" count (legacy)
                pendingWorks       = 0,                     // 4-bucket work queue — unchanged
                revenueInr         = Math.Round(revenueToday, 2, MidpointRounding.AwayFromZero),
                profitInr          = Math.Round(profitToday, 2, MidpointRounding.AwayFromZero),
                marginProfitInr    = Math.Round(busProfitToday, 2, MidpointRounding.AwayFromZero),
                expectedRevenueInr = Math.Round(revenueToday * 1.1m, 2, MidpointRounding.AwayFromZero),

                // ── New granular breakdown (prefer these over legacy failedBookings) ────────────
                totalPayments    = totalPaymentsToday,      // total payment ATTEMPTS today
                paymentBreakdown = new
                {
                    success   = todaySuccess,
                    failed    = todayFailed,
                    pending   = todayPending,
                    created   = todayCreated,
                    cancelled = todayCancelled,
                    expired   = todayExpired,
                    unknown   = todayUnknown    // safety net for unexpected/future statuses
                }
            },
            metrics = new
            {
                totalRevenue = Math.Round(totalRevenue, 2, MidpointRounding.AwayFromZero),
                revenueGrowthPercent = revenueGrowth,
                totalProfit = Math.Round(totalProfit, 2, MidpointRounding.AwayFromZero),
                profitGrowthPercent,
                profitToday = Math.Round(profitToday, 2, MidpointRounding.AwayFromZero),
                profitThisMonth = Math.Round(profitThisMonth, 2, MidpointRounding.AwayFromZero),
                busProfit = Math.Round(totalBusProfit, 2, MidpointRounding.AwayFromZero),
                flightProfit = Math.Round(totalFlightProfit, 2, MidpointRounding.AwayFromZero),
                hotelProfit = Math.Round(totalHotelProfit, 2, MidpointRounding.AwayFromZero),
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
                revenueInr = revByDay,
                profitInr = profitByDay
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
