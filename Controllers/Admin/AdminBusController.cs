using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using PickNBook.Api.Extensions;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    [Route("api/admin/bus")]
    //public class AdminBusController(AppDbContext dbContext) : ControllerBase
    public class AdminBusController(AppDbContext dbContext, PickNBook.Api.Services.ISrdvBusService srdvBusService, IMemoryCache cache) : AdminApiController
    {
        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);
        private static readonly string[] AllowedDiscountTypes = ["Percentage", "Fixed"];
        private static readonly string[] AllowedMarkupTypes = ["Percentage", "Fixed"];
        private static readonly string[] AllowedGstCategories = ["AC", "Non-AC","VOLVO"];

      

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

            var sql = "SELECT * FROM v_BusBookingSummary WHERE 1=1";
            var parameters = new Dapper.DynamicParameters();

            if (!string.IsNullOrWhiteSpace(status))
            {
                sql += " AND Status LIKE @Status";
                parameters.Add("Status", status.Trim());
            }

            if (!string.IsNullOrWhiteSpace(pnr))
            {
                sql += " AND (Pnr LIKE @Pnr OR BookingReference LIKE @Pnr)";
                parameters.Add("Pnr", pnr.Trim());
            }

            if (journeyDate.HasValue)
            {
                var (startUtc, endUtc) = GetUtcRangeForIstDate(journeyDate.Value);
                sql += " AND DepartureTime >= @StartUtc AND DepartureTime < @EndUtc";
                parameters.Add("StartUtc", startUtc);
                parameters.Add("EndUtc", endUtc);
            }

            sql += " ORDER BY BookedAtUtc DESC LIMIT @Limit";
            parameters.Add("Limit", limit);

            using var connection = dbContext.Database.GetDbConnection();
            var bookingSummaries = await connection.QueryAsync<BusBookingSummary>(sql, parameters);

            var response = bookingSummaries.Select(x =>
            {
                var departIst = ToIst(x.DepartureTime);
                var arriveIst = ToIst(x.ArrivalTime);
                var journeyDateIst = DateOnly.FromDateTime(departIst);

                return new
                {
                    x.Id,
                    BookingDateUtc = DateTime.SpecifyKind(x.BookedAtUtc, DateTimeKind.Utc),
                    BookingDateIst = ToIst(x.BookedAtUtc),
                    Pax = x.SeatsBooked,
                    x.Segment,
                    JourneyDateIst = journeyDateIst,
                    DepartureTimeIst = departIst,
                    ArrivalTimeIst = arriveIst,
                    x.Pnr,
                    x.BookingReference,
                    x.Status,
                    x.BusOperator,
                    x.BusType,
                    x.CustomerFareInr,
                    x.NetFareInr,
                    x.ProfitInr,
                    x.DiscountAmountInr,
                    x.ConvenienceFeeInr,
                    x.BaseFareInr,
                    x.MarkupAmountInr,
                    x.TaxableFareInr,
                    x.GstPercent,
                    x.GstAmountInr
                };
            }).ToList();

            return Ok(response);
        }

        [HttpPost("bookings/{bookingId:int}/cancel")]
        public async Task<IActionResult> CancelBooking(int bookingId, [FromBody] AdminCancelBusBookingRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var reservation = await dbContext.BusReservations
                .Include(r => r.BusBooking)
                .FirstOrDefaultAsync(r => r.Id == bookingId);

            if (reservation == null || reservation.BusBooking == null)
            {
                return NotFound(new { message = "Booking not found." });
            }

            if (reservation.Status == "Cancelled")
            {
                return BadRequest(new { message = "Booking is already cancelled." });
            }

            var passengers = await dbContext.BusReservationPassengers
                .Where(p => p.BusReservationId == bookingId)
                .ToListAsync();

            bool providerCancelled = false;
            string providerMessage = string.Empty;

            bool partialAllowed = false;
            string actualTraceId = reservation.BusBooking.TraceId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(reservation.SrdvBookingResponseJson))
            {
                try
                {
                    var j = System.Text.Json.JsonDocument.Parse(reservation.SrdvBookingResponseJson);
                    if (j.RootElement.TryGetProperty("TraceId", out var traceProp) && traceProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var traceStr = traceProp.GetString();
                        if (!string.IsNullOrWhiteSpace(traceStr))
                        {
                            actualTraceId = traceStr;
                        }
                    }
                    if (j.RootElement.TryGetProperty("PartialCancellationAllowed", out var pc))
                    {
                        partialAllowed = pc.ValueKind == System.Text.Json.JsonValueKind.String 
                            ? pc.GetString()?.ToLower() == "true" 
                            : pc.GetBoolean();
                    }
                }
                catch { }
            }

            try
            {
                var activePassengers = passengers.Where(p => !p.IsCancelled).ToList();
                int initialActiveCount = activePassengers.Count;

                if (request.PassengerIdsToCancel != null && request.PassengerIdsToCancel.Any())
                {
                    var passengersToCancel = activePassengers.Where(p => request.PassengerIdsToCancel.Contains(p.Id)).ToList();
                    
                    if (passengersToCancel.Count < activePassengers.Count && !partialAllowed)
                    {
                        return BadRequest(new { message = "Provider does not allow partial cancellations." });
                    }

                    activePassengers = passengersToCancel;
                }

                if (!activePassengers.Any())
                {
                    return BadRequest(new { message = "No valid active passengers selected for cancellation." });
                }

                var seatNumbers = activePassengers
                    .Where(x => !string.IsNullOrWhiteSpace(x.SeatNumber))
                    .Select(x => x.SeatNumber!)
                    .ToList();

                if (seatNumbers.Any())
                {
                    if (partialAllowed)
                    {
                        List<BusReservationPassenger> successfullyCancelledPassengers = new();
                        foreach (var p in activePassengers)
                        {
                            if (string.IsNullOrWhiteSpace(p.SeatNumber)) continue;

                            var cancelResult = await srdvBusService.CancelTicketAsync(
                                actualTraceId,
                                p.SeatNumber,
                                string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by admin" : request.Reason.Trim()
                            );
                            
                            if (cancelResult.Success)
                            {
                                successfullyCancelledPassengers.Add(p);
                            }
                            else
                            {
                                throw new Exception($"SRDV Provider Error: {cancelResult.ErrorMessage}");
                            }
                        }

                        if (successfullyCancelledPassengers.Count == 0)
                        {
                            throw new Exception("SRDV Provider failed to cancel the seats on their server.");
                        }

                        activePassengers = successfullyCancelledPassengers;
                    }
                    else
                    {
                        var cancelResult = await srdvBusService.CancelTicketAsync(
                            actualTraceId,
                            string.Join(",", seatNumbers),
                            string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by admin" : request.Reason.Trim()
                        );

                        if (!cancelResult.Success)
                        {
                            throw new Exception($"SRDV Provider Error: {cancelResult.ErrorMessage}");
                        }
                    }
                }

                foreach (var p in activePassengers)
                {
                    p.IsCancelled = true;
                    p.CancelledAtUtc = DateTime.UtcNow;
                }
                providerCancelled = true;

                // Calculate refund proportionally
                decimal totalFareOfCancelled = (reservation.CustomerFareInr / Math.Max(1, passengers.Count)) * activePassengers.Count;
                decimal thisRefund = Math.Max(0m, totalFareOfCancelled - request.CancellationCharges);
                
                reservation.CancellationChargeInr = (reservation.CancellationChargeInr ?? 0m) + request.CancellationCharges;
                reservation.RefundAmountInr = (reservation.RefundAmountInr ?? 0m) + thisRefund;
            }
            catch (Exception ex)
            {
                providerMessage = ex.Message;
            }

            var allCancelled = passengers.All(p => p.IsCancelled);
            reservation.Status = allCancelled ? "Cancelled" : "Partially Cancelled";
            if (allCancelled)
            {
                reservation.CancelledAtUtc = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync();

            return Ok(new
            {
                BookingId = reservation.Id,
                Pnr = reservation.Pnr,
                Status = reservation.Status,
                CancelledAt = reservation.CancelledAtUtc,
                CancellationCharges = reservation.CancellationChargeInr,
                RefundAmount = reservation.RefundAmountInr,
                Message = providerCancelled 
                    ? "Cancellation successful." 
                    : $"Cancellation logged locally. Provider API error: {providerMessage}"
            });
        }

        [HttpGet("discounts")]
        public async Task<IActionResult> GetDiscounts()
        {
            var discounts = await dbContext.BusDiscounts
                .AsNoTracking()
                .OrderByDescending(x => x.EntryDateUtc)
                .ToListAsync();

            var response = discounts.Select(x => new
            {
                x.Id,
                x.Value,
                x.DiscountType,
                x.EntryDateUtc,
                x.UpdateDateUtc,
                x.UpdatedBy,
                x.Remark,
                x.Status,
               
                x.Priority,
                x.IsExclusive,
                x.MinBookingAmount,
               
                x.StartDateUtc,
                x.EndDateUtc
            });

            return Ok(response);
        }

        [HttpGet("discounts/{id:int}")]
        public async Task<IActionResult> GetDiscountById(int id)
        {
            var discount = await dbContext.BusDiscounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (discount is null)
            {
                return NotFound("Discount not found.");
            }

            return Ok(discount);
        }

        [HttpPost("discounts")]
        public async Task<IActionResult> CreateDiscount([FromBody] BusDiscountRequestDto request)
        {
            var error = ValidateDiscountRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            if (!string.IsNullOrWhiteSpace(request.Code))
            {
                var normalizedCode = request.Code.Trim();
                var exists = await dbContext.BusDiscounts.AnyAsync(x => x.Code == normalizedCode);
                if (exists)
                {
                    return BadRequest($"Discount code '{normalizedCode}' already exists.");
                }
            }

            var now = DateTime.UtcNow;
            var row = new BusDiscount
            {
                Code = request.Code?.Trim(),
                Title = request.Title?.Trim(),
                Description = request.Description?.Trim(),
                Value = request.Value,
                DiscountType = NormalizeDiscountType(request.DiscountType),
                IsAutoApply = request.IsAutoApply,
                IsExclusive = request.IsExclusive,
                Priority = request.Priority,
                MinBookingAmount = request.MinBookingAmount,
                StartDateUtc = request.StartDateUtc,
                EndDateUtc = request.EndDateUtc,
                EntryDateUtc = now,
                UpdateDateUtc = now,
                UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy),
                Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim(),
                Status = NormalizeStatus(request.Status)
            };

            dbContext.BusDiscounts.Add(row);
            await dbContext.SaveChangesAsync();
            await SyncPromotionFromDiscountAsync(row);

            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetDiscountById), new { id = row.Id }, row);
        }

        [HttpPut("discounts/{id:int}")]
        public async Task<IActionResult> UpdateDiscount(int id, [FromBody] BusDiscountRequestDto request)
        {
            var row = await dbContext.BusDiscounts.FirstOrDefaultAsync(x => x.Id == id);
            if (row is null)
            {
                return NotFound("Discount not found.");
            }

            var error = ValidateDiscountRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            if (!string.IsNullOrWhiteSpace(request.Code))
            {
                var normalizedCode = request.Code.Trim();
                var exists = await dbContext.BusDiscounts.AnyAsync(x => x.Code == normalizedCode && x.Id != id);
                if (exists)
                {
                    return BadRequest($"Discount code '{normalizedCode}' already exists.");
                }
            }

            row.Code = request.Code?.Trim();
            row.Title = request.Title?.Trim();
            row.Description = request.Description?.Trim();
            row.Value = request.Value;
            row.DiscountType = NormalizeDiscountType(request.DiscountType);
            row.IsAutoApply = request.IsAutoApply;
            row.IsExclusive = request.IsExclusive;
            row.Priority = request.Priority;
            row.MinBookingAmount = request.MinBookingAmount;
            row.StartDateUtc = request.StartDateUtc;
            row.EndDateUtc = request.EndDateUtc;
            row.UpdateDateUtc = DateTime.UtcNow;
            row.UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy);
            row.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
            row.Status = NormalizeStatus(request.Status);

            await dbContext.SaveChangesAsync();
            await SyncPromotionFromDiscountAsync(row);
            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        [HttpDelete("discounts/{id:int}")]
        public async Task<IActionResult> DeleteDiscount(int id)
        {
            var discount = await dbContext.BusDiscounts
                .FirstOrDefaultAsync(x => x.Id == id);

            if (discount is null)
            {
                return NotFound("Discount not found.");
            }

            var linkedPromotion = await dbContext.BusPromotions
                .FirstOrDefaultAsync(x =>
                    x.SourceType == "Discount" &&
                    x.SourceKey == discount.Id.ToString());

            if (linkedPromotion != null)
            {
                linkedPromotion.IsActive = false;
            }

            dbContext.BusDiscounts.Remove(discount);

            await dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "Discount deleted."
            });
        }
        // GET all markup settings
        [HttpGet("markup-settings")]
        public async Task<IActionResult> GetMarkupSettings()
        {
            var rows = await dbContext.BusMarkupSettings
                .AsNoTracking()
                .OrderByDescending(x => x.UpdateDateUtc)
                .ToListAsync();
            return Ok(rows);
        }

        // GET markup by id
        [HttpGet("markup-settings/{id:int}")]
        public async Task<IActionResult> GetMarkupSettingById(int id)
        {
            var row = await dbContext.BusMarkupSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (row == null) return NotFound("Markup setting not found.");
            return Ok(row);
        }

        // POST create markup
        [HttpPost("markup-settings")]
        public async Task<IActionResult> CreateMarkupSetting([FromBody] BusMarkupRequestDto request)
        {
            var error = ValidateMarkupRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim();
            var seatType = request.SeatType.Trim();

            if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                var existingActive = await dbContext.BusMarkupSettings
                    .Where(x => x.SeatType == seatType && x.Status == "Active")
                    .ToListAsync();

                foreach (var item in existingActive)
                {
                    item.Status = "Inactive";
                    item.UpdateDateUtc = DateTime.UtcNow;
                }
            }

            var now = DateTime.UtcNow;
            var row = new BusMarkupSetting
            {
                SeatType = seatType,
                Value = request.Value,
                MarkupType = request.MarkupType.Trim(),
                Status = status,
                EntryDateUtc = now,
                UpdateDateUtc = now,
                UpdatedBy = request.UpdatedBy.Trim(),
                Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
            };
            dbContext.BusMarkupSettings.Add(row);
            await dbContext.SaveChangesAsync();
            
            cache.Remove($"BusMarkup_{seatType.ToUpper()}");
            
            return CreatedAtAction(nameof(GetMarkupSettingById), new { id = row.Id }, row);
        }

        // PUT update markup
        [HttpPut("markup-settings/{id:int}")]
        public async Task<IActionResult> UpdateMarkupSetting(int id, [FromBody] BusMarkupRequestDto request)
        {
            var row = await dbContext.BusMarkupSettings.FirstOrDefaultAsync(x => x.Id == id);
            if (row == null) return NotFound("Markup setting not found.");

            var error = ValidateMarkupRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim();
            var seatType = request.SeatType.Trim();

            if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                var existingActive = await dbContext.BusMarkupSettings
                    .Where(x => x.SeatType == seatType && x.Status == "Active" && x.Id != id)
                    .ToListAsync();

                foreach (var item in existingActive)
                {
                    item.Status = "Inactive";
                    item.UpdateDateUtc = DateTime.UtcNow;
                }
            }

            row.SeatType = seatType;
            row.Value = request.Value;
            row.MarkupType = request.MarkupType.Trim();
            row.Status = status;
            row.UpdateDateUtc = DateTime.UtcNow;
            row.UpdatedBy = request.UpdatedBy.Trim();
            row.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
            await dbContext.SaveChangesAsync();
            
            cache.Remove($"BusMarkup_{seatType.ToUpper()}");
            
            return Ok(row);
        }

        // DELETE markup
        [HttpDelete("markup-settings/{id:int}")]
        public async Task<IActionResult> DeleteMarkupSetting(int id)
        {
            var row = await dbContext.BusMarkupSettings.FindAsync(id);
            if (row == null) return NotFound("Markup setting not found.");
            dbContext.BusMarkupSettings.Remove(row);
            await dbContext.SaveChangesAsync();
            
            cache.Remove($"BusMarkup_{row.SeatType.ToUpper()}");
            
            return Ok(new { message = "Markup setting deleted." });
        }

        // GET all GST settings
        [HttpGet("gst-settings")]
        public async Task<IActionResult> GetGstSettings()
        {
            var rows = await dbContext.BusGstSettings
                .AsNoTracking()
                .OrderByDescending(x => x.UpdateDateUtc)
                .ToListAsync();
            return Ok(rows);
        }

        // GET GST by id
        [HttpGet("gst-settings/{id:int}")]
        public async Task<IActionResult> GetGstSettingById(int id)
        {
            var row = await dbContext.BusGstSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (row == null) return NotFound("GST setting not found.");
            return Ok(row);
        }

        // POST create GST
        [HttpPost("gst-settings")]
        public async Task<IActionResult> CreateGstSetting([FromBody] BusGstRequestDto request)
        {
            var error = ValidateGstRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim();
            var category = request.GstCategory.Trim();

            if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                var existingActive = await dbContext.BusGstSettings
                    .Where(x => x.GstCategory == category && x.Status == "Active")
                    .ToListAsync();

                foreach (var item in existingActive)
                {
                    item.Status = "Inactive";
                    item.UpdateDateUtc = DateTime.UtcNow;
                }
            }

            var now = DateTime.UtcNow;
            var row = new BusGstSetting
            {
                GstCategory = category,
                GstPercent = request.GstPercent,
                Status = status,
                EntryDateUtc = now,
                UpdateDateUtc = now,
                UpdatedBy = request.UpdatedBy.Trim(),
                Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
            };
            dbContext.BusGstSettings.Add(row);
            await dbContext.SaveChangesAsync();
            return CreatedAtAction(nameof(GetGstSettingById), new { id = row.Id }, row);
        }

        // PUT update GST
        [HttpPut("gst-settings/{id:int}")]
        public async Task<IActionResult> UpdateGstSetting(int id, [FromBody] BusGstRequestDto request)
        {
            var row = await dbContext.BusGstSettings.FirstOrDefaultAsync(x => x.Id == id);
            if (row == null) return NotFound("GST setting not found.");

            var error = ValidateGstRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            var status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim();
            var category = request.GstCategory.Trim();

            if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                var existingActive = await dbContext.BusGstSettings
                    .Where(x => x.GstCategory == category && x.Status == "Active" && x.Id != id)
                    .ToListAsync();

                foreach (var item in existingActive)
                {
                    item.Status = "Inactive";
                    item.UpdateDateUtc = DateTime.UtcNow;
                }
            }

            row.GstCategory = category;
            row.GstPercent = request.GstPercent;
            row.Status = status;
            row.UpdateDateUtc = DateTime.UtcNow;
            row.UpdatedBy = request.UpdatedBy.Trim();
            row.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
            await dbContext.SaveChangesAsync();
            return Ok(row);
        }

        // DELETE GST
        [HttpDelete("gst-settings/{id:int}")]
        public async Task<IActionResult> DeleteGstSetting(int id)
        {
            var row = await dbContext.BusGstSettings.FindAsync(id);
            if (row == null) return NotFound("GST setting not found.");
            dbContext.BusGstSettings.Remove(row);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "GST setting deleted." });
        }

        [HttpGet("coupons")]
        public async Task<IActionResult> GetCoupons()
        {
            var coupons = await dbContext.BusCoupons
                .AsNoTracking()
                .OrderByDescending(x => x.EntryDateUtc)
                .ToListAsync();

            var response = coupons.Select(x => new
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
                x.EntryDateUtc,
                x.MaxUsagePerUser,
                x.MinBookingAmount,
                x.Remark,
                x.Priority,
                x.IsExclusive
            });

            return Ok(response);
        }

        [HttpGet("coupons/{id:int}")]
        public async Task<IActionResult> GetCouponById(int id)
        {
            var coupon = await dbContext.BusCoupons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (coupon is null)
            {
                return NotFound("Coupon not found.");
            }

            return Ok(coupon);
        }

        [HttpPost("coupons")]
        public async Task<IActionResult> CreateCoupon([FromBody] BusCouponRequestDto request)
        {
            // Step 1: Validate input
            var error = ValidateCouponRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            // Step 2: Normalize coupon code (VERY IMPORTANT)
            var normalizedCode = request.CouponCode?.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return BadRequest("Coupon code is required.");
            }

            // Step 3: Check duplicate (API-level protection)
            var exists = await dbContext.BusCoupons
                .AnyAsync(x => x.CouponCode == normalizedCode);

            if (exists)
            {
                return BadRequest($"Coupon code '{normalizedCode}' already exists.");
            }

            // Step 4: Create coupon
            var now = DateTime.UtcNow;

            var coupon = new BusCoupon
            {
                Value = request.Value,
                CouponType = NormalizeDiscountType(request.CouponType),
                CouponCode = normalizedCode,
                StartDate = request.StartDate,
                ExpiryDate = request.ExpiryDate,
                UseLimit = request.UseLimit,
                IsAutoApply = request.IsAutoApply,
                IsExclusive = request.IsExclusive,
                Priority = request.Priority,
                // ✅ NEW FIELD (per-user limit)
                MaxUsagePerUser = request.MaxUsagePerUser,
                MinBookingAmount = request.MinBookingAmount,
                UsedCount = 0,
                Status = NormalizeStatus(request.Status),
                EntryDateUtc = now,
                Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim()
            };

            // Step 5: Save safely
            dbContext.BusCoupons.Add(coupon);

            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Final protection (DB unique constraint will trigger here)
                return BadRequest("Coupon code already exists (duplicate detected at database level).");
            }
            await SyncPromotionFromCouponAsync(coupon);
            await dbContext.SaveChangesAsync();
            // Step 6: Return response
            return CreatedAtAction(nameof(GetCouponById), new { id = coupon.Id }, coupon);
        }

        [HttpPut("coupons/{id:int}")]
        public async Task<IActionResult> UpdateCoupon(int id, [FromBody] BusCouponRequestDto request)
        {
            // Step 1: Fetch existing coupon
            var coupon = await dbContext.BusCoupons.FirstOrDefaultAsync(x => x.Id == id);
            if (coupon is null)
            {
                return NotFound("Coupon not found.");
            }

            // Step 2: Validate input
            var error = ValidateCouponRequest(request);
            if (error is not null)
            {
                return BadRequest(error);
            }

            // Step 3: Normalize coupon code
            var normalizedCode = request.CouponCode?.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return BadRequest("Coupon code is required.");
            }

            // Step 4: Check duplicate (exclude current record)
            var exists = await dbContext.BusCoupons
                .AnyAsync(x => x.CouponCode == normalizedCode && x.Id != id);

            if (exists)
            {
                return BadRequest($"Coupon code '{normalizedCode}' already exists.");
            }
           

            // Step 5: Update fields
            coupon.Value = request.Value;
            coupon.CouponType = NormalizeDiscountType(request.CouponType);
            coupon.CouponCode = normalizedCode;
            coupon.StartDate = request.StartDate;
            coupon.ExpiryDate = request.ExpiryDate;
            coupon.UseLimit = request.UseLimit;
            coupon.MaxUsagePerUser = request.MaxUsagePerUser;
            coupon.MinBookingAmount = request.MinBookingAmount;
            coupon.IsAutoApply = request.IsAutoApply;
            coupon.IsExclusive = request.IsExclusive;
            coupon.Priority = request.Priority;
            coupon.Status = NormalizeStatus(request.Status);
            coupon.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();

            // Step 6: Save safely
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return BadRequest("Coupon code already exists (duplicate detected at database level).");
            }
            await SyncPromotionFromCouponAsync(coupon);
            await dbContext.SaveChangesAsync();
            // Step 7: Return response
            return Ok(coupon);
        }

        [HttpDelete("coupons/{id:int}")]
        public async Task<IActionResult> DeleteCoupon(int id)
        {
            var coupon = await dbContext.BusCoupons
                .FirstOrDefaultAsync(x => x.Id == id);

            if (coupon is null)
            {
                return NotFound("Coupon not found.");
            }

            var linkedPromotion = await dbContext.BusPromotions
                .FirstOrDefaultAsync(x =>
                    x.SourceType == "Coupon" &&
                    x.SourceId == coupon.Id);

            if (linkedPromotion != null)
            {
                linkedPromotion.IsActive = false;
            }

            dbContext.BusCoupons.Remove(coupon);

            await dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "Coupon deleted."
            });
        }
        [HttpGet("coupons/used")]
        public async Task<IActionResult> GetUsedCoupons(
     [FromQuery] string? couponCode,
     [FromQuery] string? userId,
     [FromQuery] int limit = 200)
        {
            if (limit <= 0)
                return BadRequest("limit must be greater than 0.");

            limit = Math.Min(limit, 500);

            var queryable = dbContext.BusCouponUsages.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                var normalized = couponCode.Trim().ToUpperInvariant();
                queryable = queryable.Where(x => x.CouponCode == normalized);
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                queryable = queryable.Where(x => x.UserId == userId);
            }

            var rows = await queryable
                .OrderByDescending(x => x.UsedAtUtc)
                .Take(limit)
                .ToListAsync();

            var response = rows.Select(x => new
            {
                x.Id,
                BookingId = x.BusReservationId,
                x.CouponCode,
                x.UserId, // ✅ FIXED
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
            var row = await dbContext.BusConvenienceFees
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
        public async Task<IActionResult> UpdateConvenienceFee([FromBody] BusConvenienceFeeRequestDto request)
        {
            if (request == null)
            {
                return BadRequest("Request payload is required.");
            }

            if (request.FeeInr < 0)
            {
                return BadRequest("Fee must be greater than or equal to 0.");
            }

            var now = DateTime.UtcNow;
            var row = await dbContext.BusConvenienceFees.FirstOrDefaultAsync();
            if (row is null)
            {
                row = new BusConvenienceFee
                {
                    FeeInr = request.FeeInr,
                    EntryDateUtc = now,
                    UpdateDateUtc = now,
                    UpdatedBy = NormalizeUpdatedBy(request.UpdatedBy),
                    Status = NormalizeStatus(request.Status)
                };
                dbContext.BusConvenienceFees.Add(row);
            }
            else
            {
                row.FeeInr = request.FeeInr;
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
            var rows = await dbContext.BusReservations
                .AsNoTracking()
                .Include(x => x.BusBooking)
                .Where(x => x.Status == "Cancelled")
                .OrderByDescending(x => x.CancelledAtUtc)
                .Take(limit)
                .ToListAsync();

            var response = rows
                .Where(x => x.BusBooking != null)
                .Select(x =>
                {
                    var bus = x.BusBooking!;
                    var departIst = ToIst(bus.DepartureTime);
                    var journeyDateIst = DateOnly.FromDateTime(departIst);
                    var cancellationCharge = x.CancellationChargeInr ?? 0m;
                    var refundAmount = x.RefundAmountInr ?? Math.Max(0m, x.TotalPriceInr - cancellationCharge);

                    return new
                    {
                        x.Id,
                        Pnr = x.Pnr,
                        BookingReference = x.BookingReference,
                        CancelledDateUtc = x.CancelledAtUtc.HasValue ? DateTime.SpecifyKind(x.CancelledAtUtc.Value, DateTimeKind.Utc) : (DateTime?)null,
                        Segment = $"{bus.FromCity} - {bus.ToCity}",
                        JourneyDateIst = journeyDateIst,
                        PassengerName = x.PassengerName,
                        AmountInr = x.TotalPriceInr,
                        CancellationChargeInr = cancellationCharge,
                        RefundAmountInr = refundAmount,
                        x.Status
                    };
                })
                .ToList();

            return Ok(response);
        }

        [HttpGet("searches")]
        public async Task<IActionResult> GetSearchHistory([FromQuery] int limit = 200)
        {
            if (limit <= 0)
            {
                return BadRequest("limit must be greater than 0.");
            }

            limit = Math.Min(limit, 500);
            var rows = await dbContext.BusSearchLogs
                .AsNoTracking()
                .OrderByDescending(x => x.SearchedAtUtc)
                .Take(limit)
                .ToListAsync();

            var response = rows.Select(x => new
            {
                x.Id,
                x.UserId,
                x.UserOrGuestId,
                x.IsGuest,
                x.FromCity,
                x.ToCity,
                x.JourneyDate,
                SearchedAtUtc = ToIst(x.SearchedAtUtc)
            });

            return Ok(response);
        }

        private static string? ValidateMarkupRequest(BusMarkupRequestDto request)
        {
            if (request == null)
            {
                return "Request payload is required.";
            }

            if (string.IsNullOrWhiteSpace(request.SeatType))
            {
                return "SeatType is required.";
            }

            if (request.Value <= 0)
            {
                return "Value must be greater than 0.";
            }

            if (string.IsNullOrWhiteSpace(request.MarkupType))
            {
                return "MarkupType is required.";
            }

            var allowedTypes = new[] { "Percentage", "Fixed" };
            if (!allowedTypes.Contains(request.MarkupType.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return $"MarkupType must be one of: {string.Join(", ", allowedTypes)}.";
            }

            if (string.IsNullOrWhiteSpace(request.UpdatedBy))
            {
                return "UpdatedBy is required.";
            }

            return null;
        }

        private static string? ValidateGstRequest(BusGstRequestDto request)
        {
            if (request == null)
            {
                return "Request payload is required.";
            }

            if (string.IsNullOrWhiteSpace(request.GstCategory))
            {
                return "GstCategory is required.";
            }

            if (request.GstPercent <= 0)
            {
                return "GstPercent must be greater than 0.";
            }

            if (string.IsNullOrWhiteSpace(request.UpdatedBy))
            {
                return "UpdatedBy is required.";
            }

            return null;
        }

        private static string? ValidateDiscountRequest(BusDiscountRequestDto request)
        {
            if (request is null)
            {
                return "Request body is required.";
            }

            if (request.Value <= 0)
            {
                return "Value must be greater than 0.";
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

        private static string? ValidateCouponRequest(BusCouponRequestDto request)
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
            if (request.MaxUsagePerUser < 0)
            {
                return "MaxUsagePerUser must be greater than or equal to 0.";
            }
            if (request.MinBookingAmount < 0)
            {
                return "MinBookingAmount must be >= 0.";
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
        private async Task SyncPromotionFromCouponAsync(
    BusCoupon coupon)
        {
            var promo =
                await dbContext.BusPromotions
                    .FirstOrDefaultAsync(x =>
                        x.SourceType == "Coupon" &&
                       x.SourceId == coupon.Id);

            if (promo == null)
            {
                promo = new BusPromotion();

                dbContext.BusPromotions.Add(promo);
            }

            promo.Code = coupon.CouponCode;

            promo.Title = coupon.CouponCode;

            promo.Description = coupon.Remark;

            promo.PromotionType = "Coupon";

            promo.DiscountType = coupon.CouponType;

            promo.DiscountValue = coupon.Value;

            promo.IsActive =
                coupon.Status == "Active";

            promo.IsExclusive =
                coupon.IsExclusive;

            promo.IsAutoApply =
                coupon.IsAutoApply;

            promo.Priority =
                coupon.Priority;

            promo.MaxUsage =
                coupon.UseLimit;

            promo.UsedCount =
                coupon.UsedCount;

            promo.MaxUsagePerUser =
                coupon.MaxUsagePerUser;

            promo.StartDateUtc =
                coupon.StartDate.ToDateTime(
                    TimeOnly.MinValue);

            promo.EndDateUtc =
                coupon.ExpiryDate.ToDateTime(
                    TimeOnly.MaxValue);

            promo.SourceType = "Coupon";

            promo.SourceKey =
                coupon.CouponCode;
            promo.SourceId =
    coupon.Id;
        }
        private async Task SyncPromotionFromDiscountAsync( BusDiscount discount)
        {
            var promo =
                await dbContext.BusPromotions
                    .FirstOrDefaultAsync(x =>
                        x.SourceType == "Discount" &&
                        x.SourceKey ==
                            discount.Id.ToString());

            if (promo == null)
            {
                promo = new BusPromotion();

                dbContext.BusPromotions.Add(promo);
            }

            promo.Code =
                discount.Code ??
                $"DISC-{discount.Id}";

            promo.Title =
                discount.Title ??
                $"Discount {discount.Id}";

            promo.Description =
                discount.Description;

            promo.PromotionType =
                "Discount";

            promo.DiscountType =
                discount.DiscountType;

            promo.DiscountValue =
                discount.Value;

            promo.IsActive =
                discount.Status == "Active";

            promo.IsExclusive =
                discount.IsExclusive;

            promo.IsAutoApply =
                discount.IsAutoApply;

            promo.Priority =
                discount.Priority;

            promo.MinBookingAmount =
                discount.MinBookingAmount;

            promo.StartDateUtc =
                discount.StartDateUtc ??
                DateTime.UtcNow;

            promo.EndDateUtc =
                discount.EndDateUtc ??
                DateTime.UtcNow.AddYears(10);

            promo.SourceType =
                "Discount";

            promo.SourceKey =
                discount.Id.ToString();

            promo.SourceId =
                discount.Id;
        }
        [HttpPost("discounts/{discountId}/conditions")]
        public async Task<IActionResult>
    AddDiscountCondition(
        int discountId,
        [FromBody]
        CreateBusDiscountConditionDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var discount =
                await dbContext.BusDiscounts
                    .FirstOrDefaultAsync(x =>
                        x.Id == discountId);

            if (discount == null)
            {
                return NotFound(
                    "Discount not found.");
            }

            var discountCondition =
                new BusDiscountCondition
                {
                    BusDiscountId = discountId,

                    ConditionType =
                        request.ConditionType,

                    ConditionOperator =
                        request.ConditionOperator,

                    Value1 = request.Value1,

                    Value2 = request.Value2
                };

            dbContext.BusDiscountConditions
                .Add(discountCondition);

            await dbContext.SaveChangesAsync();

            // IMPORTANT
            // SYNC INTO PROMOTION CONDITIONS

            var promotion =
                await dbContext.BusPromotions
                    .FirstOrDefaultAsync(x =>
                        x.SourceType == "Discount" &&
                        x.SourceKey ==
                            discount.Id.ToString());

            if (promotion != null)
            {
                var promotionCondition =
                    new BusPromotionCondition
                    {
                        BusPromotionId =
                            promotion.Id,

                        ConditionType =
                            request.ConditionType,

                        ConditionOperator =
                            request.ConditionOperator,

                        Value1 =
                            request.Value1,

                        Value2 =
                            request.Value2
                    };

                dbContext.BusPromotionConditions
                    .Add(promotionCondition);

                await dbContext.SaveChangesAsync();
            }

            return Ok(
                new
                {
                    message =
                        "Discount condition added successfully."
                });
        }
        [HttpGet("discounts/{discountId}/conditions")]
        public async Task<IActionResult>
    GetDiscountConditions(
        int discountId)
        {
            var conditions =
                await dbContext.BusDiscountConditions
                    .Where(x =>
                        x.BusDiscountId == discountId)
                    .ToListAsync();

            return Ok(conditions);
        }
        [HttpDelete(
    "discounts/conditions/{conditionId}")]
        public async Task<IActionResult>
    DeleteDiscountCondition(
        int conditionId)
        {
            var condition =
                await dbContext.BusDiscountConditions
                    .FirstOrDefaultAsync(x =>
                        x.Id == conditionId);

            if (condition == null)
            {
                return NotFound(
                    "Condition not found.");
            }

            // REMOVE LINKED PROMOTION CONDITION

            var discount =
                await dbContext.BusDiscounts
                    .FirstOrDefaultAsync(x =>
                        x.Id ==
                            condition.BusDiscountId);

            if (discount != null)
            {
                var promotion =
                    await dbContext.BusPromotions
                        .FirstOrDefaultAsync(x =>
                            x.SourceType == "Discount" &&
                            x.SourceKey ==
                                discount.Id.ToString());

                if (promotion != null)
                {
                    var promoCondition =
                        await dbContext
                            .BusPromotionConditions
                            .FirstOrDefaultAsync(x =>
                                x.BusPromotionId ==
                                    promotion.Id &&
                                x.ConditionType ==
                                    condition.ConditionType &&
                                x.Value1 ==
                                    condition.Value1);

                    if (promoCondition != null)
                    {
                        dbContext
                            .BusPromotionConditions
                            .Remove(promoCondition);
                    }
                }
            }

            dbContext.BusDiscountConditions
                .Remove(condition);

            await dbContext.SaveChangesAsync();

            return Ok(
                new
                {
                    message =
                        "Condition deleted successfully."
                });
        }
        [HttpPut(
    "discounts/conditions/{conditionId}")]
        public async Task<IActionResult>
    UpdateDiscountCondition(
        int conditionId,
        [FromBody]
        UpdateBusDiscountConditionDto request)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var condition =
                await dbContext.BusDiscountConditions
                    .FirstOrDefaultAsync(x =>
                        x.Id == conditionId);

            if (condition == null)
            {
                return NotFound(
                    "Condition not found.");
            }

            // STORE OLD VALUES
            // Needed to update synced promotion condition

            var oldConditionType =
                condition.ConditionType;

            var oldValue1 =
                condition.Value1;

            // UPDATE DISCOUNT CONDITION

            condition.ConditionType =
                request.ConditionType;

            condition.ConditionOperator =
                request.ConditionOperator;

            condition.Value1 =
                request.Value1;

            condition.Value2 =
                request.Value2;

            // FIND DISCOUNT

            var discount =
                await dbContext.BusDiscounts
                    .FirstOrDefaultAsync(x =>
                        x.Id ==
                            condition.BusDiscountId);

            if (discount != null)
            {
                // FIND LINKED PROMOTION

                var promotion =
                    await dbContext.BusPromotions
                        .FirstOrDefaultAsync(x =>
                            x.SourceType == "Discount" &&
                            x.SourceKey ==
                                discount.Id.ToString());

                if (promotion != null)
                {
                    // FIND LINKED PROMOTION CONDITION

                    var promoCondition =
                        await dbContext
                            .BusPromotionConditions
                            .FirstOrDefaultAsync(x =>
                                x.BusPromotionId ==
                                    promotion.Id &&
                                x.ConditionType ==
                                    oldConditionType &&
                                x.Value1 ==
                                    oldValue1);

                    if (promoCondition != null)
                    {
                        // UPDATE PROMOTION CONDITION

                        promoCondition.ConditionType =
                            request.ConditionType;

                        promoCondition.ConditionOperator =
                            request.ConditionOperator;

                        promoCondition.Value1 =
                            request.Value1;

                        promoCondition.Value2 =
                            request.Value2;
                    }
                }
            }

            await dbContext.SaveChangesAsync();

            return Ok(
                new
                {
                    message =
                        "Condition updated successfully."
                });
        }
        [HttpGet("bookings/all")]
        public async Task<IActionResult> GetAllBusBookings(
     [FromQuery] string? passengerPhone,
     [FromQuery] string? status)
        {
            var queryable = dbContext.BusReservations
                .AsNoTracking()
                .Include(x => x.BusBooking)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(passengerPhone))
            {
                var phone = passengerPhone.Trim();

                queryable = queryable.Where(x =>
                    EF.Functions.Like(x.PassengerPhone, phone));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = status.Trim();

                queryable = queryable.Where(x =>
                    EF.Functions.Like(x.Status, normalizedStatus));
            }

            var bookings = await queryable
                .OrderByDescending(x => x.BookedAtUtc)
                .Take(500)
                .ToListAsync();

            var bookingIds = bookings
                .Select(x => x.Id)
                .ToList();

            var passengers = await dbContext.BusReservationPassengers
                .AsNoTracking()
                .Where(x => bookingIds.Contains(x.BusReservationId))
                .OrderBy(x => x.Id)
                .ToListAsync();

            var passengersByBooking = passengers
                .GroupBy(x => x.BusReservationId)
                .ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<BusReservationPassenger>)x.ToList());

            var response = bookings
                .Where(x => x.BusBooking is not null)
                .Select(x =>
                {
                    if (!passengersByBooking.TryGetValue(
                        x.Id,
                        out var passengerRows))
                    {
                        passengerRows =
                            Array.Empty<BusReservationPassenger>();
                    }

                    return new
                    {
                        x.Id,
                        x.BookingReference,
                        x.Status,

                        PassengerName = x.PassengerName,
                        PassengerPhone = x.PassengerPhone,
                        PassengerEmail = x.PassengerEmail,

                        x.TotalPriceInr,
                        x.CustomerFareInr,
                        x.NetFareInr,

                        x.DiscountAmountInr,
                        x.AutoDiscountAmountInr,
                        x.CouponDiscountAmountInr,

                        x.ConvenienceFeeInr,
                        x.BaseFareInr,
                        x.MarkupAmountInr,

                        x.GstPercent,
                        x.GstAmountInr,

                        x.CouponCode,
                        x.AutoPromotionCode,

                        BookedAtUtc = DateTime.SpecifyKind(x.BookedAtUtc, DateTimeKind.Utc),
                        CancelledAtUtc = x.CancelledAtUtc.HasValue ? DateTime.SpecifyKind(x.CancelledAtUtc.Value, DateTimeKind.Utc) : (DateTime?)null,

                        Bus = new
                        {
                            x.BusBooking!.Id,
                            x.BusBooking.BusNumber,
                            x.BusBooking.OperatorName,
                            x.BusBooking.BusType,
                            x.BusBooking.FromCity,
                            x.BusBooking.ToCity,
                            DepartureTime = DateTime.SpecifyKind(x.BusBooking.DepartureTime, DateTimeKind.Utc),
                            ArrivalTime = DateTime.SpecifyKind(x.BusBooking.ArrivalTime, DateTimeKind.Utc)
                        },

                        Passengers = passengerRows.Select(p => new
                        {
                            p.Id,
                            p.FullName,
                            p.Gender,
                            p.SeatNumber,
                            p.Age
                        })
                    };
                });

            return Ok(response);
        }

        [HttpGet("srdv-wallet/balance")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetSrdvMasterWalletBalance()
        {
            try
            {
                var ip = HttpContext.GetClientIpAddress();
                var rawJson = await srdvBusService.GetSrdvMasterWalletBalanceAsync(ip);
                return Content(rawJson, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching SRDV wallet balance", error = ex.Message });
            }
        }

        [HttpGet("srdv-wallet/log")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetSrdvMasterWalletLog()
        {
            try
            {
                var ip = HttpContext.GetClientIpAddress();
                var rawJson = await srdvBusService.GetSrdvMasterWalletLogAsync(ip);
                return Content(rawJson, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching SRDV wallet log", error = ex.Message });
            }
        }
    }
}
