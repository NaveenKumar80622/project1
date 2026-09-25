namespace PickNBook.Api.Models.Options
{
    public class AdminNotificationOptions
    {
        public decimal RevenueMilestone { get; set; } = 100000m;
        public decimal HighDiscountThreshold { get; set; } = 500m;
        public decimal HighDiscountThresholdPercentage { get; set; } = 50m;
    }
}
