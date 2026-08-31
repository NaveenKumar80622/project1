using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Payments;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;
using System.Text.Json;

namespace PickNBook.Api.Services.Implementations
{
    public class BookingOrchestratorService : IBookingOrchestratorService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<BookingOrchestratorService> _logger;
        private readonly IServiceProvider _serviceProvider; // Used to resolve scoped services like _srdvBusService dynamically without circular deps

        public BookingOrchestratorService(
            AppDbContext dbContext,
            ILogger<BookingOrchestratorService> logger,
            IServiceProvider serviceProvider)
        {
            _dbContext = dbContext;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task<(bool Success, string? ErrorMessage)> ProcessFulfillmentAsync(int paymentId)
        {
            // ATOMIC LOCK: Only ONE thread can transition the status from Pending to InProgress.
            var lockAcquired = await _dbContext.Payments
                .Where(p => p.Id == paymentId && 
                           (p.FulfillmentStatus == "Pending" || p.FulfillmentStatus == null) &&
                           (p.Status == PickNBook.Api.Models.Payments.PaymentStatus.Success || p.Status == "PAID"))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.FulfillmentStatus, "InProgress")) == 1;

            var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
            if (payment == null) return (false, "Payment not found.");

            if (payment.Status != PickNBook.Api.Models.Payments.PaymentStatus.Success && payment.Status != "PAID")
            {
                return (false, "Payment has not been verified as successful. Fulfillment blocked.");
            }

            if (!lockAcquired)
            {
                if (payment.FulfillmentStatus == "Success") return (true, null); // Already handled
                
                _logger.LogWarning("Concurrent fulfillment attempt rejected for Payment {PaymentId}", paymentId);
                return (false, "Concurrent fulfillment already in progress or completed.");
            }

            var pendingBooking = await _dbContext.PendingPaymentBookings.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
            if (pendingBooking == null) return (false, "No pending booking payload found.");

            try
            {
                (bool Success, string? ErrorMessage) result = (false, "Unknown booking type.");
                if (payment.BookingType == BookingType.Bus)
                {
                    result = await ProcessBusBookingAsync(payment, pendingBooking);
                }
                else if (payment.BookingType == BookingType.Hotel)
                {
                    result = await ProcessHotelBookingAsync(payment, pendingBooking);
                }
                else if (payment.BookingType == BookingType.Flight)
                {
                    result = await ProcessFlightBookingAsync(payment, pendingBooking);
                }

                if (!result.Success && payment.FulfillmentStatus.StartsWith("Failed"))
                {
                    await TriggerRefundAsync(payment, result.ErrorMessage ?? "Supplier Booking Failed");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fulfillment failed for payment {PaymentId}", paymentId);

                // Reset context to drop any invalid pending inserts
                _dbContext.ChangeTracker.Clear();
                var paymentToUpdate = await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
                
                if (paymentToUpdate != null)
                {
                    bool supplierSucceeded = await _dbContext.SupplierFulfillmentExecutions
                        .AnyAsync(e => e.PaymentId == paymentId && e.SupplierBookingStatus == "Success");

                    if (supplierSucceeded)
                    {
                        paymentToUpdate.FulfillmentStatus = "Failed_LocalPersistence";
                        paymentToUpdate.FailureReason = ex.Message;
                        await _dbContext.SaveChangesAsync();
                        return (false, "Local persistence failed after supplier success.");
                    }
                    
                    paymentToUpdate.FulfillmentStatus = "Failed_SupplierError";
                    paymentToUpdate.FailureReason = ex.Message;
                    await _dbContext.SaveChangesAsync();
                    await TriggerRefundAsync(paymentToUpdate, "Supplier Booking Failed: " + ex.Message);
                }

                return (false, ex.Message);
            }
        }

        public async Task RecoverFulfillmentAsync(int paymentId)
        {
            var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == paymentId && p.FulfillmentStatus == "Recovering");
            if (payment == null) return;

            var pendingBooking = await _dbContext.PendingPaymentBookings.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
            if (pendingBooking == null) return;

            var execution = await _dbContext.SupplierFulfillmentExecutions.FirstOrDefaultAsync(e => e.PaymentId == paymentId);
            
            if (execution == null)
            {
                // No execution record found. It's stuck but never reached the supplier (or crashed before recording it).
                // To be perfectly safe, we transition to Failed_Unknown so admins can reconcile manually.
                _logger.LogCritical("Payment {PaymentId} is stuck in recovery with NO SupplierFulfillmentExecution. Manual reconciliation required to ensure no duplicate supplier booking.", paymentId);
                payment.FulfillmentStatus = "Failed_Unknown";
                payment.FailureReason = "Stuck with unknown supplier status.";
                await _dbContext.SaveChangesAsync();
                return;
            }

