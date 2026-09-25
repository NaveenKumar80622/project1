using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class InAppNotificationService : IInAppNotificationService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<InAppNotificationService> _logger;

        public InAppNotificationService(AppDbContext context, ILogger<InAppNotificationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<InAppNotificationDto?> CreateNotificationAsync(
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
            CancellationToken cancellationToken = default)
        {
            int? parsedUserId = null;
            if (int.TryParse(targetUserId, out int uid))
            {
                parsedUserId = uid;
            }

            var req = new CreateInAppNotificationRequest
            {
                Type = type,
                Category = category,
                Title = title,
                Message = message,
                Severity = severity,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                ActionUrl = actionUrl,
                IdempotencyKey = idempotencyKey,
                UserId = parsedUserId,
                TargetRole = targetRole
            };

            return await CreateNotificationAsync(req, cancellationToken);
        }

        public async Task<InAppNotificationDto?> CreateNotificationAsync(
            CreateInAppNotificationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // 1. Idempotency check: Return existing notification if IdempotencyKey is supplied and already recorded
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existing = await _context.InAppNotifications
                    .Include(n => n.Recipients)
                    .FirstOrDefaultAsync(n => n.IdempotencyKey == request.IdempotencyKey, cancellationToken);

                if (existing != null)
                {
                    _logger.LogInformation("Notification with IdempotencyKey '{Key}' already exists. Returning existing record.", request.IdempotencyKey);
                    var primaryRecipient = existing.Recipients.FirstOrDefault();
                    return MapToDto(existing, primaryRecipient);
                }
            }

            var now = DateTime.UtcNow;

            var notification = new InAppNotification
            {
                Type = string.IsNullOrWhiteSpace(request.Type) ? "General" : request.Type.Trim(),
                Category = string.IsNullOrWhiteSpace(request.Category) ? "System" : request.Category.Trim(),
                Title = request.Title ?? string.Empty,
                Message = request.Message ?? string.Empty,
                Severity = string.IsNullOrWhiteSpace(request.Severity) ? "Info" : request.Severity.Trim(),
                ReferenceType = request.ReferenceType,
                ReferenceId = request.ReferenceId,
                ActionUrl = request.ActionUrl,
                IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim(),
                CreatedAtUtc = now
            };

            // 2. Build Recipients
            var recipients = new List<NotificationRecipient>();
            var targetUserIds = new HashSet<int>();

            if (request.UserIds != null && request.UserIds.Count > 0)
            {
                foreach (var uid in request.UserIds)
                {
                    targetUserIds.Add(uid);
                }
            }

            if (request.UserId.HasValue)
            {
                targetUserIds.Add(request.UserId.Value);
            }

            // Role-based recipients (e.g. Admin broadcasts)
            var targetRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (request.TargetRoles != null && request.TargetRoles.Count > 0)
            {
                foreach (var r in request.TargetRoles)
                {
                    if (!string.IsNullOrWhiteSpace(r))
                        targetRoles.Add(NormalizeRole(r));
                }
            }

            if (!string.IsNullOrWhiteSpace(request.TargetRole))
            {
                targetRoles.Add(NormalizeRole(request.TargetRole));
            }

            // Option A: Fan-out for Customer/User broadcasts
            bool isTargetingCustomers = targetRoles.Any(r =>
                string.Equals(r, AuthRoles.User, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r, "All", StringComparison.OrdinalIgnoreCase));

            if (isTargetingCustomers)
            {
                var customerUserIds = await _context.Users
                    .Where(u => u.Role == AuthRoles.User)
                    .Select(u => u.Id)
                    .ToListAsync(cancellationToken);

                foreach (var cid in customerUserIds)
                {
                    targetUserIds.Add(cid);
                }
            }

            // Role-level recipients (e.g. for Admin, SuperAdmin, or All so admins can see broadcast)
            foreach (var role in targetRoles)
            {
                // If it was targeting User/Customer only and we fanned out to active customers, skip creating a null-UserId row
                if (string.Equals(role, AuthRoles.User, StringComparison.OrdinalIgnoreCase) && isTargetingCustomers && targetUserIds.Count > 0)
                {
                    continue;
                }

                recipients.Add(new NotificationRecipient
                {
                    UserId = null,
                    RecipientRole = role,
                    IsRead = false,
                    CreatedAtUtc = now
                });
            }

            // User recipients (explicit targets + fanned-out customers)
            foreach (var uid in targetUserIds)
            {
                recipients.Add(new NotificationRecipient
                {
                    UserId = uid,
                    RecipientRole = AuthRoles.User,
                    IsRead = false,
                    CreatedAtUtc = now
                });
            }

            // Fallback: If no recipients were resolved, default to Role "User"
            if (recipients.Count == 0)
            {
                recipients.Add(new NotificationRecipient
                {
                    UserId = null,
                    RecipientRole = AuthRoles.User,
                    IsRead = false,
                    CreatedAtUtc = now
                });
            }

            notification.Recipients = recipients;

            // 3. Save to database with duplicate suppression catch
            try
            {
                _context.InAppNotifications.Add(notification);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && IsDuplicateKeyException(ex))
            {
                _logger.LogWarning("Concurrent duplicate notification detected for IdempotencyKey '{Key}'. Fetching existing record.", request.IdempotencyKey);
                _context.ChangeTracker.Clear();

                var existing = await _context.InAppNotifications
                    .Include(n => n.Recipients)
                    .FirstOrDefaultAsync(n => n.IdempotencyKey == request.IdempotencyKey, cancellationToken);

                if (existing != null)
                {
                    return MapToDto(existing, existing.Recipients.FirstOrDefault());
                }

                throw;
            }

            return MapToDto(notification, notification.Recipients.FirstOrDefault());
        }

        public async Task<PagedNotificationResult<InAppNotificationDto>> GetCustomerNotificationsAsync(
            int userId,
            NotificationQueryParameters queryParams,
            CancellationToken cancellationToken = default)
        {
            queryParams ??= new NotificationQueryParameters();

            // Customer isolation: Only fetch recipients where UserId == userId
            var query = _context.NotificationRecipients
                .AsNoTracking()
                .Include(r => r.Notification)
                .Where(r => r.UserId == userId);

            if (queryParams.UnreadOnly == true)
            {
                query = query.Where(r => !r.IsRead);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Category))
            {
                query = query.Where(r => r.Notification != null && r.Notification.Category == queryParams.Category);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Type))
            {
                query = query.Where(r => r.Notification != null && r.Notification.Type == queryParams.Type);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Severity))
            {
                query = query.Where(r => r.Notification != null && r.Notification.Severity == queryParams.Severity.Trim());
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var unreadCount = await _context.NotificationRecipients
                .CountAsync(r => r.UserId == userId && !r.IsRead, cancellationToken);

            var items = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .Skip((queryParams.Page - 1) * queryParams.PageSize)
                .Take(queryParams.PageSize)
                .Select(r => MapToDto(r.Notification!, r))
                .ToListAsync(cancellationToken);

            return new PagedNotificationResult<InAppNotificationDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = queryParams.Page,
                PageSize = queryParams.PageSize,
                UnreadCount = unreadCount
            };
        }

        public async Task<PagedNotificationResult<InAppNotificationDto>> GetAdminNotificationsAsync(
            string role,
            int? adminUserId,
            NotificationQueryParameters queryParams,
            CancellationToken cancellationToken = default)
        {
            queryParams ??= new NotificationQueryParameters();

            // Admin role targeting: Admins see records targeted to "Admin", "SuperAdmin" (if superadmin), "All", or their adminUserId
            var isSuperAdmin = string.Equals(role, AuthRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

            var query = _context.NotificationRecipients
                .AsNoTracking()
                .Include(r => r.Notification)
                .Where(r =>
                    (r.RecipientRole == AuthRoles.Admin ||
                     r.RecipientRole == "All" ||
                     (isSuperAdmin && r.RecipientRole == AuthRoles.SuperAdmin) ||
                     r.RecipientRole == role) ||
                    (adminUserId.HasValue && r.UserId == adminUserId.Value));

            if (queryParams.UnreadOnly == true)
            {
                query = query.Where(r => !r.IsRead);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Category))
            {
                query = query.Where(r => r.Notification != null && r.Notification.Category == queryParams.Category);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Type))
            {
                query = query.Where(r => r.Notification != null && r.Notification.Type == queryParams.Type);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Severity))
            {
                query = query.Where(r => r.Notification != null && r.Notification.Severity == queryParams.Severity.Trim());
            }

            if (!string.IsNullOrWhiteSpace(queryParams.TargetRole))
            {
                var normalizedTargetRole = NormalizeRole(queryParams.TargetRole);
                query = query.Where(r => r.RecipientRole == normalizedTargetRole);
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var unreadCount = await _context.NotificationRecipients
                .CountAsync(r => !r.IsRead && (
                    r.RecipientRole == AuthRoles.Admin ||
                    r.RecipientRole == "All" ||
                    (isSuperAdmin && r.RecipientRole == AuthRoles.SuperAdmin) ||
                    r.RecipientRole == role ||
                    (adminUserId.HasValue && r.UserId == adminUserId.Value)
                ), cancellationToken);

            var items = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .Skip((queryParams.Page - 1) * queryParams.PageSize)
                .Take(queryParams.PageSize)
                .Select(r => MapToDto(r.Notification!, r))
                .ToListAsync(cancellationToken);

            return new PagedNotificationResult<InAppNotificationDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = queryParams.Page,
                PageSize = queryParams.PageSize,
                UnreadCount = unreadCount
            };
        }

        public async Task<UnreadCountDto> GetUnreadCountAsync(
            int? userId,
            string role,
            CancellationToken cancellationToken = default)
        {
            if (AuthRoles.IsAdminScope(role))
            {
                var isSuperAdmin = string.Equals(role, AuthRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
                var count = await _context.NotificationRecipients
                    .CountAsync(r => !r.IsRead && (
                        r.RecipientRole == AuthRoles.Admin ||
                        r.RecipientRole == "All" ||
                        (isSuperAdmin && r.RecipientRole == AuthRoles.SuperAdmin) ||
                        r.RecipientRole == role ||
                        (userId.HasValue && r.UserId == userId.Value)
                    ), cancellationToken);

                return new UnreadCountDto { UnreadCount = count };
            }

            // Customer scope: requires valid userId
            if (!userId.HasValue)
            {
                return new UnreadCountDto { UnreadCount = 0 };
            }

            var customerUnreadCount = await _context.NotificationRecipients
                .CountAsync(r => r.UserId == userId.Value && !r.IsRead, cancellationToken);

            return new UnreadCountDto { UnreadCount = customerUnreadCount };
        }

        public async Task<bool> MarkAsReadAsync(
            int recipientId,
            int? userId,
            string role,
            CancellationToken cancellationToken = default)
        {
            var recipient = await _context.NotificationRecipients
                .FirstOrDefaultAsync(r => r.Id == recipientId, cancellationToken);

            if (recipient == null) return false;

            // Security Ownership Verification
            if (AuthRoles.IsAdminScope(role))
            {
                // Admin can mark admin-targeted or assigned notifications
                var isSuperAdmin = string.Equals(role, AuthRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
                bool allowed = recipient.RecipientRole == AuthRoles.Admin ||
                               recipient.RecipientRole == "All" ||
                               (isSuperAdmin && recipient.RecipientRole == AuthRoles.SuperAdmin) ||
                               recipient.RecipientRole == role ||
                               (userId.HasValue && recipient.UserId == userId.Value);

                if (!allowed)
                {
                    _logger.LogWarning("Admin {AdminId} ({Role}) denied marking recipient {RecipientId} as read.", userId, role, recipientId);
                    return false;
                }
            }
            else
            {
                // Customer can ONLY mark notifications assigned to their own UserId
                if (!userId.HasValue || recipient.UserId != userId.Value)
                {
                    _logger.LogWarning("User {UserId} unauthorized to mark recipient {RecipientId} as read.", userId, recipientId);
                    return false;
                }
            }

            if (!recipient.IsRead)
            {
                recipient.IsRead = true;
                recipient.ReadAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return true;
        }

        public async Task<int> MarkAllAsReadAsync(
            int? userId,
            string role,
            CancellationToken cancellationToken = default)
        {
            IQueryable<NotificationRecipient> query;

            if (AuthRoles.IsAdminScope(role))
            {
                var isSuperAdmin = string.Equals(role, AuthRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
                query = _context.NotificationRecipients
                    .Where(r => !r.IsRead && (
                        r.RecipientRole == AuthRoles.Admin ||
                        r.RecipientRole == "All" ||
                        (isSuperAdmin && r.RecipientRole == AuthRoles.SuperAdmin) ||
                        r.RecipientRole == role ||
                        (userId.HasValue && r.UserId == userId.Value)
                    ));
            }
            else
            {
                if (!userId.HasValue) return 0;
                query = _context.NotificationRecipients
                    .Where(r => r.UserId == userId.Value && !r.IsRead);
            }

            var unreadRecipients = await query.ToListAsync(cancellationToken);
            if (unreadRecipients.Count == 0) return 0;

            var now = DateTime.UtcNow;
            foreach (var r in unreadRecipients)
            {
                r.IsRead = true;
                r.ReadAtUtc = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return unreadRecipients.Count;
        }

        private static InAppNotificationDto MapToDto(InAppNotification notification, NotificationRecipient? recipient)
        {
            return new InAppNotificationDto
            {
                Id = recipient?.Id ?? notification.Id,
                NotificationId = notification.Id,
                UserId = recipient?.UserId,
                RecipientRole = recipient?.RecipientRole,
                Type = notification.Type,
                Category = notification.Category,
                Title = notification.Title,
                Message = notification.Message,
                Severity = notification.Severity,
                ReferenceType = notification.ReferenceType,
                ReferenceId = notification.ReferenceId,
                ActionUrl = notification.ActionUrl,
                IdempotencyKey = notification.IdempotencyKey,
                IsRead = recipient?.IsRead ?? false,
                ReadAtUtc = recipient?.ReadAtUtc,
                CreatedAtUtc = recipient?.CreatedAtUtc ?? notification.CreatedAtUtc
            };
        }

        private static bool IsDuplicateKeyException(DbUpdateException ex)
        {
            if (ex.InnerException is MySqlConnector.MySqlException mySqlEx && mySqlEx.Number == 1062)
            {
                return true;
            }

            var msg = ex.InnerException?.Message ?? ex.Message;
            return msg.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return AuthRoles.User;
            var trimmed = role.Trim();
            if (string.Equals(trimmed, "Customer", StringComparison.OrdinalIgnoreCase))
                return AuthRoles.User;
            if (string.Equals(trimmed, "User", StringComparison.OrdinalIgnoreCase))
                return AuthRoles.User;
            if (string.Equals(trimmed, "Admin", StringComparison.OrdinalIgnoreCase))
                return AuthRoles.Admin;
            if (string.Equals(trimmed, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                return AuthRoles.SuperAdmin;
            if (string.Equals(trimmed, "All", StringComparison.OrdinalIgnoreCase))
                return "All";
            return trimmed;
        }
    }
}
