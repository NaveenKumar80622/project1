using System;
using System.Threading;
using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public class DailySummaryMetrics
    {
        public DateOnly BusinessDate { get; set; }
        public decimal Revenue { get; set; }
        public int SearchCount { get; set; }
        public int CompletedBookings { get; set; }
        public int Cancellations { get; set; }
        public int CouponUsage { get; set; }
        public int OfferUsage { get; set; }
    }

    public interface IDailyAdminSummaryService
    {
        Task<DailySummaryMetrics> CalculateMetricsAsync(DateOnly businessDate, CancellationToken cancellationToken = default);
        Task<InAppNotificationDto?> GenerateAndSendDailySummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default);
        (DateTime utcStart, DateTime utcEnd) GetUtcWindowForIstDate(DateOnly businessDate);
        TimeZoneInfo GetIstTimeZone();
    }
}
