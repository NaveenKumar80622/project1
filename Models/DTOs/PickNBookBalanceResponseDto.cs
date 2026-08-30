namespace PickNBook.Api.Models.DTOs
{
    public class PickNBookBalanceResponseDto
    {
        public decimal Balance { get; set; }
        public decimal CreditLimit { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
