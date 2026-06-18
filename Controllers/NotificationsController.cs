using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;
using System.Globalization;

namespace ProjectTracking.Controllers
{
    public class NotificationsController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly OverdueNotificationService _notificationService;
        private static readonly CultureInfo ThaiCulture = new("th-TH");

        public NotificationsController(
            AppDbContext context,
            OverdueNotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index(string? severity = null, bool includeResolved = false)
        {
            try
            {
                await _notificationService.SyncAsync(HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                ViewBag.SyncError = ex.Message;
            }

            var query = ExcludeOpenWorkNotifications(ApplyVisibility(_context.UserNotifications
                .AsNoTracking()
                .Include(x => x.RecipientEmployee)
                    .ThenInclude(employee => employee!.LoginUser)
                .AsQueryable()));

            if (!includeResolved)
                query = query.Where(x => !x.IsResolved);

            if (!string.IsNullOrWhiteSpace(severity))
            {
                var normalizedSeverity = severity.Trim().ToUpperInvariant();
                query = query.Where(x => x.Severity == normalizedSeverity);
            }

            var notifications = await query
                .OrderBy(x => x.IsRead)
                .ThenByDescending(x => x.Severity == "DANGER")
                .ThenByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync();
            notifications = DeduplicateForPage(notifications, IsAdmin());

            ViewBag.SelectedSeverity = severity ?? "";
            ViewBag.IncludeResolved = includeResolved;
            ViewBag.IsAdmin = IsAdmin();

            var model = await BuildPageModelAsync(notifications);
            model.IsAdmin = IsAdmin();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Open(int id)
        {
            var notification = await ExcludeOpenWorkNotifications(ApplyVisibility(_context.UserNotifications.AsQueryable()))
                .FirstOrDefaultAsync(x => x.NotificationId == id);

            if (notification == null)
                return RedirectToAction(nameof(Index));

            var notificationsToMarkRead = IsAdmin()
                ? await ExcludeOpenWorkNotifications(ApplyVisibility(_context.UserNotifications.AsQueryable()))
                    .Where(x => x.SourceType == notification.SourceType
                        && x.SourceId == notification.SourceId
                        && !x.IsResolved
                        && !x.IsRead)
                    .ToListAsync()
                : notification.IsRead
                    ? new List<UserNotification>()
                    : new List<UserNotification> { notification };

            if (notificationsToMarkRead.Count > 0)
            {
                var now = DateTime.Now;
                foreach (var item in notificationsToMarkRead)
                {
                    item.IsRead = true;
                    item.ReadAt = now;
                    item.UpdatedAt = now;
                }

                await _context.SaveChangesAsync();
            }

            if (!string.IsNullOrWhiteSpace(notification.TargetUrl) && Url.IsLocalUrl(notification.TargetUrl))
                return LocalRedirect(notification.TargetUrl);

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> SyncNow()
        {
            if (!IsAdmin())
                return Forbid();

            try
            {
                await _notificationService.SyncAsync(HttpContext.RequestAborted);

                var activeCount = await ExcludeOpenWorkNotifications(_context.UserNotifications
                    .AsNoTracking()
                    .AsQueryable())
                    .CountAsync(x => !x.IsResolved);

                var unreadCount = await ExcludeOpenWorkNotifications(_context.UserNotifications
                    .AsNoTracking()
                    .AsQueryable())
                    .CountAsync(x => !x.IsResolved && !x.IsRead);

                return Content(
                    $"Notification sync completed: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\nActive: {activeCount}\nUnread: {unreadCount}",
                    "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                return Content(
                    $"Notification sync failed: {ex.GetType().Name}\n{ex.Message}",
                    "text/plain; charset=utf-8");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DebugSupport(int id)
        {
            if (!IsAdmin())
                return Forbid();

            var today = DateTime.Today;
            var order = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(project => project!.Coop)
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.OrderId == id);

            if (order == null)
                return Content($"Support order #{id} not found.", "text/plain; charset=utf-8");

            var notification = await _context.UserNotifications
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SourceType == "SUPPORT_DUE" && x.SourceId == id);

            var hasAssignee = order.AssignTo.HasValue;
            var hasEndDate = order.EndDate.HasValue;
            var status = (order.Status ?? "").Trim().ToUpperInvariant();
            var isDone = status is "FIXED" or "PASS" or "REJECT" or "DONE";
            var isDueOrRisk = hasEndDate && order.EndDate!.Value.Date <= today.AddDays(3);
            var shouldNotify = hasAssignee && hasEndDate && isDueOrRisk && !isDone;

            var lines = new List<string>
            {
                $"Support order #{order.OrderId}",
                $"Project: {ProjectDisplayName(order.Project)}",
                $"AssignTo: {order.AssignTo?.ToString() ?? "-"} ({order.Employee?.EmpName ?? "-"})",
                $"Status: {order.Status ?? "-"}",
                $"DevStatus: {order.DevStatus ?? "-"}",
                $"StartDate: {FormatDate(order.StartDate)}",
                $"EndDate: {FormatDate(order.EndDate)}",
                $"Today: {today:yyyy-MM-dd}",
                $"RiskUntil: {today.AddDays(3):yyyy-MM-dd}",
                $"ShouldNotify: {shouldNotify}",
                $"ExistingNotification: {(notification == null ? "No" : $"Yes #{notification.NotificationId}, Read={notification.IsRead}, Resolved={notification.IsResolved}, Severity={notification.Severity}")}"
            };

            return Content(string.Join('\n', lines), "text/plain; charset=utf-8");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReadAll(string? returnUrl = null)
        {
            var notifications = await ExcludeOpenWorkNotifications(ApplyVisibility(_context.UserNotifications.AsQueryable()))
                .Where(x => !x.IsResolved && !x.IsRead)
                .ToListAsync();

            var now = DateTime.Now;
            foreach (var notification in notifications)
            {
                notification.IsRead = true;
                notification.ReadAt = now;
                notification.UpdatedAt = now;
            }

            if (notifications.Count > 0)
                await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return RedirectToAction(nameof(Index));
        }

        private IQueryable<UserNotification> ApplyVisibility(IQueryable<UserNotification> query)
        {
            if (IsAdmin())
                return query;

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return query.Where(x => false);

            return query.Where(x => x.RecipientUserId == userId.Value);
        }

        private static IQueryable<UserNotification> ExcludeOpenWorkNotifications(IQueryable<UserNotification> query)
            => query.Where(x => x.SourceType != "ISSUE_DUE" && x.SourceType != "SUPPORT_DUE");

        private static List<UserNotification> DeduplicateForPage(List<UserNotification> notifications, bool isAdmin)
            => notifications
                .GroupBy(notification => isAdmin
                    ? $"{Source(notification.SourceType)}:{notification.SourceId}"
                    : $"{Source(notification.SourceType)}:{notification.SourceId}:{notification.RecipientEmpId}:{notification.RecipientUserId}")
                .Select(group => group
                    .OrderBy(notification => notification.IsRead)
                    .ThenByDescending(notification => Source(notification.Severity) == "DANGER")
                    .ThenByDescending(notification => notification.CreatedAt)
                    .ThenByDescending(notification => notification.NotificationId)
                    .First())
                .OrderBy(notification => notification.IsRead)
                .ThenByDescending(notification => Source(notification.Severity) == "DANGER")
                .ThenByDescending(notification => notification.CreatedAt)
                .ToList();

        private async Task<NotificationPageViewModel> BuildPageModelAsync(List<UserNotification> notifications)
        {
            var assignIds = notifications
                .Where(x => Source(x.SourceType) == "ASSIGN_DUE")
                .Select(x => x.SourceId)
                .Distinct()
                .ToList();

            var followupIds = notifications
                .Where(x => Source(x.SourceType) == "FOLLOWUP_DUE")
                .Select(x => x.SourceId)
                .Distinct()
                .ToList();

            var assignMap = assignIds.Count == 0
                ? new Dictionary<int, PhaseAssign>()
                : await _context.PhaseAssigns
                    .AsNoTracking()
                    .Include(x => x.Employee)
                        .ThenInclude(employee => employee!.LoginUser)
                    .Include(x => x.Phase!)
                        .ThenInclude(phase => phase.Project!)
                            .ThenInclude(project => project.Coop)
                    .Include(x => x.Phase!)
                        .ThenInclude(phase => phase.Project!)
                            .ThenInclude(project => project.BA)
                                .ThenInclude(ba => ba!.LoginUser)
                    .Where(x => assignIds.Contains(x.AssignId))
                    .ToDictionaryAsync(x => x.AssignId);

            var followupMap = followupIds.Count == 0
                ? new Dictionary<int, ProjectFollowup>()
                : await _context.ProjectFollowups
                    .AsNoTracking()
                    .Include(x => x.Owner)
                        .ThenInclude(owner => owner!.LoginUser)
                    .Include(x => x.Project!)
                        .ThenInclude(project => project.Coop)
                    .Include(x => x.Project!)
                        .ThenInclude(project => project.BA)
                            .ThenInclude(ba => ba!.LoginUser)
                    .Where(x => followupIds.Contains(x.FollowupId))
                    .ToDictionaryAsync(x => x.FollowupId);

            var items = notifications
                .Select(notification => BuildItem(notification, assignMap, followupMap))
                .ToList();

            return new NotificationPageViewModel
            {
                Groups = items
                    .GroupBy(x => x.SourceType)
                    .OrderBy(x => GroupOrder(x.Key))
                    .ThenBy(x => GroupLabel(x.Key))
                    .Select(group =>
                    {
                        var sortedItems = group
                            .OrderBy(x => x.IsRead)
                            .ThenByDescending(x => Source(x.Severity) == "DANGER")
                            .ThenByDescending(x => x.CreatedAt)
                            .ToList();

                        return new NotificationGroupViewModel
                        {
                            Key = group.Key,
                            Label = GroupLabel(group.Key),
                            Icon = GroupIcon(group.Key),
                            Tone = GroupTone(group.Key),
                            Items = sortedItems,
                            CoopGroups = BuildCoopGroups(sortedItems)
                        };
                    })
                    .ToList()
            };
        }

        private static NotificationItemViewModel BuildItem(
            UserNotification notification,
            IReadOnlyDictionary<int, PhaseAssign> assignMap,
            IReadOnlyDictionary<int, ProjectFollowup> followupMap)
        {
            var sourceType = Source(notification.SourceType);
            var item = new NotificationItemViewModel
            {
                NotificationId = notification.NotificationId,
                SourceType = sourceType,
                Severity = SeverityClass(notification.Severity),
                SeverityText = SeverityText(notification.Severity),
                Title = CleanNotificationTitle(notification.Title),
                OwnerName = notification.RecipientEmployee?.EmpName ?? "-",
                OwnerAvatarPath = ProfileImage(notification.RecipientEmployee),
                DateText = FormatDateTime(notification.CreatedAt),
                StatusText = notification.IsRead ? "อ่านแล้ว" : "ยังไม่อ่าน",
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt
            };

            if (sourceType == "ASSIGN_DUE" && assignMap.TryGetValue(notification.SourceId, out var assign))
            {
                var phase = assign.Phase;
                var project = phase?.Project;
                item.Title = FirstText(assign.Role, phase?.PhaseDisplayName, notification.Title);
                item.ProjectName = ProjectDisplayName(project);
                item.CoopName = project?.Coop?.CoopName ?? "-";
                item.BaName = project?.BA?.EmpName ?? "-";
                item.BaAvatarPath = ProfileImage(project?.BA);
                item.OwnerName = assign.Employee?.EmpName ?? item.OwnerName;
                item.OwnerAvatarPath = ProfileImage(assign.Employee);
                item.DateText = FormatDateRange(assign.PlanStart ?? phase?.PlanStart, assign.PlanEnd ?? phase?.PlanEnd);
                item.StatusText = DisplayStatus(assign.WorkStatus);
                item.ExtraStatusText = DisplayStatus(phase?.PhaseStatus);
            }
            else if (sourceType == "FOLLOWUP_DUE" && followupMap.TryGetValue(notification.SourceId, out var followup))
            {
                var project = followup.Project;
                item.Title = FirstText(followup.TaskTitle, notification.Title);
                item.ProjectName = ProjectDisplayName(project);
                item.CoopName = project?.Coop?.CoopName ?? "-";
                item.BaName = project?.BA?.EmpName ?? "-";
                item.BaAvatarPath = ProfileImage(project?.BA);
                item.OwnerName = followup.Owner?.EmpName ?? item.OwnerName;
                item.OwnerAvatarPath = ProfileImage(followup.Owner);
                item.DateText = FormatDate(followup.NextFollowupDate);
                item.StatusText = DisplayStatus(followup.Status);
            }

            return item;
        }

        private static string ProfileImage(Employee? employee)
        {
            var path = employee?.LoginUser?.ProfileImagePath;
            if (string.IsNullOrWhiteSpace(path))
                return "/images/Profile/profile.png";

            path = path.Trim();
            if (path.StartsWith("~/", StringComparison.Ordinal)) path = path[1..];
            if (!path.StartsWith("/", StringComparison.Ordinal)) path = "/" + path.TrimStart('/');
            return path;
        }

        private static string GroupLabel(string? sourceType) => Source(sourceType) switch
        {
            "ASSIGN_DUE" => "Assigns",
            "FOLLOWUP_DUE" => "Followups",
            _ => string.IsNullOrWhiteSpace(sourceType) ? "อื่น ๆ" : sourceType!
        };

        private static string GroupIcon(string? sourceType) => Source(sourceType) switch
        {
            "ASSIGN_DUE" => "👥",
            "FOLLOWUP_DUE" => "📌",
            _ => "🔔"
        };

        private static string GroupTone(string? sourceType) => Source(sourceType) switch
        {
            "ASSIGN_DUE" => "assign",
            "FOLLOWUP_DUE" => "followup",
            _ => "default"
        };

        private static int GroupOrder(string? sourceType) => Source(sourceType) switch
        {
            "ASSIGN_DUE" => 1,
            "FOLLOWUP_DUE" => 2,
            _ => 99
        };

        private static string SeverityText(string? severity) => Source(severity) switch
        {
            "DANGER" => "เลยกำหนด",
            "WARNING" => "เสี่ยงล่าช้า",
            _ => string.IsNullOrWhiteSpace(severity) ? "-" : severity!.Trim()
        };

        private static string SeverityClass(string? severity) => Source(severity) == "DANGER"
            ? "danger"
            : "warning";

        private static string TextTone(string? value) => Source(value) switch
        {
            "OPEN" => "danger",
            "URGENT" => "danger",
            "HIGH" => "danger",
            "TODO" => "muted",
            "WIP" => "warning",
            "IN PROGRESS" => "warning",
            "IN_PROGRESS" => "warning",
            "WAIT TEST" => "info",
            "WAIT_TEST" => "info",
            "MEDIUM" => "warning",
            "LOW" => "success",
            "DONE" => "success",
            "FIXED" => "success",
            "PASS" => "success",
            "REJECT" => "muted",
            "RESOLVED" => "success",
            "ส่งงวดงานแล้ว" => "success",
            "อนุมัติจ่ายเงินแล้ว" => "success",
            "กำลังดำเนินการ" => "warning",
            "วางแผน" => "info",
            _ => "muted"
        };

        private static string ProjectDisplayName(Project? project)
        {
            if (project == null)
                return "-";

            var name = project.ProjectName?.Trim();
            return string.IsNullOrWhiteSpace(name) ? "-" : name;
        }

        private static string CleanNotificationTitle(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            var clean = value.Trim();
            var colonIndex = clean.IndexOf(':');
            return colonIndex >= 0 && colonIndex + 1 < clean.Length
                ? clean[(colonIndex + 1)..].Trim()
                : clean;
        }

        private static string FirstText(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "-";

        private static string DisplayStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            return value.Trim().Replace("_", " ");
        }

        private static string FormatDate(DateTime? value)
            => value.HasValue ? value.Value.ToString("dd MMM yyyy", ThaiCulture) : "-";

        private static string FormatDateTime(DateTime value)
            => value.ToString("dd MMM yyyy HH:mm", ThaiCulture);

        private static string FormatDateRange(DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue && endDate.HasValue)
                return $"{FormatDate(startDate)} - {FormatDate(endDate)}";

            if (startDate.HasValue)
                return $"เริ่ม {FormatDate(startDate)}";

            if (endDate.HasValue)
                return $"ครบกำหนด {FormatDate(endDate)}";

            return "-";
        }

        private static List<NotificationCoopGroupViewModel> BuildCoopGroups(IEnumerable<NotificationItemViewModel> items)
            => items
                .GroupBy(item => CoopGroupName(item.CoopName))
                .OrderBy(group => group.Key == "-" ? 1 : 0)
                .ThenBy(group => group.Key)
                .Select(group => new NotificationCoopGroupViewModel
                {
                    CoopName = group.Key,
                    Items = group.ToList()
                })
                .ToList();

        private static string CoopGroupName(string? value)
        {
            var clean = value?.Trim();
            return string.IsNullOrWhiteSpace(clean) ? "-" : clean;
        }

        private static string Source(string? value)
            => (value ?? "").Trim().ToUpperInvariant();

        private bool IsAdmin()
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            return string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);
        }
    }
}
