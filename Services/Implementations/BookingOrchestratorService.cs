using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly PickNBook.Api.Services.Notifications.Interfaces.INotificationService _notificationService;
        private readonly IInAppNotificationService? _inAppNotificationService;
        private readonly IMemoryCache _cache;

        public BookingOrchestratorService(
            AppDbContext dbContext,
            ILogger<BookingOrchestratorService> logger,
            IServiceProvider serviceProvider,
            PickNBook.Api.Services.Notifications.Interfaces.INotificationService notificationService,
            IMemoryCache cache)
            : this(dbContext, logger, serviceProvider, notificationService, null, cache)
        {
        }

        public BookingOrchestratorService(
            AppDbContext dbContext,
            ILogger<BookingOrchestratorService> logger,
            IServiceProvider serviceProvider,
            PickNBook.Api.Services.Notifications.Interfaces.INotificationService notificationService,
            IInAppNotificationService? inAppNotificationService,
            IMemoryCache cache)
        {
            _dbContext = dbContext;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _notificationService = notificationService;
            _inAppNotificationService = inAppNotificationService;
            _cache = cache;
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

        public async Task RetryRefundAsync(int paymentId)
        {
            var payment = await _dbContext.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
            if (payment == null) return;
            await TriggerRefundAsync(payment, payment.RefundReason ?? "Retry failed refund");
        }

        private async Task TriggerRefundAsync(Payment payment, string reason)
        {
            try
            {
                if (payment.Status == "Success" || payment.Status == PickNBook.Api.Models.Payments.PaymentStatus.Success || payment.Status == "PAID" || (payment.FulfillmentStatus != null && payment.FulfillmentStatus.StartsWith("Failed")))
                {
                    bool isHybrid = string.Equals(payment.PaymentMethod, "Hybrid", StringComparison.OrdinalIgnoreCase);
                    bool isWallet = string.Equals(payment.PaymentMethod, "Wallet", StringComparison.OrdinalIgnoreCase) ||
                                    (payment.WalletUsedAmount > 0 && payment.GatewayPaidAmount == 0 && !string.Equals(payment.PaymentMethod, "Cashfree", StringComparison.OrdinalIgnoreCase));

                    bool walletRefundRequired = payment.WalletUsedAmount > 0 || isWallet;
                    bool gatewayRefundRequired = payment.GatewayPaidAmount > 0 || (!isWallet && payment.FinalPayableAmount > 0);

                    // 1. Component: Customer Wallet Refund
                    if (walletRefundRequired && payment.WalletReservationStatus != "Refunded")
                    {
                        if (int.TryParse(payment.UserId, out var customerUserId) && customerUserId > 0)
                        {
                            try
                            {
                                var walletService = _serviceProvider.GetRequiredService<PickNBook.Api.Services.Interfaces.IWalletService>();
                                string refundRef = isHybrid ? $"REF-{payment.PaymentReference}-W" : $"REF-{payment.PaymentReference}";
                                if (refundRef.Length > 40) refundRef = refundRef.Substring(0, 40);

                                decimal walletRefundAmount = payment.WalletUsedAmount > 0 ? payment.WalletUsedAmount : payment.TotalAmount;

                                var refundTx = await walletService.RefundAsync(
                                    userId: customerUserId,
                                    amount: walletRefundAmount,
                                    referenceType: $"{payment.BookingType}Refund",
                                    refCode: refundRef,
                                    description: $"Auto-refund for failed {payment.BookingType} booking: {reason}");

                                payment.WalletReservationStatus = "Refunded";
                                if (isWallet)
                                {
                                    payment.RefundId = refundRef;
                                    payment.RefundReason = reason;
                                    payment.RefundStatus = "Refunded";
                                }
                                _logger.LogInformation("Successfully auto-refunded {Amount} to customer wallet for failed booking Payment {PaymentId}, TxId {TxId}",
                                    walletRefundAmount, payment.Id, refundTx.Id);
                            }
                            catch (Exception wEx)
                            {
                                _logger.LogError(wEx, "Failed to auto-refund customer wallet for Payment {PaymentId}", payment.Id);
                                payment.LastError = $"Wallet refund failed: {wEx.Message}";
                                payment.RefundAttempts += 1;
                            }
                        }
                    }

                    // 2. Component: Cashfree Gateway Refund
                    if (gatewayRefundRequired && payment.RefundStatus != "Refunded" && payment.RefundStatus != "RefundProcessing")
                    {
                        try
                        {
                            var cashfreeService = _serviceProvider.GetRequiredService<PickNBook.Api.Services.Interfaces.ICashfreeService>();
                            string refundId = $"REF-{payment.CashfreeOrderId}"; // Deterministic!
                            decimal gatewayRefundAmount = payment.GatewayPaidAmount > 0 ? payment.GatewayPaidAmount : payment.FinalPayableAmount;

                            var refundResponse = await cashfreeService.InitiateRefundAsync(payment.CashfreeOrderId, gatewayRefundAmount, refundId, reason);

                            string cashfreeRefundStatus = "PENDING";
                            string? statusDescription = null;
                            if (refundResponse.RootElement.TryGetProperty("refund_status", out var statusEl))
                            {
                                cashfreeRefundStatus = statusEl.GetString() ?? "PENDING";
                            }
                            if (refundResponse.RootElement.TryGetProperty("status_description", out var descEl))
                            {
                                statusDescription = descEl.GetString();
                            }

                            payment.RefundId = refundId;
                            payment.RefundReason = reason;

                            if (cashfreeRefundStatus.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                            {
                                payment.RefundStatus = "Refunded";
                                payment.LastError = null;
                            }
                            else if (cashfreeRefundStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) || cashfreeRefundStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                            {
                                payment.RefundStatus = "RefundFailed";
                                payment.LastError = statusDescription ?? "Cashfree returned CANCELLED/FAILED for refund.";
                                payment.RefundAttempts += 1;
                            }
                            else if (cashfreeRefundStatus.Equals("ONHOLD", StringComparison.OrdinalIgnoreCase))
                            {
                                payment.RefundStatus = "RefundOnHold";
                                payment.LastError = statusDescription ?? "Refund on hold because of insufficient account balance";
                                _logger.LogCritical("CRITICAL: Cashfree refund {RefundId} for Payment {PaymentId} is ONHOLD due to insufficient merchant balance! Note: {StatusDesc}", 
                                    refundId, payment.Id, payment.LastError);
                            }
                            else
                            {
                                // PENDING or any other status — refund is in progress
                                payment.RefundStatus = "RefundProcessing";
                                payment.LastError = statusDescription;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to initiate refund for Payment {PaymentId}, Order {OrderId}", payment.Id, payment.CashfreeOrderId);
                            payment.RefundStatus = "RefundFailed";
                            payment.RefundReason = reason;
                            payment.RefundId = $"REF-{payment.CashfreeOrderId}";
                            payment.LastError = ex.Message;
                            payment.RefundAttempts += 1;
                        }
                    }

                    // 3. Evaluate terminal refund status
                    bool walletCompleted = !walletRefundRequired || payment.WalletReservationStatus == "Refunded";
                    bool gatewayCompleted = !gatewayRefundRequired || payment.RefundStatus == "Refunded";

                    if (walletCompleted && gatewayCompleted)
                    {
                        payment.Status = "REFUNDED";
                    }

                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger auto-refund for Payment {PaymentId}", payment.Id);
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
                // Resolve authoritative blocked-seat pricing
                var blockedSeats = await _dbContext.BusBlockedSeatPrices
                    .Where(x => x.TraceId == request.TraceId)
                    .ToListAsync();

                var authoritativeBlockedSeats = (request.Passengers ?? new List<CreateBusPassengerDto>())
                    .Where(p => !string.IsNullOrWhiteSpace(p.SeatNumber))
                    .Select(p =>
                    {
                        var seatCode = p.SeatNumber!.Trim();

                        var blockedSeat = blockedSeats
                            .Where(b =>
                                !string.IsNullOrWhiteSpace(b.SeatName) &&
                                b.SeatName.Equals(seatCode, StringComparison.OrdinalIgnoreCase) &&
                                b.BaseFare > 0)
                            .OrderByDescending(b => b.Id)
                            .FirstOrDefault();

                        return new
                        {
                            Passenger = p,
                            SeatCode = seatCode,
                            BlockedSeat = blockedSeat
                        };
                    })
                    .ToList();

                var missingSeats = authoritativeBlockedSeats
                    .Where(x => x.BlockedSeat == null)
                    .Select(x => x.SeatCode)
                    .ToList();

                if (missingSeats.Any())
                {
                    return (false,
                        $"Authoritative blocked seat pricing is unavailable for seat(s): {string.Join(", ", missingSeats)}. Please refresh and block the seats again.");
                }

                // Resolve authoritative SeatType from SRDV layout cache
                Dictionary<string, BusSeatLayoutItemContext>? layoutMap = null;

                if (!string.IsNullOrEmpty(request.TraceId) &&
                    !string.IsNullOrEmpty(request.ResultIndex))
                {
                    _cache.TryGetValue(
                        $"bus_seats_{request.TraceId}_{request.ResultIndex}",
                        out layoutMap);
                }

                var missingLayoutSeats = authoritativeBlockedSeats
                    .Where(x => layoutMap == null ||
                                !layoutMap.TryGetValue(x.SeatCode, out var layoutSeat) ||
                                string.IsNullOrWhiteSpace(layoutSeat.SeatType))
                    .Select(x => x.SeatCode)
                    .ToList();

                if (missingLayoutSeats.Any())
                {
                    return (false,
                        $"Authoritative seat layout information is unavailable for seat(s): {string.Join(", ", missingLayoutSeats)}. Please refresh the seat layout and block again.");
                }

                var seatsRequired = authoritativeBlockedSeats.Count;

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

                var contactName = string.IsNullOrWhiteSpace(request.PassengerName) 
                    ? (authoritativeBlockedSeats.FirstOrDefault()?.Passenger.FullName ?? "Passenger") 
                    : request.PassengerName.Trim();

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
                    TotalPriceInr = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount,
                    CustomerFareInr = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount,
                    PaymentMethod = payment.PaymentMethod ?? "Cashfree",
                    WalletPaidAmount = payment.WalletUsedAmount,
                    GatewayPaidAmount = payment.GatewayPaidAmount > 0 || payment.WalletUsedAmount > 0 ? payment.GatewayPaidAmount : payment.FinalPayableAmount,
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
                foreach (var item in authoritativeBlockedSeats)
                {
                    var p = item.Passenger;
                    var layoutSeat = layoutMap![item.SeatCode];

                    dbPassengers.Add(new BusReservationPassenger
                    {
                        BusReservationId = reservation.Id,
                        FullName = p.FullName,
                        Gender = p.Gender,
                        SeatNumber = item.SeatCode,
                        BaseFareInr = item.BlockedSeat!.BaseFare,
                        SeatType = layoutSeat.SeatType,
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

                    // Persist BusBooking & BusReservation as Failed before refunding
                    try
                    {
                        _dbContext.BusBookings.Add(bus);
                        await _dbContext.SaveChangesAsync();

                        reservation.BusBookingId = bus.Id;
                        reservation.Status = "Failed";
                        reservation.CancellationReason = payment.FailureReason;
                        reservation.BookedAtUtc = DateTime.UtcNow;
                        _dbContext.BusReservations.Add(reservation);
                        await _dbContext.SaveChangesAsync();

                        payment.BookingReferenceId = reservation.Id;
                    }
                    catch (Exception pEx)
                    {
                        _logger.LogError(pEx, "Failed to persist Failed BusReservation for Payment {PaymentId}", payment.Id);
                    }
                    
                    // Determine primary customer-facing reference
                    string bookingRef = !string.IsNullOrWhiteSpace(reservation.BookingReference)
                        ? reservation.BookingReference
                        : (!string.IsNullOrWhiteSpace(payment.PaymentReference) ? payment.PaymentReference : reservation.Pnr);

                    string cleanReason = string.IsNullOrWhiteSpace(payment.FailureReason)
                        ? "Supplier booking error"
                        : payment.FailureReason.Trim();

                    if (cleanReason.Length > 45)
                    {
                        cleanReason = cleanReason.Substring(0, 42) + "...";
                    }

                    // 1. Enqueue SMS notification if customer mobile is available
                    if (!string.IsNullOrWhiteSpace(reservation.PassengerPhone))
                    {
                        await _notificationService.EnqueueAsync(
                            eventType: "BusBookingFailed",
                            channel: "SMS",
                            recipient: reservation.PassengerPhone.Trim(),
                            templateKey: "BUS_BOOKING_FAILED",
                            payload: new
                            {
                                Reference = bookingRef,
                                Reason = cleanReason,
                                Var1 = bookingRef,
                                Var2 = cleanReason,
                                Amount = payment.FinalPayableAmount
                            },
                            bookingId: bookingRef,
                            userId: payment.UserId
                        );
                    }
                    else
                    {
                        _logger.LogWarning("Cannot enqueue BusBookingFailed SMS for Payment {PaymentId}: No phone number available.", payment.Id);
                    }

                    // 2. Enqueue Email notification if customer email is available
                    var emailRecipient = !string.IsNullOrWhiteSpace(reservation.PassengerEmail) ? reservation.PassengerEmail.Trim() : (payment.UserId.Contains('@') ? payment.UserId : null);
                    if (!string.IsNullOrWhiteSpace(emailRecipient))
                    {
                        await _notificationService.EnqueueAsync(
                            eventType: "BusBookingFailed",
                            channel: "Email",
                            recipient: emailRecipient,
                            templateKey: "BUS_BOOKING_FAILED",
                            payload: new
                            {
                                Reason = cleanReason,
                                Amount = payment.FinalPayableAmount,
                                Reference = bookingRef,
                                Var1 = bookingRef,
                                Var2 = cleanReason
                            },
                            bookingId: bookingRef,
                            userId: payment.UserId
                        );
                    }
                    else
                    {
                        _logger.LogWarning("Cannot enqueue BusBookingFailed Email for Payment {PaymentId}: No valid email recipient available.", payment.Id);
                    }

                    await _dbContext.SaveChangesAsync();

                    // Additive In-App Notifications (Step 4: Bus Failed)
                    if (_inAppNotificationService != null)
                    {
                        try
                        {
                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "Booking",
                                category: "Customer",
                                title: "Bus Booking Failed",
                                message: $"Your bus booking ({bookingRef}) could not be confirmed: {cleanReason}. Refund has been initiated.",
                                severity: "Error",
                                referenceType: "BusBooking",
                                referenceId: bookingRef,
                                actionUrl: $"/bookings/{bookingRef}",
                                idempotencyKey: $"BOOKING_FAILED_BUS_{payment.PaymentReference}",
                                targetUserId: payment.UserId
                            );

                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "Supplier",
                                category: "Admin",
                                title: "Bus Supplier Booking Failed",
                                message: $"Bus booking for payment {payment.PaymentReference} ({bookingRef}) failed: {cleanReason}.",
                                severity: "Error",
                                referenceType: "Payment",
                                referenceId: payment.Id.ToString(),
                                actionUrl: $"/admin/payments/{payment.Id}",
                                idempotencyKey: $"ADMIN_BOOKING_FAILED_BUS_{payment.PaymentReference}",
                                targetRole: "Admin"
                            );
                        }
                        catch (Exception inAppEx)
                        {
                            _logger.LogWarning(inAppEx, "Failed to create in-app notification for Bus Booking Failed {PaymentReference}", payment.PaymentReference);
                        }
                    }

                    return (false, payment.FailureReason);
                }

                // 3. Save Tracking Records (Local Persistence)
                reservation.SrdvBookingId = srdvTravelOperatorPnr;
                reservation.SrdvTicketNo = srdvTicketNo;
                reservation.Status = "Booked";
                reservation.SrdvBookingResponseJson = srdvResponseJson;
                
                if (!string.IsNullOrEmpty(srdvResponseJson))
                {
                    try
                    {
                        using var sDoc = JsonDocument.Parse(srdvResponseJson);
                        var sRoot = sDoc.RootElement;
                        if (sRoot.TryGetProperty("CancellationPolicies", out var cPolicies))
                        {
                            reservation.CancellationPolicyJson = cPolicies.ToString();
                        }
                    }
                    catch { /* Ignore parsing errors */ }
                }

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

                await _notificationService.EnqueueAsync(
                    eventType: "BusBookingSuccess",
                    channel: "Email",
                    recipient: reservation.PassengerEmail ?? payment.UserId,
                    templateKey: "BUS_BOOKING_CONFIRMED",
                    payload: new { Pnr = reservation.Pnr, Name = reservation.PassengerName, Amount = payment.FinalPayableAmount }
                );

                var boardingTime = reservation.BoardingPointTime ?? bus.DepartureTime;
                string formattedTime = boardingTime.ToString("dd/MM/yyyy hh:mm tt");
                string boardingPoint = !string.IsNullOrWhiteSpace(reservation.BoardingPointName)
                    ? reservation.BoardingPointName
                    : (!string.IsNullOrWhiteSpace(bus.BoardingPoint) ? bus.BoardingPoint : "Bus Station");

                await _notificationService.EnqueueAsync(
                    eventType: "BusBookingSuccess",
                    channel: "SMS",
                    recipient: (reservation.PassengerPhone ?? "").Trim(),
                    templateKey: "BUS_BOOKING_CONFIRMED",
                    payload: new
                    {
                        Reference = reservation.BookingReference,
                        Pnr = reservation.Pnr,
                        Boarding = boardingPoint,
                        Time = formattedTime,
                        Var1 = reservation.BookingReference,
                        Var2 = reservation.Pnr,
                        Var3 = boardingPoint,
                        Var4 = formattedTime
                    },
                    bookingId: reservation.BookingReference,
                    userId: payment.UserId
                );

                // Commit payment and coupon changes
                await _dbContext.SaveChangesAsync();

                // Additive In-App Notifications (Step 4: Bus Confirmed)
                if (_inAppNotificationService != null)
                {
                    try
                    {
                        await _inAppNotificationService.CreateNotificationAsync(
                            type: "Booking",
                            category: "Customer",
                            title: "Bus Booking Confirmed",
                            message: $"Your bus booking ({reservation.BookingReference}) has been confirmed. PNR: {reservation.Pnr}.",
                            severity: "Success",
                            referenceType: "BusBooking",
                            referenceId: reservation.BookingReference,
                            actionUrl: $"/bookings/{reservation.BookingReference}",
                            idempotencyKey: $"BOOKING_SUCCESS_BUS_{payment.PaymentReference}",
                            targetUserId: payment.UserId
                        );
                    }
                    catch (Exception inAppEx)
                    {
                        _logger.LogWarning(inAppEx, "Failed to create in-app notification for Bus Booking Confirmed {PaymentReference}", payment.PaymentReference);
                    }
                }
                
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
                int rows = await _dbContext.BusCoupons
                    .Where(x => x.CouponCode == normalizedCoupon && (x.UseLimit == 0 || x.UsedCount < x.UseLimit))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount + 1));
                    
                if (rows > 0)
                {
                    var coupon = await _dbContext.BusCoupons.FirstOrDefaultAsync(x => x.CouponCode == normalizedCoupon);
                    if (coupon != null)
                    {
                        var manualUsage = new BusCouponUsage
                        {
                            BusCouponId = coupon.Id,
                            BusReservationId = reservationId,
                            UserId = userId,
                            CouponCode = coupon.CouponCode,
                            CouponType = coupon.CouponType,
                            CouponValue = coupon.Value,
                            CouponAmountInr = discountAmount,
                            TotalFareInr = bookingTotal,
                            BookingStatus = "Booked",
                            UsedAtUtc = DateTime.UtcNow
                        };
                        _dbContext.BusCouponUsages.Add(manualUsage);
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
                    TraceId = request.TraceId.ToString(),
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
                    TotalPrice = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount,
                    PaymentMethod = payment.PaymentMethod ?? "Cashfree",
                    WalletPaidAmount = payment.WalletUsedAmount,
                    GatewayPaidAmount = payment.GatewayPaidAmount > 0 || payment.WalletUsedAmount > 0 ? payment.GatewayPaidAmount : payment.FinalPayableAmount,
                    
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
                bool isPending = false;
                string? srdvErrorMessage = null;
                string? srdvProviderBookingId = null;
                string? srdvConfirmationNo = null;
                string? srdvInvoiceNumber = null;
                string? srdvResponseJson = null;

                if (existingExecution != null)
                {
                    isSrdvSuccess = existingExecution.SupplierBookingStatus == "Success" || existingExecution.SupplierBookingStatus == "Pending";
                    isPending = existingExecution.SupplierBookingStatus == "Pending";
                    srdvErrorMessage = existingExecution.LastError;
                    srdvProviderBookingId = existingExecution.SupplierReference;
                    srdvResponseJson = existingExecution.SupplierResponseJson;
                }
                else
                {
                    var srdvRes = await hotelService.BookRoomAsync(request);
                    var result = srdvRes.BookResult;
                    
                    bool isConfirmed = result != null && (result.ResponseStatus == 1 || result.Status?.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) == true);
                    isPending = result != null && (result.ResponseStatus == 3 || result.Status?.Equals("Pending", StringComparison.OrdinalIgnoreCase) == true);
                    isSrdvSuccess = isConfirmed || isPending;
                    srdvErrorMessage = result?.Error?.ErrorMessage ?? "Unknown Error";
                    srdvProviderBookingId = result?.BookingId > 0 ? result.BookingId.ToString() : (!string.IsNullOrEmpty(result?.BookingRefNo) ? result.BookingRefNo : null);
                    srdvConfirmationNo = result?.ConfirmationNo;
                    srdvInvoiceNumber = result?.InvoiceNumber;
                    srdvResponseJson = JsonSerializer.Serialize(srdvRes);

                    var execution = new PickNBook.Api.Models.Entities.SupplierFulfillmentExecution
                    {
                        PaymentId = payment.Id,
                        BookingType = "Hotel",
                        SupplierReference = srdvProviderBookingId ?? "",
                        SupplierBookingStatus = isConfirmed ? "Success" : (isPending ? "Pending" : "Failed"),
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

                    // Persist HotelReservation as Failed before refunding
                    try
                    {
                        reservation.Status = "Failed";
                        reservation.CancellationReason = payment.FailureReason;
                        reservation.UpdatedAt = DateTime.UtcNow;
                        _dbContext.HotelReservations.Add(reservation);
                        await _dbContext.SaveChangesAsync();

                        payment.BookingReferenceId = reservation.Id;
                    }
                    catch (Exception pEx)
                    {
                        _logger.LogError(pEx, "Failed to persist Failed HotelReservation for Payment {PaymentId}", payment.Id);
                    }

                    string cleanReason = !string.IsNullOrWhiteSpace(payment.FailureReason) 
                        ? payment.FailureReason 
                        : "Room unavailable";
                    if (cleanReason.Length > 45)
                    {
                        cleanReason = cleanReason.Substring(0, 42) + "...";
                    }

                    if (!string.IsNullOrWhiteSpace(reservation.GuestPhone))
                    {
                        await _notificationService.EnqueueAsync(
                            eventType: "HotelBookingFailed",
                            channel: "SMS",
                            recipient: reservation.GuestPhone.Trim(),
                            templateKey: "HOTEL_BOOKING_FAILED",
                            payload: new
                            {
                                Reference = reservation.BookingReference,
                                Reason = cleanReason,
                                Var1 = reservation.BookingReference,
                                Var2 = cleanReason,
                                Amount = payment.FinalPayableAmount
                            },
                            bookingId: reservation.BookingReference,
                            userId: payment.UserId
                        );
                    }

                    await _notificationService.EnqueueAsync(
                        eventType: "HotelBookingFailed",
                        channel: "Email",
                        recipient: reservation.GuestEmail ?? payment.UserId,
                        templateKey: "HOTEL_BOOKING_FAILED",
                        payload: new { Reason = cleanReason, Amount = payment.FinalPayableAmount, Reference = reservation.BookingReference }
                    );

                    await _dbContext.SaveChangesAsync();

                    // Additive In-App Notifications (Step 4: Hotel Failed)
                    if (_inAppNotificationService != null)
                    {
                        try
                        {
                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "Booking",
                                category: "Customer",
                                title: "Hotel Booking Failed",
                                message: $"Your hotel booking ({reservation.BookingReference}) could not be confirmed: {cleanReason}. Refund has been initiated.",
                                severity: "Error",
                                referenceType: "HotelBooking",
                                referenceId: reservation.BookingReference,
                                actionUrl: $"/bookings/{reservation.BookingReference}",
                                idempotencyKey: $"BOOKING_FAILED_HOTEL_{payment.PaymentReference}",
                                targetUserId: payment.UserId
                            );

                            await _inAppNotificationService.CreateNotificationAsync(
                                type: "Supplier",
                                category: "Admin",
                                title: "Hotel Supplier Booking Failed",
                                message: $"Hotel booking for payment {payment.PaymentReference} ({reservation.BookingReference}) failed: {cleanReason}.",
                                severity: "Error",
                                referenceType: "Payment",
                                referenceId: payment.Id.ToString(),
                                actionUrl: $"/admin/payments/{payment.Id}",
                                idempotencyKey: $"ADMIN_BOOKING_FAILED_HOTEL_{payment.PaymentReference}",
                                targetRole: "Admin"
                            );
                        }
                        catch (Exception inAppEx)
                        {
                            _logger.LogWarning(inAppEx, "Failed to create in-app notification for Hotel Booking Failed {PaymentReference}", payment.PaymentReference);
                        }
                    }

                    return (false, payment.FailureReason);
                }

                // 3. Save Tracking Records (Local Persistence)
                reservation.ProviderBookingId = srdvProviderBookingId;
                reservation.SrdvBookingId = srdvProviderBookingId;
                reservation.ConfirmationNo = srdvConfirmationNo;
                reservation.InvoiceNumber = srdvInvoiceNumber;
                reservation.Status = isPending ? "Pending" : "Confirmed";
                reservation.SrdvBookingResponseJson = srdvResponseJson;
                reservation.UpdatedAt = DateTime.UtcNow;
                
                _dbContext.HotelReservations.Add(reservation);
                await _dbContext.SaveChangesAsync();
                
                payment.FulfillmentStatus = isPending ? "Pending_Reconciliation" : "Success";
                payment.BookingReferenceId = reservation.Id;

                await ProcessCouponConsumptionAsync(payment.CouponCode, payment.UserId, reservation.Id, payment.FinalPayableAmount, payment.DiscountAmount, "Hotel");

                if (!isPending)
                {
                    await _notificationService.EnqueueAsync(
                        eventType: "HotelBookingSuccess",
                        channel: "Email",
                        recipient: reservation.GuestEmail ?? payment.UserId,
                        templateKey: "HOTEL_BOOKING_CONFIRMED",
                        payload: new { HotelName = reservation.HotelName, Name = reservation.GuestName, Amount = payment.FinalPayableAmount }
                    );

                    string checkInFormatted = reservation.CheckInDate.ToString("dd/MM/yyyy");
                    string checkOutFormatted = reservation.CheckOutDate.ToString("dd/MM/yyyy");

                    await _notificationService.EnqueueAsync(
                        eventType: "HotelBookingSuccess",
                        channel: "SMS",
                        recipient: (reservation.GuestPhone ?? "").Trim(),
                        templateKey: "HOTEL_BOOKING_CONFIRMED",
                        payload: new
                        {
                            Reference = reservation.BookingReference,
                            Hotel = reservation.HotelName,
                            HotelName = reservation.HotelName,
                            CheckIn = checkInFormatted,
                            CheckOut = checkOutFormatted,
                            Var1 = reservation.BookingReference,
                            Var2 = reservation.HotelName,
                            Var3 = checkInFormatted,
                            Var4 = checkOutFormatted
                        },
                        bookingId: reservation.BookingReference,
                        userId: payment.UserId
                    );
                }

                await _dbContext.SaveChangesAsync();

                // Additive In-App Notifications (Step 4: Hotel Confirmed)
                if (!isPending && _inAppNotificationService != null)
                {
                    try
                    {
                        await _inAppNotificationService.CreateNotificationAsync(
                            type: "Booking",
                            category: "Customer",
                            title: "Hotel Booking Confirmed",
                            message: $"Your hotel booking ({reservation.BookingReference}) at {reservation.HotelName} has been confirmed.",
                            severity: "Success",
                            referenceType: "HotelBooking",
                            referenceId: reservation.BookingReference,
                            actionUrl: $"/bookings/{reservation.BookingReference}",
                            idempotencyKey: $"BOOKING_SUCCESS_HOTEL_{payment.PaymentReference}",
                            targetUserId: payment.UserId
                        );
                    }
                    catch (Exception inAppEx)
                    {
                        _logger.LogWarning(inAppEx, "Failed to create in-app notification for Hotel Booking Confirmed {PaymentReference}", payment.PaymentReference);
                    }
                }

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
                _logger.LogError(ex, "Flight Booking failed for Payment {Id}. Attempting recovery via BookingDetails.", payment.Id);

                // Check if we can recover via BookingDetails using TraceId
                string? recoveryTraceId = null;
                string? recoveryResultIndex = null;
                List<LCCPassengerDto>? recoveryPassengers = null;

                try
                {
                    if (isGds)
                    {
                        var flightProxy = JsonSerializer.Deserialize<FlightTicketGDSProxyRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        recoveryTraceId = flightProxy?.TraceId.ToString();
                        recoveryResultIndex = flightProxy?.ResultIndex;
                        recoveryPassengers = flightProxy?.Passengers;
                    }
                    else
                    {
                        var flightProxy = JsonSerializer.Deserialize<FlightTicketLCCProxyRequestDto>(pending.BookingPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        recoveryTraceId = flightProxy?.TraceId.ToString();
                        recoveryResultIndex = flightProxy?.ResultIndex;
                        recoveryPassengers = flightProxy?.Passengers;
                    }

                    if (long.TryParse(recoveryTraceId, out var tid) && tid > 0)
                    {
                        var recoveryDetails = await srdvFlightService.GetBookingDetailsRawAsync(tid);
                        using var rDoc = JsonDocument.Parse(recoveryDetails);
                        var rRoot = rDoc.RootElement;
                        var bookingStatus = rRoot.TryGetProperty("BookingStatus", out var bsProp) ? bsProp.GetString() : null;

                        if (string.Equals(bookingStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("BookingDetails recovery SUCCEEDED for Payment {PaymentId}, TraceId {TraceId}. Proceeding with fulfillment.", payment.Id, tid);
                            return await HandleFlightResponseAsync(payment, recoveryDetails, recoveryTraceId ?? "", recoveryResultIndex ?? "", !isGds, recoveryPassengers, null);
                        }
                        else if (string.Equals(bookingStatus, "MANUAL_CHECK_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(bookingStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("BookingDetails indicates {Status} for Payment {PaymentId}, TraceId {TraceId}. Money is reserved at supplier.", bookingStatus, payment.Id, tid);
                            payment.FulfillmentStatus = bookingStatus.ToUpperInvariant();
                            payment.FailureReason = $"Supplier ticketing status: {bookingStatus}. Awaiting supplier resolution.";
                            await _dbContext.SaveChangesAsync();
                            return (false, $"Ticketing status is {bookingStatus} at supplier. Please do not re-book.");
                        }
                    }
                }
                catch (Exception recoveryEx)
                {
                    _logger.LogError(recoveryEx, "Recovery via BookingDetails failed for Payment {PaymentId}", payment.Id);
                }

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
            
            if (root.TryGetProperty("BookingStatus", out var bsNode))
            {
                var bs = bsNode.GetString();
                if (string.Equals(bs, "SUCCESS", StringComparison.OrdinalIgnoreCase)) isSuccess = true;
                else if (string.Equals(bs, "MANUAL_CHECK_REQUIRED", StringComparison.OrdinalIgnoreCase))
                {
                    payment.FulfillmentStatus = "MANUAL_CHECK_REQUIRED";
                    payment.FailureReason = "Supplier ticketing status: MANUAL_CHECK_REQUIRED. Funds reserved.";
                    await _dbContext.SaveChangesAsync();
                    return (false, "Ticketing is under supplier review (MANUAL_CHECK_REQUIRED). Please do not re-book.");
                }
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

            string returnPnr = "";
            if (root.TryGetProperty("Legs", out var legsNode) && legsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var leg in legsNode.EnumerateArray())
                {
                    var legType = leg.TryGetProperty("Direction", out var dirProp) ? dirProp.GetString() : (leg.TryGetProperty("LegType", out var ltProp) ? ltProp.GetString() : "");
                    var legPnr = leg.TryGetProperty("PNR", out var lpProp) ? lpProp.GetString() : "";
                    if (!string.IsNullOrEmpty(legPnr))
                    {
                        if (string.Equals(legType, "RETURN", StringComparison.OrdinalIgnoreCase))
                        {
                            returnPnr = legPnr;
                        }
                        else if (string.IsNullOrEmpty(pnr))
                        {
                            pnr = legPnr;
                        }
                    }
                }
            }

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
                    if (failedRes != null)
                    {
                        failedRes.Status = "Failed";
                        failedRes.CancellationReason = payment.FailureReason;
                        payment.BookingReferenceId = failedRes.Id;
                    }
                }
                else
                {
                    try
                    {
                        var failedLccRes = new FlightReservation
                        {
                            BookingReference = $"FL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 1000)}",
                            Pnr = pnr ?? "",
                            UserId = payment.UserId,
                            Status = "Failed",
                            CancellationReason = payment.FailureReason,
                            BookedAtUtc = DateTime.UtcNow,
                            TraceId = traceId,
                            ResultIndex = resultIndex,
                            TotalPriceInr = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount,
                            CustomerFareInr = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount,
                            PaymentMethod = payment.PaymentMethod ?? "Cashfree",
                            WalletPaidAmount = payment.WalletUsedAmount,
                            GatewayPaidAmount = payment.GatewayPaidAmount > 0 || payment.WalletUsedAmount > 0 ? payment.GatewayPaidAmount : payment.FinalPayableAmount,
                            NetFareInr = payment.OriginalAmount,
                            MarkupAmount = payment.MarkupAmount,
                            CouponDiscount = payment.DiscountAmount,
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
                        _dbContext.FlightReservations.Add(failedLccRes);
                        await _dbContext.SaveChangesAsync();
                        payment.BookingReferenceId = failedLccRes.Id;
                    }
                    catch (Exception pEx)
                    {
                        _logger.LogError(pEx, "Failed to persist Failed FlightReservation for Payment {PaymentId}", payment.Id);
                    }
                }

                await _notificationService.EnqueueAsync(
                    eventType: "FlightBookingFailed",
                    channel: "Email",
                    recipient: requestPassengers?.FirstOrDefault()?.Email ?? payment.UserId,
                    templateKey: "FLIGHT_BOOKING_FAILED",
                    payload: new { Reason = payment.FailureReason, Amount = payment.FinalPayableAmount }
                );

                var failedPhone = requestPassengers?.FirstOrDefault()?.ContactNo ?? "";
                if (!string.IsNullOrWhiteSpace(failedPhone))
                {
                    await _notificationService.EnqueueAsync(
                        eventType: "FlightBookingFailed",
                        channel: "SMS",
                        recipient: failedPhone,
                        templateKey: "FLIGHT_BOOKING_FAILED",
                        payload: new { Reference = !string.IsNullOrWhiteSpace(payment.PaymentReference) ? payment.PaymentReference : $"PNBF{DateTime.UtcNow:yyMMddHHmm}", Reason = payment.FailureReason ?? "Booking could not be completed" }
                    );
                }

                await _dbContext.SaveChangesAsync();

                // Additive In-App Notifications (Step 4: Flight Failed)
                if (_inAppNotificationService != null)
                {
                    try
                    {
                        await _inAppNotificationService.CreateNotificationAsync(
                            type: "Booking",
                            category: "Customer",
                            title: "Flight Booking Failed",
                            message: $"Your flight booking (Payment: {payment.PaymentReference}) could not be confirmed: {payment.FailureReason}. Refund has been initiated.",
                            severity: "Error",
                            referenceType: "FlightBooking",
                            referenceId: payment.PaymentReference,
                            actionUrl: $"/bookings/{payment.PaymentReference}",
                            idempotencyKey: $"BOOKING_FAILED_FLIGHT_{payment.PaymentReference}",
                            targetUserId: payment.UserId
                        );

                        await _inAppNotificationService.CreateNotificationAsync(
                            type: "Supplier",
                            category: "Admin",
                            title: "Flight Supplier Booking Failed",
                            message: $"Flight booking for payment {payment.PaymentReference} failed: {payment.FailureReason}.",
                            severity: "Error",
                            referenceType: "Payment",
                            referenceId: payment.Id.ToString(),
                            actionUrl: $"/admin/payments/{payment.Id}",
                            idempotencyKey: $"ADMIN_BOOKING_FAILED_FLIGHT_{payment.PaymentReference}",
                            targetRole: "Admin"
                        );
                    }
                    catch (Exception inAppEx)
                    {
                        _logger.LogWarning(inAppEx, "Failed to create in-app notification for Flight Booking Failed {PaymentReference}", payment.PaymentReference);
                    }
                }

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
                    if (!string.IsNullOrEmpty(returnPnr)) reservation.ReturnPnr = returnPnr;
                    reservation.TotalPriceInr = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount;
                    reservation.CustomerFareInr = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount;
                    reservation.PaymentMethod = payment.PaymentMethod ?? "Cashfree";
                    reservation.WalletPaidAmount = payment.WalletUsedAmount;
                    reservation.GatewayPaidAmount = payment.GatewayPaidAmount > 0 || payment.WalletUsedAmount > 0 ? payment.GatewayPaidAmount : payment.FinalPayableAmount;
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
                    TotalPriceInr = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount,
                    CustomerFareInr = payment.TotalAmount > 0 ? payment.TotalAmount : payment.FinalPayableAmount,
                    PaymentMethod = payment.PaymentMethod ?? "Cashfree",
                    WalletPaidAmount = payment.WalletUsedAmount,
                    GatewayPaidAmount = payment.GatewayPaidAmount > 0 || payment.WalletUsedAmount > 0 ? payment.GatewayPaidAmount : payment.FinalPayableAmount,
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
                    IsLcc = true,
                    ReturnPnr = !string.IsNullOrEmpty(returnPnr) ? returnPnr : (resp.TryGetProperty("ReturnPNR", out var rpNode) ? rpNode.ToString() : null),
                    TicketStatus = resp.TryGetProperty("TicketStatus", out var tsNode) ? tsNode.ToString() : null
                };
                
                _dbContext.FlightReservations.Add(reservation);
                await _dbContext.SaveChangesAsync();
            }

            if (reservation != null)
            {
                payment.FulfillmentStatus = "Success";
                payment.BookingReferenceId = reservation.Id;

                // Ensure passenger records with ticket numbers are populated
                if (requestPassengers != null && requestPassengers.Any())
                {
                    var existingPax = await _dbContext.FlightReservationPassengers
                        .Where(p => p.FlightReservationId == reservation.Id)
                        .ToListAsync();

                    if (!existingPax.Any())
                    {
                        var responsePassengers = new List<JsonElement>();
                        if (resp.TryGetProperty("Passengers", out var pArray) && pArray.ValueKind == JsonValueKind.Array)
                        {
                            responsePassengers = pArray.EnumerateArray().ToList();
                        }
                        else if (root.TryGetProperty("Passengers", out var rootPArray) && rootPArray.ValueKind == JsonValueKind.Array)
                        {
                            responsePassengers = rootPArray.EnumerateArray().ToList();
                        }

                        // Also check return leg ticket numbers from Legs if available
                        var returnTicketNumbers = new List<string>();
                        if (root.TryGetProperty("Legs", out var legsNode2) && legsNode2.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var leg in legsNode2.EnumerateArray())
                            {
                                var legType = leg.TryGetProperty("Direction", out var dirProp) ? dirProp.GetString() : (leg.TryGetProperty("LegType", out var ltProp) ? ltProp.GetString() : "");
                                if (string.Equals(legType, "RETURN", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (leg.TryGetProperty("ReturnTicketNumber", out var rtnProp) && rtnProp.ValueKind == JsonValueKind.String)
                                    {
                                        returnTicketNumbers.Add(rtnProp.GetString()!);
                                    }
                                    else if (leg.TryGetProperty("TicketNumber", out var tnProp) && tnProp.ValueKind == JsonValueKind.String)
                                    {
                                        returnTicketNumbers.Add(tnProp.GetString()!);
                                    }
                                }
                            }
                        }

                        var reservationPassengers = new List<FlightReservationPassenger>();
                        for (int i = 0; i < requestPassengers.Count; i++)
                        {
                            var p = requestPassengers[i];
                            var passObj = new FlightReservationPassenger
                            {
                                FlightReservation = reservation,
                                FullName = $"{p.FirstName} {p.LastName}".Trim(),
                                FirstName = p.FirstName,
                                LastName = p.LastName,
                                Title = p.Title,
                                PassportNo = p.PassportNo,
                                Nationality = p.CountryName,
                                Email = p.Email,
                                ContactNo = p.ContactNo,
                                DateOfBirth = DateTime.TryParse(p.DateOfBirth, out var dob) ? dob : null,
                                PassengerType = p.PaxType == 1 ? "Adult" : p.PaxType == 2 ? "Child" : "Infant",
                                Gender = p.Gender == "1" ? "Male" : "Female",
                                Status = "Booked"
                            };

                            if (p.Seat != null && p.Seat.Any())
                            {
                                var rawSeats = p.Seat.Select(s => s.SeatNumber ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s));
                                passObj.SeatNumber = rawSeats.Any() ? string.Join(", ", rawSeats) : null;
                            }

                            if (i < responsePassengers.Count)
                            {
                                var matchedPax = responsePassengers.FirstOrDefault(r =>
                                    r.TryGetProperty("FirstName", out var fn) && fn.ToString()?.Equals(p.FirstName, StringComparison.OrdinalIgnoreCase) == true &&
                                    r.TryGetProperty("LastName", out var ln) && ln.ToString()?.Equals(p.LastName, StringComparison.OrdinalIgnoreCase) == true
                                );

                                var rPax = matchedPax.ValueKind != JsonValueKind.Undefined ? matchedPax : responsePassengers[i];

                                if (rPax.TryGetProperty("PaxId", out var paxIdNode))
                                {
                                    if (paxIdNode.ValueKind == JsonValueKind.Number)
                                        passObj.PaxId = paxIdNode.GetInt32();
                                    else if (paxIdNode.ValueKind == JsonValueKind.String && int.TryParse(paxIdNode.ToString(), out var parsedPaxId))
                                        passObj.PaxId = parsedPaxId;
                                }

                                if (rPax.TryGetProperty("Ticket", out var tktNode))
                                {
                                    var tIdStr = tktNode.TryGetProperty("TicketId", out var tId) ? tId.ToString() : null;
                                    passObj.TicketId = string.IsNullOrWhiteSpace(tIdStr) ? null : tIdStr;

                                    var tNumStr = tktNode.TryGetProperty("TicketNumber", out var tNum) ? tNum.ToString() : null;
                                    passObj.TicketNumber = string.IsNullOrWhiteSpace(tNumStr) ? null : tNumStr;
                                }

                                if (rPax.TryGetProperty("SegmentAdditionalInfo", out var segInfo) && segInfo.ValueKind == JsonValueKind.Array)
                                {
                                    var confirmedSeats = segInfo.EnumerateArray()
                                        .Select(s => s.TryGetProperty("Seat", out var seatProp) ? seatProp.GetString() : null)
                                        .Where(s => !string.IsNullOrWhiteSpace(s));

                                    if (confirmedSeats.Any())
                                    {
                                        passObj.SeatNumber = string.Join(", ", confirmedSeats);
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(passObj.TicketNumber) && i < returnTicketNumbers.Count)
                            {
                                passObj.TicketNumber = returnTicketNumbers[i];
                            }

                            reservationPassengers.Add(passObj);
                        }

                        _dbContext.FlightReservationPassengers.AddRange(reservationPassengers);
                    }
                }

                await ProcessCouponConsumptionAsync(payment.CouponCode, payment.UserId, reservation.Id, payment.FinalPayableAmount, payment.DiscountAmount, "Flight");
                
                await _notificationService.EnqueueAsync(
                    eventType: "FlightBookingSuccess",
                    channel: "Email",
                    recipient: reservation.PassengerEmail ?? payment.UserId,
                    templateKey: "FLIGHT_BOOKING_CONFIRMED",
                    payload: new { Pnr = reservation.Pnr, Name = reservation.PassengerName, Amount = payment.FinalPayableAmount }
                );

                var flightNum = !string.IsNullOrWhiteSpace(reservation.FlightNumber) ? reservation.FlightNumber : "Flight";
                var route = !string.IsNullOrWhiteSpace(reservation.FromCity) && !string.IsNullOrWhiteSpace(reservation.ToCity)
                    ? $"{reservation.FromCity}-{reservation.ToCity}"
                    : "Trip";
                var travelDate = reservation.DepartureTime != default
                    ? reservation.DepartureTime.ToString("dd/MM/yyyy hh:mm tt")
                    : DateTime.UtcNow.ToString("dd/MM/yyyy hh:mm tt");

                await _notificationService.EnqueueAsync(
                    eventType: "FlightBookingSuccess",
                    channel: "SMS",
                    recipient: reservation.PassengerPhone ?? "",
                    templateKey: "FLIGHT_BOOKING_CONFIRMED_SMS",
                    payload: new {
                        Pnr = reservation.Pnr,
                        Flight = flightNum,
                        Route = route,
                        Date = travelDate,
                        Name = reservation.PassengerName
                    }
                );

                await _dbContext.SaveChangesAsync();

                // Additive In-App Notifications (Step 4: Flight Confirmed)
                if (_inAppNotificationService != null)
                {
                    try
                    {
                        await _inAppNotificationService.CreateNotificationAsync(
                            type: "Booking",
                            category: "Customer",
                            title: "Flight Booking Confirmed",
                            message: $"Your flight booking ({reservation.BookingReference}) with PNR {reservation.Pnr} has been confirmed.",
                            severity: "Success",
                            referenceType: "FlightBooking",
                            referenceId: reservation.BookingReference,
                            actionUrl: $"/bookings/{reservation.BookingReference}",
                            idempotencyKey: $"BOOKING_SUCCESS_FLIGHT_{payment.PaymentReference}",
                            targetUserId: payment.UserId
                        );
                    }
                    catch (Exception inAppEx)
                    {
                        _logger.LogWarning(inAppEx, "Failed to create in-app notification for Flight Booking Confirmed {PaymentReference}", payment.PaymentReference);
                    }
                }
            }

            return (true, null);
        }
    }
}
