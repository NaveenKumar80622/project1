using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using PickNBook.Api.Data;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    /// <summary>
    /// Implementation of Customer Wallet Reservation Service.
    /// Manages the full lifecycle of customer wallet reservations:
    /// Reserve -> Commit (on payment success) OR Release (on payment failure/cancellation).
    /// Prevents double debits, double releases, negative balances, and concurrency anomalies.
    /// </summary>
    public class WalletReservationService : IWalletReservationService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<WalletReservationService> _logger;
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _userLocks = new();
        private static readonly object _inMemoryLock = new object();

        public WalletReservationService(AppDbContext context, ILogger<WalletReservationService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<WalletTransaction> ReserveAsync(
            int userId,
            decimal amount,
            string referenceType,
            string refCode,
            string description)
        {
            if (userId <= 0)
                throw new ArgumentException("User ID must be greater than zero.", nameof(userId));

            if (amount <= 0)
                throw new ArgumentException("Reservation amount must be greater than zero.", nameof(amount));

            if (string.IsNullOrWhiteSpace(referenceType))
                throw new ArgumentException("Reference type is required.", nameof(referenceType));

            if (string.IsNullOrWhiteSpace(refCode))
                throw new ArgumentException("RefCode is required.", nameof(refCode));

            var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
            await userLock.WaitAsync();
            try
            {
                // 1. Idempotency check: check if an existing transaction with this refCode already exists
                var existingTx = await _context.WalletTransactions
                    .FirstOrDefaultAsync(t => t.UserId == userId &&
                                              t.RefCode == refCode &&
                                              t.ReferenceType == referenceType);

                if (existingTx != null)
                {
                    if (string.Equals(existingTx.Status, "Reserved", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation(
                            "Idempotent reservation detected: Returning existing reservation {TxId} for User {UserId}, RefCode {RefCode}.",
                            existingTx.Id, userId, refCode);
                        return existingTx;
                    }

                    if (string.Equals(existingTx.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation(
                            "Reservation for User {UserId}, RefCode {RefCode} has already been committed as {TxId}.",
                            userId, refCode, existingTx.Id);
                        return existingTx;
                    }

                    if (string.Equals(existingTx.Status, "Released", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Cannot reserve: a transaction for RefCode '{refCode}' was previously released.");
                    }
                }

                // 2. Pre-flight check on user wallet status & balance
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                    throw new InvalidOperationException($"User with ID {userId} not found.");

                if (!string.Equals(user.WalletStatus, "Active", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("User wallet is not active.");

                if (user.WalletBalance < amount)
                    throw new InvalidOperationException(
                        $"Insufficient wallet balance. Available: {user.WalletBalance:N2}, Required: {amount:N2}");

                // 3. Atomic deduction within database transaction
                var isRelational = _context.Database.IsRelational();
                var hasExistingDbTx = isRelational && _context.Database.CurrentTransaction != null;
                var dbTx = (isRelational && !hasExistingDbTx) ? await _context.Database.BeginTransactionAsync() : null;

                try
                {
                    if (isRelational)
                    {
                        // Atomic SQL condition: only deduct if WalletBalance >= amount and WalletStatus = 'Active'
                        // This prevents concurrent over-deduction and guarantees balance never drops below zero.
                        int affectedRows = await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE users SET WalletBalance = WalletBalance - {amount} WHERE Id = {userId} AND WalletStatus = 'Active' AND WalletBalance >= {amount};");

                        if (affectedRows == 0)
                        {
                            // Concurrent race occurred: reload user to provide exact error
                            await _context.Entry(user).ReloadAsync();

                            if (!string.Equals(user.WalletStatus, "Active", StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("User wallet is not active.");

                            if (user.WalletBalance < amount)
                                throw new InvalidOperationException(
                                    $"Insufficient wallet balance. Available: {user.WalletBalance:N2}, Required: {amount:N2}");

                            throw new InvalidOperationException(
                                "Concurrent wallet modification detected. Balance reservation failed.");
                        }

                        await _context.Entry(user).ReloadAsync();
                    }
                    else
                    {
                        // Fallback synchronization for non-relational In-Memory test providers
                        lock (_inMemoryLock)
                        {
                            if (user.WalletBalance < amount)
                                throw new InvalidOperationException(
                                    $"Insufficient wallet balance. Available: {user.WalletBalance:N2}, Required: {amount:N2}");

                            user.WalletBalance -= amount;
                        }
                    }

                    // 4. Create pending reservation record
                    var transaction = new WalletTransaction
                    {
                        UserId = userId,
                        TransactionType = "Debit",
                        Amount = amount,
                        RunningBalance = user.WalletBalance,
                        ReferenceType = referenceType,
                        RefCode = refCode,
                        Description = description,
                        Status = "Reserved",
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.WalletTransactions.Add(transaction);
                    await _context.SaveChangesAsync();

                    if (dbTx != null)
                    {
                        await dbTx.CommitAsync();
                    }

                    _logger.LogInformation(
                        "Successfully reserved {Amount:N2} from User {UserId}. Held in Tx {TxId}. New Available: {Balance:N2}. RefCode: {RefCode}",
                        amount, userId, transaction.Id, user.WalletBalance, refCode);

                    return transaction;
                }
                catch (Exception ex)
                {
                    if (dbTx != null)
                    {
                        try
                        {
                            await dbTx.RollbackAsync();
                        }
                        catch (Exception rbEx)
                        {
                            _logger.LogError(rbEx, "Failed to rollback reservation transaction for User {UserId}, RefCode {RefCode}.", userId, refCode);
                        }
                    }

                    _logger.LogError(ex, "Failed to reserve wallet balance for User {UserId}, RefCode {RefCode}.", userId, refCode);
                    throw;
                }
            }
            finally
            {
                userLock.Release();
            }
        }

        public async Task<WalletTransaction> CommitReservationAsync(long walletTransactionId)
        {
            if (walletTransactionId <= 0)
                throw new ArgumentException("Wallet transaction ID must be greater than zero.", nameof(walletTransactionId));

            var isRelational = _context.Database.IsRelational();
            var hasExistingDbTx = isRelational && _context.Database.CurrentTransaction != null;
            var dbTx = (isRelational && !hasExistingDbTx) ? await _context.Database.BeginTransactionAsync() : null;

            try
            {
                var transaction = await _context.WalletTransactions
                    .FirstOrDefaultAsync(t => t.Id == walletTransactionId);

                if (transaction == null)
                    throw new InvalidOperationException($"Wallet transaction with ID {walletTransactionId} not found.");

                // Duplicate / idempotent commit check:
                if (string.Equals(transaction.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "CommitReservationAsync: Transaction {TxId} is already Completed. No-op.",
                        walletTransactionId);
                    return transaction;
                }

                if (string.Equals(transaction.Status, "Released", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cannot commit reservation {walletTransactionId} because it has already been Released.");
                }

                if (!string.Equals(transaction.Status, "Reserved", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cannot commit transaction {walletTransactionId} because its status is '{transaction.Status}', expected 'Reserved'.");
                }

                transaction.Status = "Completed";
                await _context.SaveChangesAsync();

                if (dbTx != null)
                {
                    await dbTx.CommitAsync();
                }

                _logger.LogInformation(
                    "Successfully committed wallet reservation {TxId} for User {UserId}. Status is now Completed.",
                    transaction.Id, transaction.UserId);

                return transaction;
            }
            catch (Exception ex)
            {
                if (dbTx != null)
                {
                    try
                    {
                        await dbTx.RollbackAsync();
                    }
                    catch (Exception rbEx)
                    {
                        _logger.LogError(rbEx, "Failed to rollback commit transaction for TxId {TxId}.", walletTransactionId);
                    }
                }

                _logger.LogError(ex, "Failed to commit wallet reservation {TxId}.", walletTransactionId);
                throw;
            }
        }

        public async Task<WalletTransaction> ReleaseReservationAsync(long walletTransactionId, string? reason = null)
        {
            if (walletTransactionId <= 0)
                throw new ArgumentException("Wallet transaction ID must be greater than zero.", nameof(walletTransactionId));

            var isRelational = _context.Database.IsRelational();
            var hasExistingDbTx = isRelational && _context.Database.CurrentTransaction != null;
            var dbTx = (isRelational && !hasExistingDbTx) ? await _context.Database.BeginTransactionAsync() : null;

            try
            {
                var transaction = await _context.WalletTransactions
                    .FirstOrDefaultAsync(t => t.Id == walletTransactionId);

                if (transaction == null)
                    throw new InvalidOperationException($"Wallet transaction with ID {walletTransactionId} not found.");

                // Duplicate / idempotent release check:
                if (string.Equals(transaction.Status, "Released", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "ReleaseReservationAsync: Transaction {TxId} is already Released. No-op.",
                        walletTransactionId);
                    return transaction;
                }

                if (string.Equals(transaction.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cannot release reservation {walletTransactionId} because it has already been Completed.");
                }

                if (!string.Equals(transaction.Status, "Reserved", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cannot release transaction {walletTransactionId} because its status is '{transaction.Status}', expected 'Reserved'.");
                }

                var userLock = _userLocks.GetOrAdd(transaction.UserId, _ => new SemaphoreSlim(1, 1));
                await userLock.WaitAsync();
                try
                {
                    // Restore reserved amount to user wallet
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == transaction.UserId);
                    if (user == null)
                        throw new InvalidOperationException(
                            $"User with ID {transaction.UserId} not found for reservation {walletTransactionId}.");

                    if (isRelational)
                    {
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE users SET WalletBalance = WalletBalance + {transaction.Amount} WHERE Id = {transaction.UserId};");

                        await _context.Entry(user).ReloadAsync();
                    }
                    else
                    {
                        lock (_inMemoryLock)
                        {
                            user.WalletBalance += transaction.Amount;
                        }
                    }

                    transaction.Status = "Released";
                    transaction.RunningBalance = user.WalletBalance;

                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        transaction.Description = string.IsNullOrWhiteSpace(transaction.Description)
                            ? $"Released: {reason}"
                            : $"{transaction.Description} | Released: {reason}";
                    }

                    await _context.SaveChangesAsync();

                    if (dbTx != null)
                    {
                        await dbTx.CommitAsync();
                    }

                    _logger.LogInformation(
                        "Successfully released wallet reservation {TxId} for User {UserId}. Restored: {Amount:N2}, New Available: {Balance:N2}. Reason: {Reason}",
                        transaction.Id, transaction.UserId, transaction.Amount, user.WalletBalance, reason ?? "N/A");

                    return transaction;
                }
                finally
                {
                    userLock.Release();
                }
            }
            catch (Exception ex)
            {
                if (dbTx != null)
                {
                    try
                    {
                        await dbTx.RollbackAsync();
                    }
                    catch (Exception rbEx)
                    {
                        _logger.LogError(rbEx, "Failed to rollback release transaction for TxId {TxId}.", walletTransactionId);
                    }
                }

                _logger.LogError(ex, "Failed to release wallet reservation {TxId}.", walletTransactionId);
                throw;
            }
        }

        public async Task<WalletTransaction> CommitReservationByRefAsync(string refCode, string referenceType)
        {
            if (string.IsNullOrWhiteSpace(refCode))
                throw new ArgumentException("RefCode is required.", nameof(refCode));

            if (string.IsNullOrWhiteSpace(referenceType))
                throw new ArgumentException("Reference type is required.", nameof(referenceType));

            var transaction = await GetReservationByRefAsync(refCode, referenceType);
            if (transaction == null)
                throw new InvalidOperationException(
                    $"No reservation found for RefCode '{refCode}' and ReferenceType '{referenceType}'.");

            return await CommitReservationAsync(transaction.Id);
        }

        public async Task<WalletTransaction> ReleaseReservationByRefAsync(string refCode, string referenceType, string? reason = null)
        {
            if (string.IsNullOrWhiteSpace(refCode))
                throw new ArgumentException("RefCode is required.", nameof(refCode));

            if (string.IsNullOrWhiteSpace(referenceType))
                throw new ArgumentException("Reference type is required.", nameof(referenceType));

            var transaction = await GetReservationByRefAsync(refCode, referenceType);
            if (transaction == null)
                throw new InvalidOperationException(
                    $"No reservation found for RefCode '{refCode}' and ReferenceType '{referenceType}'.");

            return await ReleaseReservationAsync(transaction.Id, reason);
        }

        public async Task<WalletTransaction?> GetReservationByRefAsync(string refCode, string referenceType)
        {
            return await _context.WalletTransactions
                .FirstOrDefaultAsync(t => t.RefCode == refCode &&
                                          t.ReferenceType == referenceType);
        }
    }
}
