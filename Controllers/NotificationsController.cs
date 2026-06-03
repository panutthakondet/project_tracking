using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    public class NotificationsController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly OverdueNotificationService _notificationService;

        public NotificationsController(
            AppDbContext context,
            OverdueNotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index(string? severity = null, bool includeResolved = false)
        {
            var query = ApplyVisibility(_context.UserNotifications
                .AsNoTracking()
                .Include(x => x.RecipientEmployee)
                .AsQueryable());

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

            ViewBag.SelectedSeverity = severity ?? "";
            ViewBag.IncludeResolved = includeResolved;
            ViewBag.IsAdmin = IsAdmin();

            return View(notifications);
        }

        [HttpGet]
        public async Task<IActionResult> Open(int id)
        {
            var notification = await ApplyVisibility(_context.UserNotifications.AsQueryable())
                .FirstOrDefaultAsync(x => x.NotificationId == id);

            if (notification == null)
                return RedirectToAction(nameof(Index));

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.Now;
                notification.UpdatedAt = DateTime.Now;
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

                var activeCount = await _context.UserNotifications
                    .AsNoTracking()
                    .CountAsync(x => !x.IsResolved);

                var unreadCount = await _context.UserNotifications
                    .AsNoTracking()
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
            var isDone = status == "DONE";
            var isDueOrRisk = hasEndDate && order.EndDate!.Value.Date <= today.AddDays(3);
            var shouldNotify = hasAssignee && hasEndDate && isDueOrRisk && !isDone;

            var lines = new List<string>
            {
                $"Support order #{order.OrderId}",
                $"Project: {order.Project?.ProjectName ?? "-"}",
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
            var notifications = await ApplyVisibility(_context.UserNotifications.AsQueryable())
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

        private bool IsAdmin()
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            return string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatDate(DateTime? value)
            => value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    }
}
