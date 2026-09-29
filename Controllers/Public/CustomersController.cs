using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;
using PickNBook.Api.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PickNBook.Api.Controllers;

[Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
public class CustomersController : AdminApiController
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly IWalletService _walletService;
    private readonly IInAppNotificationService? _notificationService;
    private readonly ILogger<CustomersController>? _logger;

    public CustomersController(
        AppDbContext context,
        IPasswordHasher<User> passwordHasher,
        IEmailService emailService,
        IWalletService walletService,
        IInAppNotificationService? notificationService = null,
        ILogger<CustomersController>? logger = null)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _walletService = walletService;
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] string? status,
        [FromQuery] string? walletStatus,
        [FromQuery] string? search,
        [FromQuery] decimal? minBalance,
        [FromQuery] decimal? maxBalance)
    {
        var query = _context.Users
            .AsNoTracking()
            .Where(x => x.Role == AuthRoles.User);

        // Search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchClean = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.FirstName + " " + x.LastName).ToLower().Contains(searchClean) ||
                x.Email.ToLower().Contains(searchClean) ||
                x.PhoneNumber.Contains(searchClean));
        }

        // Status filter
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Status == status);
        }

        // Wallet status filter
        if (!string.IsNullOrWhiteSpace(walletStatus) && !string.Equals(walletStatus, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.WalletStatus == walletStatus);
        }

        // Balance filters
        if (minBalance.HasValue)
        {
            query = query.Where(x => x.WalletBalance >= minBalance.Value);
        }

        if (maxBalance.HasValue)
        {
            query = query.Where(x => x.WalletBalance <= maxBalance.Value);
        }

        var customers = await query
            .OrderByDescending(x => x.Id)
            .Select(x => new CustomerResponseDto
            {
                Id = x.Id,
                Status = x.Status,
                CustomerName = x.FirstName + " " + x.LastName,
                EmailId = x.Email,
                Mobile = x.PhoneNumber,
                WalletStatus = x.WalletStatus,
                WalletBalance = x.WalletBalance,
                AltMobile = x.AltMobile,
                Gender = x.Gender,
                Currency = x.Currency,
                LoginId = x.LoginId,
                RefferedBy = x.RefferedBy,
                Address = x.Address,
                City = x.City,
                State = x.State,
                Country = x.Country,
                Pincode = x.Pincode,
                Remark = x.Remark,
                AadharNumber = x.AadharNumber,
                PanNumber = x.PanNumber,
                PanName = x.PanName,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return Ok(customers);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var emailClean = request.Email.Trim().ToLowerInvariant();
        var phoneClean = request.Mobile.Trim();

        // Check unique constraints
        var emailExists = await _context.Users.AnyAsync(x => x.Email.ToLower() == emailClean);
        if (emailExists)
        {
            return BadRequest("Email ID is already registered.");
        }

        var phoneExists = await _context.Users.AnyAsync(x => x.PhoneNumber == phoneClean);
        if (phoneExists)
        {
            return BadRequest("Mobile number is already registered.");
        }

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = emailClean,
            PhoneNumber = phoneClean,
            Role = AuthRoles.User,
            Status = request.Status,
            WalletStatus = request.WalletStatus,
            WalletBalance = 0.00m,
            AltMobile = request.AltMobile?.Trim(),
            Gender = request.Gender,
            Address = request.Address?.Trim(),
            City = request.City?.Trim(),
            State = request.State?.Trim(),
            Country = request.Country?.Trim(),
            Pincode = request.Pincode?.Trim(),
            Remark = request.Remark?.Trim(),
            AadharNumber = request.AadharNumber?.Trim(),
            PanNumber = request.PanNumber?.Trim(),
            PanName = request.PanName?.Trim(),
            RefferedBy = request.RefferedBy?.Trim(),
            LoginId = string.IsNullOrWhiteSpace(request.LoginId) ? emailClean : request.LoginId.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Customer saved successfully.",
            customerId = user.Id
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCustomer(int id, [FromBody] CreateCustomerRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var customer = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.User);
        if (customer == null)
        {
            return NotFound("Customer not found.");
        }

        var emailClean = request.Email.Trim().ToLowerInvariant();
        var phoneClean = request.Mobile.Trim();

        // Check unique constraints (exclude current user)
        var emailExists = await _context.Users.AnyAsync(x => x.Email.ToLower() == emailClean && x.Id != id);
        if (emailExists)
        {
            return BadRequest("Email ID is already registered to another user.");
        }

        var phoneExists = await _context.Users.AnyAsync(x => x.PhoneNumber == phoneClean && x.Id != id);
        if (phoneExists)
        {
            return BadRequest("Mobile number is already registered to another user.");
        }

        customer.FirstName = request.FirstName.Trim();
        customer.LastName = request.LastName.Trim();
        customer.Email = emailClean;
        customer.PhoneNumber = phoneClean;
        customer.Status = request.Status;
        customer.WalletStatus = request.WalletStatus;
        customer.AltMobile = request.AltMobile?.Trim();
        customer.Gender = request.Gender;
        customer.Address = request.Address?.Trim();
        customer.City = request.City?.Trim();
        customer.State = request.State?.Trim();
        customer.Country = request.Country?.Trim();
        customer.Pincode = request.Pincode?.Trim();
        customer.Remark = request.Remark?.Trim();
        customer.AadharNumber = request.AadharNumber?.Trim();
        customer.PanNumber = request.PanNumber?.Trim();
        customer.PanName = request.PanName?.Trim();
        customer.RefferedBy = request.RefferedBy?.Trim();
        customer.LoginId = string.IsNullOrWhiteSpace(request.LoginId) ? emailClean : request.LoginId.Trim();

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            customer.PasswordHash = _passwordHasher.HashPassword(customer, request.Password);
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Customer updated successfully.",
            customerId = customer.Id
        });
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var customer = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.User);
        if (customer == null)
        {
            return NotFound("Customer not found.");
        }

        customer.Status = string.Equals(customer.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";
        await _context.SaveChangesAsync();

        string subject = customer.Status == "Active" ? "Account Activated" : "Account Suspended";
        string body = customer.Status == "Active" 
            ? $"Hello {customer.FirstName},<br><br>Welcome back! Your Pick&amp;book account has been activated."
            : $"Hello {customer.FirstName},<br><br>Notice: Your Pick&amp;book account has been temporarily suspended. Please contact support for more information.";
        
        try
        {
            await _emailService.SendEmailAsync(customer.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send account status change email to {Email} for customer #{Id}. Non-fatal.", customer.Email, customer.Id);
        }

        return Ok(new { message = "Customer status updated successfully.", status = customer.Status });
    }

    [HttpPut("{id:int}/wallet-status")]
    public async Task<IActionResult> ToggleWalletStatus(int id)
    {
        var customer = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.User);
        if (customer == null)
        {
            return NotFound("Customer not found.");
        }

        customer.WalletStatus = string.Equals(customer.WalletStatus, "Active", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";
        await _context.SaveChangesAsync();

        string subject = customer.WalletStatus == "Active" ? "Wallet Activated" : "Wallet Suspended";
        string body = customer.WalletStatus == "Active" 
            ? $"Hello {customer.FirstName},<br><br>Your Pick&amp;book wallet has been activated."
            : $"Hello {customer.FirstName},<br><br>Notice: Your Pick&amp;book wallet has been temporarily suspended.";
        
        try
        {
            await _emailService.SendEmailAsync(customer.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send wallet status change email to {Email} for customer #{Id}. Non-fatal.", customer.Email, customer.Id);
        }

        return Ok(new { message = "Wallet status updated successfully.", walletStatus = customer.WalletStatus });
    }

    [HttpPost("{id:int}/wallet/add")]
    public async Task<IActionResult> AddWalletBalance(int id, [FromBody] AddWalletBalanceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var customer = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.User);
        if (customer == null)
        {
            return NotFound("Customer not found.");
        }

        string idempotentRef = !string.IsNullOrWhiteSpace(request.ReferenceId)
            ? request.ReferenceId
            : $"ADMIN-ADD-{id}-{request.Amount}-{Guid.NewGuid():N}";

        var tx = await _walletService.CreditAsync(id, request.Amount, "AdminCredit", idempotentRef, "Wallet balance added by admin");
        await _context.Entry(customer).ReloadAsync();

        customer.WalletStatus = "Active"; // Ensure wallet is Active when balance is added
        await _context.SaveChangesAsync();

        if (_notificationService != null)
        {
            try
            {
                await _notificationService.CreateNotificationAsync(
                    type: "WALLET_ADMIN_CREDIT",
                    category: "FINANCIAL",
                    title: "Wallet Credited by Admin",
                    message: $"Admin credited INR {request.Amount:N2} to customer {customer.FirstName} {customer.LastName} (#{customer.Id}). New balance: INR {customer.WalletBalance:N2}.",
                    severity: "INFO",
                    referenceType: "WalletTransaction",
                    referenceId: tx.Id.ToString(),
                    actionUrl: $"/admin/customers/{customer.Id}",
                    idempotencyKey: $"WALLET_ADMIN_CREDIT_{tx.Id}",
                    targetRole: "ADMIN"
                );
            }
            catch (Exception)
            {
                // Non-fatal
            }
        }

        string fullName = $"{customer.FirstName} {customer.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = "Valued Customer";
        }

        string formattedAmount = $"+₹{request.Amount:N2}";
        string formattedBalance = $"₹{customer.WalletBalance:N2}";
        string formattedDate = DateTime.UtcNow.ToString("dd MMMM yyyy");
        string subject = "Wallet Balance Added Successfully – Pick&book";

        string body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; }}
        .container {{ max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.08); }}
        .header {{ background: linear-gradient(135deg, #1e3a8a 0%, #3b82f6 100%); padding: 30px 24px; text-align: center; color: #ffffff; }}
        .header h1 {{ margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px; }}
        .content {{ padding: 28px 24px; color: #334155; line-height: 1.6; }}
        .greeting {{ font-size: 16px; font-weight: 600; margin-bottom: 12px; color: #0f172a; }}
        .card {{ background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 20px; margin: 20px 0; }}
        .table {{ width: 100%; border-collapse: collapse; }}
        .table td {{ padding: 10px 0; border-bottom: 1px solid #edf2f7; font-size: 14px; }}
        .table td:last-child {{ text-align: right; font-weight: 600; color: #0f172a; }}
        .amount-highlight {{ color: #16a34a !important; font-size: 16px; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #94a3b8; border-top: 1px solid #f1f5f9; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Pick&amp;book Wallet</h1>
        </div>
        <div class='content'>
            <div class='greeting'>Dear {fullName},</div>
            <p>Your wallet has been successfully credited by the administrator. Here are the details of this transaction:</p>
            <div class='card'>
                <table class='table'>
                    <tr>
                        <td style='color: #64748b;'>Amount Added</td>
                        <td class='amount-highlight'>{formattedAmount}</td>
                    </tr>
                    <tr>
                        <td style='color: #64748b;'>Updated Wallet Balance</td>
                        <td>{formattedBalance}</td>
                    </tr>
                    <tr>
                        <td style='color: #64748b;'>Date</td>
                        <td>{formattedDate}</td>
                    </tr>
                    <tr>
                        <td style='color: #64748b; border: none;'>Reference ID</td>
                        <td style='border: none; font-family: monospace; font-size: 12px;'>{idempotentRef}</td>
                    </tr>
                </table>
            </div>
            <p style='margin-bottom: 0;'>You can now use your updated balance to book flights, hotels, and bus tickets seamlessly on <a href='https://picknbook.in' style='color: #2563eb; text-decoration: none;'>Pick&amp;book</a>.</p>
        </div>
        <div class='footer'>
            &copy; {DateTime.UtcNow.Year} Pick&amp;book. All rights reserved.<br>
            This is an automated transaction receipt. Please do not reply to this email.
        </div>
    </div>
</body>
</html>";

        try
        {
            await _emailService.SendEmailAsync(customer.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to send wallet balance email to {Email} for user {UserId}. Non-fatal.",
                customer.Email,
                customer.Id);
        }

        return Ok(new { message = "Wallet balance updated successfully.", walletBalance = customer.WalletBalance });
    }

    [HttpPost("{id:int}/wallet/reset")]
    public async Task<IActionResult> ResetWalletBalance(int id, [FromQuery] string referenceId = null)
    {
        var customer = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.User);
        if (customer == null)
        {
            return NotFound("Customer not found.");
        }

        if (customer.WalletBalance > 0)
        {
            decimal currentBalance = customer.WalletBalance;
            string idempotentRef = !string.IsNullOrWhiteSpace(referenceId)
                ? referenceId
                : $"ADMIN-RESET-{id}-{DateTime.UtcNow.Ticks}";

            await _walletService.DebitAsync(id, currentBalance, "AdminReset", idempotentRef, "Wallet balance reset by admin");
            await _context.Entry(customer).ReloadAsync();
        }

        return Ok(new { message = "Wallet balance reset successfully.", walletBalance = customer.WalletBalance });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var customer = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.User);
        if (customer == null)
        {
            return NotFound("Customer not found.");
        }

        _context.Users.Remove(customer);
        await _context.SaveChangesAsync();

        return Ok("Customer removed successfully.");
    }
}
