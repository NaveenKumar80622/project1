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
    [Route("api/admin/flight-markups")]
    public class AdminFlightMarkupController : AdminApiController
    {
        private readonly AppDbContext _context;

        public AdminFlightMarkupController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var rules = await _context.FlightMarkupRules
                .OrderByDescending(r => r.Priority)
                .ToListAsync();

            var response = rules.Select(r => MapToResponseDto(r)).ToList();
            return Ok(response);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var rule = await _context.FlightMarkupRules.FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
            {
                return NotFound(new { message = "Flight markup rule not found." });
            }

            return Ok(MapToResponseDto(rule));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFlightMarkupRuleDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid payload.");
            }

            var rule = new FlightMarkupRule
            {
                AirlineCode = (dto.AirlineCode ?? string.Empty).Trim().ToUpperInvariant(),
                TripType = dto.TripType,
                CabinClass = string.IsNullOrWhiteSpace(dto.CabinClass) ? "*" : dto.CabinClass.Trim(),
                MarkupType = dto.MarkupType,
                MarkupValue = dto.MarkupValue,
                Priority = dto.Priority,
                IsActive = dto.IsActive,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _context.FlightMarkupRules.Add(rule);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = rule.Id }, MapToResponseDto(rule));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateFlightMarkupRuleDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid payload.");
            }

            var rule = await _context.FlightMarkupRules.FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
            {
                return NotFound(new { message = "Flight markup rule not found." });
            }

            rule.AirlineCode = (dto.AirlineCode ?? string.Empty).Trim().ToUpperInvariant();
            rule.TripType = dto.TripType;
            rule.CabinClass = string.IsNullOrWhiteSpace(dto.CabinClass) ? "*" : dto.CabinClass.Trim();
            rule.MarkupType = dto.MarkupType;
            rule.MarkupValue = dto.MarkupValue;
            rule.Priority = dto.Priority;
            rule.IsActive = dto.IsActive;
            rule.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(MapToResponseDto(rule));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var rule = await _context.FlightMarkupRules.FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
            {
                return NotFound(new { message = "Flight markup rule not found." });
            }

            _context.FlightMarkupRules.Remove(rule);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Flight markup rule deleted successfully." });
        }

        private static FlightMarkupRuleResponseDto MapToResponseDto(FlightMarkupRule rule)
        {
            return new FlightMarkupRuleResponseDto
            {
                Id = rule.Id,
                AirlineCode = rule.AirlineCode,
                TripType = rule.TripType.ToString(),
                CabinClass = rule.CabinClass,
                MarkupType = rule.MarkupType.ToString(),
                MarkupValue = rule.MarkupValue,
                Priority = rule.Priority,
                IsActive = rule.IsActive,
                CreatedAtUtc = rule.CreatedAtUtc,
                UpdatedAtUtc = rule.UpdatedAtUtc
            };
        }
    }
}
