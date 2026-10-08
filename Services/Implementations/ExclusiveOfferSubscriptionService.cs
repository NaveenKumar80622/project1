using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public interface IExclusiveOfferSubscriptionService
    {
        Task<ExclusiveOfferSubscriptionResponse> SubscribeAsync(ExclusiveOfferSubscriptionRequest request);
    }

    public class ExclusiveOfferEmailItem
    {
        public string Vertical { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DiscountDisplay { get; set; } = string.Empty;
        public string? PromoCode { get; set; }
        public string? ExpiryDateDisplay { get; set; }
    }

    public class ExclusiveOfferSubscriptionService : IExclusiveOfferSubscriptionService
    {
        private readonly AppDbContext _context;
        private readonly IFeaturedOffersService _featuredOffersService;
        private readonly IEmailService _emailService;
        private readonly IWhatsAppService _whatsAppService;

        public ExclusiveOfferSubscriptionService(
            AppDbContext context,
            IFeaturedOffersService featuredOffersService,
            IEmailService emailService,
            IWhatsAppService whatsAppService)
        {
            _context = context;
            _featuredOffersService = featuredOffersService;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
        }

        public async Task<ExclusiveOfferSubscriptionResponse> SubscribeAsync(ExclusiveOfferSubscriptionRequest request)
        {
            var now = DateTime.UtcNow;
            var email = request.Email.Trim().ToLowerInvariant();
            var whatsAppNumber = string.IsNullOrWhiteSpace(request.WhatsAppNumber)
                ? null
                : request.WhatsAppNumber.Trim();

            var subscriber = await _context.OfferSubscribers
                .FirstOrDefaultAsync(x => x.Email == email);

            var alreadySubscribed = subscriber != null;

            if (subscriber == null)
            {
                subscriber = new OfferSubscriber
                {
                    Email = email,
                    WhatsAppNumber = whatsAppNumber,
                    IsActive = true,
                    SubscribedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _context.OfferSubscribers.Add(subscriber);
            }
            else
            {
                subscriber.WhatsAppNumber = whatsAppNumber;
                subscriber.IsActive = true;
                subscriber.UpdatedAtUtc = now;
            }

            await _context.SaveChangesAsync();

            // Fetch ONLY active offers (PromotionCategory == "Offer") across verticals
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
            var activeOffers = new List<ExclusiveOfferEmailItem>();

            // 1. Flight Offers (PromotionCategory == "Offer")
            var flightOffers = await _context.FlightCoupons.AsNoTracking()
                .Where(c => c.Status == "Active" &&
                            c.PromotionCategory == "Offer" &&
                            c.StartDate <= today &&
                            c.ExpiryDate >= today &&
                            (c.UseLimit == 0 || c.UsedCount < c.UseLimit))
                .OrderBy(c => c.Priority)
                .Take(5)
                .ToListAsync();

            foreach (var f in flightOffers)
            {
                var isPct = string.Equals(f.CouponType, "Percentage", StringComparison.OrdinalIgnoreCase);
                activeOffers.Add(new ExclusiveOfferEmailItem
                {
                    Vertical = "Flight",
                    Title = string.IsNullOrWhiteSpace(f.Title) ? f.CouponCode : f.Title,
                    Description = string.IsNullOrWhiteSpace(f.Description) ? (f.Remark ?? "Special Flight Offer") : f.Description,
                    PromoCode = f.CouponCode,
                    DiscountDisplay = isPct ? $"{f.Value:0.#}% OFF" : $"INR {f.Value:0.##} OFF",
                    ExpiryDateDisplay = f.ExpiryDate.ToString("dd MMM yyyy")
                });
            }

            // 2. Hotel Offers (PromotionCategory == "Offer")
            var hotelOffers = await _context.HotelCoupons.AsNoTracking()
                .Where(c => c.Status == "Active" &&
                            c.PromotionCategory == "Offer" &&
                            c.StartDate <= today &&
                            c.ExpiryDate >= today &&
                            (c.UseLimit == 0 || c.UsedCount < c.UseLimit))
                .OrderBy(c => c.Priority)
                .Take(5)
                .ToListAsync();

            foreach (var h in hotelOffers)
            {
                var isPct = string.Equals(h.CouponType, "Percentage", StringComparison.OrdinalIgnoreCase);
                activeOffers.Add(new ExclusiveOfferEmailItem
                {
                    Vertical = "Hotel",
                    Title = string.IsNullOrWhiteSpace(h.Title) ? h.CouponCode : h.Title,
                    Description = string.IsNullOrWhiteSpace(h.Description) ? (h.Remark ?? "Special Hotel Offer") : h.Description,
                    PromoCode = h.CouponCode,
                    DiscountDisplay = isPct ? $"{h.Value:0.#}% OFF" : $"INR {h.Value:0.##} OFF",
                    ExpiryDateDisplay = h.ExpiryDate.ToString("dd MMM yyyy")
                });
            }

            // 3. Bus Offers (PromotionCategory == "Offer")
            var busOffers = await _context.BusCoupons.AsNoTracking()
                .Where(c => c.Status == "Active" &&
                            c.PromotionCategory == "Offer" &&
                            c.StartDate <= today &&
                            c.ExpiryDate >= today &&
                            (c.UseLimit == 0 || c.UsedCount < c.UseLimit))
                .OrderBy(c => c.Priority)
                .Take(5)
                .ToListAsync();

            foreach (var b in busOffers)
            {
                var isPct = string.Equals(b.CouponType, "Percentage", StringComparison.OrdinalIgnoreCase);
                activeOffers.Add(new ExclusiveOfferEmailItem
                {
                    Vertical = "Bus",
                    Title = string.IsNullOrWhiteSpace(b.Title) ? b.CouponCode : b.Title,
                    Description = string.IsNullOrWhiteSpace(b.Description) ? (b.Remark ?? "Special Bus Offer") : b.Description,
                    PromoCode = b.CouponCode,
                    DiscountDisplay = isPct ? $"{b.Value:0.#}% OFF" : $"INR {b.Value:0.##} OFF",
                    ExpiryDateDisplay = b.ExpiryDate.ToString("dd MMM yyyy")
                });
            }

            // 4. Featured Offers from featuredoffers table
            var featuredOffers = await _context.FeaturedOffers.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .Take(5)
                .ToListAsync();

            foreach (var feat in featuredOffers)
            {
                if (activeOffers.Any(o => string.Equals(o.Title, feat.Title, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var isPct = string.Equals(feat.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase);
                activeOffers.Add(new ExclusiveOfferEmailItem
                {
                    Vertical = string.IsNullOrWhiteSpace(feat.BookingType) ? "Travel" : feat.BookingType,
                    Title = feat.Title,
                    Description = string.IsNullOrWhiteSpace(feat.Description) ? (feat.Subtitle ?? "Exclusive Featured Deal") : feat.Description,
                    PromoCode = null,
                    DiscountDisplay = isPct ? $"{feat.DiscountValue:0.#}% OFF" : $"INR {feat.DiscountValue:0.##} OFF",
                    ExpiryDateDisplay = feat.EndDateUtc?.ToString("dd MMM yyyy")
                });
            }

            var emailSent = false;
            var emailError = string.Empty;

            try
            {
                var emailBody = BuildOffersEmailBody(activeOffers);
                await _emailService.SendEmailAsync(
                    email,
                    "Your Pick&book Exclusive Travel Offers",
                    emailBody);
                emailSent = true;
            }
            catch (Exception ex)
            {
                emailError = ex.Message;
            }

            var whatsAppSent = false;
            var whatsAppStatus = "WhatsApp number not provided.";

            if (!string.IsNullOrWhiteSpace(whatsAppNumber))
            {
                var whatsAppMessage = BuildWhatsAppMessage(activeOffers);
                var whatsAppResult = await _whatsAppService.SendTextAsync(whatsAppNumber, whatsAppMessage);

                whatsAppSent = whatsAppResult.IsSent;
                whatsAppStatus = whatsAppResult.Message;
            }

            var success = emailSent || whatsAppSent;
            var responseMessage = success
                ? (alreadySubscribed
                    ? "Welcome back! Exclusive offers were resent to your email."
                    : "Subscription successful! Exclusive offers were sent to your email.")
                : $"Subscription saved, but delivery failed. Email: {emailError}. WhatsApp: {whatsAppStatus}";

            return new ExclusiveOfferSubscriptionResponse
            {
                IsSuccess = success,
                Message = responseMessage,
                AlreadySubscribed = alreadySubscribed,
                EmailSent = emailSent,
                WhatsAppSent = whatsAppSent,
                Email = email,
                WhatsAppNumber = whatsAppNumber,
                OffersIncluded = activeOffers.Count,
                SubscribedAtUtc = subscriber.SubscribedAtUtc
            };
        }

        private static string BuildOffersEmailBody(IReadOnlyList<ExclusiveOfferEmailItem> offers)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'></head>");
            sb.AppendLine("<body style='margin:0;padding:24px 0;background-color:#f1f5f9;font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,Helvetica,Arial,sans-serif;'>");
            sb.AppendLine("<table align='center' border='0' cellpadding='0' cellspacing='0' width='100%' style='max-width:620px;margin:0 auto;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 4px 16px rgba(0,0,0,0.06);border:1px solid #e2e8f0;'>");

            // Header banner
            sb.AppendLine("<tr><td style='background:linear-gradient(135deg, #0f172a 0%, #1e293b 100%);padding:28px 32px;text-align:center;'>");
            sb.AppendLine("<div style='background:#ffffff;display:inline-block;padding:7px 22px;border-radius:8px;box-shadow:0 4px 12px rgba(0,0,0,0.22);margin-bottom:10px;'><img src='https://www.picknbook.in/assets/picknbook-login-q5fv1iRs.png' alt='Pick&amp;book' style='height:30px;width:auto;display:block;border:0;' /></div>");
            sb.AppendLine("<p style='margin:6px 0 0;color:#cbd5e1;font-size:14px;letter-spacing:0.3px;'>Exclusive Travel Offers &amp; Secret Deals</p>");
            sb.AppendLine("</td></tr>");

            // Intro
            sb.AppendLine("<tr><td style='padding:28px 32px 16px;'>");
            sb.AppendLine("<h2 style='margin:0 0 8px;color:#0f172a;font-size:20px;font-weight:700;'>🎉 Exclusive Deals Handpicked For You</h2>");
            sb.AppendLine("<p style='margin:0;color:#475569;font-size:14px;line-height:1.6;'>Thanks for subscribing! Here are the latest active travel offers on Pick&amp;book across Flights, Hotels, and Buses:</p>");
            sb.AppendLine("</td></tr>");

            // Offers List
            sb.AppendLine("<tr><td style='padding:0 32px 24px;'>");

            if (offers.Count == 0)
            {
                sb.AppendLine("<div style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:24px;text-align:center;color:#64748b;'>");
                sb.AppendLine("<p style='margin:0;font-size:14px;'>New seasonal promotions are being prepared. You will receive them the moment they launch!</p>");
                sb.AppendLine("</div>");
            }
            else
            {
                sb.AppendLine("<table border='0' cellpadding='0' cellspacing='0' width='100%' style='border-collapse:separate;border-spacing:0 12px;'>");

                foreach (var offer in offers)
                {
                    var verticalBadgeColor = offer.Vertical.ToLower() switch
                    {
                        "flight" => "background:#eff6ff;color:#1d4ed8;border:1px solid #bfdbfe;",
                        "hotel" => "background:#faf5ff;color:#7e22ce;border:1px solid #e9d5ff;",
                        "bus" => "background:#fef2f2;color:#b91c1c;border:1px solid #fecaca;",
                        _ => "background:#f0fdf4;color:#15803d;border:1px solid #bbf7d0;"
                    };

                    var verticalIcon = offer.Vertical.ToLower() switch
                    {
                        "flight" => "✈️ Flight",
                        "hotel" => "🏨 Hotel",
                        "bus" => "🚌 Bus",
                        _ => "🌴 Travel"
                    };

                    sb.AppendLine("<tr><td style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:16px 18px;'>");
                    sb.AppendLine("<table border='0' cellpadding='0' cellspacing='0' width='100%'><tr>");
                    sb.AppendLine("<td style='vertical-align:top;'>");
                    sb.AppendLine($"<span style='display:inline-block;padding:2px 8px;border-radius:999px;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:0.5px;margin-bottom:6px;{verticalBadgeColor}'>{verticalIcon}</span>");
                    sb.AppendLine($"<h3 style='margin:0 0 4px;font-size:16px;color:#0f172a;font-weight:700;'>{System.Net.WebUtility.HtmlEncode(offer.Title)}</h3>");
                    sb.AppendLine($"<p style='margin:0;font-size:13px;color:#64748b;line-height:1.4;'>{System.Net.WebUtility.HtmlEncode(offer.Description)}</p>");

                    if (!string.IsNullOrWhiteSpace(offer.PromoCode))
                    {
                        sb.AppendLine($"<div style='margin-top:8px;'><span style='font-size:11px;color:#64748b;'>Promo Code: </span><span style='font-family:monospace;font-size:13px;font-weight:700;background:#fee2e2;color:#dc2626;padding:2px 7px;border-radius:4px;border:1px dashed #ef4444;'>{System.Net.WebUtility.HtmlEncode(offer.PromoCode)}</span></div>");
                    }

                    sb.AppendLine("</td>");
                    sb.AppendLine("<td style='vertical-align:top;text-align:right;width:115px;'>");
                    sb.AppendLine($"<div style='background:#dc2626;color:#ffffff;font-size:13px;font-weight:800;padding:6px 10px;border-radius:6px;display:inline-block;white-space:nowrap;'>{System.Net.WebUtility.HtmlEncode(offer.DiscountDisplay)}</div>");

                    if (!string.IsNullOrWhiteSpace(offer.ExpiryDateDisplay))
                    {
                        sb.AppendLine($"<p style='margin:6px 0 0;font-size:11px;color:#94a3b8;'>Valid till {offer.ExpiryDateDisplay}</p>");
                    }

                    sb.AppendLine("</td>");
                    sb.AppendLine("</tr></table>");
                    sb.AppendLine("</td></tr>");
                }

                sb.AppendLine("</table>");
            }

            sb.AppendLine("</td></tr>");

            // CTA Button
            sb.AppendLine("<tr><td style='padding:0 32px 32px;text-align:center;'>");
            sb.AppendLine("<a href='https://www.picknbook.in' target='_blank' style='display:inline-block;background:#dc2626;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700;padding:14px 32px;border-radius:8px;box-shadow:0 4px 10px rgba(220,38,38,0.25);'>Explore &amp; Book Deals &rarr;</a>");
            sb.AppendLine("</td></tr>");

            // Footer
            sb.AppendLine("<tr><td style='background:#f8fafc;padding:20px 32px;text-align:center;border-top:1px solid #e2e8f0;'>");
            sb.AppendLine("<p style='margin:0;font-size:12px;color:#94a3b8;'>You received this email because you subscribed to exclusive offers on Pick&amp;book.</p>");
            sb.AppendLine("<p style='margin:4px 0 0;font-size:12px;color:#94a3b8;'>&copy; " + DateTime.UtcNow.Year + " Pick&amp;book. All rights reserved.</p>");
            sb.AppendLine("</td></tr>");

            sb.AppendLine("</table></body></html>");

            return sb.ToString();
        }

        private static string BuildWhatsAppMessage(IReadOnlyList<ExclusiveOfferEmailItem> offers)
        {
            var topOffers = offers.Take(6).ToList();
            if (!topOffers.Any())
            {
                return "Pick&book: You have subscribed successfully. New exclusive offers will be shared soon.";
            }

            var lines = new List<string>
            {
                "🎉 Pick&book Exclusive Travel Offers:",
                ""
            };

            foreach (var offer in topOffers)
            {
                var codeStr = string.IsNullOrWhiteSpace(offer.PromoCode) ? "" : $" (Code: {offer.PromoCode})";
                lines.Add($"• [{offer.Vertical}] {offer.Title}: {offer.DiscountDisplay}{codeStr}");
            }

            lines.Add("");
            lines.Add("Book now at https://www.picknbook.in");

            return string.Join(Environment.NewLine, lines);
        }
    }
}
