namespace PickNBook.Api.Services.Interfaces
{
    public class HybridRefundSplit
    {
        public decimal WalletRefundAmount { get; set; }
        public decimal GatewayRefundAmount { get; set; }
    }

    public interface IHybridRefundSplitStrategy
    {
        HybridRefundSplit CalculateSplit(decimal refundableAmount, decimal walletPaidAmount, decimal gatewayPaidAmount, decimal totalPaidAmount);
    }
}
