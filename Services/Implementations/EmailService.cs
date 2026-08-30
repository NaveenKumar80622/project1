using Microsoft.Extensions.Options;
using PickNBook.Api.Models;
using System.Net;
using System.Net.Mail;

namespace PickNBook.Api.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            await SendEmailWithAttachmentsAsync(toEmail, subject, body, []);
        }

        public async Task SendEmailWithAttachmentsAsync(
            string toEmail,
            string subject,
            string body,
            IEnumerable<EmailAttachment> attachments)
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            message.To.Add(toEmail);

            var attachmentStreams = new List<MemoryStream>();
            try
            {
                foreach (var item in attachments)
                {
                    var stream = new MemoryStream(item.Content);
                    attachmentStreams.Add(stream);

                    var attachment = new Attachment(stream, item.FileName, item.ContentType);
                    message.Attachments.Add(attachment);
                }

                using var smtp = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials = new NetworkCredential(_settings.Username, _settings.Password),
                    EnableSsl = true
                };

                await smtp.SendMailAsync(message);
            }
            finally
            {
                foreach (var stream in attachmentStreams)
                {
                    stream.Dispose();
                }
            }
        }
    }
}
