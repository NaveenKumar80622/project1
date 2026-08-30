using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using System;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    [Route("api/admin/featured-offers")]
    public class AdminFeaturedOffersController : AdminApiController
    {
        private readonly IAdminFeaturedOffersService _service;
        private readonly AppDbContext _context;

        public AdminFeaturedOffersController(
            IAdminFeaturedOffersService service,
            AppDbContext context)
        {
            _service = service;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var offers = await _service.GetAllAsync();
            return Ok(offers);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var offer = await _service.GetByIdAsync(id);

            if (offer == null)
            {
                return NotFound(new
                {
                    message = "Offer not found."
                });
            }

            return Ok(offer);
        }

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromForm] AdminFeaturedOfferRequestDto request)
        {
            if (request is null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var result = await _service.CreateAsync(request);

            return Ok(new
            {
                message = "Offer created successfully.",
                data = result
            });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(
            int id,
            [FromForm] AdminFeaturedOfferRequestDto request)
        {
            if (request is null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var result = await _service.UpdateAsync(id, request);

            if (result == null)
            {
                return NotFound(new
                {
                    message = "Offer not found."
                });
            }

            return Ok(new
            {
                message = "Offer updated successfully.",
                data = result
            });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _service.DeleteAsync(id);

            if (!deleted)
            {
                return NotFound(new
                {
                    message = "Offer not found."
                });
            }

            return Ok(new
            {
                message = "Offer deleted successfully."
            });
        }

        // ========================================
        // Featured Offer Condition CRUD Endpoints
        // ========================================

        [HttpPost("{offerId:int}/conditions")]
        public async Task<IActionResult> AddCondition(int offerId, [FromBody] FeaturedOfferConditionRequestDto request)
        {
            if (request is null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var offer = await _context.FeaturedOffers.FirstOrDefaultAsync(x => x.Id == offerId);
            if (offer == null)
            {
                return NotFound(new { message = "Featured offer not found." });
            }

            if (string.IsNullOrWhiteSpace(request.ConditionType))
            {
                return BadRequest(new { message = "ConditionType is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Value1))
            {
                return BadRequest(new { message = "Value1 is required." });
            }

            var condition = new FeaturedOfferCondition
            {
                FeaturedOfferId = offerId,
                ConditionType = request.ConditionType,
                Value1 = request.Value1,
                Value2 = request.Value2,
                IsActive = request.IsActive
            };

            _context.FeaturedOfferConditions.Add(condition);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Condition added successfully.",
                data = condition
            });
        }

        [HttpGet("{offerId:int}/conditions")]
        public async Task<IActionResult> GetConditions(int offerId)
        {
            var offer = await _context.FeaturedOffers.AnyAsync(x => x.Id == offerId);
            if (!offer)
            {
                return NotFound(new { message = "Featured offer not found." });
            }

            var conditions = await _context.FeaturedOfferConditions
                .Where(x => x.FeaturedOfferId == offerId)
                .ToListAsync();

            return Ok(conditions);
        }

        [HttpPut("{offerId:int}/conditions/{conditionId:int}")]
        public async Task<IActionResult> UpdateCondition(int offerId, int conditionId, [FromBody] FeaturedOfferConditionRequestDto request)
        {
            if (request is null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var offerExists = await _context.FeaturedOffers.AnyAsync(x => x.Id == offerId);
            if (!offerExists)
            {
                return NotFound(new { message = "Featured offer not found." });
            }

            if (string.IsNullOrWhiteSpace(request.ConditionType))
            {
                return BadRequest(new { message = "ConditionType is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Value1))
            {
                return BadRequest(new { message = "Value1 is required." });
            }

            var condition = await _context.FeaturedOfferConditions
                .FirstOrDefaultAsync(x => x.Id == conditionId && x.FeaturedOfferId == offerId);

            bool isNew = false;
            if (condition == null)
            {
                condition = new FeaturedOfferCondition
                {
                    FeaturedOfferId = offerId
                };
                _context.FeaturedOfferConditions.Add(condition);
                isNew = true;
            }

            condition.ConditionType = request.ConditionType;
            condition.Value1 = request.Value1;
            condition.Value2 = request.Value2;
            condition.IsActive = request.IsActive;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = isNew ? "Condition created successfully." : "Condition updated successfully.",
                data = condition
            });
        }

        [HttpDelete("{offerId:int}/conditions/{conditionId:int}")]
        public async Task<IActionResult> DeleteCondition(int offerId, int conditionId)
        {
            var condition = await _context.FeaturedOfferConditions
                .FirstOrDefaultAsync(x => x.Id == conditionId && x.FeaturedOfferId == offerId);

            if (condition == null)
            {
                return NotFound(new { message = "Condition not found." });
            }

            _context.FeaturedOfferConditions.Remove(condition);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Condition deleted successfully." });
        }
    }
}
