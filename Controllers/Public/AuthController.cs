using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Constants;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
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
            IConfiguration configuration)
        {
            _context = context;
            _jwtService = jwtService;
            _emailService = emailService;
            _smsService = smsService;
            _otpService = otpService;
            _passwordHasher = new PasswordHasher<User>();

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

                await _otpService.GenerateAndSendOtpAsync(normalizedPhone, channel, OtpPurposes.Registration);

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

                await _otpService.GenerateAndSendOtpAsync(normalizedEmail, channel, OtpPurposes.Registration);

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

            await _otpService.GenerateAndSendOtpAsync(normalizedEmail, "Email", OtpPurposes.PasswordReset, user.Id);

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

            await _otpService.GenerateAndSendOtpAsync(normalizedEmail, "Email", OtpPurposes.PasswordReset, user.Id);

            return Ok(new { success = true, message = "OTP sent successfully" });
        }

        [HttpPost("forgot-password/verify-otp")]
        public async Task<IActionResult> ForgotPasswordVerifyOtp(ForgotPasswordVerifyOtpRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var (isValid, message) = await _otpService.VerifyOtpAsync(normalizedEmail, OtpPurposes.PasswordReset, request.Otp);

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

        // ---------------- LOGIN ----------------
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null)
                return Unauthorized("Invalid credentials");

            var result = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

            if (result == PasswordVerificationResult.Failed)
                return Unauthorized("Invalid credentials");

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

        // ---------------- ADMIN LOGIN STEP-1 (PASSWORD -> SEND OTP) ----------------
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

            await _context.OTPs
                .Where(o =>
                    o.UserId == user.Id &&
                    o.Purpose == AdminLoginOtpPurpose &&
                    !o.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(o => o.IsUsed, true));

            var (isSent, challengeId) = await _otpService.GenerateAndSendOtpAsync(normalizedEmail, "Email", AdminLoginOtpPurpose, user.Id);

            return Ok(new
            {
                message = "OTP sent to admin email.",
                challengeId,
                expiresInMinutes = _adminOtpExpiryMinutes
            });
        }

        // ---------------- ADMIN LOGIN STEP-2 (VERIFY OTP -> JWT) ----------------
        [HttpPost("admin/login/verify-otp")]
        public async Task<IActionResult> VerifyAdminLoginOtp(AdminLoginVerifyOtpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.Otp))
            {
                return BadRequest("ChallengeId and Otp are required.");
            }

            var challengeId = request.ChallengeId.Trim();
            var now = DateTime.UtcNow;

            var otpRecord = await _context.OTPs
                .Include(o => o.User)
                .FirstOrDefaultAsync(o =>
                    o.ChallengeId == challengeId &&
                    o.Purpose == AdminLoginOtpPurpose &&
                    !o.IsUsed);

            if (otpRecord == null || otpRecord.User == null)
            {
                return BadRequest("Invalid or expired OTP.");
            }

            var (isValid, message) = await _otpService.VerifyOtpAsync(otpRecord.User.Email, AdminLoginOtpPurpose, request.Otp);

            if (!isValid)
            {
                return BadRequest(message);
            }

            var user = otpRecord.User;
            if (!AuthRoles.IsAdminScope(user.Role))
            {
                return Unauthorized("Invalid admin credentials.");
            }

            if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized("Your account is inactive. Please contact support.");
            }

            var token = _jwtService.GenerateToken(user, user.Role);

            return Ok(new
            {
                token,
                userId = user.Id,
                role = user.Role
            });
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

            if (user == null || !AuthRoles.IsAdminScope(user.Role))
            {
                return Ok("If the email is registered, an OTP has been sent.");
            }

            await _context.OTPs
                .Where(o =>
                    o.UserId == user.Id &&
                    o.Purpose == AdminPasswordResetOtpPurpose &&
                    !o.IsUsed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(o => o.IsUsed, true));

            await _otpService.GenerateAndSendOtpAsync(normalizedEmail, "Email", AdminPasswordResetOtpPurpose, user.Id);

            return Ok("If the email is registered, an OTP has been sent.");
        }

        // ---------------- ADMIN RESET PASSWORD ----------------
        [HttpPost("admin/reset-password")]
        public async Task<IActionResult> AdminResetPassword(AdminResetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Otp) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest("Email, OTP and new password are required.");
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null || !AuthRoles.IsAdminScope(user.Role))
            {
                return BadRequest("Invalid or expired OTP.");
            }

            var (isValid, message) = await _otpService.VerifyOtpAsync(normalizedEmail, AdminPasswordResetOtpPurpose, request.Otp);

            if (!isValid)
            {
                return BadRequest(message);
            }

            var newPasswordCheck = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.NewPassword);

            if (newPasswordCheck != PasswordVerificationResult.Failed)
            {
                return BadRequest("New password must be different from current password.");
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

            return Ok("Admin password reset successful.");
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

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);

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

            user.PasswordHash =
                _passwordHasher.HashPassword(user, request.NewPassword);

            await _context.SaveChangesAsync();
            verifiedOtp.IsUsed = true;

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
