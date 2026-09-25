using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

[Route("api/admin/hotel-coupons")]
public class AdminHotelCouponsController : AdminApiController
{
    private readonly AppDbContext _context;

    public AdminHotelCouponsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var coupons = await _context.HotelCoupons
            .OrderByDescending(c => c.Id)
            .Select(c => MapToResponseDto(c))
            .ToListAsync();

        return Ok(coupons);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var coupon = await _context.HotelCoupons.FirstOrDefaultAsync(c => c.Id == id);
        if (coupon == null)
        {
            return NotFound(new { message = "Hotel coupon not found." });
        }

        return Ok(MapToResponseDto(coupon));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertHotelCouponRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedCode = dto.CouponCode.Trim().ToUpperInvariant();
        var codeExists = await _context.HotelCoupons.AnyAsync(c => c.CouponCode == normalizedCode);
        if (codeExists)
        {
            return BadRequest(new { message = $"Coupon code '{normalizedCode}' already exists." });
        }

        var coupon = new HotelCoupon
        {
            CouponCode = normalizedCode,
            CouponType = dto.CouponType,
            Value = dto.Value,
            MinBookingAmount = dto.MinBookingAmount,
            MaxDiscountAmount = dto.MaxDiscountAmount,
            StartDate = string.IsNullOrWhiteSpace(dto.StartDate) ? DateOnly.FromDateTime(DateTime.UtcNow) : DateOnly.FromDateTime(DateTime.Parse(dto.StartDate)),
            ExpiryDate = string.IsNullOrWhiteSpace(dto.ExpiryDate) ? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(10)) : DateOnly.FromDateTime(DateTime.Parse(dto.ExpiryDate)),
            UseLimit = dto.UseLimit,
            UsedCount = 0,
            MaxUsagePerUser = dto.MaxUsagePerUser,
            Status = dto.Status,
            IsFirstTimeUserOnly = dto.IsFirstTimeUserOnly,
            EntryDateUtc = DateTime.UtcNow,
            Remark = dto.Remark?.Trim()
        };

        _context.HotelCoupons.Add(coupon);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = coupon.Id }, MapToResponseDto(coupon));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertHotelCouponRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var coupon = await _context.HotelCoupons.FirstOrDefaultAsync(c => c.Id == id);
        if (coupon == null)
        {
            return NotFound(new { message = "Hotel coupon not found." });
        }

        var normalizedCode = dto.CouponCode.Trim().ToUpperInvariant();
        var codeExists = await _context.HotelCoupons.AnyAsync(c => c.CouponCode == normalizedCode && c.Id != id);
        if (codeExists)
        {
            return BadRequest(new { message = $"Coupon code '{normalizedCode}' already exists on another coupon." });
        }

        coupon.CouponCode = normalizedCode;
        coupon.CouponType = dto.CouponType;
        coupon.Value = dto.Value;
        coupon.MinBookingAmount = dto.MinBookingAmount;
        coupon.MaxDiscountAmount = dto.MaxDiscountAmount;
        coupon.StartDate = string.IsNullOrWhiteSpace(dto.StartDate) ? DateOnly.FromDateTime(DateTime.UtcNow) : DateOnly.FromDateTime(DateTime.Parse(dto.StartDate));
        coupon.ExpiryDate = string.IsNullOrWhiteSpace(dto.ExpiryDate) ? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(10)) : DateOnly.FromDateTime(DateTime.Parse(dto.ExpiryDate));
        coupon.UseLimit = dto.UseLimit;
        coupon.MaxUsagePerUser = dto.MaxUsagePerUser;
        coupon.Status = dto.Status;
        coupon.IsFirstTimeUserOnly = dto.IsFirstTimeUserOnly;
        coupon.Remark = dto.Remark?.Trim();

        await _context.SaveChangesAsync();

        return Ok(MapToResponseDto(coupon));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var coupon = await _context.HotelCoupons.FirstOrDefaultAsync(c => c.Id == id);
        if (coupon == null)
        {
            return NotFound(new { message = "Hotel coupon not found." });
        }

        _context.HotelCoupons.Remove(coupon);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Hotel coupon deleted successfully." });
    }

    [HttpPost("{id:int}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var coupon = await _context.HotelCoupons.FirstOrDefaultAsync(c => c.Id == id);
        if (coupon == null)
        {
            return NotFound(new { message = "Hotel coupon not found." });
        }

        coupon.Status = coupon.Status == "Active" ? "Inactive" : "Active";
        await _context.SaveChangesAsync();

        return Ok(new { id = coupon.Id, status = coupon.Status });
    }

    private static HotelCouponResponseDto MapToResponseDto(HotelCoupon c)
    {
        return new HotelCouponResponseDto
        {
            Id = c.Id,
            CouponCode = c.CouponCode,
            CouponType = c.CouponType,
            Value = c.Value,
            MinBookingAmount = c.MinBookingAmount,
            MaxDiscountAmount = c.MaxDiscountAmount,
            StartDate = c.StartDate,
            ExpiryDate = c.ExpiryDate,
            UseLimit = c.UseLimit,
            UsedCount = c.UsedCount,
            MaxUsagePerUser = c.MaxUsagePerUser,
            Status = c.Status,
            IsFirstTimeUserOnly = c.IsFirstTimeUserOnly,
            EntryDateUtc = c.EntryDateUtc,
            Remark = c.Remark
        };
    }
}
