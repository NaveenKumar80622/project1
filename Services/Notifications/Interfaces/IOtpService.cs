using PickNBook.Api.Models;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Notifications.Interfaces
{
    public interface IOtpService
    {
        Task<(bool IsSuccess, string ChallengeId, string? ErrorMessage)> GenerateAndSendOtpAsync(string recipient, string channel, string purpose, int? userId = null);
        Task<(bool IsValid, string Message)> VerifyOtpAsync(string recipient, string purpose, string otpCode);
    }
}
