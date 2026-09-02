using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Services.Notifications.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Notifications.Implementations
{
    public class OtpService : IOtpService
    {
        private readonly AppDbContext _dbContext;
        private readonly INotificationService _notificationService;
        private readonly PickNBook.Api.Models.Config.NotificationRoutingSettings _routingSettings;

        public OtpService(AppDbContext dbContext, INotificationService notificationService, Microsoft.Extensions.Options.IOptions<PickNBook.Api.Models.Config.NotificationRoutingSettings> routingOptions)
        {
            _dbContext = dbContext;
            _notificationService = notificationService;
            _routingSettings = routingOptions.Value;
        }

        public async Task<(bool IsSuccess, string ChallengeId, string? ErrorMessage)> GenerateAndSendOtpAsync(string recipient, string channel, string purpose, int? userId = null)
        {
            // Simple random OTP
            string otpCode = new Random().Next(100000, 999999).ToString();
            string challengeId = Guid.NewGuid().ToString("N");
            string hash = HashOtp(otpCode);

            // Dynamically set expiry: 10 minutes for Login, 5 minutes for everything else
            int expiryMinutes = purpose == "Login" ? 10 : 5;

            var otpRecord = new PickNBook.Api.Models.OTP
            {
                UserId = userId,
                Code = hash,
                Purpose = purpose,
                Expiry = DateTime.UtcNow.AddMinutes(expiryMinutes),
                IsUsed = false,
                ChallengeId = challengeId
            };

            if (channel == "Email")
            {
                otpRecord.Email = recipient;
            }
            else
            {
                otpRecord.PhoneNumber = recipient;
            }

            _dbContext.OTPs.Add(otpRecord);
            await _dbContext.SaveChangesAsync();

            object payload;
            if (purpose == "Login" && channel == "SMS")
            {
                payload = new
                {
                    OtpCode = otpCode, // kept for backward compat / email channel
                    Var1 = _routingSettings.LoginOtpAppName ?? "ShyamAgro", // DLT ${var1}
                    Var2 = otpCode // DLT ${var2}
                };
            }
            else
            {
                payload = new { OtpCode = otpCode };
            }

            // Determine template based on purpose
            string templateKey = purpose switch
            {
                "Registration" => "REGISTRATION_OTP",
                "Login" => "LOGIN_OTP",
                "PasswordReset" => "PASSWORD_RESET_OTP",
                "B2BPasswordReset" => "PASSWORD_RESET_OTP",
                "AdminLogin" => "ADMIN_OTP",
                _ => "GENERIC_OTP"
            };

            var (isSent, errorMessage) = await _notificationService.SendImmediateAsync(
                eventType: purpose,
                channel: channel,
                recipient: recipient,
                templateKey: templateKey,
                payload: payload
            );

            return (isSent, challengeId, errorMessage);
        }

        public async Task<(bool IsValid, string Message)> VerifyOtpAsync(string recipient, string purpose, string otpCode)
        {
            var otpRecord = await _dbContext.OTPs
                .Where(o => (o.Email == recipient || o.PhoneNumber == recipient) 
                            && o.Purpose == purpose 
                            && !o.IsUsed)
                .OrderByDescending(o => o.Expiry)
                .FirstOrDefaultAsync();

            if (otpRecord == null)
            {
                return (false, "OTP not found or already used.");
            }

            if (otpRecord.Expiry < DateTime.UtcNow)
            {
                return (false, "OTP has expired.");
            }

            if (otpRecord.Code != HashOtp(otpCode))
            {
                return (false, "Invalid OTP.");
            }

            otpRecord.IsUsed = true;
            otpRecord.IsVerified = true;
            await _dbContext.SaveChangesAsync();

            return (true, "OTP verified successfully.");
        }

        private string HashOtp(string otp)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(otp);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
