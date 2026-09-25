using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services;

public interface IBusPromotionEngineService
{
    Task<BusPricingPreviewResponseDto> CalculateAsync(
        BusBooking bus,
        List<SeatPreviewDto> seats,
        string? couponCode,
        int? promotionId = null,
        int? userId = null,
        int? selectedFeaturedOfferId = null,
        BusCouponValidationContext? validationContext = null);

    bool ValidateCouponConditions(
        IEnumerable<BusCouponCondition>? conditions,
        BusCouponValidationContext context);

    bool ValidateCouponConditions(
        IEnumerable<BusCouponCondition>? conditions,
        BusBooking bus,
        List<SeatPreviewDto> seats);
}