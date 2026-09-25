namespace PickNBook.Api.Models.Config
{
    public class SrdvSettings
    {
        public string FlightBaseUrl { get; set; } = string.Empty;
        public string HotelBaseUrl { get; set; } = string.Empty;
        public string BusBaseUrl { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ApiToken { get; set; } = string.Empty;
        
        public string BusClientId { get; set; } = string.Empty;
        public string BusUserName { get; set; } = string.Empty;
        public string BusPassword { get; set; } = string.Empty;
        public string BusApiToken { get; set; } = string.Empty;
    }
}
