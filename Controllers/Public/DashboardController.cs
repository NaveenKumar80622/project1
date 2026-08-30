using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
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
        var today = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        var fromDate = today.AddDays(-13);
        var redemptions = await _context.CouponRedemptions
            .AsNoTracking()
            .Where(x => x.RedeemedAtUtc >= fromDate)
            .ToListAsync();

        var successfulToday = redemptions.Count(x => x.RedeemedAtUtc.Date == today);
        var successfulYesterday = redemptions.Count(x => x.RedeemedAtUtc.Date == yesterday);

        var failedToday = 0;
        var failedYesterday = 0;

        var revenueToday = redemptions
            .Where(x => x.RedeemedAtUtc.Date == today)
            .Sum(x => x.FinalPrice);

        var revenueYesterday = redemptions
            .Where(x => x.RedeemedAtUtc.Date == yesterday)
            .Sum(x => x.FinalPrice);

        var weekStart = StartOfWeek(today, DayOfWeek.Monday);
        var weekEndExclusive = weekStart.AddDays(7);

        var labels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var successfulByDay = new int[7];
        var failedByDay = new int[7];
        var revenueByDay = new decimal[7];

        foreach (var redemption in redemptions)
        {
            var day = redemption.RedeemedAtUtc.Date;
            if (day < weekStart || day >= weekEndExclusive)
            {
                continue;
            }

            var index = (int)(day - weekStart).TotalDays;
            if (index < 0 || index > 6)
            {
                continue;
            }

            successfulByDay[index] += 1;
            revenueByDay[index] += redemption.FinalPrice;
        }

        var expectedRevenue = CalculateExpectedRevenue(revenueByDay);
        var paymentReviewCount = 0;
        var bookingVerificationCount = 0;
        var disputeResolutionCount = 0;
        var customerResponseCount = 0;

        var pendingWorkBuckets = new List<PendingWorkBucket>
        {
            new("paymentReview", "Payment Review", paymentReviewCount),
            new("bookingVerification", "Booking Verification", bookingVerificationCount),
            new("disputeResolution", "Dispute Resolution", disputeResolutionCount),
            new("customerResponse", "Customer Response", customerResponseCount)
        };

        var requestIp = GetRequestIpAddress();
        var ipRegion = await _geoIpService.ResolveRegionAsync(requestIp);
        var lastLoginAtUtc = await GetEstimatedLastAdminLoginUtcAsync();

        var pendingWorksToday = pendingWorkBuckets.Sum(x => x.Items);
        var failedGrowthPercent = CalculateGrowthPercent(failedToday, failedYesterday);

        return Ok(new
        {
            todayStatus = new
            {
                totalBookings = successfulToday + failedToday + pendingWorksToday,
                successfulBookings = successfulToday,
                pendingWorks = pendingWorksToday,
                failedBookings = failedToday,
                revenueInr = Math.Round(revenueToday, 2, MidpointRounding.AwayFromZero),
                expectedRevenueInr = expectedRevenue
            },
            security = new
            {
                ipAddress = requestIp,
                ipRegion,
                lastLoginAtUtc,
                securityVerified = !string.IsNullOrWhiteSpace(requestIp)
            },
            revenueToday = new
            {
                amountInr = Math.Round(revenueToday, 2, MidpointRounding.AwayFromZero),
                growthPercentVsYesterday = CalculateGrowthPercent(revenueToday, revenueYesterday)
            },
            bookings = new
            {
                successfulToday,
                successfulGrowthPercent = CalculateGrowthPercent(successfulToday, successfulYesterday),
                failedToday,
                failedGrowthPercent
            },
            weeklyChart = new
            {
                labels,
                successfulBookings = successfulByDay,
                failedBookings = failedByDay
            },
            pendingWorks = new
            {
                total = pendingWorksToday,
                message = "Pending-work datasource not configured yet.",
                buckets = pendingWorkBuckets
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

        // OTP Expiry = issue time + configured expiry window.
        return latestAdminOtp.Expiry.AddMinutes(-_adminOtpExpiryMinutes);
    }

    private string? GetRequestIpAddress()
    {
        var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstForwardedIp = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(firstForwardedIp))
            {
                return firstForwardedIp;
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek startOfWeek)
    {
        var diff = (7 + (date.DayOfWeek - startOfWeek)) % 7;
        return date.AddDays(-1 * diff).Date;
    }

    private static decimal CalculateExpectedRevenue(decimal[] revenueByDay)
    {
        var nonZeroDays = revenueByDay.Count(x => x > 0m);
        if (nonZeroDays == 0)
        {
            return 0m;
        }

        var average = revenueByDay.Sum() / nonZeroDays;
        return Math.Round(average, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateGrowthPercent(decimal today, decimal yesterday)
    {
        if (yesterday <= 0)
        {
            return today > 0 ? 100m : 0m;
        }

        var growth = ((today - yesterday) / yesterday) * 100m;
        return Math.Round(growth, 2, MidpointRounding.AwayFromZero);
    }

    private sealed record PendingWorkBucket(string Key, string Label, int Items);
}
