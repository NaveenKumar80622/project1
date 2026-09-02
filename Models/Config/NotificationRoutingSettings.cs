namespace PickNBook.Api.Models.Config
{
    public class NotificationRoutingSettings
    {
        public Dictionary<string, string> SmsProviderRoutes { get; set; } = new Dictionary<string, string>();
        public string LoginOtpAppName { get; set; } = "ShyamAgro";
    }
}
