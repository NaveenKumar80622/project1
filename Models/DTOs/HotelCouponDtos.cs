using System;
using System.ComponentModel.DataAnnotations;

namespace PickNBook.Api.Models.DTOs;

public class HotelCouponResponseDto
{
    public int Id { get; set; }
    public string CouponCode { get; set; } = string.Empty;
    public string CouponType { get; set; } = "Percentage";
    public decimal Value { get; set; }
    public decimal MinBookingAmount { get; set; }
    public decimal MaxDiscountAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public int UseLimit { get; set; }
    public int UsedCount { get; set; }
    public int MaxUsagePerUser { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsFirstTimeUserOnly { get; set; }
    public DateTime EntryDateUtc { get; set; }
    public string? Remark { get; set; }
}

public class UpsertHotelCouponRequestDto
{
    [Required]
    [MaxLength(50)]
    public string CouponCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string CouponType { get; set; } = "Percentage"; // Percentage, Flat

    [Required]
    [Range(0.01, 100000.00)]
    public decimal Value { get; set; }

    [Range(0, 1000000.00)]
    public decimal MinBookingAmount { get; set; } = 0;

    [Range(0, 1000000.00)]
    public decimal MaxDiscountAmount { get; set; } = 0;

    public string? StartDate { get; set; }

    public string? ExpiryDate { get; set; }

    [Range(0, 1000000)]
    public int UseLimit { get; set; } = 0;

    [Range(1, 100)]
    public int MaxUsagePerUser { get; set; } = 1;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Active"; // Active, Inactive

    public bool IsFirstTimeUserOnly { get; set; } = false;

    [MaxLength(500)]
    public string? Remark { get; set; }
}

public class ValidateHotelCouponRequestDto
{
    [Required]
    public string CouponCode { get; set; } = string.Empty;

    [Required]
    [Range(0.01, 10000000.00)]
    public decimal TotalAmount { get; set; }
}

public class ValidateHotelCouponResponseDto
{
    public bool IsValid { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Message { get; set; } = string.Empty;
}
