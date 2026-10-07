using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;

namespace PickNBook.Api.Controllers;

public class TicketsController : BaseApiController
{
    private readonly ITicketEmailService _ticketEmailService;
    private readonly AppDbContext _context;
    private readonly ILogger<TicketsController> _logger;

    public TicketsController(ITicketEmailService ticketEmailService, AppDbContext context, ILogger<TicketsController> logger)
    {
        _ticketEmailService = ticketEmailService;
        _context = context;
        _logger = logger;
    }

    // ================= SEND EMAIL =================
    [HttpPost("send-email")]
    public async Task<IActionResult> SendTicketEmail([FromBody] SendFlightTicketEmailRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        await _ticketEmailService.SendFlightTicketAsync(request);

        return Ok(new
        {
            message = "Ticket email sent successfully.",
            request.ToEmail,
            request.BookingReference
        });
    }

    // ================= FETCH ALL TICKETS =================
    [HttpPost("fetch")]
    public async Task<IActionResult> FetchTicket([FromBody] FetchTicketRequest request)
    {
        var mobile = (request.Mobile ?? "").Trim();
        var email = (request.Email ?? "").Trim().ToLower();
        var type = (request.BookingType ?? "").Trim().ToLower();

        if (string.Equals(mobile, "string", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mobile, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mobile, "undefined", StringComparison.OrdinalIgnoreCase))
        {
            mobile = string.Empty;
        }

        if (string.Equals(email, "string", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(email, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(email, "undefined", StringComparison.OrdinalIgnoreCase))
        {
            email = string.Empty;
        }

        if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "undefined", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(type))
        {
            type = "all";
        }

        _logger.LogInformation("[TicketsController.Fetch] Request received: Mobile='{Mobile}', Email='{Email}', BookingType='{BookingType}' (normalized: '{Normalized}'), ActiveOnly={ActiveOnly}",
            mobile, email, request.BookingType, type, request.ActiveOnly);

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(mobile) &&
            string.IsNullOrWhiteSpace(email) &&
            string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new
            {
                success = false,
                message = "Mobile or Email is required"
            });
        }

        // IST Reference (UTC + 5:30)
        var nowIst = DateTime.UtcNow.AddHours(5.5);

