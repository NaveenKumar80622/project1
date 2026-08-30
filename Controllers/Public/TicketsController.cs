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

    public TicketsController(ITicketEmailService ticketEmailService, AppDbContext context)
    {
        _ticketEmailService = ticketEmailService;
        _context = context;
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
        if (string.IsNullOrWhiteSpace(request.Mobile) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.BookingType))
        {
            return BadRequest(new
            {
                success = false,
                message = "Mobile, Email and bookingType are required"
            });
        }

        var now = DateTime.Now;

        var mobile = request.Mobile?.Trim() ?? "";
        var email = request.Email?.Trim().ToLower() ?? "";
        var type = request.BookingType?.Trim().ToLower() ?? "";

        // ================= BUS TICKETS =================
        async Task<List<object>> GetBusTickets()
        {
            var bookings = await _context.BusReservations
                .Include(x => x.BusBooking)
                .Where(x =>
                    ((x.PassengerPhone ?? "").Trim() == mobile) &&
                    ((x.PassengerEmail ?? "").Trim().ToLower() == email) &&
                    x.Status != "Cancelled" &&
                    x.Status != "Completed" &&
                    x.Status != "Expired" &&
                    x.Status != "Failed" &&
                    x.BusBooking != null &&
                    x.BusBooking.DepartureTime > now
                )
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            var result = new List<object>();

            foreach (var booking in bookings)
            {
                var passengers = await _context.BusReservationPassengers
                    .Where(p => p.BusReservationId == booking.Id)
                    .ToListAsync();

                result.Add(new
                {
                    bookingReference = booking.BookingReference,
                    ticketType = "bus",

                    providerName = booking.BusBooking!.OperatorName,
                    tripNumber = booking.BusBooking.BusNumber,
                    busType = booking.BusBooking.BusType,

                    fromCity = booking.BusBooking.FromCity,
                    toCity = booking.BusBooking.ToCity,

                    departureTime = booking.BusBooking.DepartureTime,
                    arrivalTime = booking.BusBooking.ArrivalTime,

                    duration = $"{(booking.BusBooking.ArrivalTime - booking.BusBooking.DepartureTime).Hours}h {((booking.BusBooking.ArrivalTime - booking.BusBooking.DepartureTime).Minutes):D2}m",

                    boardingPoint = new
                    {
                        name = booking.BusBooking.BoardingPoint,
                        time = booking.BusBooking.DepartureTime.ToString("HH:mm")
                    },

                    droppingPoint = new
                    {
                        name = booking.BusBooking.DroppingPoint,
                        time = booking.BusBooking.ArrivalTime.ToString("HH:mm")
                    },

                    passengers = passengers.Select(p => new
                    {
                        fullName = p.FullName,
                        seatNumber = p.SeatNumber,
                        age = p.Age
                    }),

                    status = booking.Status,
                    totalFare = booking.TotalPriceInr
                });
            }

            return result;
        }

        // ================= FLIGHT TICKETS =================
        async Task<List<object>> GetFlightTickets()
        {
            var bookings = await _context.FlightReservations
                .Where(x =>
                    ((x.PassengerPhone ?? "").Trim() == mobile) &&
                    ((x.PassengerEmail ?? "").Trim().ToLower() == email) &&
                    x.Status != "Cancelled" &&
                    x.Status != "Completed" &&
                    x.Status != "Expired" &&
                    x.Status != "Failed" &&
                    true &&
                    x.DepartureTime > now
                )
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            var result = new List<object>();

            foreach (var booking in bookings)
            {
                var passengers = await _context.FlightReservationPassengers
                    .Where(p => p.FlightReservationId == booking.Id)
                    .ToListAsync();

                result.Add(new
                {
                    booking.BookingReference,
                    ticketType = "flight",
                    fromCity = booking.FromCity,
                    toCity = booking.ToCity,
                    departureTime = booking.DepartureTime,
                    passengers = passengers.Select(p => new
                    {
                        p.FullName,
                        p.SeatNumber
                    }),
                    status = booking.Status,
                    totalFare = booking.TotalPriceInr
                });
            }

            return result;
        }

        List<object> tickets = new();

        if (type == "bus")
        {
            tickets = await GetBusTickets();
        }
        else if (type == "flight")
        {
            tickets = await GetFlightTickets();
        }
        else
        {
            return BadRequest(new
            {
                success = false,
                message = "Invalid bookingType"
            });
        }

        // 🔁 fallback (if no tickets found in selected type)
        if (!tickets.Any())
        {
            tickets = type == "bus"
                ? await GetFlightTickets()
                : await GetBusTickets();
        }

        if (!tickets.Any())
        {
            return Ok(new
            {
                success = false,
                message = "No active booking found"
            });
        }

        return Ok(new
        {
            success = true,
            tickets = tickets
        });
    }
}
