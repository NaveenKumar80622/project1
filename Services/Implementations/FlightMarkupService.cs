using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;

namespace PickNBook.Api.Services
{
    public interface IFlightMarkupService
    {
        Task<decimal> CalculateMarkupAsync(string airlineCode, TripType tripType, string cabinClass, decimal supplierBaseFare);
    }

    public class FlightMarkupService : IFlightMarkupService
    {
        private readonly AppDbContext _dbContext;
        
        // Cache all rules for the duration of this request
        private List<FlightMarkupRule>? _cachedRules;

        public FlightMarkupService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<decimal> CalculateMarkupAsync(string airlineCode, TripType tripType, string cabinClass, decimal supplierBaseFare)
        {
            if (string.IsNullOrWhiteSpace(airlineCode))
            {
                return 0m;
            }

            var cleanAirlineCode = airlineCode.Trim().ToUpperInvariant();
            var cleanCabinClass = (string.IsNullOrWhiteSpace(cabinClass) ? "*" : cabinClass.Trim());

            if (_cachedRules == null)
            {
                _cachedRules = await _dbContext.FlightMarkupRules
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .ToListAsync();
            }

            var rules = _cachedRules
                .Where(x => x.TripType == tripType && 
                            (x.AirlineCode == cleanAirlineCode || x.AirlineCode == "*") &&
                            (string.Equals(x.CabinClass, cleanCabinClass, StringComparison.OrdinalIgnoreCase) || x.CabinClass == "*"))
                .OrderByDescending(x => x.Priority)
                .ToList();

            if (rules.Count == 0)
            {
                return 0m;
            }

            // Prefer specific airline match first, fallback to wildcard '*'
            // Also prefer specific cabin class match over wildcard
            var rule = rules
                .OrderByDescending(x => x.AirlineCode != "*")
                .ThenByDescending(x => x.CabinClass != "*")
                .First();

            decimal markupAmount = 0m;
            if (rule.MarkupType == FlightMarkupType.Flat)
            {
                markupAmount = rule.MarkupValue;
            }
            else if (rule.MarkupType == FlightMarkupType.Percentage)
            {
                markupAmount = supplierBaseFare * (rule.MarkupValue / 100m);
            }

            return decimal.Round(markupAmount, 2, MidpointRounding.AwayFromZero);
        }
    }
}
