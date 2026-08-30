using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PickNBook.Api.Services
{
    public interface ISrdvFlightService
    {
        Task<string> AuthenticateAsync();
        Task<List<FlightOfferDto>> SearchFlightsAsync(
            string origin, 
            string destination, 
            DateTime departureDate, 
            int adultCount = 1, 
            int childCount = 0, 
            int infantCount = 0);
        Task<string> SearchFlightsRawAsync(AirSearchRequestDto request);
        Task<string> GetFareRuleRawAsync(AirFareRuleRequestDto request);
        Task<string> GetFareQuoteRawAsync(AirFareRuleRequestDto request);
        Task<string> GetSSRRawAsync(AirFareRuleRequestDto request);
        Task<string> GetSeatMapRawAsync(AirFareRuleRequestDto request);
        Task<string> TicketLCCRawAsync(TicketLCCRequestDto request);
        Task<string> HoldGDSRawAsync(HoldGDSRequestDto request);
        Task<string> TicketGDSRawAsync(TicketGDSRequestDto request);
        Task<string> GetCalendarFareRawAsync(CalendarFareRequestDto request);
        Task<FlightFareQuoteDto> GetFareQuoteAsync(string traceId, string resultIndex);
        Task<FlightBookingResponseDto> BookFlightAsync(FlightBookingRequestDto request);
        Task<FlightBookingResponseDto> TicketLccFlightAsync(FlightBookingRequestDto request);
        Task<FlightTicketResponseDto> TicketFlightAsync(string pnr, string bookingId);
        Task<string> SendChangeRequestRawAsync(SendChangeRequestDto request);
        Task<string> GetCancelStatusRawAsync(GetCancelStatusRequestDto request);
        Task<string> GetCancellationChargesRawAsync(GetCancellationChargesRequestDto request);
        Task<string> GetApiBalanceCheckRawAsync(ApiBalanceRequestDto request);
        Task<string> GetApiBalanceLogRawAsync(ApiBalanceRequestDto request);
    }
}
