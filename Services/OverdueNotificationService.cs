using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using System.Globalization;

namespace ProjectTracking.Services
{
    public class OverdueNotificationService
    {
        private static readonly string[] ManagedSourceTypes =
        {
            "ASSIGN_DUE",
            "ISSUE_DUE",
            "SUPPORT_DUE",
            "FOLLOWUP_DUE"
        };

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly LineMessagingService _lineMessagingService;
        private readonly LineNotificationSettingsService _lineNotificationSettings;
        private readonly TelegramMessagingService _telegramMessagingService;
        private readonly TelegramNotificationSettingsService _telegramNotificationSettings;
        private readonly ILogger<OverdueNotificationService> _logger;
        private readonly int _defaultRiskDays;

        public OverdueNotificationService(
            IDbContextFactory<AppDbContext> dbFactory,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            IConfiguration configuration,
            ILogger<OverdueNotificationService> logger)
        {
            _dbFactory = dbFactory;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _logger = logger;
            _defaultRiskDays = Math.Clamp(configuration.GetValue<int?>("OVERDUE_NOTIFICATION_RISK_DAYS") ?? 7, 0, 30);
        }

        public async Task SyncAsync(CancellationToken cancellationToken = default)
            => await SyncAsync(forceTelegramSend: false, sendChatNotifications: false, cancellationToken);

        public async Task SyncAndSendTelegramAsync(CancellationToken cancellationToken = default)
            => await SyncAsync(forceTelegramSend: true, sendChatNotifications: true, cancellationToken);

