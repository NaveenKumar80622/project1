using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PickNBook.Api.Data;
using PickNBook.Api.Models;

namespace PickNBook.Api.Services
{
    public interface IHotelMarkupService
    {
        Task<decimal> CalculateMarkupAsync(decimal supplierBasePrice, string? cityCode = null, string? hotelCode = null, string userType = "B2C");
        Task<(decimal CouponDiscount, int? CouponId, string? CouponCode)> CalculateCouponDiscountAsync(
            string? couponCode,
            decimal totalBaseFare,
            string userId,
            bool isAgent);
    }

    public class HotelMarkupService : IHotelMarkupService
    {
        private readonly AppDbContext _dbContext;
        private readonly IMemoryCache _cache;

        public HotelMarkupService(AppDbContext dbContext, IMemoryCache cache)
        {
            _dbContext = dbContext;
            _cache = cache;
        }

        public async Task<decimal> CalculateMarkupAsync(decimal supplierBasePrice, string? cityCode = null, string? hotelCode = null, string userType = "B2C")
        {
            if (supplierBasePrice <= 0)
            {
                return 0m;
            }

            var cleanCityCode = string.IsNullOrWhiteSpace(cityCode) ? "*" : cityCode.Trim().ToUpperInvariant();
            var cleanHotelCode = string.IsNullOrWhiteSpace(hotelCode) ? "*" : hotelCode.Trim();
            var cleanUserType = string.IsNullOrWhiteSpace(userType) ? "B2C" : userType.Trim().ToUpperInvariant();

            // Fetch all active rules from cache or DB (cached for 30 minutes)
            var allActiveRules = await _cache.GetOrCreateAsync("ActiveHotelMarkupRules", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                return await _dbContext.HotelMarkupRules.AsNoTracking().Where(x => x.IsActive).ToListAsync();
            });

            if (allActiveRules == null || allActiveRules.Count == 0)
            {
                return 0m;
            }

            // Filter rules from the in-memory list
            var rules = allActiveRules
                .Where(x => (x.UserType == "All" || string.Equals(x.UserType, cleanUserType, StringComparison.OrdinalIgnoreCase)) &&
                            (x.CityCode == "*" || string.Equals(x.CityCode, cleanCityCode, StringComparison.OrdinalIgnoreCase)) &&
                            (x.HotelCode == "*" || string.Equals(x.HotelCode, cleanHotelCode, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (rules.Count == 0)
            {
                return 0m;
            }

            // Order by Priority descending, then specificity (HotelCode match > CityCode match > Global)
            var rule = rules
                .OrderByDescending(x => x.Priority)
                .ThenByDescending(x => x.HotelCode != "*" ? 2 : (x.CityCode != "*" ? 1 : 0))
                .First();

            decimal markupAmount = 0m;
            if (string.Equals(rule.MarkupType, "Flat", StringComparison.OrdinalIgnoreCase))
            {
                markupAmount = rule.MarkupValue;
            }
            else if (string.Equals(rule.MarkupType, "Percentage", StringComparison.OrdinalIgnoreCase))
            {
                markupAmount = supplierBasePrice * (rule.MarkupValue / 100m);
            }

            return decimal.Round(markupAmount, 2, MidpointRounding.AwayFromZero);
        }

        public async Task<(decimal CouponDiscount, int? CouponId, string? CouponCode)> CalculateCouponDiscountAsync(
            string? couponCode,
            decimal totalBaseFare,
            string userId,
            bool isAgent)
        {
            if (isAgent || string.IsNullOrWhiteSpace(couponCode))
            {
                return (0m, null, null);
            }

            var cleanCode = couponCode.Trim().ToUpperInvariant();

            var coupon = await _cache.GetOrCreateAsync($"HotelCoupon_{cleanCode}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _dbContext.HotelCoupons
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CouponCode == cleanCode && c.Status == "Active");
            });

            if (coupon == null)
            {
                return (0m, null, null);
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
            if (coupon.StartDate > today || coupon.ExpiryDate < today)
            {
                return (0m, null, null);
            }

            if (coupon.UseLimit > 0 && coupon.UsedCount >= coupon.UseLimit)
            {
                return (0m, null, null);
            }
            
            if (totalBaseFare < coupon.MinBookingAmount)
            {
                return (0m, null, null);
            }

            if (coupon.IsFirstTimeUserOnly)
            {
                bool hasPriorBooking = false;
                if (!string.IsNullOrWhiteSpace(userId) && int.TryParse(userId, out var userIntId))
                {
                    hasPriorBooking = await _dbContext.HotelReservations.AsNoTracking().AnyAsync(r => r.UserId == userId);
                }
                
                if (hasPriorBooking)
                {
                    return (0m, null, null);
                }
            }

            decimal discount = 0m;
            if (coupon.CouponType.Equals("Percentage", StringComparison.OrdinalIgnoreCase))
            {
                discount = totalBaseFare * (coupon.Value / 100m);
                if (coupon.MaxDiscountAmount > 0 && discount > coupon.MaxDiscountAmount)
                {
                    discount = coupon.MaxDiscountAmount;
                }
            }
            else
            {
                discount = coupon.Value;
            }

            discount = Math.Min(discount, totalBaseFare);
            return (decimal.Round(discount, 2, MidpointRounding.AwayFromZero), coupon.Id, coupon.CouponCode);
        }
    }
}
