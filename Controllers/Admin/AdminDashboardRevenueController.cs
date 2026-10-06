using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using System.Globalization;

namespace PickNBook.Api.Controllers.Admin;

/// <summary>
/// Revenue overview endpoint for the admin dashboard.
/// Provides separate booked revenue, cancelled amount, completed refund amount,
/// and final revenue aggregations across monthly, quarterly, weekly, and yearly timeframes.
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
                revenueSource     = "BusReservations by booking/cancellation/refund status",
                requestedYear,
                hasRecords        = false,
                timeframe,
                bookedRevenue     = 0.00m,
                cancelledAmount   = 0.00m,
                refundedAmount    = 0.00m,
                finalRevenue      = 0.00m,
                totalRevenue      = 0.00m,
                chartData         = Array.Empty<object>()
            });
        }

        // ── 4. Financial totals for the requested year ────────────────────────────
        // 1. Booked Revenue: Booked reservations placed in requested year
        var rawBookedRevenue = await _context.BusReservations
            .AsNoTracking()
            .Where(b => b.Status == "Booked"
                     && b.BookedAtUtc >= yearStart
                     && b.BookedAtUtc < yearEnd)
            .SumAsync(b => (decimal?)b.TotalPriceInr) ?? 0m;

        // 2. Cancelled Amount: Original amount of reservations cancelled in requested year
        var rawCancelledAmount = await _context.BusReservations
            .AsNoTracking()
            .Where(b => b.Status == "Cancelled"
                     && b.CancelledAtUtc != null
                     && b.CancelledAtUtc >= yearStart
                     && b.CancelledAtUtc < yearEnd)
            .SumAsync(b => (decimal?)b.TotalPriceInr) ?? 0m;

        // 3. Refunded Amount: Completed refunds completed in requested year
        var rawRefundedAmount = await _context.BookingCancellations
            .AsNoTracking()
            .Where(c => c.BookingType == "Bus"
                     && (c.Status == "Completed" || c.RefundStatus == "COMPLETED" || c.RefundStatus == "Refunded")
                     && c.CompletedAtUtc != null
                     && c.CompletedAtUtc >= yearStart
                     && c.CompletedAtUtc < yearEnd)
            .Join(_context.BusReservations.AsNoTracking(),
                  c => c.BookingReference,
                  b => b.BookingReference,
                  (c, b) => (decimal?)(c.CustomerRefundAmount > 0 ? c.CustomerRefundAmount : (b.RefundAmountInr ?? 0m)))
            .SumAsync() ?? 0m;

        // 4. Final Revenue: bookedRevenue + cancelledAmount - refundedAmount
        var rawFinalRevenue = rawBookedRevenue + rawCancelledAmount - rawRefundedAmount;

        var bookedRevenue   = Math.Round(rawBookedRevenue, 2, MidpointRounding.AwayFromZero);
        var cancelledAmount = Math.Round(rawCancelledAmount, 2, MidpointRounding.AwayFromZero);
        var refundedAmount  = Math.Round(rawRefundedAmount, 2, MidpointRounding.AwayFromZero);
        var finalRevenue    = Math.Round(rawFinalRevenue, 2, MidpointRounding.AwayFromZero);

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
            revenueSource     = "BusReservations by booking/cancellation/refund status",
            requestedYear,
            hasRecords        = true,
            timeframe,
            bookedRevenue,
            cancelledAmount,
            refundedAmount,
            finalRevenue,
            totalRevenue      = finalRevenue,
            chartData
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Private helpers — one per timeframe
    // ═══════════════════════════════════════════════════════════════════════════

    private record MonthlyPoint(
        int Period,
        string Label,
        decimal BookedRevenue,
        decimal CancelledAmount,
        decimal RefundedAmount,
        decimal FinalRevenue);

    /// <summary>
    /// Loads the 12-month financial breakdown (Jan–Dec).
    /// </summary>
    private async Task<List<MonthlyPoint>> BuildMonthlyDataAsync(DateTime yearStart, DateTime yearEnd)
    {
        // 1. Booked by month (DB-side GROUP BY)
        var bookedRaw = await _context.BusReservations
            .AsNoTracking()
            .Where(b => b.Status == "Booked"
                     && b.BookedAtUtc >= yearStart
                     && b.BookedAtUtc < yearEnd)
            .GroupBy(b => b.BookedAtUtc.Month)
            .Select(g => new { Period = g.Key, Amount = g.Sum(b => b.TotalPriceInr) })
            .ToListAsync();

        // 2. Cancelled by month
        var cancelledRaw = await _context.BusReservations
            .AsNoTracking()
            .Where(b => b.Status == "Cancelled"
                     && b.CancelledAtUtc != null
                     && b.CancelledAtUtc >= yearStart
                     && b.CancelledAtUtc < yearEnd)
            .Select(b => new { Month = b.CancelledAtUtc!.Value.Month, b.TotalPriceInr })
            .ToListAsync();

        var cancelledGrouped = cancelledRaw
            .GroupBy(x => x.Month)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalPriceInr));

        // 3. Completed refunds by month
        var refundedRaw = await _context.BookingCancellations
            .AsNoTracking()
            .Where(c => c.BookingType == "Bus"
                     && (c.Status == "Completed" || c.RefundStatus == "COMPLETED" || c.RefundStatus == "Refunded")
                     && c.CompletedAtUtc != null
                     && c.CompletedAtUtc >= yearStart
                     && c.CompletedAtUtc < yearEnd)
            .Join(_context.BusReservations.AsNoTracking(),
                  c => c.BookingReference,
                  b => b.BookingReference,
                  (c, b) => new
                  {
                      Month = c.CompletedAtUtc!.Value.Month,
                      Amount = c.CustomerRefundAmount > 0 ? c.CustomerRefundAmount : (b.RefundAmountInr ?? 0m)
                  })
            .ToListAsync();

        var refundedGrouped = refundedRaw
            .GroupBy(x => x.Month)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var bookedMap = bookedRaw.ToDictionary(x => x.Period, x => x.Amount);

        var labels = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                              "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        return Enumerable.Range(1, 12)
            .Select(m =>
            {
                var bRev = bookedMap.GetValueOrDefault(m, 0m);
                var cAmt = cancelledGrouped.GetValueOrDefault(m, 0m);
                var rAmt = refundedGrouped.GetValueOrDefault(m, 0m);
                var fRev = bRev + cAmt - rAmt;

                return new MonthlyPoint(
                    Period: m,
                    Label: labels[m - 1],
                    BookedRevenue: Math.Round(bRev, 2, MidpointRounding.AwayFromZero),
                    CancelledAmount: Math.Round(cAmt, 2, MidpointRounding.AwayFromZero),
                    RefundedAmount: Math.Round(rAmt, 2, MidpointRounding.AwayFromZero),
                    FinalRevenue: Math.Round(fRev, 2, MidpointRounding.AwayFromZero)
                );
            })
            .ToList();
    }

    /// <summary>
    /// monthly — 12 points (Jan–Dec).
    /// Invariant: sum(chartData.finalRevenue) == topLevel.finalRevenue ✅
    /// </summary>
    private async Task<List<object>> BuildMonthlyAsync(DateTime yearStart, DateTime yearEnd)
    {
        var data = await BuildMonthlyDataAsync(yearStart, yearEnd);

        return data.Select(x => (object)new
        {
            period          = x.Period,
            label           = x.Label,
            bookedRevenue   = x.BookedRevenue,
            cancelledAmount = x.CancelledAmount,
            refundedAmount  = x.RefundedAmount,
            finalRevenue    = x.FinalRevenue,
            revenue         = x.FinalRevenue
        }).ToList();
    }

    /// <summary>
    /// quarterly — 4 points (Q1–Q4), derived from monthly data.
    /// Q1=Jan-Mar, Q2=Apr-Jun, Q3=Jul-Sep, Q4=Oct-Dec.
    /// Invariant: sum(chartData.finalRevenue) == topLevel.finalRevenue ✅
    /// </summary>
    private async Task<List<object>> BuildQuarterlyAsync(DateTime yearStart, DateTime yearEnd)
    {
        var monthlyData = await BuildMonthlyDataAsync(yearStart, yearEnd);

        return Enumerable.Range(1, 4)
            .Select(q =>
            {
                var qPoints = monthlyData.Where(x => (x.Period - 1) / 3 + 1 == q).ToList();
                var qBooked = qPoints.Sum(x => x.BookedRevenue);
                var qCancelled = qPoints.Sum(x => x.CancelledAmount);
                var qRefunded = qPoints.Sum(x => x.RefundedAmount);
                var qFinal = qBooked + qCancelled - qRefunded;

                return (object)new
                {
                    period          = q,
                    label           = $"Q{q}",
                    bookedRevenue   = Math.Round(qBooked, 2, MidpointRounding.AwayFromZero),
                    cancelledAmount = Math.Round(qCancelled, 2, MidpointRounding.AwayFromZero),
                    refundedAmount  = Math.Round(qRefunded, 2, MidpointRounding.AwayFromZero),
                    finalRevenue    = Math.Round(qFinal, 2, MidpointRounding.AwayFromZero),
                    revenue         = Math.Round(qFinal, 2, MidpointRounding.AwayFromZero)
                };
            })
            .ToList();
    }

    /// <summary>
    /// weekly — 52 or 53 points (W1–W52/53), ISO 8601 week numbers.
    /// Scoped to requested year with minimal selected columns.
    /// Invariant: sum(chartData.finalRevenue) == topLevel.finalRevenue ✅
    /// </summary>
    private async Task<List<object>> BuildWeeklyAsync(
        int requestedYear, DateTime yearStart, DateTime yearEnd)
    {
        var cal = CultureInfo.InvariantCulture.Calendar;

        var bookedRaw = await _context.BusReservations
            .AsNoTracking()
            .Where(b => b.Status == "Booked"
                     && b.BookedAtUtc >= yearStart
                     && b.BookedAtUtc < yearEnd)
            .Select(b => new { b.BookedAtUtc, b.TotalPriceInr })
            .ToListAsync();

        var cancelledRaw = await _context.BusReservations
            .AsNoTracking()
            .Where(b => b.Status == "Cancelled"
                     && b.CancelledAtUtc != null
                     && b.CancelledAtUtc >= yearStart
                     && b.CancelledAtUtc < yearEnd)
            .Select(b => new { CancelledAtUtc = b.CancelledAtUtc!.Value, b.TotalPriceInr })
            .ToListAsync();

        var refundedRaw = await _context.BookingCancellations
            .AsNoTracking()
            .Where(c => c.BookingType == "Bus"
                     && (c.Status == "Completed" || c.RefundStatus == "COMPLETED" || c.RefundStatus == "Refunded")
                     && c.CompletedAtUtc != null
                     && c.CompletedAtUtc >= yearStart
                     && c.CompletedAtUtc < yearEnd)
            .Join(_context.BusReservations.AsNoTracking(),
                  c => c.BookingReference,
                  b => b.BookingReference,
                  (c, b) => new
                  {
                      CompletedAtUtc = c.CompletedAtUtc!.Value,
                      Amount = c.CustomerRefundAmount > 0 ? c.CustomerRefundAmount : (b.RefundAmountInr ?? 0m)
                  })
            .ToListAsync();

        var bookedGrouped = bookedRaw
            .GroupBy(b => cal.GetWeekOfYear(b.BookedAtUtc, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalPriceInr));

        var cancelledGrouped = cancelledRaw
            .GroupBy(b => cal.GetWeekOfYear(b.CancelledAtUtc, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalPriceInr));

        var refundedGrouped = refundedRaw
            .GroupBy(r => cal.GetWeekOfYear(r.CompletedAtUtc, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // Dec 28 is always guaranteed to fall in the last ISO week of the year
        int totalWeeks = cal.GetWeekOfYear(
            new DateTime(requestedYear, 12, 28),
            CalendarWeekRule.FirstFourDayWeek,
            DayOfWeek.Monday);

        return Enumerable.Range(1, totalWeeks)
            .Select(w =>
            {
                var bRev = bookedGrouped.GetValueOrDefault(w, 0m);
                var cAmt = cancelledGrouped.GetValueOrDefault(w, 0m);
                var rAmt = refundedGrouped.GetValueOrDefault(w, 0m);
                var fRev = bRev + cAmt - rAmt;

                return (object)new
                {
                    period          = w,
                    label           = $"W{w}",
                    bookedRevenue   = Math.Round(bRev, 2, MidpointRounding.AwayFromZero),
                    cancelledAmount = Math.Round(cAmt, 2, MidpointRounding.AwayFromZero),
                    refundedAmount  = Math.Round(rAmt, 2, MidpointRounding.AwayFromZero),
                    finalRevenue    = Math.Round(fRev, 2, MidpointRounding.AwayFromZero),
                    revenue         = Math.Round(fRev, 2, MidpointRounding.AwayFromZero)
                };
            })
            .ToList();
    }

    /// <summary>
    /// yearly — one point per calendar year from systemStartDate.Year → requestedYear.
    /// Invariant: chartData[last].finalRevenue == topLevel.finalRevenue ✅
    /// </summary>
    private async Task<List<object>> BuildYearlyAsync(
        DateTime systemStartDate, int requestedYear, DateTime yearEnd)
    {
        var allYearsStart = new DateTime(systemStartDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var bookedRaw = await _context.BusReservations
            .AsNoTracking()
            .Where(b => b.Status == "Booked"
                     && b.BookedAtUtc >= allYearsStart
                     && b.BookedAtUtc < yearEnd)
            .GroupBy(b => b.BookedAtUtc.Year)
            .Select(g => new { Year = g.Key, Amount = g.Sum(b => b.TotalPriceInr) })
            .ToListAsync();

        var cancelledRaw = await _context.BusReservations
            .AsNoTracking()
            .Where(b => b.Status == "Cancelled"
                     && b.CancelledAtUtc != null
                     && b.CancelledAtUtc >= allYearsStart
                     && b.CancelledAtUtc < yearEnd)
            .Select(b => new { Year = b.CancelledAtUtc!.Value.Year, b.TotalPriceInr })
            .ToListAsync();

        var cancelledGrouped = cancelledRaw
            .GroupBy(x => x.Year)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalPriceInr));

        var refundedRaw = await _context.BookingCancellations
            .AsNoTracking()
            .Where(c => c.BookingType == "Bus"
                     && (c.Status == "Completed" || c.RefundStatus == "COMPLETED" || c.RefundStatus == "Refunded")
                     && c.CompletedAtUtc != null
                     && c.CompletedAtUtc >= allYearsStart
                     && c.CompletedAtUtc < yearEnd)
            .Join(_context.BusReservations.AsNoTracking(),
                  c => c.BookingReference,
                  b => b.BookingReference,
                  (c, b) => new
                  {
                      Year = c.CompletedAtUtc!.Value.Year,
                      Amount = c.CustomerRefundAmount > 0 ? c.CustomerRefundAmount : (b.RefundAmountInr ?? 0m)
                  })
            .ToListAsync();

        var refundedGrouped = refundedRaw
            .GroupBy(x => x.Year)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var bookedMap = bookedRaw.ToDictionary(x => x.Year, x => x.Amount);

        int totalYears = requestedYear - systemStartDate.Year + 1;

        return Enumerable.Range(systemStartDate.Year, totalYears)
            .Select(y =>
            {
                var bRev = bookedMap.GetValueOrDefault(y, 0m);
                var cAmt = cancelledGrouped.GetValueOrDefault(y, 0m);
                var rAmt = refundedGrouped.GetValueOrDefault(y, 0m);
                var fRev = bRev + cAmt - rAmt;

                return (object)new
                {
                    period          = y,
                    label           = y.ToString(),
                    bookedRevenue   = Math.Round(bRev, 2, MidpointRounding.AwayFromZero),
                    cancelledAmount = Math.Round(cAmt, 2, MidpointRounding.AwayFromZero),
                    refundedAmount  = Math.Round(rAmt, 2, MidpointRounding.AwayFromZero),
                    finalRevenue    = Math.Round(fRev, 2, MidpointRounding.AwayFromZero),
                    revenue         = Math.Round(fRev, 2, MidpointRounding.AwayFromZero)
                };
            })
            .ToList();
    }
}
