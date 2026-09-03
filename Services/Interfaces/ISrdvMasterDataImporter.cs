using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public interface ISrdvMasterDataImporter
    {
        Task<MasterDataImportResultDto> ImportBusCitiesAsync(CancellationToken cancellationToken = default);
        Task<MasterDataImportResultDto> ImportHotelCitiesAsync(CancellationToken cancellationToken = default);
        Task<MasterDataImportResultDto> ImportAirportsAsync(CancellationToken cancellationToken = default);
        Task<MasterDataImportResultDto> ImportAirlinesAsync(CancellationToken cancellationToken = default);
        Task<List<MasterDataImportResultDto>> ImportAllAsync(CancellationToken cancellationToken = default);
        Task<Dictionary<string, int>> GetMasterDataStatusAsync(CancellationToken cancellationToken = default);
    }
}
