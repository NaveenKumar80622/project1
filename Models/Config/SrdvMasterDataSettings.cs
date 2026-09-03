namespace PickNBook.Api.Models.Config
{
    public class SrdvMasterDataSettings
    {
        public string BusResourceUrl { get; set; } = "https://www.srdvtechnologies.com/document/bus/v9/download-city-code-list-mysql";
        public string HotelSpecialResourceUrl { get; set; } = "https://www.srdvtechnologies.com/document/hotel/v8/download_city_code_list_mysql";
        public string HotelInternationalResourceUrl { get; set; } = "https://www.srdvtechnologies.com/document/hotel/v8/download_city_code_list_international_mysql";
        public string FlightAirportResourceUrl { get; set; } = "https://www.srdvtechnologies.com/document/flight/v8/download_airport_list_mysql";
        public string FlightAirlineResourceUrl { get; set; } = "https://www.srdvtechnologies.com/document/flight/v8/download_airline_list_mysql";
        public string DownloadDirectory { get; set; } = "Temp/SrdvStaging";
        public int CandidateLookupLimit { get; set; } = 150;
        public int CacheExpirationMinutes { get; set; } = 60;
    }
}
