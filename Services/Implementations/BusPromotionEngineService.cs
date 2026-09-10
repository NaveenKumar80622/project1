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

                decimal markupAmount = ResolveApplicableMarkup(currentBaseFare, seat.SeatType, allMarkups);

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
            // MANUAL COUPON EVALUATION (Sole source of Bus discounts)
            // =========================================================================
            decimal couponDiscount = 0m;
            BusCoupon? appliedCoupon = null;

            if (!string.IsNullOrWhiteSpace(couponCode))
            {
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

                couponDiscount = appliedCoupon.CouponType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
                    ? subtotal * appliedCoupon.Value / 100m
                    : appliedCoupon.Value;

                if (appliedCoupon.MaxDiscountAmount.HasValue)
                {
                    couponDiscount = Math.Min(couponDiscount, appliedCoupon.MaxDiscountAmount.Value);
                }

                couponDiscount = Math.Min(couponDiscount, subtotal);
                couponDiscount = decimal.Round(couponDiscount, 2, MidpointRounding.AwayFromZero);

                response.AppliedPromotionCode = appliedCoupon.CouponCode;
                response.AppliedPromotionTitle = appliedCoupon.Title ?? appliedCoupon.CouponCode;
                response.AppliedPromotionType = "Coupon";
                response.DiscountSource = "Coupon";
                response.DiscountLabel = appliedCoupon.Title ?? appliedCoupon.CouponCode;
            }

            response.AutoPromotionCode = null;
            response.AutoDiscountAmount = 0m;
            response.ManualDiscountAmount = 0m;
            response.CouponDiscountAmount = couponDiscount;
            response.CouponAmount = couponDiscount;
            response.TotalDiscount = couponDiscount;

            var taxableFare = subtotal - response.TotalDiscount;
            response.TaxableFare = decimal.Round(taxableFare, 2);

            response.GstPercent = 0m;
            response.GstAmount = decimal.Round(totalExternalGst, 2);
            response.ConvenienceFee = 0m;

            response.GrandTotal = decimal.Round(taxableFare + response.GstAmount, 2);
            response.FinalAmount = response.GrandTotal;

            return response;
        }

        public static decimal ResolveApplicableMarkup(
            decimal baseFare,
            string? seatType,
            IEnumerable<BusMarkupSetting> activeMarkups)
        {
            var isSleeper = (seatType ?? "").Contains("sleeper", StringComparison.OrdinalIgnoreCase);
            var normalizedSeatType = isSleeper ? "Sleeper" : "Seater";

            var markup = activeMarkups.FirstOrDefault(x =>
                x.SeatType.Equals(normalizedSeatType, StringComparison.OrdinalIgnoreCase));

            if (markup != null && baseFare > 0)
            {
                return markup.MarkupType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
                    ? baseFare * markup.Value / 100m
                    : markup.Value;
            }

            return 0m;
        }

        public bool ValidateCouponConditions(
            IEnumerable<BusCouponCondition>? conditions,
            BusBooking bus,
            List<SeatPreviewDto> seats)
        {
            var istDeparture = DateTime.SpecifyKind(bus.DepartureTime, DateTimeKind.Utc).Add(IndiaOffset);

            var allMarkups = _db.BusMarkupSettings
                .AsNoTracking()
                .Where(x => x.Status == "Active")
                .ToList();

            decimal preDiscountFare = 0m;
            foreach (var seat in seats)
            {
                var markupAmount = ResolveApplicableMarkup(seat.BaseFare, seat.SeatType, allMarkups);
                preDiscountFare += (seat.BaseFare + markupAmount);
            }

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
                var op = string.IsNullOrWhiteSpace(condition.ConditionOperator) ? "Equals" : condition.ConditionOperator.Trim();

                // Only DayOfWeek condition is evaluated
                if (string.Equals(condition.ConditionType, "DayOfWeek", StringComparison.OrdinalIgnoreCase))
                {
                    if (context.DayOfWeek == null)
                    {
                        return false;
                    }

                    if (!IsDayOfWeekMatching(context.DayOfWeek.Value, condition.ConditionOperator, condition.Value1))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool IsDayOfWeekMatching(DayOfWeek dayOfWeek, string? op, string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || string.Equals(rawValue.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var currentDay = dayOfWeek.ToString();
            var allowedDays = rawValue.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(d => d.Trim())
                                      .ToList();

            var normalizedOp = string.IsNullOrWhiteSpace(op) ? "Equals" : op.Trim();
            switch (normalizedOp)
            {
                case "Equals":
                case "=":
                case "==":
                    return allowedDays.Any(d => string.Equals(currentDay, d, StringComparison.OrdinalIgnoreCase));
                case "NotEquals":
                case "!=":
                    return !allowedDays.Any(d => string.Equals(currentDay, d, StringComparison.OrdinalIgnoreCase));
                default:
                    return false;
            }
        }
    }
}