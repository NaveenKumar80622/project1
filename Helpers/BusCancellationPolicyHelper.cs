using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Helpers
{
    public static class BusCancellationPolicyHelper
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static List<SrdvCancellationPolicyDto> ParsePolicyDtos(string? rawPolicy)
        {
            if (string.IsNullOrWhiteSpace(rawPolicy))
            {
                return new List<SrdvCancellationPolicyDto>();
            }

            var trimmed = rawPolicy.Trim();
            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
            {
                try
                {
                    var unescaped = JsonSerializer.Deserialize<string>(trimmed);
                    if (!string.IsNullOrWhiteSpace(unescaped))
                    {
                        trimmed = unescaped.Trim();
                    }
                }
                catch
                {
                    // Ignore unescape error and try parsing trimmed
                }
            }

            try
            {
                if (trimmed.StartsWith("["))
                {
                    var list = JsonSerializer.Deserialize<List<SrdvCancellationPolicyDto>>(trimmed, JsonOptions);
                    return list ?? new List<SrdvCancellationPolicyDto>();
                }
                else if (trimmed.StartsWith("{"))
                {
                    var single = JsonSerializer.Deserialize<SrdvCancellationPolicyDto>(trimmed, JsonOptions);
                    return single != null ? new List<SrdvCancellationPolicyDto> { single } : new List<SrdvCancellationPolicyDto>();
                }
            }
            catch
            {
                // Strict: return empty on unparseable JSON without synthetic fallbacks
            }

            return new List<SrdvCancellationPolicyDto>();
        }

        public static (decimal RefundAmount, decimal CancellationCharge) CalculateSupplierRefund(
            BusBooking bus,
            IReadOnlyList<BusReservationPassenger> cancelledPassengers,
            decimal netFareInr,
            int totalBookedSeats,
            string? policyJsonOverride = null,
            DateTime? asOfTimeUtc = null)
        {
            var cancelledSeats = cancelledPassengers?.Count ?? 0;
            var refundablePool = netFareInr;
            var proportionalPrice = totalBookedSeats > 0 ? (refundablePool / totalBookedSeats) * cancelledSeats : 0m;

            if (cancelledSeats == 0 || proportionalPrice <= 0m)
            {
                return (0m, 0m);
            }

            var rawPolicy = !string.IsNullOrWhiteSpace(policyJsonOverride)
                ? policyJsonOverride.Trim()
                : bus?.CancellationPoliciesJson?.Trim();

            var policies = ParsePolicyDtos(rawPolicy);
            if (policies.Count == 0)
            {
                return (0m, proportionalPrice);
            }

            var nowIst = IndiaTimeHelper.ToIst(asOfTimeUtc ?? DateTime.UtcNow);
            var departureIst = bus?.DepartureTime ?? DateTime.MinValue;
            var hoursBeforeDeparture = (departureIst - nowIst).TotalHours;

            if (hoursBeforeDeparture <= 0)
            {
                return (0m, proportionalPrice);
            }

            var orderedTiers = policies
                .Select(p => new
                {
                    Policy = p,
                    Hours = double.TryParse(p.TimeBeforeDept, out var h) ? h : 0
                })
                .OrderBy(x => x.Hours < 0 ? double.MaxValue : x.Hours)
                .ToList();

            decimal matchedCharge = 0m;
            string chargeType = "Percentage";
            bool tierFound = false;

            foreach (var tier in orderedTiers)
            {
                if (tier.Hours > 0 && hoursBeforeDeparture <= tier.Hours)
                {
                    matchedCharge = decimal.TryParse(tier.Policy.CancellationCharge, out var c) ? c : 0m;
                    chargeType = tier.Policy.CancellationChargeType ?? "Percentage";
                    tierFound = true;
                    break;
                }
                if (tier.Hours < 0)
                {
                    matchedCharge = decimal.TryParse(tier.Policy.CancellationCharge, out var c) ? c : 0m;
                    chargeType = tier.Policy.CancellationChargeType ?? "Percentage";
                    tierFound = true;
                    break;
                }
            }

            if (!tierFound)
            {
                return (0m, proportionalPrice);
            }

            var cancelledBaseFare = cancelledPassengers?.Sum(p => p.BaseFareInr) ?? 0m;
            var fareForPercentage = cancelledBaseFare > 0 ? cancelledBaseFare : proportionalPrice;

            decimal cancellationCharge = 0m;
            if (chargeType.Equals("Percentage", StringComparison.OrdinalIgnoreCase))
            {
                cancellationCharge = (fareForPercentage * matchedCharge) / 100m;
            }
            else
            {
                cancellationCharge = matchedCharge * cancelledSeats;
            }

            if (cancellationCharge > proportionalPrice) cancellationCharge = proportionalPrice;
            if (cancellationCharge < 0) cancellationCharge = 0;

            decimal refundAmount = proportionalPrice - cancellationCharge;
            if (refundAmount < 0) refundAmount = 0;

            return (decimal.Round(refundAmount, 2), decimal.Round(cancellationCharge, 2));
        }

        public static BusCancellationQuoteResult CalculateQuote(
            BusReservation reservation,
            BusBooking bus,
            IReadOnlyList<BusReservationPassenger> allPassengers,
            ICancellationRefundCalculator? refundCalculator,
            IEnumerable<int>? targetPassengerIds = null,
            DateTime? asOfTimeUtc = null)
        {
            var nowIst = IndiaTimeHelper.ToIst(asOfTimeUtc ?? DateTime.UtcNow);
            var departureIst = bus.DepartureTime;
            var hoursUntilDeparture = (departureIst - nowIst).TotalHours;

            var targetPaxList = (allPassengers ?? Array.Empty<BusReservationPassenger>())
                .Where(p => !p.IsCancelled)
                .ToList();

            if (targetPassengerIds != null && targetPassengerIds.Any())
            {
                var idSet = new HashSet<int>(targetPassengerIds);
                targetPaxList = targetPaxList.Where(p => idSet.Contains(p.Id)).ToList();
            }

            int totalBookedSeats = reservation.SeatsBooked > 0
                ? reservation.SeatsBooked
                : Math.Max(1, allPassengers?.Count ?? 1);
            int selectedSeatsCount = targetPaxList.Count;

            decimal proportionalPaid = totalBookedSeats > 0
                ? (reservation.TotalPriceInr / totalBookedSeats) * selectedSeatsCount
                : 0m;
            decimal proportionalNetFare = totalBookedSeats > 0
                ? (reservation.NetFareInr / totalBookedSeats) * selectedSeatsCount
                : 0m;
            decimal proportionalMarkup = totalBookedSeats > 0
                ? (reservation.MarkupAmountInr / totalBookedSeats) * selectedSeatsCount
                : 0m;
            decimal totalDiscount = reservation.CouponDiscountAmountInr + reservation.AutoDiscountAmountInr + reservation.FeaturedOfferDiscountAmount;
            decimal proportionalDiscount = totalBookedSeats > 0
                ? (totalDiscount / totalBookedSeats) * selectedSeatsCount
                : 0m;
            decimal proportionalConvenienceFee = totalBookedSeats > 0
                ? (reservation.ConvenienceFeeInr / totalBookedSeats) * selectedSeatsCount
                : 0m;

            bool isConfirmedStatus = BusBookingStatus.IsConfirmed(reservation.Status)
                || reservation.Status == "Booked"
                || reservation.Status == "SUCCESS";
            bool isBeforeDeparture = hoursUntilDeparture > 0;

            bool canCancel = isConfirmedStatus && isBeforeDeparture && selectedSeatsCount > 0;
            string? cannotCancelReason = null;

            if (reservation.Status == "Cancelled" || reservation.Status == BusBookingStatus.Cancelled)
            {
                cannotCancelReason = "Booking is already cancelled.";
            }
            else if (reservation.Status == "CancelInProcess" || reservation.Status == BusBookingStatus.CancelInProcess)
            {
                cannotCancelReason = "Cancellation request is already in progress.";
            }
            else if (!isBeforeDeparture)
            {
                cannotCancelReason = "Cannot cancel ticket after bus departure.";
            }
            else if (!isConfirmedStatus)
            {
                cannotCancelReason = $"Booking status '{reservation.Status}' is not eligible for cancellation.";
            }
            else if (selectedSeatsCount == 0)
            {
                cannotCancelReason = "No active passengers selected for cancellation.";
            }

            var rawPolicy = !string.IsNullOrWhiteSpace(reservation.CancellationPolicyJson)
                ? reservation.CancellationPolicyJson.Trim()
                : bus.CancellationPoliciesJson?.Trim();

            var policyDtos = ParsePolicyDtos(rawPolicy);

            var orderedTiers = policyDtos
                .Select(p => new
                {
                    Policy = p,
                    Hours = double.TryParse(p.TimeBeforeDept, out var h) ? h : 0
                })
                .OrderBy(x => x.Hours < 0 ? double.MaxValue : x.Hours)
                .ToList();

            int matchedTierIndex = -1;
            if (isBeforeDeparture && orderedTiers.Count > 0)
            {
                for (int i = 0; i < orderedTiers.Count; i++)
                {
                    var tier = orderedTiers[i];
                    if (tier.Hours > 0 && hoursUntilDeparture <= tier.Hours)
                    {
                        matchedTierIndex = i;
                        break;
                    }
                    if (tier.Hours < 0)
                    {
                        matchedTierIndex = i;
                        break;
                    }
                }
            }

            var slabs = new List<BusCancellationPolicySlabDto>();
            for (int i = 0; i < orderedTiers.Count; i++)
            {
                var tier = orderedTiers[i];
                bool isActive = (i == matchedTierIndex);
                decimal tierCharge = decimal.TryParse(tier.Policy.CancellationCharge, out var c) ? c : 0m;
                string tierType = tier.Policy.CancellationChargeType ?? "Percentage";

                decimal tierSuppCharge = tierType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
                    ? (proportionalNetFare * tierCharge) / 100m
                    : tierCharge * selectedSeatsCount;

                if (tierSuppCharge > proportionalNetFare) tierSuppCharge = proportionalNetFare;
                if (tierSuppCharge < 0) tierSuppCharge = 0m;
                decimal tierSuppRefund = Math.Max(0m, proportionalNetFare - tierSuppCharge);

                decimal tierCustomerRefund;
                if (refundCalculator != null)
                {
                    var calc = refundCalculator.CalculateCustomerRefund(new RefundCalculationInput
                    {
                        OriginalCustomerPaid = proportionalPaid,
                        SupplierAmount = proportionalNetFare,
                        MarkupAmount = proportionalMarkup,
                        DiscountAmount = proportionalDiscount,
                        ConvenienceFee = proportionalConvenienceFee,
                        SupplierCancellationCharge = tierSuppCharge,
                        SupplierRefundAmount = tierSuppRefund
                    });
                    tierCustomerRefund = calc.FinalCustomerRefundAmount;
                }
                else
                {
                    tierCustomerRefund = Math.Max(0m, Math.Min(proportionalPaid, tierSuppRefund - proportionalDiscount));
                }

                decimal tierDeduction = Math.Max(0m, proportionalPaid - tierCustomerRefund);

                string displayText;
                if (!string.IsNullOrWhiteSpace(tier.Policy.PolicyString))
                {
                    displayText = tier.Policy.PolicyString;
                }
                else if (tier.Hours < 0)
                {
                    displayText = $"Anytime before departure: {tierCharge}{(tierType == "Percentage" ? "%" : " INR")} fee";
                }
                else
                {
                    displayText = $"Within {tier.Hours} hours of departure: {tierCharge}{(tierType == "Percentage" ? "%" : " INR")} fee";
                }

                slabs.Add(new BusCancellationPolicySlabDto
                {
                    HoursBeforeDeparture = tier.Hours,
                    DisplayText = displayText,
                    CancellationCharge = tierCharge,
                    CancellationChargeType = tierType,
                    FromDate = tier.Policy.FromDate,
                    PolicyString = tier.Policy.PolicyString,
                    IsActive = isActive,
                    EstimatedDeduction = decimal.Round(tierDeduction, 2),
                    EstimatedRefund = decimal.Round(tierCustomerRefund, 2)
                });
            }

            decimal finalSupplierRefund = 0m;
            decimal finalSupplierCharge = proportionalNetFare;
            string activePolicyDescription = "No operator cancellation policy provided by bus operator.";

            if (canCancel && matchedTierIndex >= 0 && matchedTierIndex < slabs.Count)
            {
                var activeSlab = slabs[matchedTierIndex];
                activePolicyDescription = activeSlab.DisplayText;

                decimal chargeVal = activeSlab.CancellationCharge;
                if (activeSlab.CancellationChargeType.Equals("Percentage", StringComparison.OrdinalIgnoreCase))
                {
                    finalSupplierCharge = (proportionalNetFare * chargeVal) / 100m;
                }
                else
                {
                    finalSupplierCharge = chargeVal * selectedSeatsCount;
                }

                if (finalSupplierCharge > proportionalNetFare) finalSupplierCharge = proportionalNetFare;
                if (finalSupplierCharge < 0) finalSupplierCharge = 0m;
                finalSupplierRefund = Math.Max(0m, proportionalNetFare - finalSupplierCharge);
            }
            else if (!isBeforeDeparture)
            {
                activePolicyDescription = "Bus has already departed. No cancellation or refund allowed.";
            }

            decimal customerRefundAmount;
            decimal supplierFeeBreakdown;
            decimal convenienceFeeRetained = proportionalConvenienceFee;
            decimal couponForfeited = proportionalDiscount;

            if (canCancel && refundCalculator != null)
            {
                var refundResult = refundCalculator.CalculateCustomerRefund(new RefundCalculationInput
                {
                    OriginalCustomerPaid = proportionalPaid,
                    SupplierAmount = proportionalNetFare,
                    MarkupAmount = proportionalMarkup,
                    DiscountAmount = proportionalDiscount,
                    ConvenienceFee = proportionalConvenienceFee,
                    SupplierCancellationCharge = finalSupplierCharge,
                    SupplierRefundAmount = finalSupplierRefund
                });

                customerRefundAmount = refundResult.FinalCustomerRefundAmount;
                supplierFeeBreakdown = refundResult.SupplierCancellationCharge;
                convenienceFeeRetained = refundResult.FeeRetained;
                couponForfeited = refundResult.CouponForfeited;
            }
            else if (canCancel)
            {
                customerRefundAmount = Math.Max(0m, Math.Min(proportionalPaid, finalSupplierRefund - proportionalDiscount));
                supplierFeeBreakdown = finalSupplierCharge;
            }
            else
            {
                customerRefundAmount = 0m;
                supplierFeeBreakdown = proportionalNetFare;
            }

            decimal totalDeduction = Math.Max(0m, proportionalPaid - customerRefundAmount);

            var passengerEstimates = targetPaxList.Select(p =>
            {
                decimal paxPaid = totalBookedSeats > 0 ? reservation.TotalPriceInr / totalBookedSeats : 0m;
                decimal paxRefund = selectedSeatsCount > 0 ? customerRefundAmount / selectedSeatsCount : 0m;
                decimal paxDeduction = Math.Max(0m, paxPaid - paxRefund);

                return new PassengerCancellationEstimateDto
                {
                    PassengerId = p.Id,
                    SeatNumber = p.SeatNumber ?? string.Empty,
                    FullName = p.FullName,
                    BaseFare = p.BaseFareInr,
                    EstimatedDeduction = decimal.Round(paxDeduction, 2),
                    EstimatedRefund = decimal.Round(paxRefund, 2),
                    IsCancelled = p.IsCancelled
                };
            }).ToList();

            return new BusCancellationQuoteResult
            {
                BookingId = reservation.Id,
                BookingReference = reservation.BookingReference ?? string.Empty,
                CanCancel = canCancel,
                CannotCancelReason = cannotCancelReason,
                CurrentTimeIst = nowIst,
                DepartureTimeIst = departureIst,
                DepartureTimeDisplay = departureIst != default ? departureIst.ToString("dd MMM yyyy, hh:mm tt") : "-",
                HoursUntilDeparture = Math.Round(hoursUntilDeparture, 1),
                TotalCustomerPaid = decimal.Round(proportionalPaid, 2),
                EstimatedCancellationCharge = decimal.Round(totalDeduction, 2),
                EstimatedRefundAmount = decimal.Round(customerRefundAmount, 2),
                IsNonRefundable = (customerRefundAmount <= 0m),
                ActivePolicyDescription = activePolicyDescription,
                Breakdown = new BusCancellationBreakdownDto
                {
                    CustomerPaid = decimal.Round(proportionalPaid, 2),
                    SupplierCancellationCharge = decimal.Round(supplierFeeBreakdown, 2),
                    NonRefundableConvenienceFee = decimal.Round(convenienceFeeRetained, 2),
                    CouponForfeited = decimal.Round(couponForfeited, 2),
                    NetCustomerRefund = decimal.Round(customerRefundAmount, 2)
                },
                PolicySlabs = slabs,
                PassengerEstimates = passengerEstimates
            };
        }
    }
}
