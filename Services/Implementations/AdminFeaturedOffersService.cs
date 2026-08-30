using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public class AdminFeaturedOffersService
     : IAdminFeaturedOffersService
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public AdminFeaturedOffersService(
     AppDbContext context,
     IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public async Task<List<FeaturedOffer>> GetAllAsync()
        {
            return await _context.FeaturedOffers
                .AsNoTracking()
                .Include(x => x.Conditions)
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync();
        }

        public async Task<FeaturedOffer?> GetByIdAsync(int id)
        {
            return await _context.FeaturedOffers
                .Include(x => x.Conditions)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<FeaturedOffer> CreateAsync(
     AdminFeaturedOfferRequestDto request)
        {
            string imageUrl = string.Empty;

            if (request.Image != null)
            {
                imageUrl =
                    await SaveImageAsync(request.Image);
            }

            var offer = new FeaturedOffer
            {
                Title = request.Title,
                Subtitle = request.Subtitle,
                Description = request.Description,
                IsActive = request.IsActive,
                DiscountType = request.DiscountType,
                DiscountValue = request.DiscountValue,
                MaxDiscountAmount = request.MaxDiscountAmount,
                MinBookingAmount = request.MinBookingAmount,
                StartDateUtc = request.StartDateUtc,
                EndDateUtc = request.EndDateUtc,
                MaxUsage = request.MaxUsage,
                UsedCount = request.UsedCount,
                BookingType = request.BookingType,
                ImageUrl = imageUrl,
                DisplayOrder = request.DisplayOrder,
            };

            _context.FeaturedOffers.Add(offer);
            await _context.SaveChangesAsync();

            return (await GetByIdAsync(offer.Id))!;
        }

        public async Task<FeaturedOffer?> UpdateAsync(
     int id,
     AdminFeaturedOfferRequestDto request)
        {
            var offer = await _context.FeaturedOffers
                .FirstOrDefaultAsync(x => x.Id == id);

            if (offer == null)
            {
                return null;
            }

            offer.Title = request.Title;
            offer.Subtitle = request.Subtitle;
            offer.Description = request.Description;
            offer.IsActive = request.IsActive;
            offer.DiscountType = request.DiscountType;
            offer.DiscountValue = request.DiscountValue;
            offer.MaxDiscountAmount = request.MaxDiscountAmount;
            offer.MinBookingAmount = request.MinBookingAmount;
            offer.StartDateUtc = request.StartDateUtc;
            offer.EndDateUtc = request.EndDateUtc;
            offer.MaxUsage = request.MaxUsage;
            offer.UsedCount = request.UsedCount;
            offer.BookingType = request.BookingType;
            offer.DisplayOrder = request.DisplayOrder;

            if (request.Image != null)
            {
                offer.ImageUrl =
                    await SaveImageAsync(request.Image);
            }

            await _context.SaveChangesAsync();

            return await GetByIdAsync(offer.Id);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var offer = await _context.FeaturedOffers
                .FirstOrDefaultAsync(x => x.Id == id);

            if (offer == null)
            {
                return false;
            }

            _context.FeaturedOffers.Remove(offer);

            await _context.SaveChangesAsync();

            return true;
        }
        private async Task<string> SaveImageAsync(
    IFormFile file)
        {
            var uploadsFolder = Path.Combine(
                _environment.WebRootPath,
                "offers");

            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(
                    uploadsFolder);
            }

            var uniqueFileName =
                Guid.NewGuid().ToString() +
                Path.GetExtension(file.FileName);

            var filePath = Path.Combine(
                uploadsFolder,
                uniqueFileName);

            using var stream =
                new FileStream(filePath, FileMode.Create);

            await file.CopyToAsync(stream);

            return $"/offers/{uniqueFileName}";
        }
    }
    }
