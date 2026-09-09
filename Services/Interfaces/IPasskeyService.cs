using System.Collections.Generic;
using System.Threading.Tasks;
using Fido2NetLib;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services.Interfaces
{
    public interface IPasskeyService
    {
        Task<CredentialCreateOptions> GetRegisterOptionsAsync(int userId, string? deviceName);
        Task<UserPasskeyDto> CompleteRegistrationAsync(int userId, PasskeyRegisterCompleteRequest request);
        Task<PasskeyLoginOptionsResponse> GetLoginOptionsAsync(string? email);
        Task<(string Token, int UserId, string Role, object UserData)> CompleteLoginAsync(PasskeyLoginCompleteRequest request, string? guestId);
        Task<List<UserPasskeyDto>> GetUserPasskeysAsync(int userId);
        Task<bool> DeletePasskeyAsync(int userId, int passkeyId);
        Task<bool> RenamePasskeyAsync(int userId, int passkeyId, string newName);
    }
}
