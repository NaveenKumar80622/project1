using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : BaseApiController
    {
        private readonly IInAppNotificationService _notificationService;

        public NotificationsController(IInAppNotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications(
            [FromQuery] NotificationQueryParameters queryParams,
            CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUserId(out var userId))
                return Unauthorized(new { message = "Invalid or missing user authentication token." });

            queryParams ??= new NotificationQueryParameters();

            var result = await _notificationService.GetCustomerNotificationsAsync(userId, queryParams, cancellationToken);

            return Ok(new NotificationListResponse
            {
                Items = result.Items,
                Page = result.Page,
                PageSize = result.PageSize,
                TotalCount = result.TotalCount,
                TotalPages = result.TotalPages,
                UnreadCount = result.UnreadCount
            });
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUserId(out var userId))
                return Unauthorized(new { message = "Invalid or missing user authentication token." });

            var result = await _notificationService.GetUnreadCountAsync(userId, AuthRoles.User, cancellationToken);
            return Ok(result);
        }

        [HttpPut("{id:int}/read")]
        public async Task<IActionResult> MarkAsRead([FromRoute] int id, CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUserId(out var userId))
                return Unauthorized(new { message = "Invalid or missing user authentication token." });

            var success = await _notificationService.MarkAsReadAsync(id, userId, AuthRoles.User, cancellationToken);
            if (!success)
            {
                return NotFound(new { message = "Notification not found or access denied." });
            }

            return Ok(new MarkReadResponse
            {
                Success = true,
                Message = "Notification marked as read",
                Id = id,
                IsRead = true
            });
        }

        [HttpPut("mark-all-read")]
        public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
        {
            if (!TryGetCurrentUserId(out var userId))
                return Unauthorized(new { message = "Invalid or missing user authentication token." });

            var count = await _notificationService.MarkAllAsReadAsync(userId, AuthRoles.User, cancellationToken);
            return Ok(new MarkAllReadResponse
            {
                Success = true,
                Message = "All notifications marked as read",
                Count = count
            });
        }

        private bool TryGetCurrentUserId(out int userId)
        {
            userId = 0;
            var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? User.FindFirst("sub")?.Value
                           ?? User.FindFirst("id")?.Value;

            return int.TryParse(userIdValue, out userId);
        }
    }
}
