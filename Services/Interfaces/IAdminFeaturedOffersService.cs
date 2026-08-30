using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services
{
    public interface IAdminFeaturedOffersService
    {
        Task<List<FeaturedOffer>> GetAllAsync();

        Task<FeaturedOffer?> GetByIdAsync(int id);

        Task<FeaturedOffer> CreateAsync(
        AdminFeaturedOfferRequestDto request);

        Task<FeaturedOffer?> UpdateAsync(
            int id,
            AdminFeaturedOfferRequestDto request);

        Task<bool> DeleteAsync(int id);
    }
}
