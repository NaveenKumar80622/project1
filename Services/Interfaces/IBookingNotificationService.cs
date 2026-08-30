using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services;

public interface IBookingNotificationService
{
    Task<bool> TrySendTicketEmailAsync(TicketEmailRequestDto request, CancellationToken cancellationToken = default);
}
