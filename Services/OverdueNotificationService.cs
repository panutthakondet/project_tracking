using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;

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
        private readonly ILogger<OverdueNotificationService> _logger;
        private readonly int _riskDays;

        public OverdueNotificationService(
            IDbContextFactory<AppDbContext> dbFactory,
            LineMessagingService lineMessagingService,
            IConfiguration configuration,
            ILogger<OverdueNotificationService> logger)
        {
            _dbFactory = dbFactory;
            _lineMessagingService = lineMessagingService;
            _logger = logger;
            _riskDays = Math.Clamp(configuration.GetValue<int?>("OVERDUE_NOTIFICATION_RISK_DAYS") ?? 7, 0, 30);
        }

        public async Task SyncAsync(CancellationToken cancellationToken = default)
            => await SyncAsync(forceLineSend: false, cancellationToken);

        public async Task SyncAndSendLineAsync(CancellationToken cancellationToken = default)
            => await SyncAsync(forceLineSend: true, cancellationToken);

        private async Task SyncAsync(bool forceLineSend, CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var today = DateTime.Today;
            var riskUntil = today.AddDays(_riskDays);
            var now = DateTime.Now;

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
            var lineQueue = new List<LineNotificationPayload>();

            await SyncPhaseAssignsAsync(db, employees, existingByKey, activeKeys, lineQueue, forceLineSend, today, riskUntil, now, cancellationToken);
            await SyncIssuesAsync(db, employees, existingByKey, activeKeys, lineQueue, forceLineSend, today, riskUntil, now, cancellationToken);
            await SyncSupportOrdersAsync(db, employees, existingByKey, activeKeys, lineQueue, forceLineSend, today, riskUntil, now, cancellationToken);
            await SyncFollowupsAsync(db, employees, existingByKey, activeKeys, lineQueue, forceLineSend, today, riskUntil, now, cancellationToken);

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
            await SendLineNotificationsAsync(lineQueue, cancellationToken);
            _logger.LogInformation("Overdue notification sync completed. Active={ActiveCount}", activeKeys.Count);
        }

        private async Task SyncPhaseAssignsAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            IList<LineNotificationPayload> lineQueue,
            bool forceLineSend,
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

                var projectName = ProjectDisplayName(row.Phase?.Project);
                var title = string.IsNullOrWhiteSpace(row.Role) ? row.Phase?.PhaseName ?? $"Assign #{row.AssignId}" : row.Role!;
                var message = BuildPhaseAssignMessage(
                    stateText,
                    row.Phase?.Project?.Coop?.CoopName,
                    projectName,
                    title,
                    EmployeeName(employees, row.EmpId),
                    EmployeeName(employees, row.Phase?.Project?.BaEmpId),
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

                if (row.Phase?.Project?.BaEmpId.HasValue == true)
                {
                    recipients.Add(new NotificationRecipient(
                        row.Phase.Project.BaEmpId.Value,
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
                        lineQueue,
                        forceLineSend,
                        sourceType: "ASSIGN_DUE",
                        sourceId: row.AssignId,
                        empId: recipient.EmpId,
                        severity: severity,
                        title: $"{SeverityTitle(severity)} Assigns: {title}",
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
            IList<LineNotificationPayload> lineQueue,
            bool forceLineSend,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectIssues
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Where(x => x.EndDate.HasValue
                    && x.EndDate.Value <= riskUntil)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (IsIssueDone(row.IssueStatus, row.DevStatus))
                    continue;

                if (!TryBuildDueState(row.EndDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var projectName = ProjectDisplayName(row.Project);
                var baEmpId = row.Project?.BaEmpId;
                var message = BuildWorkMessage(
                    stateText,
                    projectName,
                    row.IssueName,
                    EmployeeName(employees, row.AssignTo),
                    EmployeeName(employees, baEmpId),
                    row.StartDate,
                    row.EndDate);
                var recipients = new List<NotificationRecipient>();

                if (row.Project?.BaEmpId.HasValue == true)
                    recipients.Add(new NotificationRecipient(row.Project.BaEmpId.Value, $"/ProjectIssues/Edit/{row.IssueId}"));

                recipients.Add(new NotificationRecipient(row.AssignTo, $"/ProjectIssues/DevEdit/{row.IssueId}"));

                foreach (var recipient in UniqueRecipients(recipients))
                {
                    AddOrUpdate(
                        db,
                        employees,
                        existingByKey,
                        activeKeys,
                        lineQueue,
                        forceLineSend,
                        sourceType: "ISSUE_DUE",
                        sourceId: row.IssueId,
                        empId: recipient.EmpId,
                        severity: severity,
                        title: $"{SeverityTitle(severity)} Issue: {row.IssueName}",
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
            IList<LineNotificationPayload> lineQueue,
            bool forceLineSend,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectSupportOrders
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Where(x => x.EndDate.HasValue
                    && x.EndDate.Value <= riskUntil)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (IsSupportDone(row.Status, row.DevStatus))
                    continue;

                if (!TryBuildDueState(row.EndDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var title = string.IsNullOrWhiteSpace(row.OrderTitle) ? $"Support #{row.OrderId}" : row.OrderTitle!;
                var projectName = ProjectDisplayName(row.Project);
                var baEmpId = row.Project?.BaEmpId;
                var message = BuildWorkMessage(
                    stateText,
                    projectName,
                    title,
                    EmployeeName(employees, row.AssignTo),
                    EmployeeName(employees, baEmpId),
                    row.StartDate,
                    row.EndDate);
                var recipients = new List<NotificationRecipient>();

                if (row.Project?.BaEmpId.HasValue == true)
                    recipients.Add(new NotificationRecipient(row.Project.BaEmpId.Value, $"/SupportOrders/Edit/{row.OrderId}"));

                if (row.AssignTo.HasValue)
                    recipients.Add(new NotificationRecipient(row.AssignTo.Value, $"/SupportOrdersDev/Edit/{row.OrderId}"));

                foreach (var recipient in UniqueRecipients(recipients))
                {
                    AddOrUpdate(
                        db,
                        employees,
                        existingByKey,
                        activeKeys,
                        lineQueue,
                        forceLineSend,
                        sourceType: "SUPPORT_DUE",
                        sourceId: row.OrderId,
                        empId: recipient.EmpId,
                        severity: severity,
                        title: $"{SeverityTitle(severity)} Support: {title}",
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
            IList<LineNotificationPayload> lineQueue,
            bool forceLineSend,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectFollowups
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Where(x => x.OwnerEmpId.HasValue
                    && x.NextFollowupDate.HasValue
                    && x.NextFollowupDate.Value <= riskUntil)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!row.OwnerEmpId.HasValue)
                    continue;

                if (IsFollowupDone(row.Status))
                    continue;

                if (!TryBuildDueState(row.NextFollowupDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var projectName = ProjectDisplayName(row.Project);
                var message = $"{stateText} | นัดติดตาม {dueText} | Project: {projectName}";

                AddOrUpdate(
                    db,
                    employees,
                    existingByKey,
                    activeKeys,
                    lineQueue,
                    forceLineSend,
                    sourceType: "FOLLOWUP_DUE",
                    sourceId: row.FollowupId,
                    empId: row.OwnerEmpId.Value,
                    severity: severity,
                    title: $"{SeverityTitle(severity)} Followup: {row.TaskTitle}",
                    message: message,
                    targetUrl: $"/Followups/Details/{row.FollowupId}",
                    now: now);
            }
        }

        private static void AddOrUpdate(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            IList<LineNotificationPayload> lineQueue,
            bool forceLineSend,
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
                    lineQueue.Add(new LineNotificationPayload(empId, normalizedTitle, message, targetUrl));
                }
                else if (forceLineSend)
                {
                    lineQueue.Add(new LineNotificationPayload(empId, normalizedTitle, message, targetUrl));
                }

                return;
            }

            lineQueue.Add(new LineNotificationPayload(empId, normalizedTitle, message, targetUrl));
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

        private async Task SendLineNotificationsAsync(
            IEnumerable<LineNotificationPayload> lineQueue,
            CancellationToken cancellationToken)
        {
            foreach (var notification in lineQueue
                .GroupBy(x => $"{x.EmpId}:{x.Title}:{x.Message}:{x.TargetUrl}")
                .Select(x => x.First()))
            {
                try
                {
                    await _lineMessagingService.SendNotificationToEmployeeAsync(
                        notification.EmpId,
                        notification.Title,
                        notification.Message,
                        notification.TargetUrl,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LINE notification failed for EmpId={EmpId}", notification.EmpId);
                }
            }
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

            dueText = due.ToString("dd/MM/yyyy");

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

        private static string SeverityTitle(string severity)
        {
            return string.Equals(severity, "DANGER", StringComparison.OrdinalIgnoreCase)
                ? "งานล่าช้า"
                : "งานเสี่ยงล่าช้า";
        }

        private static string Key(string sourceType, int sourceId, int empId)
            => $"{sourceType}:{sourceId}:{empId}";

        private static string ProjectDisplayName(Project? project)
            => project == null || string.IsNullOrWhiteSpace(project.ProjectDisplayName)
                ? "-"
                : project.ProjectDisplayName;

        private static string BuildWorkMessage(
            string stateText,
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
                $"Project: {projectName}",
                $"หัวข้อ: {title}",
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
                $"หัวข้อ: {title}",
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
            => value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "-";

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

        private static bool IsIssueDone(string? issueStatus, string? devStatus)
        {
            var issue = (issueStatus ?? "").Trim().ToUpperInvariant();
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();
            return issue is "FIXED" or "PASS" or "REJECT" || dev == "FIXED";
        }

        private static bool IsDone(string? status)
        {
            var raw = (status ?? "").Trim();
            var normalized = raw.ToUpperInvariant();
            return normalized == "DONE" || raw is "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
        }

        private static bool IsSupportDone(string? status, string? devStatus)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();
            return normalized is "FIXED" or "PASS" or "REJECT" or "DONE" || dev == "FIXED";
        }

        private static bool IsFollowupDone(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized is "DONE" or "ACK";
        }

        private static bool IsClosedPhase(string? phaseStatus)
        {
            var normalized = (phaseStatus ?? "").Trim();
            return normalized is "ส่งงวดงานแล้ว" or "อนุมัติจ่ายเงินแล้ว" or "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
        }

        private sealed record EmployeeRecipient(int EmpId, string EmpName, int? LoginUserId);
        private sealed record NotificationRecipient(int EmpId, string TargetUrl);
        private sealed record LineNotificationPayload(int EmpId, string Title, string? Message, string? TargetUrl);
    }
}
