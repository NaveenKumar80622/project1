using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Services;

namespace PickNBook.Api.Controllers
{

    [ApiController]
    [Route("api/bookings")]
    public class BookingsController : ControllerBase
    {
        private readonly IBookingHistoryService _bookingHistoryService;

        public BookingsController(
            IBookingHistoryService bookingHistoryService)
        {
            _bookingHistoryService = bookingHistoryService;
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] string? type)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                         ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    message = "User ID not found in token"
                });
            }

            var result = await _bookingHistoryService
                .GetBookingHistoryAsync(userId, type);

            return Ok(result);
        }
    }
}
