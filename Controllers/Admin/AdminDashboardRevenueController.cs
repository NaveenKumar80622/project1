using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using System.Globalization;

namespace PickNBook.Api.Controllers.Admin;

/// <summary>
/// Revenue overview endpoint for the admin dashboard.
/// Provides flexible chart-ready aggregations by timeframe.
/// </summary>
[Route("api/admin/dashboard")]
public class AdminDashboardRevenueController : AdminApiController
{
    private readonly AppDbContext _context;

    public AdminDashboardRevenueController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Returns revenue aggregated by the requested timeframe for the given year.
    /// </summary>
    /// <param name="year">The year to report on. Defaults to the current UTC year.</param>
    /// <param name="timeframe">
    /// Aggregation granularity: <c>monthly</c> (12 pts), <c>quarterly</c> (4 pts),
    /// <c>weekly</c> (52–53 pts), <c>yearly</c> (one pt per year since system start).
    /// Defaults to <c>monthly</c>.
    /// </param>
    [HttpGet("revenue-overview")]
    public async Task<IActionResult> GetRevenueOverview(
        [FromQuery] int? year        = null,
        [FromQuery] string timeframe = "monthly")
    {
        // ── 0. Normalise inputs ───────────────────────────────────────────────────
        int requestedYear = year ?? DateTime.UtcNow.Year;

        timeframe = (timeframe ?? "monthly").ToLowerInvariant().Trim();
        if (timeframe is not ("monthly" or "quarterly" or "weekly" or "yearly"))
        {
            return BadRequest(new
            {
                message  = "Invalid timeframe. Allowed values: monthly, quarterly, weekly, yearly.",
                received = timeframe
            });
        }

        // ── 1. systemStartDate — three separate index-friendly queries ────────────
        var minFlight = await _context.FlightReservations
            .OrderBy(x => x.BookedAtUtc)
            .Select(x => (DateTime?)x.BookedAtUtc)
            .FirstOrDefaultAsync();

        var minBus = await _context.BusReservations
            .OrderBy(x => x.BookedAtUtc)
            .Select(x => (DateTime?)x.BookedAtUtc)
            .FirstOrDefaultAsync();

        var minHotel = await _context.HotelReservations
            .OrderBy(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync();

        var systemStartDate = new[] { minFlight, minBus, minHotel }
            .Where(d => d.HasValue)
            .Select(d => d!.Value.Date)
            .DefaultIfEmpty(new DateTime(2024, 9, 1, 0, 0, 0, DateTimeKind.Utc))
            .Min();

        // ── 2. Date range for the requested year ──────────────────────────────────
        var yearStart = new DateTime(requestedYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd   = yearStart.AddYears(1); // exclusive upper bound

        // ── 3. Out-of-range guard ─────────────────────────────────────────────────
        if (requestedYear < systemStartDate.Year)
        {
            return Ok(new
            {
                systemStartDate   = systemStartDate,
                systemStartSource = "Earliest reservation date (Flight/Bus/Hotel)",
                revenueSource     = "Payment.CreatedAt where Status=SUCCESS",
                requestedYear,
                hasRecords        = false,
                timeframe,
                totalRevenue      = 0.00m,
                chartData         = Array.Empty<object>()
            });
        }

        // ── 4. totalRevenue for the requested year (always) ───────────────────────
        var totalRevenue = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Status == "SUCCESS"
                     && p.CreatedAt >= yearStart
                     && p.CreatedAt < yearEnd)
            .SumAsync(p => p.FinalPayableAmount);

        // ── 5. Chart aggregation by timeframe ─────────────────────────────────────
        object chartData = timeframe switch
        {
            "monthly"   => await BuildMonthlyAsync(yearStart, yearEnd),
            "quarterly" => await BuildQuarterlyAsync(yearStart, yearEnd),
            "weekly"    => await BuildWeeklyAsync(requestedYear, yearStart, yearEnd),
            "yearly"    => await BuildYearlyAsync(systemStartDate, requestedYear, yearEnd),
            _           => throw new InvalidOperationException("Unreachable — validated above.")
        };

        // ── 6. Response ───────────────────────────────────────────────────────────
        return Ok(new
        {
            systemStartDate   = systemStartDate,
            systemStartSource = "Earliest reservation date (Flight/Bus/Hotel)",
            revenueSource     = "Payment.CreatedAt where Status=SUCCESS",
            requestedYear,
            hasRecords        = true,
            timeframe,
            totalRevenue      = Math.Round(totalRevenue, 2, MidpointRounding.AwayFromZero),
            chartData
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Private helpers — one per timeframe
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// monthly — 12 points (Jan–Dec), fully DB-side GROUP BY month.
    /// Invariant: sum(chartData.revenue) == totalRevenue ✅
    /// </summary>
    private async Task<List<object>> BuildMonthlyAsync(DateTime yearStart, DateTime yearEnd)
    {
        var raw = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Status == "SUCCESS"
                     && p.CreatedAt >= yearStart
                     && p.CreatedAt < yearEnd)
            .GroupBy(p => p.CreatedAt.Month)
            .Select(g => new { Period = g.Key, Revenue = g.Sum(p => p.FinalPayableAmount) })
            .ToListAsync();

        var labels = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                              "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        return Enumerable.Range(1, 12)
            .Select(m => (object)new
            {
                label   = labels[m - 1],
                revenue = Math.Round(
                    raw.FirstOrDefault(x => x.Period == m)?.Revenue ?? 0m,
                    2, MidpointRounding.AwayFromZero)
            })
            .ToList();
    }

    /// <summary>
    /// quarterly — 4 points (Q1–Q4), derived from the same DB-side monthly data.
    /// Q1=Jan-Mar, Q2=Apr-Jun, Q3=Jul-Sep, Q4=Oct-Dec.
    /// Invariant: sum(chartData.revenue) == totalRevenue ✅
    /// </summary>
    private async Task<List<object>> BuildQuarterlyAsync(DateTime yearStart, DateTime yearEnd)
    {
        // Reuse the same monthly raw result — no extra DB round-trip
        var raw = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Status == "SUCCESS"
                     && p.CreatedAt >= yearStart
                     && p.CreatedAt < yearEnd)
            .GroupBy(p => p.CreatedAt.Month)
            .Select(g => new { Period = g.Key, Revenue = g.Sum(p => p.FinalPayableAmount) })
            .ToListAsync();

        return Enumerable.Range(1, 4)
            .Select(q => (object)new
            {
                label   = $"Q{q}",
                revenue = Math.Round(
                    raw.Where(x => (x.Period - 1) / 3 + 1 == q).Sum(x => x.Revenue),
                    2, MidpointRounding.AwayFromZero)
            })
            .ToList();
    }

    /// <summary>
    /// weekly — 52 or 53 points (W1–W52/53), ISO 8601 week numbers.
    /// Fetched into memory — EF Core has no native ISO week SQL function.
    /// Scoped to ONE year only, so memory usage is bounded.
    /// Invariant: sum(chartData.revenue) == totalRevenue ✅
    /// </summary>
    private async Task<List<object>> BuildWeeklyAsync(
        int requestedYear, DateTime yearStart, DateTime yearEnd)
    {
        var weeklyRaw = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Status == "SUCCESS"
                     && p.CreatedAt >= yearStart
                     && p.CreatedAt < yearEnd)
            .Select(p => new { p.CreatedAt, p.FinalPayableAmount })
            .ToListAsync();

        var cal = CultureInfo.InvariantCulture.Calendar;

        var weeklyGrouped = weeklyRaw
            .GroupBy(p => cal.GetWeekOfYear(
                p.CreatedAt,
                CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.FinalPayableAmount));

        // Dec 28 is always guaranteed to fall in the last ISO week of the year
        int totalWeeks = cal.GetWeekOfYear(
            new DateTime(requestedYear, 12, 28),
            CalendarWeekRule.FirstFourDayWeek,
            DayOfWeek.Monday);

        return Enumerable.Range(1, totalWeeks)
            .Select(w => (object)new
            {
                label   = $"W{w}",
                revenue = Math.Round(
                    weeklyGrouped.GetValueOrDefault(w, 0m),
                    2, MidpointRounding.AwayFromZero)
            })
            .ToList();
    }

    /// <summary>
    /// yearly — one point per calendar year from systemStartDate.Year → requestedYear.
    /// DB-side GROUP BY year across the full history window.
    /// Invariant: chartData[last].revenue == totalRevenue ✅ (NOT sum of all points)
    /// </summary>
    private async Task<List<object>> BuildYearlyAsync(
        DateTime systemStartDate, int requestedYear, DateTime yearEnd)
    {
        var allYearsStart = new DateTime(systemStartDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var yearlyRaw = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Status == "SUCCESS"
                     && p.CreatedAt >= allYearsStart
                     && p.CreatedAt < yearEnd)
            .GroupBy(p => p.CreatedAt.Year)
            .Select(g => new { Year = g.Key, Revenue = g.Sum(p => p.FinalPayableAmount) })
            .ToListAsync();

        int totalYears = requestedYear - systemStartDate.Year + 1;

        return Enumerable.Range(systemStartDate.Year, totalYears)
            .Select(y => (object)new
            {
                label   = y.ToString(),
                revenue = Math.Round(
                    yearlyRaw.FirstOrDefault(x => x.Year == y)?.Revenue ?? 0m,
                    2, MidpointRounding.AwayFromZero)
            })
            .ToList();
    }
}
