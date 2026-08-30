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
    [Route("api/[controller]")]
    [ApiController]
    public class FlightPromotionsController : BaseApiController
    {
        private readonly AppDbContext _context;

        public FlightPromotionsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetActivePromotions()
        {
            var today = DateTime.UtcNow.Date;
            var promotions = await _context.FlightPromotions
                .Include(p => p.Conditions)
                .Where(p => p.IsActive && 
                            (!p.StartDate.HasValue || p.StartDate.Value <= today) && 
                            (!p.EndDate.HasValue || p.EndDate.Value >= today))
                .OrderByDescending(p => p.Priority)
                .ToListAsync();

            var response = promotions.Select(p => new FlightPromotionResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                DiscountType = p.DiscountType.ToString(),
                DiscountValue = p.DiscountValue,
                MaximumDiscount = p.MaximumDiscount,
                MinimumFare = p.MinimumFare,
                Priority = p.Priority,
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                IsActive = p.IsActive,
                CreatedAtUtc = p.CreatedAtUtc,
                UpdatedAtUtc = p.UpdatedAtUtc,
                Conditions = p.Conditions.Select(c => new FlightPromotionConditionDto
                {
                    Id = c.Id,
                    ConditionType = c.ConditionType,
                    Operator = c.Operator,
                    Value = c.Value
                }).ToList()
            }).ToList();

            return Ok(response);
        }
    }
}
