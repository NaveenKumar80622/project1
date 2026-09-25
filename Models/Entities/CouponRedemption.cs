namespace PickNBook.Api.Models;

public class CouponRedemption
{
    public long Id { get; set; }
    public int FeaturedOfferId { get; set; }
    public string OfferCode { get; set; } = string.Empty;
    public string CouponCode { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalPrice { get; set; }
    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;

    public FeaturedOffer? FeaturedOffer { get; set; }
}
