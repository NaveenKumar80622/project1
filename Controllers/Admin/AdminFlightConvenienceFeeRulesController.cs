using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    [Route("api/admin/flight-convenience-fee-rules")]
    public class AdminFlightConvenienceFeeRulesController : AdminApiController
    {
        private readonly AppDbContext _context;

        public AdminFlightConvenienceFeeRulesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var rules = await _context.FlightConvenienceFeeRules
                .OrderBy(r => r.TripType)
                .ToListAsync();

            var response = rules.Select(r => MapToResponseDto(r)).ToList();
            return Ok(response);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var rule = await _context.FlightConvenienceFeeRules.FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
            {
                return NotFound(new { message = "Flight convenience fee rule not found." });
            }

            return Ok(MapToResponseDto(rule));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFlightConvenienceFeeRuleDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid payload.");
            }

            var allowedFeeTypes = new[] { "Flat", "Percentage" };
            var feeType = (dto.FeeType ?? string.Empty).Trim();
            if (!allowedFeeTypes.Contains(feeType, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest($"FeeType must be one of: {string.Join(", ", allowedFeeTypes)}.");
            }

            var rule = new FlightConvenienceFeeRule
            {
                TripType = dto.TripType,
                FeeType = feeType,
                FeeValue = dto.FeeValue,
                IsActive = dto.IsActive
            };

            _context.FlightConvenienceFeeRules.Add(rule);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = rule.Id }, MapToResponseDto(rule));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateFlightConvenienceFeeRuleDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid payload.");
            }

            var allowedFeeTypes = new[] { "Flat", "Percentage" };
            var feeType = (dto.FeeType ?? string.Empty).Trim();
            if (!allowedFeeTypes.Contains(feeType, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest($"FeeType must be one of: {string.Join(", ", allowedFeeTypes)}.");
            }

            var rule = await _context.FlightConvenienceFeeRules.FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
            {
                return NotFound(new { message = "Flight convenience fee rule not found." });
            }

            rule.TripType = dto.TripType;
            rule.FeeType = feeType;
            rule.FeeValue = dto.FeeValue;
            rule.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return Ok(MapToResponseDto(rule));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var rule = await _context.FlightConvenienceFeeRules.FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
            {
                return NotFound(new { message = "Flight convenience fee rule not found." });
            }

            _context.FlightConvenienceFeeRules.Remove(rule);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Flight convenience fee rule deleted successfully." });
        }

        private static FlightConvenienceFeeRuleResponseDto MapToResponseDto(FlightConvenienceFeeRule rule)
        {
            return new FlightConvenienceFeeRuleResponseDto
            {
                Id = rule.Id,
                TripType = rule.TripType.ToString(),
                FeeType = rule.FeeType,
                FeeValue = rule.FeeValue,
                IsActive = rule.IsActive
            };
        }
    }
}
