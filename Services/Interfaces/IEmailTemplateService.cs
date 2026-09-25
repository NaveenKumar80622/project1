using System.Threading.Tasks;
using PickNBook.Api.Models.Entities;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IEmailTemplateService
    {
        Task SendTemplatedEmailAsync(string toEmail, string templateKey, object placeholderData, string? customSubject = null, string? customMessage = null);
        Task SendManualEmailAsync(string toEmail, int templateId, string? customSubject = null, string? customMessage = null, bool includeLoginLink = false);
        Task SendSecurityEmailAsync(string templateKey, User user, string ipAddress, string reason);
    }
}
