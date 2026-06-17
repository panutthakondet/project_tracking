using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;

namespace ProjectTracking.Services
{
    public class MeetingNotificationService
    {
        private static readonly int[] LineReminderDays = { 3, 2, 1, 0 };
        private const string CreatedEmailKind = "created_email";

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly EmailService _emailService;
        private readonly LineMessagingService _lineMessagingService;
        private readonly ILogger<MeetingNotificationService> _logger;
        private readonly string _appBaseUrl;

        public MeetingNotificationService(
            IDbContextFactory<AppDbContext> dbFactory,
            EmailService emailService,
            LineMessagingService lineMessagingService,
            IConfiguration configuration,
            ILogger<MeetingNotificationService> logger)
        {
            _dbFactory = dbFactory;
            _emailService = emailService;
            _lineMessagingService = lineMessagingService;
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
                        EmailSent: x.Any(n => n.Kind == CreatedEmailKind),
                        LineSent: x.Any(n => n.Kind.StartsWith("line_reminder_"))));
        }

        public async Task<MeetingNotificationResult> SendCreatedEmailAsync(
            int meetingId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await EnsureNotificationTableAsync(db, cancellationToken);

            var meeting = await LoadMeetingAsync(db, meetingId, cancellationToken);
            if (meeting == null)
                return new MeetingNotificationResult(0, 0, 0);

            var recipients = await LoadEmailRecipientsAsync(db, meetingId, cancellationToken);
            if (recipients.Count == 0)
                return new MeetingNotificationResult(0, 0, 0);

            var attachment = await BuildCalendarAttachmentAsync(meeting.Id, cancellationToken);
            if (attachment == null)
                return new MeetingNotificationResult(0, 0, 0);

            var sent = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var recipient in recipients)
            {
                if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CreatedEmailKind, cancellationToken))
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

                    await InsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, CreatedEmailKind, cancellationToken);
                    sent++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(
                        ex,
                        "Failed to send meeting created email. MeetingId={MeetingId}, AttendeeId={AttendeeId}, Email={Email}",
                        meeting.Id,
                        recipient.AttendeeId,
                        recipient.Email);
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
                    var recipients = await LoadLineRecipientsAsync(db, meeting.Id, cancellationToken);
                    if (recipients.Count == 0)
                        continue;

                    var kind = LineReminderKind(daysBefore);
                    foreach (var recipient in recipients)
                    {
                        if (await HasNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, kind, cancellationToken))
                        {
                            skipped++;
                            continue;
                        }

                        try
                        {
                            var lineSendCount = await _lineMessagingService.SendNotificationToEmployeeAsync(
                                recipient.EmpId,
                                BuildLineTitle(daysBefore, meeting.Title),
                                BuildLineMessage(
                                    meeting,
                                    daysBefore,
                                    ToAbsoluteUrl($"/Meetings/Show/{meeting.Id}") ?? $"/Meetings/Show/{meeting.Id}",
                                    ToAbsoluteUrl($"/Meetings/Calendar/{meeting.Id}") ?? $"/Meetings/Calendar/{meeting.Id}"),
                                ToAbsoluteUrl($"/Meetings/Show/{meeting.Id}") ?? $"/Meetings/Show/{meeting.Id}",
                                cancellationToken);

                            if (lineSendCount > 0)
                            {
                                await InsertNotificationLogAsync(db, meeting.Id, recipient.AttendeeId, kind, cancellationToken);
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

        private static async Task EnsureNotificationTableAsync(AppDbContext db, CancellationToken cancellationToken)
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
        }

        private static async Task<bool> HasNotificationLogAsync(
            AppDbContext db,
            int meetingId,
            int attendeeId,
            string kind,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT 1
FROM meeting_email_notifications
WHERE meeting_id = @mid
  AND attendee_id = @aid
  AND kind = @kind
LIMIT 1;";

            return await db.Database
                .SqlQueryRaw<int>(
                    sql,
                    new MySqlConnector.MySqlParameter("@mid", meetingId),
                    new MySqlConnector.MySqlParameter("@aid", attendeeId),
                    new MySqlConnector.MySqlParameter("@kind", kind))
                .AnyAsync(cancellationToken);
        }

        private static async Task InsertNotificationLogAsync(
            AppDbContext db,
            int meetingId,
            int attendeeId,
            string kind,
            CancellationToken cancellationToken)
        {
            const string sql = @"
INSERT INTO meeting_email_notifications(meeting_id, attendee_id, kind, sent_at)
VALUES(@mid, @aid, @kind, NOW());";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                new object[]
                {
                    new MySqlConnector.MySqlParameter("@mid", meetingId),
                    new MySqlConnector.MySqlParameter("@aid", attendeeId),
                    new MySqlConnector.MySqlParameter("@kind", kind)
                },
                cancellationToken);
        }

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
  TIMESTAMP(m.meeting_date, m.end_time) AS EndAt
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
  TIMESTAMP(m.meeting_date, m.end_time) AS EndAt
