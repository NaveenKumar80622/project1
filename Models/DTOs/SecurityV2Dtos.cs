using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models.DTOs
{
    public class WhitelistIpRequest
    {
        public string IpAddress { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string WhitelistType { get; set; } = "TEMPORARY";
        public int DurationMinutes { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool SendEmail { get; set; }
        public long? EmailTemplateId { get; set; }
    }

    public class BlacklistIpRequest
    {
        public string IpAddress { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string BlockType { get; set; } = "TEMPORARY";
        public int DurationMinutes { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool SendEmail { get; set; }
        public long? EmailTemplateId { get; set; }
    }

    public class BlockIpRequest
    {
        public string IpAddress { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class UnblockIpRequest
    {
        public string UnblockAction { get; set; } = "UNBLOCK_ONLY"; // UNBLOCK_ONLY, UNBLOCK_AND_WHITELIST
        public string Reason { get; set; } = string.Empty;
        public bool SendEmail { get; set; }
    }

    public class ExtendBlockRequest
    {
        public int NewDurationMinutes { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class UpdateLimitRequest
    {
        public bool IsEnabled { get; set; }
        public int LimitValue { get; set; }
        public int? TimePeriodValue { get; set; }
        public string? TimePeriodUnit { get; set; }
        public string? AccountAction { get; set; }
        public string? IpAction { get; set; }
        public int? BlockDurationValue { get; set; }
        public string? BlockDurationUnit { get; set; }
        public bool EmailEnabled { get; set; }
        public long? EmailTemplateId { get; set; }
        public int? ResetPeriodValue { get; set; }
        public string? ResetPeriodUnit { get; set; }
    }

    public class BulkUpdateLimitRequest
    {
        public string Scope { get; set; } = string.Empty;
        public List<LimitRuleDto> Rules { get; set; } = new();
    }

    public class LimitRuleDto
    {
        public long Id { get; set; }
        public bool IsEnabled { get; set; }
        public int LimitValue { get; set; }
        public int? TimePeriodValue { get; set; }
        public string? TimePeriodUnit { get; set; }
        public string? AccountAction { get; set; }
        public string? IpAction { get; set; }
        public int? BlockDurationValue { get; set; }
        public string? BlockDurationUnit { get; set; }
        public bool EmailEnabled { get; set; }
        public long? EmailTemplateId { get; set; }
        public int? ResetPeriodValue { get; set; }
        public string? ResetPeriodUnit { get; set; }
    }

    public class AuthSettingRequest
    {
        public string Scope { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public object Settings { get; set; } = new();
    }

    public class UnlockAccountRequest
    {
        public string Reason { get; set; } = string.Empty;
    }

    public class LockAccountRequest
    {
        public long AccountId { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string LockStatus { get; set; } = string.Empty;
        public string LockReason { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public bool SendEmail { get; set; }
    }

    public class ResetCountersRequest
    {
        public string Scope { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class ApiRuleRequest
    {
        public string ApiName { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = string.Empty;
        public string UrlPattern { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public int? RateLimitValue { get; set; }
        public string? RateLimitPeriod { get; set; }
        public bool IsBlockable { get; set; }
        public bool IsPublic { get; set; }
        public bool IsException { get; set; }
        public string Status { get; set; } = "ACTIVE";
    }
}
