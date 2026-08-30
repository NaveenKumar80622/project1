using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PickNBook.Api.Services
{
    public class BackgroundJobQueue : IBackgroundJobQueue
    {
        private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;

        public BackgroundJobQueue()
        {
            var options = new BoundedChannelOptions(10000)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(options);
        }

        public void QueueBackgroundWorkItem(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            if (!_queue.Writer.TryWrite(workItem))
            {
                throw new InvalidOperationException("Background queue is full.");
            }
        }

        public async Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            return await _queue.Reader.ReadAsync(cancellationToken);
        }
    }
}
