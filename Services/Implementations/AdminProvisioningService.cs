using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;

namespace PickNBook.Api.Services;

public interface IAdminProvisioningService
{
    Task<AdminProvisionResult> ProvisionFromEnvironmentAsync();
}

public sealed record AdminProvisionResult(bool IsSuccess, string Message);

public class AdminProvisioningService : IAdminProvisioningService
{
    private readonly AppDbContext _context;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AdminProvisioningService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AdminProvisionResult> ProvisionFromEnvironmentAsync()
    {
        var superAdminEmail = (Environment.GetEnvironmentVariable("SUPERADMIN_SEED_EMAIL") ?? string.Empty).Trim().ToLowerInvariant();
        var superAdminPassword = Environment.GetEnvironmentVariable("SUPERADMIN_SEED_PASSWORD") ?? string.Empty;
        var adminEmail = (Environment.GetEnvironmentVariable("ADMIN_SEED_EMAIL") ?? string.Empty).Trim().ToLowerInvariant();
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_SEED_PASSWORD") ?? string.Empty;

        var hasSuperAdminSeed = !string.IsNullOrWhiteSpace(superAdminEmail) || !string.IsNullOrWhiteSpace(superAdminPassword);
        var hasAdminSeed = !string.IsNullOrWhiteSpace(adminEmail) || !string.IsNullOrWhiteSpace(adminPassword);

        if (!hasSuperAdminSeed && !hasAdminSeed)
        {
            return new AdminProvisionResult(
                false,
                "Missing env vars. Set SUPERADMIN_SEED_EMAIL/SUPERADMIN_SEED_PASSWORD or ADMIN_SEED_EMAIL/ADMIN_SEED_PASSWORD.");
        }

        if (hasSuperAdminSeed && hasAdminSeed &&
            string.Equals(superAdminEmail, adminEmail, StringComparison.OrdinalIgnoreCase))
        {
            return new AdminProvisionResult(false, "SUPERADMIN_SEED_EMAIL and ADMIN_SEED_EMAIL must be different.");
        }

        var messages = new List<string>();

        if (hasSuperAdminSeed)
        {
            var superAdminResult = await ProvisionUserFromEnvironmentAsync(
                emailVar: "SUPERADMIN_SEED_EMAIL",
                passwordVar: "SUPERADMIN_SEED_PASSWORD",
                firstNameVar: "SUPERADMIN_SEED_FIRST_NAME",
                lastNameVar: "SUPERADMIN_SEED_LAST_NAME",
                phoneVar: "SUPERADMIN_SEED_PHONE",
                defaultFirstName: "Super",
                defaultLastName: "Admin",
                role: AuthRoles.SuperAdmin,
                roleLabel: "SuperAdmin");

            if (!superAdminResult.IsSuccess)
            {
                return superAdminResult;
            }

            messages.Add(superAdminResult.Message);
        }

        if (hasAdminSeed)
        {
            var adminResult = await ProvisionUserFromEnvironmentAsync(
                emailVar: "ADMIN_SEED_EMAIL",
                passwordVar: "ADMIN_SEED_PASSWORD",
                firstNameVar: "ADMIN_SEED_FIRST_NAME",
                lastNameVar: "ADMIN_SEED_LAST_NAME",
                phoneVar: "ADMIN_SEED_PHONE",
                defaultFirstName: "Admin",
                defaultLastName: "User",
                role: AuthRoles.Admin,
                roleLabel: "Admin");

            if (!adminResult.IsSuccess)
            {
                return adminResult;
            }

            messages.Add(adminResult.Message);
        }

        return new AdminProvisionResult(true, string.Join(" | ", messages));
    }

    private async Task<AdminProvisionResult> ProvisionUserFromEnvironmentAsync(
        string emailVar,
        string passwordVar,
        string firstNameVar,
        string lastNameVar,
        string phoneVar,
        string defaultFirstName,
        string defaultLastName,
        string role,
        string roleLabel)
    {
        var email = (Environment.GetEnvironmentVariable(emailVar) ?? string.Empty).Trim().ToLowerInvariant();
        var password = Environment.GetEnvironmentVariable(passwordVar) ?? string.Empty;
        var firstName = (Environment.GetEnvironmentVariable(firstNameVar) ?? defaultFirstName).Trim();
        var lastName = (Environment.GetEnvironmentVariable(lastNameVar) ?? defaultLastName).Trim();
        var phoneNumber = (Environment.GetEnvironmentVariable(phoneVar) ?? "0000000000").Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return new AdminProvisionResult(false, $"Missing env vars. Set {emailVar} and {passwordVar}.");
        }

        if (password.Length < 8)
        {
            return new AdminProvisionResult(false, $"{passwordVar} must be at least 8 characters.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == email);
        if (user == null)
        {
            user = new User
            {
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = phoneNumber,
                Email = email,
                Role = role
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return new AdminProvisionResult(true, $"{roleLabel} user created: {email}");
        }

        user.FirstName = firstName;
        user.LastName = lastName;
        user.PhoneNumber = phoneNumber;
        user.Role = role;
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        await _context.SaveChangesAsync();
        return new AdminProvisionResult(true, $"Existing user updated to {roleLabel}: {email}");
    }
}
