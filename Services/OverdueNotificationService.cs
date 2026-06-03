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
        private readonly ILogger<OverdueNotificationService> _logger;
        private readonly int _riskDays;

        public OverdueNotificationService(
            IDbContextFactory<AppDbContext> dbFactory,
            IConfiguration configuration,
            ILogger<OverdueNotificationService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _riskDays = Math.Clamp(configuration.GetValue<int?>("OVERDUE_NOTIFICATION_RISK_DAYS") ?? 3, 0, 30);
        }

        public async Task SyncAsync(CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var today = DateTime.Today;
            var riskUntil = today.AddDays(_riskDays);
            var now = DateTime.Now;

            var employees = await db.Employees
                .AsNoTracking()
                .Select(x => new EmployeeRecipient(
                    x.EmpId,
                    x.EmpName ?? $"Employee #{x.EmpId}",
                    x.LoginUserId))
                .ToDictionaryAsync(x => x.EmpId, cancellationToken);

            var existingNotifications = await db.UserNotifications
                .Where(x => ManagedSourceTypes.Contains(x.SourceType))
                .ToListAsync(cancellationToken);

            var existingByKey = existingNotifications
                .Where(x => x.RecipientEmpId.HasValue)
                .GroupBy(x => Key(x.SourceType, x.SourceId, x.RecipientEmpId!.Value))
                .ToDictionary(x => x.Key, x => x.OrderByDescending(n => n.UpdatedAt).First());

            var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await SyncPhaseAssignsAsync(db, employees, existingByKey, activeKeys, today, riskUntil, now, cancellationToken);
            await SyncIssuesAsync(db, employees, existingByKey, activeKeys, today, riskUntil, now, cancellationToken);
            await SyncSupportOrdersAsync(db, employees, existingByKey, activeKeys, today, riskUntil, now, cancellationToken);
            await SyncFollowupsAsync(db, employees, existingByKey, activeKeys, today, riskUntil, now, cancellationToken);

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
            _logger.LogInformation("Overdue notification sync completed. Active={ActiveCount}", activeKeys.Count);
        }

        private async Task SyncPhaseAssignsAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
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
                .Where(x => x.PlanEnd.HasValue
                    && x.PlanEnd.Value <= riskUntil
                    && (x.WorkStatus == null || x.WorkStatus.ToUpper() != "DONE")
                    && x.Phase != null
                    && (x.Phase.PhaseStatus == null
                        || (x.Phase.PhaseStatus != "ส่งงวดงานแล้ว"
                            && x.Phase.PhaseStatus != "อนุมัติจ่ายเงินแล้ว")))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!TryBuildDueState(row.PlanEnd, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var projectName = row.Phase?.Project?.ProjectName ?? "-";
                var title = string.IsNullOrWhiteSpace(row.Role) ? row.Phase?.PhaseName ?? $"Assign #{row.AssignId}" : row.Role!;
                var message = $"{stateText} | กำหนด {dueText} | Project: {projectName}";

                AddOrUpdate(
                    db,
                    employees,
                    existingByKey,
                    activeKeys,
                    sourceType: "ASSIGN_DUE",
                    sourceId: row.AssignId,
                    empId: row.EmpId,
                    severity: severity,
                    title: $"{SeverityTitle(severity)} Assigns: {title}",
                    message: message,
                    targetUrl: $"/PhaseAssigns?projectId={row.Phase?.ProjectId}&empId={row.EmpId}",
                    now: now);
            }
        }

        private async Task SyncIssuesAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectIssues
                .AsNoTracking()
                .Include(x => x.Project)
                .Where(x => x.EndDate.HasValue
                    && x.EndDate.Value <= riskUntil
                    && !IsIssueDone(x.IssueStatus, x.DevStatus))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!TryBuildDueState(row.EndDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var projectName = row.Project?.ProjectName ?? "-";
                var message = $"{stateText} | กำหนด {dueText} | Project: {projectName}";

                AddOrUpdate(
                    db,
                    employees,
                    existingByKey,
                    activeKeys,
                    sourceType: "ISSUE_DUE",
                    sourceId: row.IssueId,
                    empId: row.AssignTo,
                    severity: severity,
                    title: $"{SeverityTitle(severity)} Issue: {row.IssueName}",
                    message: message,
                    targetUrl: $"/ProjectIssues/Details/{row.IssueId}",
                    now: now);
            }
        }

        private async Task SyncSupportOrdersAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectSupportOrders
                .AsNoTracking()
                .Include(x => x.Project)
                .Where(x => x.AssignTo.HasValue
                    && x.EndDate.HasValue
                    && x.EndDate.Value <= riskUntil
                    && !IsSupportDone(x.Status))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!row.AssignTo.HasValue)
                    continue;

                if (!TryBuildDueState(row.EndDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var title = string.IsNullOrWhiteSpace(row.OrderTitle) ? $"Support #{row.OrderId}" : row.OrderTitle!;
                var projectName = row.Project?.ProjectName ?? "-";
                var message = $"{stateText} | กำหนด {dueText} | Project: {projectName}";

                AddOrUpdate(
                    db,
                    employees,
                    existingByKey,
                    activeKeys,
                    sourceType: "SUPPORT_DUE",
                    sourceId: row.OrderId,
                    empId: row.AssignTo.Value,
                    severity: severity,
                    title: $"{SeverityTitle(severity)} Support: {title}",
                    message: message,
                    targetUrl: $"/SupportOrders/Details/{row.OrderId}",
                    now: now);
            }
        }

        private async Task SyncFollowupsAsync(
            AppDbContext db,
            IReadOnlyDictionary<int, EmployeeRecipient> employees,
            IReadOnlyDictionary<string, UserNotification> existingByKey,
            ISet<string> activeKeys,
            DateTime today,
            DateTime riskUntil,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var rows = await db.ProjectFollowups
                .AsNoTracking()
                .Include(x => x.Project)
                .Where(x => x.OwnerEmpId.HasValue
                    && x.NextFollowupDate.HasValue
                    && x.NextFollowupDate.Value <= riskUntil
                    && !IsFollowupDone(x.Status))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!row.OwnerEmpId.HasValue)
                    continue;

                if (!TryBuildDueState(row.NextFollowupDate, today, riskUntil, out var severity, out var dueText, out var stateText))
                    continue;

                var projectName = row.Project?.ProjectName ?? "-";
                var message = $"{stateText} | นัดติดตาม {dueText} | Project: {projectName}";

                AddOrUpdate(
                    db,
                    employees,
                    existingByKey,
                    activeKeys,
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
                }

                return;
            }

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

        private static string Trim(string value, int maxLength)
            => string.IsNullOrEmpty(value) || value.Length <= maxLength
                ? value
                : value[..maxLength];

        private static bool IsIssueDone(string? issueStatus, string? devStatus)
        {
            var issue = (issueStatus ?? "").Trim().ToUpperInvariant();
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();
            return issue is "FIXED" or "PASS" || dev == "FIXED";
        }

        private static bool IsSupportDone(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized == "DONE";
        }

        private static bool IsFollowupDone(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized is "DONE" or "ACK";
        }

        private sealed record EmployeeRecipient(int EmpId, string EmpName, int? LoginUserId);
    }
}
