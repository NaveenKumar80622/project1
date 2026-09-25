using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PickNBook.Api.Models.DTOs
{
    public class InAppNotificationDto
    {
        public int Id { get; set; } // NotificationRecipient.Id (used for mark-read)

        [JsonIgnore]
        public int NotificationId { get; set; }

        [JsonIgnore]
        public int? UserId { get; set; }

        [JsonIgnore]
        public string? RecipientRole { get; set; }

        public string Type { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = "Info";
        public string? ReferenceType { get; set; }
        public string? ReferenceId { get; set; }
        public string? ActionUrl { get; set; }

        [JsonIgnore]
        public string? IdempotencyKey { get; set; }

        public bool IsRead { get; set; }
        public DateTime? ReadAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class CreateInAppNotificationRequest
    {
        public string Type { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = "Info";
        public string? ReferenceType { get; set; }
        public string? ReferenceId { get; set; }
        public string? ActionUrl { get; set; }
        public string? IdempotencyKey { get; set; }

        public int? UserId { get; set; }
        public List<int>? UserIds { get; set; }
        public string? TargetRole { get; set; }
        public List<string>? TargetRoles { get; set; }
    }

    public class PagedNotificationResult<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageNumber => Page;
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => TotalPages > 0 && Page < TotalPages;
        public int UnreadCount { get; set; }
    }

    public class NotificationListResponse
    {
        public List<InAppNotificationDto> Items { get; set; } = new List<InAppNotificationDto>();
        public int Page { get; set; }
        public int PageNumber
        {
            get => Page;
            set => Page = value;
        }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => TotalPages > 0 && Page < TotalPages;
        public int UnreadCount { get; set; }
    }

    public class MarkReadResponse
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = "Notification marked as read";

        [JsonIgnore]
        public int? Id { get; set; }

        [JsonIgnore]
        public bool? IsRead { get; set; }
    }

    public class MarkAllReadResponse
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = "All notifications marked as read";
        public int Count { get; set; }
        public int UpdatedCount
        {
            get => Count;
            set => Count = value;
        }
    }
}
