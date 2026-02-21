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

            // 2) Find meetings starting ~1 day from now
            const string meetingsSql = @"
SELECT
  id,
  title,
  description,
  location,
  TIMESTAMP(meeting_date, start_time) AS start_at
FROM meetings
WHERE TIMESTAMP(meeting_date, start_time) >= DATE_ADD(NOW(), INTERVAL 1 DAY)
  AND TIMESTAMP(meeting_date, start_time) <= DATE_ADD(DATE_ADD(NOW(), INTERVAL 1 DAY), INTERVAL @win MINUTE);";

            var meetings = new List<(int Id, string Title, string? Description, string? Location, DateTime StartAt)>();

            await using (var conn = db.Database.GetDbConnection())
            {
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync(ct);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = meetingsSql;

                var pWin = cmd.CreateParameter();
                pWin.ParameterName = "@win";
                pWin.Value = WINDOW_MINUTES;
                cmd.Parameters.Add(pWin);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetInt32(0);
                    var title = reader.GetString(1);
                    var desc = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var loc = reader.IsDBNull(3) ? null : reader.GetString(3);
                    var startAt = reader.GetDateTime(4);
                    meetings.Add((id, title, desc, loc, startAt));
                }
            }

            if (meetings.Count == 0)
            {
                _logger.LogInformation("🔔 No meetings needing 1-day reminder (window {WindowMinutes} min)", WINDOW_MINUTES);
                return;
            }

            foreach (var m in meetings)
            {
                // 3) Get recipients using RAW SQL join on DB columns (no dependency on Employee.LoginUserId property)
                const string recipientsSql = @"
SELECT
  ma.id AS attendee_id,
  u.username,
  u.email
FROM meeting_attendees ma
JOIN employee e ON e.emp_id = ma.user_id
JOIN login_user u ON u.user_id = e.login_user_id
WHERE ma.meeting_id = @mid
  AND u.email IS NOT NULL
  AND u.email <> '';";

                var recipients = new List<(int AttendeeId, string Username, string Email)>();

                await using (var conn = db.Database.GetDbConnection())
                {
                    if (conn.State != ConnectionState.Open)
                        await conn.OpenAsync(ct);

                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = recipientsSql;

                    var pMid = cmd.CreateParameter();
                    pMid.ParameterName = "@mid";
                    pMid.Value = m.Id;
                    cmd.Parameters.Add(pMid);

                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        var attendeeId = reader.GetInt32(0);
                        var username = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        var emailAddr = reader.GetString(2);
                        recipients.Add((attendeeId, username, emailAddr));
                    }
                }

                if (recipients.Count == 0)
                {
                    _logger.LogInformation("🔔 Meeting {MeetingId} has no recipients", m.Id);
                    continue;
                }

                foreach (var r in recipients)
                {
                    // 4) Skip if already sent
                    const string existsSql = @"SELECT 1 FROM meeting_email_notifications WHERE meeting_id=@mid AND attendee_id=@aid AND kind=@kind LIMIT 1;";
                    var alreadySent = false;

                    await using (var conn = db.Database.GetDbConnection())
                    {
                        if (conn.State != ConnectionState.Open)
                            await conn.OpenAsync(ct);

                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = existsSql;

                        var pMid = cmd.CreateParameter(); pMid.ParameterName = "@mid"; pMid.Value = m.Id; cmd.Parameters.Add(pMid);
                        var pAid = cmd.CreateParameter(); pAid.ParameterName = "@aid"; pAid.Value = r.AttendeeId; cmd.Parameters.Add(pAid);
                        var pKind = cmd.CreateParameter(); pKind.ParameterName = "@kind"; pKind.Value = REMIND_KIND; cmd.Parameters.Add(pKind);

                        var scalar = await cmd.ExecuteScalarAsync(ct);
                        alreadySent = scalar != null && scalar != DBNull.Value;
                    }

                    if (alreadySent) continue;

                    // 5) Send email
                    var subject = $"แจ้งเตือนการประชุม: {m.Title}";
                    var sb = new StringBuilder();
                    sb.Append($"สวัสดี {System.Net.WebUtility.HtmlEncode(r.Username)}<br/>");
                    sb.Append($"การประชุม <b>{System.Net.WebUtility.HtmlEncode(m.Title)}</b> จะเริ่มวันที่ <b>{m.StartAt:dd/MM/yyyy}</b> เวลา <b>{m.StartAt:HH:mm}</b> (แจ้งล่วงหน้า 1 วัน)<br/>");
                    if (!string.IsNullOrWhiteSpace(m.Location)) sb.Append($"สถานที่: {System.Net.WebUtility.HtmlEncode(m.Location)}<br/>");
                    if (!string.IsNullOrWhiteSpace(m.Description)) sb.Append($"รายละเอียด: {System.Net.WebUtility.HtmlEncode(m.Description)}<br/>");
                    sb.Append("<br/><small>ProjectTracking</small>");

                    await email.SendAsync(r.Email, subject, sb.ToString());

                    // 6) Insert log after successful send
                    const string insertSql = @"INSERT INTO meeting_email_notifications(meeting_id, attendee_id, kind, sent_at) VALUES(@mid, @aid, @kind, NOW());";
                    await using (var conn = db.Database.GetDbConnection())
                    {
                        if (conn.State != ConnectionState.Open)
                            await conn.OpenAsync(ct);

                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = insertSql;

                        var pMid = cmd.CreateParameter(); pMid.ParameterName = "@mid"; pMid.Value = m.Id; cmd.Parameters.Add(pMid);
                        var pAid = cmd.CreateParameter(); pAid.ParameterName = "@aid"; pAid.Value = r.AttendeeId; cmd.Parameters.Add(pAid);
                        var pKind = cmd.CreateParameter(); pKind.ParameterName = "@kind"; pKind.Value = REMIND_KIND; cmd.Parameters.Add(pKind);

                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    _logger.LogInformation("📧 Sent {Kind} meeting={MeetingId} attendee={AttendeeId} to={Email}", REMIND_KIND, m.Id, r.AttendeeId, r.Email);
                }
            }

            _logger.LogInformation("🔔 Meeting reminder check complete");
        }
    }
}