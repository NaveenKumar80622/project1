using System;
using System.Threading;
using System.Threading.Tasks;

namespace PickNBook.Api.Services
{
    public interface IBackgroundJobQueue
    {
        void QueueBackgroundWorkItem(Func<IServiceProvider, CancellationToken, Task> workItem);
        Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
    }
}
