using Microsoft.Extensions.Options;
using PickNBook.Api.Models;
using System.Net;
using System.Net.Mail;

namespace PickNBook.Api.Services
{
    public class EmailService : IEmailService, PickNBook.Api.Services.Notifications.Interfaces.IEmailProvider
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task<(bool IsSuccess, string? ProviderMessageId, string? ErrorMessage)> SendAsync(string recipient, string content, string? subject = null)
        {
            try
            {
                await SendEmailAsync(recipient, subject ?? "Pick&book Notification", content);
                return (true, System.Guid.NewGuid().ToString(), null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body, string? replyToEmail = null)
        {
            await SendEmailWithAttachmentsAsync(toEmail, subject, body, [], replyToEmail);
        }

        public async Task SendEmailWithAttachmentsAsync(
            string toEmail,
            string subject,
            string body,
            IEnumerable<EmailAttachment> attachments,
            string? replyToEmail = null)
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                BodyEncoding = System.Text.Encoding.UTF8,
                SubjectEncoding = System.Text.Encoding.UTF8,
                HeadersEncoding = System.Text.Encoding.UTF8
            };

            message.To.Add(toEmail);

            if (!string.IsNullOrWhiteSpace(replyToEmail))
            {
                message.ReplyToList.Add(new MailAddress(replyToEmail));
            }

            var attachmentStreams = new List<MemoryStream>();
            try
            {
                var attachmentsList = attachments?.ToList() ?? new List<EmailAttachment>();
                var inlineList = attachmentsList.Where(a => a.IsInline || !string.IsNullOrWhiteSpace(a.ContentId)).ToList();
                var regularList = attachmentsList.Where(a => !a.IsInline && string.IsNullOrWhiteSpace(a.ContentId)).ToList();

                if (inlineList.Count > 0)
                {
                    var htmlView = AlternateView.CreateAlternateViewFromString(body, System.Text.Encoding.UTF8, "text/html");
                    foreach (var item in inlineList)
                    {
                        var stream = new MemoryStream(item.Content);
                        attachmentStreams.Add(stream);

                        var lr = new LinkedResource(stream, item.ContentType)
                        {
                            ContentId = item.ContentId?.Trim('<', '>'),
                            TransferEncoding = System.Net.Mime.TransferEncoding.Base64
                        };
                        if (!string.IsNullOrWhiteSpace(item.FileName))
                        {
                            lr.ContentType.Name = item.FileName;
                        }
                        htmlView.LinkedResources.Add(lr);
                    }
                    message.AlternateViews.Add(htmlView);
                }
                else
                {
                    message.Body = body;
                    message.IsBodyHtml = true;
                }

                foreach (var item in regularList)
                {
                    var stream = new MemoryStream(item.Content);
                    attachmentStreams.Add(stream);

                    var attachment = new Attachment(stream, item.FileName, item.ContentType);
                    message.Attachments.Add(attachment);
                }

                using var smtp = new SmtpClient(_settings.SmtpServer, _settings.Port)
                {
                    Credentials = new NetworkCredential(_settings.Username, _settings.Password?.Replace(" ", "")),
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
