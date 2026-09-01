using System.Threading.Tasks;

namespace PickNBook.Api.Services.Notifications.Interfaces
{
    public interface INotificationProvider
    {
        Task<(bool IsSuccess, string? ProviderMessageId, string? ErrorMessage)> SendAsync(string recipient, string content, string? subject = null);
    }

    public interface ISmsProvider : INotificationProvider { }
    public interface IWhatsAppProvider : INotificationProvider { }
    public interface IEmailProvider : INotificationProvider { }
}
