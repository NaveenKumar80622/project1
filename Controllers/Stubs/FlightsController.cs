using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class FlightsController : ControllerBase
{
    private readonly IFlightAnalyticsService _analyticsService;

    public FlightsController(IFlightAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeatured(
        string origin,
        string destination,
        decimal? budget = null)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination))
            return BadRequest("Origin and Destination are required.");

        try
        {
            var result = await _analyticsService
                .GetFeaturedFlights(origin.ToUpper(), destination.ToUpper(), budget);

            return Ok(result);
        }
        catch
        {
            return StatusCode(500, "Flight service unavailable. Please try again later.");
        }
    }
}