FROM meetings m
LEFT JOIN project p ON p.project_id = m.project_id
LEFT JOIN cnt_m_coop c ON c.coop_id = p.coop_id
WHERE DATE(m.meeting_date) = @meetingDate;";

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
  ma.id AS attendee_id,
  e.emp_name,
  e.position,
  u.email
FROM meeting_attendees ma
JOIN employee e ON e.emp_id = ma.user_id
JOIN login_user u ON u.user_id = e.login_user_id
WHERE ma.meeting_id = @mid
  AND COALESCE(LOWER(ma.status), '') <> 'rejected'
  AND u.email IS NOT NULL
  AND u.email <> ''
ORDER BY ma.id;";

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

        private static async Task<List<LineRecipientInfo>> LoadLineRecipientsAsync(
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
  AND COALESCE(LOWER(ma.status), '') <> 'rejected'
ORDER BY ma.id;";

            var rows = await db.Database
                .SqlQueryRaw<LineRecipientRow>(
                    sql,
                    new MySqlConnector.MySqlParameter("@mid", meetingId))
                .ToListAsync(cancellationToken);

            return rows
                .Select(x => new LineRecipientInfo(
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

        private static string BuildLineTitle(int daysBefore, string meetingTitle)
        {
            var prefix = daysBefore == 0
                ? "แจ้งเตือนประชุมวันนี้"
                : $"แจ้งเตือนประชุมล่วงหน้า {daysBefore} วัน";

            return $"{prefix}: {meetingTitle}";
        }

        private static string BuildLineMessage(
            MeetingDetails meeting,
            int daysBefore,
            string? detailUrl,
            string? calendarUrl)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(meeting.ProjectName))
                sb.AppendLine($"โครงการ: {meeting.ProjectName}");

            sb.AppendLine($"หัวข้อ: {meeting.Title}");
            sb.AppendLine($"วันที่: {FormatThaiDate(meeting.StartAt)}");
            sb.AppendLine($"เวลา: {meeting.StartAt:HH:mm} - {meeting.EndAt:HH:mm}");

            if (!string.IsNullOrWhiteSpace(meeting.Location))
                sb.AppendLine($"สถานที่: {meeting.Location}");

            if (!string.IsNullOrWhiteSpace(meeting.Description))
                sb.AppendLine($"รายละเอียด: {meeting.Description}");

            if (!string.IsNullOrWhiteSpace(detailUrl))
                sb.AppendLine($"ลิงก์รายละเอียด: {detailUrl}");

            if (!string.IsNullOrWhiteSpace(calendarUrl))
                sb.AppendLine($"ไฟล์ปฏิทิน (.ics): {calendarUrl}");

            sb.Append(daysBefore == 0
                ? "การประชุมนี้มีกำหนดในวันนี้"
                : $"การประชุมนี้จะเริ่มในอีก {daysBefore} วัน");

            return sb.ToString();
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

        private string? ToAbsoluteUrl(string? targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return null;

            if (Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
                return targetUrl;

            if (string.IsNullOrWhiteSpace(_appBaseUrl))
                return null;

            return targetUrl.StartsWith("/")
                ? $"{_appBaseUrl}{targetUrl}"
                : $"{_appBaseUrl}/{targetUrl}";
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
        }

        private sealed class EmailRecipientRow
        {
            public int attendee_id { get; set; }
            public string? emp_name { get; set; }
            public string? position { get; set; }
            public string email { get; set; } = "";
        }

        private sealed class LineRecipientRow
        {
            public int attendee_id { get; set; }
            public int emp_id { get; set; }
            public string? emp_name { get; set; }
            public string? position { get; set; }
        }

        private sealed record EmailRecipient(int AttendeeId, string DisplayName, string Email);
        private sealed record LineRecipientInfo(int AttendeeId, int EmpId, string DisplayName);
    }

    public sealed record MeetingNotificationResult(int SentCount, int SkippedCount, int FailedCount);
    public sealed record MeetingAttendeeNotificationStatus(bool EmailSent, bool LineSent);
}
