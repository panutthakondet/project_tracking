using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;

namespace ProjectTracking.Services
{
    public class MeetingNotificationService
    {
        private static readonly int[] LineReminderDays = { 3, 2, 1, 0 };
        private static readonly int[] TelegramReminderDays = { 3, 2, 1, 0 };
        private static readonly SemaphoreSlim NotificationTableEnsureLock = new(1, 1);
        private const string CreatedEmailKind = "created_email";
        private const string CreatedLineKind = "created_line";
        private const string CreatedTelegramKind = "created_telegram";
        private const string CancelledEmailKind = "cancelled_email";
        private const string CancelledLineKind = "cancelled_line";
        private const string CancelledTelegramKind = "cancelled_telegram";
        private const string UpdatedEmailKindPrefix = "updated_email";
        private const string UpdatedLineKindPrefix = "updated_line";
        private const string UpdatedTelegramKindPrefix = "updated_telegram";

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly EmailService _emailService;
        private readonly LineMessagingService _lineMessagingService;
        private readonly LineNotificationSettingsService _lineNotificationSettings;
        private readonly TelegramMessagingService _telegramMessagingService;
        private readonly TelegramNotificationSettingsService _telegramNotificationSettings;
        private readonly ILogger<MeetingNotificationService> _logger;
        private readonly string _appBaseUrl;

        public MeetingNotificationService(
            IDbContextFactory<AppDbContext> dbFactory,
            EmailService emailService,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            IConfiguration configuration,
            ILogger<MeetingNotificationService> logger)
        {
            _dbFactory = dbFactory;
            _emailService = emailService;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _logger = logger;
            _appBaseUrl = (Environment.GetEnvironmentVariable("APP_BASE_URL")
                ?? configuration["APP_BASE_URL"]
                ?? "").TrimEnd('/');
        }

        public async Task<EmailAttachment?> BuildCalendarAttachmentAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var meeting = await LoadMeetingAsync(db, meetingId, cancellationToken);
            if (meeting == null)
                return null;

            return BuildCalendarAttachment(
                meeting.Id,
                meeting.Title,
                meeting.Description,
                meeting.Location,
                meeting.ProjectName,
                meeting.StartAt,
                meeting.EndAt);
        }

        public async Task<IReadOnlyDictionary<int, MeetingAttendeeNotificationStatus>> GetAttendeeNotificationStatusesAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var rows = await db.MeetingEmailNotifications
                .AsNoTracking()
                .Where(x => x.MeetingId == meetingId)
                .Select(x => new { x.AttendeeId, x.Kind })
                .ToListAsync(cancellationToken);

