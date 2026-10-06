using System.Threading;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Interfaces
{
    public interface ISrdvWalletAlertService
    {
        /// <summary>
        /// Evaluates current available balance against configured LowBalanceThreshold,
        /// and dispatches In-App and Email notifications to all administrators if below threshold,
        /// respecting the alert cooldown period.
        /// </summary>
        Task EvaluateAndAlertAsync(decimal availableBalance, CancellationToken cancellationToken = default);
    }
}
