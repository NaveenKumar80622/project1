using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;

namespace PickNBook.Api.Services
{
    public interface IUserBookingHistoryService
    {
        Task<bool> HasPriorBookingAsync(string userId, string? passengerPhone);
    }

    public class UserBookingHistoryService(AppDbContext dbContext) : IUserBookingHistoryService
    {
        public async Task<bool> HasPriorBookingAsync(string userId, string? passengerPhone)
        {
            var cleanPhone = passengerPhone?.Trim();

            // 1. Check Flight reservations
            var flightQuery = dbContext.FlightReservations.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(cleanPhone))
            {
                var flightExists = await flightQuery.AnyAsync(r => 
                    (r.UserId == userId || r.PassengerPhone == cleanPhone) && 
                    r.Status != "Cancelled");
                if (flightExists) return true;
            }
            else
            {
                var flightExists = await flightQuery.AnyAsync(r => 
                    r.UserId == userId && 
                    r.Status != "Cancelled");
                if (flightExists) return true;
            }

            // 2. Check Bus reservations
            var busQuery = dbContext.BusReservations.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(cleanPhone))
            {
                var busExists = await busQuery.AnyAsync(r => 
                    (r.UserId == userId || r.PassengerPhone == cleanPhone) && 
                    r.Status != "Cancelled");
                return busExists;
            }
            else
            {
                var busExists = await busQuery.AnyAsync(r => 
                    r.UserId == userId && 
                    r.Status != "Cancelled");
                return busExists;
            }
        }
    }
}
