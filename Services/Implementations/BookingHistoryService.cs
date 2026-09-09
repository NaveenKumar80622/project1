using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public class BookingHistoryService : IBookingHistoryService
    {
        private readonly AppDbContext _context;
        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

        private static DateTime ToIst(DateTime utcDateTime)
        {
            return DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc).Add(IndiaOffset);
        }

        public BookingHistoryService(AppDbContext context)
        {
            _context = context;
        }

        public static string? ResolveUserHistoryStatus(string? rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
                return null;

            var s = rawStatus.Trim();

            // 1. Confirmed / Completed states
            if (s.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Booked", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Success", StringComparison.OrdinalIgnoreCase))
            {
                return "Confirmed";
            }

            // 2. Cancelled states
            if (s.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                return "Cancelled";
            }

            // 3. Failed states
            if (s.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Failure", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("PaymentFailed", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("BookingFailed", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Aborted", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("Failed_", StringComparison.OrdinalIgnoreCase))
            {
                return "Failed";
            }

            // 4. Non-final intermediate statuses are excluded from user history
            // (e.g. Pending, Processing, InProgress, Hold, AwaitingConfirmation)
            return null;
        }

        public async Task<List<BookingHistoryDto>> GetBookingHistoryAsync(string userId, string? type = null)
        {
            var result = new List<BookingHistoryDto>();

            // ------------------ BUS BOOKINGS ------------------
            if (string.IsNullOrEmpty(type) || type.Equals("bus", StringComparison.OrdinalIgnoreCase))
            {
                var busBookings = await _context.BusReservations
                    .Include(x => x.BusBooking)
                    .Where(x => x.UserId == userId)
                    .ToListAsync();

                foreach (var booking in busBookings)
                {
                    string? normalizedStatus = ResolveUserHistoryStatus(booking.Status);
                    if (normalizedStatus == null) continue; // Exclude non-final states

                    DateTime journeyDateTime = booking.BusBooking != null
                        ? ToIst(booking.BusBooking.DepartureTime)
                        : ToIst(booking.BookedAtUtc);

                    string fromCity = booking.BusBooking?.FromCity ?? "Bus Journey";
                    string toCity = booking.BusBooking?.ToCity ?? "Destination";

                    string note;
                    string ctaLabel;

                    if (normalizedStatus == "Failed")
                    {
                        note = !string.IsNullOrWhiteSpace(booking.CancellationReason)
                            ? booking.CancellationReason
                            : "Booking was unsuccessful. Amount has been refunded.";
                        ctaLabel = "Book Again";
                    }
                    else if (normalizedStatus == "Cancelled")
                    {
                        note = !string.IsNullOrWhiteSpace(booking.CancellationReason)
                            ? booking.CancellationReason
                            : "Seats on this route are filling fast.";
                        ctaLabel = "Book Again";
                    }
                    else
                    {
                        bool isUpcoming = journeyDateTime > ToIst(DateTime.UtcNow);
                        note = isUpcoming ? "Your journey is coming up soon." : "Hope you had a great trip!";
                        ctaLabel = "View Ticket";
                    }

                    result.Add(new BookingHistoryDto
                    {
                        BookingId = booking.Id,
                        BookingReference = booking.BookingReference,
                        TripType = "Bus",
                        From = fromCity,
                        To = toCity,
                        Date = journeyDateTime.ToString("ddd, dd MMM yyyy"),
                        Time = journeyDateTime.ToString("HH:mm"),
                        Status = normalizedStatus,
                        Note = note,
                        CtaLabel = ctaLabel,
                        OriginalDate = journeyDateTime
                    });
                }
            }

            // ------------------ HOTEL BOOKINGS ------------------
            if (string.IsNullOrEmpty(type) || type.Equals("hotel", StringComparison.OrdinalIgnoreCase))
            {
                var hotelBookings = await _context.HotelReservations
                    .Where(x => x.UserId == userId)
                    .ToListAsync();

                foreach (var booking in hotelBookings)
                {
                    string? normalizedStatus = ResolveUserHistoryStatus(booking.Status);
                    if (normalizedStatus == null) continue;

                    var checkInDateTimeIst = ToIst(booking.CheckInDate);

                    string note;
                    string ctaLabel;

                    if (normalizedStatus == "Failed")
                    {
                        note = !string.IsNullOrWhiteSpace(booking.CancellationReason)
                            ? booking.CancellationReason
                            : "Hotel reservation failed. Amount has been refunded.";
                        ctaLabel = "Book Again";
                    }
                    else if (normalizedStatus == "Cancelled")
                    {
                        note = !string.IsNullOrWhiteSpace(booking.CancellationReason)
                            ? booking.CancellationReason
                            : "Need a room? Book another hotel.";
                        ctaLabel = "Book Again";
                    }
                    else
                    {
                        bool isUpcoming = checkInDateTimeIst > ToIst(DateTime.UtcNow);
                        note = isUpcoming ? "Your hotel stay is coming up soon." : "Hope you enjoyed your stay!";
                        ctaLabel = "View Booking";
                    }

                    result.Add(new BookingHistoryDto
                    {
                        BookingId = booking.Id,
                        BookingReference = booking.BookingReference,
                        TripType = "Hotel",
                        From = booking.HotelName,
                        To = !string.IsNullOrWhiteSpace(booking.CityCode) ? booking.CityCode : "Hotel Stay",
                        Date = checkInDateTimeIst.ToString("ddd, dd MMM yyyy"),
                        Time = checkInDateTimeIst.ToString("HH:mm"),
                        Status = normalizedStatus,
                        Note = note,
                        CtaLabel = ctaLabel,
                        OriginalDate = checkInDateTimeIst
                    });
                }
            }

            // ------------------ FLIGHT BOOKINGS ------------------
            if (string.IsNullOrEmpty(type) || type.Equals("flight", StringComparison.OrdinalIgnoreCase))
            {
                var flightBookings = await _context.FlightReservations
                    .Where(x => x.UserId == userId)
                    .ToListAsync();

                foreach (var booking in flightBookings)
                {
                    string? normalizedStatus = ResolveUserHistoryStatus(booking.Status);
                    if (normalizedStatus == null) continue;

                    var departureDateTimeIst = ToIst(booking.DepartureTime);

                    string note;
                    string ctaLabel;

                    if (normalizedStatus == "Failed")
                    {
                        note = !string.IsNullOrWhiteSpace(booking.CancellationReason)
                            ? booking.CancellationReason
                            : "Flight booking failed. Amount has been refunded.";
                        ctaLabel = "Book Again";
                    }
                    else if (normalizedStatus == "Cancelled")
                    {
                        note = !string.IsNullOrWhiteSpace(booking.CancellationReason)
                            ? booking.CancellationReason
                            : "Need to fly? Book another flight.";
                        ctaLabel = "Book Again";
                    }
                    else
                    {
                        bool isUpcoming = departureDateTimeIst > ToIst(DateTime.UtcNow);
                        note = isUpcoming ? "Your flight is coming up soon." : "Hope you enjoyed your flight!";
                        ctaLabel = "View Ticket";
                    }

                    result.Add(new BookingHistoryDto
                    {
                        BookingId = booking.Id,
                        BookingReference = booking.BookingReference,
                        TripType = "Flight",
                        From = booking.FromCity,
                        To = booking.ToCity,
                        Date = departureDateTimeIst.ToString("ddd, dd MMM yyyy"),
                        Time = departureDateTimeIst.ToString("HH:mm"),
                        Status = normalizedStatus,
                        Note = note,
                        CtaLabel = ctaLabel,
                        OriginalDate = departureDateTimeIst
                    });
                }
            }

            return result
                .OrderByDescending(x => x.OriginalDate)
                .ToList();
        }
    }
}
