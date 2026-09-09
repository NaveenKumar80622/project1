using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IAirlineLookupService
    {
        /// <summary>
        /// Gets the canonical airline display name for a given 2-letter airline code.
        /// Falls back to the provided fallback name or the airline code itself if not found.
        /// </summary>
        string GetAirlineName(string airlineCode, string? fallback = null);

        /// <summary>
        /// Asynchronously ensures cache is warm and retrieves the canonical airline name.
        /// </summary>
        Task<string> GetAirlineNameAsync(string airlineCode, string? fallback = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all airlines for client-side matching, dropdowns, and search filters.
        /// </summary>
        Task<IReadOnlyList<FlightAirlineDto>> GetAllAirlinesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Invalidates in-memory airline cache (e.g. after master data sync).
        /// </summary>
        void InvalidateCache();
    }
}
