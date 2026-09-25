using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public interface IBookingHistoryService
    {
        Task<List<BookingHistoryDto>> GetBookingHistoryAsync(string userId, string? type = null);
    }
}
