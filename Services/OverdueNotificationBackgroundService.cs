namespace ProjectTracking.Services
{
    public class OverdueNotificationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OverdueNotificationBackgroundService> _logger;
        private readonly TimeSpan _interval;

        public OverdueNotificationBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<OverdueNotificationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            var minutes = Math.Clamp(configuration.GetValue<int?>("OVERDUE_NOTIFICATION_INTERVAL_MINUTES") ?? 60, 5, 1440);
            _interval = TimeSpan.FromMinutes(minutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OverdueNotificationBackgroundService started. Interval={Interval}", _interval);

            await RunOnceAsync(stoppingToken);

            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<OverdueNotificationService>();
                await service.SyncAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Overdue notification sync cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overdue notification sync failed");
            }
        }
    }
}