        private async Task SyncAsync(bool forceTelegramSend, bool sendChatNotifications, CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var now = GetBangkokNow();
            var today = now.Date;
            var riskDays = await GetRiskDaysAsync(db, cancellationToken);
            var riskUntil = today.AddDays(riskDays);

            var userEmpLinks = await db.LoginUsers
                .AsNoTracking()
                .Where(x => x.EmpId.HasValue && x.Status == "ACTIVE")
                .Select(x => new { x.UserId, EmpId = x.EmpId!.Value })
                .ToListAsync(cancellationToken);

            var loginUserIdByEmpId = userEmpLinks
                .GroupBy(x => x.EmpId)
                .ToDictionary(x => x.Key, x => (int?)x.OrderBy(u => u.UserId).First().UserId);

            var employeeRows = await db.Employees
                .AsNoTracking()
                .Select(x => new EmployeeRecipient(
                    x.EmpId,
                    x.EmpName ?? $"Employee #{x.EmpId}",
                    x.LoginUserId))
                .ToListAsync(cancellationToken);

            var employees = employeeRows.ToDictionary(
                x => x.EmpId,
                x => new EmployeeRecipient(
                    x.EmpId,
                    x.EmpName,
                    x.LoginUserId ?? loginUserIdByEmpId.GetValueOrDefault(x.EmpId)));

            var existingNotifications = await db.UserNotifications
                .Where(x => ManagedSourceTypes.Contains(x.SourceType))
                .ToListAsync(cancellationToken);

            ResolveDuplicateNotifications(existingNotifications, now);

            var existingByKey = existingNotifications
                .Where(x => x.RecipientEmpId.HasValue)
                .GroupBy(x => Key(x.SourceType, x.SourceId, x.RecipientEmpId!.Value))
                .ToDictionary(x => x.Key, x => SelectPrimaryNotification(x));

            var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var telegramQueue = new List<TelegramNotificationPayload>();

            await SyncPhaseAssignsAsync(db, employees, existingByKey, activeKeys, telegramQueue, forceTelegramSend, today, riskUntil, now, cancellationToken);
            await SyncIssuesAsync(db, employees, existingByKey, activeKeys, telegramQueue, forceTelegramSend, today, riskUntil, now, cancellationToken);
            await SyncSupportOrdersAsync(db, employees, existingByKey, activeKeys, telegramQueue, forceTelegramSend, today, riskUntil, now, cancellationToken);
            await SyncFollowupsAsync(db, employees, existingByKey, activeKeys, telegramQueue, forceTelegramSend, today, riskUntil, now, cancellationToken);

            foreach (var notification in existingNotifications.Where(x => !x.IsResolved))
            {
                if (!notification.RecipientEmpId.HasValue)
                    continue;

                var key = Key(notification.SourceType, notification.SourceId, notification.RecipientEmpId.Value);
                if (activeKeys.Contains(key))
                    continue;

                notification.IsResolved = true;
                notification.ResolvedAt = now;
                notification.UpdatedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            if (sendChatNotifications)
            {
                await SendTelegramNotificationsAsync(telegramQueue, cancellationToken);
            }
            else if (telegramQueue.Count > 0)
            {
                _logger.LogInformation(
                    "Automatic overdue chat notifications skipped during manual sync. Queued={QueuedCount}",
                    telegramQueue.Count);
            }

            _logger.LogInformation(
                "Overdue notification sync completed. Active={ActiveCount}, ChatQueued={ChatQueuedCount}, ChatSendEnabled={ChatSendEnabled}",
                activeKeys.Count,
                telegramQueue.Count,
                sendChatNotifications);
        }

        private async Task SyncPhaseAssignsAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            IList<TelegramNotificationPayload> telegramQueue,
            bool forceTelegramSend,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.PhaseAssigns
                .AsNoTracking()
                .Include(x => x.Employee)
                .Include(x => x.Phase!)
                    .ThenInclude(x => x.Project)
                        .ThenInclude(x => x!.Coop)
                .Include(x => x.Phase!)
                    .ThenInclude(x => x.Project)
                        .ThenInclude(x => x!.BA)
                .Include(x => x.Phase!)
                    .ThenInclude(x => x.Project)
                        .ThenInclude(x => x!.TeamMembers)
                            .ThenInclude(member => member.Employee)
                .Where(x => x.PlanEnd.HasValue
                    && x.PlanEnd.Value <= riskUntil
                    && x.Phase != null)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (IsDone(row.WorkStatus) || IsClosedPhase(row.Phase?.PhaseStatus))
                    continue;

                if (!TryBuildDueState(row.PlanEnd, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var projectName = ProjectName(row.Phase?.Project);
                var title = string.IsNullOrWhiteSpace(row.Role) ? row.Phase?.PhaseName ?? $"Assign #{row.AssignId}" : row.Role!;
                var message = BuildPhaseAssignMessage(
                    stateText,
                    row.Phase?.Project?.Coop?.CoopName,
                    projectName,
                    title,
                    EmployeeName(employees, row.EmpId),
                    ProjectBaNames(employees, row.Phase?.Project),
                    row.Phase?.PhaseOrder,
                    row.Phase?.PeriodOrder,
                    row.Phase?.Project?.StartDate,
                    row.Phase?.Project?.EndDate,
                    row.PlanStart ?? row.Phase?.PlanStart,
                    row.PlanEnd ?? row.Phase?.PlanEnd,
                    row.Phase?.PeriodEndDate,
                    row.Remark);
                var projectId = row.Phase?.ProjectId;
                var recipients = new List<NotificationRecipient>();

                foreach (var baEmpId in ProjectBaIds(row.Phase?.Project))
                {
                    recipients.Add(new NotificationRecipient(
                        baEmpId,
                        projectId.HasValue ? $"/PhaseAssigns?projectId={projectId.Value}" : "/PhaseAssigns"));
                }

                recipients.Add(new NotificationRecipient(
                    row.EmpId,
                    projectId.HasValue
                        ? $"/PhaseAssigns?projectId={projectId.Value}&empId={row.EmpId}"
                        : $"/PhaseAssigns?empId={row.EmpId}"));

                foreach (var recipient in UniqueRecipients(recipients))
                {
                    AddOrUpdate(
                        db,
                        employees,
                        existingByKey,
                        activeKeys,
                        telegramQueue,
                        forceTelegramSend,
                        sourceType: "ASSIGN_DUE",
                        sourceId: row.AssignId,
                        empId: recipient.EmpId,
                        severity: severity,
                        title: $"{SeverityTitle(severity)} Assigns:",
                        message: message,
                        targetUrl: recipient.TargetUrl,
                        now: now);
                }
            }
        }

        private async Task SyncIssuesAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            IList<TelegramNotificationPayload> telegramQueue,
            bool forceTelegramSend,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectIssues
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Include(x => x.Project)
                    .ThenInclude(x => x!.BA)
                .Include(x => x.Project)
                    .ThenInclude(x => x!.TeamMembers)
                        .ThenInclude(member => member.Employee)
                .Where(x => x.EndDate.HasValue
                    && x.EndDate.Value <= riskUntil)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (IsIssueClosed(row.IssueStatus))
                    continue;

                if (!TryBuildDueState(row.EndDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var projectName = ProjectName(row.Project);
                var message = BuildWorkMessage(
                    stateText,
                    row.Project?.Coop?.CoopName,
                    projectName,
                    row.IssueName,
                    EmployeeName(employees, row.AssignTo),
                    ProjectBaNames(employees, row.Project),
                    row.StartDate,
                    row.EndDate);
                var recipients = new List<NotificationRecipient>();
                var isWaitingBaReview = IsIssueWaitingBaReview(row.IssueStatus, row.DevStatus);

                foreach (var baEmpId in ProjectBaIds(row.Project))
                    recipients.Add(new NotificationRecipient(baEmpId, $"/ProjectIssues/Details/{row.IssueId}"));

                if (!isWaitingBaReview)
                    recipients.Add(new NotificationRecipient(row.AssignTo, $"/ProjectIssues/DevDetails/{row.IssueId}"));

                foreach (var recipient in UniqueRecipients(recipients))
                {
                    AddOrUpdate(
                        db,
                        employees,
                        existingByKey,
                        activeKeys,
                        telegramQueue,
                        forceTelegramSend,
                        sourceType: "ISSUE_DUE",
                        sourceId: row.IssueId,
                        empId: recipient.EmpId,
                        severity: severity,
                        title: $"{SeverityTitle(severity)} Issues:",
                        message: message,
                        targetUrl: recipient.TargetUrl,
                        now: now);
                }
            }
        }

        private async Task SyncSupportOrdersAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            IList<TelegramNotificationPayload> telegramQueue,
            bool forceTelegramSend,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectSupportOrders
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Include(x => x.Project)
                    .ThenInclude(x => x!.BA)
                .Include(x => x.Project)
                    .ThenInclude(x => x!.TeamMembers)
                        .ThenInclude(member => member.Employee)
                .Where(x => x.EndDate.HasValue
                    && x.EndDate.Value <= riskUntil)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (IsSupportClosed(row.Status))
                    continue;

                if (!TryBuildDueState(row.EndDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var title = string.IsNullOrWhiteSpace(row.OrderTitle) ? $"Support #{row.OrderId}" : row.OrderTitle!;
                var projectName = ProjectName(row.Project);
                var message = BuildWorkMessage(
                    stateText,
                    row.Project?.Coop?.CoopName,
                    projectName,
                    title,
                    EmployeeName(employees, row.AssignTo),
                    ProjectBaNames(employees, row.Project),
                    row.StartDate,
                    row.EndDate);
                var recipients = new List<NotificationRecipient>();
                var isWaitingBaReview = IsSupportWaitingBaReview(row.Status, row.DevStatus);

                foreach (var baEmpId in ProjectBaIds(row.Project))
                    recipients.Add(new NotificationRecipient(baEmpId, $"/SupportOrders/Details/{row.OrderId}"));

                if (!isWaitingBaReview && row.AssignTo.HasValue)
                    recipients.Add(new NotificationRecipient(row.AssignTo.Value, $"/SupportOrdersDev/Details/{row.OrderId}"));

                foreach (var recipient in UniqueRecipients(recipients))
                {
                    AddOrUpdate(
                        db,
                        employees,
                        existingByKey,
                        activeKeys,
                        telegramQueue,
                        forceTelegramSend,
                        sourceType: "SUPPORT_DUE",
                        sourceId: row.OrderId,
                        empId: recipient.EmpId,
                        severity: severity,
                        title: $"{SeverityTitle(severity)} Support:",
                        message: message,
                        targetUrl: recipient.TargetUrl,
                        now: now);
                }
            }
        }

        private async Task SyncFollowupsAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            IList<TelegramNotificationPayload> telegramQueue,
            bool forceTelegramSend,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectFollowups
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Include(x => x.Project)
                    .ThenInclude(x => x!.BA)
                .Include(x => x.Project)
                    .ThenInclude(x => x!.TeamMembers)
                        .ThenInclude(member => member.Employee)
                .Where(x => x.OwnerEmpId.HasValue
                    && x.NextFollowupDate.HasValue
                    && x.NextFollowupDate.Value <= riskUntil)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!row.OwnerEmpId.HasValue)
                    continue;

