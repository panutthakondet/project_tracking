using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using ProjectTracking.Data;
using System.Globalization;

namespace ProjectTracking.Services
{
    // Sends meeting reminders to LINE and Telegram 3, 2, 1, and 0 days before the meeting.
    // Email is sent once immediately when a meeting is created, not by this background service.
    public class MeetingReminderBackgroundService : BackgroundService
    {
        private const string LastAutoRunDateConfigKey = "MEETING_NOTIFICATION_LAST_AUTO_RUN_DATE";
        private const string LastAutoRunDateDescription = "Last Bangkok date the automatic meeting reminder scheduler ran.";
        private const string RunDateFormat = "yyyy-MM-dd";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MeetingReminderBackgroundService> _logger;
        private readonly TimeSpan _defaultRunAt;

        public MeetingReminderBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<MeetingReminderBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _defaultRunAt = ParseRunAt(configuration["MEETING_NOTIFICATION_RUN_AT"], "06:00");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var runAt = await GetRunAtAsync(stoppingToken);
            _logger.LogInformation(
                "MeetingReminderBackgroundService started in scheduled chat notification mode. RunAt={RunAt}",
                runAt);

            var startedAt = GetBangkokNow();
            if (startedAt.TimeOfDay >= runAt)
            {
                _logger.LogInformation(
                    "Meeting reminder run time has already passed today. Running startup sync now. StartedAt={StartedAt}, RunAt={RunAt}",
                    startedAt,
                    runAt);
                await RunOnceForDateAsync(startedAt.Date, "startup", stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                runAt = await GetRunAtAsync(stoppingToken);
                var now = GetBangkokNow();
                var nextRun = NextRunAt(now, runAt);
                var delay = nextRun - now;
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;

                _logger.LogInformation("Next meeting reminder sync scheduled at {NextRun}", nextRun);
                await Task.Delay(delay, stoppingToken);
                await RunOnceForDateAsync(nextRun.Date, "scheduled", stoppingToken);
            }
        }

        private async Task RunOnceForDateAsync(DateTime runDate, string reason, CancellationToken cancellationToken)
        {
            if (!await TryMarkAutoRunDateAsync(runDate, cancellationToken))
            {
                _logger.LogInformation(
                    "Meeting chat reminder skipped because it already ran today. Reason={Reason}, RunDate={RunDate}",
                    reason,
                    runDate.ToString(RunDateFormat, CultureInfo.InvariantCulture));
                return;
            }

            await RunOnceAsync(cancellationToken);
        }

        private async Task<bool> TryMarkAutoRunDateAsync(DateTime runDate, CancellationToken cancellationToken)
        {
            var runDateText = runDate.ToString(RunDateFormat, CultureInfo.InvariantCulture);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var affected = await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE system_config
                SET config_value = @runDate,
                    description = @description,
                    updated_at = NOW()
                WHERE config_key = @configKey
                  AND (config_value IS NULL OR config_value <> @runDate);
                """,
                new object[]
                {
                    new MySqlParameter("@runDate", runDateText),
                    new MySqlParameter("@description", LastAutoRunDateDescription),
                    new MySqlParameter("@configKey", LastAutoRunDateConfigKey)
                },
                cancellationToken);

            if (affected > 0)
                return true;

            var alreadyMarked = await db.SystemConfigs
                .AsNoTracking()
                .AnyAsync(x => x.ConfigKey == LastAutoRunDateConfigKey
                    && x.ConfigValue == runDateText,
                    cancellationToken);

            if (alreadyMarked)
                return false;

            try
            {
                affected = await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO system_config(config_key, config_value, description, updated_at)
                    VALUES(@configKey, @runDate, @description, NOW());
                    """,
                    new object[]
                    {
                        new MySqlParameter("@configKey", LastAutoRunDateConfigKey),
                        new MySqlParameter("@runDate", runDateText),
                        new MySqlParameter("@description", LastAutoRunDateDescription)
                    },
                    cancellationToken);

                return affected > 0;
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return await TryMarkAutoRunDateAsync(runDate, cancellationToken);
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
                var fieldService = scope.ServiceProvider.GetRequiredService<FieldServiceNotificationService>();
                var fieldLineResult = await SendFieldServiceLineRemindersSafelyAsync(fieldService, cancellationToken);
                var fieldTelegramResult = await SendFieldServiceTelegramRemindersSafelyAsync(fieldService, cancellationToken);
                var result = new MeetingNotificationResult(
                    lineResult.SentCount + telegramResult.SentCount + fieldLineResult.SentCount + fieldTelegramResult.SentCount,
                    lineResult.SkippedCount + telegramResult.SkippedCount + fieldLineResult.SkippedCount + fieldTelegramResult.SkippedCount,
                    lineResult.FailedCount + telegramResult.FailedCount + fieldLineResult.FailedCount + fieldTelegramResult.FailedCount);

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

        private async Task<FieldServiceNotificationResult> SendFieldServiceLineRemindersSafelyAsync(
            FieldServiceNotificationService service,
            CancellationToken cancellationToken)
        {
            try
            {
                return await service.SendLineRemindersAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Field Service LINE reminder failed");
                return new FieldServiceNotificationResult(0, 0, 1, ex.Message);
            }
        }

        private async Task<FieldServiceNotificationResult> SendFieldServiceTelegramRemindersSafelyAsync(
            FieldServiceNotificationService service,
            CancellationToken cancellationToken)
        {
            try
            {
                return await service.SendTelegramRemindersAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Field Service Telegram reminder failed");
                return new FieldServiceNotificationResult(0, 0, 1, ex.Message);
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
                    .Where(x => x.ConfigKey == "MEETING_NOTIFICATION_RUN_AT")
                    .Select(x => x.ConfigValue)
                    .FirstOrDefaultAsync(cancellationToken);

                return ParseRunAt(value, _defaultRunAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read MEETING_NOTIFICATION_RUN_AT from system_config. Using fallback RunAt={RunAt}", _defaultRunAt);
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

            return ParseRunAt(defaultValue, new TimeSpan(6, 0, 0));
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
