using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CouponsController : BaseApiController
{
    private readonly AppDbContext _context;

    public CouponsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Unified endpoint to retrieve active coupons and offers across bus, hotel, and flight verticals.
    /// </summary>
    /// <param name="serviceType">Optional filter: "bus", "hotel", "flight", or "all" (default: "all")</param>
    /// <param name="category">Optional filter: "Coupon", "Offer", or null for both</param>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetCoupons(
        [FromQuery] string? serviceType = "all",
        [FromQuery] string? category = null)
    {
        var targetType = string.IsNullOrWhiteSpace(serviceType) ? "all" : serviceType.Trim().ToLowerInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        var results = new List<UnifiedCouponDto>();

        // 1. Bus Coupons & Offers
        if (targetType is "bus" or "all")
        {
            var busQuery = _context.BusCoupons.AsNoTracking()
                .Where(c => c.Status == "Active" &&
                            c.StartDate <= today &&
                            c.ExpiryDate >= today &&
                            (c.UseLimit == 0 || c.UsedCount < c.UseLimit));

            if (!string.IsNullOrWhiteSpace(category))
            {
                busQuery = busQuery.Where(c => c.PromotionCategory == category);
            }

            var busItems = await busQuery.ToListAsync();
            results.AddRange(busItems.Select(c => new UnifiedCouponDto
            {
                Id = c.Id,
                ServiceType = "bus",
                PromotionCategory = c.PromotionCategory ?? "Coupon",
                CouponCode = c.CouponCode,
                Title = string.IsNullOrWhiteSpace(c.Title) ? c.CouponCode : c.Title,
                Description = string.IsNullOrWhiteSpace(c.Description) ? c.Remark : c.Description,
                CouponType = c.CouponType,
                Value = c.Value,
                MaxDiscountAmount = c.MaxDiscountAmount,
                MinBookingAmount = c.MinBookingAmount,
                StartDate = c.StartDate,
                ExpiryDate = c.ExpiryDate,
                IsAutoApply = c.IsAutoApply,
                IsExclusive = c.IsExclusive,
                IsFirstTimeUserOnly = c.IsFirstTimeUserOnly,
                Priority = c.Priority,
                ImageUrl = c.ImageUrl,
                Remark = c.Remark
            }));
        }

        // 2. Hotel Coupons & Offers
        if (targetType is "hotel" or "all")
        {
            var hotelQuery = _context.HotelCoupons.AsNoTracking()
                .Where(c => c.Status == "Active" &&
                            c.StartDate <= today &&
                            c.ExpiryDate >= today &&
                            (c.UseLimit == 0 || c.UsedCount < c.UseLimit));

            if (!string.IsNullOrWhiteSpace(category))
            {
                hotelQuery = hotelQuery.Where(c => c.PromotionCategory == category);
            }

            var hotelItems = await hotelQuery.ToListAsync();
            results.AddRange(hotelItems.Select(c => new UnifiedCouponDto
            {
                Id = c.Id,
                ServiceType = "hotel",
                PromotionCategory = c.PromotionCategory ?? "Coupon",
                CouponCode = c.CouponCode,
                Title = string.IsNullOrWhiteSpace(c.Title) ? c.CouponCode : c.Title,
                Description = string.IsNullOrWhiteSpace(c.Description) ? c.Remark : c.Description,
                CouponType = c.CouponType,
                Value = c.Value,
                MaxDiscountAmount = c.MaxDiscountAmount,
                MinBookingAmount = c.MinBookingAmount,
                StartDate = c.StartDate,
                ExpiryDate = c.ExpiryDate,
                IsAutoApply = c.IsAutoApply,
                IsExclusive = c.IsExclusive,
                IsFirstTimeUserOnly = c.IsFirstTimeUserOnly,
                Priority = c.Priority,
                ImageUrl = c.ImageUrl,
                Remark = c.Remark
            }));
        }

        // 3. Flight Coupons & Offers
        if (targetType is "flight" or "all")
        {
            var flightQuery = _context.FlightCoupons.AsNoTracking()
                .Where(c => c.Status == "Active" &&
                            c.StartDate <= today &&
                            c.ExpiryDate >= today &&
                            (c.UseLimit == 0 || c.UsedCount < c.UseLimit));

            if (!string.IsNullOrWhiteSpace(category))
            {
                flightQuery = flightQuery.Where(c => c.PromotionCategory == category);
            }

            var flightItems = await flightQuery.ToListAsync();
            results.AddRange(flightItems.Select(c => new UnifiedCouponDto
            {
                Id = c.Id,
                ServiceType = "flight",
                PromotionCategory = c.PromotionCategory ?? "Coupon",
                CouponCode = c.CouponCode,
                Title = string.IsNullOrWhiteSpace(c.Title) ? c.CouponCode : c.Title,
                Description = string.IsNullOrWhiteSpace(c.Description) ? c.Remark : c.Description,
                CouponType = c.CouponType,
                Value = c.Value,
                MaxDiscountAmount = c.MaxDiscountAmount,
                MinBookingAmount = c.MinBookingAmount,
                StartDate = c.StartDate,
                ExpiryDate = c.ExpiryDate,
                IsAutoApply = c.IsAutoApply,
                IsExclusive = c.IsExclusive,
                IsFirstTimeUserOnly = c.IsFirstTimeUserOnly,
                Priority = c.Priority,
                ImageUrl = c.ImageUrl,
                Remark = c.Remark
            }));
        }

        var sortedResults = results
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.ExpiryDate)
            .ToList();

        return Ok(sortedResults);
    }

    /// <summary>
    /// Unified validation endpoint for coupons before checkout.
    /// </summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateCoupon([FromBody] ValidateUnifiedCouponRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CouponCode))
        {
            return BadRequest(new ValidateUnifiedCouponResponseDto
            {
                IsValid = false,
                Message = "Coupon code is required."
            });
        }

        var normalizedCode = request.CouponCode.Trim().ToUpperInvariant();
        var serviceType = string.IsNullOrWhiteSpace(request.ServiceType) ? "bus" : request.ServiceType.Trim().ToLowerInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));

        decimal discountValue = 0m;
        string couponType = "Percentage";
        decimal? maxDiscount = null;
        decimal minBooking = 0m;
        string? remark = null;

        if (serviceType == "bus")
        {
            var coupon = await _context.BusCoupons.AsNoTracking().FirstOrDefaultAsync(c => c.CouponCode == normalizedCode && c.Status == "Active");
            if (coupon == null)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Invalid or inactive bus coupon code." });
            }
            if (coupon.StartDate > today || coupon.ExpiryDate < today)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Coupon is not valid for today's date." });
            }
            if (coupon.UseLimit > 0 && coupon.UsedCount >= coupon.UseLimit)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Coupon usage limit has been reached." });
            }
            if (request.TotalAmount < coupon.MinBookingAmount)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = $"Minimum booking amount of INR {coupon.MinBookingAmount} is required." });
            }

            discountValue = coupon.Value;
            couponType = coupon.CouponType;
            maxDiscount = coupon.MaxDiscountAmount;
            minBooking = coupon.MinBookingAmount;
            remark = coupon.Remark;
        }
        else if (serviceType == "hotel")
        {
            var coupon = await _context.HotelCoupons.AsNoTracking().FirstOrDefaultAsync(c => c.CouponCode == normalizedCode && c.Status == "Active");
            if (coupon == null)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Invalid or inactive hotel coupon code." });
            }
            if (coupon.StartDate > today || coupon.ExpiryDate < today)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Coupon is not valid for today's date." });
            }
            if (coupon.UseLimit > 0 && coupon.UsedCount >= coupon.UseLimit)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Coupon usage limit has been reached." });
            }
            if (request.TotalAmount < coupon.MinBookingAmount)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = $"Minimum booking amount of INR {coupon.MinBookingAmount} is required." });
            }

            discountValue = coupon.Value;
            couponType = coupon.CouponType;
            maxDiscount = coupon.MaxDiscountAmount;
            minBooking = coupon.MinBookingAmount;
            remark = coupon.Remark;
        }
        else if (serviceType == "flight")
        {
            var coupon = await _context.FlightCoupons.AsNoTracking().FirstOrDefaultAsync(c => c.CouponCode == normalizedCode && c.Status == "Active");
            if (coupon == null)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Invalid or inactive flight coupon code." });
            }
            if (coupon.StartDate > today || coupon.ExpiryDate < today)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Coupon is not valid for today's date." });
            }
            if (coupon.UseLimit > 0 && coupon.UsedCount >= coupon.UseLimit)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = "Coupon usage limit has been reached." });
            }
            if (request.TotalAmount < coupon.MinBookingAmount)
            {
                return Ok(new ValidateUnifiedCouponResponseDto { IsValid = false, Message = $"Minimum booking amount of INR {coupon.MinBookingAmount} is required." });
            }

            discountValue = coupon.Value;
            couponType = coupon.CouponType;
            maxDiscount = coupon.MaxDiscountAmount;
            minBooking = coupon.MinBookingAmount;
            remark = coupon.Remark;
        }
        else
        {
            return BadRequest(new ValidateUnifiedCouponResponseDto
            {
                IsValid = false,
                Message = $"Unknown serviceType '{serviceType}'. Must be 'bus', 'hotel', or 'flight'."
            });
        }

        // Calculate discount
        decimal discountAmount = 0m;
        if (couponType.Equals("Percentage", StringComparison.OrdinalIgnoreCase))
        {
            discountAmount = Math.Round((request.TotalAmount * discountValue) / 100m, 2, MidpointRounding.AwayFromZero);
            if (maxDiscount.HasValue && maxDiscount.Value > 0)
            {
                discountAmount = Math.Min(discountAmount, maxDiscount.Value);
            }
        }
        else
        {
            discountAmount = discountValue;
        }

        discountAmount = Math.Min(discountAmount, request.TotalAmount);
        var finalTotal = Math.Max(0m, request.TotalAmount - discountAmount);

        return Ok(new ValidateUnifiedCouponResponseDto
        {
            IsValid = true,
            DiscountAmount = discountAmount,
            FinalTotal = finalTotal,
            Message = string.IsNullOrWhiteSpace(remark) ? "Coupon applied successfully." : remark
        });
    }
}
