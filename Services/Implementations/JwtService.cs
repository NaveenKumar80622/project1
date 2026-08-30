using Microsoft.IdentityModel.Tokens;
using PickNBook.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PickNBook.Api.Services
{
    public class JwtService : IJwtService
    {
        private readonly IConfiguration _config;

        public JwtService(IConfiguration config)
        {
            _config = config;
        }

        public string GenerateToken(User user, string? role = null)
        {
            var resolvedRole = string.IsNullOrWhiteSpace(role) ? user.Role : role;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email)
      

            };

            if (!string.IsNullOrWhiteSpace(resolvedRole))

            {
                claims.Add(new Claim(ClaimTypes.Role, resolvedRole));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));

            var creds = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

            double expiryMinutes;
            if (AuthRoles.IsAdminScope(resolvedRole))
            {
                expiryMinutes = Convert.ToDouble(_config["Jwt:AdminExpiryMinutes"] ?? "1440");
            }
            else
            {
                expiryMinutes = Convert.ToDouble(_config["Jwt:ExpiryMinutes"] ?? "60");
            }

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
