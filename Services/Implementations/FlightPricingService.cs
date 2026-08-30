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

            // 4. Find and apply the best auto promotion
            FlightPromotion? bestAutoPromo = null;
            decimal autoPromotionDiscount = 0m;
            
            FlightPromotion? manualPromo = null;
            decimal manualPromotionDiscount = 0m;

            if (!isAgent)
            {
                var autoPromotionContext = new FlightPromotionEvaluationContext
                {
                    AirlineCode = airlineCode,
                    AirlineName = airlineName,
                    Origin = origin,
                    Destination = destination,
                    DepartureDate = departureDate,
                    TravelClass = travelClass,
                    TripType = tripType,
                    BaseFare = fareAfterMarkup,
                    PassengerCount = passengerCount,
                    UserId = userId,
                    SelectedPromotionId = null // Force Auto Flow
                };

                bestAutoPromo = await _promotionEngine.GetBestPromotionAsync(autoPromotionContext);
                if (bestAutoPromo != null)
                {
                    autoPromotionDiscount = await _promotionEngine.CalculatePromotionDiscountAsync(bestAutoPromo, fareAfterMarkup);
                }
            }

            decimal fareAfterAutoPromo = fareAfterMarkup - autoPromotionDiscount;

            // 5. Apply manual coupon discount
            var (couponDiscount, couponId, appliedCouponCode) = await CalculateCouponDiscountAsync(
                couponCode,
                fareAfterAutoPromo,
                userId,
                isAgent);

            decimal fareAfterCoupon = fareAfterAutoPromo - couponDiscount;

            // 6. Apply manual promotion discount ONLY if coupon was not applied
            if (!isAgent && couponDiscount == 0 && selectedPromotionId.HasValue)
            {
                var manualPromotionContext = new FlightPromotionEvaluationContext
                {
                    AirlineCode = airlineCode,
                    AirlineName = airlineName,
                    Origin = origin,
                    Destination = destination,
                    DepartureDate = departureDate,
                    TravelClass = travelClass,
                    TripType = tripType,
                    BaseFare = fareAfterCoupon,
                    PassengerCount = passengerCount,
                    UserId = userId,
                    SelectedPromotionId = selectedPromotionId
                };

                manualPromo = await _promotionEngine.GetBestPromotionAsync(manualPromotionContext);
                if (manualPromo != null)
                {
                    manualPromotionDiscount = await _promotionEngine.CalculatePromotionDiscountAsync(manualPromo, fareAfterCoupon);
                }
            }

            decimal fareAfterManualPromo = fareAfterCoupon - manualPromotionDiscount;

            // 7. Calculate Final Amount
            decimal finalAmount = fareAfterManualPromo;

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
                PromotionDiscount = autoPromotionDiscount + manualPromotionDiscount,
                CouponDiscount = couponDiscount,
                ConvenienceFee = 0m,
                FinalAmount = decimal.Round(finalAmount, 2, MidpointRounding.AwayFromZero),
                PromotionId = manualPromo != null ? manualPromo.Id : bestAutoPromo?.Id,
                PromotionName = manualPromo != null ? manualPromo.Name : bestAutoPromo?.Name,
                CouponId = couponId,
                CouponCode = appliedCouponCode
            };
        }

        private async Task<(decimal CouponDiscount, int? CouponId, string? CouponCode)> CalculateCouponDiscountAsync(
            string? couponCode,
            decimal fareAfterMarkupAndPromo,
            string userId,
            bool isAgent)
        {
            if (isAgent || string.IsNullOrWhiteSpace(couponCode))
            {
                return (0m, null, null);
            }

            var cleanCode = couponCode.Trim().ToUpperInvariant();
            
            if (!_cachedCoupons.TryGetValue(cleanCode, out var coupon))
            {
                coupon = await _dbContext.FlightCoupons
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
