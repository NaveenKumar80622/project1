using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class DailyAdminSummaryService : IDailyAdminSummaryService
    {
        private readonly AppDbContext _context;
        private readonly IInAppNotificationService _inAppNotificationService;
        private readonly ILogger<DailyAdminSummaryService> _logger;

        public DailyAdminSummaryService(
            AppDbContext context,
            IInAppNotificationService inAppNotificationService,
            ILogger<DailyAdminSummaryService> logger)
        {
            _context = context;
            _inAppNotificationService = inAppNotificationService;
            _logger = logger;
        }

        public static TimeZoneInfo GetIstTimeZoneStatic()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
                }
                catch (TimeZoneNotFoundException)
                {
                    return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "India Standard Time");
                }
            }
        }

        public TimeZoneInfo GetIstTimeZone() => GetIstTimeZoneStatic();

        public (DateTime utcStart, DateTime utcEnd) GetUtcWindowForIstDate(DateOnly businessDate)
        {
            var istZone = GetIstTimeZone();
            var istStart = new DateTime(businessDate.Year, businessDate.Month, businessDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var istEnd = istStart.AddDays(1);

            var utcStart = TimeZoneInfo.ConvertTimeToUtc(istStart, istZone);
            var utcEnd = TimeZoneInfo.ConvertTimeToUtc(istEnd, istZone);

            return (utcStart, utcEnd);
        }

        public async Task<DailySummaryMetrics> CalculateMetricsAsync(DateOnly businessDate, CancellationToken cancellationToken = default)
        {
            var (utcStart, utcEnd) = GetUtcWindowForIstDate(businessDate);

            // 1. Revenue: Successful payments completed within the IST window
            var revenue = await _context.Payments
                .AsNoTracking()
                .Where(p => (p.Status == "SUCCESS" || p.Status == "Success")
                         && (p.PaidAt ?? p.CreatedAt) >= utcStart
                         && (p.PaidAt ?? p.CreatedAt) < utcEnd)
                .SumAsync(p => (decimal?)p.FinalPayableAmount, cancellationToken) ?? 0m;

            // 2. Completed Bookings: Confirmed/Booked reservations in Bus, Flight, Hotel
            var busBookings = await _context.BusReservations
                .AsNoTracking()
                .CountAsync(r => (r.Status == "Booked" || r.Status == "Confirmed")
                              && r.BookedAtUtc >= utcStart
                              && r.BookedAtUtc < utcEnd, cancellationToken);

            var flightBookings = await _context.FlightReservations
                .AsNoTracking()
                .CountAsync(r => (r.Status == "Booked" || r.Status == "Confirmed")
                              && r.BookedAtUtc >= utcStart
                              && r.BookedAtUtc < utcEnd, cancellationToken);

            var hotelBookings = await _context.HotelReservations
                .AsNoTracking()
                .CountAsync(r => (r.Status == "Booked" || r.Status == "Confirmed")
                              && r.CreatedAt >= utcStart
                              && r.CreatedAt < utcEnd, cancellationToken);

            var completedBookings = busBookings + flightBookings + hotelBookings;

            // 3. Cancellations: Persisted cancellation audit records in BookingCancellations
            var cancellations = await _context.BookingCancellations
                .AsNoTracking()
                .CountAsync(c => c.CreatedAtUtc >= utcStart && c.CreatedAtUtc < utcEnd, cancellationToken);

            // 4. Coupon Usage: Persisted usage logs across Bus, Flight, and Hotel coupons
            var busCoupons = await _context.BusCouponUsages
                .AsNoTracking()
                .CountAsync(u => u.UsedAtUtc >= utcStart && u.UsedAtUtc < utcEnd, cancellationToken);

            var flightCoupons = await _context.FlightCouponUsages
                .AsNoTracking()
                .CountAsync(u => u.UsedAtUtc >= utcStart && u.UsedAtUtc < utcEnd, cancellationToken);

            var hotelCoupons = await _context.HotelCouponUsages
                .AsNoTracking()
                .CountAsync(u => u.UsedAtUtc >= utcStart && u.UsedAtUtc < utcEnd, cancellationToken);

            var totalCoupons = busCoupons + flightCoupons + hotelCoupons;

            // 5. Offer Usage: Persisted FeaturedOfferUsages and FlightPromotionUsages
            var busOffers = await _context.FeaturedOfferUsages
                .AsNoTracking()
                .CountAsync(u => u.UsedAtUtc >= utcStart && u.UsedAtUtc < utcEnd, cancellationToken);

            var flightOffers = await _context.FlightPromotionUsages
                .AsNoTracking()
                .CountAsync(u => u.CreatedAtUtc >= utcStart && u.CreatedAtUtc < utcEnd, cancellationToken);

            var totalOffers = busOffers + flightOffers;

            // 6. Searches: Persisted search events across Bus, Flight, and Hotel search logs
            var busSearches = await _context.BusSearchLogs
                .AsNoTracking()
                .CountAsync(s => s.SearchedAtUtc >= utcStart && s.SearchedAtUtc < utcEnd, cancellationToken);

            var flightSearches = await _context.FlightSearchLogs
                .AsNoTracking()
                .CountAsync(s => s.SearchedAtUtc >= utcStart && s.SearchedAtUtc < utcEnd, cancellationToken);

            var hotelSearches = await _context.HotelSearchLogs
                .AsNoTracking()
                .CountAsync(s => s.SearchedAtUtc >= utcStart && s.SearchedAtUtc < utcEnd, cancellationToken);

            var totalSearches = busSearches + flightSearches + hotelSearches;

            return new DailySummaryMetrics
            {
                BusinessDate = businessDate,
                Revenue = revenue,
                SearchCount = totalSearches,
                CompletedBookings = completedBookings,
                Cancellations = cancellations,
                CouponUsage = totalCoupons,
                OfferUsage = totalOffers
            };
        }

        public async Task<InAppNotificationDto?> GenerateAndSendDailySummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default)
        {
            var metrics = await CalculateMetricsAsync(businessDate, cancellationToken);

            var title = "Daily PickNBook Summary";
            var message = $"Today's revenue: ₹{metrics.Revenue:N0}\n" +
                          $"Bookings completed: {metrics.CompletedBookings:N0}\n" +
                          $"Cancellations: {metrics.Cancellations:N0}\n" +
                          $"Coupon uses: {metrics.CouponUsage:N0}\n" +
                          $"Offers used: {metrics.OfferUsage:N0}\n" +
                          $"Searches: {metrics.SearchCount:N0}";

            var idempotencyKey = $"DAILY_ADMIN_SUMMARY_{businessDate:yyyy-MM-dd}";

            try
            {
                _logger.LogInformation("Creating daily admin summary notification for {BusinessDate} with idempotency key '{Key}'",
                    businessDate, idempotencyKey);

                var notification = await _inAppNotificationService.CreateNotificationAsync(
                    type: "DAILY_ADMIN_SUMMARY",
                    category: "OPERATIONS",
                    title: title,
                    message: message,
                    severity: "INFO",
                    referenceType: "DailySummary",
                    referenceId: businessDate.ToString("yyyy-MM-dd"),
                    actionUrl: "/admin/dashboard",
                    idempotencyKey: idempotencyKey,
                    targetRole: "ADMIN",
                    cancellationToken: cancellationToken
                );

                return notification;
            }
            catch (Exception ex)
            {
                // Non-fatal logging pattern
                _logger.LogError(ex, "Failed to create in-app notification for daily summary on {BusinessDate}. Notification failure must not crash execution.", businessDate);
                return null;
            }
        }
    }
}