                if (!IsFollowupOpen(row.Status))
                    continue;

                if (!TryBuildDueState(row.NextFollowupDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var projectName = ProjectName(row.Project);
                var title = string.IsNullOrWhiteSpace(row.TaskTitle) ? $"Followup #{row.FollowupId}" : row.TaskTitle!;
                var startDate = row.LastContactDate ?? row.CreatedAt;
                var message = BuildWorkMessage(
                    stateText,
                    row.Project?.Coop?.CoopName,
                    projectName,
                    title,
                    EmployeeName(employees, row.OwnerEmpId),
                    ProjectBaNames(employees, row.Project),
                    startDate,
                    row.NextFollowupDate);
                var recipients = new List<NotificationRecipient>
                {
                    new(row.OwnerEmpId.Value, $"/Followups/Details/{row.FollowupId}")
                };

                foreach (var baEmpId in ProjectBaIds(row.Project))
                    recipients.Add(new NotificationRecipient(baEmpId, $"/Followups/Details/{row.FollowupId}"));

                foreach (var recipient in UniqueRecipients(recipients))
                {
                    AddOrUpdate(
                        db,
                        employees,
                        existingByKey,
                        activeKeys,
                        telegramQueue,
                        forceTelegramSend,
                        sourceType: "FOLLOWUP_DUE",
                        sourceId: row.FollowupId,
                        empId: recipient.EmpId,
                        severity: severity,
                        title: $"{SeverityTitle(severity)} Followup:",
                        message: message,
                        targetUrl: recipient.TargetUrl,
                        now: now);
                }
            }
        }