        // Precompute phone candidates in memory for EF Core index-friendly translation
        var phoneCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(mobile))
        {
            phoneCandidates.Add(mobile);
            var digits = new string(mobile.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits))
            {
                phoneCandidates.Add(digits);
                var last10 = digits.Length >= 10 ? digits.Substring(digits.Length - 10) : digits;
                phoneCandidates.Add(last10);
                phoneCandidates.Add("+91" + last10);
                phoneCandidates.Add("91" + last10);
                phoneCandidates.Add("0" + last10);
            }
            phoneCandidates = phoneCandidates.Distinct().ToList();
        }

        bool hasPhone = phoneCandidates.Count > 0;
        bool hasEmail = !string.IsNullOrWhiteSpace(email);
        bool hasUser = !string.IsNullOrWhiteSpace(userId);

        // Standardized confirmed statuses
        var busConfirmedStatuses = new[] { "SUCCESS", "Success", "Booked", "BOOKED", "Confirmed", "CONFIRMED", "Completed", "COMPLETED" };
        var flightConfirmedStatuses = new[] { "Booked", "BOOKED", "Confirmed", "CONFIRMED", "Ticketed", "TICKETED", "Success", "SUCCESS", "Completed", "COMPLETED" };
        var hotelConfirmedStatuses = new[] { "Booked", "BOOKED", "Confirmed", "CONFIRMED", "Completed", "COMPLETED", "Success", "SUCCESS" };

        // ================= BUS TICKETS =================
        async Task<List<object>> GetBusTickets()
        {
            var query = _context.BusReservations
                .Include(x => x.BusBooking)
                .Where(x => busConfirmedStatuses.Contains(x.Status) && x.BusBooking != null);

            if (request.ActiveOnly)
            {
                query = query.Where(x => x.BusBooking!.DepartureTime >= nowIst);
            }

            if (hasUser)
            {
                query = query.Where(x => x.UserId == userId);
            }
            else if (hasPhone && hasEmail)
            {
                query = query.Where(x => phoneCandidates.Contains(x.PassengerPhone) && 
                    (string.IsNullOrEmpty(x.PassengerEmail) || x.PassengerEmail.ToLower() == email));
            }
            else if (hasPhone)
            {
                query = query.Where(x => phoneCandidates.Contains(x.PassengerPhone));
            }
            else if (hasEmail)
            {
                query = query.Where(x => x.PassengerEmail != null && x.PassengerEmail.ToLower() == email);
            }

            var bookings = await query
                .OrderByDescending(x => x.BusBooking!.DepartureTime)
                .ToListAsync();

            _logger.LogInformation("[TicketsController.Fetch] Table: BusReservations. Matched {Count} records.", bookings.Count);

            if (!bookings.Any()) return new List<object>();

            // Batch load passengers (Avoid N+1)
            var bookingIds = bookings.Select(b => b.Id).ToList();
            var allPassengers = await _context.BusReservationPassengers
                .Where(p => bookingIds.Contains(p.BusReservationId) && !p.IsCancelled)
                .ToListAsync();
            var passengersByBooking = allPassengers.ToLookup(p => p.BusReservationId);

            var result = new List<object>();
            foreach (var booking in bookings)
            {
                var dep = booking.BusBooking!.DepartureTime;
                var arr = booking.BusBooking.ArrivalTime;
                var totalMinutes = (int)Math.Max(0, (arr - dep).TotalMinutes);
                var durationStr = $"{totalMinutes / 60}h {totalMinutes % 60:D2}m";

                var boardingName = !string.IsNullOrWhiteSpace(booking.BoardingPointName) 
                    ? booking.BoardingPointName 
                    : booking.BusBooking.BoardingPoint;

                var boardingTimeStr = booking.BoardingPointTime.HasValue 
                    ? booking.BoardingPointTime.Value.ToString("HH:mm") 
                    : dep.ToString("HH:mm");

                var droppingName = !string.IsNullOrWhiteSpace(booking.DroppingPointName) 
                    ? booking.DroppingPointName 
                    : booking.BusBooking.DroppingPoint;

                var droppingTimeStr = booking.DroppingPointTime.HasValue 
                    ? booking.DroppingPointTime.Value.ToString("HH:mm") 
                    : arr.ToString("HH:mm");

                var paxList = passengersByBooking[booking.Id].ToList();

                result.Add(new
                {
                    bookingReference = booking.BookingReference,
                    ticketType = "bus",
                    pnr = booking.Pnr,
                    ticketNumber = booking.SrdvTicketNo ?? "",
                    providerName = booking.BusBooking.OperatorName,
                    tripNumber = booking.BusBooking.BusNumber,
                    busType = booking.BusBooking.BusType,
                    fromCity = booking.BusBooking.FromCity,
                    toCity = booking.BusBooking.ToCity,
                    departureTime = dep,
                    arrivalTime = arr,
                    duration = durationStr,
                    boardingPoint = new
                    {
                        name = boardingName,
                        time = boardingTimeStr
                    },
                    droppingPoint = new
                    {
                        name = droppingName,
                        time = droppingTimeStr
                    },
                    passengers = paxList.Select(p => new
                    {
                        fullName = p.FullName,
                        seatNumber = p.SeatNumber,
                        gender = p.Gender,
                        age = p.Age
                    }),
                    seatsBooked = booking.SeatsBooked > 0 ? booking.SeatsBooked : paxList.Count,
                    status = booking.Status,
                    totalFare = booking.TotalPriceInr
                });
            }

            return result;
        }

        // ================= FLIGHT TICKETS =================
        async Task<List<object>> GetFlightTickets()
        {
            var query = _context.FlightReservations
                .Include(x => x.Segments)
                .Where(x => flightConfirmedStatuses.Contains(x.Status));

            if (request.ActiveOnly)
            {
                query = query.Where(x => x.DepartureTime >= nowIst);
            }

            if (hasUser)
            {
                query = query.Where(x => x.UserId == userId);
            }
            else if (hasPhone && hasEmail)
            {
                query = query.Where(x => phoneCandidates.Contains(x.PassengerPhone) && 
                    (string.IsNullOrEmpty(x.PassengerEmail) || x.PassengerEmail.ToLower() == email));
            }
            else if (hasPhone)
            {
                query = query.Where(x => phoneCandidates.Contains(x.PassengerPhone));
            }
            else if (hasEmail)
            {
                query = query.Where(x => x.PassengerEmail != null && x.PassengerEmail.ToLower() == email);
            }

            var bookings = await query
                .OrderByDescending(x => x.DepartureTime)
                .ToListAsync();

            _logger.LogInformation("[TicketsController.Fetch] Table: FlightReservations. Matched {Count} records.", bookings.Count);

            if (!bookings.Any()) return new List<object>();

            // Batch load passengers (Avoid N+1)
            var bookingIds = bookings.Select(b => b.Id).ToList();
            var allPassengers = await _context.FlightReservationPassengers
                .Where(p => bookingIds.Contains(p.FlightReservationId) && !p.IsCancelled)
                .ToListAsync();
            var passengersByBooking = allPassengers.ToLookup(p => p.FlightReservationId);

            var result = new List<object>();
            foreach (var booking in bookings)
            {
                var dep = booking.DepartureTime;
                var arr = booking.ArrivalTime;
                var totalMinutes = (int)Math.Max(0, (arr - dep).TotalMinutes);
                var durationStr = $"{totalMinutes / 60}h {totalMinutes % 60:D2}m";

                var paxList = passengersByBooking[booking.Id].ToList();

                result.Add(new
                {
                    bookingReference = booking.BookingReference,
                    ticketType = "flight",
                    pnr = !string.IsNullOrWhiteSpace(booking.Pnr) ? booking.Pnr : booking.GdsPnr ?? "",
                    gdsPnr = booking.GdsPnr ?? "",
                    airline = booking.Airline,
                    flightNumber = booking.FlightNumber,
                    travelClass = booking.TravelClass,
                    fromCity = booking.FromCity,
                    toCity = booking.ToCity,
                    departureTime = dep,
                    arrivalTime = arr,
                    duration = durationStr,
                    segments = booking.Segments.OrderBy(s => s.SegmentIndicator).Select(s => new
                    {
                        segmentId = s.Id,
                        airline = s.Airline,
                        flightNumber = s.FlightNumber,
                        fromCity = s.FromCity,
                        toCity = s.ToCity,
                        departureTime = s.DepartureTime,
                        arrivalTime = s.ArrivalTime,
                        duration = $"{s.Duration / 60}h {s.Duration % 60:D2}m",
                        pnr = s.Pnr
                    }),
                    passengers = paxList.Select(p => new
                    {
                        fullName = p.FullName,
                        seatNumber = p.SeatNumber,
                        ticketNumber = p.TicketNumber ?? "",
                        passengerType = p.PassengerType,
                        gender = p.Gender
                    }),
                    status = booking.Status,
                    totalFare = booking.TotalPriceInr
                });
            }

            return result;
        }

        // ================= HOTEL TICKETS =================
        async Task<List<object>> GetHotelTickets()
        {
            var query = _context.HotelReservations
                .Where(x => hotelConfirmedStatuses.Contains(x.Status));

            if (request.ActiveOnly)
            {
                query = query.Where(x => x.CheckOutDate >= nowIst.Date);
            }

            if (hasUser)
            {
                query = query.Where(x => x.UserId == userId);
            }
            else if (hasPhone && hasEmail)
            {
                query = query.Where(x => phoneCandidates.Contains(x.GuestPhone) && 
                    (string.IsNullOrEmpty(x.GuestEmail) || x.GuestEmail.ToLower() == email));
            }
            else if (hasPhone)
            {
                query = query.Where(x => phoneCandidates.Contains(x.GuestPhone));
            }
            else if (hasEmail)
            {
                query = query.Where(x => x.GuestEmail.ToLower() == email);
            }

            var bookings = await query
                .OrderByDescending(x => x.CheckInDate)
                .ToListAsync();

            _logger.LogInformation("[TicketsController.Fetch] Table: HotelReservations. Matched {Count} records.", bookings.Count);

            if (!bookings.Any()) return new List<object>();

            var result = new List<object>();
            foreach (var booking in bookings)
            {
                result.Add(new
                {
                    bookingReference = booking.BookingReference,
                    ticketType = "hotel",
                    confirmationNo = booking.ConfirmationNo ?? booking.ProviderBookingId ?? "",
                    hotelName = booking.HotelName,
                    city = !string.IsNullOrWhiteSpace(booking.CityCode) ? booking.CityCode : "Hotel Stay",
                    checkInDate = booking.CheckInDate,
                    checkOutDate = booking.CheckOutDate,
                    roomType = !string.IsNullOrWhiteSpace(booking.RoomTypeName) ? booking.RoomTypeName : "Standard Room",
                    rooms = booking.Rooms,
                    adults = booking.Adults,
                    children = booking.Children,
                    guestName = booking.GuestName,
                    guestEmail = booking.GuestEmail,
                    guestPhone = booking.GuestPhone,
                    status = booking.Status,
                    totalFare = booking.TotalPrice > 0 ? booking.TotalPrice : (booking.B2CFinalFare > 0 ? booking.B2CFinalFare : booking.Price)
                });
            }

            return result;
        }

        var tickets = new List<object>();

        if (type == "bus")
        {
            tickets.AddRange(await GetBusTickets());
        }
        else if (type == "flight")
        {
            tickets.AddRange(await GetFlightTickets());
        }
        else if (type == "hotel")
        {
            tickets.AddRange(await GetHotelTickets());
        }
        else if (string.IsNullOrEmpty(type) || type == "all")
        {
            tickets.AddRange(await GetBusTickets());
            tickets.AddRange(await GetFlightTickets());
            tickets.AddRange(await GetHotelTickets());
        }
        else
        {
            return BadRequest(new
            {
                success = false,
                message = "Invalid bookingType. Valid types are: bus, flight, hotel, all."
            });
        }

        _logger.LogInformation("[TicketsController.Fetch] Execution complete. TotalTickets={Count}, FinalTicketTypes=[{Types}]",
            tickets.Count, string.Join(", ", tickets.Select(t => ((dynamic)t).ticketType?.ToString() ?? "unknown")));

        if (!tickets.Any())
        {
            return Ok(new
            {
                success = false,
                message = "No active booking found",
                tickets = new List<object>(),
                totalCount = 0
            });
        }

        return Ok(new
        {
            success = true,
            tickets = tickets,
            totalCount = tickets.Count
        });
    }
}
