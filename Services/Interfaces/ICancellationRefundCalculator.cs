using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public interface ICancellationRefundCalculator
    {
        RefundCalculationResult CalculateCustomerRefund(
            RefundCalculationInput input);
    }
}
