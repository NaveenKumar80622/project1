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

            var activeOffers = (await _featuredOffersService.GetFeaturedOffersAsync())
                .OrderBy(x => x.OfferId)
                .Take(15)
                .ToList();

            var emailSent = false;
            var emailError = string.Empty;

            try
            {
                var emailBody = BuildOffersEmailBody(activeOffers);
                await _emailService.SendEmailAsync(
                    email,
                    "Your PickNBook Exclusive Offers",
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
                ? "Subscription successful. Exclusive offers were sent."
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

        private static string BuildOffersEmailBody(IReadOnlyList<FeaturedOfferDto> offers)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<h2>Exclusive Offers For You</h2>");
            sb.AppendLine("<p>Thanks for subscribing. Here are your latest exclusive offers:</p>");
            sb.AppendLine("<table style='border-collapse:collapse;width:100%;font-family:Arial,sans-serif;'>");
            sb.AppendLine("<thead><tr>");
            sb.AppendLine("<th style='border:1px solid #ddd;padding:8px;text-align:left;'>Offer</th>");
            sb.AppendLine("<th style='border:1px solid #ddd;padding:8px;text-align:left;'>Description</th>");
            sb.AppendLine("<th style='border:1px solid #ddd;padding:8px;text-align:left;'>Discount</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var offer in offers)
            {
                var isPercentage = offer.DiscountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase);
                var discountText = isPercentage
                    ? $"{offer.DiscountValue}% OFF"
                    : $"INR {offer.DiscountValue} OFF";

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td style='border:1px solid #ddd;padding:8px;'><b>{offer.Title}</b></td>");
                sb.AppendLine($"<td style='border:1px solid #ddd;padding:8px;'>{offer.Description}</td>");
                sb.AppendLine($"<td style='border:1px solid #ddd;padding:8px;'>{discountText}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("<p>Book now and save more with PickNBook.</p>");

            return sb.ToString();
        }

        private static string BuildWhatsAppMessage(IReadOnlyList<FeaturedOfferDto> offers)
        {
            var topOffers = offers.Take(5).ToList();
            if (!topOffers.Any())
            {
                return "PickNBook: You are subscribed successfully. New offers will be shared soon.";
            }

            var lines = new List<string>
            {
                "PickNBook Exclusive Offers:"
            };

            foreach (var offer in topOffers)
            {
                var isPercentage = offer.DiscountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase);
                var discountText = isPercentage
                    ? $"{offer.DiscountValue}% OFF"
                    : $"INR {offer.DiscountValue} OFF";

                lines.Add($"- {offer.Title}: {discountText}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
