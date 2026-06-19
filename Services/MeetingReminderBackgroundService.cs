namespace ProjectTracking.Services
{
    // Sends meeting reminders to LINE and Telegram 3, 2, 1, and 0 days before the meeting.
    // Email is sent once immediately when a meeting is created, not by this background service.
    public class MeetingReminderBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MeetingReminderBackgroundService> _logger;
        private readonly TimeSpan _interval;

        public MeetingReminderBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<MeetingReminderBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _interval = ParseInterval(
                configuration["MEETING_TELEGRAM_REMINDER_INTERVAL_MINUTES"]
                ?? configuration["MEETING_LINE_REMINDER_INTERVAL_MINUTES"]);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "MeetingReminderBackgroundService started in chat notification mode. Interval={Interval}",
                _interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(_interval, stoppingToken);
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

        private static TimeSpan ParseInterval(string? value)
        {
            if (int.TryParse(value, out var minutes) && minutes > 0)
                return TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 1440));

            return TimeSpan.FromMinutes(5);
        }
    }
}