            return rows
                .GroupBy(x => x.AttendeeId)
                .ToDictionary(
                    x => x.Key,
                    x => new MeetingAttendeeNotificationStatus(
                        EmailSent: x.Any(n => n.Kind == CreatedEmailKind
                            || n.Kind == CancelledEmailKind
                            || n.Kind.StartsWith(UpdatedEmailKindPrefix)),
                        LineSent: x.Any(n => n.Kind == CreatedLineKind
                            || n.Kind == CancelledLineKind
                            || n.Kind.StartsWith(UpdatedLineKindPrefix)
                            || n.Kind.StartsWith("line_reminder_")),
                        TelegramSent: x.Any(n => n.Kind == CreatedTelegramKind
                            || n.Kind == CancelledTelegramKind
                            || n.Kind.StartsWith(UpdatedTelegramKindPrefix)
                            || n.Kind.StartsWith("telegram_reminder_"))));
        }

        public async Task<MeetingNotificationResult> SendCreatedNotificationsAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            var emailTask = SendChannelSafelyAsync(
                "Email",
                meetingId,
                () => SendCreatedEmailAsync(meetingId, cancellationToken));
            var lineTask = SendChannelSafelyAsync(
                "LINE",
                meetingId,
                () => SendCreatedLineAsync(meetingId, cancellationToken));
            var telegramTask = SendChannelSafelyAsync(
                "Telegram",
                meetingId,
                () => SendCreatedTelegramAsync(meetingId, cancellationToken));

            await Task.WhenAll(emailTask, lineTask, telegramTask);

            var emailResult = await emailTask;
            var lineResult = await lineTask;
            var telegramResult = await telegramTask;

            return new MeetingNotificationResult(
                emailResult.SentCount + lineResult.SentCount + telegramResult.SentCount,
                emailResult.SkippedCount + lineResult.SkippedCount + telegramResult.SkippedCount,
                emailResult.FailedCount + lineResult.FailedCount + telegramResult.FailedCount,
                BuildNotificationBreakdown(emailResult, lineResult, telegramResult));
        }

        public async Task<MeetingNotificationResult> SendCreatedEmailAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var meeting = await LoadMeetingAsync(db, meetingId, cancellationToken);
            if (meeting == null)
                return new MeetingNotificationResult(0, 0, 1, "ไม่พบ Meeting");

            var recipients = await LoadEmailRecipientsAsync(db, meetingId, cancellationToken);
            if (recipients.Count == 0)
                return new MeetingNotificationResult(0, 1, 0, "ไม่มี email ของผู้เข้าร่วม");

            var attachment = await BuildCalendarAttachmentAsync(meeting.Id, cancellationToken);
            if (attachment == null)
                return new MeetingNotificationResult(0, 0, 1, "สร้างไฟล์ calendar ไม่สำเร็จ");

            var sent = 0;
            var skipped = 0;
            var failed = 0;
            var failureReasons = new List<string>();
            var notifiedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var recipient in recipients)
            {
                if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CreatedEmailKind, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                if (!notifiedEmails.Add(recipient.Email.Trim()))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var detailUrl = ToAbsoluteUrl($"/Meetings/Show/{meeting.Id}") ?? $"/Meetings/Show/{meeting.Id}";

                    await _emailService.SendAsync(
                        recipient.Email,
                        $"เชิญประชุม: {meeting.Title}",
                        BuildCreatedEmailBody(meeting, recipient.DisplayName, detailUrl),
                        attachments: new[] { attachment });

                    await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CreatedEmailKind, cancellationToken);
                    sent++;
                }
                catch (Exception ex)
                {
                    failed++;
                    AddFailureReason(failureReasons, ex.Message);
                    _logger.LogError(
                        ex,
                        "Failed to send meeting created email. MeetingId={MeetingId}, AttendeeId={AttendeeId}, Email={Email}",
                        meeting.Id,
                        recipient.AttendeeId,
                        recipient.Email);
                }
            }

            return new MeetingNotificationResult(sent, skipped, failed, BuildDetail(failureReasons));
        }

        public async Task<MeetingNotificationResult> SendUpdatedNotificationsAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var meeting = await LoadMeetingAsync(db, meetingId, cancellationToken);
            if (meeting == null)
                return new MeetingNotificationResult(0, 0, 0);

            var attachment = await BuildCalendarAttachmentAsync(meeting.Id, cancellationToken);
            var telegramAttachment = ToTelegramAttachment(attachment);
            var detailUrl = ToAbsoluteUrl($"/Meetings/Show/{meeting.Id}") ?? $"/Meetings/Show/{meeting.Id}";
            var calendarUrl = ToAbsoluteUrl($"/Meetings/Calendar/{meeting.Id}") ?? $"/Meetings/Calendar/{meeting.Id}";
            var emailKind = UpdatedNotificationKind(UpdatedEmailKindPrefix, meeting);
            var lineKind = UpdatedNotificationKind(UpdatedLineKindPrefix, meeting);
            var telegramKind = UpdatedNotificationKind(UpdatedTelegramKindPrefix, meeting);
            var sent = 0;
            var skipped = 0;
            var failed = 0;

            var emailRecipients = await LoadEmailRecipientsAsync(db, meetingId, cancellationToken);
            var notifiedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var recipient in emailRecipients)
            {
                if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, emailKind, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                if (!notifiedEmails.Add(recipient.Email.Trim()))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    await _emailService.SendAsync(
                        recipient.Email,
                        $"อัปเดตประชุม: {meeting.Title}",
                        BuildUpdatedEmailBody(meeting, recipient.DisplayName, detailUrl),
                        attachments: attachment == null ? null : new[] { attachment });

                    await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, emailKind, cancellationToken);
                    sent++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(
                        ex,
                        "Failed to send meeting updated email. MeetingId={MeetingId}, AttendeeId={AttendeeId}, Email={Email}",
                        meeting.Id,
                        recipient.AttendeeId,
                        recipient.Email);
                }
            }

            if (_lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.MeetingsUpdate, cancellationToken))
            {
                var lineRecipients = await LoadMeetingRecipientsAsync(db, meetingId, cancellationToken);
                var notifiedLineUserIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var recipient in lineRecipients)
                {
                    if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, lineKind, cancellationToken))
                    {
                        skipped++;
                        continue;
                    }

                    var lineUserIds = await _lineMessagingService.GetActiveLineUserIdsForEmployeeAsync(
                        recipient.EmpId,
                        cancellationToken);
                    if (lineUserIds.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    var unsentLineUserIds = lineUserIds
                        .Where(lineUserId => notifiedLineUserIds.Add(lineUserId))
                        .ToList();

                    if (unsentLineUserIds.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var lineSendCount = await _lineMessagingService.SendNotificationToLineUserIdsAsync(
                            unsentLineUserIds,
                            $"อัปเดตประชุม: {meeting.Title}",
                            BuildUpdatedTelegramMessage(meeting, detailUrl, calendarUrl),
                            detailUrl,
                            cancellationToken,
                            recipient.EmpId);

                        if (lineSendCount > 0)
                        {
                            await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, lineKind, cancellationToken);
                            sent += lineSendCount;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(
                            ex,
                            "Failed to send meeting updated LINE. MeetingId={MeetingId}, AttendeeId={AttendeeId}, EmpId={EmpId}",
                            meeting.Id,
                            recipient.AttendeeId,
                            recipient.EmpId);
                    }
                }
            }

            if (!_telegramMessagingService.IsConfigured
                || !await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.MeetingsUpdate, cancellationToken))
                return new MeetingNotificationResult(sent, skipped, failed);

            var telegramRecipients = await LoadMeetingRecipientsAsync(db, meetingId, cancellationToken);
            var notifiedTelegramChatIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var recipient in telegramRecipients)
            {
                if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, telegramKind, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                var chatIds = await _telegramMessagingService.GetActiveTelegramChatIdsForEmployeeAsync(
                    recipient.EmpId,
                    cancellationToken);
                if (chatIds.Count == 0)
                {
                    skipped++;
                    continue;
                }

                var unsentChatIds = chatIds
                    .Where(chatId => notifiedTelegramChatIds.Add(chatId))
                    .ToList();

                if (unsentChatIds.Count == 0)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var telegramSendCount = await _telegramMessagingService.SendNotificationToChatIdsAsync(
                        unsentChatIds,
                        $"อัปเดตประชุม: {meeting.Title}",
                        BuildUpdatedTelegramMessage(meeting, detailUrl, calendarUrl),
                        detailUrl,
                        cancellationToken,
                        telegramAttachment,
                        recipient.EmpId);

                    if (telegramSendCount > 0)
                    {
                        await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, telegramKind, cancellationToken);
                        sent += telegramSendCount;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(
                        ex,
                        "Failed to send meeting updated Telegram. MeetingId={MeetingId}, AttendeeId={AttendeeId}, EmpId={EmpId}",
                        meeting.Id,
                        recipient.AttendeeId,
                        recipient.EmpId);
                }
            }

            return new MeetingNotificationResult(sent, skipped, failed);
        }

        private async Task<MeetingNotificationResult> SendCreatedLineAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            if (!_lineMessagingService.IsConfigured)
                return new MeetingNotificationResult(0, 1, 0, "LINE ยังไม่ได้ตั้งค่า");

            if (!await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.MeetingsCreate, cancellationToken))
                return new MeetingNotificationResult(0, 1, 0, "LINE Meetings.Create ปิดอยู่");

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var meeting = await LoadMeetingAsync(db, meetingId, cancellationToken);
            if (meeting == null)
                return new MeetingNotificationResult(0, 0, 1, "ไม่พบ Meeting");

            var detailUrl = ToAbsoluteUrl($"/Meetings/Show/{meeting.Id}") ?? $"/Meetings/Show/{meeting.Id}";
            var calendarUrl = ToAbsoluteUrl($"/Meetings/Calendar/{meeting.Id}") ?? $"/Meetings/Calendar/{meeting.Id}";
            var recipients = await LoadMeetingRecipientsAsync(db, meetingId, cancellationToken);
            var sent = 0;
            var skipped = 0;
            var failed = 0;
            var missingRecipientCount = 0;
            var failureReasons = new List<string>();
            var notifiedLineUserIds = new HashSet<string>(StringComparer.Ordinal);

            if (recipients.Count == 0)
                return new MeetingNotificationResult(0, 1, 0, "ไม่มีผู้เข้าร่วมสำหรับ LINE");

            foreach (var recipient in recipients)
            {
                if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CreatedLineKind, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                var lineUserIds = await _lineMessagingService.GetActiveLineUserIdsForEmployeeAsync(
                    recipient.EmpId,
                    cancellationToken);
                if (lineUserIds.Count == 0)
                {
                    skipped++;
                    missingRecipientCount++;
                    continue;
                }

                var unsentLineUserIds = lineUserIds
                    .Where(lineUserId => notifiedLineUserIds.Add(lineUserId))
                    .ToList();

                if (unsentLineUserIds.Count == 0)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var lineSendCount = await _lineMessagingService.SendNotificationToLineUserIdsAsync(
                        unsentLineUserIds,
                        $"เชิญประชุม: {meeting.Title}",
                        BuildCreatedTelegramMessage(meeting, detailUrl, calendarUrl),
                        detailUrl,
                        cancellationToken,
                        recipient.EmpId);

                    if (lineSendCount > 0)
                    {
                        await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CreatedLineKind, cancellationToken);
                        sent += lineSendCount;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    AddFailureReason(failureReasons, ex.Message);
                    _logger.LogError(
                        ex,
                        "Failed to send meeting created LINE. MeetingId={MeetingId}, AttendeeId={AttendeeId}, EmpId={EmpId}",
                        meeting.Id,
                        recipient.AttendeeId,
                        recipient.EmpId);
                }
            }

            return new MeetingNotificationResult(
                sent,
                skipped,
                failed,
                BuildDetail(
                    missingRecipientCount > 0 ? $"ไม่มี LINE user id {missingRecipientCount} คน" : null,
                    BuildDetail(failureReasons)));
        }

        private async Task<MeetingNotificationResult> SendCreatedTelegramAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            if (!_telegramMessagingService.IsConfigured)
                return new MeetingNotificationResult(0, 1, 0, "Telegram ยังไม่ได้ตั้งค่า bot");

            if (!await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.MeetingsCreate, cancellationToken))
                return new MeetingNotificationResult(0, 1, 0, "Telegram Meetings.Create ปิดอยู่");

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var meeting = await LoadMeetingAsync(db, meetingId, cancellationToken);
            if (meeting == null)
                return new MeetingNotificationResult(0, 0, 1, "ไม่พบ Meeting");

            var detailUrl = ToAbsoluteUrl($"/Meetings/Show/{meeting.Id}") ?? $"/Meetings/Show/{meeting.Id}";
            var calendarUrl = ToAbsoluteUrl($"/Meetings/Calendar/{meeting.Id}") ?? $"/Meetings/Calendar/{meeting.Id}";
            var attachment = await BuildCalendarAttachmentAsync(meeting.Id, cancellationToken);
            var telegramAttachment = ToTelegramAttachment(attachment);
            var recipients = await LoadMeetingRecipientsAsync(db, meetingId, cancellationToken);
            var sent = 0;
            var skipped = 0;
            var failed = 0;
            var missingRecipientCount = 0;
            var failureReasons = new List<string>();
            var notifiedTelegramChatIds = new HashSet<string>(StringComparer.Ordinal);

            if (recipients.Count == 0)
                return new MeetingNotificationResult(0, 1, 0, "ไม่มีผู้เข้าร่วมสำหรับ Telegram");

            foreach (var recipient in recipients)
            {
                if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CreatedTelegramKind, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                var chatIds = await _telegramMessagingService.GetActiveTelegramChatIdsForEmployeeAsync(
                    recipient.EmpId,
                    cancellationToken);
                if (chatIds.Count == 0)
                {
                    skipped++;
                    missingRecipientCount++;
                    continue;
                }

                var unsentChatIds = chatIds
                    .Where(chatId => notifiedTelegramChatIds.Add(chatId))
                    .ToList();

                if (unsentChatIds.Count == 0)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var telegramSendCount = await _telegramMessagingService.SendNotificationToChatIdsAsync(
                        unsentChatIds,
                        $"เชิญประชุม: {meeting.Title}",
                        BuildCreatedTelegramMessage(meeting, detailUrl, calendarUrl),
                        detailUrl,
                        cancellationToken,
                        telegramAttachment,
                        recipient.EmpId);

                    if (telegramSendCount > 0)
                    {
                        await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CreatedTelegramKind, cancellationToken);
                        sent += telegramSendCount;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    AddFailureReason(failureReasons, ex.Message);
                    _logger.LogError(
                        ex,
                        "Failed to send meeting created Telegram. MeetingId={MeetingId}, AttendeeId={AttendeeId}, EmpId={EmpId}",
                        meeting.Id,
                        recipient.AttendeeId,
                        recipient.EmpId);
                }
            }

            return new MeetingNotificationResult(
                sent,
                skipped,
                failed,
                BuildDetail(
                    missingRecipientCount > 0 ? $"ไม่มี Telegram chat id {missingRecipientCount} คน" : null,
                    BuildDetail(failureReasons)));
        }

        public async Task<MeetingNotificationResult> SendCancelledNotificationsAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var meeting = await LoadMeetingAsync(db, meetingId, cancellationToken);
            if (meeting == null)
                return new MeetingNotificationResult(0, 0, 0);

            var detailUrl = ToAbsoluteUrl($"/Meetings/Show/{meeting.Id}") ?? $"/Meetings/Show/{meeting.Id}";
            var sent = 0;
            var skipped = 0;
            var failed = 0;

            var emailRecipients = await LoadEmailRecipientsAsync(db, meetingId, cancellationToken);
            var notifiedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var recipient in emailRecipients)
            {
                if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CancelledEmailKind, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                if (!notifiedEmails.Add(recipient.Email.Trim()))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    await _emailService.SendAsync(
                        recipient.Email,
                        $"ยกเลิกประชุม: {meeting.Title}",
                        BuildCancelledEmailBody(meeting, recipient.DisplayName, detailUrl));

                    await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CancelledEmailKind, cancellationToken);
                    sent++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(
                        ex,
                        "Failed to send meeting cancelled email. MeetingId={MeetingId}, AttendeeId={AttendeeId}, Email={Email}",
                        meeting.Id,
                        recipient.AttendeeId,
                        recipient.Email);
                }
            }

            if (_lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.MeetingsCancel, cancellationToken))
            {
                var lineRecipients = await LoadMeetingRecipientsAsync(db, meetingId, cancellationToken);
                var notifiedLineUserIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var recipient in lineRecipients)
                {
                    if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CancelledLineKind, cancellationToken))
                    {
                        skipped++;
                        continue;
                    }

                    var lineUserIds = await _lineMessagingService.GetActiveLineUserIdsForEmployeeAsync(
                        recipient.EmpId,
                        cancellationToken);
                    if (lineUserIds.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    var unsentLineUserIds = lineUserIds
                        .Where(lineUserId => notifiedLineUserIds.Add(lineUserId))
                        .ToList();

                    if (unsentLineUserIds.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var lineSendCount = await _lineMessagingService.SendNotificationToLineUserIdsAsync(
                            unsentLineUserIds,
                            $"ยกเลิกประชุม: {meeting.Title}",
                            BuildCancelledTelegramMessage(meeting, detailUrl),
                            detailUrl,
                            cancellationToken,
                            recipient.EmpId);

                        if (lineSendCount > 0)
                        {
                            await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CancelledLineKind, cancellationToken);
                            sent += lineSendCount;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(
                            ex,
                            "Failed to send meeting cancelled LINE. MeetingId={MeetingId}, AttendeeId={AttendeeId}, EmpId={EmpId}",
                            meeting.Id,
                            recipient.AttendeeId,
                            recipient.EmpId);
                    }
                }
            }

            if (!_telegramMessagingService.IsConfigured
                || !await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.MeetingsCancel, cancellationToken))
                return new MeetingNotificationResult(sent, skipped, failed);

            var telegramRecipients = await LoadMeetingRecipientsAsync(db, meetingId, cancellationToken);
            var notifiedTelegramChatIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var recipient in telegramRecipients)
            {
                if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CancelledTelegramKind, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                var chatIds = await _telegramMessagingService.GetActiveTelegramChatIdsForEmployeeAsync(
                    recipient.EmpId,
                    cancellationToken);
                if (chatIds.Count == 0)
                {
                    skipped++;
                    continue;
                }

                var unsentChatIds = chatIds
                    .Where(chatId => notifiedTelegramChatIds.Add(chatId))
                    .ToList();

                if (unsentChatIds.Count == 0)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var telegramSendCount = await _telegramMessagingService.SendNotificationToChatIdsAsync(
                        unsentChatIds,
                        $"ยกเลิกประชุม: {meeting.Title}",
                        BuildCancelledTelegramMessage(meeting, detailUrl),
                        detailUrl,
                        cancellationToken,
                        null,
                        recipient.EmpId);

                    if (telegramSendCount > 0)
                    {
                        await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CancelledTelegramKind, cancellationToken);
                        sent += telegramSendCount;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(
                        ex,
                        "Failed to send meeting cancelled Telegram. MeetingId={MeetingId}, AttendeeId={AttendeeId}, EmpId={EmpId}",
                        meeting.Id,
                        recipient.AttendeeId,
                        recipient.EmpId);
                }
            }

            return new MeetingNotificationResult(sent, skipped, failed);
        }

        public async Task<MeetingNotificationResult> SendLineRemindersAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_lineMessagingService.IsConfigured)
            {
                _logger.LogDebug("Meeting LINE reminder skipped because LINE is not configured.");
                return new MeetingNotificationResult(0, 0, 0);
            }

            if (!await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.MeetingsReminder, cancellationToken))
            {
                _logger.LogInformation("Meeting LINE reminder skipped because it is disabled in Line Notification settings.");
                return new MeetingNotificationResult(0, 0, 0);
            }

            if (!await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.MeetingsAuto, cancellationToken))
            {
                _logger.LogInformation("Meeting LINE reminder skipped because LINE meeting auto send is disabled.");
                return new MeetingNotificationResult(0, 0, 0);
            }

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var today = GetBangkokToday();
            var sent = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var daysBefore in LineReminderDays)
            {
                var reminderDate = today.AddDays(daysBefore);
                var meetings = await LoadMeetingsByDateAsync(db, reminderDate, cancellationToken);

                foreach (var meeting in meetings)
                {
                    var recipients = await LoadMeetingRecipientsAsync(db, meeting.Id, cancellationToken);
                    if (recipients.Count == 0)
                        continue;

                    var kind = LineReminderKind(daysBefore);
                    var notifiedLineUserIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var recipient in recipients)
                    {
                        if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, kind, cancellationToken))
                        {
                            skipped++;
                            continue;
                        }

                        var lineUserIds = await _lineMessagingService.GetActiveLineUserIdsForEmployeeAsync(
                            recipient.EmpId,
                            cancellationToken);
                        if (lineUserIds.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        var unsentLineUserIds = lineUserIds
                            .Where(lineUserId => notifiedLineUserIds.Add(lineUserId))
                            .ToList();

                        if (unsentLineUserIds.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        var detailPath = $"/Meetings/Show/{meeting.Id}";
                        var calendarPath = $"/Meetings/Calendar/{meeting.Id}";
                        var detailUrlForLog = ToAbsoluteUrl(detailPath);
                        var detailUrl = detailUrlForLog ?? detailPath;
                        var calendarUrl = ToAbsoluteUrl(calendarPath) ?? calendarPath;
                        var title = BuildTelegramTitle(daysBefore, meeting.Title);
                        var message = BuildTelegramMessage(meeting, daysBefore, detailUrl, calendarUrl);

                        if (await HasNotificationSendLogTodayAsync(
                            db,
                            "LINE",
                            recipient.EmpId,
                            title,
                            message,
                            detailUrlForLog,
                            today,
                            cancellationToken))
                        {
                            await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, kind, cancellationToken);
                            skipped++;
                            _logger.LogInformation(
                                "Meeting LINE reminder skipped because NotificationSendLogs already has a successful send today. MeetingId={MeetingId}, EmpId={EmpId}, DaysBefore={DaysBefore}",
                                meeting.Id,
                                recipient.EmpId,
                                daysBefore);
                            continue;
                        }

                        try
                        {
                            var lineSendCount = await _lineMessagingService.SendNotificationToLineUserIdsAsync(
                                unsentLineUserIds,
                                title,
                                message,
                                detailUrl,
                                cancellationToken,
                                recipient.EmpId);

                            if (lineSendCount > 0)
                            {
                                await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, kind, cancellationToken);
                                sent += lineSendCount;
                            }
                            else
                            {
                                skipped++;
                                _logger.LogDebug(
                                    "Meeting LINE reminder skipped because recipient has no active LINE binding. MeetingId={MeetingId}, EmpId={EmpId}, DaysBefore={DaysBefore}",
                                    meeting.Id,
                                    recipient.EmpId,
                                    daysBefore);
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            _logger.LogError(
                                ex,
                                "Failed to send meeting LINE reminder. MeetingId={MeetingId}, AttendeeId={AttendeeId}, EmpId={EmpId}, DaysBefore={DaysBefore}",
                                meeting.Id,
                                recipient.AttendeeId,
                                recipient.EmpId,
                                daysBefore);
                        }
                    }
                }
            }

            return new MeetingNotificationResult(sent, skipped, failed);
        }

        public async Task<MeetingNotificationResult> SendTelegramRemindersAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_telegramMessagingService.IsConfigured)
            {
                _logger.LogDebug("Meeting Telegram reminder skipped because Telegram is not configured.");
                return new MeetingNotificationResult(0, 0, 0);
            }

            if (!await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.MeetingsReminder, cancellationToken))
            {
                _logger.LogInformation("Meeting Telegram reminder skipped because it is disabled in Telegram Notification settings.");
                return new MeetingNotificationResult(0, 0, 0);
            }

            if (!await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.MeetingsAuto, cancellationToken))
            {
                _logger.LogInformation("Meeting Telegram reminder skipped because Telegram meeting auto send is disabled.");
                return new MeetingNotificationResult(0, 0, 0);
            }

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var today = GetBangkokToday();
            var sent = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var daysBefore in TelegramReminderDays)
            {
                var reminderDate = today.AddDays(daysBefore);
                var meetings = await LoadMeetingsByDateAsync(db, reminderDate, cancellationToken);

                foreach (var meeting in meetings)
                {
                    var recipients = await LoadMeetingRecipientsAsync(db, meeting.Id, cancellationToken);
                    if (recipients.Count == 0)
                        continue;

                    var kind = TelegramReminderKind(daysBefore);
                    var attachment = ToTelegramAttachment(await BuildCalendarAttachmentAsync(meeting.Id, cancellationToken));
                    var notifiedTelegramChatIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var recipient in recipients)
                    {
                        if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, kind, cancellationToken))
                        {
                            skipped++;
                            continue;
                        }

                        var chatIds = await _telegramMessagingService.GetActiveTelegramChatIdsForEmployeeAsync(
                            recipient.EmpId,
                            cancellationToken);
                        if (chatIds.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        var unsentChatIds = chatIds
                            .Where(chatId => notifiedTelegramChatIds.Add(chatId))
                            .ToList();

                        if (unsentChatIds.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        var detailPath = $"/Meetings/Show/{meeting.Id}";
                        var calendarPath = $"/Meetings/Calendar/{meeting.Id}";
                        var detailUrlForLog = ToAbsoluteUrl(detailPath);
                        var detailUrl = detailUrlForLog ?? detailPath;
                        var calendarUrl = ToAbsoluteUrl(calendarPath) ?? calendarPath;
                        var title = BuildTelegramTitle(daysBefore, meeting.Title);
                        var message = BuildTelegramMessage(meeting, daysBefore, detailUrl, calendarUrl);

                        if (await HasNotificationSendLogTodayAsync(
                            db,
                            "TELEGRAM",
                            recipient.EmpId,
                            title,
                            message,
                            detailUrlForLog,
                            today,
                            cancellationToken))
                        {
                            await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, kind, cancellationToken);
                            skipped++;
                            _logger.LogInformation(
                                "Meeting Telegram reminder skipped because NotificationSendLogs already has a successful send today. MeetingId={MeetingId}, EmpId={EmpId}, DaysBefore={DaysBefore}",
                                meeting.Id,
                                recipient.EmpId,
                                daysBefore);
                            continue;
                        }

                        try
                        {
                            var telegramSendCount = await _telegramMessagingService.SendNotificationToChatIdsAsync(
                                unsentChatIds,
                                title,
                                message,
                                detailUrl,
                                cancellationToken,
                                attachment,
                                recipient.EmpId);

                            if (telegramSendCount > 0)
                            {
                                await TryInsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, kind, cancellationToken);
                                sent += telegramSendCount;
                            }
                            else
                            {
                                skipped++;
                                _logger.LogDebug(
                                    "Meeting Telegram reminder skipped because recipient has no active Telegram binding. MeetingId={MeetingId}, EmpId={EmpId}, DaysBefore={DaysBefore}",
                                    meeting.Id,
                                    recipient.EmpId,
                                    daysBefore);
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            _logger.LogError(
                                ex,
                                "Failed to send meeting Telegram reminder. MeetingId={MeetingId}, AttendeeId={AttendeeId}, EmpId={EmpId}, DaysBefore={DaysBefore}",
                                meeting.Id,
                                recipient.AttendeeId,
                                recipient.EmpId,
                                daysBefore);
                        }
                    }
                }
            }

            return new MeetingNotificationResult(sent, skipped, failed);
        }

        private static async Task EnsureNotificationTableAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            await NotificationTableEnsureLock.WaitAsync(cancellationToken);
            try
            {
                const string sql = @"
CREATE TABLE IF NOT EXISTS meeting_email_notifications (
  id INT AUTO_INCREMENT PRIMARY KEY,
  meeting_id INT NOT NULL,
  attendee_id INT NOT NULL,
  kind VARCHAR(50) NOT NULL,
  sent_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_meeting_attendee_kind (meeting_id, attendee_id, kind),
  KEY idx_meeting (meeting_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";

                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);

                const string removeDuplicatesSql = @"
DELETE n1
FROM meeting_email_notifications n1
JOIN meeting_email_notifications n2
  ON n1.meeting_id = n2.meeting_id
 AND n1.attendee_id = n2.attendee_id
 AND n1.kind = n2.kind
 AND n1.id > n2.id;";

                await db.Database.ExecuteSqlRawAsync(removeDuplicatesSql, cancellationToken);

                const string hasAnyIndexSql = @"
SELECT COUNT(*) AS Value
FROM INFORMATION_SCHEMA.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'meeting_email_notifications'
  AND INDEX_NAME = 'uq_meeting_attendee_kind'";

                const string hasUniqueIndexSql = @"
SELECT COUNT(*) AS Value
FROM INFORMATION_SCHEMA.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'meeting_email_notifications'
  AND INDEX_NAME = 'uq_meeting_attendee_kind'
  AND NON_UNIQUE = 0";

                var hasAnyIndex = await db.Database
                    .SqlQueryRaw<int>(hasAnyIndexSql)
                    .SingleAsync(cancellationToken);
                var hasUniqueIndex = await db.Database
                    .SqlQueryRaw<int>(hasUniqueIndexSql)
                    .SingleAsync(cancellationToken);

                if (hasUniqueIndex == 0)
                {
                    if (hasAnyIndex > 0)
                    {
                        await db.Database.ExecuteSqlRawAsync(
                            "DROP INDEX uq_meeting_attendee_kind ON meeting_email_notifications;",
                            cancellationToken);
                    }

                    await db.Database.ExecuteSqlRawAsync(
                        "CREATE UNIQUE INDEX uq_meeting_attendee_kind ON meeting_email_notifications(meeting_id, attendee_id, kind);",
                        cancellationToken);
                }
            }
            finally
            {
                NotificationTableEnsureLock.Release();
            }
        }

        private static async Task<bool> TryInsertNotificationLogAsync(
            AppDbContext db,
            int meetingId,
            int attendeeId,
            string kind,
            CancellationToken cancellationToken)
        {
            const string sql = @"
INSERT IGNORE INTO meeting_email_notifications(meeting_id, attendee_id, kind, sent_at)
VALUES(@mid, @aid, @kind, NOW());";

            var affected = await db.Database.ExecuteSqlRawAsync(
                sql,
                new object[]
                {
                    new MySqlConnector.MySqlParameter("@mid", meetingId),
                    new MySqlConnector.MySqlParameter("@aid", attendeeId),
                    new MySqlConnector.MySqlParameter("@kind", kind)
                },
                cancellationToken);

            return affected > 0;
        }

        private static string BuildNotificationBreakdown(
            MeetingNotificationResult emailResult,
            MeetingNotificationResult lineResult,
            MeetingNotificationResult telegramResult)
        {
            var parts = new[]
            {
                FormatNotificationBreakdown("Email", emailResult),
                FormatNotificationBreakdown("LINE", lineResult),
                FormatNotificationBreakdown("Telegram", telegramResult)
            };

            return string.Join(" | ", parts);
        }

        private async Task<MeetingNotificationResult> SendChannelSafelyAsync(
            string channel,
            int meetingId,
            Func<Task<MeetingNotificationResult>> sendAsync)
        {
            try
            {
                return await sendAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Meeting created notification channel failed. Channel={Channel}, MeetingId={MeetingId}",
                    channel,
                    meetingId);

                return new MeetingNotificationResult(0, 0, 1, $"{channel} error: {ex.Message}");
            }
        }

        private static string FormatNotificationBreakdown(string channel, MeetingNotificationResult result)
        {
            var text = $"{channel}: ส่ง {result.SentCount}, ข้าม {result.SkippedCount}, ไม่สำเร็จ {result.FailedCount}";
            return string.IsNullOrWhiteSpace(result.Detail)
                ? text
                : $"{text} ({result.Detail})";
        }

        private static void AddFailureReason(List<string> reasons, string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return;

            var normalized = reason.Trim();
            if (!reasons.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                reasons.Add(normalized);
        }

        private static string BuildDetail(params string?[] parts)
            => string.Join("; ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        private static string BuildDetail(IReadOnlyCollection<string> parts)
            => string.Join("; ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Take(3));

        private static Task<bool> HasNotificationLogAsync(
            AppDbContext db,
            int meetingId,
            int attendeeId,
            string kind,
            CancellationToken cancellationToken)
            => db.MeetingEmailNotifications
                .AsNoTracking()
                .AnyAsync(x => x.MeetingId == meetingId
                    && x.AttendeeId == attendeeId
                    && x.Kind == kind,
                    cancellationToken);

        private static Task<bool> HasNotificationSendLogTodayAsync(
            AppDbContext db,
            string channel,
            int empId,
            string title,
            string? message,
            string? targetUrl,
            DateTime today,
            CancellationToken cancellationToken)
        {
            var tomorrow = today.AddDays(1);
            var normalizedChannel = channel.Trim().ToUpperInvariant();
            var normalizedTitle = TrimForLog(title, 255) ?? "";
            var normalizedTargetUrl = TrimForLog(targetUrl, 500);

            return db.NotificationSendLogs
                .AsNoTracking()
                .AnyAsync(x => x.SentAt >= today
                    && x.SentAt < tomorrow
                    && x.Channel == normalizedChannel
                    && x.RecipientEmpId == empId
                    && x.Title == normalizedTitle
                    && x.Message == message
                    && x.TargetUrl == normalizedTargetUrl,
                    cancellationToken);
        }

        private static string? TrimForLog(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static Task<bool> HasActiveLineRecipientAsync(
            AppDbContext db,
            int empId,
            CancellationToken cancellationToken)
            => db.LineRecipients
                .AsNoTracking()
                .AnyAsync(x => x.IsActive
                    && x.EmpId == empId
                    && x.LineUserId != null
                    && x.LineUserId != "",
                    cancellationToken);

        private static Task<bool> HasActiveTelegramRecipientAsync(
            AppDbContext db,
            int empId,
            CancellationToken cancellationToken)
            => db.TelegramRecipients
                .AsNoTracking()
                .AnyAsync(x => x.IsActive
                    && x.EmpId == empId
                    && x.TelegramChatId != null
                    && x.TelegramChatId != "",
                    cancellationToken);

        private static async Task<MeetingDetails?> LoadMeetingAsync(
            AppDbContext db,
            int meetingId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT
  m.id AS Id,
  m.title AS Title,
  m.description AS Description,
  m.location AS Location,
  CASE
    WHEN c.coop_name IS NULL OR c.coop_name = '' THEN p.project_name
    WHEN p.project_name IS NULL OR p.project_name = '' THEN c.coop_name
    ELSE CONCAT(c.coop_name, ' - ', p.project_name)
  END AS ProjectName,
  TIMESTAMP(m.meeting_date, m.start_time) AS StartAt,
  TIMESTAMP(m.meeting_date, m.end_time) AS EndAt,
  COALESCE(m.updated_at, m.created_at) AS UpdatedAt
FROM meetings m
LEFT JOIN project p ON p.project_id = m.project_id
LEFT JOIN cnt_m_coop c ON c.coop_id = p.coop_id
WHERE m.id = @mid
LIMIT 1;";

            var rows = await db.Database
                .SqlQueryRaw<MeetingDetails>(
                    sql,
                    new MySqlConnector.MySqlParameter("@mid", meetingId))
                .ToListAsync(cancellationToken);

            return rows.FirstOrDefault();
        }

        private static async Task<List<MeetingDetails>> LoadMeetingsByDateAsync(
            AppDbContext db,
            DateTime meetingDate,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT
  m.id AS Id,
  m.title AS Title,
  m.description AS Description,
  m.location AS Location,
  CASE
    WHEN c.coop_name IS NULL OR c.coop_name = '' THEN p.project_name
    WHEN p.project_name IS NULL OR p.project_name = '' THEN c.coop_name
    ELSE CONCAT(c.coop_name, ' - ', p.project_name)
  END AS ProjectName,
  TIMESTAMP(m.meeting_date, m.start_time) AS StartAt,
  TIMESTAMP(m.meeting_date, m.end_time) AS EndAt,
  COALESCE(m.updated_at, m.created_at) AS UpdatedAt
FROM meetings m
LEFT JOIN project p ON p.project_id = m.project_id
LEFT JOIN cnt_m_coop c ON c.coop_id = p.coop_id
WHERE DATE(m.meeting_date) = @meetingDate
  AND COALESCE(UPPER(m.status), 'ACTIVE') <> 'CANCELLED';";

            return await db.Database
                .SqlQueryRaw<MeetingDetails>(
                    sql,
                    new MySqlConnector.MySqlParameter("@meetingDate", meetingDate.Date))
                .ToListAsync(cancellationToken);
        }

        private static async Task<List<EmailRecipient>> LoadEmailRecipientsAsync(
            AppDbContext db,
            int meetingId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT
  q.attendee_id,
  q.emp_name,
  q.position,
  q.email
FROM (
  SELECT
    ma.id AS attendee_id,
    e.emp_name,
    e.position,
    COALESCE(
      (
        SELECT u.email
        FROM login_user u
        WHERE u.user_id = e.login_user_id
          AND u.email IS NOT NULL
          AND u.email <> ''
        LIMIT 1
      ),
      (
        SELECT u.email
        FROM login_user u
        WHERE u.emp_id = e.emp_id
          AND u.email IS NOT NULL
          AND u.email <> ''
        ORDER BY u.user_id
        LIMIT 1
      )
    ) AS email
  FROM meeting_attendees ma
  JOIN employee e ON e.emp_id = ma.user_id
  WHERE ma.meeting_id = @mid
) q
WHERE q.email IS NOT NULL
  AND q.email <> ''
ORDER BY q.attendee_id;";

            var rows = await db.Database
                .SqlQueryRaw<EmailRecipientRow>(
                    sql,
                    new MySqlConnector.MySqlParameter("@mid", meetingId))
                .ToListAsync(cancellationToken);

            return rows
                .Select(x => new EmailRecipient(
                    x.attendee_id,
                    BuildDisplayName(x.emp_name, x.position),
                    x.email))
                .ToList();
        }

        private static async Task<List<MeetingRecipientInfo>> LoadMeetingRecipientsAsync(
            AppDbContext db,
            int meetingId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT
  ma.id AS attendee_id,
  e.emp_id,
  e.emp_name,
  e.position
FROM meeting_attendees ma
JOIN employee e ON e.emp_id = ma.user_id
WHERE ma.meeting_id = @mid
ORDER BY ma.id;";

            var rows = await db.Database
                .SqlQueryRaw<MeetingRecipientRow>(
                    sql,
                    new MySqlConnector.MySqlParameter("@mid", meetingId))
                .ToListAsync(cancellationToken);

            return rows
                .Select(x => new MeetingRecipientInfo(
                    x.attendee_id,
                    x.emp_id,
                    BuildDisplayName(x.emp_name, x.position)))
                .ToList();
        }

        private static string BuildCreatedEmailBody(MeetingDetails meeting, string displayName, string detailUrl)
        {
            var sb = new StringBuilder();
            sb.Append($"สวัสดี {System.Net.WebUtility.HtmlEncode(displayName)}<br/>");
            sb.Append("คุณได้รับเชิญเข้าร่วมการประชุม<br/>");

            if (!string.IsNullOrWhiteSpace(meeting.ProjectName))
                sb.Append($"โครงการ: <b>{System.Net.WebUtility.HtmlEncode(meeting.ProjectName)}</b><br/>");

            sb.Append($"หัวข้อ: <b>{System.Net.WebUtility.HtmlEncode(meeting.Title)}</b><br/>");
            sb.Append($"วันที่: <b>{FormatThaiDate(meeting.StartAt)}</b><br/>");
            sb.Append($"เวลา: <b>{meeting.StartAt:HH:mm} - {meeting.EndAt:HH:mm}</b><br/>");

            if (!string.IsNullOrWhiteSpace(meeting.Location))
                sb.Append($"สถานที่: {System.Net.WebUtility.HtmlEncode(meeting.Location)}<br/>");

            if (!string.IsNullOrWhiteSpace(meeting.Description))
                sb.Append($"รายละเอียด: {System.Net.WebUtility.HtmlEncode(meeting.Description)}<br/>");

            var encodedDetailUrl = System.Net.WebUtility.HtmlEncode(detailUrl);
            sb.Append("<br/>");
            sb.Append($@"<a href=""{encodedDetailUrl}"" style=""display:inline-block;background:#0F766E;color:#ffffff;text-decoration:none;padding:10px 16px;border-radius:8px;font-weight:700;"">เปิดรายละเอียด Meeting</a><br/>");
            sb.Append($@"ลิงก์รายละเอียด: <a href=""{encodedDetailUrl}"">{encodedDetailUrl}</a><br/>");

            sb.Append("<br/><small>ProjectTracking</small>");
            return sb.ToString();
        }

        private static string BuildUpdatedEmailBody(MeetingDetails meeting, string displayName, string detailUrl)
        {
            var sb = new StringBuilder();
            sb.Append($"สวัสดี {System.Net.WebUtility.HtmlEncode(displayName)}<br/>");
            sb.Append("<b>มีการอัปเดตรายละเอียดการประชุม</b><br/>");

            if (!string.IsNullOrWhiteSpace(meeting.ProjectName))
                sb.Append($"โครงการ: <b>{System.Net.WebUtility.HtmlEncode(meeting.ProjectName)}</b><br/>");

            sb.Append($"หัวข้อ: <b>{System.Net.WebUtility.HtmlEncode(meeting.Title)}</b><br/>");
            sb.Append($"วันที่: <b>{FormatThaiDate(meeting.StartAt)}</b><br/>");
            sb.Append($"เวลา: <b>{meeting.StartAt:HH:mm} - {meeting.EndAt:HH:mm}</b><br/>");

            if (!string.IsNullOrWhiteSpace(meeting.Location))
                sb.Append($"สถานที่: {System.Net.WebUtility.HtmlEncode(meeting.Location)}<br/>");

            if (!string.IsNullOrWhiteSpace(meeting.Description))
                sb.Append($"รายละเอียด: {System.Net.WebUtility.HtmlEncode(meeting.Description)}<br/>");

            var encodedDetailUrl = System.Net.WebUtility.HtmlEncode(detailUrl);
            sb.Append("<br/>");
            sb.Append($@"<a href=""{encodedDetailUrl}"" style=""display:inline-block;background:#2563EB;color:#ffffff;text-decoration:none;padding:10px 16px;border-radius:8px;font-weight:700;"">เปิดรายละเอียด Meeting</a><br/>");
            sb.Append($@"ลิงก์รายละเอียด: <a href=""{encodedDetailUrl}"">{encodedDetailUrl}</a><br/>");

            sb.Append("<br/><small>ProjectTracking</small>");
            return sb.ToString();
        }

        private static string BuildCancelledEmailBody(MeetingDetails meeting, string displayName, string detailUrl)
        {
            var sb = new StringBuilder();
            sb.Append($"สวัสดี {System.Net.WebUtility.HtmlEncode(displayName)}<br/>");
            sb.Append("<b>การประชุมนี้ถูกยกเลิกแล้ว</b><br/>");

            if (!string.IsNullOrWhiteSpace(meeting.ProjectName))
                sb.Append($"โครงการ: <b>{System.Net.WebUtility.HtmlEncode(meeting.ProjectName)}</b><br/>");

            sb.Append($"หัวข้อ: <b>{System.Net.WebUtility.HtmlEncode(meeting.Title)}</b><br/>");
            sb.Append($"วันที่: <b>{FormatThaiDate(meeting.StartAt)}</b><br/>");
            sb.Append($"เวลา: <b>{meeting.StartAt:HH:mm} - {meeting.EndAt:HH:mm}</b><br/>");

            if (!string.IsNullOrWhiteSpace(meeting.Location))
                sb.Append($"สถานที่: {System.Net.WebUtility.HtmlEncode(meeting.Location)}<br/>");

            if (!string.IsNullOrWhiteSpace(meeting.Description))
                sb.Append($"รายละเอียด: {System.Net.WebUtility.HtmlEncode(meeting.Description)}<br/>");

            var encodedDetailUrl = System.Net.WebUtility.HtmlEncode(detailUrl);
            sb.Append("<br/>");
            sb.Append($@"<a href=""{encodedDetailUrl}"" style=""display:inline-block;background:#DC2626;color:#ffffff;text-decoration:none;padding:10px 16px;border-radius:8px;font-weight:700;"">เปิดรายละเอียด Meeting</a><br/>");
            sb.Append($@"ลิงก์รายละเอียด: <a href=""{encodedDetailUrl}"">{encodedDetailUrl}</a><br/>");

            sb.Append("<br/><small>ProjectTracking</small>");
            return sb.ToString();
        }

        private static string BuildTelegramTitle(int daysBefore, string meetingTitle)
        {
            var prefix = daysBefore == 0
                ? "แจ้งเตือนประชุมวันนี้"
                : $"แจ้งเตือนประชุมล่วงหน้า {daysBefore} วัน";

            return $"{prefix}: {meetingTitle}";
        }

        private static string BuildCreatedTelegramMessage(MeetingDetails meeting, string? detailUrl, string? calendarUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine("คุณได้รับเชิญเข้าร่วมการประชุม");

            AppendTelegramMeetingDetails(sb, meeting);

            if (!string.IsNullOrWhiteSpace(detailUrl))
                sb.AppendLine($"ลิงก์รายละเอียด: {detailUrl}");

            if (!string.IsNullOrWhiteSpace(calendarUrl))
                sb.AppendLine($"ไฟล์ปฏิทิน (.ics): {calendarUrl}");

            return sb.ToString().Trim();
        }

        private static string BuildUpdatedTelegramMessage(MeetingDetails meeting, string? detailUrl, string? calendarUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine("มีการอัปเดตรายละเอียดการประชุม");

            AppendTelegramMeetingDetails(sb, meeting);

            if (!string.IsNullOrWhiteSpace(detailUrl))
                sb.AppendLine($"ลิงก์รายละเอียด: {detailUrl}");

            if (!string.IsNullOrWhiteSpace(calendarUrl))
                sb.AppendLine($"ไฟล์ปฏิทิน (.ics): {calendarUrl}");

            return sb.ToString().Trim();
        }

        private static string BuildTelegramMessage(
            MeetingDetails meeting,
            int daysBefore,
            string? detailUrl,
            string? calendarUrl)
        {
            var sb = new StringBuilder();

            AppendTelegramMeetingDetails(sb, meeting);

            if (!string.IsNullOrWhiteSpace(detailUrl))
                sb.AppendLine($"ลิงก์รายละเอียด: {detailUrl}");

            if (!string.IsNullOrWhiteSpace(calendarUrl))
                sb.AppendLine($"ไฟล์ปฏิทิน (.ics): {calendarUrl}");

            sb.Append(daysBefore == 0
                ? "การประชุมนี้มีกำหนดในวันนี้"
                : $"การประชุมนี้จะเริ่มในอีก {daysBefore} วัน");

            return sb.ToString();
        }

        private static string BuildCancelledTelegramMessage(MeetingDetails meeting, string? detailUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine("การประชุมนี้ถูกยกเลิกแล้ว");

            AppendTelegramMeetingDetails(sb, meeting);

            if (!string.IsNullOrWhiteSpace(detailUrl))
                sb.AppendLine($"ลิงก์รายละเอียด: {detailUrl}");

            return sb.ToString().Trim();
        }

        private static void AppendTelegramMeetingDetails(StringBuilder sb, MeetingDetails meeting)
        {
            if (!string.IsNullOrWhiteSpace(meeting.ProjectName))
                sb.AppendLine($"โครงการ: {meeting.ProjectName}");

            sb.AppendLine($"หัวข้อ: {meeting.Title}");
            sb.AppendLine($"วันที่: {FormatThaiDate(meeting.StartAt)}");
            sb.AppendLine($"เวลา: {meeting.StartAt:HH:mm} - {meeting.EndAt:HH:mm}");

            if (!string.IsNullOrWhiteSpace(meeting.Location))
                sb.AppendLine($"สถานที่: {meeting.Location}");

            if (!string.IsNullOrWhiteSpace(meeting.Description))
                sb.AppendLine($"รายละเอียด: {meeting.Description}");
        }

        private static TelegramAttachment? ToTelegramAttachment(EmailAttachment? attachment)
        {
            return attachment == null
                ? null
                : new TelegramAttachment(attachment.FileName, attachment.ContentType, attachment.Content);
        }

        private static EmailAttachment BuildCalendarAttachment(
            int meetingId,
            string title,
            string? description,
            string? location,
            string? projectName,
            DateTime startAt,
            DateTime endAt)
        {
            if (endAt <= startAt)
                endAt = startAt.AddHours(1);

            var summary = string.IsNullOrWhiteSpace(projectName)
                ? title
                : $"{projectName} - {title}";

            var detail = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(projectName))
                detail.Append("โครงการ: ").Append(projectName).Append('\n');

            if (!string.IsNullOrWhiteSpace(description))
                detail.Append(description);

            var lines = new List<string>
            {
                "BEGIN:VCALENDAR",
                "VERSION:2.0",
                "PRODID:-//SO-AT Solution//ProjectTracking//TH",
                "CALSCALE:GREGORIAN",
                "METHOD:PUBLISH",
                "BEGIN:VTIMEZONE",
                "TZID:Asia/Bangkok",
                "BEGIN:STANDARD",
                "DTSTART:19700101T000000",
                "TZOFFSETFROM:+0700",
                "TZOFFSETTO:+0700",
                "TZNAME:ICT",
                "END:STANDARD",
                "END:VTIMEZONE",
                "BEGIN:VEVENT",
                $"UID:meeting-{meetingId}@projecttracking.local",
                $"DTSTAMP:{DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}",
                $"DTSTART;TZID=Asia/Bangkok:{startAt.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)}",
                $"DTEND;TZID=Asia/Bangkok:{endAt.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)}",
                $"SUMMARY:{EscapeIcsText(summary)}",
                $"DESCRIPTION:{EscapeIcsText(detail.ToString())}",
                $"LOCATION:{EscapeIcsText(location)}",
                "STATUS:CONFIRMED",
                "TRANSP:OPAQUE",
                "X-MICROSOFT-CDO-BUSYSTATUS:BUSY",
                "END:VEVENT",
                "END:VCALENDAR"
            };

            var content = new StringBuilder();
            foreach (var line in lines)
            {
                foreach (var foldedLine in FoldIcsLine(line))
                    content.Append(foldedLine).Append("\r\n");
            }

            return new EmailAttachment(
                $"meeting-{meetingId}.ics",
                "text/calendar",
                Encoding.UTF8.GetBytes(content.ToString()));
        }

        private static DateTime GetBangkokToday()
        {
            try
            {
                var bangkokTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, bangkokTimeZone).Date;
            }
            catch (TimeZoneNotFoundException)
            {
                return DateTime.Today;
            }
            catch (InvalidTimeZoneException)
            {
                return DateTime.Today;
            }
        }

        private static string LineReminderKind(int daysBefore)
            => $"line_reminder_{daysBefore}d";

        private static string TelegramReminderKind(int daysBefore)
            => $"telegram_reminder_{daysBefore}d";

        private static string UpdatedNotificationKind(string prefix, MeetingDetails meeting)
        {
            var updatedAt = meeting.UpdatedAt == default ? DateTime.UtcNow : meeting.UpdatedAt;
            var raw = string.Join("|",
                meeting.Id,
                meeting.Title,
                meeting.Description,
                meeting.Location,
                meeting.ProjectName,
                meeting.StartAt.ToString("O", CultureInfo.InvariantCulture),
                meeting.EndAt.ToString("O", CultureInfo.InvariantCulture));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..8];

            return $"{prefix}_{updatedAt:yyyyMMddHHmmss}_{hash}";
        }

        private string? ToAbsoluteUrl(string? targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return null;

            if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var absoluteUri)
                && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
            {
                return targetUrl;
            }

            if (string.IsNullOrWhiteSpace(_appBaseUrl))
                return null;

            var baseUrl = _appBaseUrl.Contains("://", StringComparison.Ordinal)
                ? _appBaseUrl
                : $"https://{_appBaseUrl}";

            var candidate = targetUrl.StartsWith("/")
                ? $"{baseUrl}{targetUrl}"
                : $"{baseUrl}/{targetUrl}";

            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    ? candidate
                    : null;
        }

        private static string BuildDisplayName(string? empName, string? position)
        {
            var displayName = string.IsNullOrWhiteSpace(empName) ? "ผู้เข้าร่วม" : empName.Trim();
            if (!string.IsNullOrWhiteSpace(position))
                displayName += " (" + position.Trim() + ")";

            return displayName;
        }

        private static string FormatThaiDate(DateTime value)
        {
            var culture = new CultureInfo("th-TH");
            return value.ToString("ddddที่ d MMMM yyyy", culture);
        }

        private static string EscapeIcsText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n");
        }

        private static IEnumerable<string> FoldIcsLine(string line)
        {
            const int maxLength = 70;

            if (line.Length <= maxLength)
            {
                yield return line;
                yield break;
            }

            var offset = 0;
            var firstLine = true;
            while (offset < line.Length)
            {
                var remaining = line.Length - offset;
                var take = Math.Min(firstLine ? maxLength : maxLength - 1, remaining);
                var part = line.Substring(offset, take);
                yield return firstLine ? part : " " + part;

                offset += take;
                firstLine = false;
            }
        }

        private sealed class MeetingDetails
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public string? Description { get; set; }
            public string? Location { get; set; }
            public string? ProjectName { get; set; }
            public DateTime StartAt { get; set; }
            public DateTime EndAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class EmailRecipientRow
        {
            public int attendee_id { get; set; }
            public string? emp_name { get; set; }
            public string? position { get; set; }
            public string email { get; set; } = "";
        }

        private sealed class MeetingRecipientRow
        {
            public int attendee_id { get; set; }
            public int emp_id { get; set; }
            public string? emp_name { get; set; }
            public string? position { get; set; }
        }

        private sealed record EmailRecipient(int AttendeeId, string DisplayName, string Email);
        private sealed record MeetingRecipientInfo(int AttendeeId, int EmpId, string DisplayName);
    }

    public sealed record MeetingNotificationResult(int SentCount, int SkippedCount, int FailedCount, string Detail = "");
    public sealed record MeetingAttendeeNotificationStatus(bool EmailSent, bool LineSent, bool TelegramSent);
}
