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

            if (searchItem != null)
            {
                context.OperatorName = searchItem.OperatorName;
                context.BusType = searchItem.BusType;

                // Resolve city names from city codes if applicable
                var fromName = _srdvBusService.MapCityCodeToName(searchItem.FromCity);
                context.SourceCity = !string.IsNullOrWhiteSpace(fromName) ? fromName : searchItem.FromCity;

                var toName = _srdvBusService.MapCityCodeToName(searchItem.ToCity);
                context.DestinationCity = !string.IsNullOrWhiteSpace(toName) ? toName : searchItem.ToCity;

                if (DateTime.TryParse(searchItem.DepartureTime, out var parsedDep))
                {
                    context.TravelDate = parsedDep;
                    context.DayOfWeek = parsedDep.DayOfWeek;
                }
                else if (DateTime.TryParse(searchItem.DepartDate, out var parsedDepartDate))
                {
                    context.TravelDate = parsedDepartDate;
                    context.DayOfWeek = parsedDepartDate.DayOfWeek;
                }
            }
            else if (fallbackBus != null)
            {
                // Fallback to BusBooking values if cache has expired
                context.OperatorName = fallbackBus.OperatorName;
                context.BusType = fallbackBus.BusType;

                var fromName = _srdvBusService.MapCityCodeToName(fallbackBus.FromCity);
                context.SourceCity = !string.IsNullOrWhiteSpace(fromName) ? fromName : fallbackBus.FromCity;

                var toName = _srdvBusService.MapCityCodeToName(fallbackBus.ToCity);
                context.DestinationCity = !string.IsNullOrWhiteSpace(toName) ? toName : fallbackBus.ToCity;

                var istDeparture = DateTime.SpecifyKind(fallbackBus.DepartureTime, DateTimeKind.Utc).Add(IndiaOffset);
                context.TravelDate = istDeparture;
                context.DayOfWeek = istDeparture.DayOfWeek;
            }

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
            decimal totalFare = 0m;
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

                // Authoritative SeatType resolution
                if (layoutMap != null && layoutMap.TryGetValue(seatCode, out var layoutSeat))
                {
                    seatCtx.SeatType = layoutSeat.SeatType;
                    seatCtx.Fare = layoutSeat.BaseFare;
                }

                // Authoritative Blocked Fare resolution (supersedes layout fare if block occurred)
                var blocked = blockedSeats.FirstOrDefault(b => b.SeatName.Equals(seatCode, StringComparison.OrdinalIgnoreCase));
                if (blocked != null && blocked.BaseFare > 0)
                {
                    seatCtx.Fare = blocked.BaseFare;
                }

                // Fallback to DTO values if cache expired
                if (fallbackSeats != null)
                {
                    var fallback = fallbackSeats.FirstOrDefault(f => f.SeatCode.Equals(seatCode, StringComparison.OrdinalIgnoreCase));
                    if (fallback != null)
                    {
                        if (string.IsNullOrWhiteSpace(seatCtx.SeatType))
                            seatCtx.SeatType = fallback.SeatType;

                        if (seatCtx.Fare <= 0)
                            seatCtx.Fare = fallback.BaseFare;
                    }
                }

                totalFare += seatCtx.Fare;
                context.SelectedSeats.Add(seatCtx);
            }

            // Set BookingFare
            context.BookingFare = totalFare > 0 ? totalFare : (fallbackBus?.PriceInr ?? 0m);

            return context;
        }
    }
}
