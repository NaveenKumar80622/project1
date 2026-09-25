using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class PasskeyService : IPasskeyService
    {
        private readonly IFido2 _fido2;
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IJwtService _jwtService;
        private readonly ILogger<PasskeyService> _logger;

        private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

        public PasskeyService(
            IFido2 fido2,
            AppDbContext context,
            IMemoryCache cache,
            IJwtService jwtService,
            ILogger<PasskeyService> logger)
        {
            _fido2 = fido2;
            _context = context;
            _cache = cache;
            _jwtService = jwtService;
            _logger = logger;
        }

        public async Task<CredentialCreateOptions> GetRegisterOptionsAsync(int userId, string? deviceName)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found.");

            var existingKeys = await _context.UserPasskeys
                .Where(p => p.UserId == userId)
                .Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))
                .ToListAsync();

            var userHandle = Encoding.UTF8.GetBytes(user.Id.ToString());

            var fidoUser = new Fido2User
            {
                Id = userHandle,
                Name = user.Email,
                DisplayName = string.IsNullOrWhiteSpace(user.FirstName) ? user.Email : $"{user.FirstName} {user.LastName}".Trim()
            };

            var authenticatorSelection = new AuthenticatorSelection
            {
                UserVerification = UserVerificationRequirement.Preferred,
                ResidentKey = ResidentKeyRequirement.Preferred
            };

            var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
            {
                User = fidoUser,
                ExcludeCredentials = existingKeys,
                AuthenticatorSelection = authenticatorSelection,
                AttestationPreference = AttestationConveyancePreference.None
            });

            string cacheKey = $"fido2_reg_opt_{userId}";
            _cache.Set(cacheKey, options, ChallengeLifetime);

            return options;
        }

        public async Task<UserPasskeyDto> CompleteRegistrationAsync(int userId, PasskeyRegisterCompleteRequest request)
        {
            if (request.AttestationResponse == null)
                throw new ArgumentException("Attestation response is required.", nameof(request));

            string cacheKey = $"fido2_reg_opt_{userId}";
            if (!_cache.TryGetValue(cacheKey, out CredentialCreateOptions? originalOptions) || originalOptions == null)
            {
                throw new InvalidOperationException("Registration session expired or challenge not found. Please try again.");
            }

            _cache.Remove(cacheKey); // Single-use challenge eviction

            var makeNewCredentialParams = new MakeNewCredentialParams
            {
                AttestationResponse = request.AttestationResponse,
                OriginalOptions = originalOptions,
                IsCredentialIdUniqueToUserCallback = async (args, cancellationToken) =>
                {
                    bool exists = await _context.UserPasskeys.AnyAsync(p => p.CredentialId == args.CredentialId, cancellationToken);
                    return !exists;
                }
            };

            var credential = await _fido2.MakeNewCredentialAsync(makeNewCredentialParams);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found.");

            string deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? "WebAuthn Authenticator" : request.DeviceName.Trim();

            var passkey = new UserPasskey
            {
                UserId = userId,
                CredentialId = credential.Id,
                PublicKey = credential.PublicKey,
                UserHandle = credential.User.Id,
                SignatureCounter = credential.SignCount,
                CredType = credential.Type.ToString(),
                AaGuid = credential.AaGuid != Guid.Empty ? credential.AaGuid : null,
                DeviceName = deviceName,
                CreatedAtUtc = DateTime.UtcNow,
                LastUsedAtUtc = DateTime.UtcNow
            };

            _context.UserPasskeys.Add(passkey);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully registered passkey {PasskeyId} for user {UserId} ({DeviceName})",
                passkey.Id, userId, deviceName);

            return new UserPasskeyDto
            {
                Id = passkey.Id,
                DeviceName = passkey.DeviceName,
                CredType = passkey.CredType,
                CreatedAtUtc = passkey.CreatedAtUtc,
                LastUsedAtUtc = passkey.LastUsedAtUtc
            };
        }

        public async Task<PasskeyLoginOptionsResponse> GetLoginOptionsAsync(string? email)
        {
            List<PublicKeyCredentialDescriptor>? allowedCredentials = null;

            if (!string.IsNullOrWhiteSpace(email))
            {
                var cleanEmail = email.Trim().ToLowerInvariant();
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);
                if (user != null)
                {
                    allowedCredentials = await _context.UserPasskeys
                        .Where(p => p.UserId == user.Id)
                        .Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))
                        .ToListAsync();
                }
            }

            var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
            {
                AllowedCredentials = allowedCredentials,
                UserVerification = UserVerificationRequirement.Preferred
            });

            string sessionId = Guid.NewGuid().ToString("N");
            string cacheKey = $"fido2_login_opt_{sessionId}";
            _cache.Set(cacheKey, options, ChallengeLifetime);

            return new PasskeyLoginOptionsResponse
            {
                SessionId = sessionId,
                Options = options
            };
        }

        public async Task<(string Token, int UserId, string Role, object UserData)> CompleteLoginAsync(PasskeyLoginCompleteRequest request, string? guestId)
        {
            if (string.IsNullOrWhiteSpace(request.SessionId) || request.AssertionResponse == null)
                throw new ArgumentException("SessionId and AssertionResponse are required.");

            string cacheKey = $"fido2_login_opt_{request.SessionId}";
            if (!_cache.TryGetValue(cacheKey, out AssertionOptions? originalOptions) || originalOptions == null)
            {
                throw new InvalidOperationException("Login session expired or challenge not found. Please try again.");
            }

            _cache.Remove(cacheKey); // Single-use challenge eviction

            byte[] credentialId = request.AssertionResponse.RawId;
            var passkey = await _context.UserPasskeys
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.CredentialId == credentialId);

            if (passkey == null || passkey.User == null)
            {
                throw new InvalidOperationException("Unknown credential or associated user not found.");
            }

            var user = passkey.User;
            if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("User account is inactive.");
            }

            var makeAssertionParams = new MakeAssertionParams
            {
                AssertionResponse = request.AssertionResponse,
                OriginalOptions = originalOptions,
                StoredPublicKey = passkey.PublicKey,
                StoredSignatureCounter = passkey.SignatureCounter,
                IsUserHandleOwnerOfCredentialIdCallback = (args, cancellationToken) =>
                {
                    // Verify that the user handle matches the user id
                    string userHandleStr = Encoding.UTF8.GetString(args.UserHandle);
                    bool match = string.Equals(userHandleStr, user.Id.ToString(), StringComparison.Ordinal);
                    return Task.FromResult(match);
                }
            };

            var res = await _fido2.MakeAssertionAsync(makeAssertionParams);

            // Update signature counter and last used timestamp
            passkey.SignatureCounter = res.SignCount;
            passkey.LastUsedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Migrate guest tracking if provided
            if (!string.IsNullOrWhiteSpace(guestId))
            {
                await MigrateGuestDataAsync(guestId, user.Id.ToString());
            }

            // Generate standard PickNBook JWT token
            string token = _jwtService.GenerateToken(user, user.Role);

            var userData = new
            {
                userId = user.Id.ToString(),
                name = user.Role == AuthRoles.Agent ? user.CompanyName : $"{user.FirstName} {user.LastName}".Trim(),
                email = user.Email,
                role = user.Role,
                walletBalance = user.WalletBalance,
                picknbookCoins = user.PicknbookCoins
            };

            _logger.LogInformation("Passkey authentication successful for user {UserId} ({Email}) using credential {PasskeyId}",
                user.Id, user.Email, passkey.Id);

            return (token, user.Id, user.Role, userData);
        }

        public async Task<List<UserPasskeyDto>> GetUserPasskeysAsync(int userId)
        {
            return await _context.UserPasskeys
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAtUtc)
                .Select(p => new UserPasskeyDto
                {
                    Id = p.Id,
                    DeviceName = p.DeviceName,
                    CredType = p.CredType,
                    CreatedAtUtc = p.CreatedAtUtc,
                    LastUsedAtUtc = p.LastUsedAtUtc
                })
                .ToListAsync();
        }

        public async Task<bool> DeletePasskeyAsync(int userId, int passkeyId)
        {
            var passkey = await _context.UserPasskeys
                .FirstOrDefaultAsync(p => p.Id == passkeyId && p.UserId == userId);

            if (passkey == null) return false;

            _context.UserPasskeys.Remove(passkey);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RenamePasskeyAsync(int userId, int passkeyId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("Device name cannot be empty.", nameof(newName));

            var passkey = await _context.UserPasskeys
                .FirstOrDefaultAsync(p => p.Id == passkeyId && p.UserId == userId);

            if (passkey == null) return false;

            passkey.DeviceName = newName.Trim();
            await _context.SaveChangesAsync();
            return true;
        }

        private async Task MigrateGuestDataAsync(string guestId, string userId)
        {
            try
            {
                if (Regex.IsMatch(guestId, @"^guest_[a-zA-Z0-9\-]+$"))
                {
                    await _context.FlightSearchLogs
                        .Where(x => x.UserOrGuestId == guestId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserOrGuestId, userId).SetProperty(x => x.IsGuest, false));

                    await _context.BusSearchLogs
                        .Where(x => x.UserOrGuestId == guestId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserOrGuestId, userId).SetProperty(x => x.IsGuest, false));

                    await _context.HotelSearchLogs
                        .Where(x => x.UserId == guestId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, userId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to migrate guest search data for {GuestId} to {UserId}", guestId, userId);
            }
        }
    }
}
