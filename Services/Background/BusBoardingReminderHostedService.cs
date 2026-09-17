using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Services.Notifications.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Background
{
    public class BusBoardingReminderHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BusBoardingReminderHostedService> _logger;
        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

        public BusBoardingReminderHostedService(
            IServiceProvider serviceProvider, 
            ILogger<BusBoardingReminderHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bus Boarding Reminder Hosted Service started.");

            // Initial startup delay
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessUpcomingBoardingRemindersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing bus boarding reminders.");
                }

                // Poll every 10 minutes
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }

            _logger.LogInformation("Bus Boarding Reminder Hosted Service stopped.");
        }

        public async Task ProcessUpcomingBoardingRemindersAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var nowUtc = DateTime.UtcNow;
            // Reminder window: Trips departing between 1 hr 45 min and 2 hr 15 min from now (centered at T-2 hours)
            var windowStartUtc = nowUtc.AddMinutes(105);
            var windowEndUtc = nowUtc.AddMinutes(135);

            var upcomingReservations = await dbContext.BusReservations
                .Include(r => r.BusBooking)
                .Where(r => (r.Status == "Booked" || r.Status == "SUCCESS" || r.Status == "Confirmed")
                            && r.CancelledAtUtc == null
                            && !string.IsNullOrWhiteSpace(r.PassengerPhone)
                            && ((r.BoardingPointTime.HasValue && r.BoardingPointTime.Value >= windowStartUtc && r.BoardingPointTime.Value <= windowEndUtc)
                                || (!r.BoardingPointTime.HasValue && r.BusBooking != null && r.BusBooking.DepartureTime >= windowStartUtc && r.BusBooking.DepartureTime <= windowEndUtc)))
                .ToListAsync(stoppingToken);

            if (!upcomingReservations.Any())
            {
                return;
            }

            _logger.LogInformation("Found {Count} upcoming bus reservations eligible for boarding reminder.", upcomingReservations.Count);

            foreach (var reservation in upcomingReservations)
            {
                try
                {
                    // Deduplication check: verify if a reminder has already been queued or logged
                    bool alreadyQueuedOrSent = await dbContext.NotificationOutbox
                        .AnyAsync(o => o.TemplateKey == "BUS_BOARDING_REMINDER" 
                                    && o.BookingId == reservation.BookingReference, stoppingToken)
                        || await dbContext.NotificationLogs
                        .AnyAsync(l => l.TemplateKey == "BUS_BOARDING_REMINDER" 
                                    && l.Recipient == reservation.PassengerPhone.Trim() 
                                    && l.Status == "Success", stoppingToken);

                    if (alreadyQueuedOrSent)
                    {
                        continue;
                    }

                    var rawTime = reservation.BoardingPointTime ?? reservation.BusBooking?.DepartureTime ?? DateTime.UtcNow;
                    var istTime = ToIst(rawTime);
                    string formattedDate = istTime.ToString("dd/MM/yyyy");
                    string formattedTime = istTime.ToString("hh:mm tt");

                    string boardingPoint = !string.IsNullOrWhiteSpace(reservation.BoardingPointName)
                        ? reservation.BoardingPointName
                        : (!string.IsNullOrWhiteSpace(reservation.BusBooking?.BoardingPoint) ? reservation.BusBooking.BoardingPoint : "Bus Station");

                    string pnr = !string.IsNullOrWhiteSpace(reservation.Pnr) ? reservation.Pnr : reservation.BookingReference;

                    await notificationService.EnqueueAsync(
                        eventType: "BusBoardingReminder",
                        channel: "SMS",
                        recipient: reservation.PassengerPhone.Trim(),
                        templateKey: "BUS_BOARDING_REMINDER",
                        payload: new
                        {
                            Pnr = pnr,
                            Date = formattedDate,
                            Time = formattedTime,
                            Boarding = boardingPoint,
                            Var1 = pnr,
                            Var2 = formattedDate,
                            Var3 = formattedTime,
                            Var4 = boardingPoint
                        },
                        bookingId: reservation.BookingReference,
                        userId: reservation.UserId
                    );

                    _logger.LogInformation("Enqueued boarding reminder SMS for booking {BookingReference}, PNR {Pnr} to {Phone}",
                        reservation.BookingReference, pnr, reservation.PassengerPhone);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue boarding reminder for reservation {Id}", reservation.Id);
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }

        private static DateTime ToIst(DateTime utcOrLocal)
        {
            if (utcOrLocal.Kind == DateTimeKind.Utc)
            {
                return utcOrLocal + IndiaOffset;
            }
            return DateTime.SpecifyKind(utcOrLocal, DateTimeKind.Utc) + IndiaOffset;
        }
    }
}
