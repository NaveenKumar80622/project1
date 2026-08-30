namespace PickNBook.Api.Models
{
    public class CashfreeSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiVersion { get; set; } = string.Empty;

        /// <summary>
        /// Server-side webhook URL override for production.
        /// When set, the server uses this instead of the frontend-provided NotifyUrl.
        /// Leave empty for sandbox testing (uses frontend's NotifyUrl).
        /// </summary>
        public string? WebhookUrl { get; set; }
    }
}
