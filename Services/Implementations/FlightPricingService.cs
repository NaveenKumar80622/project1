using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public interface IFlightPricingService
    {
        Task<FlightPricingBreakdownDto> CalculatePricingAsync(
            decimal supplierBaseFare,
            decimal supplierTaxAmount,
            string airlineCode,
            string airlineName,
            string origin,
            string destination,
            DateTime departureDate,
            string travelClass,
            TripType tripType,
            int passengerCount,
            string? couponCode,
            string userId,
            int? selectedPromotionId = null);
    }

    public class FlightPricingService : IFlightPricingService
    {
        private readonly AppDbContext _dbContext;
        private readonly IFlightMarkupService _markupService;
        private readonly IFlightPromotionEngine _promotionEngine;
        private readonly IUserBookingHistoryService _bookingHistoryService;
        
        // Caching fields for the duration of this request
        private User? _cachedUser;
        private bool _isUserCached = false;
        private AgentMarkupSetting? _cachedAgentMarkup;
        private bool _isAgentMarkupCached = false;
        private readonly System.Collections.Generic.Dictionary<string, FlightCoupon?> _cachedCoupons = new();
        private bool? _cachedHasPriorBooking = null;

        public FlightPricingService(
            AppDbContext dbContext,
            IFlightMarkupService markupService,
            IFlightPromotionEngine promotionEngine,
            IUserBookingHistoryService bookingHistoryService)
        {
            _dbContext = dbContext;
            _markupService = markupService;
            _promotionEngine = promotionEngine;
            _bookingHistoryService = bookingHistoryService;
        }

        public async Task<FlightPricingBreakdownDto> CalculatePricingAsync(
            decimal supplierBaseFare,
            decimal supplierTaxAmount,
            string airlineCode,
            string airlineName,
            string origin,
            string destination,
            DateTime departureDate,
            string travelClass,
            TripType tripType,
            int passengerCount,
            string? couponCode,
            string userId,
            int? selectedPromotionId = null)
        {
            // 1. Calculate supplier fares
            decimal supplierTotalFare = supplierBaseFare + supplierTaxAmount;

            // 2. Calculate markup amount (System Markup)
            decimal markupAmount = await _markupService.CalculateMarkupAsync(
                airlineCode, 
                tripType,
                travelClass,
                supplierBaseFare);

            // 3.1 Calculate Agent Markup (if user is B2B Agent)
            decimal agentMarkupAmount = 0m;
            bool isAgent = false;
            if (!string.IsNullOrEmpty(userId))
            {
                if (!_isUserCached)
                {
                    _cachedUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id.ToString() == userId);
                    _isUserCached = true;
                }
                
                var user = _cachedUser;
                if (user != null && user.Role == AuthRoles.Agent)
                {
                    isAgent = true;
                    // B2B Agent Markup removed from flight pipeline per new B2C fare flow
                    agentMarkupAmount = 0m; 
                }
            }

            decimal totalMarkup = markupAmount + agentMarkupAmount;
            decimal fareAfterMarkup = supplierTotalFare + totalMarkup;

            // 4. Promotions are deactivated - pricing mirrors Bus model (coupon only)
            decimal autoPromotionDiscount = 0m;
            decimal manualPromotionDiscount = 0m;
            decimal fareAfterAutoPromo = fareAfterMarkup;

            // 5. Apply coupon discount with DayOfWeek condition
            var (couponDiscount, couponId, appliedCouponCode) = await CalculateCouponDiscountAsync(
                couponCode,
                fareAfterAutoPromo,
                userId,
                isAgent,
                departureDate);

            // 6. Calculate Final Amount
            decimal finalAmount = fareAfterAutoPromo - couponDiscount;
            if (finalAmount < 0)
            {
                finalAmount = 0;
            }

            return new FlightPricingBreakdownDto
            {
                SupplierBaseFare = supplierBaseFare,
                SupplierTaxAmount = supplierTaxAmount,
                SupplierTotalFare = supplierTotalFare,
                MarkupAmount = totalMarkup,
                PromotionDiscount = 0m,
                CouponDiscount = couponDiscount,
                ConvenienceFee = 0m,
                FinalAmount = decimal.Round(finalAmount, 2, MidpointRounding.AwayFromZero),
                PromotionId = null,
                PromotionName = null,
                CouponId = couponId,
                CouponCode = appliedCouponCode
            };
        }

        private async Task<(decimal CouponDiscount, int? CouponId, string? CouponCode)> CalculateCouponDiscountAsync(
            string? couponCode,
            decimal fareAfterMarkupAndPromo,
            string userId,
            bool isAgent,
            DateTime departureDate)
        {
            if (isAgent || string.IsNullOrWhiteSpace(couponCode))
            {
                return (0m, null, null);
            }

            var cleanCode = couponCode.Trim().ToUpperInvariant();
            
            if (!_cachedCoupons.TryGetValue(cleanCode, out var coupon))
            {
                coupon = await _dbContext.FlightCoupons
                    .Include(c => c.Conditions)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CouponCode == cleanCode && c.Status == "Active");
                _cachedCoupons[cleanCode] = coupon;
            }

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

            if (coupon.IsFirstTimeUserOnly)
            {
                if (!_cachedHasPriorBooking.HasValue)
                {
                    string? userPhone = null;
                    if (!string.IsNullOrWhiteSpace(userId) && int.TryParse(userId, out var userIntId))
                    {
                        if (!_isUserCached)
                        {
                            _cachedUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userIntId);
                            _isUserCached = true;
                        }
                        userPhone = _cachedUser?.PhoneNumber;
                    }

                    _cachedHasPriorBooking = await _bookingHistoryService.HasPriorBookingAsync(userId, userPhone);
                }
                
                if (_cachedHasPriorBooking.Value)
                {
                    return (0m, null, null);
                }
            }

            // DayOfWeek condition check
            if (coupon.Conditions != null && coupon.Conditions.Any())
            {
                var dayCond = coupon.Conditions.FirstOrDefault(c => string.Equals(c.ConditionType, "DayOfWeek", StringComparison.OrdinalIgnoreCase));
                if (dayCond != null && !BusPromotionEngineService.IsDayOfWeekMatching(departureDate.DayOfWeek, dayCond.ConditionOperator, dayCond.Value1))
                {
                    return (0m, null, null);
                }
            }

            decimal discount = 0m;
            if (coupon.CouponType.Equals("Percentage", StringComparison.OrdinalIgnoreCase))
            {
                discount = fareAfterMarkupAndPromo * (coupon.Value / 100m);
            }
            else
            {
                discount = coupon.Value;
            }

            discount = Math.Min(discount, fareAfterMarkupAndPromo);
            return (decimal.Round(discount, 2, MidpointRounding.AwayFromZero), coupon.Id, coupon.CouponCode);
        }

    }
}
