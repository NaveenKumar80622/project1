using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PickNBook.Api.Services.Implementations
{
    public class EmailTemplateService : IEmailTemplateService
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailTemplateService> _logger;

        public EmailTemplateService(
            AppDbContext context,
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<EmailTemplateService> logger)
        {
            _context = context;
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendSecurityEmailAsync(string templateKey, User user, string ipAddress, string reason)
        {
            var data = new
            {
                UserName = user.FirstName,
                Email = user.Email,
                IpAddress = ipAddress,
                Reason = reason,
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Time = DateTime.UtcNow.ToString("HH:mm:ss"),
                ApplicationName = "PickNBook"
            };

            await SendTemplatedEmailAsync(user.Email, templateKey, data);
        }

        public async Task SendTemplatedEmailAsync(string toEmail, string templateKey, object placeholderData, string? customSubject = null, string? customMessage = null)
        {
            var template = await _context.EmailTemplates.OrderBy(t => t.Id).FirstOrDefaultAsync(t => t.TemplateKey == templateKey && t.IsActive);
            if (template == null)
            {
                _logger.LogWarning($"Email template not found or inactive for key: {templateKey}");
                return;
            }

            await SendEmailInternalAsync(toEmail, template, placeholderData, customSubject, customMessage, template.IncludeLoginLink);
        }

        public async Task SendManualEmailAsync(string toEmail, int templateId, string? customSubject = null, string? customMessage = null, bool includeLoginLink = false)
        {
            var template = await _context.EmailTemplates.FindAsync(templateId);
            if (template == null)
            {
                _logger.LogWarning($"Email template not found for ID: {templateId}");
                return;
            }

            await SendEmailInternalAsync(toEmail, template, new { ApplicationName = "PickNBook" }, customSubject, customMessage, includeLoginLink);
        }

        private async Task SendEmailInternalAsync(string toEmail, EmailTemplate template, object placeholderData, string? customSubject, string? customMessage, bool includeLoginLink)
        {
            string subject = customSubject ?? template.Subject;
            string body = customMessage ?? template.Body;

            // Replace Placeholders
            if (placeholderData != null)
            {
                var properties = placeholderData.GetType().GetProperties();
                foreach (var prop in properties)
                {
                    var val = prop.GetValue(placeholderData)?.ToString() ?? "";
                    subject = subject.Replace($"{{{prop.Name}}}", val, StringComparison.OrdinalIgnoreCase);
                    body = body.Replace($"{{{prop.Name}}}", val, StringComparison.OrdinalIgnoreCase);
                }
            }

            // Handle Login Link
            if (includeLoginLink)
            {
                var loginUrl = _configuration["FrontendSettings:LoginUrl"] ?? "https://yourdomain.com/login";
                var buttonText = template.LoginButtonText ?? "Login to Your Account";
                var buttonHtml = $"<br/><br/><a href='{loginUrl}' style='display:inline-block;padding:10px 20px;background-color:#007bff;color:#ffffff;text-decoration:none;border-radius:5px;'>{buttonText}</a>";
                
                body = body.Replace("{LoginLink}", buttonHtml, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                body = body.Replace("{LoginLink}", "", StringComparison.OrdinalIgnoreCase);
            }

            // Format body
            if (template.BodyFormat?.ToLower() != "html")
            {
                body = body.Replace(Environment.NewLine, "<br/>");
            }

            var history = new EmailHistory
            {
                RecipientEmail = toEmail,
                TemplateId = template.Id,
                Subject = subject,
                EmailType = "Security",
                Status = "Pending"
            };
            _context.EmailHistory.Add(history);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendEmailAsync(toEmail, subject, body);
                history.Status = "Sent";
                history.DeliveryStatus = "SENT";
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Email sent successfully to {toEmail}");
            }
            catch (Exception ex)
            {
                history.Status = "Failed";
                history.ErrorMessage = ex.Message;
                await _context.SaveChangesAsync();
                _logger.LogError($"Failed to send email to {toEmail}: {ex.Message}");
            }
        }
    }
}
