using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Services;

public class BusPromotionEngineService
    : IBusPromotionEngineService
{
    private readonly AppDbContext _db;
    private readonly IUserBookingHistoryService _bookingHistoryService;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan IndiaOffset =
        TimeSpan.FromHours(5.5);

    public BusPromotionEngineService(AppDbContext db, IUserBookingHistoryService bookingHistoryService, IMemoryCache cache)
    {
        _db = db;
        _bookingHistoryService = bookingHistoryService;
        _cache = cache;
    }

    public async Task<BusPricingPreviewResponseDto> CalculateAsync(
        BusBooking bus,
        List<SeatPreviewDto> seats,
        string? couponCode,
        int? promotionId,
        int? userId = null,
         int? selectedFeaturedOfferId = null)
    {
        User? userObj = null;
        string? userPhone = null;
        bool isAgent = false;
        if (userId.HasValue)
        {
            userObj = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId.Value);
            isAgent = (userObj != null && userObj.Role == AuthRoles.Agent);
            userPhone = userObj?.PhoneNumber;
        }

        if (isAgent)
        {
            couponCode = null;
            promotionId = null;
            selectedFeaturedOfferId = null;
        }

        var response =
            new BusPricingPreviewResponseDto
            {
                CouponAllowed = true
            };

        decimal subtotal = 0m;
        decimal totalExternalGst = 0m;

        var allMarkups = await _db.BusMarkupSettings
            .AsNoTracking()
            .Where(x => x.Status == "Active")
            .ToListAsync();

        foreach (var seat in seats)
        {
            // Use the per-seat pricing data from SeatPreviewDto (already resolved by the controller)
            // No fallback to generic bus.PriceInr — if seat pricing is missing, fail loudly
            if (seat.BaseFare <= 0)
                throw new Exception($"Seat pricing data unavailable for seat {seat.SeatCode}. Please refresh the seat layout and try again.");

            var currentBaseFare = seat.BaseFare;
            totalExternalGst += seat.ExternalGst;

            var isSleeper = seat.SeatType.Contains("sleeper", StringComparison.OrdinalIgnoreCase);
            var normalizedSeatType = isSleeper ? "Sleeper" : "Seater";

            var markup = allMarkups.FirstOrDefault(x =>
                x.SeatType.Equals(normalizedSeatType, StringComparison.OrdinalIgnoreCase));

            decimal markupAmount = 0m;

            if (markup != null)
            {
                markupAmount =
                    markup.MarkupType.Equals(
                        "Percentage",
                        StringComparison.OrdinalIgnoreCase)
                    ? currentBaseFare * markup.Value / 100m
                    : markup.Value;
            }

            var fareBeforeTax =
                currentBaseFare + markupAmount;

            subtotal += fareBeforeTax;

            response.Seats.Add(
                new BusSeatPriceBreakdownDto
                {
                    SeatCode = seat.SeatCode,
                    SeatType = seat.SeatType,
                    BaseFare = currentBaseFare,
                    MarkupAmount = decimal.Round(
                        markupAmount,
                        2),
                    FareBeforeTax = decimal.Round(
                        fareBeforeTax,
                        2)
                });
        }

        response.SubtotalBeforeCoupon =
     decimal.Round(subtotal, 2);
        FeaturedOffer? selectedOffer = null;

        if (selectedFeaturedOfferId.HasValue)
        {
            selectedOffer = await _db.FeaturedOffers
                .Include(x => x.Conditions)
                .FirstOrDefaultAsync(x =>
                    x.Id == selectedFeaturedOfferId.Value &&
                    x.IsActive);

            if (selectedOffer == null)
                throw new Exception("Selected offer is invalid or inactive");
        }
        // ========================================
        // AUTO APPLY PROMOTIONS
        // ========================================

        // ========================================
        // BEST AUTO APPLY PROMOTION ONLY
        // ========================================

        decimal autoDiscount = 0m;

        BusPromotion? bestAutoPromotion = null;

        decimal bestAutoDiscount = 0m;
        var promoNowUtc = DateTime.UtcNow;
        if (!_cache.TryGetValue("BusAutoPromotions", out List<BusPromotion>? allAutoPromotions))
        {
            allAutoPromotions = await _db.BusPromotions
                .Include(x => x.Conditions)
                .AsNoTracking()
                .Where(x => x.IsActive && x.IsAutoApply)
                .OrderByDescending(x => x.Priority)
                .ToListAsync();
            _cache.Set("BusAutoPromotions", allAutoPromotions, TimeSpan.FromMinutes(5));
        }

        var autoPromotions = allAutoPromotions!
            .Where(x =>
                (!x.StartDateUtc.HasValue || x.StartDateUtc <= promoNowUtc) &&
                (!x.EndDateUtc.HasValue || x.EndDateUtc >= promoNowUtc))
            .ToList();

        foreach (var promo in autoPromotions)
        {
            if (!ValidatePromotionConditions(
                    promo,
                    bus,
                    seats))
            {
                continue;
            }

            if (promo.MinBookingAmount > 0m &&
      subtotal < promo.MinBookingAmount)
            {
                continue;
            }

            if (promo.IsFirstTimeUserOnly)
            {
                var hasPrior = await _bookingHistoryService.HasPriorBookingAsync(userId?.ToString() ?? string.Empty, userPhone);
                if (hasPrior)
                {
                    continue;
                }
            }

            decimal amount =
                promo.DiscountType.Equals(
                    "Percentage",
                    StringComparison.OrdinalIgnoreCase)
                ? subtotal * promo.DiscountValue / 100m
                : promo.DiscountValue;

            if (promo.MaxDiscountAmount.HasValue)
            {
                amount = Math.Min(
                    amount,
                    promo.MaxDiscountAmount.Value);
            }

            if (amount > bestAutoDiscount)
            {
                bestAutoDiscount = amount;
                bestAutoPromotion = promo;
            }
        }

        autoDiscount = bestAutoDiscount;

        bool skipCouponValidation = false;

        if (bestAutoPromotion != null)
        {
            response.AutoPromotionCode =
                bestAutoPromotion.Code;

            // Exclusive auto discounts should block
            // ONLY manual coupons,
            // NOT featured-offer-linked coupons.

            if (bestAutoPromotion.IsExclusive &&
                selectedOffer == null)
            {
                skipCouponValidation = true;
            }
        }

        // ========================================
        // BEST AUTO DISCOUNT (already calculated above)
        // ========================================

        // ========================================
        // USER COUPON / MANUAL PROMOTION
        // ========================================

        BusPromotion? manualPromotion = null;
        decimal manualDiscount = 0m;

        decimal couponDiscount = 0m;
        decimal offerDiscount = 0m;

        if (selectedOffer != null)
        {
            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                throw new Exception("Featured offers cannot stack with manual coupons");
            }

            if (promotionId.HasValue)
            {
                throw new Exception("Only one manual promotion/offer can be applied.");
            }

            // Validate featured offer
            var nowUtc = DateTime.UtcNow;
            if (selectedOffer.StartDateUtc.HasValue && selectedOffer.StartDateUtc.Value > nowUtc)
            {
                throw new Exception("Featured offer has not started yet.");
            }
            if (selectedOffer.EndDateUtc.HasValue && selectedOffer.EndDateUtc.Value < nowUtc)
            {
                throw new Exception("Featured offer has expired.");
            }
            if (selectedOffer.MaxUsage.HasValue && selectedOffer.UsedCount >= selectedOffer.MaxUsage.Value)
            {
                throw new Exception("Featured offer usage limit has been reached.");
            }
            if (selectedOffer.MinBookingAmount > 0m && subtotal < selectedOffer.MinBookingAmount)
            {
                throw new Exception($"Minimum booking amount of INR {selectedOffer.MinBookingAmount} is required.");
            }
            if (!ValidateFeaturedOfferConditions(selectedOffer, bus, seats))
            {
                throw new Exception("Featured offer conditions not met.");
            }

            bool isPercentage = selectedOffer.DiscountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase);
            offerDiscount = isPercentage
                ? subtotal * selectedOffer.DiscountValue / 100m
                : selectedOffer.DiscountValue;

            if (selectedOffer.MaxDiscountAmount.HasValue)
            {
                offerDiscount = Math.Min(offerDiscount, selectedOffer.MaxDiscountAmount.Value);
            }

            response.DiscountSource = "Offer";
            response.DiscountLabel = selectedOffer.Title;
            response.AppliedPromotionTitle = selectedOffer.Title;
            response.AppliedPromotionType = "Offer";
        }
        else if (!skipCouponValidation && !string.IsNullOrWhiteSpace(couponCode))
        {
            var normalizedCoupon = couponCode.Trim().ToUpperInvariant();
            var promoByCode = await _db.BusPromotions
                .Include(x => x.Conditions)
                .FirstOrDefaultAsync(x =>
                    x.Code == normalizedCoupon &&
                    x.IsActive &&
                    !x.IsAutoApply);

            if (promoByCode == null)
            {
                throw new Exception("Invalid or inactive coupon");
            }

            if (promotionId.HasValue && promotionId.Value != promoByCode.Id)
            {
                throw new Exception("Only one manual promotion/offer can be applied.");
            }

            manualPromotion = promoByCode;
        }
        else if (promotionId.HasValue)
        {
            var promoById = await _db.BusPromotions
                .Include(x => x.Conditions)
                .FirstOrDefaultAsync(x =>
                    x.Id == promotionId.Value &&
                    x.IsActive &&
                    !x.IsAutoApply);

            if (promoById == null)
            {
                throw new Exception("Invalid or inactive promotion");
            }

            manualPromotion = promoById;
        }

        if (manualPromotion != null)
        {
            bool valid =
                ValidatePromotionConditions(
                    manualPromotion,
                    bus,
                    seats);

            if (valid)
            {
                if (manualPromotion.IsFirstTimeUserOnly)
                {
                    var hasPrior = await _bookingHistoryService.HasPriorBookingAsync(userId?.ToString() ?? string.Empty, userPhone);
                    if (hasPrior)
                    {
                        throw new Exception("This promotion is only valid for your first booking.");
                    }
                }

                // Validate min booking amount
                if (manualPromotion.MinBookingAmount > 0m && subtotal < manualPromotion.MinBookingAmount)
                {
                    throw new Exception($"Minimum booking amount of INR {manualPromotion.MinBookingAmount} is required.");
                }

                manualDiscount =
                    manualPromotion.DiscountType.Equals(
                        "Percentage",
                        StringComparison.OrdinalIgnoreCase)
                    ? subtotal *
                        manualPromotion.DiscountValue / 100m
                    : manualPromotion.DiscountValue;

                if (manualPromotion.MaxDiscountAmount.HasValue)
                {
                    manualDiscount = Math.Min(
                        manualDiscount,
                        manualPromotion.MaxDiscountAmount.Value);
                }
                // SET APPLIED PROMOTION DETAILS
                response.AppliedPromotionCode = manualPromotion.Code;
                response.AppliedPromotionTitle = manualPromotion.Title;
                response.AppliedPromotionType = manualPromotion.PromotionType;
                response.DiscountSource = manualPromotion.PromotionType;
                response.DiscountLabel = manualPromotion.Title;

                // SPLIT MANUAL DISCOUNT BY TYPE
                if (manualPromotion.PromotionType.Equals(
                        "Coupon",
                        StringComparison.OrdinalIgnoreCase))
                {
                    couponDiscount = manualDiscount;
                }
                else
                {
                    offerDiscount = manualDiscount;
                }
            }
            else
            {
                throw new Exception("Promotion conditions not met.");
            }
        }

        // ========================================
        // ROUNDING
        // ========================================

        autoDiscount =
            decimal.Round(
                autoDiscount,
                2,
                MidpointRounding.AwayFromZero);

        couponDiscount =
            decimal.Round(
                couponDiscount,
                2,
                MidpointRounding.AwayFromZero);

        offerDiscount =
            decimal.Round(
                offerDiscount,
                2,
                MidpointRounding.AwayFromZero);

        // ========================================
        // RESPONSE DISCOUNT FIELDS
        // ========================================

        response.AutoDiscountAmount =
            autoDiscount;

        response.CouponDiscountAmount =
            couponDiscount;

        response.ManualDiscountAmount =
            offerDiscount;

        var totalDiscount =
            Math.Min(
                autoDiscount +
                couponDiscount +
                offerDiscount,
                subtotal);

        response.CouponAmount =
            totalDiscount;

        response.TotalDiscount =
            totalDiscount;

      

        // ========================================
        // TAXABLE FARE
        // ========================================

        var taxableFare =
            subtotal - totalDiscount;

        response.TaxableFare =
            decimal.Round(
                taxableFare,
                2);

        // ========================================
        // GST (EXTERNAL FROM SRDV)
        // ========================================
        
        // SRDV DisplayFare already includes GST. 
        // We do not add the ExternalGst again to avoid double charging.

        response.GstPercent = 0m; // Not driven by local percentage anymore
        response.GstAmount = decimal.Round(totalExternalGst, 2);

        // ========================================
        // CONVENIENCE FEE (Removed per requirements)
        // ========================================

        response.ConvenienceFee = 0m;

        // ========================================
        // GRAND TOTAL
        // ========================================

        response.GrandTotal =
            decimal.Round(
                taxableFare +
                response.GstAmount,
                2);
        response.FinalAmount =response.GrandTotal;

        return response;
    }

    private bool ValidatePromotionConditions(
        BusPromotion promotion,
        BusBooking bus,
        List<SeatPreviewDto> seats)
    {
        if (promotion.Conditions == null ||
            promotion.Conditions.Count == 0)
            return true;

        var istDeparture =
            DateTime.SpecifyKind(
                bus.DepartureTime,
                DateTimeKind.Utc)
            .Add(IndiaOffset);

        foreach (var condition in promotion.Conditions)
        {
            switch (condition.ConditionType)
            {
                case "DayOfWeek":

                    if (!istDeparture.DayOfWeek
                        .ToString()
                        .Equals(
                            condition.Value1,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    break;

                case "SourceCity":

                    if (!bus.FromCity.Equals(
                        condition.Value1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    break;

                case "DestinationCity":

                    if (!bus.ToCity.Equals(
                        condition.Value1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    break;

                case "SeatType":

                    if (!seats.Any(x =>
                        x.SeatType.Equals(
                            condition.Value1,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }

                    break;

                case "BusType":

                    if (!bus.BusType.Equals(
                        condition.Value1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    break;

                case "OperatorName":

                    if (!bus.OperatorName.Equals(
                        condition.Value1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    break;
                case "MinimumFare":

                    decimal currentFare =
                        seats.Count * bus.PriceInr;

                    decimal value1 =
                        decimal.Parse(condition.Value1);

                    decimal value2 =
                        string.IsNullOrWhiteSpace(condition.Value2)
                        ? 0
                        : decimal.Parse(condition.Value2);

                    switch (condition.ConditionOperator)
                    {
                        case ">":

                            if (!(currentFare > value1))
                                return false;

                            break;

                        case ">=":

                            if (!(currentFare >= value1))
                                return false;

                            break;

                        case "<":

                            if (!(currentFare < value1))
                                return false;

                            break;

                        case "<=":

                            if (!(currentFare <= value1))
                                return false;

                            break;

                        case "Between":

                            if (!(currentFare >= value1 &&
                                  currentFare <= value2))
                            {
                                return false;
                            }

                            break;
                    }

                    break;
            }
        }

        return true;
    }

    private bool ValidateFeaturedOfferConditions(
        FeaturedOffer offer,
        BusBooking bus,
        List<SeatPreviewDto> seats)
    {
        if (offer.Conditions == null ||
            offer.Conditions.Count == 0)
            return true;

        var istDeparture =
            DateTime.SpecifyKind(
                bus.DepartureTime,
                DateTimeKind.Utc)
            .Add(IndiaOffset);

        foreach (var condition in offer.Conditions)
        {
            if (!condition.IsActive)
                continue;

            switch (condition.ConditionType)
            {
                case "DayOfWeek":
                    if (!istDeparture.DayOfWeek
                        .ToString()
                        .Equals(
                            condition.Value1,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    break;

                case "SourceCity":
                    if (!bus.FromCity.Equals(
                        condition.Value1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    break;

                case "DestinationCity":
                    if (!bus.ToCity.Equals(
                        condition.Value1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    break;

                case "SeatType":
                    if (!seats.Any(x =>
                        x.SeatType.Equals(
                            condition.Value1,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }
                    break;

                case "BusType":
                    if (!bus.BusType.Equals(
                        condition.Value1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    break;

                case "OperatorName":
                    if (!bus.OperatorName.Equals(
                        condition.Value1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    break;

                case "MinimumFare":
                    decimal currentFare =
                        seats.Count * bus.PriceInr;

                    decimal value1 =
                        decimal.Parse(condition.Value1);

                    decimal value2 =
                        string.IsNullOrWhiteSpace(condition.Value2)
                        ? 0
                        : decimal.Parse(condition.Value2);

                    if (string.IsNullOrWhiteSpace(condition.Value2))
                    {
                        if (currentFare < value1)
                            return false;
                    }
                    else
                    {
                        if (currentFare < value1 || currentFare > value2)
                            return false;
                    }
                    break;

                case "TravelDate":
                    var depDate = bus.DepartureTime.Date;
                    if (DateTime.TryParse(condition.Value1, out var date1))
                    {
                        if (string.IsNullOrWhiteSpace(condition.Value2))
                        {
                            if (depDate != date1.Date)
                                return false;
                        }
                        else if (DateTime.TryParse(condition.Value2, out var date2))
                        {
                            if (depDate < date1.Date || depDate > date2.Date)
                                return false;
                        }
                    }
                    break;
            }
        }

        return true;
    }
}