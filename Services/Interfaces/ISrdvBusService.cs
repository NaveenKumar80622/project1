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
        Task<SrdvBoardingDroppingDetailsDto> GetBoardingPointDetailsAsync(string traceId, long srdvIndex, string resultIndex);
        Task<List<SrdvSeatDto>> GetSeatLayoutAsync(string traceId, long srdvIndex, string resultIndex, string? boardingPointId = null, string? droppingPointId = null);
        Task<string> GetSeatLayoutRawAsync(string traceId, long srdvIndex, string resultIndex, string? boardingPointId = null, string? droppingPointId = null);
        Task<(bool Success, string ErrorMessage, decimal CancellationCharge, decimal RefundAmount)> CancelTicketAsync(string traceId, string seatName, string remark);
        Task<SrdvBusCancelResponseDto> CancelTicketV9Async(long traceId, List<string> seatNames, string remarks);
        Task<string> SearchBusesProxyAsync(BusSearchProxyRequestDto request);
        Task<string> GetSeatLayoutProxyAsync(BusSeatLayoutProxyRequestDto request);
        Task<string> GetBoardingPointDetailsProxyAsync(BusBoardingPointsProxyRequestDto request);
        Task<string> BlockBusProxyAsync(SrdvBusBookingRequestDto request);
        Task<string> BookBusProxyAsync(long traceId, string resultIndex);
        Task<string> GetSrdvMasterWalletBalanceAsync(string endUserIp);
        Task<string> GetSrdvMasterWalletLogAsync(string endUserIp);
        Task<SrdvBusBookingDetailsResponseDto> GetBookingDetailsAsync(string traceId);
    }
}
