using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace PickNBook.Api.Controllers
{
    [Authorize]
    public class TravelersController(AppDbContext dbContext) : BaseApiController
    {
        private const string UserIdHeaderName = "X-User-Id";
        private static readonly string[] AllowedTypes = ["Adult", "Child", "Infant"];
        private static readonly string[] AllowedTitles = ["Mr", "Mrs", "Ms"];
        private static readonly string[] AllowedGenders = ["Male", "Female", "Other"];

        [HttpGet]
        public async Task<IActionResult> GetTravelers(
            [FromQuery] string? type,
            [FromQuery] string? phoneNo,
            [FromQuery] string? email,
            [FromQuery] string? query,
            [FromQuery] int limit = 100)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            if (limit <= 0)
            {
                return BadRequest("limit must be greater than 0.");
            }

            limit = Math.Min(limit, 500);

            var normalizedType = ResolveAllowedValue(type, AllowedTypes);
            if (!string.IsNullOrWhiteSpace(type) && normalizedType is null)
            {
                return BadRequest($"Invalid type. Allowed values: {string.Join(", ", AllowedTypes)}.");
            }

            var travelersQuery = dbContext.Travelers
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .AsQueryable();

            if (normalizedType is not null)
            {
                travelersQuery = travelersQuery.Where(x => x.Type == normalizedType);
            }

            if (!string.IsNullOrWhiteSpace(phoneNo))
            {
                var phone = phoneNo.Trim();
                travelersQuery = travelersQuery.Where(x => EF.Functions.Like(x.PhoneNo, $"%{phone}%"));
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var emailValue = email.Trim();
                travelersQuery = travelersQuery.Where(x => EF.Functions.Like(x.Email, $"%{emailValue}%"));
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var keyword = query.Trim();
                travelersQuery = travelersQuery.Where(x =>
                    EF.Functions.Like(x.FirstName, $"%{keyword}%") ||
                    EF.Functions.Like(x.LastName, $"%{keyword}%") ||
                    EF.Functions.Like(x.Email, $"%{keyword}%") ||
                    EF.Functions.Like(x.PhoneNo, $"%{keyword}%") ||
                    EF.Functions.Like(x.Country, $"%{keyword}%"));
            }

            var travelers = await travelersQuery
                .OrderBy(x => x.FirstName)
                .ThenBy(x => x.LastName)
                .Take(limit)
                .ToListAsync();

            return Ok(travelers.Select(MapTraveler));
        }

        [HttpGet("{travelerId:int}")]
        public async Task<IActionResult> GetTravelerById(int travelerId)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            var traveler = await dbContext.Travelers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == travelerId && x.UserId == userId);

            if (traveler is null)
            {
                return NotFound("Traveler not found.");
            }

            return Ok(MapTraveler(traveler));
        }

        [HttpPost]
        public async Task<IActionResult> CreateTraveler([FromBody] UpsertTravelerRequestDto request)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            //var validationError = ValidateTraveler(request, out var normalizedType, out var normalizedTitle, out var normalizedGender);
            var validationError = ValidateTraveler(
    request,
    out var normalizedType,
    out var normalizedTitle,
    out var normalizedGender
   );


            if (validationError is not null)
            {
                return BadRequest(validationError);
            }

            var utcNow = DateTime.UtcNow;

            var traveler = new Traveler
            {
                UserId = userId!,
                Type = normalizedType!,
                Title = normalizedTitle!,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Gender = normalizedGender!,
                //Dob = request.Dob,
                Age = request.Age,
                Email = request.Email.Trim(),
                PhoneNo = request.PhoneNo.Trim(),
                PassportNo = string.IsNullOrWhiteSpace(request.PassportNo) ? null : request.PassportNo.Trim(),
                Country = request.Country.Trim(),
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow
            };

            dbContext.Travelers.Add(traveler);
            await dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTravelerById), new { travelerId = traveler.Id }, MapTraveler(traveler));
        }

        [HttpPut("{travelerId:int}")]
        public async Task<IActionResult> UpdateTraveler(int travelerId, [FromBody] UpsertTravelerRequestDto request)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            //var validationError = ValidateTraveler(request, out var normalizedType, out var normalizedTitle, out var normalizedGender);
            var validationError = ValidateTraveler(
    request,
    out var normalizedType,
    out var normalizedTitle,
    out var normalizedGender
    );

            if (validationError is not null)
            {
                return BadRequest(validationError);
            }

            var traveler = await dbContext.Travelers
                .FirstOrDefaultAsync(x => x.Id == travelerId && x.UserId == userId);
            if (traveler is null)
            {
                return NotFound("Traveler not found.");
            }

            traveler.Type = normalizedType!;
            traveler.Title = normalizedTitle!;
            traveler.FirstName = request.FirstName.Trim();
            traveler.LastName = request.LastName.Trim();
            traveler.Gender = normalizedGender!;
            //traveler.Dob = request.Dob;
            traveler.Age = request.Age;
            traveler.Email = request.Email.Trim();
            traveler.PhoneNo = request.PhoneNo.Trim();
            traveler.PassportNo = string.IsNullOrWhiteSpace(request.PassportNo) ? null : request.PassportNo.Trim();
            traveler.Country = request.Country.Trim();
            traveler.UpdatedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();
            return Ok(MapTraveler(traveler));
        }

        [HttpDelete("{travelerId:int}")]
        public async Task<IActionResult> DeleteTraveler(int travelerId)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            var traveler = await dbContext.Travelers
                .FirstOrDefaultAsync(x => x.Id == travelerId && x.UserId == userId);
            if (traveler is null)
            {
                return NotFound("Traveler not found.");
            }

            dbContext.Travelers.Remove(traveler);
            await dbContext.SaveChangesAsync();
            return Ok(new { message = "Traveler deleted successfully." });
        }

        private static TravelerResponseDto MapTraveler(Traveler traveler)
        {
            return new TravelerResponseDto
            {
                Id = traveler.Id,
                UserId = traveler.UserId,
                Type = traveler.Type,
                Title = traveler.Title,
                FirstName = traveler.FirstName,
                LastName = traveler.LastName,
                Gender = traveler.Gender,
                Age = traveler.Age,
                Email = traveler.Email,
                PhoneNo = traveler.PhoneNo,
                PassportNo = traveler.PassportNo,
                Country = traveler.Country,
                CreatedAtUtc = traveler.CreatedAtUtc,
                UpdatedAtUtc = traveler.UpdatedAtUtc
            };
        }


        private static string? ValidateTraveler(
            UpsertTravelerRequestDto request,
            out string? normalizedType,
            out string? normalizedTitle,
            out string? normalizedGender
            )
        {
            normalizedType = ResolveAllowedValue(request.Type, AllowedTypes);
            normalizedTitle = ResolveAllowedValue(request.Title, AllowedTitles);
            normalizedGender = ResolveAllowedValue(request.Gender, AllowedGenders);
           


            if (normalizedType is null)
            {
                return $"Invalid type. Allowed values: {string.Join(", ", AllowedTypes)}.";
            }

            if (normalizedTitle is null)
            {
                return $"Invalid title. Allowed values: {string.Join(", ", AllowedTitles)}.";
            }

            if (normalizedGender is null)
            {
                return $"Invalid gender. Allowed values: {string.Join(", ", AllowedGenders)}.";
            }

            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            {
                return "FirstName and LastName are required.";
            }

            //if (string.IsNullOrWhiteSpace(request.Email))
            //{
            //    return "Email is required.";
            //}

            //if (!new EmailAddressAttribute().IsValid(request.Email.Trim()))
            //{
            //    return "Email format is invalid.";
            //}

            //if (string.IsNullOrWhiteSpace(request.PhoneNo))
            //{
            //    return "PhoneNo is required.";
            //}

            if (string.IsNullOrWhiteSpace(request.Country))
            {
                return "Country is required.";
            }

            if (request.Age <= 0)
            {
                return "Age must be greater than 0.";
            }

            var age = request.Age;


            // 🔥 NOW USE parsedDob instead of request.Dob

            var today = DateOnly.FromDateTime(DateTime.UtcNow);


            if (normalizedType == "Adult" && age < 12)
            {
                return "Type Adult requires age 12+.";
            }

            if (normalizedType == "Child" && (age < 2 || age > 11))
            {
                return "Type Child requires age 2 to 11.";
            }

            if (normalizedType == "Infant" && (age < 0 || age > 1))
            {
                return "Type Infant requires age 0 to 1.";
            }

            return null;
        }

        

        private static string? ResolveAllowedValue(string? value, string[] allowed)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return allowed.FirstOrDefault(x => x.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        }

        private bool TryGetCurrentUserId(out string? userId, out string? error)
        {
            userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrWhiteSpace(userId))
            {
                error = "User is not authenticated.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
