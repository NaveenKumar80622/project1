namespace PickNBook.Api.Models
{
    public class SmsSettings
    {
        public string Provider { get; set; } = "Twilio"; // "Twilio" or "Msg91"
        public bool Enabled { get; set; } = false;
        public TwilioSettings? Twilio { get; set; }
        public Msg91Settings? Msg91 { get; set; }
    }

    public class TwilioSettings
    {
        public string AccountSid { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
        public string FromPhoneNumber { get; set; } = string.Empty;
    }

    public class Msg91Settings
    {
        public string AuthKey { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
    }
}
