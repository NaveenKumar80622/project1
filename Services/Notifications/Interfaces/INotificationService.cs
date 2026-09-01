using System.Threading.Tasks;

namespace PickNBook.Api.Services.Notifications.Interfaces
{
    public interface INotificationService
    {
        /// <summary>
        /// Enqueues a notification within the bounds of an existing EF Core transaction.
        /// Ensure this is called BEFORE SaveChangesAsync() is invoked on the ambient DbContext.
        /// </summary>
        Task EnqueueAsync(string eventType, string channel, string recipient, string templateKey, object payload, string? bookingId = null, string? userId = null);

        /// <summary>
        /// Sends a notification immediately bypassing the outbox. Used for OTPs.
        /// </summary>
        Task<bool> SendImmediateAsync(string eventType, string channel, string recipient, string templateKey, object payload);
    }
}
