using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models.DTOs
{
    public class BusCancellationPolicySlabDto
    {
        public double HoursBeforeDeparture { get; set; }
        public string DisplayText { get; set; } = string.Empty;
        public decimal CancellationCharge { get; set; }
        public string CancellationChargeType { get; set; } = "Percentage";
        public string FromDate { get; set; } = string.Empty;
        public string PolicyString { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public decimal EstimatedDeduction { get; set; }
        public decimal EstimatedRefund { get; set; }
    }

    public class BusCancellationBreakdownDto
    {
        public decimal CustomerPaid { get; set; }
        public decimal SupplierCancellationCharge { get; set; }
        public decimal NonRefundableConvenienceFee { get; set; }
        public decimal CouponForfeited { get; set; }
        public decimal NetCustomerRefund { get; set; }
    }

    public class PassengerCancellationEstimateDto
    {
        public int PassengerId { get; set; }
        public string SeatNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public decimal BaseFare { get; set; }
        public decimal EstimatedDeduction { get; set; }
        public decimal EstimatedRefund { get; set; }
        public bool IsCancelled { get; set; }
    }

    public class BusCancellationQuoteResult
    {
        public int BookingId { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public bool CanCancel { get; set; }
        public string? CannotCancelReason { get; set; }
        public DateTime CurrentTimeIst { get; set; }
        public DateTime DepartureTimeIst { get; set; }
        public string DepartureTimeDisplay { get; set; } = string.Empty;
        public double HoursUntilDeparture { get; set; }
        public decimal TotalCustomerPaid { get; set; }
        public decimal EstimatedCancellationCharge { get; set; }
        public decimal EstimatedRefundAmount { get; set; }
        public bool IsNonRefundable { get; set; }
        public string ActivePolicyDescription { get; set; } = string.Empty;
        public BusCancellationBreakdownDto Breakdown { get; set; } = new();
        public List<BusCancellationPolicySlabDto> PolicySlabs { get; set; } = new();
        public List<PassengerCancellationEstimateDto> PassengerEstimates { get; set; } = new();
    }
}
