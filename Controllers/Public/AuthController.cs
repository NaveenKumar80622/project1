using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Constants;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using PickNBook.Api.Services.Implementations;
using System.Security.Claims;
using System.Security.Cryptography;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IEmailService _emailService;
        private readonly ISmsService _smsService;
        private readonly PickNBook.Api.Services.Notifications.Interfaces.IOtpService _otpService;
        private readonly PasswordHasher<User> _passwordHasher;
        private readonly ILogger<AuthController> _logger;
        private readonly int _adminOtpExpiryMinutes;
        private readonly int _adminMaxOtpAttempts;
        private const string AdminLoginOtpPurpose = "AdminLogin";
        private const string AdminPasswordResetOtpPurpose = "AdminPasswordReset";

        public AuthController(
            AppDbContext context,
            IJwtService jwtService,
            IEmailService emailService,
            ISmsService smsService,
            PickNBook.Api.Services.Notifications.Interfaces.IOtpService otpService,
            IConfiguration configuration,
            ILogger<AuthController> logger = null)
        {
            _context = context;
            _jwtService = jwtService;
            _emailService = emailService;
            _smsService = smsService;
            _otpService = otpService;
            _passwordHasher = new PasswordHasher<User>();
            _logger = logger;

            _adminOtpExpiryMinutes = Math.Clamp(
                configuration.GetValue<int?>("AdminAuth:OtpExpiryMinutes") ?? 5,
                1,
                30);

            _adminMaxOtpAttempts = Math.Clamp(
                configuration.GetValue<int?>("AdminAuth:MaxOtpAttempts") ?? 5,
                1,
                10);
        }

        private string GenerateOtp()
        {
            return RandomNumberGenerator
                .GetInt32(100000, 1000000)
                .ToString();
        }

        [HttpPost("send-registration-otp")]
        public async Task<IActionResult> SendRegistrationOtp(SendRegistrationOtpRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var isMobile = string.Equals(request.Channel, "Mobile", StringComparison.OrdinalIgnoreCase);
            var channel = isMobile ? "SMS" : "Email";

            if (isMobile)
            {
                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return BadRequest(new { success = false, message = "Phone number is required for Mobile OTP." });
                }

                var normalizedPhone = request.PhoneNumber.Trim();
                var existingUser = await _context.Users.AnyAsync(x => x.PhoneNumber == normalizedPhone);

                if (existingUser)
                {
                    return BadRequest(new { success = false, message = "Phone number already registered" });
                }

                var oldOtps = _context.OTPs.Where(x => x.PhoneNumber == normalizedPhone && x.Purpose == OtpPurposes.Registration && !x.IsUsed);
                _context.OTPs.RemoveRange(oldOtps);
                await _context.SaveChangesAsync();

                var (isSent, _, errorMessage) = await _otpService.GenerateAndSendOtpAsync(normalizedPhone, channel, OtpPurposes.Registration);

                if (!isSent) return BadRequest(new { success = false, message = "Unable to send registration OTP.", error = errorMessage ?? "SMS provider rejected the request." });

                return Ok(new { success = true, message = "OTP sent successfully" });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Email))
                {
                    return BadRequest(new { success = false, message = "Email is required for Email OTP." });
                }

                var normalizedEmail = request.Email.Trim().ToLowerInvariant();
                var existingUser = await _context.Users.AnyAsync(x => x.Email.ToLower() == normalizedEmail);

                if (existingUser)
                {
                    return BadRequest(new { success = false, message = "Email already registered" });
                }

                var oldOtps = _context.OTPs.Where(x => x.Email == normalizedEmail && x.Purpose == OtpPurposes.Registration && !x.IsUsed);
                _context.OTPs.RemoveRange(oldOtps);
                await _context.SaveChangesAsync();

                var (isSent, _, errorMessage) = await _otpService.GenerateAndSendOtpAsync(normalizedEmail, channel, OtpPurposes.Registration);

                if (!isSent) return BadRequest(new { success = false, message = "Unable to send registration OTP.", error = errorMessage ?? "Email provider rejected the request." });

                return Ok(new { success = true, message = "OTP sent successfully" });
            }
        }

        [HttpPost("verify-registration-otp")]
        public async Task<IActionResult> VerifyRegistrationOtp(VerifyRegistrationOtpRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var isMobile = string.Equals(request.Channel, "Mobile", StringComparison.OrdinalIgnoreCase);
            var recipient = isMobile ? request.PhoneNumber?.Trim() : request.Email?.Trim()?.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(recipient))
            {
                return BadRequest(new { success = false, message = isMobile ? "Phone number is required." : "Email is required." });
            }

            var (isValid, message) = await _otpService.VerifyOtpAsync(recipient, OtpPurposes.Registration, request.Otp);

            if (!isValid)
            {
                return BadRequest(new { success = false, message = message });
            }

            return Ok(new { success = true, message = "OTP verified successfully" });
        }

        // ---------------- B2B FORGOT PASSWORD (SEND OTP) ----------------
        [HttpPost("b2b/forgot-password/send-otp")]
        public async Task<IActionResult> B2BForgotPasswordSendOtp(ForgotPasswordSendOtpRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            if (User.Identity?.IsAuthenticated == true)
            {
                var loggedInEmail = User.FindFirstValue(ClaimTypes.Email)?.ToLowerInvariant();
                if (!string.Equals(loggedInEmail, normalizedEmail))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Wrong email: Please enter your registered logged-in email."
                    });
                }
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);

            if (user == null || user.Role != AuthRoles.Agent)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Email is not registered as a B2B Agent."
                });
            }

            if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Your agent account is not active. Please contact support."
                });
            }

            var oldOtps = _context.OTPs.Where(x => x.Email == normalizedEmail && x.Purpose == OtpPurposes.PasswordReset && !x.IsUsed);
            _context.OTPs.RemoveRange(oldOtps);
            await _context.SaveChangesAsync();

            var (isSent, _, errorMessage) = await _otpService.GenerateAndSendOtpAsync(normalizedEmail, "Email", OtpPurposes.PasswordReset, user.Id);

            if (!isSent) return BadRequest(new { success = false, message = "Unable to send OTP.", error = errorMessage ?? "Email provider rejected the request." });

            return Ok(new { success = true, message = "OTP sent successfully" });
        }

        // ---------------- B2B FORGOT PASSWORD (VERIFY OTP) ----------------
        [HttpPost("b2b/forgot-password/verify-otp")]
        public async Task<IActionResult> B2BForgotPasswordVerifyOtp(ForgotPasswordVerifyOtpRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail && x.Role == AuthRoles.Agent);

            if (user == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Agent not found"
                });
            }

            var (isValid, message) = await _otpService.VerifyOtpAsync(normalizedEmail, OtpPurposes.PasswordReset, request.Otp);

            if (!isValid)
            {
                return BadRequest(new { success = false, message = message });
            }

            return Ok(new { success = true, message = "OTP verified successfully" });
        }

        [HttpPost("forgot-password/send-otp")]
        public async Task<IActionResult> ForgotPasswordSendOtp(ForgotPasswordSendOtpRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var isMobile = string.Equals(request.Channel, "Mobile", StringComparison.OrdinalIgnoreCase);
            var channel = isMobile ? "SMS" : "Email";

            if (isMobile)
            {
                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return BadRequest(new { success = false, message = "Phone number is required for Mobile OTP." });
                }

                var normalizedPhone = request.PhoneNumber.Trim();

                if (User.Identity?.IsAuthenticated == true)
                {
                    var loggedInPhone = User.FindFirstValue(ClaimTypes.MobilePhone);
                    if (!string.IsNullOrEmpty(loggedInPhone) && !string.Equals(loggedInPhone, normalizedPhone))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Wrong phone number: Please enter your registered logged-in phone number."
                        });
                    }
                }

                var user = await _context.Users
                    .FirstOrDefaultAsync(x => 
                        (x.PhoneNumber == normalizedPhone || x.PhoneNumber == "+91" + normalizedPhone || x.PhoneNumber == "91" + normalizedPhone) && 
                        x.Role == AuthRoles.User);

                if (user == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Phone number not registered"
                    });
                }

                if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
                {
                    return Unauthorized(new { success = false, message = "Your account is inactive. Please contact support." });
                }

                var oldOtps = _context.OTPs.Where(x => x.PhoneNumber == normalizedPhone && x.Purpose == OtpPurposes.PasswordReset && !x.IsUsed);
                _context.OTPs.RemoveRange(oldOtps);
                await _context.SaveChangesAsync();

                var (isSent, _, errorMessage) = await _otpService.GenerateAndSendOtpAsync(normalizedPhone, channel, OtpPurposes.PasswordReset, user.Id);

                if (!isSent) return BadRequest(new { success = false, message = "Unable to send password reset OTP.", error = errorMessage ?? "SMS provider rejected the request." });

                return Ok(new { success = true, message = "OTP sent successfully" });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Email))
                {
                    return BadRequest(new { success = false, message = "Email is required for Email OTP." });
                }

                var normalizedEmail = request.Email.Trim().ToLowerInvariant();

                if (User.Identity?.IsAuthenticated == true)
                {
                    var loggedInEmail = User.FindFirstValue(ClaimTypes.Email)?.ToLowerInvariant();
                    if (!string.Equals(loggedInEmail, normalizedEmail))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Wrong email: Please enter your registered logged-in email."
                        });
                    }
                }

                var user = await _context.Users
                    .FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);

                if (user == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Email not registered"
                    });
                }

                var oldOtps = _context.OTPs.Where(x => x.Email == normalizedEmail && x.Purpose == OtpPurposes.PasswordReset && !x.IsUsed);
                _context.OTPs.RemoveRange(oldOtps);
                await _context.SaveChangesAsync();

                var (isSent, _, errorMessage) = await _otpService.GenerateAndSendOtpAsync(normalizedEmail, "Email", OtpPurposes.PasswordReset, user.Id);

                if (!isSent) return BadRequest(new { success = false, message = "Unable to send OTP.", error = errorMessage ?? "Email provider rejected the request." });

                return Ok(new { success = true, message = "OTP sent successfully" });
            }
        }

        [HttpPost("forgot-password/verify-otp")]
        public async Task<IActionResult> ForgotPasswordVerifyOtp(ForgotPasswordVerifyOtpRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var isMobile = string.Equals(request.Channel, "Mobile", StringComparison.OrdinalIgnoreCase);
            var recipient = isMobile ? request.PhoneNumber?.Trim() : request.Email?.Trim()?.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(recipient))
            {
                return BadRequest(new { success = false, message = isMobile ? "Phone number is required." : "Email is required." });
            }

            var (isValid, message) = await _otpService.VerifyOtpAsync(recipient, OtpPurposes.PasswordReset, request.Otp);

            if (!isValid)
            {
                return BadRequest(new { success = false, message = message });
            }

            return Ok(new { success = true, message = "OTP verified successfully" });
        }

        // ---------------- B2B REGISTER ----------------
        [HttpPost("b2b/register")]
        public async Task<IActionResult> B2BRegister(B2BRegisterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var normalizedPhone = request.PhoneNumber.Trim();

            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Email already exists"
                });
            }

            if (!string.IsNullOrEmpty(normalizedPhone) && await _context.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Phone number already exists"
                });
            }

            // Split ContactName to FirstName and LastName
            string firstName;
            string lastName;
            var parts = request.ContactName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                firstName = parts[0];
                lastName = parts[1];
            }
            else
            {
                firstName = request.ContactName.Trim();
                lastName = string.Empty;
            }

            var user = new User
            {
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = normalizedPhone,
                Email = normalizedEmail,
                Role = AuthRoles.Agent,
                Status = "PendingApproval", // B2B Agents must be approved by admin
                WalletStatus = "Active",
                WalletBalance = 0.00m,
                CompanyName = request.CompanyName.Trim(),
                BusinessType = request.BusinessType.Trim(),
                Gstin = request.Gstin.Trim(),
                City = request.City.Trim()
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Registration request submitted successfully. Redirecting to login..."
            });
        }

        // ---------------- REGISTER ----------------
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var normalizedPhone = request.PhoneNumber.Trim();

            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Email already exists"
                });
            }

            if (!string.IsNullOrEmpty(normalizedPhone) && await _context.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Phone number already exists"
                });
            }

            // Find a verified OTP for either this email or this phone number
            var verifiedOtp = await _context.OTPs
                .FirstOrDefaultAsync(x =>
                    x.Purpose == OtpPurposes.Registration &&
                    x.IsVerified &&
                    x.Expiry > DateTime.UtcNow &&
                    ((x.Email == normalizedEmail && !string.IsNullOrEmpty(x.Email)) || 
                     (x.PhoneNumber == normalizedPhone && !string.IsNullOrEmpty(x.PhoneNumber))));

            if (verifiedOtp == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "OTP verification required"
                });
            }

            var user = new User
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                PhoneNumber = normalizedPhone,
                Email = normalizedEmail,
                Role = AuthRoles.User
            };

            user.PasswordHash =
                _passwordHasher.HashPassword(user, request.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            verifiedOtp.IsUsed = true;

            // Mark other registration/reset OTPs as used
            await _context.OTPs
                .Where(x =>
                    (x.Email == normalizedEmail || (!string.IsNullOrEmpty(x.PhoneNumber) && x.PhoneNumber == normalizedPhone)) &&
                    (x.Purpose == OtpPurposes.Registration || x.Purpose == OtpPurposes.PasswordReset) &&
                    !x.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.IsUsed, true));

            await _context.SaveChangesAsync();

            var guestId = HttpContext.Request.Headers["X-Guest-Id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(guestId))
            {
                await MigrateGuestDataAsync(guestId, user.Id.ToString());
            }

            return Ok(new
            {
                success = true,
                message = "User registered successfully"
            });
        }

        // ---------------- LOCKOUT HELPERS ----------------
        private string GetClientIp()
        {
            var forwarded = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded))
            {
                return forwarded.Split(',')[0].Trim();
            }
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        private async Task<(bool IsLocked, string LockMessage)> CheckAndHandleExistingLockAsync(User user, string ipAddress)
        {
            var now = DateTime.UtcNow;
            var lockout = await _context.UserLockouts
                .Where(l => l.UserId == user.Id.ToString() && l.Status == "Locked")
                .OrderByDescending(l => l.LockedOn)
                .FirstOrDefaultAsync();

            if (lockout == null)
            {
                return (false, string.Empty);
            }

            if (lockout.UnlockAt <= now)
            {
                // Lock expired - auto-unlock
                lockout.Status = "Unlocked";
                lockout.FailedAttempts = 0;
                await _context.SaveChangesAsync();
                _logger?.LogInformation("User {UserId} ({Email}) account auto-unlocked after expiry.", user.Id, user.Email);
                return (false, string.Empty);
            }

            // Lock is actively blocking
            string message;
            if ((lockout.UnlockAt - lockout.LockedOn).TotalMinutes > 30 || (lockout.Reason != null && lockout.Reason.Contains("24 hours")))
            {
                message = "Your account has been blocked due to multiple attempts. Try after 24 hours.";
            }
            else
            {
                message = "Your account has been blocked due to multiple attempts. Try after 15 minutes.";
            }

            return (true, message);
        }

        private async Task<IActionResult> HandleFailedAuthenticationAsync(
            User user,
            string failureType,
            string ipAddress,
            string? customErrorMessage = null)
        {
            var now = DateTime.UtcNow;
            var istZone = DailyAdminSummaryService.GetIstTimeZoneStatic();
            var istNow = TimeZoneInfo.ConvertTimeFromUtc(now, istZone);
            var istStartOfDay = istNow.Date;
            var utcStartOfDay = TimeZoneInfo.ConvertTimeToUtc(istStartOfDay, istZone);

            // Fetch or create tracking lockout record
            var tracker = await _context.UserLockouts
                .Where(l => l.UserId == user.Id.ToString() && l.Status == "Unlocked")
                .OrderByDescending(l => l.LockedOn)
                .FirstOrDefaultAsync();

            if (tracker == null)
            {
                tracker = new UserLockout
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = user.Id.ToString(),
                    UserName = $"{user.FirstName} {user.LastName}".Trim(),
                    Email = user.Email ?? string.Empty,
                    FailedAttempts = 0,
                    MaxAllowedAttempts = 3,
                    Status = "Unlocked",
                    LockedOn = now,
                    UnlockAt = now,
                    Reason = "Failed attempt tracker"
                };
                _context.UserLockouts.Add(tracker);
            }

            // If previous failed attempts occurred before today (IST), reset count
            if (tracker.LockedOn < utcStartOfDay)
            {
                tracker.FailedAttempts = 0;
            }

            tracker.FailedAttempts++;
            tracker.LockedOn = now;

            if (tracker.FailedAttempts >= 3)
            {
                // Count prior lockouts today
                var previousLocksToday = await _context.UserLockouts
                    .CountAsync(l => l.UserId == user.Id.ToString() &&
                                     l.LockedOn >= utcStartOfDay &&
                                     (l.Reason.Contains("15 minutes") || l.Reason.Contains("24 hours")));

                bool isSecondLock = previousLocksToday >= 1;
                var lockDuration = isSecondLock ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(15);
                var unlockAt = now.Add(lockDuration);

                string lockReason = isSecondLock
                    ? "Account blocked for 24 hours due to multiple failed attempts."
                    : "Account blocked for 15 minutes due to multiple failed attempts.";

                string lockMessage = isSecondLock
                    ? "Your account has been blocked due to multiple attempts. Try after 24 hours."
                    : "Your account has been blocked due to multiple attempts. Try after 15 minutes.";

                string rawName = $"{user.FirstName} {user.LastName}".Trim();
                string displayName = string.IsNullOrWhiteSpace(rawName) ? "Valued Customer" : rawName;
                string encodedName = System.Net.WebUtility.HtmlEncode(displayName);
                string durationText = isSecondLock ? "24 hours" : "15 minutes";
                string formattedDate = now.ToString("dd MMMM yyyy, hh:mm tt 'UTC'");
                int currentYear = now.Year;

                string emailSubject = isSecondLock
                    ? "Security Alert: Your Pick&book Account Has Been Blocked for 24 Hours"
                    : "Security Alert: Your Pick&book Account Has Been Temporarily Blocked";

                string emailBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f8fafc; margin: 0; padding: 20px; }}
        .container {{ max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.08); border: 1px solid #e2e8f0; }}
        .header {{ background: linear-gradient(135deg, #991b1b 0%, #dc2626 100%); padding: 32px 24px; text-align: center; color: #ffffff; }}
        .header h1 {{ margin: 0; font-size: 24px; font-weight: 700; letter-spacing: 0.5px; }}
        .header p {{ margin: 6px 0 0; font-size: 14px; opacity: 0.9; }}
        .content {{ padding: 32px 24px; color: #334155; line-height: 1.6; font-size: 15px; }}
        .greeting {{ font-size: 16px; font-weight: 600; margin-bottom: 16px; color: #0f172a; }}
        .card {{ background: #fef2f2; border: 1px solid #fee2e2; border-radius: 8px; padding: 20px; margin: 24px 0; }}
        .card-title {{ font-weight: 700; color: #991b1b; font-size: 14px; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 12px; }}
        .table {{ width: 100%; border-collapse: collapse; }}
        .table td {{ padding: 10px 0; border-bottom: 1px solid #fecaca; font-size: 14px; }}
        .table tr:last-child td {{ border-bottom: none; }}
        .table td:first-child {{ color: #7f1d1d; width: 40%; }}
        .table td:last-child {{ text-align: right; font-weight: 600; color: #0f172a; }}
        .status-badge {{ display: inline-block; background-color: #ef4444; color: #ffffff; padding: 3px 10px; border-radius: 12px; font-size: 12px; font-weight: 700; }}
        .btn-wrapper {{ text-align: center; margin: 30px 0; }}
        .btn {{ display: inline-block; background: #dc2626; color: #ffffff !important; padding: 14px 32px; border-radius: 8px; text-decoration: none; font-weight: 600; font-size: 15px; box-shadow: 0 4px 10px rgba(220,38,38,0.25); }}
        .footer {{ padding: 24px; text-align: center; font-size: 12px; color: #94a3b8; background-color: #f8fafc; border-top: 1px solid #f1f5f9; line-height: 1.5; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Pick&amp;book</h1>
            <p>Account Security Notification</p>
        </div>
        <div class='content'>
            <div class='greeting'>Hello {encodedName},</div>
            <p>For your security, your Pick&amp;book account has been temporarily blocked after multiple unsuccessful login attempts.</p>
            <p>Your account will remain blocked for <strong>{durationText}</strong>. After this period, you can try signing in again.</p>
            
            <div class='card'>
                <div class='card-title'>Account Security Details</div>
                <table class='table'>
                    <tr>
                        <td>Status</td>
                        <td><span class='status-badge'>Temporarily Blocked</span></td>
                    </tr>
                    <tr>
                        <td>Reason</td>
                        <td>Multiple failed login attempts</td>
                    </tr>
                    <tr>
                        <td>Lock Duration</td>
                        <td>{durationText}</td>
                    </tr>
                    <tr>
                        <td>Effective Date</td>
                        <td>{formattedDate}</td>
                    </tr>
                </table>
            </div>

            <div class='btn-wrapper'>
                <a href='https://picknbook.in/login' class='btn' target='_blank'>Sign In to Pick&amp;book</a>
            </div>

            <p style='font-size: 13px; color: #64748b; margin-top: 25px;'>
                If you believe this activity was not performed by you, please contact Pick&amp;book Support immediately to safeguard your account.
            </p>

            <p style='margin-bottom: 0;'>
                Best regards,<br>
                <strong>Pick&amp;book Security Team</strong>
            </p>
        </div>
        <div class='footer'>
            &copy; {currentYear} Pick&amp;book. All rights reserved.<br>
            This is an automated security notification. Please do not reply to this email.
        </div>
    </div>
</body>
</html>";

                // Create the actual lockout record
                var lockoutRecord = new UserLockout
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = user.Id.ToString(),
                    UserName = $"{user.FirstName} {user.LastName}".Trim(),
                    Email = user.Email ?? string.Empty,
                    FailedAttempts = tracker.FailedAttempts,
                    MaxAllowedAttempts = 3,
                    LockedOn = now,
                    UnlockAt = unlockAt,
                    Reason = lockReason,
                    Status = "Locked"
                };
                _context.UserLockouts.Add(lockoutRecord);

                // Reset tracker failed attempts
                tracker.FailedAttempts = 0;
                await _context.SaveChangesAsync();

                _logger?.LogWarning("Account locked for User #{UserId} ({Email}). Lock type: {LockType}. IP: {Ip}",
                    user.Id, user.Email, isSecondLock ? "24 Hours" : "15 Minutes", ipAddress);

                // Send lockout notification email in try/catch (non-fatal)
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    try
                    {
                        await _emailService.SendEmailAsync(user.Email, emailSubject, emailBody);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to send account lockout email to {Email} for user #{UserId}. Non-fatal.",
                            user.Email, user.Id);
                    }
                }

                return StatusCode(StatusCodes.Status401Unauthorized, new
                {
                    success = false,
                    message = lockMessage
                });
            }

            // Not yet reached 3 attempts
            await _context.SaveChangesAsync();

            if (failureType == "FAILED_OTP")
            {
                return BadRequest(new { success = false, message = customErrorMessage ?? "Invalid OTP." });
            }

            return Unauthorized("Invalid credentials");
        }

        private async Task ResetFailedAttemptsOnSuccessAsync(User user)
        {
            var tracker = await _context.UserLockouts
                .Where(l => l.UserId == user.Id.ToString() && l.Status == "Unlocked" && l.FailedAttempts > 0)
                .OrderByDescending(l => l.LockedOn)
                .FirstOrDefaultAsync();

            if (tracker != null)
            {
                tracker.FailedAttempts = 0;
                await _context.SaveChangesAsync();
            }
        }

        // ---------------- LOGIN ----------------
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var ip = GetClientIp();
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null)
                return Unauthorized("Invalid credentials");

            var lockCheck = await CheckAndHandleExistingLockAsync(user, ip);
            if (lockCheck.IsLocked)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new { success = false, message = lockCheck.LockMessage });
            }

            var result = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

            if (result == PasswordVerificationResult.Failed)
            {
                return await HandleFailedAuthenticationAsync(user, "FAILED_LOGIN", ip);
            }

            await ResetFailedAttemptsOnSuccessAsync(user);

            if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized("Your account is inactive. Please contact support.");
            }

            if (AuthRoles.IsAdminScope(user.Role))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Admin users must login using admin OTP flow.");
            }

            var token = _jwtService.GenerateToken(user, user.Role);

            var guestId = HttpContext.Request.Headers["X-Guest-Id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(guestId))
            {
                await MigrateGuestDataAsync(guestId, user.Id.ToString());
            }

            return Ok(new
            {
                token,
                userId = user.Id,
                role = user.Role,
                user = new
                {
                    userId = user.Id.ToString(),
                    name = user.Role == AuthRoles.Agent ? user.CompanyName : $"{user.FirstName} {user.LastName}",
                    email = user.Email,
                    role = user.Role
                }
            });
        }

        // ---------------- MOBILE OTP LOGIN ----------------
        [HttpPost("send-login-otp")]
        public async Task<IActionResult> SendLoginOtp(SendLoginOtpRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var ip = GetClientIp();
            var normalizedPhone = request.PhoneNumber.Trim();

            var user = await _context.Users
     .FirstOrDefaultAsync(u =>
         u.PhoneNumber == normalizedPhone &&
         u.Role == "User");

            if (user == null)
                return Unauthorized(new { success = false, message = "Mobile number not registered." });

            var lockCheck = await CheckAndHandleExistingLockAsync(user, ip);
            if (lockCheck.IsLocked)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new { success = false, message = lockCheck.LockMessage });
            }

            if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
                return Unauthorized(new { success = false, message = "Your account is inactive. Please contact support." });

            if (AuthRoles.IsAdminScope(user.Role))
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Admin users must login using admin OTP flow." });

            await _context.OTPs
                .Where(o =>
                    o.PhoneNumber == normalizedPhone &&
                    o.Purpose == OtpPurposes.Login &&
                    !o.IsUsed)
                .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.IsUsed, true));

            var (isSent, _, errorMessage) = await _otpService.GenerateAndSendOtpAsync(normalizedPhone, "SMS", OtpPurposes.Login, user.Id);

            if (!isSent)
            {
                return BadRequest(new { success = false, message = "Unable to send login OTP.", error = errorMessage ?? "SMS provider rejected the request." });
            }

            return Ok(new { success = true, message = "Login OTP sent successfully." });
        }

        [HttpPost("verify-login-otp")]
        public async Task<IActionResult> VerifyLoginOtp(VerifyLoginOtpRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var ip = GetClientIp();
            var normalizedPhone = request.PhoneNumber.Trim();

            var user = await _context.Users
     .FirstOrDefaultAsync(u =>
         u.PhoneNumber == normalizedPhone &&
         u.Role == "User");

            if (user != null)
            {
                var lockCheck = await CheckAndHandleExistingLockAsync(user, ip);
                if (lockCheck.IsLocked)
                {
                    return StatusCode(StatusCodes.Status401Unauthorized, new { success = false, message = lockCheck.LockMessage });
                }
            }

            var (isValid, message) = await _otpService.VerifyOtpAsync(normalizedPhone, OtpPurposes.Login, request.Otp);

            if (!isValid)
            {
                if (user != null)
                {
                    return await HandleFailedAuthenticationAsync(user, "FAILED_OTP", ip, message);
                }
                return BadRequest(new { success = false, message = message });
            }

            if (user == null)
                return Unauthorized(new { success = false, message = "User not found." });

            await ResetFailedAttemptsOnSuccessAsync(user);

            if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
                return Unauthorized(new { success = false, message = "Your account is inactive. Please contact support." });

            var token = _jwtService.GenerateToken(user, user.Role);

            var guestId = HttpContext.Request.Headers["X-Guest-Id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(guestId))
            {
                await MigrateGuestDataAsync(guestId, user.Id.ToString());
            }

            return Ok(new
            {
                token,
                userId = user.Id,
                role = user.Role,
                user = new
                {
                    userId = user.Id.ToString(),
                    name = user.Role == AuthRoles.Agent ? user.CompanyName : $"{user.FirstName} {user.LastName}",
                    email = user.Email,
                    role = user.Role
                }
            });
        }

        // ---------------- ADMIN LOGIN (DIRECT EMAIL & PASSWORD) ----------------
        [HttpPost("admin/login")]
        [HttpPost("admin/login/request-otp")]
        public async Task<IActionResult> RequestAdminLoginOtp(AdminLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Email and password are required.");
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null || !AuthRoles.IsAdminScope(user.Role))
            {
                return Unauthorized("Invalid admin credentials.");
            }

            var passwordResult = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

            if (passwordResult == PasswordVerificationResult.Failed)
            {
                return Unauthorized("Invalid admin credentials.");
            }

            if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized("Your account is inactive. Please contact support.");
            }

            // Invalidate any old unused admin login OTPs
            await _context.OTPs
                .Where(o =>
                    o.UserId == user.Id &&
                    o.Purpose == AdminLoginOtpPurpose &&
                    !o.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(o => o.IsUsed, true));

            var token = _jwtService.GenerateToken(user, user.Role);

            return Ok(new
            {
                success = true,
                message = "Admin login successful.",
                token,
                userId = user.Id,
                role = user.Role,
                user = new
                {
                    userId = user.Id.ToString(),
                    name = $"{user.FirstName} {user.LastName}".Trim(),
                    email = user.Email,
                    role = user.Role
                }
            });
        }

        // ---------------- ADMIN LOGIN STEP-2 (VERIFY OTP -> RETAINED FOR COMPATIBILITY) ----------------
        [Obsolete("Admin login now authenticates directly in step 1. This endpoint is retained for route compatibility only.")]
        [HttpPost("admin/login/verify-otp")]
        public Task<IActionResult> VerifyAdminLoginOtp(AdminLoginVerifyOtpRequest request)
        {
            return Task.FromResult<IActionResult>(Ok(new
            {
                success = true,
                message = "Admin OTP verification is no longer required. Direct login is active via /api/Auth/admin/login/request-otp."
            }));
        }

        // ---------------- ADMIN FORGOT PASSWORD ----------------
        [HttpPost("admin/forgot-password")]
        public async Task<IActionResult> AdminForgotPassword(ForgotPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest("Email is required.");
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null)
            {
                return BadRequest(new { success = false, message = "Email is not registered." });
            }

            if (!AuthRoles.IsAdminScope(user.Role))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Access denied. User does not have administrator privileges." });
            }

            if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(new { success = false, message = "Admin account is inactive. Please contact support." });
            }

            await _context.OTPs
                .Where(o =>
                    o.UserId == user.Id &&
                    o.Purpose == AdminPasswordResetOtpPurpose &&
                    !o.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(o => o.IsUsed, true));

            var (isSent, _, errorMessage) = await _otpService.GenerateAndSendOtpAsync(normalizedEmail, "Email", AdminPasswordResetOtpPurpose, user.Id);

            if (!isSent)
            {
                return BadRequest(new { success = false, message = "Unable to send admin password reset OTP.", error = errorMessage ?? "Email provider rejected the request." });
            }

            return Ok(new { success = true, message = "Admin account verified. Password reset code has been sent to your registered email." });
        }

        // ---------------- ADMIN RESET PASSWORD ----------------
        [HttpPost("admin/reset-password")]
        public async Task<IActionResult> AdminResetPassword(AdminResetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Otp) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { success = false, message = "Email, OTP and new password are required." });
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null)
            {
                return BadRequest(new { success = false, message = "Email is not registered." });
            }

            if (!AuthRoles.IsAdminScope(user.Role))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Access denied. Invalid administrator account." });
            }

            if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(new { success = false, message = "Admin account is inactive. Please contact support." });
            }

            var (isValid, message) = await _otpService.VerifyOtpAsync(normalizedEmail, AdminPasswordResetOtpPurpose, request.Otp);

            if (!isValid)
            {
                return BadRequest(new { success = false, message = message });
            }

            var newPasswordCheck = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.NewPassword);

            if (newPasswordCheck != PasswordVerificationResult.Failed)
            {
                return BadRequest(new { success = false, message = "New password must be different from current password." });
            }

            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            await _context.SaveChangesAsync();

            await _context.OTPs
                .Where(o =>
                    o.UserId == user.Id &&
                    o.Purpose == AdminPasswordResetOtpPurpose &&
                    !o.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(o => o.IsUsed, true));

            return Ok(new { success = true, message = "Admin password reset successful. You may now log in with your new password." });
        }

        // ---------------- CREATE ADMIN (SUPERADMIN ONLY) ----------------
        [Authorize(Roles = AuthRoles.SuperAdmin)]
        [HttpPost("admin/create")]
        public async Task<IActionResult> CreateAdmin(CreateAdminRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FirstName) ||
                string.IsNullOrWhiteSpace(request.LastName) ||
                string.IsNullOrWhiteSpace(request.PhoneNumber) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("First name, last name, phone number, email and password are required.");
            }

            if (request.Password.Length < 8)
            {
                return BadRequest("Password must be at least 8 characters.");
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var firstName = request.FirstName.Trim();
            var lastName = request.LastName.Trim();
            var phoneNumber = request.PhoneNumber.Trim();

            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Email already exists"
                });
            }

            var adminUser = new User
            {
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = phoneNumber,
                Email = normalizedEmail,
                Role = AuthRoles.Admin
            };

            adminUser.PasswordHash = _passwordHasher.HashPassword(adminUser, request.Password);

            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Admin created successfully",
                userId = adminUser.Id,
                email = adminUser.Email,
                role = adminUser.Role
            });
        }

        // ---------------- LIST ADMINS (SUPERADMIN ONLY) ----------------
        [Authorize(Roles = AuthRoles.SuperAdmin)]
        [HttpGet("admin/list")]
        public async Task<IActionResult> GetAdminList()
        {
            var admins = await _context.Users
                .Where(u => u.Role.ToLower() == AuthRoles.Admin.ToLower())
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new
                {
                    userId = u.Id,
                    firstName = u.FirstName,
                    lastName = u.LastName,
                    email = u.Email,
                    phoneNumber = u.PhoneNumber,
                    role = u.Role,
                    createdAt = u.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                count = admins.Count,
                admins
            });
        }

        // ---------------- DELETE ADMIN (SUPERADMIN ONLY) ----------------
        [Authorize(Roles = AuthRoles.SuperAdmin)]
        [HttpDelete("admin/{adminId:int}")]
        public async Task<IActionResult> DeleteAdmin(int adminId)
        {
            var callerUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(callerUserIdClaim, out var callerUserId))
            {
                return Unauthorized("Invalid token");
            }

            if (adminId == callerUserId)
            {
                return BadRequest("You cannot delete your own account.");
            }

            var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == adminId);
            if (targetUser == null)
            {
                return NotFound("Admin not found.");
            }

            if (!string.Equals(targetUser.Role, AuthRoles.Admin, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Only Admin accounts can be deleted.");
            }

            _context.Users.Remove(targetUser);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Admin deleted successfully",
                userId = adminId
            });
        }

        // ---------------- B2B RESET PASSWORD ----------------
        [HttpPost("b2b/reset-password")]
        public async Task<IActionResult> B2BResetPassword(ForgotPasswordResetRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail && x.Role == AuthRoles.Agent);

            if (user == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Agent not found"
                });
            }

            if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Your agent account is inactive."
                });
            }

            var verifiedOtp = await _context.OTPs
                .FirstOrDefaultAsync(x =>
                    x.Email == normalizedEmail &&
                    x.Purpose == OtpPurposes.PasswordReset &&
                    x.IsVerified &&
                    x.Expiry > DateTime.UtcNow);

            if (verifiedOtp == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "OTP verification required"
                });
            }

            var passwordCheck = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.NewPassword);

            if (passwordCheck != PasswordVerificationResult.Failed)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "New password must be different from current password"
                });
            }

            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            verifiedOtp.IsUsed = true;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Password reset successfully"
            });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ForgotPasswordResetRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var normalizedEmail = request.Email?.Trim().ToLowerInvariant();
            var normalizedPhone = request.PhoneNumber?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedEmail) && string.IsNullOrWhiteSpace(normalizedPhone))
            {
                return BadRequest(new { success = false, message = "Email or Phone number is required." });
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(x =>
                    (!string.IsNullOrEmpty(normalizedPhone) && (x.PhoneNumber == normalizedPhone || x.PhoneNumber == "+91" + normalizedPhone || x.PhoneNumber == "91" + normalizedPhone) && x.Role == AuthRoles.User) ||
                    (!string.IsNullOrEmpty(normalizedEmail) && x.Email.ToLower() == normalizedEmail));

            if (user == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "User not found"
                });
            }

            var verifiedOtp = await _context.OTPs
                .FirstOrDefaultAsync(x =>
                    x.Purpose == OtpPurposes.PasswordReset &&
                    x.IsVerified &&
                    x.Expiry > DateTime.UtcNow &&
                    ((!string.IsNullOrEmpty(normalizedPhone) && x.PhoneNumber == normalizedPhone) ||
                     (!string.IsNullOrEmpty(normalizedEmail) && x.Email == normalizedEmail)));

            if (verifiedOtp == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "OTP verification required"
                });
            }

            var passwordCheck = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.NewPassword);

            if (passwordCheck != PasswordVerificationResult.Failed)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "New password must be different from current password"
                });
            }

            user.PasswordHash =
                _passwordHasher.HashPassword(user, request.NewPassword);

            verifiedOtp.IsUsed = true;

            // Invalidate any other pending password reset OTPs for this user
            await _context.OTPs
                .Where(x =>
                    ((!string.IsNullOrEmpty(normalizedPhone) && x.PhoneNumber == normalizedPhone) ||
                     (!string.IsNullOrEmpty(normalizedEmail) && x.Email == normalizedEmail)) &&
                    x.Purpose == OtpPurposes.PasswordReset &&
                    !x.IsUsed)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsUsed, true));

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Password reset successful"
            });
        }

        // ---------------- CHANGE PASSWORD ----------------
        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.OldPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest("Old password and new password are required");
            }

            if (request.OldPassword == request.NewPassword)
            {
                return BadRequest("New password must be different from old password");
            }

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized("Invalid token");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return Unauthorized("User not found");
            }

            var passwordCheck = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.OldPassword);

            if (passwordCheck == PasswordVerificationResult.Failed)
            {
                return BadRequest("Old password is incorrect");
            }

            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            await _context.SaveChangesAsync();

            return Ok("Password changed successfully");
        }

        private async Task MigrateGuestDataAsync(string guestId, string userId)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(guestId, @"^guest_[a-zA-Z0-9\-]+$"))
            {
                await _context.FlightSearchLogs
                    .Where(x => x.UserOrGuestId == guestId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.UserOrGuestId, userId)
                        .SetProperty(x => x.IsGuest, false));

                await _context.BusSearchLogs
                    .Where(x => x.UserOrGuestId == guestId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.UserOrGuestId, userId)
                        .SetProperty(x => x.IsGuest, false));
            }
        }

        // ---------------- ADMIN CHANGE PASSWORD ----------------
        [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
        [HttpPost("admin/change-password")]
        public Task<IActionResult> ChangeAdminPassword(ChangePasswordRequest request)
        {
            return ChangePassword(request);
        }
    }
}
