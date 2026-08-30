using PickNBook.Api.Models.Payments;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IBookingOrchestratorService
    {
        /// <summary>
        /// Processes a successful payment and attempts to finalize the SRDV booking.
        /// Extracts the payload from the PendingPaymentBooking and orchestrates
        /// the exact vertical flow without EF Core transaction wrapping.
        /// </summary>
        /// <param name="paymentId">The ID of the successful payment.</param>
        /// <returns>A tuple indicating success status and an optional error message.</returns>
        Task<(bool Success, string? ErrorMessage)> ProcessFulfillmentAsync(int paymentId);
        Task RecoverFulfillmentAsync(int paymentId);
    }
}
