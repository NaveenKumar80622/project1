using System.Threading.Tasks;

namespace PickNBook.Api.Services.Interfaces
{
    public interface ISecurityCounterService
    {
        Task HandleLoginFailureAsync(string ipAddress, long accountId, string accountEmail, string scope);
        Task HandleLoginSuccessAsync(string ipAddress, long accountId, string scope);
        Task HandleOtpMismatchAsync(string ipAddress, long accountId, string accountEmail, string scope);
        Task ResetCounterAsync(long accountId, string scope, string counterKey);
    }
}
