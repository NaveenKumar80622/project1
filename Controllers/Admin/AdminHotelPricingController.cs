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
    [Route("api/admin/hotel-pricing")]
    public class AdminHotelPricingController : AdminApiController
    {
        private readonly AppDbContext _context;

        public AdminHotelPricingController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var settings = await _context.HotelPricingSettings
                .OrderByDescending(s => s.IsActive)
                .ThenByDescending(s => s.UpdatedAtUtc)
                .ToListAsync();

            var response = settings.Select(s => MapToResponseDto(s)).ToList();
            return Ok(response);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var setting = await _context.HotelPricingSettings.FirstOrDefaultAsync(s => s.Id == id);
            if (setting == null)
            {
                return NotFound(new { message = "Hotel pricing setting not found." });
            }

            return Ok(MapToResponseDto(setting));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateHotelPricingSettingDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid payload.");
            }

            if (dto.IsActive)
            {
                // Deactivate all other rules
                var activeRules = await _context.HotelPricingSettings.Where(r => r.IsActive).ToListAsync();
                foreach (var rule in activeRules)
                {
                    rule.IsActive = false;
                    rule.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            var setting = new HotelPricingSetting
            {
                MarkupType = dto.MarkupType,
                MarkupValue = dto.MarkupValue,
                IsActive = dto.IsActive,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = User?.Identity?.Name ?? "Admin"
            };

            _context.HotelPricingSettings.Add(setting);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = setting.Id }, MapToResponseDto(setting));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateHotelPricingSettingDto dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid payload.");
            }

            var setting = await _context.HotelPricingSettings.FirstOrDefaultAsync(s => s.Id == id);
            if (setting == null)
            {
                return NotFound(new { message = "Hotel pricing setting not found." });
            }

            if (dto.IsActive && !setting.IsActive)
            {
                // Deactivate all other rules
                var activeRules = await _context.HotelPricingSettings.Where(r => r.IsActive && r.Id != id).ToListAsync();
                foreach (var rule in activeRules)
                {
                    rule.IsActive = false;
                    rule.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            setting.MarkupType = dto.MarkupType;
            setting.MarkupValue = dto.MarkupValue;
            setting.IsActive = dto.IsActive;
            setting.UpdatedAtUtc = DateTime.UtcNow;
            setting.UpdatedBy = User?.Identity?.Name ?? "Admin";

            await _context.SaveChangesAsync();

            return Ok(MapToResponseDto(setting));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var setting = await _context.HotelPricingSettings.FirstOrDefaultAsync(s => s.Id == id);
            if (setting == null)
            {
                return NotFound(new { message = "Hotel pricing setting not found." });
            }

            _context.HotelPricingSettings.Remove(setting);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Hotel pricing setting deleted successfully." });
        }

        private static HotelPricingSettingResponseDto MapToResponseDto(HotelPricingSetting rule)
        {
            return new HotelPricingSettingResponseDto
            {
                Id = rule.Id,
                MarkupType = rule.MarkupType,
                MarkupValue = rule.MarkupValue,
                IsActive = rule.IsActive,
                CreatedAtUtc = rule.CreatedAtUtc,
                UpdatedAtUtc = rule.UpdatedAtUtc,
                UpdatedBy = rule.UpdatedBy
            };
        }
    }
}
