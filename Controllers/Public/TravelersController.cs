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
using System.Text.RegularExpressions;
using System.Text.Json;

namespace PickNBook.Api.Controllers
{
    [Authorize]
    public class TravelersController(AppDbContext dbContext) : BaseApiController
    {
        private const string UserIdHeaderName = "X-User-Id";
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly string[] AllowedTypes = ["Adult", "Child", "Infant"];
        private static readonly string[] AllowedTitles = ["Mr", "Mrs", "Ms"];
        private static readonly string[] AllowedGenders = ["Male", "Female", "Other"];
        private static readonly Regex NameRegex = new(@"^[A-Za-z\s\-']+$", RegexOptions.Compiled);
        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
        private static readonly Regex PhoneRegex = new(@"^\+?[1-9]\d{6,14}$", RegexOptions.Compiled);
        private static readonly Regex PassportRegex = new(@"^[A-Za-z0-9]{6,20}$", RegexOptions.Compiled);

        [HttpGet]
        public async Task<IActionResult> GetTravelers(
            [FromQuery] string? ids,
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

            var idList = ParseIds(ids);
            if (idList.Count > 0)
            {
                travelersQuery = travelersQuery.Where(x => idList.Contains(x.Id));
            }

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

        [HttpGet("{travelerId}")]
        public async Task<IActionResult> GetTravelerById(string travelerId)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            var idList = ParseIds(travelerId);
            if (idList.Count == 0)
            {
                return BadRequest("Invalid traveler ID.");
            }

            var travelers = await dbContext.Travelers
                .AsNoTracking()
                .Where(x => idList.Contains(x.Id) && x.UserId == userId)
                .ToListAsync();

            if (travelers.Count == 0)
            {
                return NotFound("Traveler(s) not found.");
            }

            if (idList.Count == 1)
            {
                return Ok(MapTraveler(travelers[0]));
            }

            return Ok(travelers.Select(MapTraveler));
        }

        [HttpPost]
        public async Task<IActionResult> CreateTraveler([FromBody] JsonElement payload)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            var requests = new List<UpsertTravelerRequestDto>();
            bool isArray = payload.ValueKind == JsonValueKind.Array;

            if (isArray)
            {
                requests = JsonSerializer.Deserialize<List<UpsertTravelerRequestDto>>(payload.GetRawText(), JsonOptions) ?? [];
            }
            else if (payload.ValueKind == JsonValueKind.Object)
            {
                var single = JsonSerializer.Deserialize<UpsertTravelerRequestDto>(payload.GetRawText(), JsonOptions);
                if (single is not null)
                {
                    requests.Add(single);
                }
            }
            else
            {
                return BadRequest("Payload must be a traveler object or an array of traveler objects.");
            }

            if (requests.Count == 0)
            {
                return BadRequest("At least one traveler must be provided.");
            }

            var validationErrors = new List<string>();
            var entitiesToInsert = new List<Traveler>();
            var utcNow = DateTime.UtcNow;

            for (int i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                var validationError = ValidateTraveler(
                    req,
                    out var normalizedType,
                    out var normalizedTitle,
                    out var normalizedGender);

                if (validationError is not null)
                {
                    validationErrors.Add(isArray ? $"Item [{i}]: {validationError}" : validationError);
                    continue;
                }

                entitiesToInsert.Add(new Traveler
                {
                    UserId = userId!,
                    Type = normalizedType!,
                    Title = normalizedTitle!,
                    FirstName = (req.FirstName ?? string.Empty).Trim(),
                    LastName = (req.LastName ?? string.Empty).Trim(),
                    Gender = normalizedGender!,
                    Age = req.Age,
                    Email = (req.Email ?? string.Empty).Trim(),
                    PhoneNo = (req.PhoneNo ?? string.Empty).Trim(),
                    PassportNo = string.IsNullOrWhiteSpace(req.PassportNo) ? null : req.PassportNo.Trim().ToUpperInvariant(),
                    Country = (req.Country ?? string.Empty).Trim(),
                    CreatedAtUtc = utcNow,
                    UpdatedAtUtc = utcNow
                });
            }

            if (validationErrors.Count > 0)
            {
                return BadRequest(isArray ? (object)new { errors = validationErrors } : validationErrors[0]);
            }

            dbContext.Travelers.AddRange(entitiesToInsert);
            await dbContext.SaveChangesAsync();

            if (isArray)
            {
                return Ok(entitiesToInsert.Select(MapTraveler));
            }

