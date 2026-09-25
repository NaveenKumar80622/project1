using PickNBook.Api.Models;

namespace PickNBook.Api.Services
{
    public interface IJwtService
    {
        string GenerateToken(User user, string? role = null);
    }
}
