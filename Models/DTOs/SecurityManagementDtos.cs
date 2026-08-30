namespace PickNBook.Api.Models.DTOs
{
    public class GeneralSettingsDto
    {
        public int SessionTimeoutMinutes { get; set; } = 30;
        public int MaxConcurrentSessions { get; set; } = 1;
        public bool EnforceTwoFactorAuth { get; set; } = false;
        public string AllowedLoginHours { get; set; } = "00:00-23:59";
        public string AllowedIpRanges { get; set; } = "*";
    }

    public class PasswordPolicyDto
    {
        public int MinLength { get; set; } = 8;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireLowercase { get; set; } = true;
        public bool RequireNumbers { get; set; } = true;
        public bool RequireSpecialCharacters { get; set; } = true;
        public int PasswordExpirationDays { get; set; } = 90;
        public int PreventPasswordReuseCount { get; set; } = 5;
    }

    public class AccountLockoutPolicyDto
    {
        public int MaxFailedLoginAttempts { get; set; } = 5;
        public int LockoutDurationMinutes { get; set; } = 30;
        public int ResetFailedAttemptsAfterMinutes { get; set; } = 15;
        public bool NotifyAdminOnLockout { get; set; } = true;
        public bool NotifyUserOnLockout { get; set; } = true;
    }

    public class OtpVerificationSettingsDto
    {
        public int OtpExpirationMinutes { get; set; } = 5;
        public int MaxOtpRequestsPerDay { get; set; } = 10;
        public int MaxFailedOtpAttempts { get; set; } = 3;
        public int ResendOtpCooldownSeconds { get; set; } = 60;
    }

    public class SecuritySettingsDto
    {
        public GeneralSettingsDto GeneralSettings { get; set; } = new();
        public PasswordPolicyDto PasswordPolicy { get; set; } = new();
        public AccountLockoutPolicyDto AccountLockoutPolicy { get; set; } = new();
        public OtpVerificationSettingsDto OtpVerificationSettings { get; set; } = new();
    }

    // IP Rules
    public class IpRuleCreateDto
    {
        public string IpAddress { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class AddWhitelistDto
    {
        public string IpAddress { get; set; } = string.Empty;
        public string Scope { get; set; } = "USER";
        public string WhitelistType { get; set; } = "TEMPORARY";
        public int DurationMinutes { get; set; } = 1440;
        public string Reason { get; set; } = string.Empty;
        public bool SendEmail { get; set; } = true;
        public long? EmailTemplateId { get; set; }
    }

    public class AddBlacklistDto
    {
        public string IpAddress { get; set; } = string.Empty;
        public string Scope { get; set; } = "USER";
        public string BlockType { get; set; } = "TEMPORARY";
        public int DurationMinutes { get; set; } = 120;
        public string Reason { get; set; } = string.Empty;
        public bool SendEmail { get; set; } = true;
        public long? EmailTemplateId { get; set; }
    }

    public class AddBlockDto
    {
        public string IpAddress { get; set; } = string.Empty;
        public string Scope { get; set; } = "USER";
        public int DurationMinutes { get; set; } = 60;
        public string Reason { get; set; } = string.Empty;
    }

    public class UnblockIpDto
    {
        public string UnblockAction { get; set; } = "UNBLOCK_ONLY"; // UNBLOCK_ONLY, UNBLOCK_AND_WHITELIST
        public string Reason { get; set; } = string.Empty;
        public bool SendEmail { get; set; } = true;
    }

    public class ExtendBlockDto
    {
        public int NewDurationMinutes { get; set; } = 120;
        public string Reason { get; set; } = string.Empty;
    }

    public class IpRuleStatusUpdateDto
    {
        public string Status { get; set; } = string.Empty;
    }

    // Locked Accounts
    public class ManualLockDto
    {
        public string AccountId { get; set; } = string.Empty;
        public string LockReason { get; set; } = string.Empty;
        public int DurationMinutes { get; set; } = 60;
    }

    public class UnlockAccountDto
    {
        public bool ConfirmUnlock { get; set; }
        public string UnlockedBy { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class SecurityMetricsDto
    {
        public int ActiveLockouts { get; set; }
        public int BlacklistedIps { get; set; }
        public int ActiveBlockedIps { get; set; }
        public int WhitelistedIps { get; set; }
        public int AutomaticBlocks { get; set; }
        public int ManualBlocks { get; set; }
        public int ExpiredBlocks { get; set; }
        public int FailedLoginAttempts24h { get; set; }
        public int OtpViolations24h { get; set; }
        public int PasswordViolations24h { get; set; }
        public int RegistrationViolations24h { get; set; }
        public int ApiViolations24h { get; set; }
        public int UserRestrictions { get; set; }
        public int AdminRestrictions { get; set; }
        public int B2bRestrictions { get; set; }
    }
    public class RecentActivityDto
    {
        public long Id { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string AccountEmail { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public System.DateTime CreatedAt { get; set; }
    }

    public class UpdateSecurityLimitDto
    {
        public bool IsEnabled { get; set; }
        public int LimitValue { get; set; }
        public int TimePeriodValue { get; set; }
        public string TimePeriodUnit { get; set; } = "MINUTES";
        public string AccountAction { get; set; } = "NONE";
        public string IpAction { get; set; } = "NONE";
        public int BlockDurationValue { get; set; }
        public string BlockDurationUnit { get; set; } = "MINUTES";
        public bool EmailEnabled { get; set; }
        public long? EmailTemplateId { get; set; }
        public int ResetPeriodValue { get; set; }
        public string ResetPeriodUnit { get; set; } = "MINUTES";
    }

    public class BulkUpdateSecurityLimitDto
    {
        public string Scope { get; set; } = "USER";
        public System.Collections.Generic.List<UpdateSecurityLimitDtoItem> Rules { get; set; } = new();
    }

    public class UpdateSecurityLimitDtoItem : UpdateSecurityLimitDto
    {
        public long Id { get; set; }
    }
}
