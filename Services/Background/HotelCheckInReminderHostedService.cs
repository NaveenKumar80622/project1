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
    public class HotelCheckInReminderHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<HotelCheckInReminderHostedService> _logger;
        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

        public HotelCheckInReminderHostedService(
            IServiceProvider serviceProvider, 
            ILogger<HotelCheckInReminderHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Hotel Check-In Reminder Hosted Service started.");

            // Initial startup delay
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessUpcomingCheckInRemindersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing hotel check-in reminders.");
                }

                // Poll every 30 minutes
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }

            _logger.LogInformation("Hotel Check-In Reminder Hosted Service stopped.");
        }

        public async Task ProcessUpcomingCheckInRemindersAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var nowIst = DateTime.UtcNow.Add(IndiaOffset);
            var todayIstDate = nowIst.Date;
            var tomorrowIstDate = todayIstDate.AddDays(1);

            // Fetch active reservations whose check-in date is either today or tomorrow (upcoming within next 24-36h window in IST)
            var upcomingReservations = await dbContext.HotelReservations
                .Where(r => (r.Status == "Booked" || r.Status == "Confirmed")
                            && r.CancelledAt == null
                            && !string.IsNullOrWhiteSpace(r.GuestPhone)
                            && r.CheckInDate.Date >= todayIstDate
                            && r.CheckInDate.Date <= tomorrowIstDate)
                .ToListAsync(stoppingToken);

            if (!upcomingReservations.Any())
            {
                return;
            }

            _logger.LogInformation("Found {Count} upcoming hotel reservations eligible for check-in reminder.", upcomingReservations.Count);

            foreach (var reservation in upcomingReservations)
            {
                try
                {
                    // Deduplication check: verify if a reminder has already been queued or logged
                    bool alreadyQueuedOrSent = await dbContext.NotificationOutbox
                        .AnyAsync(o => o.TemplateKey == "HOTEL_CHECKIN_REMINDER" 
                                    && o.BookingId == reservation.BookingReference, stoppingToken)
                        || await dbContext.NotificationLogs
                        .AnyAsync(l => l.TemplateKey == "HOTEL_CHECKIN_REMINDER" 
                                    && l.Recipient == reservation.GuestPhone.Trim() 
                                    && l.Status == "Success", stoppingToken);

                    if (alreadyQueuedOrSent)
                    {
                        continue;
                    }

                    string formattedDate = reservation.CheckInDate.ToString("dd/MM/yyyy");
                    string hotelName = !string.IsNullOrWhiteSpace(reservation.HotelName) 
                        ? reservation.HotelName.Trim() 
                        : "Hotel";

                    // Truncate hotel name if excessively long for SMS readability
                    if (hotelName.Length > 40)
                    {
                        hotelName = hotelName.Substring(0, 37) + "...";
                    }

                    await notificationService.EnqueueAsync(
                        eventType: "HotelCheckInReminder",
                        channel: "SMS",
                        recipient: reservation.GuestPhone.Trim(),
                        templateKey: "HOTEL_CHECKIN_REMINDER",
                        payload: new
                        {
                            Hotel = hotelName,
                            CheckIn = formattedDate,
                            Date = formattedDate,
                            Reference = reservation.BookingReference,
                            Var1 = hotelName,
                            Var2 = formattedDate,
                            Var3 = reservation.BookingReference
                        },
                        bookingId: reservation.BookingReference,
                        userId: reservation.UserId
                    );

                    _logger.LogInformation("Enqueued check-in reminder SMS for booking {BookingReference}, Hotel {Hotel}, Date {Date} to {Phone}",
                        reservation.BookingReference, hotelName, formattedDate, reservation.GuestPhone);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue check-in reminder for booking {BookingReference}", reservation.BookingReference);
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
