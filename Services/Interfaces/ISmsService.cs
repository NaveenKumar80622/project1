namespace PickNBook.Api.Services
{
    public interface ISmsService
    {
        Task<(bool IsSent, string Message)> SendSmsAsync(string phoneNumber, string message);
    }
}
