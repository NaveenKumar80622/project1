using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;
using System;

namespace PickNBook.Api.Services.Implementations
{
    public class CancellationRefundCalculator : ICancellationRefundCalculator
    {
        public RefundCalculationResult CalculateCustomerRefund(
            RefundCalculationInput input,
            bool refundMarkup,
            bool refundConvenienceFee,
            bool refundCoupon)
        {
            decimal supplierRefund = input.SupplierRefundAmount;

            decimal markupRefunded = refundMarkup ? input.MarkupAmount : 0m;
            decimal markupRetained = input.MarkupAmount - markupRefunded;

            decimal feeRefunded = refundConvenienceFee ? input.ConvenienceFee : 0m;
            decimal feeRetained = input.ConvenienceFee - feeRefunded;

            decimal couponForfeited = refundCoupon ? 0m : input.DiscountAmount;
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
