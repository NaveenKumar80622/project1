using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Services
{
    public interface IFeaturedOffersService
    {
        Task<IReadOnlyList<FeaturedOfferDto>> GetFeaturedOffersAsync();
    }

    public class FeaturedOffersService : IFeaturedOffersService
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string? _publicApiBaseUrl;

        public FeaturedOffersService(
            AppDbContext context,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _publicApiBaseUrl = configuration["PublicApiBaseUrl"]?.Trim().TrimEnd('/');
        }

        public async Task<IReadOnlyList<FeaturedOfferDto>> GetFeaturedOffersAsync()
        {
            var offers = await _context.FeaturedOffers
                .Include(x => x.Conditions)
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync();

            return offers
                .Select(x => MapOfferToDto(x))
                .ToList();
        }

        private FeaturedOfferDto MapOfferToDto(FeaturedOffer offer)
        {
            decimal previewFinalPrice = 0m;
            decimal discountValue = offer.DiscountValue;
            bool isPercentage = offer.DiscountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase);
            decimal basePrice = 1000m;

            var discountAmount = CalculateDiscountAmount(basePrice, isPercentage, discountValue);
            if (offer.MaxDiscountAmount.HasValue)
            {
                discountAmount = Math.Min(discountAmount, offer.MaxDiscountAmount.Value);
            }
            previewFinalPrice = Math.Round(
                Math.Max(0m, basePrice - discountAmount),
                2,
                MidpointRounding.AwayFromZero);

            return new FeaturedOfferDto
            {
                OfferId = offer.Id,
                Title = offer.Title,
                Subtitle = offer.Subtitle,
                Description = offer.Description,
                ImageUrl = ResolveImageUrl(offer.ImageUrl),
                BookingType = offer.BookingType,
                DiscountType = offer.DiscountType,
                DiscountValue = discountValue,
                MaxDiscountAmount = offer.MaxDiscountAmount,
                MinBookingAmount = offer.MinBookingAmount,
                StartDateUtc = offer.StartDateUtc,
                EndDateUtc = offer.EndDateUtc,
                MaxUsage = offer.MaxUsage,
                UsedCount = offer.UsedCount,
                PreviewFinalPrice = previewFinalPrice,
                Conditions = offer.Conditions
                    .Where(c => c.IsActive)
                    .Select(c => new FeaturedOfferConditionDto
                    {
                        ConditionType = c.ConditionType,
                        Value1 = c.Value1,
                        Value2 = c.Value2
                    })
                    .ToList()
            };
        }

        private string ResolveImageUrl(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }

            var normalizedPath = imageUrl.StartsWith('/') ? imageUrl : $"/{imageUrl}";

            if (!string.IsNullOrWhiteSpace(_publicApiBaseUrl))
            {
                return $"{_publicApiBaseUrl}{normalizedPath}";
            }

            var request = _httpContextAccessor.HttpContext?.Request;
            if (request == null || !request.Host.HasValue)
            {
                return normalizedPath;
            }

            var host = request.Host.Host;
            var scheme = request.Scheme;

            if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "https";
            }

            return $"{scheme}://{request.Host}{request.PathBase}{normalizedPath}";
        }

        private static decimal CalculateDiscountAmount(decimal originalPrice, bool isPercentage, decimal discountValue)
        {
            if (discountValue <= 0 || originalPrice <= 0)
            {
                return 0m;
            }

            var discount = isPercentage
                ? (originalPrice * discountValue) / 100m
                : discountValue;

            return Math.Round(Math.Min(discount, originalPrice), 2, MidpointRounding.AwayFromZero);
        }
    }
}
