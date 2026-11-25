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
        public bool ForcePanOptional { get; set; } = true;

        // Hotel v8 location discovery settings ("CityList" uses cities.sql + NearbyHotel; "AutoSuggest" uses CityAutoSuggest)
        public string HotelLocationMode { get; set; } = "CityList";
        public bool EnableNearbyHotel { get; set; } = true;
        public bool EnableCityAutoSuggest { get; set; } = false;
        public int AutoSuggestCacheMinutes { get; set; } = 30;
        public int NearbyHotelCacheMinutes { get; set; } = 15;
        public int NearbyHotelDefaultBatchSize { get; set; } = 200;
        public int NearbyHotelMaxConcurrentBatches { get; set; } = 2;
    }
}