            var created = entitiesToInsert[0];
            return CreatedAtAction(nameof(GetTravelerById), new { travelerId = created.Id.ToString() }, MapTraveler(created));
        }

        [HttpPut("{travelerId}")]
        public async Task<IActionResult> UpdateTraveler(string travelerId, [FromBody] JsonElement payload)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            var routeIds = ParseIds(travelerId);
            var updateItems = new List<UpdateTravelerItemDto>();
            bool isArray = payload.ValueKind == JsonValueKind.Array;

            if (isArray)
            {
                updateItems = JsonSerializer.Deserialize<List<UpdateTravelerItemDto>>(payload.GetRawText(), JsonOptions) ?? [];
                for (int i = 0; i < updateItems.Count; i++)
                {
                    if (!updateItems[i].Id.HasValue && i < routeIds.Count)
                    {
                        updateItems[i].Id = routeIds[i];
                    }
                }
            }
            else if (payload.ValueKind == JsonValueKind.Object)
            {
                var single = JsonSerializer.Deserialize<UpdateTravelerItemDto>(payload.GetRawText(), JsonOptions);
                if (single is not null)
                {
                    if (!single.Id.HasValue && routeIds.Count > 0)
                    {
                        single.Id = routeIds[0];
                    }
                    updateItems.Add(single);
                }
            }
            else
            {
                return BadRequest("Payload must be a traveler object or an array of traveler objects.");
            }

            if (updateItems.Count == 0)
            {
                return BadRequest("No traveler updates provided.");
            }

