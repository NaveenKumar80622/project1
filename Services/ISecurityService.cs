using PickNBook.Api.Models.Entities;
using System.Threading.Tasks;

namespace PickNBook.Api.Services
{
    public interface ISecurityService
    {
        Task LogAuditAsync(string eventType, string action, string status, string ipAddress, string? userId = null, string? email = null, string? sessionId = null, string? reason = null);
        Task<SecuritySettings?> GetSettingsAsync();
        Task AddIpToBlacklistAsync(string ipAddress, string reason, bool isPermanent, System.DateTime? expiresAt, string? createdBy);
        Task RemoveIpFromBlacklistAsync(string ipAddress);
        Task QueueNotificationAsync(string eventType, string recipientType, string recipient, string subject, string? userId = null, string? ipAddress = null, string? cooldownKey = null);
        Task<PickNBook.Api.Models.DTOs.SecurityMetricsDto> GetDashboardMetricsAsync();
        Task<System.Collections.Generic.List<PickNBook.Api.Models.DTOs.RecentActivityDto>> GetRecentActivityAsync(int limit = 10);
        Task<System.Collections.Generic.List<SecurityLimit>> GetSecurityLimitsAsync(string scope);
        Task<bool> UpdateSecurityLimitAsync(long id, PickNBook.Api.Models.DTOs.UpdateSecurityLimitDto dto);
        Task<bool> BulkUpdateSecurityLimitsAsync(PickNBook.Api.Models.DTOs.BulkUpdateSecurityLimitDto dto);
        Task<System.Collections.Generic.List<SecurityAccountLock>> GetB2bWalletRestrictionsAsync();
        Task<bool> UnblockB2bWalletAsync(long id, string reason, string unblockedBy);

        // User Security Rules (No HTTP Method)
        Task<bool> AddUserBlockAsync(PickNBook.Api.Models.DTOs.AddUserBlockRequestDto dto, string createdBy);
        Task<System.Collections.Generic.List<long>> AddUserUrlBlockAsync(PickNBook.Api.Models.DTOs.AddUserUrlBlockRequestDto dto, string createdBy);
        Task<bool> UnblockUserRuleAsync(long ruleId, string reason, string unblockedBy);
        Task<bool> ExtendUserRuleAsync(long ruleId, int newDurationMinutes, string reason, string updatedBy);
        Task<bool> DeleteUserRuleAsync(long ruleId, string deletedBy);
        Task<System.Collections.Generic.List<PickNBook.Api.Models.DTOs.UserRuleResponseDto>> GetUserRulesAsync(string? userId, string? status, int page = 1, int pageSize = 20);
        Task<bool> IsUserBlockedAsync(string userId);
        Task<bool> IsUserUrlBlockedAsync(string userId, string route);
    }
}
