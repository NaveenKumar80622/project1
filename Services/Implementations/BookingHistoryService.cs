using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public class BookingHistoryService :   IBookingHistoryService
    {
        private readonly AppDbContext _context;

        private static readonly TimeSpan IndiaOffset =
    TimeSpan.FromHours(5.5);

        private static DateTime ToIst(DateTime utcDateTime)
        {
            return DateTime.SpecifyKind(
                utcDateTime,
                DateTimeKind.Utc).Add(IndiaOffset);
        }
        public BookingHistoryService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<BookingHistoryDto>> GetBookingHistoryAsync(string userId, string? type = null)
        {
            var result = new List<BookingHistoryDto>();

            if (string.IsNullOrEmpty(type) || type.Equals("bus", StringComparison.OrdinalIgnoreCase))
            {
                var busBookings = await _context.BusReservations
                    .Include(x => x.BusBooking)
                .Where(x => x.UserId == userId)
                .ToListAsync();

            foreach (var booking in busBookings)
            {
                if (booking.BusBooking == null)
                {
                    continue;
                }

                var journeyDateTime =
     ToIst(booking.BusBooking.DepartureTime);
                string status;
;
                if (booking.Status == "Cancelled")
                {
                    status = "Cancelled";
                }
                else if (journeyDateTime >
          ToIst(DateTime.UtcNow))
                {
                    status = "Upcoming";
                }
                else
                {
                    status = "Past";
                }

                string note;
                string ctaLabel;

                switch (status)
                {
                    case "Upcoming":
                        note = "Your journey is coming up soon.";
                        ctaLabel = "View Ticket";
                        break;

                    case "Past":
                        note = "Extra savings on next booking!";
                        ctaLabel = "Book Return";
                        break;

                    case "Cancelled":
                        note = "Seats on this route are filling fast.";
                        ctaLabel = "Book Again";
                        break;

                    default:
                        note = "";
                        ctaLabel = "";
                        break;
                }

                result.Add(new BookingHistoryDto
                {
                    BookingId = booking.Id,
                    BookingReference = booking.BookingReference,

                    TripType = "Bus",

                    From = booking.BusBooking.FromCity,
                    To = booking.BusBooking.ToCity,

                    Date = journeyDateTime.ToString("ddd, dd MMM yyyy"),

                    Time = journeyDateTime.ToString("HH:mm"),

                    Status = status,

                    Note = note,

                    CtaLabel = ctaLabel,

                    OriginalDate = journeyDateTime
                });
            }
            }

            if (string.IsNullOrEmpty(type) || type.Equals("hotel", StringComparison.OrdinalIgnoreCase))
            {
                var hotelBookings = await _context.HotelReservations
                    .Where(x => x.UserId == userId)
                    .ToListAsync();

            foreach (var booking in hotelBookings)
            {
                var checkInDateTimeIst = ToIst(booking.CheckInDate);
                string status;
                if (booking.Status == "Cancelled")
                {
                    status = "Cancelled";
                }
                else if (checkInDateTimeIst > ToIst(DateTime.UtcNow))
                {
                    status = "Upcoming";
                }
                else
                {
                    status = "Past";
                }

                string note;
                string ctaLabel;

                switch (status)
                {
                    case "Upcoming":
                        note = "Your hotel stay is coming up soon.";
                        ctaLabel = "View Booking";
                        break;

                    case "Past":
                        note = "Hope you enjoyed your stay!";
                        ctaLabel = "Book Again";
                        break;

                    case "Cancelled":
                        note = "Need a room? Book another hotel.";
                        ctaLabel = "Book Again";
                        break;

                    default:
                        note = "";
                        ctaLabel = "";
                        break;
                }

                result.Add(new BookingHistoryDto
                {
                    BookingId = booking.Id,
                    BookingReference = booking.BookingReference,
                    TripType = "Hotel",
                    From = booking.HotelName,
                    To = booking.CityCode,
                    Date = checkInDateTimeIst.ToString("ddd, dd MMM yyyy"),
                    Time = checkInDateTimeIst.ToString("HH:mm"),
                    Status = status,
                    Note = note,
                    CtaLabel = ctaLabel,
                    OriginalDate = checkInDateTimeIst
                });
            }
            }

            if (string.IsNullOrEmpty(type) || type.Equals("flight", StringComparison.OrdinalIgnoreCase))
            {
                var flightBookings = await _context.FlightReservations
                    .Where(x => x.UserId == userId)
                    .ToListAsync();

                foreach (var booking in flightBookings)
                {
                    var departureDateTimeIst = ToIst(booking.DepartureTime);
                    string status;
                    if (booking.Status.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
                    {
                        status = "Cancelled";
                    }
                    else if (booking.Status.Contains("Pending", StringComparison.OrdinalIgnoreCase))
                    {
                        status = "Pending";
                    }
                    else if (departureDateTimeIst > ToIst(DateTime.UtcNow))
                    {
                        status = "Upcoming";
                    }
                    else
                    {
                        status = "Past";
                    }

                    string note;
                    string ctaLabel;

                    switch (status)
                    {
                        case "Pending":
                            note = "Your flight is currently pending confirmation.";
                            ctaLabel = "View Details";
                            break;

                        case "Upcoming":
                            note = "Your flight is coming up soon.";
                            ctaLabel = "View Ticket";
                            break;

                        case "Past":
                            note = "Hope you enjoyed your flight!";
                            ctaLabel = "Book Return";
                            break;

                        case "Cancelled":
                            note = "Need to fly? Book another flight.";
                            ctaLabel = "Book Again";
                            break;

                        default:
                            note = "";
                            ctaLabel = "";
                            break;
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
                        Status = status,
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
