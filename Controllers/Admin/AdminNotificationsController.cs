using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Controllers.Admin
{
    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [ApiController]
    [Route("api/admin/notifications")]
    public class AdminNotificationsController : AdminApiController
    {
        private readonly IInAppNotificationService _notificationService;

        public AdminNotificationsController(IInAppNotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications(
            [FromQuery] NotificationQueryParameters queryParams,
            CancellationToken cancellationToken)
        {
            var role = GetCurrentRole();
            var adminId = GetAdminUserId();

            queryParams ??= new NotificationQueryParameters();

            var result = await _notificationService.GetAdminNotificationsAsync(role, adminId, queryParams, cancellationToken);

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
            var role = GetCurrentRole();
            var adminId = GetAdminUserId();

            var result = await _notificationService.GetUnreadCountAsync(adminId, role, cancellationToken);
            return Ok(result);
        }

        [HttpPut("{id:int}/read")]
        public async Task<IActionResult> MarkAsRead([FromRoute] int id, CancellationToken cancellationToken)
        {
            var role = GetCurrentRole();
            var adminId = GetAdminUserId();

            var success = await _notificationService.MarkAsReadAsync(id, adminId, role, cancellationToken);
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
            var role = GetCurrentRole();
            var adminId = GetAdminUserId();

            var count = await _notificationService.MarkAllAsReadAsync(adminId, role, cancellationToken);
            return Ok(new MarkAllReadResponse
            {
                Success = true,
                Message = "All notifications marked as read",
                Count = count
            });
        }

        [HttpPost("broadcast")]
        public async Task<IActionResult> Broadcast(
            [FromBody] BroadcastNotificationRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request payload is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { message = "Title and Message are required for broadcast notifications." });
            }

            var targetRoleRaw = string.IsNullOrWhiteSpace(request.TargetRole) ? "All" : request.TargetRole.Trim();
            var normalizedRole = string.Equals(targetRoleRaw, "Customer", StringComparison.OrdinalIgnoreCase)
                ? AuthRoles.User
                : targetRoleRaw;

            var createRequest = new CreateInAppNotificationRequest
            {
                Type = string.IsNullOrWhiteSpace(request.Type) ? "Broadcast" : request.Type.Trim(),
                Category = string.IsNullOrWhiteSpace(request.Category) ? "System" : request.Category.Trim(),
                Title = request.Title.Trim(),
                Message = request.Message.Trim(),
                Severity = string.IsNullOrWhiteSpace(request.Severity) ? "Info" : request.Severity.Trim(),
                ReferenceType = request.ReferenceType,
                ReferenceId = request.ReferenceId,
                ActionUrl = request.ActionUrl,
                TargetRole = normalizedRole,
                IdempotencyKey = request.IdempotencyKey
            };

            var result = await _notificationService.CreateNotificationAsync(createRequest, cancellationToken);
            if (result == null)
            {
                return StatusCode(500, new { message = "Failed to create broadcast notification." });
            }

            return Ok(result);
        }

        private string GetCurrentRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value
                ?? AuthRoles.Admin;
        }

        private int? GetAdminUserId()
        {
            var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? User.FindFirst("sub")?.Value
                           ?? User.FindFirst("id")?.Value;

            return int.TryParse(userIdValue, out var id) ? id : null;
        }
    }
}
