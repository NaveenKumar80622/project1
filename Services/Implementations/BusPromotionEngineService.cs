using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Services
{
    public class BusPromotionEngineService : IBusPromotionEngineService
    {
        private readonly AppDbContext _db;
        private readonly IUserBookingHistoryService _bookingHistoryService;
        private readonly IMemoryCache _cache;

        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

        public BusPromotionEngineService(
            AppDbContext db,
            IUserBookingHistoryService bookingHistoryService,
            IMemoryCache cache)
        {
            _db = db;
            _bookingHistoryService = bookingHistoryService;
            _cache = cache;
        }

        public async Task<BusPricingPreviewResponseDto> CalculateAsync(
            BusBooking bus,
            List<SeatPreviewDto> seats,
            string? couponCode,
            int? promotionId = null,
            int? userId = null,
            int? selectedFeaturedOfferId = null,
            BusCouponValidationContext? validationContext = null)
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
            }

            var response = new BusPricingPreviewResponseDto
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
                    markupAmount = markup.MarkupType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
                        ? currentBaseFare * markup.Value / 100m
                        : markup.Value;
                }

                var fareBeforeTax = currentBaseFare + markupAmount;
                subtotal += fareBeforeTax;

                response.Seats.Add(new BusSeatPriceBreakdownDto
                {
                    SeatCode = seat.SeatCode,
                    SeatType = seat.SeatType,
                    BaseFare = currentBaseFare,
                    MarkupAmount = decimal.Round(markupAmount, 2),
                    FareBeforeTax = decimal.Round(fareBeforeTax, 2)
                });
            }

            response.SubtotalBeforeCoupon = decimal.Round(subtotal, 2);

            // Ensure validationContext is constructed and booking fare matches current subtotal
            if (validationContext == null)
            {
                var istDeparture = DateTime.SpecifyKind(bus.DepartureTime, DateTimeKind.Utc).Add(IndiaOffset);
                validationContext = new BusCouponValidationContext
                {
                    OperatorName = bus.OperatorName,
                    BusType = bus.BusType,
                    SourceCity = bus.FromCity,
                    DestinationCity = bus.ToCity,
                    TravelDate = istDeparture,
                    DayOfWeek = istDeparture.DayOfWeek,
                    BookingFare = subtotal,
                    SelectedSeats = seats.Select(s => new BusCouponSeatContext
                    {
                        SeatName = s.SeatCode,
                        SeatType = s.SeatType,
                        Fare = s.BaseFare
                    }).ToList()
                };
            }
            else
            {
                validationContext.BookingFare = subtotal;
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow.Add(IndiaOffset));

            // =========================================================================
            // 1. AUTO-APPLY PROMOTIONS (Direct from BusCoupon)
            // =========================================================================
            decimal autoDiscount = 0m;
            BusCoupon? bestAutoCoupon = null;
            decimal bestAutoDiscount = 0m;

            var autoCoupons = await _db.BusCoupons
                .Include(x => x.Conditions)
                .AsNoTracking()
                .Where(x => x.Status == "Active" && x.IsAutoApply)
                .OrderByDescending(x => x.Priority)
                .ToListAsync();

            foreach (var autoCpn in autoCoupons)
            {
                if (autoCpn.StartDate > today || autoCpn.ExpiryDate < today)
                    continue;

                if (autoCpn.UseLimit > 0 && autoCpn.UsedCount >= autoCpn.UseLimit)
                    continue;

                if (autoCpn.MinBookingAmount > 0m && subtotal < autoCpn.MinBookingAmount)
                    continue;

                if (autoCpn.IsFirstTimeUserOnly)
                {
                    var hasPrior = await _bookingHistoryService.HasPriorBookingAsync(userId?.ToString() ?? string.Empty, userPhone);
                    if (hasPrior)
                        continue;
                }

                if (userId.HasValue && autoCpn.MaxUsagePerUser > 0)
                {
                    var userCount = await _db.BusCouponUsages
                        .CountAsync(x => x.CouponCode == autoCpn.CouponCode && x.UserId == userId.Value.ToString() && x.BookingStatus == "Booked");
                    if (userCount >= autoCpn.MaxUsagePerUser)
                        continue;
                }

                if (!ValidateCouponConditions(autoCpn.Conditions, validationContext))
                    continue;

                decimal amount = autoCpn.CouponType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
                    ? subtotal * autoCpn.Value / 100m
                    : autoCpn.Value;

                if (autoCpn.MaxDiscountAmount.HasValue)
                {
                    amount = Math.Min(amount, autoCpn.MaxDiscountAmount.Value);
                }

                if (amount > bestAutoDiscount)
                {
                    bestAutoDiscount = amount;
                    bestAutoCoupon = autoCpn;
                }
            }

            autoDiscount = bestAutoDiscount;
            bool skipManualCoupon = false;

            if (bestAutoCoupon != null)
            {
                response.AutoPromotionCode = bestAutoCoupon.CouponCode;
                if (bestAutoCoupon.IsExclusive)
                {
                    skipManualCoupon = true;
                }
            }

            // =========================================================================
            // 2. MANUAL COUPON / OFFER EVALUATION (Direct from BusCoupon)
            // =========================================================================
            decimal manualDiscount = 0m;
            BusCoupon? appliedCoupon = null;

            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                if (skipManualCoupon)
                {
                    throw new Exception("An exclusive auto-applied discount is already active. Manual coupons cannot be combined.");
                }

                var normalizedCode = couponCode.Trim().ToUpperInvariant();
                appliedCoupon = await _db.BusCoupons
                    .Include(x => x.Conditions)
                    .FirstOrDefaultAsync(x => x.CouponCode == normalizedCode);

                if (appliedCoupon == null || !appliedCoupon.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Invalid or inactive coupon code.");
                }

                if (appliedCoupon.StartDate > today)
                {
                    throw new Exception("Coupon has not started yet.");
                }

                if (appliedCoupon.ExpiryDate < today)
                {
                    throw new Exception("Coupon has expired.");
                }

                if (appliedCoupon.UseLimit > 0 && appliedCoupon.UsedCount >= appliedCoupon.UseLimit)
                {
                    throw new Exception("Coupon usage limit has been reached.");
                }

                if (userId.HasValue && appliedCoupon.MaxUsagePerUser > 0)
                {
                    var userCount = await _db.BusCouponUsages
                        .CountAsync(x => x.CouponCode == appliedCoupon.CouponCode && x.UserId == userId.Value.ToString() && x.BookingStatus == "Booked");
                    if (userCount >= appliedCoupon.MaxUsagePerUser)
                    {
                        throw new Exception("Your usage limit for this coupon has been reached.");
                    }
                }

                if (appliedCoupon.IsFirstTimeUserOnly)
                {
                    var hasPrior = await _bookingHistoryService.HasPriorBookingAsync(userId?.ToString() ?? string.Empty, userPhone);
                    if (hasPrior)
                    {
                        throw new Exception("This promotion is only valid for your first booking.");
                    }
                }

                if (appliedCoupon.MinBookingAmount > 0m && subtotal < appliedCoupon.MinBookingAmount)
                {
                    throw new Exception($"Minimum booking amount of INR {appliedCoupon.MinBookingAmount} is required.");
                }

                if (!ValidateCouponConditions(appliedCoupon.Conditions, validationContext))
                {
                    throw new Exception("Coupon conditions not met.");
                }

                manualDiscount = appliedCoupon.CouponType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
                    ? subtotal * appliedCoupon.Value / 100m
                    : appliedCoupon.Value;

                if (appliedCoupon.MaxDiscountAmount.HasValue)
                {
                    manualDiscount = Math.Min(manualDiscount, appliedCoupon.MaxDiscountAmount.Value);
                }

                var category = string.IsNullOrWhiteSpace(appliedCoupon.PromotionCategory) ? "Coupon" : appliedCoupon.PromotionCategory;
                response.AppliedPromotionCode = appliedCoupon.CouponCode;
                response.AppliedPromotionTitle = appliedCoupon.Title ?? appliedCoupon.CouponCode;
                response.AppliedPromotionType = category;
                response.DiscountSource = category;
                response.DiscountLabel = appliedCoupon.Title ?? appliedCoupon.CouponCode;

                if (appliedCoupon.IsExclusive)
                {
                    autoDiscount = 0m;
                    response.AutoPromotionCode = null;
                }
            }

            // =========================================================================
            // 3. ROUNDING & TOTALS
            // =========================================================================
            autoDiscount = decimal.Round(autoDiscount, 2, MidpointRounding.AwayFromZero);
            manualDiscount = decimal.Round(manualDiscount, 2, MidpointRounding.AwayFromZero);

            response.AutoDiscountAmount = autoDiscount;

            if (appliedCoupon != null && appliedCoupon.PromotionCategory.Equals("Offer", StringComparison.OrdinalIgnoreCase))
            {
                response.ManualDiscountAmount = manualDiscount;
                response.CouponDiscountAmount = 0m;
            }
            else
            {
                response.CouponDiscountAmount = manualDiscount;
                response.ManualDiscountAmount = 0m;
            }

            var totalDiscount = Math.Min(autoDiscount + manualDiscount, subtotal);
            response.CouponAmount = totalDiscount;
            response.TotalDiscount = totalDiscount;

            var taxableFare = subtotal - totalDiscount;
            response.TaxableFare = decimal.Round(taxableFare, 2);

            response.GstPercent = 0m;
            response.GstAmount = decimal.Round(totalExternalGst, 2);
            response.ConvenienceFee = 0m;

            response.GrandTotal = decimal.Round(taxableFare + response.GstAmount, 2);
            response.FinalAmount = response.GrandTotal;

            return response;
        }

        public bool ValidateCouponConditions(
            IEnumerable<BusCouponCondition>? conditions,
            BusBooking bus,
            List<SeatPreviewDto> seats)
        {
            var istDeparture = DateTime.SpecifyKind(bus.DepartureTime, DateTimeKind.Utc).Add(IndiaOffset);
            var preDiscountFare = seats.Sum(s => s.BaseFare);
            if (preDiscountFare <= 0 && bus.PriceInr > 0)
            {
                preDiscountFare = bus.PriceInr;
            }

            var context = new BusCouponValidationContext
            {
                OperatorName = bus.OperatorName,
                BusType = bus.BusType,
                SourceCity = bus.FromCity,
                DestinationCity = bus.ToCity,
                TravelDate = istDeparture,
                DayOfWeek = istDeparture.DayOfWeek,
                BookingFare = preDiscountFare,
                SelectedSeats = seats.Select(s => new BusCouponSeatContext
                {
                    SeatName = s.SeatCode,
                    SeatType = s.SeatType,
                    Fare = s.BaseFare
                }).ToList()
            };

            return ValidateCouponConditions(conditions, context);
        }

        public bool ValidateCouponConditions(
            IEnumerable<BusCouponCondition>? conditions,
            BusCouponValidationContext context)
        {
            if (conditions == null || !conditions.Any())
                return true;

            foreach (var condition in conditions)
            {
                // Unrestricted/ALL sentinel check: Short-circuit immediately without parsing
                if (string.IsNullOrWhiteSpace(condition.Value1) ||
                    string.Equals(condition.Value1.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var trimmedVal1 = condition.Value1.Trim();

                switch (condition.ConditionType)
                {
                    case "OperatorName":
                        if (!string.Equals(context.OperatorName, trimmedVal1, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        break;

                    case "BusType":
                        if (!string.Equals(context.BusType, trimmedVal1, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        break;

                    case "SeatType":
                        // Strict rule: ALL selected seats must match the configured SeatType condition
                        if (context.SelectedSeats == null || !context.SelectedSeats.Any())
                        {
                            return false;
                        }

                        bool allSeatsMatch = context.SelectedSeats.All(s =>
                            s.SeatType != null &&
                            s.SeatType.Contains(trimmedVal1, StringComparison.OrdinalIgnoreCase));

                        if (!allSeatsMatch)
                        {
                            return false;
                        }
                        break;

                    case "SourceCity":
                        if (!string.Equals(context.SourceCity, trimmedVal1, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        break;

                    case "DestinationCity":
                        if (!string.Equals(context.DestinationCity, trimmedVal1, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        break;

                    case "DayOfWeek":
                        if (context.DayOfWeek == null ||
                            !string.Equals(context.DayOfWeek.Value.ToString(), trimmedVal1, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        break;

                    case "TravelDate":
                        if (context.TravelDate == null)
                        {
                            return false;
                        }

                        var depDate = context.TravelDate.Value.Date;
                        if (DateTime.TryParse(trimmedVal1, out var date1))
                        {
                            if (string.IsNullOrWhiteSpace(condition.Value2) ||
                                string.Equals(condition.Value2.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
                            {
                                if (depDate != date1.Date) return false;
                            }
                            else if (DateTime.TryParse(condition.Value2.Trim(), out var date2))
                            {
                                if (depDate < date1.Date || depDate > date2.Date) return false;
                            }
                        }
                        break;

                    case "MinimumFare":
                        decimal currentFare = context.BookingFare;
                        if (!decimal.TryParse(trimmedVal1, out var val1))
                        {
                            break;
                        }

                        decimal val2 = 0m;
                        if (!string.IsNullOrWhiteSpace(condition.Value2) &&
                            !string.Equals(condition.Value2.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
                        {
                            decimal.TryParse(condition.Value2.Trim(), out val2);
                        }

                        switch (condition.ConditionOperator)
                        {
                            case ">":
                                if (!(currentFare > val1)) return false;
                                break;
                            case ">=":
                                if (!(currentFare >= val1)) return false;
                                break;
                            case "<":
                                if (!(currentFare < val1)) return false;
                                break;
                            case "<=":
                                if (!(currentFare <= val1)) return false;
                                break;
                            case "Between":
                                if (!(currentFare >= val1 && currentFare <= val2)) return false;
                                break;
                            default:
                                if (currentFare < val1) return false;
                                break;
                        }
                        break;
                }
            }

            return true;
        }
    }
}