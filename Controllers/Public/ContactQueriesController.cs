using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    public class ContactQueriesController : BaseApiController
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;

        public ContactQueriesController(AppDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // POST: api/contactqueries
        [HttpPost]
        public async Task<IActionResult> SubmitQuery([FromBody] CreateContactQueryRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest("Email is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Subject))
            {
                return BadRequest("Subject is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest("Message is required.");
            }

            var query = new ContactQuery
            {
                Name = request.Name.Trim(),
                Email = request.Email.Trim(),
                PhoneNo = request.PhoneNo?.Trim(),
                Subject = request.Subject.Trim(),
                Message = request.Message.Trim(),
                Status = "Pending",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _context.ContactQueries.Add(query);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Query submitted successfully.",
                queryId = query.Id
            });
        }

        // GET: api/contactqueries/admin/list
        [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
        [HttpGet("admin/list")]
        public async Task<IActionResult> GetAdminQueries()
        {
            var queries = await _context.ContactQueries
                .AsNoTracking()
                .OrderByDescending(q => q.CreatedAtUtc)
                .ToListAsync();

            return Ok(queries);
        }

        // PUT: api/contactqueries/admin/{id}/status
        [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
        [HttpPut("admin/{id:long}/status")]
        public async Task<IActionResult> UpdateQueryStatus(long id, [FromBody] UpdateContactQueryStatusRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Status))
            {
                return BadRequest("Status is required.");
            }

            var query = await _context.ContactQueries.FirstOrDefaultAsync(q => q.Id == id);
            if (query == null)
            {
                return NotFound("Query not found.");
            }

            query.Status = request.Status.Trim();
            query.ReplyMessage = request.ReplyMessage?.Trim();
            query.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if ((query.Status == "Resolved" || query.Status == "Replied") && !string.IsNullOrWhiteSpace(query.ReplyMessage))
            {
                var subject = $"[{query.Status}] Support Ticket #{query.Id} - {query.Subject}";
                var body = $@"Hello {query.Name},

The support team has reviewed and updated your query.

---
Query Details:
- Ticket ID: #{query.Id}
- Subject: {query.Subject}
Your Original Message:
""{query.Message}""

Resolution Status: {query.Status}

Our Update / Reply:
""{query.ReplyMessage}""
---

If you have any further questions or if the problem persists, please reply to this email.

Best regards,
Support & Resolutions Team";

                await _emailService.SendEmailAsync(query.Email, subject, body);
            }

            return Ok(new
            {
                success = true,
                message = "Query updated and reply sent successfully.",
                data = query
            });
        }

        // DELETE: api/contactqueries/admin/{id}
        [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
        [HttpDelete("admin/{id:long}")]
        public async Task<IActionResult> DeleteQuery(long id)
        {
            var query = await _context.ContactQueries.FirstOrDefaultAsync(q => q.Id == id);
            if (query == null)
            {
                return NotFound("Query not found.");
            }

            _context.ContactQueries.Remove(query);
            await _context.SaveChangesAsync();

            return Ok("Query deleted successfully.");
        }
    }
}
