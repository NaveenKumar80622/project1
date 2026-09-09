using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public class RefundRouteContext
    {
        public int UserId { get; set; }
        public string BookingType { get; set; } = string.Empty; // "Bus", "Hotel", "Flight"
        public string BookingReference { get; set; } = string.Empty;
        public decimal RefundAmount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty; // "Wallet", "Cashfree", etc.
        public string? CashfreeOrderId { get; set; }
        public string? RefundPreference { get; set; } // "WALLET" or "ORIGINAL_PAYMENT_METHOD"
        public string? Reason { get; set; }
    }

    public interface IRefundRouterService
    {
        Task<CancellationRefundResultDto> RouteAsync(RefundRouteContext context);
    }
}
