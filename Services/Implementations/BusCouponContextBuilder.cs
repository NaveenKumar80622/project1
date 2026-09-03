using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Services
{
    public class BusCouponContextBuilder : IBusCouponContextBuilder
    {
        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ISrdvBusService _srdvBusService;
        private readonly ILogger<BusCouponContextBuilder> _logger;

        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

        public BusCouponContextBuilder(
            AppDbContext db,
            IMemoryCache cache,
            ISrdvBusService srdvBusService,
            ILogger<BusCouponContextBuilder> logger)
        {
            _db = db;
            _cache = cache;
            _srdvBusService = srdvBusService;
            _logger = logger;
        }

        public async Task<BusCouponValidationContext> BuildContextAsync(
            string? traceId,
            string? resultIndex,
            List<string> seatCodes,
            BusBooking? fallbackBus = null,
            List<SeatPreviewDto>? fallbackSeats = null)
        {
            var context = new BusCouponValidationContext();
            var normalizedTraceId = traceId?.Trim() ?? string.Empty;
            var normalizedResultIndex = resultIndex?.Trim() ?? string.Empty;

            // ---------------------------------------------------------
            // 1. Resolve Search Context (Operator, BusType, Route, Date)
            // ---------------------------------------------------------
            BusSearchItemContext? searchItem = null;
            if (!string.IsNullOrEmpty(normalizedTraceId) && !string.IsNullOrEmpty(normalizedResultIndex))
            {
                _cache.TryGetValue($"bus_ctx_{normalizedTraceId}_{normalizedResultIndex}", out searchItem);
            }

            if (searchItem == null || string.IsNullOrWhiteSpace(searchItem.DepartDate))
            {
                throw new InvalidOperationException("Authoritative journey date (DepartDate) could not be resolved from bus search data.");
            }

            context.OperatorName = searchItem.OperatorName;
            context.BusType = searchItem.BusType;

            // Resolve city names from city codes if applicable
            var fromName = _srdvBusService.MapCityCodeToName(searchItem.FromCity);
            context.SourceCity = !string.IsNullOrWhiteSpace(fromName) ? fromName : searchItem.FromCity;

            var toName = _srdvBusService.MapCityCodeToName(searchItem.ToCity);
            context.DestinationCity = !string.IsNullOrWhiteSpace(toName) ? toName : searchItem.ToCity;

            DateTime travelDate;
            if (!DateTime.TryParseExact(searchItem.DepartDate.Trim(),
                    new[] { "dd/MM/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "yyyy/MM/dd" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out travelDate))
            {
                throw new InvalidOperationException("Authoritative journey date (DepartDate) could not be resolved from bus search data.");
            }

            if (!string.IsNullOrWhiteSpace(searchItem.DepartureTime) &&
                TimeSpan.TryParse(searchItem.DepartureTime.Trim(), out var timePart))
            {
                travelDate = travelDate.Date.Add(timePart);
            }

            context.TravelDate = travelDate;
            context.DayOfWeek = travelDate.DayOfWeek;

            // ---------------------------------------------------------
            // 2. Resolve Seat Layout Context (SeatType, BaseFare)
            // ---------------------------------------------------------
            Dictionary<string, BusSeatLayoutItemContext>? layoutMap = null;
            if (!string.IsNullOrEmpty(normalizedTraceId) && !string.IsNullOrEmpty(normalizedResultIndex))
            {
                _cache.TryGetValue($"bus_seats_{normalizedTraceId}_{normalizedResultIndex}", out layoutMap);
            }

            // ---------------------------------------------------------
            // 3. Resolve Blocked Seat Prices (Authoritative prices at Block)
            // ---------------------------------------------------------
            List<BusBlockedSeatPrice> blockedSeats = new();
            if (!string.IsNullOrEmpty(normalizedTraceId))
            {
                blockedSeats = await _db.BusBlockedSeatPrices
                    .AsNoTracking()
                    .Where(x => x.TraceId == normalizedTraceId)
                    .ToListAsync();
            }

            // ---------------------------------------------------------
            // 4. Build SelectedSeats with Authoritative Data
            // ---------------------------------------------------------
            var distinctSeatCodes = seatCodes
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var seatCode in distinctSeatCodes)
            {
                var seatCtx = new BusCouponSeatContext
                {
                    SeatName = seatCode
                };

                // Authoritative SeatType resolution: strictly from backend layoutMap
                if (layoutMap != null && 
                    layoutMap.TryGetValue(seatCode, out var layoutSeat) && 
                    !string.IsNullOrWhiteSpace(layoutSeat.SeatType))
                {
                    seatCtx.SeatType = layoutSeat.SeatType;
                    seatCtx.Fare = layoutSeat.BaseFare;
                }
                else
                {
                    throw new InvalidOperationException($"Authoritative seat layout information is unavailable for seat '{seatCode}'. Please refresh the seat layout and block again.");
                }

                // Authoritative Blocked Fare resolution (supersedes layout fare if block occurred)
                var blocked = blockedSeats
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefault(b => b.SeatName.Equals(seatCode, StringComparison.OrdinalIgnoreCase));
                if (blocked != null && blocked.BaseFare > 0)
                {
                    seatCtx.Fare = blocked.BaseFare;
                }

                context.SelectedSeats.Add(seatCtx);
            }

            // ---------------------------------------------------------
            // 5. Pre-Discount Qualifying Fare (Base Fare + Markup)
            // ---------------------------------------------------------
            var allMarkups = await _db.BusMarkupSettings
                .AsNoTracking()
                .Where(x => x.Status == "Active")
                .ToListAsync();

            decimal preDiscountQualifyingFare = 0m;
            foreach (var seat in context.SelectedSeats)
            {
                var isSleeper = (seat.SeatType ?? "").Contains("sleeper", StringComparison.OrdinalIgnoreCase);
                var normalizedSeatType = isSleeper ? "Sleeper" : "Seater";

                var markup = allMarkups.FirstOrDefault(x =>
                    x.SeatType.Equals(normalizedSeatType, StringComparison.OrdinalIgnoreCase));

                decimal markupAmount = 0m;
                if (markup != null && seat.Fare > 0)
                {
                    markupAmount = markup.MarkupType.Equals("Percentage", StringComparison.OrdinalIgnoreCase)
                        ? seat.Fare * markup.Value / 100m
                        : markup.Value;
                }

                preDiscountQualifyingFare += (seat.Fare + markupAmount);
            }

            // Set BookingFare to the pre-discount qualifying fare
            context.BookingFare = preDiscountQualifyingFare > 0 ? preDiscountQualifyingFare : (fallbackBus?.PriceInr ?? 0m);

            return context;
        }
    }
}