            if (execution.SupplierBookingStatus != "Success")
            {
                _logger.LogInformation("Payment {PaymentId} in recovery with FAILED SupplierFulfillmentExecution. Transitioning to Failed_SupplierError and refunding.", paymentId);
                payment.FulfillmentStatus = "Failed_SupplierError";
                payment.FailureReason = execution.LastError ?? "Supplier execution failed.";
                await _dbContext.SaveChangesAsync();
                await TriggerRefundAsync(payment, "Supplier Booking Failed");
                return;
            }

            // Execution was SUCCESS. We need to retry local persistence.
            _logger.LogInformation("Payment {PaymentId} in recovery with SUCCESSFUL SupplierFulfillmentExecution. Retrying local persistence.", paymentId);
            
            try
            {
                (bool Success, string? ErrorMessage) result = (false, "Unknown booking type.");
                if (payment.BookingType == BookingType.Bus)
                {
                    result = await ProcessBusBookingAsync(payment, pendingBooking, execution);
                }
                else if (payment.BookingType == BookingType.Hotel)
                {
                    result = await ProcessHotelBookingAsync(payment, pendingBooking, execution);
                }
                else if (payment.BookingType == BookingType.Flight)
                {
                    result = await ProcessFlightBookingAsync(payment, pendingBooking, execution);
                }

                if (!result.Success && payment.FulfillmentStatus.StartsWith("Failed"))
                {
                    // Do NOT refund because supplier succeeded. Leave it for manual fix.
                    _logger.LogCritical("Payment {PaymentId}: Retry of local persistence failed. Supplier was successful. DO NOT REFUND.", paymentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recovery of local persistence failed for payment {PaymentId}", paymentId);
                payment.FulfillmentStatus = "Failed_LocalPersistence";
                payment.FailureReason = "Recovery failed: " + ex.Message;
                await _dbContext.SaveChangesAsync();
            }
        }

        private async Task TriggerRefundAsync(Payment payment, string reason)
        {
            try
            {
                if (payment.Status == "Success")
                {
                    // Check if already refunded
                    if (payment.RefundStatus == "Refunded" || payment.RefundStatus == "RefundProcessing") return;
                    
                    var cashfreeService = _serviceProvider.GetRequiredService<PickNBook.Api.Services.Interfaces.ICashfreeService>();
                    string refundId = $"REF-{payment.CashfreeOrderId}"; // Deterministic!
                    
                    var refundResponse = await cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, payment.FinalPayableAmount, refundId, reason);
                    
                    // Check Cashfree's actual refund status from the response
                    string cashfreeRefundStatus = "PENDING";
                    if (refundResponse.RootElement.TryGetProperty("refund_status", out var statusEl))
                    {
                        cashfreeRefundStatus = statusEl.GetString() ?? "PENDING";
                    }
                    
                    payment.RefundId = refundId;
                    payment.RefundReason = reason;
                    
                    if (cashfreeRefundStatus == "SUCCESS")
                    {
                        payment.RefundStatus = "Refunded";
                        payment.Status = "REFUNDED";
                    }
                    else if (cashfreeRefundStatus == "CANCELLED")
                    {
                        payment.RefundStatus = "RefundFailed";
                        payment.LastError = "Cashfree returned CANCELLED for refund.";
                    }
                    else
                    {
                        // PENDING or any other status — refund is in progress
                        payment.RefundStatus = "RefundProcessing";
                    }
                    
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate refund for Payment {PaymentId}, Order {OrderId}", payment.Id, payment.CashfreeOrderId);
                
                // Do not swallow! Save the failure state for the background sweeper
                payment.RefundStatus = "RefundFailed";
                payment.RefundReason = reason;
                payment.RefundId = $"REF-{payment.CashfreeOrderId}";
                payment.LastError = ex.Message;
                payment.RefundAttempts += 1;
                await _dbContext.SaveChangesAsync();
            }
        }

        private async Task<(bool Success, string? ErrorMessage)> ProcessBusBookingAsync(Payment payment, PendingPaymentBooking pending, SupplierFulfillmentExecution? existingExecution = null)
        {
            var request = JsonSerializer.Deserialize<CreateBusBookingRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (request == null) return (false, "Invalid bus booking payload.");

            // Dependencies resolved safely without transaction pollution
            var srdvBusService = _serviceProvider.GetRequiredService<ISrdvBusService>();

            try
            {
                // Parse original passenger and request details
                var passengers = request.Passengers;
                var seatsRequired = passengers.Count;

                var depTime = DateTime.Parse(request.DepartureTime).ToUniversalTime();
                var arrTime = string.IsNullOrWhiteSpace(request.ArrivalTime) ? depTime.AddHours(10) : DateTime.Parse(request.ArrivalTime).ToUniversalTime();

                // 1. Create DB Booking Tracking Record
                var bus = new BusBooking
                {
                    BusNumber = "SRDV-" + Random.Shared.Next(1000, 9999),
                    OperatorName = request.OperatorName ?? "Unknown",
                    BusType = request.BusType ?? "Unknown",
                    GstCategory = "AC",
                    FromCity = request.FromCity,
                    ToCity = request.ToCity,
                    DepartureTime = depTime,
                    ArrivalTime = arrTime,
                    PriceInr = request.TotalFare,
                    TotalSeats = 40,
                    AvailableSeats = 40,
                    BoardingPoint = request.BoardingPointName ?? "Default Point",
                    DroppingPoint = request.DroppingPointName ?? "Default Point",
                    TraceId = request.TraceId,
                    ResultIndex = request.ResultIndex,
                    SrdvIndex = request.SrdvIndex,
                    OperatorId = "",
                    CancellationPoliciesJson = null,
                    IsIdProofRequired = false
                };

                var contactName = string.IsNullOrWhiteSpace(request.PassengerName) ? passengers[0].FullName : request.PassengerName.Trim();

                // Generate PNR
                string pnr = await GenerateUniqueBusPnrAsync();

                // Extract pricing exactly as calculated in Phase 1.5 from the payment object
                var reservation = new BusReservation
                {
                    BookingReference = $"BS-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}",
                    Pnr = pnr,
                    UserId = payment.UserId,
                    BusBookingId = bus.Id,
                    PassengerName = contactName,
                    PassengerPhone = request.PassengerPhone.Trim(),
                    PassengerEmail = string.IsNullOrWhiteSpace(request.PassengerEmail) ? null : request.PassengerEmail.Trim(),
                    SeatsBooked = seatsRequired,
                    TotalPriceInr = payment.FinalPayableAmount, // Pre-calculated
                    CustomerFareInr = payment.FinalPayableAmount,
                    NetFareInr = payment.OriginalAmount,
                    BaseFareInr = payment.OriginalAmount, // Adjust based on DB structure
                    MarkupAmountInr = payment.MarkupAmount,
                    TaxableFareInr = 0,
                    GstPercent = 0,
                    GstAmountInr = 0,
                    DiscountAmountInr = payment.DiscountAmount,
                    ConvenienceFeeInr = payment.ConvenienceFee,
                    CouponCode = payment.CouponCode,
                    AppliedPromotionId = null,
                    AppliedFeaturedOfferId = !string.IsNullOrWhiteSpace(payment.OfferCode) ? int.Parse(payment.OfferCode) : null,
                    Status = "Booked",
                    BookedAtUtc = DateTime.UtcNow,
                    BoardingPointName = request.BoardingPointName,
                    BoardingPointTime = request.BoardingPointTime,
                    DroppingPointName = request.DroppingPointName,
                    DroppingPointTime = request.DroppingPointTime
                };

                var dbPassengers = new List<BusReservationPassenger>();
                foreach (var p in passengers)
                {
                    dbPassengers.Add(new BusReservationPassenger
                    {
                        BusReservationId = reservation.Id,
                        FullName = p.FullName,
                        Gender = p.Gender,
                        SeatNumber = p.SeatNumber!,
                        BaseFareInr = p.BaseFare,
                        SeatType = p.SeatType,
                        Age = p.Age
                    });
                }

                bool isSrdvSuccess = false;
                string? srdvErrorMessage = null;
                string? srdvTravelOperatorPnr = null;
                string? srdvTicketNo = null;
                string? srdvPnr = null;
                string? srdvResponseJson = null;

                if (existingExecution != null)
                {
                    isSrdvSuccess = existingExecution.SupplierBookingStatus == "Success";
                    srdvErrorMessage = existingExecution.LastError;
                    srdvTravelOperatorPnr = existingExecution.SupplierReference;
                    srdvTicketNo = existingExecution.SupplierReference;
                    srdvPnr = existingExecution.SupplierReference;
                    srdvResponseJson = existingExecution.SupplierResponseJson;
                }
                else
                {
                    var srdvReq = new SrdvBusBookingRequestDto
                    {
                        TraceId = request.TraceId,
                        ResultIndex = request.ResultIndex,
                        SrdvIndex = request.SrdvIndex > 0 ? request.SrdvIndex : (bus.SrdvIndex ?? 0),
                        BoardingPointId = request.BoardingPointId ?? bus.BoardingPoint,
                        DroppingPointId = request.DroppingPointId ?? bus.DroppingPoint,
                        Passengers = dbPassengers.Select(p => new SrdvBusPassengerDto
                        {
                            Title = p.Gender == "Male" ? "Mr" : "Ms",
                            FirstName = p.FullName,
                            LastName = "Passenger",
                            Age = p.Age,
                            Gender = p.Gender == "Male" ? 1 : 2,
                            SeatName = p.SeatNumber,
                            Fare = p.BaseFareInr,
                            Address = "PickNBook Address",
                            City = bus.FromCity,
                            State = "State",
                            ContactNo = reservation.PassengerPhone,
                            Email = reservation.PassengerEmail ?? "info@picknbook.com"
                        }).ToList()
                    };

                    var srdvRes = await srdvBusService.BookBusAsync(srdvReq, request.BlockKey ?? "");
                    
                    isSrdvSuccess = srdvRes.Success;
                    srdvErrorMessage = srdvRes.ErrorMessage;
                    srdvTravelOperatorPnr = srdvRes.TravelOperatorPNR;
                    srdvTicketNo = srdvRes.TicketNo;
                    srdvPnr = srdvRes.SrdvBookingId;
                    srdvResponseJson = srdvRes.ResponseJson;
                    
                    // STEP B: Log execution strictly before saving reservation details
                    var execution = new PickNBook.Api.Models.Entities.SupplierFulfillmentExecution
                    {
                        PaymentId = payment.Id,
                        BookingType = "Bus",
                        SupplierReference = srdvRes.TravelOperatorPNR ?? srdvRes.TicketNo ?? "",
                        SupplierBookingStatus = srdvRes.Success ? "Success" : "Failed",
                        SupplierResponseJson = srdvRes.ResponseJson,
                        LastError = srdvRes.ErrorMessage,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    
                    // Save execution independently in a new scope so it commits immediately regardless of the main context
                    using (var executionScope = _serviceProvider.CreateScope())
                    {
                        var executionContext = executionScope.ServiceProvider.GetRequiredService<PickNBook.Api.Data.AppDbContext>();
                        executionContext.SupplierFulfillmentExecutions.Add(execution);
                        await executionContext.SaveChangesAsync();
                    }
                }

                if (!isSrdvSuccess)
                {
                    payment.FulfillmentStatus = "Failed_SupplierError";
                    payment.FailureReason = srdvErrorMessage ?? "Supplier responded with false success flag.";
                    await _dbContext.SaveChangesAsync();
                    return (false, payment.FailureReason);
                }

                // 3. Save Tracking Records (Local Persistence)
                reservation.SrdvBookingId = srdvTravelOperatorPnr;
                reservation.SrdvTicketNo = srdvTicketNo;
                reservation.Status = "Booked";
                reservation.SrdvBookingResponseJson = srdvResponseJson;
                reservation.Pnr = srdvPnr ?? reservation.Pnr;
                
                _dbContext.BusBookings.Add(bus);
                await _dbContext.SaveChangesAsync();
                
                reservation.BusBookingId = bus.Id;
                _dbContext.BusReservations.Add(reservation);
                await _dbContext.SaveChangesAsync();

                foreach (var passenger in dbPassengers)
                {
                    passenger.BusReservationId = reservation.Id;
                }
                _dbContext.BusReservationPassengers.AddRange(dbPassengers);
                await _dbContext.SaveChangesAsync();

                // Update payment success status
                payment.FulfillmentStatus = "Success";
                payment.BookingReferenceId = reservation.Id;

                // Coupon Consumption
                await ProcessCouponConsumptionAsync(payment.CouponCode, payment.UserId, reservation.Id, payment.FinalPayableAmount, payment.DiscountAmount, "Bus");

                // Commit payment and coupon changes
                await _dbContext.SaveChangesAsync();
                
                // Try to update SupplierFulfillmentExecution with ReservationId
                try
                {
                    var exec = await _dbContext.SupplierFulfillmentExecutions.FirstOrDefaultAsync(e => e.PaymentId == payment.Id);
                    if (exec != null)
                    {
                        exec.ReservationId = reservation.Id;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update ReservationId on SupplierFulfillmentExecution for Payment {PaymentId}. Non-fatal.", payment.Id);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bus Booking failed for Payment {Id}", payment.Id);
                return (false, ex.Message);
            }
        }

        private async Task<string> GenerateUniqueBusPnrAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                var prefix = "PNR-B";
                var randomSuffix = Random.Shared.Next(100000, 999999).ToString();
                var pnr = $"{prefix}{randomSuffix}";
                bool exists = await _dbContext.BusReservations.AnyAsync(r => r.Pnr == pnr);
                if (!exists) return pnr;
            }
            return $"PNR-B{DateTime.UtcNow.Ticks}";
        }

        private async Task ProcessCouponConsumptionAsync(string? couponCode, string userId, int reservationId, decimal bookingTotal, decimal discountAmount, string bookingType = "Bus")
        {
            if (string.IsNullOrWhiteSpace(couponCode)) return;
            
            var normalizedCoupon = couponCode.Trim().ToUpperInvariant();
            
            if (bookingType == "Bus")
            {
                int rows = await _dbContext.BusPromotions
                    .Where(x => x.Code == normalizedCoupon && x.UsedCount < (x.MaxUsage ?? 999999))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount + 1));
                    
                if (rows > 0)
                {
                    var promo = await _dbContext.BusPromotions.FirstOrDefaultAsync(x => x.Code == normalizedCoupon);
                    if (promo != null)
                    {
                        var manualUsage = new BusPromotionUsage
                        {
                            BusPromotionId = promo.Id,
                            BusReservationId = reservationId,
                            UserId = userId,
                            PromotionCode = promo.Code,
                            PromotionType = promo.PromotionType,
                            DiscountAmountInr = discountAmount,
                            BookingTotalInr = bookingTotal,
                            BookingStatus = "Booked",
                            UsedAtUtc = DateTime.UtcNow
                        };
                        _dbContext.BusPromotionUsages.Add(manualUsage);
                    }
                }
            }
            else if (bookingType == "Hotel")
            {
                int rows = await _dbContext.HotelCoupons
                    .Where(x => x.CouponCode == normalizedCoupon && (x.UseLimit == 0 || x.UsedCount < x.UseLimit))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount + 1));
                    
                if (rows > 0)
                {
                    var promo = await _dbContext.HotelCoupons.FirstOrDefaultAsync(x => x.CouponCode == normalizedCoupon);
                    if (promo != null)
                    {
                        var manualUsage = new HotelCouponUsage
                        {
                            HotelReservationId = reservationId,
                            UserId = userId,
                            CouponCode = promo.CouponCode,
                            DiscountAmount = discountAmount,
                            TotalPrice = bookingTotal,
                            CouponType = promo.CouponType,
                            CouponValue = promo.Value,
                            BookingStatus = "Booked",
                            UsedAtUtc = DateTime.UtcNow
                        };
                        _dbContext.HotelCouponUsages.Add(manualUsage);
                    }
                }
            }
            else if (bookingType == "Flight")
            {
                int rows = await _dbContext.FlightCoupons
                    .Where(x => x.CouponCode == normalizedCoupon && (x.UseLimit == 0 || x.UsedCount < x.UseLimit))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount + 1));
                    
                if (rows > 0)
                {
                    var promo = await _dbContext.FlightCoupons.FirstOrDefaultAsync(x => x.CouponCode == normalizedCoupon);
                    if (promo != null)
                    {
                        var manualUsage = new FlightCouponUsage
                        {
                            FlightReservationId = reservationId,
                            CouponCode = promo.CouponCode,
                            CouponAmountInr = discountAmount,
                            TotalFareInr = bookingTotal,
                            CouponType = promo.CouponType,
                            CouponValue = promo.Value,
                            BookingStatus = "Booked",
                            UsedAtUtc = DateTime.UtcNow
                        };
                        _dbContext.FlightCouponUsages.Add(manualUsage);
                    }
                }
            }
        }

        private async Task<(bool Success, string? ErrorMessage)> ProcessHotelBookingAsync(Payment payment, PendingPaymentBooking pending, SupplierFulfillmentExecution? existingExecution = null)
        {
            var request = JsonSerializer.Deserialize<HotelBookRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (request == null) return (false, "Invalid hotel booking payload.");

            var hotelService = _serviceProvider.GetRequiredService<IHotelService>();

            try
            {
                // 1. Create Local DB Tracking Record (HotelReservation)
                var bookingRef = $"HT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}";
                var firstRoom = request.HotelRoomsDetails?.FirstOrDefault();

                var reservation = new HotelReservation
                {
                    BookingReference = bookingRef,
                    UserId = payment.UserId,
                    HotelId = request.HotelCode,
                    HotelName = request.HotelName,
                    OfferId = request.ResultIndex,
                    CityCode = "", // Not readily available in DTO
                    GuestName = request.GuestName,
                    GuestEmail = request.GuestEmail,
                    GuestPhone = request.GuestPhone,
                    GuestNationality = request.GuestNationality,
                    RoomTypeName = request.RoomTypeName,
                    CheckInDate = DateTime.TryParse(request.CheckInDate, out var checkIn) ? checkIn : DateTime.MinValue,
                    CheckOutDate = DateTime.TryParse(request.CheckOutDate, out var checkOut) ? checkOut : DateTime.MinValue,
                    Adults = request.HotelRoomsDetails?.Sum(r => r.HotelPassenger.Count(p => p.PaxType == "1")) ?? 1,
                    Children = request.HotelRoomsDetails?.Sum(r => r.ChildCount) ?? 0,
                    Rooms = request.NoOfRooms,
                    
                    SrdvOfferedPrice = payment.OriginalAmount,
                    Price = payment.OriginalAmount,
                    NetPrice = payment.OriginalAmount,
                    MarkupAmount = payment.MarkupAmount,
                    BasePrice = payment.OriginalAmount,
                    ConvenienceFee = payment.ConvenienceFee,
                    TotalPrice = payment.FinalPayableAmount,
                    
                    SrdvGstAmount = firstRoom?.Price?.TotalGSTAmount ?? 0m,
                    SrdvCgstAmount = firstRoom?.Price?.GST?.CGSTAmount ?? 0m,
                    SrdvSgstAmount = firstRoom?.Price?.GST?.SGSTAmount ?? 0m,
                    SrdvIgstAmount = firstRoom?.Price?.GST?.IGSTAmount ?? 0m,
                    
                    RatePlanCode = request.RatePlanCode,
                    RoomTypeCode = request.RoomTypeCode,
                    LastCancellationDate = DateTime.TryParse(firstRoom?.LastCancellationDate, out var lcd) ? lcd : null,
                    CancellationPolicyJson = firstRoom?.CancellationPolicies != null ? JsonSerializer.Serialize(firstRoom.CancellationPolicies) : null,
                    
                    CouponCode = payment.CouponCode,
                    CouponDiscount = payment.DiscountAmount,
                    
                    Currency = payment.Currency,
                    Status = "Booked",
                    CreatedAt = DateTime.UtcNow
                };

                bool isSrdvSuccess = false;
                string? srdvErrorMessage = null;
                string? srdvProviderBookingId = null;
                string? srdvConfirmationNo = null;
                string? srdvInvoiceNumber = null;
                string? srdvResponseJson = null;

                if (existingExecution != null)
                {
                    isSrdvSuccess = existingExecution.SupplierBookingStatus == "Success";
                    srdvErrorMessage = existingExecution.LastError;
                    srdvProviderBookingId = existingExecution.SupplierReference;
                    srdvResponseJson = existingExecution.SupplierResponseJson;
                }
                else
                {
                    var srdvRes = await hotelService.BookRoomAsync(request);
                    var result = srdvRes.BookResult;
                    
                    isSrdvSuccess = result != null && (result.ResponseStatus == 1 || result.Status?.ToUpperInvariant() == "CONFIRMED");
                    srdvErrorMessage = result?.Error?.ErrorMessage ?? "Unknown Error";
                    srdvProviderBookingId = result?.BookingId.ToString();
                    srdvConfirmationNo = result?.ConfirmationNo;
                    srdvInvoiceNumber = result?.InvoiceNumber;
                    srdvResponseJson = JsonSerializer.Serialize(srdvRes);

                    if (result != null && (result.IsPriceChanged || result.IsCancellationPolicyChanged))
                    {
                        isSrdvSuccess = false; // We treat this as a failure because we don't want to auto-book changed prices.
                        srdvErrorMessage = "Price or Cancellation Policy changed at provider during booking.";
                    }

                    var execution = new PickNBook.Api.Models.Entities.SupplierFulfillmentExecution
                    {
                        PaymentId = payment.Id,
                        BookingType = "Hotel",
                        SupplierReference = srdvProviderBookingId ?? "",
                        SupplierBookingStatus = isSrdvSuccess ? "Success" : "Failed",
                        SupplierResponseJson = srdvResponseJson,
                        LastError = isSrdvSuccess ? null : srdvErrorMessage,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    
                    using (var executionScope = _serviceProvider.CreateScope())
                    {
                        var executionContext = executionScope.ServiceProvider.GetRequiredService<PickNBook.Api.Data.AppDbContext>();
                        executionContext.SupplierFulfillmentExecutions.Add(execution);
                        await executionContext.SaveChangesAsync();
                    }
                }

                if (!isSrdvSuccess)
                {
                    payment.FulfillmentStatus = "Failed_SupplierError";
                    payment.FailureReason = srdvErrorMessage ?? "Supplier rejected booking.";
                    await _dbContext.SaveChangesAsync();
                    return (false, payment.FailureReason);
                }

                // 3. Save Tracking Records (Local Persistence)
                reservation.ProviderBookingId = srdvProviderBookingId;
                reservation.SrdvBookingId = srdvProviderBookingId;
                reservation.ConfirmationNo = srdvConfirmationNo;
                reservation.InvoiceNumber = srdvInvoiceNumber;
                reservation.Status = "Confirmed";
                reservation.SrdvBookingResponseJson = srdvResponseJson;
                reservation.UpdatedAt = DateTime.UtcNow;
                
                _dbContext.HotelReservations.Add(reservation);
                await _dbContext.SaveChangesAsync();
                
                payment.FulfillmentStatus = "Success";
                payment.BookingReferenceId = reservation.Id;

                await ProcessCouponConsumptionAsync(payment.CouponCode, payment.UserId, reservation.Id, payment.FinalPayableAmount, payment.DiscountAmount, "Hotel");

                await _dbContext.SaveChangesAsync();

                try
                {
                    var exec = await _dbContext.SupplierFulfillmentExecutions.FirstOrDefaultAsync(e => e.PaymentId == payment.Id);
                    if (exec != null)
                    {
                        exec.ReservationId = reservation.Id;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update ReservationId on SupplierFulfillmentExecution for Payment {PaymentId}", payment.Id);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hotel Booking failed for Payment {Id}", payment.Id);
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string? ErrorMessage)> ProcessFlightBookingAsync(Payment payment, PendingPaymentBooking pending, SupplierFulfillmentExecution? existingExecution = null)
        {
            var srdvFlightService = _serviceProvider.GetRequiredService<ISrdvFlightService>();
            
            using var doc = JsonDocument.Parse(pending.BookingPayloadJson);
            var root = doc.RootElement;
            bool isGds = root.TryGetProperty("PNR", out var pnrNode) && !string.IsNullOrEmpty(pnrNode.GetString());

            try
            {
                if (existingExecution != null)
                {
                    bool isLccInner = !isGds;
                    var passengers = isGds 
                        ? JsonSerializer.Deserialize<FlightTicketGDSProxyRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Passengers 
                        : JsonSerializer.Deserialize<FlightTicketLCCProxyRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Passengers;
                    return await HandleFlightResponseAsync(payment, existingExecution.SupplierResponseJson ?? "{}", "", "", isLccInner, passengers, existingExecution);
                }

                if (isGds)
                {
                    var request = JsonSerializer.Deserialize<TicketGDSRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (request == null) return (false, "Invalid GDS flight booking payload.");
                    request.EndUserIp = "127.0.0.1";

                    var responseRaw = await srdvFlightService.TicketGDSRawAsync(request);
                    var flightProxy = JsonSerializer.Deserialize<FlightTicketGDSProxyRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return await HandleFlightResponseAsync(payment, responseRaw, request.TraceId, request.ResultIndex, isLcc: false, flightProxy?.Passengers, null);
                }
                else
                {
                    var request = JsonSerializer.Deserialize<TicketLCCRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (request == null) return (false, "Invalid LCC flight booking payload.");
                    request.EndUserIp = "127.0.0.1";

                    var responseRaw = await srdvFlightService.TicketLCCRawAsync(request);
                    var flightProxy = JsonSerializer.Deserialize<FlightTicketLCCProxyRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return await HandleFlightResponseAsync(payment, responseRaw, request.TraceId, request.ResultIndex, isLcc: true, flightProxy?.Passengers, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flight Booking failed for Payment {Id}", payment.Id);
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string? ErrorMessage)> HandleFlightResponseAsync(Payment payment, string responseRaw, string traceId, string resultIndex, bool isLcc, List<LCCPassengerDto>? requestPassengers, SupplierFulfillmentExecution? existingExecution)
        {
            using var doc = JsonDocument.Parse(responseRaw);
            var root = doc.RootElement;
            
            bool isSuccess = false;
            JsonElement resp = root;
            if (root.TryGetProperty("Response", out var responseNode)) resp = responseNode;
            else if (root.TryGetProperty("Results", out var resultsNode)) resp = resultsNode;

            if (resp.TryGetProperty("ResponseStatus", out var status))
            {
                if (status.ValueKind == JsonValueKind.Number && status.GetInt32() == 1) isSuccess = true;
                if (status.ValueKind == JsonValueKind.String && status.ToString() == "1") isSuccess = true;
            }
            
            var errSource = root.TryGetProperty("Error", out var rootErr) ? root : resp;
            if (errSource.TryGetProperty("Error", out var err) && err.TryGetProperty("ErrorCode", out var errCode))
            {
                if (errCode.ValueKind == JsonValueKind.Number && errCode.GetInt32() == 0) isSuccess = true;
                if (errCode.ValueKind == JsonValueKind.String && (errCode.ToString() == "0" || errCode.ToString() == "")) isSuccess = true;
                if (errCode.ValueKind == JsonValueKind.Null) isSuccess = true;
            }

            string pnr = resp.TryGetProperty("PNR", out var pnrProp) ? (pnrProp.ToString() ?? "") : "";
            string bookingId = resp.TryGetProperty("BookingId", out var bIdProp) ? (bIdProp.ToString() ?? "") : "";

            bool isPriceChanged = resp.TryGetProperty("IsPriceChanged", out var ipc) && ipc.ValueKind == JsonValueKind.True;

            if (existingExecution == null)
            {
                // STEP B: Log execution strictly before saving reservation details
                var execution = new PickNBook.Api.Models.Entities.SupplierFulfillmentExecution
                {
                    PaymentId = payment.Id,
                    BookingType = isLcc ? "Flight_LCC" : "Flight_GDS",
                    SupplierReference = pnr ?? bookingId,
                    SupplierBookingStatus = isSuccess && !isPriceChanged ? "Success" : "Failed",
                    SupplierResponseJson = responseRaw,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                
                using (var executionScope = _serviceProvider.CreateScope())
                {
                    var executionContext = executionScope.ServiceProvider.GetRequiredService<PickNBook.Api.Data.AppDbContext>();
                    executionContext.SupplierFulfillmentExecutions.Add(execution);
                    await executionContext.SaveChangesAsync();
                }
            }

            if (isPriceChanged)
            {
                payment.FulfillmentStatus = "Failed_PriceChanged";
                payment.FailureReason = "Supplier Price Increased during Ticketing.";

                if (!isLcc) 
                {
                    var failedRes = await _dbContext.FlightReservations.FirstOrDefaultAsync(r => r.Pnr == pnr || r.SrdvBookingId == bookingId);
                    if (failedRes != null) failedRes.Status = "Failed";
                }

                await _dbContext.SaveChangesAsync();
                return (false, "Supplier Price Increased during Ticketing.");
            }

            if (!isSuccess)
            {
                payment.FulfillmentStatus = "Failed_SupplierError";
                payment.FailureReason = "Ticketing failed at supplier.";
                if (errSource.TryGetProperty("Error", out var err2) && err2.TryGetProperty("ErrorMessage", out var errMsg) && errMsg.ValueKind == JsonValueKind.String)
                    payment.FailureReason = errMsg.GetString();
                    
                if (!isLcc) 
                {
                    var failedRes = await _dbContext.FlightReservations.FirstOrDefaultAsync(r => r.Pnr == pnr || r.SrdvBookingId == bookingId);
                    if (failedRes != null) failedRes.Status = "Failed";
                }

                await _dbContext.SaveChangesAsync();
                return (false, payment.FailureReason);
            }

            FlightReservation? reservation = null;

            if (!isLcc)
            {
                // GDS: Reservation was created during HoldGDS
                reservation = await _dbContext.FlightReservations.FirstOrDefaultAsync(r => r.Pnr == pnr || r.SrdvBookingId == bookingId);
                if (reservation != null)
                {
                    reservation.Status = "Booked";
                    reservation.SrdvTicketResponseJson = responseRaw;
                    reservation.TicketStatus = resp.TryGetProperty("TicketStatus", out var ts) ? ts.ToString() : reservation.TicketStatus;
                }
            }
            else
            {
                // LCC: Create Reservation
                reservation = new FlightReservation
                {
                    BookingReference = $"FL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}",
                    Pnr = pnr,
                    UserId = payment.UserId,
                    Status = "Booked",
                    BookedAtUtc = DateTime.UtcNow,
                    TraceId = traceId,
                    ResultIndex = resultIndex,
                    TotalPriceInr = payment.FinalPayableAmount,
                    CustomerFareInr = payment.FinalPayableAmount,
                    NetFareInr = payment.OriginalAmount,
                    MarkupAmount = payment.MarkupAmount,
                    CouponDiscount = payment.DiscountAmount,
                    SrdvTicketResponseJson = responseRaw,
                    PassengerName = requestPassengers?.FirstOrDefault()?.FirstName ?? "",
                    PassengerEmail = requestPassengers?.FirstOrDefault()?.Email ?? "",
                    PassengerPhone = requestPassengers?.FirstOrDefault()?.ContactNo ?? "",
                    Adults = requestPassengers?.Count(p => p.PaxType == 1) ?? 1,
                    Children = requestPassengers?.Count(p => p.PaxType == 2) ?? 0,
                    Infants = requestPassengers?.Count(p => p.PaxType == 3) ?? 0,
                    SeatsBooked = requestPassengers?.Count(p => p.PaxType == 1 || p.PaxType == 2) ?? 1,
                    SrdvBookingId = bookingId,
                    IsLcc = true
                };
                
                _dbContext.FlightReservations.Add(reservation);
            }

            if (reservation != null)
            {
                payment.FulfillmentStatus = "Success";
                payment.BookingReferenceId = reservation.Id;

                await ProcessCouponConsumptionAsync(payment.CouponCode, payment.UserId, reservation.Id, payment.FinalPayableAmount, payment.DiscountAmount, "Flight");
                await _dbContext.SaveChangesAsync();
            }

            return (true, null);
        }
    }
}
