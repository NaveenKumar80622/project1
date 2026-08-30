using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    [Route("api/admin/flight-promotions")]
    public class AdminFlightPromotionsController : AdminApiController
    {
        private readonly AppDbContext _context;

        public AdminFlightPromotionsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var promotions = await _context.FlightPromotions
                .Include(p => p.Conditions)
                .OrderByDescending(p => p.Priority)
                .ToListAsync();

            var response = promotions.Select(p => MapToResponseDto(p)).ToList();
            return Ok(response);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var promo = await _context.FlightPromotions
                .Include(p => p.Conditions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (promo == null)
            {
                return NotFound(new { message = "Flight promotion not found." });
            }

            return Ok(MapToResponseDto(promo));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFlightPromotionDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid payload.");
            }

            var promo = new FlightPromotion
            {
                Name = dto.Name,
                Description = dto.Description,
                DiscountType = dto.DiscountType,
                DiscountValue = dto.DiscountValue,
                MaximumDiscount = dto.MaximumDiscount,
                MinimumFare = dto.MinimumFare,
                Priority = dto.Priority,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                IsActive = dto.IsActive,
                IsAutoApply = dto.IsAutoApply,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Conditions = dto.Conditions.Select(c => new FlightPromotionCondition
                {
                    ConditionType = c.ConditionType,
                    Operator = c.Operator,
                    Value = c.Value,
                    CreatedAtUtc = DateTime.UtcNow
                }).ToList()
            };

            _context.FlightPromotions.Add(promo);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = promo.Id }, MapToResponseDto(promo));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateFlightPromotionDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid payload.");
            }

            var promo = await _context.FlightPromotions
                .Include(p => p.Conditions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (promo == null)
            {
                return NotFound(new { message = "Flight promotion not found." });
            }

            promo.Name = dto.Name;
            promo.Description = dto.Description;
            promo.DiscountType = dto.DiscountType;
            promo.DiscountValue = dto.DiscountValue;
            promo.MaximumDiscount = dto.MaximumDiscount;
            promo.MinimumFare = dto.MinimumFare;
            promo.Priority = dto.Priority;
            promo.StartDate = dto.StartDate;
            promo.EndDate = dto.EndDate;
            promo.IsActive = dto.IsActive;
            promo.IsAutoApply = dto.IsAutoApply;
            promo.UpdatedAtUtc = DateTime.UtcNow;

            // Update conditions
            _context.FlightPromotionConditions.RemoveRange(promo.Conditions);

            promo.Conditions = dto.Conditions.Select(c => new FlightPromotionCondition
            {
                FlightPromotionId = id,
                ConditionType = c.ConditionType,
                Operator = c.Operator,
                Value = c.Value,
                CreatedAtUtc = DateTime.UtcNow
            }).ToList();

            await _context.SaveChangesAsync();

            return Ok(MapToResponseDto(promo));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var promo = await _context.FlightPromotions.FirstOrDefaultAsync(p => p.Id == id);
            if (promo == null)
            {
                return NotFound(new { message = "Flight promotion not found." });
            }

            _context.FlightPromotions.Remove(promo);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Flight promotion deleted successfully." });
        }

        private static FlightPromotionResponseDto MapToResponseDto(FlightPromotion promo)
        {
            return new FlightPromotionResponseDto
            {
                Id = promo.Id,
                Name = promo.Name,
                Description = promo.Description,
                DiscountType = promo.DiscountType.ToString(),
                DiscountValue = promo.DiscountValue,
                MaximumDiscount = promo.MaximumDiscount,
                MinimumFare = promo.MinimumFare,
                Priority = promo.Priority,
                StartDate = promo.StartDate,
                EndDate = promo.EndDate,
                IsActive = promo.IsActive,
                IsAutoApply = promo.IsAutoApply,
                CreatedAtUtc = promo.CreatedAtUtc,
                UpdatedAtUtc = promo.UpdatedAtUtc,
                Conditions = promo.Conditions.Select(c => new FlightPromotionConditionDto
                {
                    Id = c.Id,
                    ConditionType = c.ConditionType,
                    Operator = c.Operator,
                    Value = c.Value
                }).ToList()
            };
        }
    }
}
