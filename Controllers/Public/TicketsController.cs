using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
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
        var bookingRef = (request.BookingReference ?? "").Trim();

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

        if (string.Equals(bookingRef, "string", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bookingRef, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bookingRef, "undefined", StringComparison.OrdinalIgnoreCase))
        {
            bookingRef = string.Empty;
        }

        if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "undefined", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(type))
        {
            type = "all";
        }

        _logger.LogInformation("[TicketsController.Fetch] Request received: Mobile='{Mobile}', Email='{Email}', BookingRef='{BookingRef}', BookingType='{BookingType}' (normalized: '{Normalized}'), ActiveOnly={ActiveOnly}",
            mobile, email, bookingRef, request.BookingType, type, request.ActiveOnly);

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                     ?? User.FindFirst("sub")?.Value;

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

        bool hasRef = !string.IsNullOrWhiteSpace(bookingRef);
        bool hasPhone = phoneCandidates.Count > 0;
        bool hasEmail = !string.IsNullOrWhiteSpace(email);
        bool hasUser = !string.IsNullOrWhiteSpace(userId);

        if (!hasRef && !hasPhone && !hasEmail && !hasUser)
        {
            return BadRequest(new
            {
                success = false,
                message = "Mobile, Email, or Booking Reference is required."
            });
        }

        var nowUtc = DateTime.UtcNow;

        // Standardized confirmed and eligible statuses
        var busConfirmedStatuses = new[] { "SUCCESS", "Success", "Booked", "BOOKED", "Confirmed", "CONFIRMED", "Completed", "COMPLETED" };
        var busEligibleStatuses = request.ActiveOnly
            ? busConfirmedStatuses
            : new[] { "SUCCESS", "Success", "Booked", "BOOKED", "Confirmed", "CONFIRMED", "Completed", "COMPLETED", "Cancelled", "CANCELLED", "Refunded", "REFUNDED" };
        var flightConfirmedStatuses = new[] { "Booked", "BOOKED", "Confirmed", "CONFIRMED", "Ticketed", "TICKETED", "Success", "SUCCESS", "Completed", "COMPLETED" };
        var hotelConfirmedStatuses = new[] { "Booked", "BOOKED", "Confirmed", "CONFIRMED", "Completed", "COMPLETED", "Success", "SUCCESS" };

        // ================= BUS TICKETS =================
        async Task<List<object>> GetBusTickets()
        {
            var query = _context.BusReservations
                .Include(x => x.BusBooking)
                .Where(x => busEligibleStatuses.Contains(x.Status) && x.BusBooking != null);

            if (request.ActiveOnly)
            {
                query = query.Where(x => x.BusBooking!.DepartureTime >= nowUtc);
            }

            if (hasRef)
            {
                query = query.Where(x => x.BookingReference == bookingRef || x.Pnr == bookingRef);
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
            else if (hasUser)
            {
                query = query.Where(x => x.UserId == userId);
            }

            var bookings = await query
                .OrderByDescending(x => x.BusBooking!.DepartureTime)
                .ToListAsync();

            _logger.LogInformation("[TicketsController.Fetch] Table: BusReservations. Matched {Count} records.", bookings.Count);

            if (!bookings.Any()) return new List<object>();

            // Batch load passengers (Avoid N+1) - include all passengers so cancelled bookings retain their seat and traveler data
            var bookingIds = bookings.Select(b => b.Id).ToList();
            var allPassengers = await _context.BusReservationPassengers
                .Where(p => bookingIds.Contains(p.BusReservationId))
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

                var bTime = booking.BoardingPointTime ?? dep;
                var boardingTimeStr = bTime.ToString("HH:mm");
                var boardingTimeDisplay = bTime.ToString("hh:mm tt");
                var boardingDateStr = bTime.ToString("ddd, dd MMM yyyy");

                var droppingName = !string.IsNullOrWhiteSpace(booking.DroppingPointName) 
                    ? booking.DroppingPointName 
                    : booking.BusBooking.DroppingPoint;

                var dTime = booking.DroppingPointTime ?? arr;
                var droppingTimeStr = dTime.ToString("HH:mm");
                var droppingTimeDisplay = dTime.ToString("hh:mm tt");
                var droppingDateStr = dTime.ToString("ddd, dd MMM yyyy");

                var paxList = passengersByBooking[booking.Id].ToList();

                result.Add(new
                {
                    bookingReference = booking.BookingReference,
                    ticketType = "bus",
                    pnr = booking.Pnr,
                    ticketNumber = booking.SrdvTicketNo ?? "",
                    providerName = booking.BusBooking.OperatorName,
                    operatorName = booking.BusBooking.OperatorName,
                    tripNumber = booking.BusBooking.BusNumber,
                    busType = booking.BusBooking.BusType,
                    fromCity = booking.BusBooking.FromCity,
                    toCity = booking.BusBooking.ToCity,
                    departureTime = PickNBook.Api.Helpers.IndiaTimeHelper.ToUtcFromIst(dep),
                    departureTimeIst = dep,
                    departureTimeDisplay = dep != default ? dep.ToString("dd MMM yyyy, hh:mm tt") : "-",
                    arrivalTime = PickNBook.Api.Helpers.IndiaTimeHelper.ToUtcFromIst(arr),
                    arrivalTimeIst = arr,
                    arrivalTimeDisplay = arr != default ? arr.ToString("dd MMM yyyy, hh:mm tt") : "-",
                    duration = durationStr,
                    boardingPoint = new
                    {
                        name = boardingName,
                        time = boardingTimeStr,
                        timeDisplay = boardingTimeDisplay,
                        date = boardingDateStr,
                        dateTime = PickNBook.Api.Helpers.IndiaTimeHelper.ToUtcFromIst(bTime),
                        dateTimeIst = bTime
                    },
                    droppingPoint = new
                    {
                        name = droppingName,
                        time = droppingTimeStr,
                        timeDisplay = droppingTimeDisplay,
                        date = droppingDateStr,
                        dateTime = PickNBook.Api.Helpers.IndiaTimeHelper.ToUtcFromIst(dTime),
                        dateTimeIst = dTime
                    },
                    passengers = paxList.Select(p => new
                    {
                        fullName = p.FullName,
                        name = p.FullName,
                        seatNumber = p.SeatNumber,
                        gender = p.Gender,
                        age = p.Age
                    }),
                    seatsBooked = booking.SeatsBooked > 0 ? booking.SeatsBooked : paxList.Count,
                    status = booking.Status,
                    isCancelled = BusBookingStatus.IsCancelled(booking.Status),
                    cancelledAtUtc = booking.CancelledAtUtc,
                    cancelledAtIst = booking.CancelledAtUtc.HasValue ? PickNBook.Api.Helpers.IndiaTimeHelper.ToIst(booking.CancelledAtUtc.Value) : (DateTime?)null,
                    refundAmount = booking.RefundAmountInr ?? 0m,
                    cancellationReason = booking.CancellationReason ?? string.Empty,
                    cancellationCharge = booking.CancellationChargeInr ?? 0m,
                    totalFare = booking.TotalPriceInr,
                    bookedAtUtc = DateTime.SpecifyKind(booking.BookedAtUtc, DateTimeKind.Utc),
                    bookedAtIst = PickNBook.Api.Helpers.IndiaTimeHelper.ToIst(booking.BookedAtUtc),
                    bookedAtDisplay = PickNBook.Api.Helpers.IndiaTimeHelper.FormatIst(booking.BookedAtUtc, "dd MMM yyyy, hh:mm tt")
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
                query = query.Where(x => x.DepartureTime >= nowUtc);
            }

            if (hasRef)
            {
                query = query.Where(x => x.BookingReference == bookingRef || x.Pnr == bookingRef || x.GdsPnr == bookingRef);
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
            else if (hasUser)
            {
                query = query.Where(x => x.UserId == userId);
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
                    providerName = booking.Airline,
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
                        name = p.FullName,
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
                query = query.Where(x => x.CheckOutDate >= nowUtc.Date);
            }

            if (hasRef)
            {
                query = query.Where(x => x.BookingReference == bookingRef || x.ProviderBookingId == bookingRef || x.ConfirmationNo == bookingRef);
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
            else if (hasUser)
            {
                query = query.Where(x => x.UserId == userId);
            }

            var bookings = await query
                .OrderByDescending(x => x.CheckInDate)
                .ToListAsync();

            _logger.LogInformation("[TicketsController.Fetch] Table: HotelReservations. Matched {Count} records.", bookings.Count);

            if (!bookings.Any()) return new List<object>();

            var result = new List<object>();
            foreach (var booking in bookings)
            {
                var cityLabel = !string.IsNullOrWhiteSpace(booking.CityCode) ? booking.CityCode : "Hotel Stay";
                var roomName = !string.IsNullOrWhiteSpace(booking.RoomTypeName) ? booking.RoomTypeName : "Standard Room";
                var fare = booking.TotalPrice > 0 ? booking.TotalPrice : (booking.B2CFinalFare > 0 ? booking.B2CFinalFare : booking.Price);

                result.Add(new
                {
                    bookingReference = booking.BookingReference,
                    ticketType = "hotel",
                    pnr = !string.IsNullOrWhiteSpace(booking.ConfirmationNo) ? booking.ConfirmationNo : booking.BookingReference,
                    confirmationNo = booking.ConfirmationNo ?? booking.ProviderBookingId ?? "",
                    hotelName = booking.HotelName,
                    providerName = booking.HotelName,
                    operatorName = booking.HotelName,
                    fromCity = cityLabel,
                    toCity = cityLabel,
                    city = cityLabel,
                    departureTime = booking.CheckInDate,
                    arrivalTime = booking.CheckOutDate,
                    departureTimeIst = booking.CheckInDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    checkInDate = booking.CheckInDate,
                    checkOutDate = booking.CheckOutDate,
                    roomType = roomName,
                    rooms = booking.Rooms,
                    adults = booking.Adults,
                    children = booking.Children,
                    guestName = booking.GuestName,
                    guestEmail = booking.GuestEmail,
                    guestPhone = booking.GuestPhone,
                    status = booking.Status,
                    totalFare = fare,
                    passengers = new[]
                    {
                        new
                        {
                            fullName = booking.GuestName,
                            name = booking.GuestName,
                            seatNumber = roomName,
                            gender = "",
                            age = 0
                        }
                    }
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
            var busTask = GetBusTickets();
            var flightTask = GetFlightTickets();
            var hotelTask = GetHotelTickets();

            await Task.WhenAll(busTask, flightTask, hotelTask);

            tickets.AddRange(busTask.Result);
            tickets.AddRange(flightTask.Result);
            tickets.AddRange(hotelTask.Result);
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
