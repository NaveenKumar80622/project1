using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Services.Notifications.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Background
{
    [Obsolete("Bus boarding reminder functionality has been permanently decommissioned.")]
    public class BusBoardingReminderHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BusBoardingReminderHostedService> _logger;
        private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

        public BusBoardingReminderHostedService(
            IServiceProvider serviceProvider, 
            ILogger<BusBoardingReminderHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bus Boarding Reminder Hosted Service is decommissioned and inactive.");
            return Task.CompletedTask;
        }

        public Task ProcessUpcomingBoardingRemindersAsync(CancellationToken stoppingToken)
        {
            // Decommissioned - no reminders sent
            return Task.CompletedTask;
        }
    }
}