            var targetIds = updateItems.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).Distinct().ToList();
            if (targetIds.Count == 0)
            {
                return BadRequest("Traveler ID must be provided in route or payload.");
            }

            var existingTravelers = await dbContext.Travelers
                .Where(x => targetIds.Contains(x.Id) && x.UserId == userId)
                .ToDictionaryAsync(x => x.Id);

            var errors = new List<string>();
            var updatedEntities = new List<Traveler>();

            foreach (var item in updateItems)
            {
                if (!item.Id.HasValue || !existingTravelers.TryGetValue(item.Id.Value, out var traveler))
                {
                    errors.Add($"Traveler ID {item.Id} not found.");
                    continue;
                }

                var validationError = ValidateTraveler(
                    item,
                    out var normalizedType,
                    out var normalizedTitle,
                    out var normalizedGender);

                if (validationError is not null)
                {
                    errors.Add(isArray ? $"Traveler ID {item.Id}: {validationError}" : validationError);
                    continue;
                }

                traveler.Type = normalizedType!;
                traveler.Title = normalizedTitle!;
                traveler.FirstName = (item.FirstName ?? string.Empty).Trim();
                traveler.LastName = (item.LastName ?? string.Empty).Trim();
                traveler.Gender = normalizedGender!;
                traveler.Age = item.Age;
                traveler.Email = (item.Email ?? string.Empty).Trim();
                traveler.PhoneNo = (item.PhoneNo ?? string.Empty).Trim();
                traveler.PassportNo = string.IsNullOrWhiteSpace(item.PassportNo) ? null : item.PassportNo.Trim().ToUpperInvariant();
                traveler.Country = (item.Country ?? string.Empty).Trim();
                traveler.UpdatedAtUtc = DateTime.UtcNow;

                updatedEntities.Add(traveler);
            }

            if (errors.Count > 0)
            {
                if (!isArray)
                {
                    if (existingTravelers.Count == 0)
                    {
                        return NotFound("Traveler not found.");
                    }
                    return BadRequest(errors[0]);
                }
                return BadRequest(new { errors });
            }

            await dbContext.SaveChangesAsync();

            if (isArray)
            {
                return Ok(updatedEntities.Select(MapTraveler));
            }

            return Ok(MapTraveler(updatedEntities[0]));
        }

        [HttpDelete("{travelerId}")]
        public async Task<IActionResult> DeleteTraveler(string travelerId)
        {
            if (!TryGetCurrentUserId(out var userId, out var userIdError))
            {
                return BadRequest(userIdError);
            }

            var idList = ParseIds(travelerId);
            if (idList.Count == 0)
            {
                return BadRequest("No valid traveler ID(s) provided.");
            }

            var travelers = await dbContext.Travelers
                .Where(x => idList.Contains(x.Id) && x.UserId == userId)
                .ToListAsync();

            if (travelers.Count == 0)
            {
                return NotFound("Traveler(s) not found.");
            }

            dbContext.Travelers.RemoveRange(travelers);
            await dbContext.SaveChangesAsync();

            if (idList.Count == 1)
            {
                return Ok(new { message = "Traveler deleted successfully." });
            }

            return Ok(new
            {
                message = $"{travelers.Count} traveler(s) deleted successfully.",
                deletedIds = travelers.Select(x => x.Id).ToList()
            });
        }

        private static List<int> ParseIds(string? rawIds)
        {
            if (string.IsNullOrWhiteSpace(rawIds))
            {
                return [];
            }

            return rawIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
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
            out string? normalizedGender)
        {
            normalizedTitle = ResolveAllowedValue(request.Title, AllowedTitles);
            normalizedGender = ResolveAllowedValue(request.Gender, AllowedGenders);

            // 1. Mandatory Title
            if (normalizedTitle is null)
            {
                normalizedType = null;
                return $"Invalid title. Allowed values: {string.Join(", ", AllowedTitles)}.";
            }

            // 2. Mandatory Gender
            if (normalizedGender is null)
            {
                normalizedType = null;
                return $"Invalid gender. Allowed values: {string.Join(", ", AllowedGenders)}.";
            }

            // 3. Mandatory FirstName
            if (string.IsNullOrWhiteSpace(request.FirstName))
            {
                normalizedType = null;
                return "FirstName is required.";
            }

            var firstName = request.FirstName.Trim();
            if (firstName.Length > 80)
            {
                normalizedType = null;
                return "FirstName cannot exceed 80 characters.";
            }

            if (!NameRegex.IsMatch(firstName))
            {
                normalizedType = null;
                return "FirstName can only contain letters, spaces, or hyphens.";
            }

            // 4. Mandatory Age
            if (request.Age < 0 || request.Age > 120)
            {
                normalizedType = null;
                return "Age must be between 0 and 120.";
            }

            var age = request.Age;

            // 5. Optional Type (validated only when provided, otherwise auto-inferred from age)
            if (!string.IsNullOrWhiteSpace(request.Type))
            {
                normalizedType = ResolveAllowedValue(request.Type, AllowedTypes);
                if (normalizedType is null)
                {
                    return $"Invalid type. Allowed values: {string.Join(", ", AllowedTypes)}.";
                }

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
            }
            else
            {
                normalizedType = age >= 12 ? "Adult" : (age >= 2 ? "Child" : "Infant");
            }

            // 6. Optional LastName (validated only when provided)
            if (!string.IsNullOrWhiteSpace(request.LastName))
            {
                var lastName = request.LastName.Trim();
                if (lastName.Length > 80)
                {
                    return "LastName cannot exceed 80 characters.";
                }

                if (!NameRegex.IsMatch(lastName))
                {
                    return "LastName can only contain letters, spaces, or hyphens.";
                }
            }

            // 7. Optional Country (validated only when provided)
            if (!string.IsNullOrWhiteSpace(request.Country))
            {
                var country = request.Country.Trim();
                if (country.Length > 80)
                {
                    return "Country cannot exceed 80 characters.";
                }
            }

            // 8. Optional Email (validated only when provided)
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var email = request.Email.Trim();
                if (email.Length > 150)
                {
                    return "Email cannot exceed 150 characters.";
                }

                if (!EmailRegex.IsMatch(email))
                {
                    return "Email format is invalid.";
                }
            }

            // 9. Optional PhoneNo (validated only when provided)
            if (!string.IsNullOrWhiteSpace(request.PhoneNo))
            {
                var rawPhone = request.PhoneNo.Trim();
                if (rawPhone.Length > 30)
                {
                    return "Phone number cannot exceed 30 characters.";
                }

                var cleanPhone = Regex.Replace(rawPhone, @"[\s\-]", string.Empty);
                if (!PhoneRegex.IsMatch(cleanPhone))
                {
                    return "Invalid phone number format (7 to 15 digits with optional country code).";
                }
            }

            // 10. Optional PassportNo (validated only when provided)
            if (!string.IsNullOrWhiteSpace(request.PassportNo))
            {
                var passport = request.PassportNo.Trim();
                if (passport.Length > 40)
                {
                    return "Passport number cannot exceed 40 characters.";
                }

                if (!PassportRegex.IsMatch(passport))
                {
                    return "Passport number must be 6 to 20 alphanumeric characters.";
                }
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
