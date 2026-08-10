using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;

namespace ProjectTracking.Services;

public sealed class FieldServiceNotificationService
{
    private static readonly int[] ReminderDays = { 3, 2, 1, 0 };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EmailService _emailService;
    private readonly LineMessagingService _lineMessagingService;
    private readonly LineNotificationSettingsService _lineSettings;
    private readonly TelegramMessagingService _telegramMessagingService;
    private readonly TelegramNotificationSettingsService _telegramSettings;
    private readonly ILogger<FieldServiceNotificationService> _logger;
    private readonly string _appBaseUrl;

    public FieldServiceNotificationService(
        IDbContextFactory<AppDbContext> dbFactory,
        EmailService emailService,
        LineMessagingService lineMessagingService,
        LineNotificationSettingsService lineSettings,
        TelegramMessagingService telegramMessagingService,
        TelegramNotificationSettingsService telegramSettings,
        IConfiguration configuration,
        ILogger<FieldServiceNotificationService> logger)
    {
        _dbFactory = dbFactory;
        _emailService = emailService;
        _lineMessagingService = lineMessagingService;
        _lineSettings = lineSettings;
        _telegramMessagingService = telegramMessagingService;
        _telegramSettings = telegramSettings;
        _logger = logger;
        _appBaseUrl = (Environment.GetEnvironmentVariable("APP_BASE_URL")
            ?? configuration["APP_BASE_URL"]
            ?? "").TrimEnd('/');
    }

    public Task<FieldServiceNotificationResult> SendCreatedNotificationsAsync(
        int visitId,
        CancellationToken cancellationToken = default)
        => SendChangeNotificationsAsync(visitId, FieldServiceNotificationKind.Created, cancellationToken);

    public Task<FieldServiceNotificationResult> SendUpdatedNotificationsAsync(
        int visitId,
        CancellationToken cancellationToken = default)
        => SendChangeNotificationsAsync(visitId, FieldServiceNotificationKind.Updated, cancellationToken);

    public Task<FieldServiceNotificationResult> SendCancelledNotificationsAsync(
        int visitId,
        CancellationToken cancellationToken = default)
        => SendChangeNotificationsAsync(visitId, FieldServiceNotificationKind.Cancelled, cancellationToken);

    public Task<FieldServiceNotificationResult> SendLineRemindersAsync(
        CancellationToken cancellationToken = default)
        => SendRemindersAsync("LINE", cancellationToken);

    public Task<FieldServiceNotificationResult> SendTelegramRemindersAsync(
        CancellationToken cancellationToken = default)
        => SendRemindersAsync("TELEGRAM", cancellationToken);

