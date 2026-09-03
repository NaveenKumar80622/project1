using System.Collections.Generic;
using System.Threading.Tasks;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public interface IBusCouponContextBuilder
    {
        Task<BusCouponValidationContext> BuildContextAsync(
            string? traceId,
            string? resultIndex,
            List<string> seatCodes,
            BusBooking? fallbackBus = null,
            List<SeatPreviewDto>? fallbackSeats = null);
    }
}
