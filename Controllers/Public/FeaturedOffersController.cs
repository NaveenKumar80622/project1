using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;

namespace PickNBook.Api.Controllers;

public class FeaturedOffersController : BaseApiController
{
    private readonly IFeaturedOffersService _featuredOffersService;
    private readonly IExclusiveOfferSubscriptionService _subscriptionService;

    public FeaturedOffersController(
        IFeaturedOffersService featuredOffersService,
        IExclusiveOfferSubscriptionService subscriptionService)
    {
        _featuredOffersService = featuredOffersService;
        _subscriptionService = subscriptionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetFeaturedOffers([FromQuery] string? bookingType = null)
    {
        var offers = await _featuredOffersService.GetFeaturedOffersAsync(bookingType);

        return Ok(new
        {
            count = offers.Count,
            offers
        });
    }



    [HttpPost("subscribe")]
    public async Task<IActionResult> SubscribeToExclusiveOffers([FromBody] ExclusiveOfferSubscriptionRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { isSuccess = false, message = "Email is required." });
        }

        var email = request.Email.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            return BadRequest(new { isSuccess = false, message = "Please enter a valid email address." });
        }

        var response = await _subscriptionService.SubscribeAsync(request);
        if (!response.IsSuccess)
        {
            return StatusCode(502, response);
        }

        return Ok(response);
    }
}
