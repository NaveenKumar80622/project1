using PickNBook.Api.Models.Config;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;
using System;

namespace PickNBook.Api.Services.Implementations
{
    public class CancellationRefundCalculator : ICancellationRefundCalculator
    {
        private readonly RefundPolicyOptions _refundOptions;

        public CancellationRefundCalculator(Microsoft.Extensions.Options.IOptions<PickNBook.Api.Models.Config.RefundPolicyOptions> options)
        {
            _refundOptions = options.Value;
        }

        public RefundCalculationResult CalculateCustomerRefund(
            RefundCalculationInput input)
        {
            decimal supplierRefund = input.SupplierRefundAmount;

            decimal markupRefunded = _refundOptions.RefundMarkup ? input.MarkupAmount : 0m;
            decimal markupRetained = input.MarkupAmount - markupRefunded;

            decimal feeRefunded = _refundOptions.RefundConvenienceFee ? input.ConvenienceFee : 0m;
            decimal feeRetained = input.ConvenienceFee - feeRefunded;

            decimal couponForfeited = _refundOptions.RefundCoupon ? 0m : input.DiscountAmount;
            decimal couponRefunded = input.DiscountAmount - couponForfeited;

            decimal customerRefund = supplierRefund + markupRefunded + feeRefunded - couponForfeited;

            if (customerRefund < 0)
            {
                customerRefund = 0;
            }
            if (customerRefund > input.OriginalCustomerPaid)
            {
                customerRefund = input.OriginalCustomerPaid;
            }

            return new RefundCalculationResult
            {
                SupplierRefundAmount = supplierRefund,
                SupplierCancellationCharge = input.SupplierCancellationCharge,
                MarkupRefunded = markupRefunded,
                MarkupRetained = markupRetained,
                FeeRefunded = feeRefunded,
                FeeRetained = feeRetained,
                CouponForfeited = couponForfeited,
                CouponRefunded = couponRefunded,
                FinalCustomerRefundAmount = customerRefund
            };
        }
    }
}
