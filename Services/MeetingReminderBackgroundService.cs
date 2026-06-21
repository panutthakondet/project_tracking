namespace ProjectTracking.Services
{
    // Sends meeting reminders to LINE and Telegram 3, 2, 1, and 0 days before the meeting.
    // Email is sent once immediately when a meeting is created, not by this background service.
    public class MeetingReminderBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MeetingReminderBackgroundService> _logger;
        private readonly TimeSpan _runAt;

        public MeetingReminderBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<MeetingReminderBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _runAt = ParseRunAt(configuration["MEETING_NOTIFICATION_RUN_AT"]);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "MeetingReminderBackgroundService started in scheduled chat notification mode. RunAt={RunAt}",
                _runAt);

            var startedAt = GetBangkokNow();
            if (startedAt.TimeOfDay >= _runAt)
            {
                _logger.LogInformation(
                    "Meeting reminder run time has already passed today. Running startup sync now. StartedAt={StartedAt}, RunAt={RunAt}",
                    startedAt,
                    _runAt);
                await RunOnceAsync(stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = GetBangkokNow();
                var nextRun = NextRunAt(now, _runAt);
                var delay = nextRun - now;
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;

                _logger.LogInformation("Next meeting reminder sync scheduled at {NextRun}", nextRun);
                await Task.Delay(delay, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<MeetingNotificationService>();
                var lineResult = await SendLineRemindersSafelyAsync(service, cancellationToken);
                var telegramResult = await SendTelegramRemindersSafelyAsync(service, cancellationToken);
                var result = new MeetingNotificationResult(
                    lineResult.SentCount + telegramResult.SentCount,
                    lineResult.SkippedCount + telegramResult.SkippedCount,
                    lineResult.FailedCount + telegramResult.FailedCount);

                if (result.SentCount > 0 || result.FailedCount > 0)
                {
                    _logger.LogInformation(
                        "Meeting chat reminder completed. Sent={SentCount}, Skipped={SkippedCount}, Failed={FailedCount}",
                        result.SentCount,
                        result.SkippedCount,
                        result.FailedCount);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Meeting chat reminder cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meeting chat reminder failed");
            }
        }

        private async Task<MeetingNotificationResult> SendLineRemindersSafelyAsync(
            MeetingNotificationService service,
            CancellationToken cancellationToken)
        {
            try
            {
                return await service.SendLineRemindersAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meeting LINE reminder failed");
                return new MeetingNotificationResult(0, 0, 1, ex.Message);
            }
        }

        private async Task<MeetingNotificationResult> SendTelegramRemindersSafelyAsync(
            MeetingNotificationService service,
            CancellationToken cancellationToken)
        {
            try
            {
                return await service.SendTelegramRemindersAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meeting Telegram reminder failed");
                return new MeetingNotificationResult(0, 0, 1, ex.Message);
            }
        }

        private static DateTime NextRunAt(DateTime now, TimeSpan runAt)
        {
            var next = now.Date.Add(runAt);
            return next <= now
                ? next.AddDays(1)
                : next;
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

        private static TimeSpan ParseRunAt(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "06:00"
                : value.Trim().Replace('.', ':');

            if (TimeSpan.TryParse(normalized, out var parsed)
                && parsed >= TimeSpan.Zero
                && parsed < TimeSpan.FromDays(1))
            {
                return new TimeSpan(parsed.Hours, parsed.Minutes, 0);
            }

            return new TimeSpan(6, 0, 0);
        }
    }
}
