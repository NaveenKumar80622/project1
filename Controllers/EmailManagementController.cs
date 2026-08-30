using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services;
using PickNBook.Api.Services.Interfaces;
using System.Text.Json;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    [Route("api/v1/admin/email")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public class EmailManagementController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailTemplateService _templateService;
        private readonly ISecurityService _securityService;

        public EmailManagementController(
            AppDbContext context,
            IEmailTemplateService templateService,
            ISecurityService securityService)
        {
            _context = context;
            _templateService = templateService;
            _securityService = securityService;
        }

        // ================= TEMPLATES =================

        [HttpGet("templates")]
        public async Task<IActionResult> GetTemplates()
        {
            var templates = await _context.EmailTemplates.OrderByDescending(t => t.Id).ToListAsync();
            return Ok(new { success = true, data = templates });
        }

        [HttpGet("templates/{id}")]
        public async Task<IActionResult> GetTemplate(int id)
        {
            var template = await _context.EmailTemplates.FindAsync(id);
            if (template == null) return NotFound(new { success = false, message = "Template not found." });
            return Ok(new { success = true, data = template });
        }

        [HttpPost("templates")]
        public async Task<IActionResult> CreateTemplate([FromBody] EmailTemplate req)
        {
            if (string.IsNullOrWhiteSpace(req.TemplateKey) || string.IsNullOrWhiteSpace(req.Subject) || string.IsNullOrWhiteSpace(req.Body))
                return BadRequest(new { success = false, message = "TemplateKey, Subject, and Body are required." });

            req.CreatedBy = User.Identity?.Name;
            req.CreatedAt = DateTime.UtcNow;

            _context.EmailTemplates.Add(req);
            await _context.SaveChangesAsync();

            await _securityService.LogAuditAsync("EMAIL_TEMPLATE_CREATED", "Create Email Template", "Success", "", reason: $"Template ID: {req.Id}");

            return Ok(new { success = true, data = req });
        }

        [HttpPut("templates/{id}")]
        public async Task<IActionResult> UpdateTemplate(int id, [FromBody] EmailTemplate req)
        {
            var template = await _context.EmailTemplates.FindAsync(id);
            if (template == null) return NotFound();

            template.TemplateName = req.TemplateName;
            template.TemplateKey = req.TemplateKey;
            template.Subject = req.Subject;
            template.Body = req.Body;
            template.BodyFormat = req.BodyFormat;
            template.IncludeLoginLink = req.IncludeLoginLink;
            template.LoginButtonText = req.LoginButtonText;
            template.IsActive = req.IsActive;
            template.UpdatedBy = User.Identity?.Name;
            template.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _securityService.LogAuditAsync("EMAIL_TEMPLATE_UPDATED", "Update Email Template", "Success", "", reason: $"Template ID: {template.Id}");

            return Ok(new { success = true, data = template });
        }

        [HttpDelete("templates/{id}")]
        public async Task<IActionResult> DeleteTemplate(int id)
        {
            var template = await _context.EmailTemplates.FindAsync(id);
            if (template == null) return NotFound();

            // Soft delete
            template.IsActive = false;
            template.UpdatedBy = User.Identity?.Name;
            template.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _securityService.LogAuditAsync("EMAIL_TEMPLATE_DEACTIVATED", "Deactivate Email Template", "Success", "", reason: $"Template ID: {template.Id}");

            return Ok(new { success = true, message = "Template deactivated successfully." });
        }

        // ================= MANUAL & TEST =================

        public class ManualEmailRequest
        {
            public string RecipientEmail { get; set; } = string.Empty;
            public int TemplateId { get; set; }
            public string? Subject { get; set; }
            public string? Message { get; set; }
            public bool IncludeLoginLink { get; set; }
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendManualEmail([FromBody] ManualEmailRequest req)
        {
            await _templateService.SendManualEmailAsync(req.RecipientEmail, req.TemplateId, req.Subject, req.Message, req.IncludeLoginLink);
            
            await _securityService.LogAuditAsync("MANUAL_EMAIL_SENT", "Send Manual Email", "Success", "", email: req.RecipientEmail, reason: $"Template ID: {req.TemplateId}");

            return Ok(new { success = true, message = "Email queued for sending." });
        }

        public class TestEmailRequest
        {
            public string RecipientEmail { get; set; } = string.Empty;
            public string TemplateKey { get; set; } = string.Empty;
        }

        [HttpPost("test")]
        public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest req)
        {
            var testData = new
            {
                UserName = "Test Admin",
                Email = req.RecipientEmail,
                IpAddress = "192.168.1.100",
                Reason = "This is a test notification.",
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Time = DateTime.UtcNow.ToString("HH:mm:ss"),
                Status = "Active",
                ApplicationName = "PickNBook Admin"
            };

            await _templateService.SendTemplatedEmailAsync(req.RecipientEmail, req.TemplateKey, testData);

            await _securityService.LogAuditAsync("TEST_EMAIL_SENT", "Send Test Email", "Success", "", email: req.RecipientEmail, reason: $"Template Key: {req.TemplateKey}");

            return Ok(new { success = true, message = "Test email sent." });
        }

        // ================= REMINDERS =================

        [HttpGet("reminders")]
        public async Task<IActionResult> GetReminders()
        {
            var reminders = await _context.EmailReminders.OrderByDescending(r => r.Id).ToListAsync();
            return Ok(new { success = true, data = reminders });
        }

        [HttpGet("reminders/{id}")]
        public async Task<IActionResult> GetReminder(int id)
        {
            var reminder = await _context.EmailReminders.FindAsync(id);
            if (reminder == null) return NotFound();
            return Ok(new { success = true, data = reminder });
        }

        [HttpPost("reminders")]
        public async Task<IActionResult> CreateReminder([FromBody] EmailReminder req)
        {
            req.CreatedBy = User.Identity?.Name;
            req.CreatedAt = DateTime.UtcNow;
            req.Status = "Pending";

            _context.EmailReminders.Add(req);
            await _context.SaveChangesAsync();

            await _securityService.LogAuditAsync("REMINDER_CREATED", "Create Email Reminder", "Success", "", email: req.RecipientEmail, reason: $"Reminder ID: {req.Id}");

            return Ok(new { success = true, data = req });
        }

        [HttpPut("reminders/{id}")]
        public async Task<IActionResult> UpdateReminder(int id, [FromBody] EmailReminder req)
        {
            var reminder = await _context.EmailReminders.FindAsync(id);
            if (reminder == null) return NotFound();

            reminder.ReminderName = req.ReminderName;
            reminder.RecipientEmail = req.RecipientEmail;
            reminder.TemplateId = req.TemplateId;
            reminder.Subject = req.Subject;
            reminder.Message = req.Message;
            reminder.IncludeLoginLink = req.IncludeLoginLink;
            reminder.ScheduledTime = req.ScheduledTime;
            reminder.Status = req.Status;
            reminder.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _securityService.LogAuditAsync("REMINDER_UPDATED", "Update Email Reminder", "Success", "", reason: $"Reminder ID: {reminder.Id}");

            return Ok(new { success = true, data = reminder });
        }

        [HttpPost("reminders/{id}/cancel")]
        public async Task<IActionResult> CancelReminder(int id)
        {
            var reminder = await _context.EmailReminders.FindAsync(id);
            if (reminder == null) return NotFound();

            reminder.Status = "Cancelled";
            reminder.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _securityService.LogAuditAsync("REMINDER_CANCELLED", "Cancel Email Reminder", "Success", "", reason: $"Reminder ID: {reminder.Id}");

            return Ok(new { success = true, message = "Reminder cancelled successfully." });
        }

        [HttpDelete("reminders/{id}")]
        public async Task<IActionResult> DeleteReminder(int id)
        {
            var reminder = await _context.EmailReminders.FindAsync(id);
            if (reminder == null) return NotFound();

            _context.EmailReminders.Remove(reminder);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        // ================= HISTORY =================

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var history = await _context.EmailHistory.OrderByDescending(h => h.Id).Take(100).ToListAsync();
            return Ok(new { success = true, data = history });
        }
    }
}
