using PickNBook.Api.Models.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PickNBook.Api.Services
{
    public interface ISrdvBusService
    {
        Task<string> AuthenticateAsync();
        Task<List<BusCityDto>> SearchBusCitiesAsync(string query);
        string MapCityCodeToName(string cityCode);
        Task<List<SrdvBusOfferDto>> SearchBusesAsync(string originId, string destinationId, string journeyDate);
        Task<(string RawJson, List<SrdvBusOfferDto> Buses)> SearchBusesWithRawAsync(string originId, string destinationId, string journeyDate);
        Task<SrdvBusBookingResponseDto> BookBusAsync(SrdvBusBookingRequestDto request, string blockKey);
        Task<SrdvBoardingDroppingDetailsDto> GetBoardingPointDetailsAsync(string traceId, int srdvIndex, string resultIndex);
        Task<List<SrdvSeatDto>> GetSeatLayoutAsync(string traceId, int srdvIndex, string resultIndex);
        Task<string> GetSeatLayoutRawAsync(string traceId, int srdvIndex, string resultIndex);
        Task<(bool Success, string ErrorMessage)> CancelTicketAsync(string traceId, string seatName, string remark);
        Task<string> SearchBusesProxyAsync(BusSearchProxyRequestDto request);
        Task<string> GetSeatLayoutProxyAsync(BusSeatLayoutProxyRequestDto request);
        Task<string> GetBoardingPointDetailsProxyAsync(BusBoardingPointsProxyRequestDto request);
        Task<string> BlockBusProxyAsync(SrdvBusBookingRequestDto request);
        Task<string> GetSrdvMasterWalletBalanceAsync(string endUserIp);
        Task<string> GetSrdvMasterWalletLogAsync(string endUserIp);
    }
}
