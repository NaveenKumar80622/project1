using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class SrdvMasterWalletBalanceResponseDto
    {
        [JsonPropertyName("Error")]
        public SrdvWalletErrorDto? Error { get; set; }

        [JsonPropertyName("CurrencyCode")]
        public string? CurrencyCode { get; set; }

        [JsonPropertyName("Balance")]
        public decimal? Balance { get; set; }

        [JsonPropertyName("CreditLimit")]
        public decimal? CreditLimit { get; set; }

        [JsonPropertyName("HeldAmount")]
        public decimal? HeldAmount { get; set; }

        [JsonPropertyName("AvailableBalance")]
        public decimal? AvailableBalance { get; set; }

        [JsonIgnore]
        public bool IsSuccess => Error == null || Error.ErrorCode == 0;
    }

    public class SrdvWalletErrorDto
    {
        [JsonPropertyName("ErrorCode")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("ErrorMessage")]
        public string? ErrorMessage { get; set; }
    }
}
