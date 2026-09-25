using System;

namespace PickNBook.Api.Models.DTOs
{
    public class AgentResponseDto
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string BusinessType { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Gstin { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string WalletStatus { get; set; } = string.Empty;
        public decimal WalletBalance { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
