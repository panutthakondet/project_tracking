using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectTracking.Data;

namespace ProjectTracking.Services
{
    // Sends reminder emails for meetings starting 1 day in advance.
    // Recipients: everyone in meeting_attendees for that meeting_id (except rejected) who has email in login_user.
    // Prevents duplicate sends using table: meeting_email_notifications (raw SQL, no EF model needed).
    public class MeetingReminderBackgroundService : BackgroundService
    {
        private const int REMIND_DAYS = 1;
        // Safety window to avoid missing the exact minute (service ticks every 1 minute)
        private const int WINDOW_MINUTES = 5;
        private const string REMIND_KIND = "reminder_1d";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MeetingReminderBackgroundService> _logger;

        public MeetingReminderBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<MeetingReminderBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        private class MeetingRow
        {
            public int id { get; set; }
            public string title { get; set; } = "";
            public string? description { get; set; }
            public string? location { get; set; }
            public string? project_name { get; set; }
            public DateTime start_at { get; set; }
        }

        private class RecipientRow
        {
            public int attendee_id { get; set; }
            public string? emp_name { get; set; }
            public string? position { get; set; }
            public string email { get; set; } = "";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🔔 MeetingReminderBackgroundService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Meeting reminder error");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var email = scope.ServiceProvider.GetRequiredService<EmailService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 1) Ensure log table exists (no migration required)
            const string createLogSql = @"
CREATE TABLE IF NOT EXISTS meeting_email_notifications (
  id INT AUTO_INCREMENT PRIMARY KEY,
  meeting_id INT NOT NULL,
  attendee_id INT NOT NULL,
  kind VARCHAR(50) NOT NULL,
  sent_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_meeting_attendee_kind (meeting_id, attendee_id, kind),
  KEY idx_meeting (meeting_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;";

            await db.Database.ExecuteSqlRawAsync(createLogSql, ct);

            // 2) Find meetings scheduled for tomorrow (full-day match, no minute window)
            const string meetingsSql = @"
SELECT
  m.id,
  m.title,
  m.description,
  m.location,
  p.project_name,
  TIMESTAMP(m.meeting_date, m.start_time) AS start_at
FROM meetings m
LEFT JOIN project p ON p.project_id = m.project_id
WHERE DATE(m.meeting_date) = DATE_ADD(CURDATE(), INTERVAL 1 DAY);";

            var meetings = new List<(int Id, string Title, string? Description, string? Location, string? ProjectName, DateTime StartAt)>();

            var meetingRows = await db.Database
                .SqlQueryRaw<MeetingRow>(meetingsSql)
                .ToListAsync(ct);

            foreach (var row in meetingRows)
            {
                meetings.Add((row.id, row.title, row.description, row.location, row.project_name, row.start_at));
            }

            if (meetings.Count == 0)
            {
                _logger.LogInformation("🔔 No meetings scheduled for tomorrow (1-day reminder)");
                return;
            }

            foreach (var m in meetings)
            {
                // 3) Get recipients using RAW SQL join on DB columns (no dependency on Employee.LoginUserId property)
                const string recipientsSql = @"
SELECT
  ma.id AS attendee_id,
  e.emp_name,
  e.position,
  u.email
FROM meeting_attendees ma
JOIN employee e ON e.emp_id = ma.user_id
JOIN login_user u ON u.user_id = e.login_user_id
WHERE ma.meeting_id = @mid
  AND u.email IS NOT NULL
  AND u.email <> ''";

                var recipients = new List<(int AttendeeId, string DisplayName, string Email)>();

                var recipientRows = await db.Database
                    .SqlQueryRaw<RecipientRow>(recipientsSql, new MySqlConnector.MySqlParameter("@mid", m.Id))
                    .ToListAsync(ct);

                foreach (var rr in recipientRows)
                {
                    var displayName = rr.emp_name ?? "";
                    if (!string.IsNullOrWhiteSpace(rr.position))
                    {
                        displayName += " (" + rr.position + ")";
                    }
                    recipients.Add((rr.attendee_id, displayName, rr.email));
                }

                if (recipients.Count == 0)
                {
                    _logger.LogInformation("🔔 Meeting {MeetingId} has no recipients", m.Id);
                    continue;
                }

                foreach (var r in recipients)
                {
                    // 4) Skip if already sent
                    const string existsSql = @"SELECT 1 FROM meeting_email_notifications WHERE meeting_id=@mid AND attendee_id=@aid AND kind=@kind LIMIT 1";

                    var exists = await db.Database
                        .SqlQueryRaw<int>(existsSql,
                            new MySqlConnector.MySqlParameter("@mid", m.Id),
                            new MySqlConnector.MySqlParameter("@aid", r.AttendeeId),
                            new MySqlConnector.MySqlParameter("@kind", REMIND_KIND))
                        .AnyAsync(ct);

                    if (exists) continue;

                    // 5) Send email
                    var subject = $"แจ้งเตือนการประชุม: {m.Title}";
                    var sb = new StringBuilder();
                    sb.Append($"สวัสดี {System.Net.WebUtility.HtmlEncode(r.DisplayName)}<br/>");
                    if (!string.IsNullOrWhiteSpace(m.ProjectName))
                    {
                        sb.Append($"โครงการ: <b>{System.Net.WebUtility.HtmlEncode(m.ProjectName)}</b><br/>");
                    }
                    sb.Append($"การประชุม <b>{System.Net.WebUtility.HtmlEncode(m.Title)}</b> จะเริ่มวันที่ <b>{m.StartAt:dd/MM/yyyy}</b> เวลา <b>{m.StartAt:HH:mm}</b> (แจ้งล่วงหน้า 1 วัน)<br/>");
                    if (!string.IsNullOrWhiteSpace(m.Location)) sb.Append($"สถานที่: {System.Net.WebUtility.HtmlEncode(m.Location)}<br/>");
                    if (!string.IsNullOrWhiteSpace(m.Description)) sb.Append($"รายละเอียด: {System.Net.WebUtility.HtmlEncode(m.Description)}<br/>");
                    sb.Append("<br/><small>ProjectTracking</small>");

                    await email.SendAsync(r.Email, subject, sb.ToString());

                    // 6) Insert log after successful send
                    const string insertSql = @"INSERT INTO meeting_email_notifications(meeting_id, attendee_id, kind, sent_at) VALUES(@mid, @aid, @kind, NOW())";

                    await db.Database.ExecuteSqlRawAsync(
                        insertSql,
                        new object[]
                        {
                            new MySqlConnector.MySqlParameter("@mid", m.Id),
                            new MySqlConnector.MySqlParameter("@aid", r.AttendeeId),
                            new MySqlConnector.MySqlParameter("@kind", REMIND_KIND)
                        },
                        ct);

                    _logger.LogInformation("📧 Sent {Kind} meeting={MeetingId} attendee={AttendeeId} to={Email}", REMIND_KIND, m.Id, r.AttendeeId, r.Email);
                }
            }

            _logger.LogInformation("🔔 Meeting reminder check complete");
        }
    }
}