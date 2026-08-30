using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Services;

public interface IFlightAnalyticsService
{
    Task<FeaturedFlightsDto> GetFeaturedFlights(
        string origin,
        string destination,
        decimal? budget);
}

public class FlightAnalyticsService : IFlightAnalyticsService
{
    private readonly ISrdvFlightService _srdvFlightService;
    private readonly AppDbContext _context;

    public FlightAnalyticsService(
        ISrdvFlightService srdvFlightService,
        AppDbContext context)
    {
        _srdvFlightService = srdvFlightService;
        _context = context;
    }

    public async Task<FeaturedFlightsDto> GetFeaturedFlights(
        string origin,
        string destination,
        decimal? budget)
    {
        var flights = await _srdvFlightService.SearchFlightsAsync(
            origin,
            destination,
            DateTime.UtcNow.Date);

        if (flights == null || !flights.Any())
        {
            return new FeaturedFlightsDto();
        }

        // De-duplicate same offer variants returned by provider.
        var uniqueFlights = flights
            .GroupBy(f => new
            {
                f.Airline,
                f.Origin,
                f.Destination,
                f.DepartureTime,
                f.ArrivalTime,
                f.Price,
                f.Currency,
                f.DurationMinutes,
                f.StopsCount
            })
            .Select(g => g
                .OrderByDescending(x => x.AvailableSeats)
                .First())
            .ToList();

        // =============================
        // CHEAPEST
        // =============================
        var cheapest = uniqueFlights
            .OrderBy(f => f.Price)
            .FirstOrDefault();

        // =============================
        // UNDER BUDGET (LIMIT 5)
        // budget=null means no budget filtering.
        // =============================
        var underBudgetBase = budget.HasValue
            ? uniqueFlights.Where(f => f.Price <= budget.Value)
            : uniqueFlights;

        var underBudget = underBudgetBase
            .OrderBy(f => f.Price)
            .Take(5)
            .ToList();

        // =============================
        // FASTEST (Prefer Non-Stop)
        // =============================
        var fastestNonStop = uniqueFlights
            .Where(f => f.StopsCount == 0)
            .OrderBy(f => f.DurationMinutes)
            .ThenBy(f => f.Price)
            .FirstOrDefault();

        var fastestCheapest = fastestNonStop ?? uniqueFlights
            .OrderBy(f => f.DurationMinutes)
            .ThenBy(f => f.Price)
            .FirstOrDefault();

        // =============================
        // CHEAPEST AIRLINE
        // =============================
        var cheapestAirline = uniqueFlights
            .GroupBy(f => f.Airline)
            .OrderBy(g => g.Min(f => f.Price))
            .FirstOrDefault()?.Key;

        // =============================
        // LIMITED SEATS (<=5)
        // =============================
        var limitedSeats = uniqueFlights
            .Where(f => f.AvailableSeats <= 5)
            .OrderBy(f => f.AvailableSeats)
            .Take(5)
            .ToList();

        // =============================
        // WEEKLY CHEAPEST (LAST 7 DAYS)
        // =============================
        // Removed as CheapestFlight legacy table is deleted
        FlightOfferDto? weeklyCheapest = null;

        return new FeaturedFlightsDto
        {
            CheapestRoute = cheapest,
            UnderBudgetFlights = underBudget,
            FastestCheapestCombo = fastestCheapest,
            CheapestAirline = cheapestAirline,
            LimitedSeatFlights = limitedSeats,
            WeeklyCheapestFlight = weeklyCheapest
        };
    }
}
