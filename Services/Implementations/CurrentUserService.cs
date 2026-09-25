using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace PickNBook.Api.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetUserOrGuestId()
        {
            var context = _httpContextAccessor.HttpContext;

            var userId = context?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context?.User?.FindFirst("sub")?.Value;

            if (!string.IsNullOrWhiteSpace(userId))
                return userId;

            var guestId = context?.Request.Headers["X-Guest-Id"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(guestId))
            {
                ValidateGuestId(guestId);
                return guestId;
            }

            return context?.Connection?.RemoteIpAddress?.ToString()
                ?? "anonymous";
        }

        public bool IsAuthenticated()
        {
            return _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
        }

        public bool IsGuest()
        {
            return !IsAuthenticated();
        }

        private static void ValidateGuestId(string guestId)
        {
            if (!Regex.IsMatch(guestId, @"^guest_[a-zA-Z0-9\-]+$"))
            {
                throw new UnauthorizedAccessException("Invalid Guest Id");
            }
        }
    }
}
