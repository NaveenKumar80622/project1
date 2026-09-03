using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IPlacesService
    {
        Task<List<PlaceSuggestionDto>> GetPlacesAsync(
            string? query,
            string tripType = "all",
            string field = "all",
            string? requestType = null,
            int limit = 20,
            CancellationToken cancellationToken = default);
    }
}
