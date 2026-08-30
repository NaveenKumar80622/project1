using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

[Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
public class CustomersController : AdminApiController
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IEmailService _emailService;

    public CustomersController(AppDbContext context, IPasswordHasher<User> passwordHasher, IEmailService emailService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
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
            ? $"Hello {customer.FirstName},<br><br>Welcome back! Your PickNBook account has been activated."
            : $"Hello {customer.FirstName},<br><br>Notice: Your PickNBook account has been temporarily suspended. Please contact support for more information.";
        
        await _emailService.SendEmailAsync(customer.Email, subject, body);

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
            ? $"Hello {customer.FirstName},<br><br>Your PickNBook wallet has been activated."
            : $"Hello {customer.FirstName},<br><br>Notice: Your PickNBook wallet has been temporarily suspended.";
        
        await _emailService.SendEmailAsync(customer.Email, subject, body);

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

        customer.WalletBalance += request.Amount;
        customer.WalletStatus = "Active"; // Ensure wallet is Active when balance is added
        await _context.SaveChangesAsync();

        string subject = "Wallet Balance Added";
        string body = $"Hello {customer.FirstName},<br><br>An amount of ₹{request.Amount} has been added to your PickNBook wallet.<br>Your updated wallet balance is ₹{customer.WalletBalance}.";
        
        await _emailService.SendEmailAsync(customer.Email, subject, body);

        return Ok(new { message = "Wallet balance updated successfully.", walletBalance = customer.WalletBalance });
    }

    [HttpPost("{id:int}/wallet/reset")]
    public async Task<IActionResult> ResetWalletBalance(int id)
    {
        var customer = await _context.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == AuthRoles.User);
        if (customer == null)
        {
            return NotFound("Customer not found.");
        }

        customer.WalletBalance = 0.00m;
        await _context.SaveChangesAsync();

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