    private async Task<FieldServiceNotificationResult> SendChangeNotificationsAsync(
        int visitId,
        FieldServiceNotificationKind kind,
        CancellationToken cancellationToken)
    {
        var visit = await LoadVisitAsync(visitId, cancellationToken);
        if (visit == null)
            return new FieldServiceNotificationResult(0, 0, 1, "ไม่พบงานเข้าไซต์");

        var title = kind switch
        {
            FieldServiceNotificationKind.Created => $"มอบหมายงานเข้าไซต์: {visit.Title}",
            FieldServiceNotificationKind.Cancelled => $"ยกเลิกงานเข้าไซต์: {visit.Title}",
            _ => $"อัปเดตงานเข้าไซต์: {visit.Title}"
        };
        var message = BuildMessage(visit, kind);
        var targetUrl = $"/FieldService/Show/{visit.Id}";
        var absoluteUrl = ToAbsoluteUrl(targetUrl) ?? targetUrl;
        var emailSubject = title;
        var emailBody = BuildEmailBody(visit, kind, absoluteUrl);
        var calendarAttachment = BuildCalendarAttachment(visit, kind == FieldServiceNotificationKind.Cancelled);
        var sent = 0;
        var skipped = 0;
        var failed = 0;

        var notifiedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipient in visit.Recipients)
        {
            if (string.IsNullOrWhiteSpace(recipient.Email) || !notifiedEmails.Add(recipient.Email.Trim()))
            {
                skipped++;
                continue;
            }

            try
            {
                await _emailService.SendAsync(
                    recipient.Email,
                    emailSubject,
                    emailBody.Replace("{{DISPLAY_NAME}}", WebUtility.HtmlEncode(recipient.DisplayName)),
                    attachments: new[] { calendarAttachment });
                sent++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "Failed to send Field Service email. VisitId={VisitId}, EmpId={EmpId}, Kind={Kind}",
                    visit.Id, recipient.EmpId, kind);
            }
        }

        var lineFeature = kind switch
        {
            FieldServiceNotificationKind.Created => LineNotificationFeatures.FieldServiceCreate,
            FieldServiceNotificationKind.Cancelled => LineNotificationFeatures.FieldServiceCancel,
            _ => LineNotificationFeatures.FieldServiceUpdate
        };
        if (_lineMessagingService.IsConfigured && await _lineSettings.IsEnabledAsync(lineFeature, cancellationToken))
        {
            foreach (var recipient in visit.Recipients)
            {
                try
                {
                    var count = await _lineMessagingService.SendNotificationToEmployeeAsync(
                        recipient.EmpId, title, message, targetUrl, cancellationToken);
                    if (count > 0) sent += count; else skipped++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "Failed to send Field Service LINE. VisitId={VisitId}, EmpId={EmpId}, Kind={Kind}",
                        visit.Id, recipient.EmpId, kind);
                }
            }
        }

        var telegramFeature = kind switch
        {
            FieldServiceNotificationKind.Created => TelegramNotificationFeatures.FieldServiceCreate,
            FieldServiceNotificationKind.Cancelled => TelegramNotificationFeatures.FieldServiceCancel,
            _ => TelegramNotificationFeatures.FieldServiceUpdate
        };
        if (_telegramMessagingService.IsConfigured && await _telegramSettings.IsEnabledAsync(telegramFeature, cancellationToken))
        {
            foreach (var recipient in visit.Recipients)
            {
                try
                {
                    var count = await _telegramMessagingService.SendNotificationToEmployeeAsync(
                        recipient.EmpId, title, message, targetUrl, cancellationToken);
                    if (count > 0) sent += count; else skipped++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "Failed to send Field Service Telegram. VisitId={VisitId}, EmpId={EmpId}, Kind={Kind}",
                        visit.Id, recipient.EmpId, kind);
                }
            }
        }

        return new FieldServiceNotificationResult(sent, skipped, failed);
    }

    private async Task<FieldServiceNotificationResult> SendRemindersAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        var isLine = channel == "LINE";
        var configured = isLine ? _lineMessagingService.IsConfigured : _telegramMessagingService.IsConfigured;
        if (!configured)
            return new FieldServiceNotificationResult(0, 0, 0);

        var featureEnabled = isLine
            ? await _lineSettings.IsEnabledAsync(LineNotificationFeatures.FieldServiceReminder, cancellationToken)
              && await _lineSettings.IsEnabledAsync(LineNotificationFeatures.FieldServiceAuto, cancellationToken)
            : await _telegramSettings.IsEnabledAsync(TelegramNotificationFeatures.FieldServiceReminder, cancellationToken)
              && await _telegramSettings.IsEnabledAsync(TelegramNotificationFeatures.FieldServiceAuto, cancellationToken);
        if (!featureEnabled)
            return new FieldServiceNotificationResult(0, 0, 0);

        var sent = 0;
        var skipped = 0;
        var failed = 0;
        var today = BangkokToday();

        foreach (var daysBefore in ReminderDays)
        {
            var targetDate = today.AddDays(daysBefore);
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var visitIds = await db.FieldServiceVisits
                .AsNoTracking()
                .Where(x => x.VisitDate == targetDate && x.Status != "CANCELLED")
                .Select(x => x.VisitId)
                .ToListAsync(cancellationToken);

            foreach (var visitId in visitIds)
            {
                var visit = await LoadVisitAsync(visitId, cancellationToken);
                if (visit == null) continue;
                var title = daysBefore == 0
                    ? $"แจ้งเตือนงานเข้าไซต์วันนี้: {visit.Title}"
                    : $"แจ้งเตือนงานเข้าไซต์ล่วงหน้า {daysBefore} วัน: {visit.Title}";
                var message = BuildReminderMessage(visit, daysBefore);
                var targetUrl = $"/FieldService/Show/{visit.Id}";

                foreach (var recipient in visit.Recipients)
                {
                    try
                    {
                        var count = isLine
                            ? await _lineMessagingService.SendNotificationToEmployeeAsync(
                                recipient.EmpId, title, message, targetUrl, cancellationToken)
                            : await _telegramMessagingService.SendNotificationToEmployeeAsync(
                                recipient.EmpId, title, message, targetUrl, cancellationToken);
                        if (count > 0) sent += count; else skipped++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(ex,
                            "Failed to send Field Service reminder. Channel={Channel}, VisitId={VisitId}, EmpId={EmpId}, DaysBefore={DaysBefore}",
                            channel, visit.Id, recipient.EmpId, daysBefore);
                    }
                }
            }
        }

        return new FieldServiceNotificationResult(sent, skipped, failed);
    }

    private async Task<FieldServiceVisitDetails?> LoadVisitAsync(int visitId, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.FieldServiceVisits
            .AsNoTracking()
            .Include(x => x.Coop)
            .Include(x => x.Assignees)
                .ThenInclude(x => x.Employee)
                    .ThenInclude(x => x!.LoginUser)
            .FirstOrDefaultAsync(x => x.VisitId == visitId, cancellationToken);
        if (row == null) return null;

        var employees = row.Assignees
            .Where(x => x.Employee != null)
            .Select(x => x.Employee!)
            .GroupBy(x => x.EmpId)
            .Select(x => x.First())
            .ToList();
        var employeeIds = employees.Select(x => x.EmpId).ToList();
        var loginUserIds = employees.Where(x => x.LoginUserId.HasValue).Select(x => x.LoginUserId!.Value).ToList();
        var fallbackEmails = await db.LoginUsers
            .AsNoTracking()
            .Where(x => (x.EmpId.HasValue && employeeIds.Contains(x.EmpId.Value)) || loginUserIds.Contains(x.UserId))
            .Where(x => x.Email != null && x.Email != "")
            .OrderBy(x => x.UserId)
            .ToListAsync(cancellationToken);

        var recipients = employees.Select(employee =>
        {
            var email = employee.LoginUser?.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                email = fallbackEmails.FirstOrDefault(x => x.UserId == employee.LoginUserId)?.Email
                    ?? fallbackEmails.FirstOrDefault(x => x.EmpId == employee.EmpId)?.Email;
            }
            return new FieldServiceRecipient(
                employee.EmpId,
                string.IsNullOrWhiteSpace(employee.Position)
                    ? employee.EmpName
                    : $"{employee.EmpName} ({employee.Position})",
                email);
        }).ToList();

        return new FieldServiceVisitDetails
        {
            Id = row.VisitId,
            Title = row.Title,
            ServiceType = row.ServiceType,
            CoopName = row.Coop?.CoopName ?? "ไม่ระบุสหกรณ์",
            VisitDate = row.VisitDate.Date,
            EndVisitDate = (row.EndVisitDate ?? row.VisitDate).Date,
            StartTime = row.StartTime,
            EndTime = row.EndTime,
            Description = row.Description,
            Status = row.Status,
            Recipients = recipients
        };
    }

    private static string BuildMessage(FieldServiceVisitDetails visit, FieldServiceNotificationKind kind)
    {
        var heading = kind switch
        {
            FieldServiceNotificationKind.Created => "คุณได้รับมอบหมายงานเข้าไซต์",
            FieldServiceNotificationKind.Cancelled => "งานเข้าไซต์นี้ถูกยกเลิกแล้ว",
            _ => "มีการอัปเดตรายละเอียดงานเข้าไซต์"
        };
        return $"{heading}\n{BuildPlainDetails(visit)}";
    }

    private static string BuildReminderMessage(FieldServiceVisitDetails visit, int daysBefore)
    {
        var footer = daysBefore == 0
            ? "งานนี้มีกำหนดเริ่มในวันนี้"
            : $"งานนี้มีกำหนดเริ่มในอีก {daysBefore} วัน";
        return $"{BuildPlainDetails(visit)}\n{footer}";
    }

    private static string BuildPlainDetails(FieldServiceVisitDetails visit)
    {
        var lines = new List<string>
        {
            $"สหกรณ์: {visit.CoopName}",
            $"ประเภทบริการ: {visit.ServiceType}",
            $"งาน: {visit.Title}",
            $"วันที่: {FormatThaiDateRange(visit)}"
        };
        var time = FormatTimeRange(visit);
        if (!string.IsNullOrWhiteSpace(time)) lines.Add($"เวลา: {time}");
        if (!string.IsNullOrWhiteSpace(visit.Description)) lines.Add($"รายละเอียด: {visit.Description}");
        return string.Join("\n", lines);
    }

    private static string BuildEmailBody(
        FieldServiceVisitDetails visit,
        FieldServiceNotificationKind kind,
        string detailUrl)
    {
        var heading = kind switch
        {
            FieldServiceNotificationKind.Created => "คุณได้รับมอบหมายงานเข้าไซต์",
            FieldServiceNotificationKind.Cancelled => "งานเข้าไซต์นี้ถูกยกเลิกแล้ว",
            _ => "มีการอัปเดตรายละเอียดงานเข้าไซต์"
        };
        var encodedUrl = WebUtility.HtmlEncode(detailUrl);
        var sb = new StringBuilder();
        sb.Append("สวัสดี {{DISPLAY_NAME}}<br/>");
        sb.Append($"<b>{WebUtility.HtmlEncode(heading)}</b><br/>");
        sb.Append($"สหกรณ์: <b>{WebUtility.HtmlEncode(visit.CoopName)}</b><br/>");
        sb.Append($"ประเภทบริการ: {WebUtility.HtmlEncode(visit.ServiceType)}<br/>");
        sb.Append($"งาน: <b>{WebUtility.HtmlEncode(visit.Title)}</b><br/>");
        sb.Append($"วันที่: <b>{WebUtility.HtmlEncode(FormatThaiDateRange(visit))}</b><br/>");
        var time = FormatTimeRange(visit);
        if (!string.IsNullOrWhiteSpace(time)) sb.Append($"เวลา: <b>{time}</b><br/>");
        if (!string.IsNullOrWhiteSpace(visit.Description))
            sb.Append($"รายละเอียด: {WebUtility.HtmlEncode(visit.Description)}<br/>");
        sb.Append("<br/>");
        sb.Append($"<a href=\"{encodedUrl}\">เปิดรายละเอียดงานเข้าไซต์</a><br/>");
        sb.Append("<br/><small>ProjectTracking</small>");
        return sb.ToString();
    }

    private static EmailAttachment BuildCalendarAttachment(FieldServiceVisitDetails visit, bool cancelled)
    {
        var now = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var uid = $"field-service-{visit.Id}@projecttracking";
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//ProjectTracking//Field Service//TH");
        sb.AppendLine("METHOD:PUBLISH");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{uid}");
        sb.AppendLine($"DTSTAMP:{now}");
        if (visit.StartTime.HasValue)
        {
            sb.AppendLine($"DTSTART:{visit.VisitDate.Add(visit.StartTime.Value):yyyyMMdd'T'HHmmss}");
            var endAt = visit.EndVisitDate.Add(visit.EndTime ?? visit.StartTime.Value.Add(TimeSpan.FromHours(1)));
            sb.AppendLine($"DTEND:{endAt:yyyyMMdd'T'HHmmss}");
        }
        else
        {
            sb.AppendLine($"DTSTART;VALUE=DATE:{visit.VisitDate:yyyyMMdd}");
            sb.AppendLine($"DTEND;VALUE=DATE:{visit.EndVisitDate.AddDays(1):yyyyMMdd}");
        }
        sb.AppendLine($"SUMMARY:{EscapeIcs($"{visit.ServiceType} - {visit.Title}")}");
        sb.AppendLine($"LOCATION:{EscapeIcs(visit.CoopName)}");
        sb.AppendLine($"DESCRIPTION:{EscapeIcs(visit.Description)}");
        if (cancelled) sb.AppendLine("STATUS:CANCELLED");
        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");
        return new EmailAttachment($"field-service-{visit.Id}.ics", "text/calendar", Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private string? ToAbsoluteUrl(string targetUrl)
    {
        if (Uri.TryCreate(targetUrl, UriKind.Absolute, out _)) return targetUrl;
        if (string.IsNullOrWhiteSpace(_appBaseUrl)) return null;
        var baseUrl = _appBaseUrl.Contains("://", StringComparison.Ordinal) ? _appBaseUrl : $"https://{_appBaseUrl}";
        return targetUrl.StartsWith('/') ? $"{baseUrl}{targetUrl}" : $"{baseUrl}/{targetUrl}";
    }

    private static string FormatThaiDateRange(FieldServiceVisitDetails visit)
    {
        var culture = new CultureInfo("th-TH");
        var start = visit.VisitDate.ToString("d MMMM yyyy", culture);
        var end = visit.EndVisitDate.ToString("d MMMM yyyy", culture);
        return visit.VisitDate == visit.EndVisitDate ? start : $"{start} ถึง {end}";
    }

    private static string FormatTimeRange(FieldServiceVisitDetails visit)
        => visit.StartTime.HasValue
            ? $"{visit.StartTime:hh\\:mm} - {(visit.EndTime.HasValue ? visit.EndTime.Value.ToString("hh\\:mm") : "-")}"
            : "";

    private static string EscapeIcs(string? value)
        => (value ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
            .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    private static DateTime BangkokToday()
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        }
        catch
        {
            return DateTime.Today;
        }
    }

    private enum FieldServiceNotificationKind { Created, Updated, Cancelled }

    private sealed class FieldServiceVisitDetails
    {
        public int Id { get; init; }
        public string Title { get; init; } = "";
        public string ServiceType { get; init; } = "";
        public string CoopName { get; init; } = "";
        public DateTime VisitDate { get; init; }
        public DateTime EndVisitDate { get; init; }
        public TimeSpan? StartTime { get; init; }
        public TimeSpan? EndTime { get; init; }
        public string? Description { get; init; }
        public string Status { get; init; } = "";
        public List<FieldServiceRecipient> Recipients { get; init; } = new();
    }

    private sealed record FieldServiceRecipient(int EmpId, string DisplayName, string? Email);
}

public sealed record FieldServiceNotificationResult(int SentCount, int SkippedCount, int FailedCount, string Detail = "");
