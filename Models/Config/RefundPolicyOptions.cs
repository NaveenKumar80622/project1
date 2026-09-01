namespace PickNBook.Api.Models.Config
{
    public class RefundPolicyOptions
    {
        public bool RefundMarkup { get; set; } = false;
        public bool RefundConvenienceFee { get; set; } = false;
        public bool RefundCoupon { get; set; } = false;
    }
}
