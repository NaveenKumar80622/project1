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
            decimal supplierDebit = input.SupplierDebitAmount > 0 ? input.SupplierDebitAmount : input.SupplierAmount;
            decimal marginDifference = Math.Max(0m, input.OriginalCustomerPaid - supplierDebit);
            decimal markupRefunded = Math.Max(marginDifference, _refundOptions.RefundMarkup ? input.MarkupAmount : 0m);
            decimal markupRetained = Math.Max(0m, input.MarkupAmount - markupRefunded);

            decimal feeRefunded = _refundOptions.RefundConvenienceFee ? input.ConvenienceFee : 0m;
            decimal feeRetained = input.ConvenienceFee - feeRefunded;

            decimal couponForfeited = _refundOptions.RefundCoupon ? 0m : input.DiscountAmount;
            decimal couponRefunded = input.DiscountAmount - couponForfeited;

            decimal serviceCharge = input.ServiceCharge;

            decimal rawCustomerRefund = supplierRefund + markupRefunded - serviceCharge + feeRefunded - couponForfeited;

            if (rawCustomerRefund < 0m)
            {
                rawCustomerRefund = 0m;
            }
            decimal maxRefund = input.OriginalCustomerPaid;
            if (rawCustomerRefund > maxRefund)
            {
                rawCustomerRefund = maxRefund;
            }

            // Rounding: >= 0.5 round UP to 1, < 0.5 round DOWN (MidpointRounding.AwayFromZero)
            decimal roundedRefund = Math.Round(rawCustomerRefund, 0, MidpointRounding.AwayFromZero);
            if (roundedRefund > maxRefund) roundedRefund = maxRefund;
            if (roundedRefund < 0m) roundedRefund = 0m;
            decimal roundingDiff = roundedRefund - rawCustomerRefund;

            return new RefundCalculationResult
            {
                SupplierRefundAmount = supplierRefund,
                SupplierCancellationCharge = input.SupplierCancellationCharge,
                ServiceChargeRetained = serviceCharge,
                MarkupRefunded = markupRefunded,
                MarkupRetained = markupRetained,
                FeeRefunded = feeRefunded,
                FeeRetained = feeRetained,
                CouponForfeited = couponForfeited,
                CouponRefunded = couponRefunded,
                FinalCustomerRefundAmount = roundedRefund,
                RoundingDifference = roundingDiff
            };
        }
    }
}
