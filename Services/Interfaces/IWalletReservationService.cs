using System.Threading.Tasks;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Services.Interfaces
{
    /// <summary>
    /// Service responsible for managing Customer Wallet holds (reservations),
    /// commits (upon successful gateway payment), and releases (upon gateway payment failure/cancellation).
    /// Customer wallet only — do NOT use for Agent / B2B wallet operations.
    /// </summary>
    public interface IWalletReservationService
    {
        /// <summary>
        /// Atomically reserves an amount from the customer wallet, holding the funds
        /// and creating a pending WalletTransaction in 'Reserved' status.
        /// Idempotent: Repeated calls with the same (userId, referenceType, refCode) return the existing reservation.
        /// </summary>
        Task<WalletTransaction> ReserveAsync(int userId, decimal amount, string referenceType, string refCode, string description);

        /// <summary>
        /// Commits a previously reserved amount when gateway payment succeeds.
        /// Transitions the transaction status from 'Reserved' to 'Completed'.
        /// Idempotent: If already 'Completed', returns the existing transaction without double-debiting.
        /// </summary>
        Task<WalletTransaction> CommitReservationAsync(long walletTransactionId);

        /// <summary>
        /// Releases a previously reserved amount when gateway payment fails, is cancelled, or expires.
        /// Transitions the transaction status from 'Reserved' to 'Released' and restores the funds to usable balance.
        /// Exactly-once: Repeated calls return the existing transaction without double-crediting.
        /// </summary>
        Task<WalletTransaction> ReleaseReservationAsync(long walletTransactionId, string? reason = null);

        /// <summary>
        /// Commits a reservation identified by its reference type and ref code.
        /// </summary>
        Task<WalletTransaction> CommitReservationByRefAsync(string refCode, string referenceType);

        /// <summary>
        /// Releases a reservation identified by its reference type and ref code.
        /// </summary>
        Task<WalletTransaction> ReleaseReservationByRefAsync(string refCode, string referenceType, string? reason = null);

        /// <summary>
        /// Retrieves an existing reservation by reference type and ref code, if any.
        /// </summary>
        Task<WalletTransaction?> GetReservationByRefAsync(string refCode, string referenceType);
    }
}
