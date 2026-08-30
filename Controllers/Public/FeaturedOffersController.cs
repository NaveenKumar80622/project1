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
    public async Task<IActionResult> GetFeaturedOffers()
    {
        var offers = await _featuredOffersService.GetFeaturedOffersAsync();

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
            return BadRequest("Email is required.");
        }

        var response = await _subscriptionService.SubscribeAsync(request);
        if (!response.IsSuccess)
        {
            return StatusCode(502, response);
        }

        return Ok(response);
    }
}
