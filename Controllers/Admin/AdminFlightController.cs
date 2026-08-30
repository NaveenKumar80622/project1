using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;        

using PickNBook.Api.Services;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    [Route("api/admin/flight")]
    public class AdminFlightController(AppDbContext dbContext, ISrdvFlightService srdvFlightService, ILogger<AdminFlightController> logger) : AdminApiController
    {
        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);
        private static readonly string[] AllowedDiscountTypes = ["Percentage", "Fixed"];

        [HttpGet("bookings")]
        public async Task<IActionResult> GetBookingList(
            [FromQuery] string? status,
            [FromQuery] string? pnr,
            [FromQuery] DateOnly? journeyDate,
            [FromQuery] int limit = 200)
        {
            if (limit <= 0)
            {
                return BadRequest("limit must be greater than 0.");
            }

            limit = Math.Min(limit, 500);

            var queryable = dbContext.FlightReservations
                .AsNoTracking()
                
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.Trim();
                queryable = queryable.Where(x => EF.Functions.Like(x.Status, normalized));
            }

            if (!string.IsNullOrWhiteSpace(pnr))
            {
                var normalized = pnr.Trim();
                queryable = queryable.Where(x => EF.Functions.Like(x.Pnr, normalized) || EF.Functions.Like(x.BookingReference, normalized));
            }

            if (journeyDate.HasValue)
            {
                var (startUtc, endUtc) = GetUtcRangeForIstDate(journeyDate.Value);
                queryable = queryable.Where(x => 
                                                 x.DepartureTime >= startUtc &&
                                                 x.DepartureTime < endUtc);
            }

            var bookings = await queryable
                .OrderByDescending(x => x.BookedAtUtc)
                .Take(limit)
                .ToListAsync();

            var response = bookings
                .Where(x => true)
                .Select(x =>
                {
                    var flight = x;
                    var departIst = ToIst(flight.DepartureTime);
                    var journeyDateIst = DateOnly.FromDateTime(departIst);
                    var customerFare = x.CustomerFareInr > 0 ? x.CustomerFareInr : x.TotalPriceInr;
                    var netFare = x.NetFareInr > 0 ? x.NetFareInr : x.TotalPriceInr;
                    var profit = customerFare - netFare;

                    return new
                    {
                        x.Id,
                        BookingDateUtc = DateTime.SpecifyKind(x.BookedAtUtc, DateTimeKind.Utc),
                        BookingDateIst = ToIst(x.BookedAtUtc),
                        JourneyDateIst = journeyDateIst,
                        Segment = $"{flight.FromCity} - {flight.ToCity}",
                        x.Status,
                        Pnr = x.Pnr,
                        BookingReference = x.BookingReference,
                        Passenger = x.PassengerName,
                        PassengerPhone = x.PassengerPhone,
                        CustomerFareInr = customerFare,
                        NetFareInr = netFare,
                        ProfitInr = profit,
                        BookedBy = x.UserId,
                        TravelClass = x.TravelClass
                    };
                })
                .ToList();

            return Ok(response);
        }

        [HttpGet("discounts")]
        public async Task<IActionResult> GetDiscounts()
        {
            var rows = await dbContext.FlightDiscounts
                .AsNoTracking()
                .OrderByDescending(x => x.UpdateDateUtc)
                .ToListAsync();

            var response = rows.Select(x => new
            {
                x.Id,
                x.Value,
                x.DiscountType,
                x.Name,
                x.UpdateDateUtc,
                x.UpdatedBy,
                x.Remark,
                x.Status
            });

            return Ok(response);
        }

        [HttpPost("discounts")]
        public async Task<IActionResult> CreateDiscount([FromBody] FlightDiscountRequestDto request)
        {
            var error = ValidateFlightDiscountRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var now = DateTime.UtcNow;
            var row = new FlightDiscount
            {
                Value = request.Value,
                DiscountType = NormalizeDiscountType(request.DiscountType),
                Name = request.Name.Trim(),
                EntryDateUtc = now,
                UpdateDateUtc = now,
                UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy),
                Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim(),
                Status = NormalizeStatus(request.Status)
            };

            dbContext.FlightDiscounts.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetDiscountById), new { id = row.Id }, row);
        }

        [HttpGet("discounts/{id:int}")]
        public async Task<IActionResult> GetDiscountById(int id)
        {
            var row = await dbContext.FlightDiscounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Discount not found.");
            }

            return Ok(row);
        }

        [HttpPut("discounts/{id:int}")]
        public async Task<IActionResult> UpdateDiscount(int id, [FromBody] FlightDiscountRequestDto request)
        {
            var row = await dbContext.FlightDiscounts.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Discount not found.");
            }

            var error = ValidateFlightDiscountRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            row.Value = request.Value;
            row.DiscountType = NormalizeDiscountType(request.DiscountType);
            row.Name = request.Name.Trim();
            row.UpdateDateUtc = DateTime.UtcNow;
            row.UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy);
            row.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
            row.Status = NormalizeStatus(request.Status);

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("discounts/{id:int}")]
        public async Task<IActionResult> DeleteDiscount(int id)
        {
            var row = await dbContext.FlightDiscounts.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Discount not found.");
            }

            dbContext.FlightDiscounts.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Discount deleted." });
        }

        [HttpPost("discounts/{discountId:int}/conditions")]
        public async Task<IActionResult> AddDiscountCondition(int discountId, [FromBody] CreateFlightDiscountConditionDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var discount = await dbContext.FlightDiscounts.FirstOrDefaultAsync(x => x.Id == discountId);
            if (discount == null)
            {
                return NotFound("Discount not found.");
            }

            var condition = new FlightDiscountCondition
            {
                FlightDiscountId = discountId,
                ConditionType = request.ConditionType.Trim(),
                ConditionOperator = request.ConditionOperator.Trim(),
                Value1 = request.Value1.Trim(),
                Value2 = request.Value2?.Trim()
            };

            dbContext.FlightDiscountConditions.Add(condition);
            await dbContext.SaveChangesAsync();

            return Ok(new { message = "Discount condition added successfully.", condition });
        }

        [HttpGet("discounts/{discountId:int}/conditions")]
        public async Task<IActionResult> GetDiscountConditions(int discountId)
        {
            var discount = await dbContext.FlightDiscounts.AnyAsync(x => x.Id == discountId);
            if (!discount)
            {
                return NotFound("Discount not found.");
            }

            var conditions = await dbContext.FlightDiscountConditions
                .Where(x => x.FlightDiscountId == discountId)
                .ToListAsync();

            return Ok(conditions);
        }

        [HttpDelete("discounts/conditions/{conditionId:int}")]
        public async Task<IActionResult> DeleteDiscountCondition(int conditionId)
        {
            var condition = await dbContext.FlightDiscountConditions.FirstOrDefaultAsync(x => x.Id == conditionId);
            if (condition == null)
            {
                return NotFound("Condition not found.");
            }

            dbContext.FlightDiscountConditions.Remove(condition);
            await dbContext.SaveChangesAsync();

            return Ok(new { message = "Condition deleted successfully." });
        }

        [HttpGet("remarks")]
        public async Task<IActionResult> GetRemarks()
        {
            var rows = await dbContext.FlightRemarks
                .AsNoTracking()
                .OrderByDescending(x => x.UpdateDateUtc)
                .ToListAsync();

            return Ok(rows);
        }

        [HttpPost("remarks")]
        public async Task<IActionResult> CreateRemark([FromBody] FlightRemarkRequestDto request)
        {
            var error = ValidateFlightRemarkRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var now = DateTime.UtcNow;
            var row = new FlightRemark
            {
                EntryDateUtc = now,
                UpdateDateUtc = now,
                SourceType = request.SourceType.Trim(),
                UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy),
                Remark = request.Remark.Trim(),
                Status = NormalizeStatus(request.Status)
            };

            dbContext.FlightRemarks.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetRemarkById), new { id = row.Id }, row);
        }

        [HttpGet("remarks/{id:int}")]
        public async Task<IActionResult> GetRemarkById(int id)
        {
            var row = await dbContext.FlightRemarks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Remark not found.");
            }

            return Ok(row);
        }

        [HttpPut("remarks/{id:int}")]
        public async Task<IActionResult> UpdateRemark(int id, [FromBody] FlightRemarkRequestDto request)
        {
            var row = await dbContext.FlightRemarks.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Remark not found.");
            }

            var error = ValidateFlightRemarkRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            row.UpdateDateUtc = DateTime.UtcNow;
            row.SourceType = request.SourceType.Trim();
            row.UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy);
            row.Remark = request.Remark.Trim();
            row.Status = NormalizeStatus(request.Status);

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("remarks/{id:int}")]
        public async Task<IActionResult> DeleteRemark(int id)
        {
            var row = await dbContext.FlightRemarks.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Remark not found.");
            }

            dbContext.FlightRemarks.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Remark deleted." });
        }

        [HttpGet("coupons")]
        public async Task<IActionResult> GetCoupons()
        {
            var rows = await dbContext.FlightCoupons
                .AsNoTracking()
                .OrderByDescending(x => x.EntryDateUtc)
                .ToListAsync();

            var response = rows.Select(x => new
            {
                x.Id,
                x.Value,
                x.CouponType,
                x.CouponCode,
                x.StartDate,
                x.ExpiryDate,
                x.UseLimit,
                x.UsedCount,
                x.Status,
                InsertDateUtc = x.EntryDateUtc,
                x.Remark
            });

            return Ok(response);
        }

        [HttpPost("coupons")]
        public async Task<IActionResult> CreateCoupon([FromBody] FlightCouponRequestDto request)
        {
            var error = ValidateFlightCouponRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var row = new FlightCoupon
            {
                Value = request.Value,
                CouponType = NormalizeDiscountType(request.CouponType),
                CouponCode = request.CouponCode.Trim().ToUpperInvariant(),
                StartDate = request.StartDate,
                ExpiryDate = request.ExpiryDate,
                UseLimit = request.UseLimit,
                UsedCount = 0,
                Status = NormalizeStatus(request.Status),
                EntryDateUtc = DateTime.UtcNow,
                Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
            };

            dbContext.FlightCoupons.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCouponById), new { id = row.Id }, row);
        }

        [HttpGet("coupons/{id:int}")]
        public async Task<IActionResult> GetCouponById(int id)
        {
            var row = await dbContext.FlightCoupons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Coupon not found.");
            }

            return Ok(row);
        }

        [HttpPut("coupons/{id:int}")]
        public async Task<IActionResult> UpdateCoupon(int id, [FromBody] FlightCouponRequestDto request)
        {
            var row = await dbContext.FlightCoupons.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Coupon not found.");
            }

            var error = ValidateFlightCouponRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            row.Value = request.Value;
            row.CouponType = NormalizeDiscountType(request.CouponType);
            row.CouponCode = request.CouponCode.Trim().ToUpperInvariant();
            row.StartDate = request.StartDate;
            row.ExpiryDate = request.ExpiryDate;
            row.UseLimit = request.UseLimit;
            row.Status = NormalizeStatus(request.Status);
            row.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("coupons/{id:int}")]
        public async Task<IActionResult> DeleteCoupon(int id)
        {
            var row = await dbContext.FlightCoupons.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Coupon not found.");
            }

            dbContext.FlightCoupons.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Coupon deleted." });
        }

        [HttpGet("coupons/used")]
        public async Task<IActionResult> GetUsedCoupons([FromQuery] string? couponCode, [FromQuery] int limit = 200)
        {
            if (limit <= 0)
            {
                return BadRequest("limit must be greater than 0.");
            }

            limit = Math.Min(limit, 500);
            var queryable = dbContext.FlightCouponUsages.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                var normalized = couponCode.Trim().ToUpperInvariant();
                queryable = queryable.Where(x => x.CouponCode.ToUpper() == normalized);
            }

            var rows = await queryable
                .OrderByDescending(x => x.UsedAtUtc)
                .Take(limit)
                .ToListAsync();

            var response = rows.Select(x => new
            {
                x.Id,
                BookingId = x.FlightReservationId,
                x.CouponCode,
                UsedDateUtc = x.UsedAtUtc,
                x.TotalFareInr,
                x.CouponType,
                x.CouponValue,
                x.CouponAmountInr,
                x.BookingStatus
            });

            return Ok(response);
        }

        [HttpGet("convenience-fee")]
        public async Task<IActionResult> GetConvenienceFee()
        {
            var row = await dbContext.FlightConvenienceFees
                .AsNoTracking()
                .OrderByDescending(x => x.UpdateDateUtc)
                .FirstOrDefaultAsync();

            if (row is null)
            {
                return Ok(new { message = "No convenience fee configured." });
            }

            return Ok(row);
        }

        [HttpPut("convenience-fee")]
        public async Task<IActionResult> UpdateConvenienceFee([FromBody] FlightConvenienceFeeRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            if (request.Value < 0)
            {
                return BadRequest("Value must be greater than or equal to 0.");
            }

            if (!AllowedDiscountTypes.Contains(request.AmountType.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest($"AmountType must be one of: {string.Join(", ", AllowedDiscountTypes)}.");
            }

            var now = DateTime.UtcNow;
            var row = await dbContext.FlightConvenienceFees.FirstOrDefaultAsync();
            if (row is null)
            {
                row = new FlightConvenienceFee
                {
                    AmountType = NormalizeDiscountType(request.AmountType),
                    Value = request.Value,
                    EntryDateUtc = now,
                    UpdateDateUtc = now,
                    UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy),
                    Status = NormalizeStatus(request.Status)
                };
                dbContext.FlightConvenienceFees.Add(row);
            }
            else
            {
                row.AmountType = NormalizeDiscountType(request.AmountType);
                row.Value = request.Value;
                row.UpdateDateUtc = now;
                row.UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy);
                row.Status = NormalizeStatus(request.Status);
            }

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpGet("cancellations")]
        public async Task<IActionResult> GetCancellations([FromQuery] int limit = 200)
        {
            if (limit <= 0)
            {
                return BadRequest("limit must be greater than 0.");
            }

            limit = Math.Min(limit, 500);
            var rows = await dbContext.FlightCancellationRequests
                .AsNoTracking()
                .Include(x => x.FlightReservation)
                .OrderByDescending(x => x.RequestDateUtc)
                .Take(limit)
                .ToListAsync();

            var response = rows
                .Where(x => true)
                .Select(x =>
                {
                    var booking = x.FlightReservation!;
                    var flight = booking;
                    return new
                    {
                        x.Id,
                        RequestDateUtc = DateTime.SpecifyKind(x.RequestDateUtc, DateTimeKind.Utc),
                        Segment = $"{flight.FromCity} - {flight.ToCity}",
                        Customer = booking.PassengerName,
                        Status = x.CancellationStatus,
                        CustomerRefundAmountInr = x.CustomerRefundAmountInr,
                        AdminRefundAmountInr = x.AdminRefundAmountInr,
                        Remark = x.AdminRemark,
                        Details = new
                        {
                            x.CancellationStatus,
                            x.CustomerRefundStatus,
                            x.AdminRefundStatus,
                            x.CustomerRefundAmountInr,
                            x.CustomerCancellationChargeInr,
                            x.CustomerServiceChargeInr,
                            x.AdminRefundAmountInr,
                            x.AdminCancellationChargeInr,
                            x.AdminServiceChargeInr,
                            x.SupplierRemark,
                            x.CustomerRemark,
                            x.AdminRemark
                        }
                    };
                })
                .ToList();

            return Ok(response);
        }

        [HttpPost("cancellations")]
        public async Task<IActionResult> CreateCancellation([FromBody] FlightCancellationRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var booking = await dbContext.FlightReservations.FirstOrDefaultAsync(x => x.Id == request.FlightReservationId);
            if (booking is null)
            {
                return BadRequest("FlightReservationId is invalid.");
            }

            var row = new FlightCancellationRequest
            {
                FlightReservationId = request.FlightReservationId,
                RequestDateUtc = DateTime.UtcNow,
                CancellationStatus = NormalizeStatus(request.CancellationStatus),
                CustomerRefundStatus = NormalizeStatus(request.CustomerRefundStatus),
                AdminRefundStatus = NormalizeStatus(request.AdminRefundStatus),
                CustomerRefundAmountInr = request.CustomerRefundAmountInr,
                CustomerCancellationChargeInr = request.CustomerCancellationChargeInr,
                CustomerServiceChargeInr = request.CustomerServiceChargeInr,
                AdminRefundAmountInr = request.AdminRefundAmountInr,
                AdminCancellationChargeInr = request.AdminCancellationChargeInr,
                AdminServiceChargeInr = request.AdminServiceChargeInr,
                SupplierRemark = request.SupplierRemark,
                CustomerRemark = request.CustomerRemark,
                AdminRemark = request.AdminRemark
            };

            dbContext.FlightCancellationRequests.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCancellationById), new { id = row.Id }, row);
        }

        [HttpGet("cancellations/{id:int}")]
        public async Task<IActionResult> GetCancellationById(int id)
        {
            var row = await dbContext.FlightCancellationRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Cancellation not found.");
            }

            return Ok(row);
        }

        [HttpPut("cancellations/{id:int}")]
        public async Task<IActionResult> UpdateCancellation(int id, [FromBody] FlightCancellationRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var row = await dbContext.FlightCancellationRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Cancellation not found.");
            }

            row.CancellationStatus = NormalizeStatus(request.CancellationStatus);
            row.CustomerRefundStatus = NormalizeStatus(request.CustomerRefundStatus);
            row.AdminRefundStatus = NormalizeStatus(request.AdminRefundStatus);
            row.CustomerRefundAmountInr = request.CustomerRefundAmountInr;
            row.CustomerCancellationChargeInr = request.CustomerCancellationChargeInr;
            row.CustomerServiceChargeInr = request.CustomerServiceChargeInr;
            row.AdminRefundAmountInr = request.AdminRefundAmountInr;
            row.AdminCancellationChargeInr = request.AdminCancellationChargeInr;
            row.AdminServiceChargeInr = request.AdminServiceChargeInr;
            row.SupplierRemark = request.SupplierRemark;
            row.CustomerRemark = request.CustomerRemark;
            row.AdminRemark = request.AdminRemark;

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("cancellations/{id:int}")]
        public async Task<IActionResult> DeleteCancellation(int id)
        {
            var row = await dbContext.FlightCancellationRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Cancellation not found.");
            }

            dbContext.FlightCancellationRequests.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Cancellation deleted." });
        }

        [HttpGet("amendments")]
        public async Task<IActionResult> GetAmendments([FromQuery] int limit = 200)
        {
            if (limit <= 0)
            {
                return BadRequest("limit must be greater than 0.");
            }

            limit = Math.Min(limit, 500);
            var rows = await dbContext.FlightAmendmentRequests
                .AsNoTracking()
                .Include(x => x.FlightReservation)
                .OrderByDescending(x => x.RequestDateUtc)
                .Take(limit)
                .ToListAsync();

            var response = rows
                .Where(x => true)
                .Select(x =>
                {
                    var booking = x.FlightReservation!;
                    var flight = booking;
                    return new
                    {
                        x.Id,
                        RequestDateUtc = DateTime.SpecifyKind(x.RequestDateUtc, DateTimeKind.Utc),
                        Segment = $"{flight.FromCity} - {flight.ToCity}",
                        Customer = booking.PassengerName,
                        Status = x.AmendmentStatus,
                        Remark = x.AdminRemark,
                        Details = new
                        {
                            x.AmendmentStatus,
                            x.SupplierRemark,
                            x.CustomerRemark,
                            x.AdminRemark
                        }
                    };
                })
                .ToList();

            return Ok(response);
        }

        [HttpPost("amendments")]
        public async Task<IActionResult> CreateAmendment([FromBody] FlightAmendmentRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var booking = await dbContext.FlightReservations.FirstOrDefaultAsync(x => x.Id == request.FlightReservationId);
            if (booking is null)
            {
                return BadRequest("FlightReservationId is invalid.");
            }

            var row = new FlightAmendmentRequest
            {
                FlightReservationId = request.FlightReservationId,
                RequestDateUtc = DateTime.UtcNow,
                AmendmentStatus = NormalizeStatus(request.AmendmentStatus),
                SupplierRemark = request.SupplierRemark,
                CustomerRemark = request.CustomerRemark,
                AdminRemark = request.AdminRemark
            };

            dbContext.FlightAmendmentRequests.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAmendmentById), new { id = row.Id }, row);
        }

        [HttpGet("amendments/{id:int}")]
        public async Task<IActionResult> GetAmendmentById(int id)
        {
            var row = await dbContext.FlightAmendmentRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Amendment not found.");
            }

            return Ok(row);
        }

        [HttpPut("amendments/{id:int}")]
        public async Task<IActionResult> UpdateAmendment(int id, [FromBody] FlightAmendmentRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var row = await dbContext.FlightAmendmentRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Amendment not found.");
            }

            row.AmendmentStatus = NormalizeStatus(request.AmendmentStatus);
            row.SupplierRemark = request.SupplierRemark;
            row.CustomerRemark = request.CustomerRemark;
            row.AdminRemark = request.AdminRemark;

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("amendments/{id:int}")]
        public async Task<IActionResult> DeleteAmendment(int id)
        {
            var row = await dbContext.FlightAmendmentRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Amendment not found.");
            }

            dbContext.FlightAmendmentRequests.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Amendment deleted." });
        }

        [HttpGet("searches")]
        public async Task<IActionResult> GetSearchHistory([FromQuery] int limit = 200)
        {
            return Ok(new List<object>());
        }

        [HttpGet("pending-airlines")]
        public async Task<IActionResult> GetPendingAirlines()
        {
            var rows = await dbContext.PendingAirlines
                .AsNoTracking()
                .OrderByDescending(x => x.UpdatedOnUtc)
                .ToListAsync();

            return Ok(rows);
        }

        [HttpPost("pending-airlines")]
        public async Task<IActionResult> CreatePendingAirline([FromBody] PendingAirlineRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.AirlineCode) || string.IsNullOrWhiteSpace(request.FareType))
            {
                return BadRequest("AirlineCode and FareType are required.");
            }

            var row = new PendingAirline
            {
                AirlineCode = request.AirlineCode.Trim().ToUpperInvariant(),
                FareType = request.FareType.Trim(),
                UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy),
                UpdatedOnUtc = DateTime.UtcNow,
                Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
            };

            dbContext.PendingAirlines.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPendingAirlineById), new { id = row.Id }, row);
        }

        [HttpGet("pending-airlines/{id:int}")]
        public async Task<IActionResult> GetPendingAirlineById(int id)
        {
            var row = await dbContext.PendingAirlines.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Pending airline not found.");
            }

            return Ok(row);
        }

        [HttpPut("pending-airlines/{id:int}")]
        public async Task<IActionResult> UpdatePendingAirline(int id, [FromBody] PendingAirlineRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.AirlineCode) || string.IsNullOrWhiteSpace(request.FareType))
            {
                return BadRequest("AirlineCode and FareType are required.");
            }

            var row = await dbContext.PendingAirlines.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Pending airline not found.");
            }

            row.AirlineCode = request.AirlineCode.Trim().ToUpperInvariant();
            row.FareType = request.FareType.Trim();
            row.UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy);
            row.UpdatedOnUtc = DateTime.UtcNow;
            row.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("pending-airlines/{id:int}")]
        public async Task<IActionResult> DeletePendingAirline(int id)
        {
            var row = await dbContext.PendingAirlines.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Pending airline not found.");
            }

            dbContext.PendingAirlines.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Pending airline deleted." });
        }

        [HttpGet("airlines")]
        public async Task<IActionResult> GetAirlines()
        {
            var rows = await dbContext.Airlines.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
            return Ok(rows);
        }

        [HttpPost("airlines")]
        public async Task<IActionResult> CreateAirline([FromBody] AirlineRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest("Name and Code are required.");
            }

            var row = new Airline
            {
                Name = request.Name.Trim(),
                Code = request.Code.Trim().ToUpperInvariant(),
                ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
                Status = NormalizeStatus(request.Status)
            };

            dbContext.Airlines.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAirlineById), new { id = row.Id }, row);
        }

        [HttpGet("airlines/{id:int}")]
        public async Task<IActionResult> GetAirlineById(int id)
        {
            var row = await dbContext.Airlines.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Airline not found.");
            }

            return Ok(row);
        }

        [HttpPut("airlines/{id:int}")]
        public async Task<IActionResult> UpdateAirline(int id, [FromBody] AirlineRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var row = await dbContext.Airlines.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Airline not found.");
            }

            row.Name = request.Name.Trim();
            row.Code = request.Code.Trim().ToUpperInvariant();
            row.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
            row.Status = NormalizeStatus(request.Status);

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("airlines/{id:int}")]
        public async Task<IActionResult> DeleteAirline(int id)
        {
            var row = await dbContext.Airlines.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Airline not found.");
            }

            dbContext.Airlines.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Airline deleted." });
        }

        [HttpGet("airline-webcheck")]
        public async Task<IActionResult> GetAirlineWebcheckLinks()
        {
            var rows = await dbContext.AirlineWebcheckLinks.AsNoTracking().OrderBy(x => x.Airline).ToListAsync();
            return Ok(rows);
        }

        [HttpPost("airline-webcheck")]
        public async Task<IActionResult> CreateAirlineWebcheckLink([FromBody] AirlineWebcheckLinkRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Airline) ||
                string.IsNullOrWhiteSpace(request.AirlineCode) ||
                string.IsNullOrWhiteSpace(request.Url))
            {
                return BadRequest("Airline, AirlineCode and Url are required.");
            }

            if (request.Airline.Length > 120)
            {
                return BadRequest("Airline name cannot exceed 120 characters.");
            }

            if (request.AirlineCode.Length > 10)
            {
                return BadRequest("AirlineCode cannot exceed 10 characters.");
            }

            if (request.Url.Length > 500)
            {
                return BadRequest("Url cannot exceed 500 characters.");
            }

            var trimmedUrl = request.Url.Trim();
            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uriResult) ||
                (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest("Url must be a valid HTTP or HTTPS URL.");
            }

            var normalizedCode = request.AirlineCode.Trim().ToUpperInvariant();
            var exists = await dbContext.AirlineWebcheckLinks.AnyAsync(x => x.AirlineCode == normalizedCode);
            if (exists)
            {
                return BadRequest($"Webcheck link for airline code '{normalizedCode}' already exists.");
            }

            var row = new AirlineWebcheckLink
            {
                Airline = request.Airline.Trim(),
                AirlineCode = normalizedCode,
                Url = trimmedUrl
            };

            dbContext.AirlineWebcheckLinks.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAirlineWebcheckLinkById), new { id = row.Id }, row);
        }

        [HttpGet("airline-webcheck/{id:int}")]
        public async Task<IActionResult> GetAirlineWebcheckLinkById(int id)
        {
            var row = await dbContext.AirlineWebcheckLinks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Webcheck link not found.");
            }

            return Ok(row);
        }

        [HttpPut("airline-webcheck/{id:int}")]
        public async Task<IActionResult> UpdateAirlineWebcheckLink(int id, [FromBody] AirlineWebcheckLinkRequestDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Airline) ||
                string.IsNullOrWhiteSpace(request.AirlineCode) ||
                string.IsNullOrWhiteSpace(request.Url))
            {
                return BadRequest("Airline, AirlineCode and Url are required.");
            }

            if (request.Airline.Length > 120)
            {
                return BadRequest("Airline name cannot exceed 120 characters.");
            }

            if (request.AirlineCode.Length > 10)
            {
                return BadRequest("AirlineCode cannot exceed 10 characters.");
            }

            if (request.Url.Length > 500)
            {
                return BadRequest("Url cannot exceed 500 characters.");
            }

            var trimmedUrl = request.Url.Trim();
            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uriResult) ||
                (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest("Url must be a valid HTTP or HTTPS URL.");
            }

            var normalizedCode = request.AirlineCode.Trim().ToUpperInvariant();
            var exists = await dbContext.AirlineWebcheckLinks.AnyAsync(x => x.AirlineCode == normalizedCode && x.Id != id);
            if (exists)
            {
                return BadRequest($"Webcheck link for airline code '{normalizedCode}' already exists.");
            }

            var row = await dbContext.AirlineWebcheckLinks.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Webcheck link not found.");
            }

            row.Airline = request.Airline.Trim();
            row.AirlineCode = normalizedCode;
            row.Url = trimmedUrl;

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("airline-webcheck/{id:int}")]
        public async Task<IActionResult> DeleteAirlineWebcheckLink(int id)
        {
            var row = await dbContext.AirlineWebcheckLinks.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Webcheck link not found.");
            }

            dbContext.AirlineWebcheckLinks.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Webcheck link deleted." });
        }

        [HttpGet("popular-destinations")]
        public async Task<IActionResult> GetPopularDestinations()
        {
            var rows = await dbContext.PopularDestinations
                .AsNoTracking()
                .OrderByDescending(x => x.EntryDateUtc)
                .ToListAsync();

            return Ok(rows);
        }

        [HttpPost("popular-destinations")]
        public async Task<IActionResult> CreatePopularDestination([FromBody] PopularDestinationRequestDto request)
        {
            var error = ValidatePopularDestinationRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var row = new PopularDestination
            {
                EntryDateUtc = DateTime.UtcNow,
                Title = request.Title.Trim(),
                SubTitle = request.SubTitle.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
                Category = request.Category.Trim(),
                Placement = string.IsNullOrWhiteSpace(request.Placement) ? "Main" : request.Placement.Trim(),
                Url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim(),
                Status = NormalizeStatus(request.Status)
            };

            dbContext.PopularDestinations.Add(row);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPopularDestinationById), new { id = row.Id }, row);
        }

        [HttpGet("popular-destinations/{id:int}")]
        public async Task<IActionResult> GetPopularDestinationById(int id)
        {
            var row = await dbContext.PopularDestinations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Popular destination not found.");
            }

            return Ok(row);
        }

        [HttpPut("popular-destinations/{id:int}")]
        public async Task<IActionResult> UpdatePopularDestination(int id, [FromBody] PopularDestinationRequestDto request)
        {
            var error = ValidatePopularDestinationRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var row = await dbContext.PopularDestinations.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Popular destination not found.");
            }

            row.Title = request.Title.Trim();
            row.SubTitle = request.SubTitle.Trim();
            row.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
            row.Category = request.Category.Trim();
            row.Placement = string.IsNullOrWhiteSpace(request.Placement) ? "Main" : request.Placement.Trim();
            row.Url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
            row.Status = NormalizeStatus(request.Status);

            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("popular-destinations/{id:int}")]
        public async Task<IActionResult> DeletePopularDestination(int id)
        {
            var row = await dbContext.PopularDestinations.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Popular destination not found.");
            }

            dbContext.PopularDestinations.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Popular destination deleted." });
        }

        private static string? ValidatePopularDestinationRequest(PopularDestinationRequestDto request)
        {
            if (request is null)
            {
                return "Request body is required.";
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return "Title is required.";
            }

            if (string.IsNullOrWhiteSpace(request.Category))
            {
                return "Category is required.";
            }

            if (string.IsNullOrWhiteSpace(request.SubTitle))
            {
                return "SubTitle is required.";
            }

            if (request.Title.Length > 120)
            {
                return "Title cannot exceed 120 characters.";
            }

            if (request.SubTitle.Length > 180)
            {
                return "SubTitle cannot exceed 180 characters.";
            }

            if (request.ImageUrl is not null && request.ImageUrl.Length > 500)
            {
                return "ImageUrl cannot exceed 500 characters.";
            }

            if (request.Category.Length > 80)
            {
                return "Category cannot exceed 80 characters.";
            }

            if (request.Placement is not null && request.Placement.Length > 20)
            {
                return "Placement cannot exceed 20 characters.";
            }

            if (request.Url is not null && request.Url.Length > 500)
            {
                return "Url cannot exceed 500 characters.";
            }

            if (request.Status is not null && request.Status.Length > 20)
            {
                return "Status cannot exceed 20 characters.";
            }

            return null;
        }

        private static string? ValidateFlightDiscountRequest(FlightDiscountRequestDto request)
        {
            if (request is null)
            {
                return "Request body is required.";
            }

            if (request.Value <= 0)
            {
                return "Value must be greater than 0.";
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return "Name is required.";
            }

            if (string.IsNullOrWhiteSpace(request.DiscountType))
            {
                return "DiscountType is required.";
            }

            if (!AllowedDiscountTypes.Contains(request.DiscountType.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return $"DiscountType must be one of: {string.Join(", ", AllowedDiscountTypes)}.";
            }

            return null;
        }

        private static string? ValidateFlightRemarkRequest(FlightRemarkRequestDto request)
        {
            if (request is null)
            {
                return "Request body is required.";
            }

            if (string.IsNullOrWhiteSpace(request.SourceType))
            {
                return "SourceType is required.";
            }

            if (string.IsNullOrWhiteSpace(request.Remark))
            {
                return "Remark is required.";
            }

            return null;
        }

        private static string? ValidateFlightCouponRequest(FlightCouponRequestDto request)
        {
            if (request is null)
            {
                return "Request body is required.";
            }

            if (request.Value <= 0)
            {
                return "Value must be greater than 0.";
            }

            if (string.IsNullOrWhiteSpace(request.CouponType))
            {
                return "CouponType is required.";
            }

            if (!AllowedDiscountTypes.Contains(request.CouponType.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return $"CouponType must be one of: {string.Join(", ", AllowedDiscountTypes)}.";
            }

            if (string.IsNullOrWhiteSpace(request.CouponCode))
            {
                return "CouponCode is required.";
            }

            if (request.ExpiryDate < request.StartDate)
            {
                return "ExpiryDate must be on or after StartDate.";
            }

            if (request.UseLimit < 0)
            {
                return "UseLimit must be greater than or equal to 0.";
            }

            return null;
        }

        private static string NormalizeDiscountType(string value)
        {
            var normalized = value.Trim();
            return AllowedDiscountTypes.First(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeStatus(string? status)
        {
            return string.IsNullOrWhiteSpace(status) ? "Active" : status.Trim();
        }

        private static string NormalizeUpdatedBy(string? updatedBy)
        {
            return string.IsNullOrWhiteSpace(updatedBy) ? "system" : updatedBy.Trim();
        }

        private static (DateTime StartUtc, DateTime EndUtc) GetUtcRangeForIstDate(DateOnly date)
        {
            var startIst = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, IndiaOffset);
            var endIst = startIst.AddDays(1);
            return (startIst.UtcDateTime, endIst.UtcDateTime);
        }

        private static DateTime ToIst(DateTime utcDateTime)
        {
            return DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc).Add(IndiaOffset);
        }

        [HttpPost("ApiBalanceCheck")]
        public async Task<IActionResult> ApiBalanceCheck([FromBody] ApiBalanceRequestDto request)
        {
            try
            {
                var responseRaw = await srdvFlightService.GetApiBalanceCheckRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting API balance check.");
                return StatusCode(500, new { message = "Failed to retrieve API balance check.", error = ex.Message });
            }
        }

        [HttpPost("ApiBalanceLog")]
        public async Task<IActionResult> ApiBalanceLog([FromBody] ApiBalanceRequestDto request)
        {
            try
            {
                var responseRaw = await srdvFlightService.GetApiBalanceLogRawAsync(request);
                using var doc = JsonDocument.Parse(responseRaw);
                return Ok(doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting API balance log.");
                return StatusCode(500, new { message = "Failed to retrieve API balance log.", error = ex.Message });
            }
        }
    }

}
