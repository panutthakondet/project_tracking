namespace ProjectTracking.Services
{
    public class OverdueNotificationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OverdueNotificationBackgroundService> _logger;
        private readonly TimeSpan _runAt;

        public OverdueNotificationBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<OverdueNotificationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            _runAt = ParseRunAt(configuration["OVERDUE_NOTIFICATION_RUN_AT"]);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OverdueNotificationBackgroundService started. RunAt={RunAt}", _runAt);

            while (!stoppingToken.IsCancellationRequested)
            {
                var nextRun = NextRunAt(DateTime.Now, _runAt);
                var delay = nextRun - DateTime.Now;
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;

                _logger.LogInformation("Next overdue notification sync scheduled at {NextRun}", nextRun);
                await Task.Delay(delay, stoppingToken);
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

        private static DateTime NextRunAt(DateTime now, TimeSpan runAt)
        {
            var next = now.Date.Add(runAt);
            return next <= now
                ? next.AddDays(1)
                : next;
        }

        private static TimeSpan ParseRunAt(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "09:00"
                : value.Trim().Replace('.', ':');

            if (TimeSpan.TryParse(normalized, out var parsed)
                && parsed >= TimeSpan.Zero
                && parsed < TimeSpan.FromDays(1))
            {
                return new TimeSpan(parsed.Hours, parsed.Minutes, 0);
            }

            return new TimeSpan(9, 0, 0);
        }
    }
}
