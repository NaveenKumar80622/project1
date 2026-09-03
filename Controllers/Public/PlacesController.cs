using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Controllers
{
    public class PlacesController(AppDbContext dbContext, IPlacesService placesService) : BaseApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetPlaces(
            [FromQuery] string? query,
            [FromQuery] string tripType = "all",
            [FromQuery] string field = "all",
            [FromQuery] string? requestType = null,
            [FromQuery] int limit = 20,
            CancellationToken cancellationToken = default)
        {
            if (limit <= 0)
            {
                return BadRequest("limit must be greater than 0.");
            }

            var normalizedTripType = tripType.Trim().ToLowerInvariant();
            if (normalizedTripType is not ("all" or "flight" or "bus" or "hotel"))
            {
                return BadRequest("tripType must be one of: all, flight, bus, hotel.");
            }

            var normalizedField = field.Trim().ToLowerInvariant();
            if (normalizedField is not ("all" or "from" or "to"))
            {
                return BadRequest("field must be one of: all, from, to.");
            }

            var results = await placesService.GetPlacesAsync(
                query,
                tripType,
                field,
                requestType,
                limit,
                cancellationToken);

            return Ok(results);
        }

        [HttpPost("track")]
        public async Task<IActionResult> TrackPlaceSelection([FromBody] TrackPlaceRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CityName) || string.IsNullOrWhiteSpace(request.TripType))
                return BadRequest("CityName and TripType are required.");

            var existing = await dbContext.PlaceSearchStats
                .FirstOrDefaultAsync(x => x.CityName == request.CityName && x.TripType == request.TripType);

            if (existing != null)
            {
                existing.SelectionCount++;
                existing.LastSelectedAtUtc = DateTime.UtcNow;
            }
            else
            {
                dbContext.PlaceSearchStats.Add(new PlaceSearchStat
                {
                    CityName = request.CityName,
                    CityCode = request.CityCode,
                    TripType = request.TripType,
                    SelectionCount = 1,
                    LastSelectedAtUtc = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync();
            return Ok();
        }
    }
}
