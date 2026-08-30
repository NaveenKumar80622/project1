using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace PickNBook.Api.Services
{
    public class BackgroundJobExecutor : BackgroundService
    {
        private readonly IBackgroundJobQueue _queue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackgroundJobExecutor> _logger;

        public BackgroundJobExecutor(
            IBackgroundJobQueue queue,
            IServiceProvider serviceProvider,
            ILogger<BackgroundJobExecutor> logger)
        {
            _queue = queue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Background Job Executor is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var workItem = await _queue.DequeueAsync(stoppingToken);

                    _ = Task.Run(async () =>
                    {
                        using var scope = _serviceProvider.CreateScope();
                        try
                        {
                            await workItem(scope.ServiceProvider, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            var logger = scope.ServiceProvider.GetRequiredService<ILogger<BackgroundJobExecutor>>();
                            logger.LogError(ex, "Error occurred executing background job.");
                        }
                    }, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Safe shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred dequeuing background job.");
                }
            }

            _logger.LogInformation("Background Job Executor is stopping.");
        }
    }
}
