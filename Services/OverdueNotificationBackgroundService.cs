using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;

namespace ProjectTracking.Services
{
    public class OverdueNotificationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OverdueNotificationBackgroundService> _logger;
        private readonly TimeSpan _defaultRunAt;

        public OverdueNotificationBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<OverdueNotificationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            _defaultRunAt = ParseRunAt(configuration["OVERDUE_NOTIFICATION_RUN_AT"], "07:00");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var runAt = await GetRunAtAsync(stoppingToken);
            _logger.LogInformation("OverdueNotificationBackgroundService started. RunAt={RunAt}", runAt);

            var startedAt = GetBangkokNow();
            if (startedAt.TimeOfDay >= runAt)
            {
                _logger.LogInformation(
                    "Overdue notification run time has already passed today. Running startup sync now. StartedAt={StartedAt}, RunAt={RunAt}",
                    startedAt,
                    runAt);
                await RunOnceAsync(stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                runAt = await GetRunAtAsync(stoppingToken);
                var now = GetBangkokNow();
                var nextRun = NextRunAt(now, runAt);
                var delay = nextRun - now;
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
                await service.SyncAndSendTelegramAsync(cancellationToken);
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

        private async Task<TimeSpan> GetRunAtAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var value = await db.SystemConfigs
                    .AsNoTracking()
                    .Where(x => x.ConfigKey == "OVERDUE_NOTIFICATION_RUN_AT")
                    .Select(x => x.ConfigValue)
                    .FirstOrDefaultAsync(cancellationToken);

                return ParseRunAt(value, _defaultRunAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read OVERDUE_NOTIFICATION_RUN_AT from system_config. Using fallback RunAt={RunAt}", _defaultRunAt);
                return _defaultRunAt;
            }
        }

        private static DateTime GetBangkokNow()
        {
            try
            {
                var bangkokTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, bangkokTimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    var bangkokTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                    return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, bangkokTimeZone);
                }
                catch
                {
                    return DateTime.Now;
                }
            }
            catch (InvalidTimeZoneException)
            {
                return DateTime.Now;
            }
        }

        private static TimeSpan ParseRunAt(string? value, string defaultValue)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? defaultValue
                : value.Trim().Replace('.', ':');

            if (TimeSpan.TryParse(normalized, out var parsed)
                && parsed >= TimeSpan.Zero
                && parsed < TimeSpan.FromDays(1))
            {
                return new TimeSpan(parsed.Hours, parsed.Minutes, 0);
            }

            return ParseRunAt(defaultValue, new TimeSpan(7, 0, 0));
        }

        private static TimeSpan ParseRunAt(string? value, TimeSpan fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Trim().Replace('.', ':');

            if (TimeSpan.TryParse(normalized, out var parsed)
                && parsed >= TimeSpan.Zero
                && parsed < TimeSpan.FromDays(1))
            {
                return new TimeSpan(parsed.Hours, parsed.Minutes, 0);
            }

            return fallback;
        }
    }
}