        private static void AddOrUpdate(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            IList<TelegramNotificationPayload> telegramQueue,
            bool forceTelegramSend,
            string sourceType,
            int sourceId,
            int empId,
            string severity,
            string title,
            string message,
            string targetUrl,
            DateTime now)
        {
            employees.TryGetValue(empId, out var employee);
            var key = Key(sourceType, sourceId, empId);
            activeKeys.Add(key);
            var normalizedTitle = Trim(title, 255);

            if (existingByKey.TryGetValue(key, out var notification))
            {
                var wasResolved = notification.IsResolved;
                var contentChanged = !string.Equals(notification.Title, normalizedTitle, StringComparison.Ordinal)
                    || !string.Equals(notification.Message, message, StringComparison.Ordinal)
                    || !string.Equals(notification.TargetUrl, targetUrl, StringComparison.Ordinal);
                var severityChangedToDanger = !string.Equals(notification.Severity, severity, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(severity, "DANGER", StringComparison.OrdinalIgnoreCase);

                notification.RecipientUserId = employee?.LoginUserId;
                notification.RecipientEmpId = empId;
                notification.Title = normalizedTitle;
                notification.Message = message;
                notification.TargetUrl = targetUrl;
                notification.Severity = severity;
                notification.IsResolved = false;
                notification.ResolvedAt = null;
                notification.UpdatedAt = now;

                if (wasResolved || contentChanged || severityChangedToDanger)
                {
                    notification.IsRead = false;
                    notification.ReadAt = null;
                    telegramQueue.Add(new TelegramNotificationPayload(empId, normalizedTitle, message, targetUrl));
                }
                else if (forceTelegramSend)
                {
                    telegramQueue.Add(new TelegramNotificationPayload(empId, normalizedTitle, message, targetUrl));
                }

                return;
            }

            telegramQueue.Add(new TelegramNotificationPayload(empId, normalizedTitle, message, targetUrl));
            db.UserNotifications.Add(new UserNotification
            {
                RecipientUserId = employee?.LoginUserId,
                RecipientEmpId = empId,
                SourceType = sourceType,
                SourceId = sourceId,
                Title = normalizedTitle,
                Message = message,
                TargetUrl = targetUrl,
                Severity = severity,
                IsRead = false,
                IsResolved = false,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        private async Task SendTelegramNotificationsAsync(
            IEnumerable<TelegramNotificationPayload> telegramQueue,
            CancellationToken cancellationToken)
        {
            var notifications = telegramQueue
                .GroupBy(x => $"{x.EmpId}:{x.Title}:{x.Message}:{x.TargetUrl}")
                .Select(x => x.First())
                .ToList();

            if (notifications.Count == 0)
                return;

            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.OverdueAuto, cancellationToken);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.OverdueAuto, cancellationToken);

            if (!sendLine && !sendTelegram)
            {
                _logger.LogInformation("Automatic overdue chat notifications skipped because LINE and Telegram are disabled or not configured.");
                return;
            }

            var sentToday = await LoadTodaySuccessfulAutoSendKeysAsync(sendLine, sendTelegram, cancellationToken);

            foreach (var notification in notifications)
            {
                if (sendLine)
                {
                    var key = BuildSendLogKey("LINE", notification);
                    if (sentToday.Contains(key))
                    {
                        _logger.LogInformation(
                            "Automatic LINE notification skipped because it was already sent today. EmpId={EmpId}, Title={Title}",
                            notification.EmpId,
                            notification.Title);
                    }
                    else if (await SendLineNotificationSafelyAsync(notification, cancellationToken))
                    {
                        sentToday.Add(key);
                    }
                }

                if (sendTelegram)
                {
                    var key = BuildSendLogKey("TELEGRAM", notification);
                    if (sentToday.Contains(key))
                    {
                        _logger.LogInformation(
                            "Automatic Telegram notification skipped because it was already sent today. EmpId={EmpId}, Title={Title}",
                            notification.EmpId,
                            notification.Title);
                    }
                    else if (await SendTelegramNotificationSafelyAsync(notification, cancellationToken))
                    {
                        sentToday.Add(key);
                    }
                }
            }
        }

        private async Task<HashSet<string>> LoadTodaySuccessfulAutoSendKeysAsync(
            bool includeLine,
            bool includeTelegram,
            CancellationToken cancellationToken)
        {
            var channels = new List<string>(capacity: 2);
            if (includeLine)
                channels.Add("LINE");
            if (includeTelegram)
                channels.Add("TELEGRAM");

            if (channels.Count == 0)
                return new HashSet<string>(StringComparer.Ordinal);

            var today = GetBangkokNow().Date;
            var tomorrow = today.AddDays(1);

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var logs = await db.NotificationSendLogs
                .AsNoTracking()
                .Where(x => x.SentAt >= today
                    && x.SentAt < tomorrow
                    && x.RecipientEmpId.HasValue
                    && channels.Contains(x.Channel))
                .Select(x => new
                {
                    x.Channel,
                    EmpId = x.RecipientEmpId!.Value,
                    x.Title,
                    x.Message,
                    x.TargetUrl
                })
                .ToListAsync(cancellationToken);

            return logs
                .Select(x => BuildSendLogKey(x.Channel, x.EmpId, x.Title, x.Message, x.TargetUrl))
                .ToHashSet(StringComparer.Ordinal);
        }

        private async Task<bool> SendLineNotificationSafelyAsync(
            TelegramNotificationPayload notification,
            CancellationToken cancellationToken)
        {
            try
            {
                var sentCount = await _lineMessagingService.SendNotificationToEmployeeAsync(
                    notification.EmpId,
                    notification.Title,
                    notification.Message,
                    notification.TargetUrl,
                    cancellationToken);
                return sentCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Automatic LINE notification failed for EmpId={EmpId}", notification.EmpId);
                return false;
            }
        }

        private async Task<bool> SendTelegramNotificationSafelyAsync(
            TelegramNotificationPayload notification,
            CancellationToken cancellationToken)
        {
            try
            {
                var sentCount = await _telegramMessagingService.SendNotificationToEmployeeAsync(
                    notification.EmpId,
                    notification.Title,
                    notification.Message,
                    notification.TargetUrl,
                    cancellationToken);
                return sentCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Automatic Telegram notification failed for EmpId={EmpId}", notification.EmpId);
                return false;
            }
        }

        private static string BuildSendLogKey(string channel, TelegramNotificationPayload notification)
            => BuildSendLogKey(channel, notification.EmpId, notification.Title, notification.Message, notification.TargetUrl);

        private static string BuildSendLogKey(string channel, int empId, string title, string? message, string? targetUrl)
            => string.Join('\u001f',
                channel.Trim().ToUpperInvariant(),
                empId.ToString(CultureInfo.InvariantCulture),
                Trim(title, 255) ?? "",
                message ?? "",
                NormalizeTargetUrlForSendLogKey(targetUrl));

        private static string NormalizeTargetUrlForSendLogKey(string? targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return "";

            var trimmed = Trim(targetUrl, 500);
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.PathAndQuery;
            }

            return trimmed;
        }

        private static void ResolveDuplicateNotifications(IEnumerable<UserNotification> notifications, DateTime now)
        {
            var duplicateGroups = notifications
                .Where(x => x.RecipientEmpId.HasValue)
                .GroupBy(x => Key(x.SourceType, x.SourceId, x.RecipientEmpId!.Value))
                .Where(group => group.Count() > 1);

            foreach (var group in duplicateGroups)
            {
                var keeper = SelectPrimaryNotification(group);
                foreach (var duplicate in group.Where(x => x.NotificationId != keeper.NotificationId))
                {
                    if (duplicate.IsResolved)
                        continue;

                    duplicate.IsResolved = true;
                    duplicate.ResolvedAt = now;
                    duplicate.UpdatedAt = now;
                }
            }
        }

        private static UserNotification SelectPrimaryNotification(IEnumerable<UserNotification> notifications)
            => notifications
                .OrderByDescending(x => !x.IsResolved)
                .ThenBy(x => x.IsRead)
                .ThenByDescending(x => string.Equals(x.Severity, "DANGER", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.NotificationId)
                .First();

        private static bool TryBuildDueState(
            DateTime? dueDate,
            DateTime today,
            DateTime riskUntil,
            out string severity,
            out string dueText,
            out string stateText)
        {
            severity = "WARNING";
            dueText = "";
            stateText = "";

            if (!dueDate.HasValue)
                return false;

            var due = dueDate.Value.Date;
            if (due > riskUntil)
                return false;

            dueText = ThaiDateText(due);

            if (due < today)
            {
                severity = "DANGER";
                stateText = $"ล่าช้า {(today - due).Days:N0} วัน";
                return true;
            }

            if (due == today)
            {
                severity = "WARNING";
                stateText = "ครบกำหนดวันนี้";
                return true;
            }

            severity = "WARNING";
            stateText = $"เสี่ยงล่าช้า เหลือ {(due - today).Days:N0} วัน";
            return true;
        }

        private async Task<int> GetRiskDaysAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            var value = await db.SystemConfigs
                .AsNoTracking()
                .Where(x => x.ConfigKey == "OVERDUE_NOTIFICATION_RISK_DAYS")
                .Select(x => x.ConfigValue)
                .FirstOrDefaultAsync(cancellationToken);

            if (int.TryParse(value, out var parsed))
                return Math.Clamp(parsed, 0, 30);

            return _defaultRiskDays;
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

        private static string SeverityTitle(string severity)
        {
            return string.Equals(severity, "DANGER", StringComparison.OrdinalIgnoreCase)
                ? "งานล่าช้า"
                : "งานเสี่ยงล่าช้า";
        }

        private static string Key(string sourceType, int sourceId, int empId)
            => $"{sourceType}:{sourceId}:{empId}";

        private static string ProjectName(Project? project)
            => project == null || string.IsNullOrWhiteSpace(project.ProjectName)
                ? "-"
                : project.ProjectName;

        private static string BuildWorkMessage(
            string stateText,
            string? coopName,
            string projectName,
            string title,
            string ownerName,
            string baName,
            DateTime? startDate,
            DateTime? endDate)
        {
            return string.Join("\n", new[]
            {
                $"สถานะ: {stateText}",
                $"สหกรณ์: {(string.IsNullOrWhiteSpace(coopName) ? "-" : coopName)}",
                $"Project: {projectName}",
                $"งาน: {title}",
                $"เจ้าของงาน: {ownerName}",
                $"BA: {baName}",
                $"วันที่เริ่ม: {DateText(startDate)}",
                $"วันที่สิ้นสุด: {DateText(endDate)}"
            });
        }

        private static string BuildPhaseAssignMessage(
            string stateText,
            string? coopName,
            string projectName,
            string title,
            string ownerName,
            string baName,
            int? phaseOrder,
            int? periodOrder,
            DateTime? projectStart,
            DateTime? projectEnd,
            DateTime? planStart,
            DateTime? planEnd,
            DateTime? dueDate,
            string? remark)
        {
            return string.Join("\n", new[]
            {
                $"สถานะ: {stateText}",
                $"สหกรณ์: {(string.IsNullOrWhiteSpace(coopName) ? "-" : coopName)}",
                $"Project: {projectName}",
                $"งาน: {title}",
                $"เจ้าของงาน: {ownerName}",
                $"BA: {baName}",
                $"ส่วน / งวด: ส่วนที่ {(phaseOrder?.ToString() ?? "-")} / งวดที่ {(periodOrder?.ToString() ?? "-")}",
                $"Project Period: {DateText(projectStart)} - {DateText(projectEnd)}",
                $"Plan: {DateText(planStart)} - {DateText(planEnd)}",
                $"กำหนดส่งงวดงาน: {DateText(dueDate)}",
                $"Remark: {(string.IsNullOrWhiteSpace(remark) ? "-" : remark)}"
            });
        }

        private static string EmployeeName(
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            int? empId)
        {
            if (!empId.HasValue || empId.Value <= 0)
                return "-";

            return employees.TryGetValue(empId.Value, out var employee)
                ? employee.EmpName
                : $"Employee #{empId.Value}";
        }

        private static string DateText(DateTime? value)
            => value.HasValue ? ThaiDateText(value.Value) : "-";

        private static string ThaiDateText(DateTime value)
            => value.ToString("dd MMM yyyy", ThaiCulture);

        private static readonly CultureInfo ThaiCulture = new("th-TH");

        private static IReadOnlyList<int> ProjectBaIds(Project? project)
        {
            if (project == null)
                return Array.Empty<int>();

            return project.BusinessAnalysts
                .Select(employee => employee.EmpId)
                .Distinct()
                .ToList();
        }

        private static string ProjectBaNames(
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            Project? project)
        {
            var names = ProjectBaIds(project)
                .Select(empId => EmployeeName(employees, empId))
                .Where(name => !string.IsNullOrWhiteSpace(name) && name != "-")
                .Distinct()
                .ToList();
            return names.Count == 0 ? "-" : string.Join(", ", names);
        }

        private static IEnumerable<NotificationRecipient> UniqueRecipients(IEnumerable<NotificationRecipient> recipients)
        {
            var seen = new HashSet<int>();
            foreach (var recipient in recipients)
            {
                if (recipient.EmpId <= 0 || !seen.Add(recipient.EmpId))
                    continue;

                yield return recipient;
            }
        }

        private static string Trim(string value, int maxLength)
            => string.IsNullOrEmpty(value) || value.Length <= maxLength
                ? value
                : value[..maxLength];

        private static bool IsIssueClosed(string? issueStatus)
        {
            var issue = (issueStatus ?? "").Trim().ToUpperInvariant();
            return issue is "PASS" or "REJECT" or "DONE";
        }

        private static bool IsIssueWaitingBaReview(string? issueStatus, string? devStatus)
        {
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();
            return !IsIssueClosed(issueStatus) && dev == "FIXED";
        }

        private static bool IsDone(string? status)
        {
            var raw = (status ?? "").Trim();
            var normalized = raw.ToUpperInvariant();
            return normalized == "DONE" || raw is "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
        }

        private static bool IsSupportClosed(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized is "PASS" or "REJECT" or "DONE";
        }

        private static bool IsSupportWaitingBaReview(string? status, string? devStatus)
        {
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();
            return !IsSupportClosed(status) && dev == "FIXED";
        }

        private static bool IsFollowupOpen(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized == "OPEN";
        }

        private static bool IsClosedPhase(string? phaseStatus)
        {
            var normalized = (phaseStatus ?? "").Trim();
            return normalized is "ส่งงวดงานแล้ว" or "อนุมัติจ่ายเงินแล้ว" or "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
        }

        private sealed record EmployeeRecipient(int EmpId, string EmpName, int? LoginUserId);
        private sealed record NotificationRecipient(int EmpId, string TargetUrl);
        private sealed record TelegramNotificationPayload(int EmpId, string Title, string? Message, string? TargetUrl);
    }
}
