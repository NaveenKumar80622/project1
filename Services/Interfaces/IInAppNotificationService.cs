using System.Threading;
using System.Threading.Tasks;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IInAppNotificationService
    {
        Task<InAppNotificationDto?> CreateNotificationAsync(
            CreateInAppNotificationRequest request,
            CancellationToken cancellationToken = default);

        Task<InAppNotificationDto?> CreateNotificationAsync(
            string type,
            string category,
            string title,
            string message,
            string severity,
            string? referenceType = null,
            string? referenceId = null,
            string? actionUrl = null,
            string? idempotencyKey = null,
            string? targetUserId = null,
            string? targetRole = null,
            CancellationToken cancellationToken = default);

        Task<PagedNotificationResult<InAppNotificationDto>> GetCustomerNotificationsAsync(
            int userId,
            NotificationQueryParameters queryParams,
            CancellationToken cancellationToken = default);

        Task<PagedNotificationResult<InAppNotificationDto>> GetAdminNotificationsAsync(
            string role,
            int? adminUserId,
            NotificationQueryParameters queryParams,
            CancellationToken cancellationToken = default);

        Task<UnreadCountDto> GetUnreadCountAsync(
            int? userId,
            string role,
            CancellationToken cancellationToken = default);

        Task<bool> MarkAsReadAsync(
            int recipientId,
            int? userId,
            string role,
            CancellationToken cancellationToken = default);

        Task<int> MarkAllAsReadAsync(
            int? userId,
            string role,
            CancellationToken cancellationToken = default);
    }
}
