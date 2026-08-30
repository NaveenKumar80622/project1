using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers.Public
{
    [Authorize(Roles = AuthRoles.Agent)]
    [ApiController]
    [Route("api/[controller]")]
    public class AgentPortalController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IFileStorageService _fileStorage;

        public AgentPortalController(AppDbContext context, IFileStorageService fileStorage)
        {
            _context = context;
            _fileStorage = fileStorage;
        }

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(idClaim) || !int.TryParse(idClaim, out var userId))
            {
                throw new UnauthorizedAccessException("User is not authenticated or user ID is invalid.");
            }
            return userId;
        }

        // ---------------- 1. LOGO UPLOAD ----------------
        [HttpPut("profile/logo")]
        public async Task<IActionResult> UploadLogo(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var userId = GetCurrentUserId();
            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (agent == null)
            {
                return NotFound("Agent profile not found.");
            }

            // Remove existing logo if any
            if (!string.IsNullOrWhiteSpace(agent.AgentLogoUrl))
            {
                _fileStorage.DeleteFile(agent.AgentLogoUrl);
            }

            // Save new logo
            var folder = "agent/logos";
            var relativePath = await _fileStorage.SaveFileAsync(file, folder);
            if (string.IsNullOrEmpty(relativePath))
            {
                return StatusCode(500, "Failed to save logo file.");
            }

            agent.AgentLogoUrl = relativePath;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Logo uploaded successfully.",
                logoUrl = relativePath
            });
        }

        // ---------------- 2. GET MARKUPS ----------------
        [HttpGet("markups")]
        public async Task<IActionResult> GetMarkups()
        {
            var userId = GetCurrentUserId();
            var settings = await _context.AgentMarkupSettings
                .Where(x => x.AgentId == userId)
                .ToListAsync();

            // Initialize default markup settings if empty
            if (settings.Count == 0)
            {
                var flightDefault = new AgentMarkupSetting
                {
                    AgentId = userId,
                    ServiceType = "Flight",
                    MarkupType = "Flat",
                    MarkupValue = 0m
                };
                var busDefault = new AgentMarkupSetting
                {
                    AgentId = userId,
                    ServiceType = "Bus",
                    MarkupType = "Flat",
                    MarkupValue = 0m
                };

                _context.AgentMarkupSettings.AddRange(flightDefault, busDefault);
                await _context.SaveChangesAsync();

                settings = new System.Collections.Generic.List<AgentMarkupSetting> { flightDefault, busDefault };
            }

            return Ok(settings.Select(x => new
            {
                x.ServiceType,
                x.MarkupType,
                x.MarkupValue,
                x.UpdatedAtUtc
            }));
        }

        // ---------------- 3. UPDATE MARKUPS ----------------
        [HttpPut("markups")]
        public async Task<IActionResult> UpdateMarkup([FromBody] UpdateAgentMarkupDto request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetCurrentUserId();
            var service = request.ServiceType.Trim();
            var type = request.MarkupType.Trim();

            if (!string.Equals(service, "Flight", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(service, "Bus", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Invalid service type. Must be 'Flight' or 'Bus'.");
            }

            if (!string.Equals(type, "Flat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "Percentage", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Invalid markup type. Must be 'Flat' or 'Percentage'.");
            }

            var setting = await _context.AgentMarkupSettings
                .FirstOrDefaultAsync(x => x.AgentId == userId && x.ServiceType == service);

            if (setting == null)
            {
                setting = new AgentMarkupSetting
                {
                    AgentId = userId,
                    ServiceType = service
                };
                _context.AgentMarkupSettings.Add(setting);
            }

            setting.MarkupType = type;
            setting.MarkupValue = request.MarkupValue;
            setting.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"{service} markup updated successfully.",
                serviceType = service,
                markupType = type,
                markupValue = request.MarkupValue
            });
        }

        // ---------------- 4. GET LEDGER STATEMENT ----------------
        [HttpGet("ledger")]
        public async Task<IActionResult> GetLedger([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        {
            var userId = GetCurrentUserId();
            var query = _context.AgentLedgerEntries
                .Where(x => x.AgentId == userId);

            if (fromDate.HasValue)
            {
                query = query.Where(x => x.CreatedAtUtc >= fromDate.Value.Date);
            }
            if (toDate.HasValue)
            {
                // Include the whole day of toDate
                var endOfDate = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(x => x.CreatedAtUtc <= endOfDate);
            }

            var ledger = await query
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            return Ok(ledger.Select(x => new
            {
                x.Id,
                x.TransactionType,
                x.ReferenceId,
                x.DebitAmount,
                x.CreditAmount,
                x.RunningBalance,
                x.Description,
                x.CreatedAtUtc
            }));
        }

        // ---------------- 5. SUBMIT DEPOSIT REQUEST ----------------
        [HttpPost("deposits")]
        public async Task<IActionResult> SubmitDeposit([FromBody] SubmitAgentDepositDto request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (request.Amount <= 0)
            {
                return BadRequest("Deposit amount must be greater than zero.");
            }

            var userId = GetCurrentUserId();
            var deposit = new DepositRequest
            {
                UserId = userId,
                Amount = request.Amount,
                Type = request.Type,
                Status = "Pending",
                UserRemark = request.UserRemark?.Trim(),
                EntryDateUtc = DateTime.UtcNow,
                TransactionDate = request.TransactionDate ?? DateTime.UtcNow
            };

            _context.DepositRequests.Add(deposit);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Deposit request submitted successfully. Awaiting Admin verification.",
                depositId = deposit.Id,
                amount = deposit.Amount,
                status = deposit.Status
            });
        }

        // ---------------- 6. GET BOOKINGS REPORT ----------------
        [HttpGet("bookings")]
        public async Task<IActionResult> GetBookings([FromQuery] string? serviceType, [FromQuery] string? status, [FromQuery] bool export = false)
        {
            var userId = GetCurrentUserId().ToString();

            var flightBookingsQuery = _context.FlightReservations
                .Where(x => x.UserId == userId)
                .AsNoTracking();

            var busBookingsQuery = _context.BusReservations
                .Where(x => x.UserId == userId)
                .AsNoTracking();

            // Apply status filters
            if (!string.IsNullOrWhiteSpace(status))
            {
                flightBookingsQuery = flightBookingsQuery.Where(x => x.Status == status);
                busBookingsQuery = busBookingsQuery.Where(x => x.Status == status);
            }

            var flightList = await flightBookingsQuery.ToListAsync();
            var busList = await busBookingsQuery.ToListAsync();

            var combinedList = flightList.Select(x => new AgentBookingReportDto
            {
                BookingId = x.Id.ToString(),
                BookingReference = x.BookingReference,
                Pnr = x.Pnr,
                ServiceType = "Flight",
                PassengerName = x.PassengerName,
                Amount = x.FinalAmount > 0 ? x.FinalAmount : x.TotalPriceInr,
                Status = x.Status,
                BookedAt = x.BookedAtUtc
            }).Concat(busList.Select(x => new AgentBookingReportDto
            {
                BookingId = x.Id.ToString(),
                BookingReference = x.BookingReference,
                Pnr = x.Pnr,
                ServiceType = "Bus",
                PassengerName = x.PassengerName,
                Amount = x.TotalPriceInr,
                Status = x.Status,
                BookedAt = x.BookedAtUtc
            })).OrderByDescending(x => x.BookedAt).ToList();

            if (!string.IsNullOrWhiteSpace(serviceType))
            {
                combinedList = combinedList.Where(x => string.Equals(x.ServiceType, serviceType, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (export)
            {
                var csv = new StringBuilder();
                csv.AppendLine("Booking Reference,PNR,Service Type,Passenger Name,Amount,Status,Booked Date");

                foreach (var item in combinedList)
                {
                    csv.AppendLine($"\"{item.BookingReference}\",\"{item.Pnr}\",\"{item.ServiceType}\",\"{item.PassengerName}\",\"{item.Amount}\",\"{item.Status}\",\"{item.BookedAt:yyyy-MM-dd HH:mm:ss}\"");
                }

                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", $"B2B_Bookings_Report_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
            }

            return Ok(combinedList);
        }

        // ---------------- 7. GET AGENT PROFILE ----------------
        [HttpGet("profile")]
        [HttpGet("me")]
        [HttpGet("account")]
        public async Task<IActionResult> GetAgentProfile()
        {
            var userId = GetCurrentUserId();
            var agent = await _context.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (agent == null)
            {
                return NotFound("Agent profile not found.");
            }

            return Ok(new
            {
                userId = agent.Id.ToString(),
                agencyName = agent.CompanyName ?? "",
                name = $"{agent.FirstName} {agent.LastName}".Trim(),
                contactName = $"{agent.FirstName} {agent.LastName}".Trim(),
                email = agent.Email,
                phone = agent.PhoneNumber,
                city = agent.City ?? "",
                walletBalance = agent.WalletBalance,
                logoUrl = agent.AgentLogoUrl ?? ""
            });
        }
    }

    public class UpdateAgentMarkupDto
    {
        public string ServiceType { get; set; } = string.Empty;
        public string MarkupType { get; set; } = string.Empty;
        public decimal MarkupValue { get; set; }
    }

    public class SubmitAgentDepositDto
    {
        public decimal Amount { get; set; }
        public string Type { get; set; } = "NEFT";
        public string? UserRemark { get; set; }
        public DateTime? TransactionDate { get; set; }
    }

    public class AgentBookingReportDto
    {
        public string BookingId { get; set; } = string.Empty;
        public string BookingReference { get; set; } = string.Empty;
        public string Pnr { get; set; } = string.Empty;
        public string ServiceType { get; set; } = string.Empty;
        public string PassengerName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime BookedAt { get; set; }
    }
}
