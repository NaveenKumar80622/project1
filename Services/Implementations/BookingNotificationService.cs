using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services;

public class BookingNotificationService(
    ITicketEmailService ticketEmailService,
    ILogger<BookingNotificationService> logger) : IBookingNotificationService
{
    public async Task<bool> TrySendTicketEmailAsync(TicketEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ToEmail))
            return false;

        try
        {
            var flightRequest = new SendFlightTicketEmailRequest
            {
                ToEmail = request.ToEmail,
                PassengerName = request.PassengerName,
                BookingReference = request.BookingReference,
                Airline = request.Airline,
                Origin = request.Origin,
                Destination = request.Destination,
                DepartureTime = request.DepartureTime,
                ArrivalTime = request.ArrivalTime,
                Pnr = request.Pnr,
                SeatNumber = request.SeatNumber,
                Terminal = request.Terminal,
                Price = request.Price,
                Currency = request.Currency,
                StopsCount = request.StopsCount,
                DurationMinutes = request.DurationMinutes,
                Segments = request.Segments
            };

            await ticketEmailService.SendFlightTicketAsync(flightRequest);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ticket email failed for booking {BookingReference}", request.BookingReference);
            return false;
        }
    }
}