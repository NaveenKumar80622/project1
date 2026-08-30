namespace PickNBook.Api.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body);
        Task SendEmailWithAttachmentsAsync(
            string toEmail,
            string subject,
            string body,
            IEnumerable<EmailAttachment> attachments);
    }

    public sealed class EmailAttachment
    {
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
        public required byte[] Content { get; init; }
    }
}
