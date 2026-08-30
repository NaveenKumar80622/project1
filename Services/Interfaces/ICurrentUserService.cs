namespace PickNBook.Api.Services
{
    public interface ICurrentUserService
    {
        string GetUserOrGuestId();

        bool IsAuthenticated();

        bool IsGuest();
    }
}
