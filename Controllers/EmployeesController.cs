using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;
using System.Globalization;

namespace ProjectTracking.Controllers
{
    public class EmployeesController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly LineMessagingService _lineMessagingService;
        private readonly LineNotificationSettingsService _lineNotificationSettings;
        private readonly TelegramMessagingService _telegramMessagingService;
        private readonly TelegramNotificationSettingsService _telegramNotificationSettings;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmployeesController> _logger;

        public EmployeesController(
            AppDbContext context,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            IConfiguration configuration,
            ILogger<EmployeesController> logger)
        {
            _context = context;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _configuration = configuration;
            _logger = logger;
        }

        // ===========================
        // GET: /Employees
        // แสดงเฉพาะพนักงาน ACTIVE
        // ===========================
        [RequireMenu("Employees.Index")]
        public async Task<IActionResult> Index()
        {
            var employees = await _context.Employees
                .Include(e => e.LoginUser)
                .Where(e => e.Status == "ACTIVE")
                .OrderBy(e => e.Position)
                .ThenBy(e => e.EmpId)
                .ToListAsync();

            var employeeIds = employees.Select(x => x.EmpId).ToList();
            var linkedUserIds = employees.Where(x => x.LoginUserId.HasValue)
                .Select(x => x.LoginUserId!.Value).Distinct().ToList();
            var profileUsers = await _context.LoginUsers.AsNoTracking()
                .Where(x => linkedUserIds.Contains(x.UserId)
                    || (x.EmpId.HasValue && employeeIds.Contains(x.EmpId.Value)))
                .Select(x => new { x.UserId, x.EmpId, x.ProfileImagePath })
                .ToListAsync();
            ViewBag.EmployeeProfileImages = employees.ToDictionary(
                x => x.EmpId,
                x => profileUsers.FirstOrDefault(u => x.LoginUserId.HasValue && u.UserId == x.LoginUserId.Value)?.ProfileImagePath
                    ?? profileUsers.FirstOrDefault(u => u.EmpId == x.EmpId)?.ProfileImagePath
                    ?? "/images/Profile/profile.png");

            var lineLinkedEmpIds = await _context.LineRecipients
                .AsNoTracking()
                .Where(x => x.IsActive
                    && x.EmpId.HasValue
                    && x.RecipientType == "USER"
                    && x.LineUserId != null
                    && x.LineUserId != "")
                .Select(x => x.EmpId!.Value)
                .Distinct()
                .ToListAsync();

            var telegramLinkedEmpIds = await _context.TelegramRecipients
                .AsNoTracking()
                .Where(x => x.IsActive
                    && x.EmpId.HasValue
                    && x.RecipientType == "USER"
                    && x.TelegramChatId != null
                    && x.TelegramChatId != "")
                .Select(x => x.EmpId!.Value)
                .Distinct()
                .ToListAsync();

            ViewBag.LineLinkedEmpIds = lineLinkedEmpIds.ToHashSet();
            ViewBag.TelegramLinkedEmpIds = telegramLinkedEmpIds.ToHashSet();

            return View(employees);
        }

        // ===========================
        // GET: /Employees/Create
        // ===========================
        [RequireMenu("Employees.Create")]
        public IActionResult Create()
        {
            return View();
        }

        // ===========================
        // POST: /Employees/Create
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee employee)
        {
            if (ModelState.IsValid)
            {
                _context.Add(employee);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(employee);
        }

        // ===========================
        // GET: /Employees/Edit/5
        // ===========================
        [RequireMenu("Employees.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
            {
                return NotFound();
            }

            return View(employee);
        }

        // ===========================
        // POST: /Employees/Edit/5
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Employee employee)
        {
            if (id != employee.EmpId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                _context.Update(employee);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(employee);
        }

        // ===========================
        // GET: /Employees/Delete/5
        // ===========================
        [RequireMenu("Employees.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmpId == id);

            if (employee == null)
            {
                return NotFound();
            }

            return View(employee);
        }

        // ===========================
        // POST: /Employees/Delete/5
        // Soft Delete
        // ===========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee != null)
            {
                employee.Status = "INACTIVE";
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        [RequireMenu("Employees.LineOverdue")]
        public async Task<IActionResult> LineOverdue(string? type)
        {
            var items = await BuildLineOverdueSelectionItemsAsync();
            if (!string.IsNullOrWhiteSpace(type))
            {
                items = items
                    .Where(x => string.Equals(x.SourceType, type, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return View(new LineOverdueSelectionViewModel { Items = items });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Employees.LineOverdue")]
        public async Task<IActionResult> SendSelectedLineOverdue(List<string>? selectedKeys)
        {
            try
            {
                var sendLine = _lineMessagingService.IsConfigured
                    && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.LineOverdueManual, HttpContext.RequestAborted);
                var sendTelegram = _telegramMessagingService.IsConfigured
                    && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.LineOverdueManual, HttpContext.RequestAborted);

                if (!sendLine && !sendTelegram)
                {
                    TempData["Error"] = "ปิดการส่งแจ้งเตือนสำหรับหน้า Employees/LineOverdue อยู่ หรือยังไม่ได้ตั้งค่า token";
                    return RedirectToAction(nameof(LineOverdue));
                }

                if (selectedKeys == null || selectedKeys.Count == 0)
                {
                    TempData["Error"] = "กรุณาเลือกรายการที่ต้องการส่งแจ้งเตือน";
                    return RedirectToAction(nameof(LineOverdue));
                }

                var selected = selectedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var items = (await BuildLineOverdueSelectionItemsAsync())
                    .Where(x => selected.Contains(x.Key))
                    .ToList();

                var sentCount = 0;
                foreach (var item in items.Where(x => x.HasLineRecipient))
                {
                    foreach (var recipient in item.Recipients.Where(x => x.HasLineRecipient))
                    {
                        try
                        {
                            var targetUrl = ToRequestAbsoluteUrl(recipient.TargetUrl);
                            var deliveredCount = await SendChatNotificationToEmployeeSafelyAsync(
                                recipient.EmpId,
                                BuildSelectionLineTitle(item),
                                item.Message,
                                targetUrl,
                                sendLine,
                                sendTelegram,
                                item.Key);

                            sentCount += deliveredCount;
                            if (deliveredCount > 0)
                                await UpsertLineOverdueNotificationSendLogAsync(item, recipient);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Chat overdue send failed. EmpId={EmpId}, ItemKey={ItemKey}", recipient.EmpId, item.Key);
                            TempData["Error"] = "ส่งแจ้งเตือนไม่สำเร็จบางรายการ กรุณาตรวจสอบ token/API หรือดู log เพิ่มเติม";
                        }
                    }
                }

                if (sentCount > 0)
                    await _context.SaveChangesAsync();

                var skippedCount = items.Sum(x => x.Recipients.Count(r => !r.HasLineRecipient));
                TempData["Success"] = $"ส่งแจ้งเตือนแล้ว {sentCount} ปลายทาง จาก {items.Count} รายการ";
                if (skippedCount > 0)
                    TempData["Error"] = $"มี {skippedCount} ปลายทางที่ยังไม่ได้ผูก LINE หรือ Telegram";

                return RedirectToAction(nameof(LineOverdue));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat overdue send action failed.");
                TempData["Error"] = "ส่งแจ้งเตือนไม่สำเร็จ ระบบบันทึก error ไว้แล้ว กรุณาลองใหม่หรือตรวจสอบ log";
                return RedirectToAction(nameof(LineOverdue));
            }
        }

        private async Task<int> SendChatNotificationToEmployeeSafelyAsync(
            int empId,
            string title,
            string message,
            string? targetUrl,
            bool sendLine,
            bool sendTelegram,
            string itemKey)
        {
            var deliveredCount = 0;

            if (sendLine)
            {
                try
                {
                    deliveredCount += await _lineMessagingService.SendNotificationToEmployeeAsync(
                        empId,
                        title,
                        message,
                        targetUrl,
                        HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LINE overdue send failed. EmpId={EmpId}, ItemKey={ItemKey}", empId, itemKey);
                }
            }

            if (sendTelegram)
            {
                try
                {
                    deliveredCount += await _telegramMessagingService.SendNotificationToEmployeeAsync(
                        empId,
                        title,
                        message,
                        targetUrl,
                        HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telegram overdue send failed. EmpId={EmpId}, ItemKey={ItemKey}", empId, itemKey);
                }
            }

            return deliveredCount;
        }

        private string? ToRequestAbsoluteUrl(string? targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return null;

            if (Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
                return targetUrl;

            var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
            var path = targetUrl.StartsWith("/", StringComparison.Ordinal)
                ? targetUrl
                : $"/{targetUrl}";

            return $"{baseUrl}{path}";
        }

        private async Task<List<LineOverdueSelectionItemViewModel>> BuildLineOverdueSelectionItemsAsync()
        {
            var today = DateTime.Today;
            var riskDays = await GetOverdueRiskDaysAsync();
            var riskUntil = today.AddDays(riskDays);

            var employeeRows = await _context.Employees
                .AsNoTracking()
                .Select(x => new
                {
                    x.EmpId,
                    x.EmpName,
                    x.LoginUserId,
                    Username = x.LoginUser != null ? x.LoginUser.Username : null,
                    ProfileImagePath = x.LoginUser != null ? x.LoginUser.ProfileImagePath : null
                })
                .ToListAsync();

            var employees = employeeRows.ToDictionary(x => x.EmpId, x => x.EmpName ?? $"Employee #{x.EmpId}");
            var employeeUsers = employeeRows.ToDictionary(x => x.EmpId, x => x.LoginUserId);
            var employeeUsernames = employeeRows.ToDictionary(x => x.EmpId, x => string.IsNullOrWhiteSpace(x.Username) ? null : x.Username);
            var employeeAvatars = employeeRows.ToDictionary(x => x.EmpId, x => ProfileImage(x.ProfileImagePath));
            var lineEmpIds = await _context.LineRecipients
                .AsNoTracking()
                .Where(x => x.IsActive && x.EmpId.HasValue && x.LineUserId != null && x.LineUserId != "")
                .Select(x => x.EmpId!.Value)
                .Distinct()
                .ToListAsync();
            var telegramEmpIds = await _context.TelegramRecipients
                .AsNoTracking()
                .Where(x => x.IsActive && x.EmpId.HasValue && x.TelegramChatId != null && x.TelegramChatId != "")
                .Select(x => x.EmpId!.Value)
                .Distinct()
                .ToListAsync();
            var hasLine = lineEmpIds.Concat(telegramEmpIds).Distinct().ToHashSet();
            var sendStats = await LoadLineSendStatsAsync();

            var items = new List<LineOverdueSelectionItemViewModel>();

            var assigns = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(x => x.Phase!)
                    .ThenInclude(x => x.Project)
                        .ThenInclude(x => x!.Coop)
                .Where(x => x.Phase != null
                    && ((x.PlanEnd ?? x.Phase!.PlanEnd).HasValue)
                    && (x.PlanEnd ?? x.Phase!.PlanEnd)!.Value <= riskUntil)
                .ToListAsync();

            foreach (var row in assigns)
            {
                if (IsDone(row.WorkStatus, row.Phase?.PhaseStatus) || IsClosedPhaseForSelection(row.Phase?.PhaseStatus))
                    continue;

                var dueDate = row.PlanEnd ?? row.Phase?.PlanEnd;
                if (!TrySelectionDueState(dueDate, today, riskUntil, out var severity, out var stateText, out var overdueDays))
                    continue;

                var project = row.Phase?.Project;
                var title = string.IsNullOrWhiteSpace(row.Role) ? row.Phase?.PhaseName ?? $"Assign #{row.AssignId}" : row.Role!;
                var ownerName = EmployeeName(employees, row.EmpId);
                var baEmpId = project?.BaEmpId;
                var baName = EmployeeName(employees, baEmpId);
                var message = BuildSelectionMessage(
                    stateText,
                    project?.Coop?.CoopName,
                    ProjectNameForSelection(project),
                    title,
                    ownerName,
                    baName,
                    row.Phase?.PhaseOrder,
                    row.Phase?.PeriodOrder,
                    project?.StartDate,
                    project?.EndDate,
                    row.PlanStart ?? row.Phase?.PlanStart,
                    row.PlanEnd ?? row.Phase?.PlanEnd,
                    row.Phase?.PeriodEndDate,
                    row.Remark,
                    includeTitleLine: true);

                AddSelectionItem(items, employees, employeeUsers, employeeUsernames, hasLine, sendStats, "ASSIGN_DUE", "Phase Assign", row.AssignId, row.EmpId, "เจ้าของงาน", ownerName, EmployeeAvatar(employeeAvatars, row.EmpId), baName, EmployeeAvatar(employeeAvatars, baEmpId), severity, stateText, project?.Coop?.CoopName, ProjectNameForSelection(project), title, row.PlanStart ?? row.Phase?.PlanStart, row.PlanEnd ?? row.Phase?.PlanEnd, row.Phase?.PeriodEndDate, overdueDays, message, project != null ? $"/PhaseAssigns?projectId={project.ProjectId}&empId={row.EmpId}" : $"/PhaseAssigns?empId={row.EmpId}");

                if (baEmpId.HasValue)
                {
                    AddSelectionItem(items, employees, employeeUsers, employeeUsernames, hasLine, sendStats, "ASSIGN_DUE", "Phase Assign", row.AssignId, baEmpId.Value, "BA", ownerName, EmployeeAvatar(employeeAvatars, row.EmpId), baName, EmployeeAvatar(employeeAvatars, baEmpId), severity, stateText, project?.Coop?.CoopName, ProjectNameForSelection(project), title, row.PlanStart ?? row.Phase?.PlanStart, row.PlanEnd ?? row.Phase?.PlanEnd, row.Phase?.PeriodEndDate, overdueDays, message, project != null ? $"/PhaseAssigns?projectId={project.ProjectId}" : "/PhaseAssigns");
                }
            }

            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Where(x => x.EndDate.HasValue && x.EndDate.Value <= riskUntil)
                .ToListAsync();

            foreach (var row in issues)
            {
                if (IsIssueDoneForSelection(row.IssueStatus, row.DevStatus))
                    continue;

                if (!TrySelectionDueState(row.EndDate, today, riskUntil, out var severity, out var stateText, out var overdueDays))
                    continue;

                var ownerName = EmployeeName(employees, row.AssignTo);
                var baEmpId = row.Project?.BaEmpId;
                var baName = EmployeeName(employees, baEmpId);
                var message = BuildSelectionMessage(stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), row.IssueName, ownerName, baName, null, null, row.StartDate, row.EndDate, row.StartDate, row.EndDate, row.EndDate, null);

                AddSelectionItem(items, employees, employeeUsers, employeeUsernames, hasLine, sendStats, "ISSUE_DUE", "Issue", row.IssueId, row.AssignTo, "เจ้าของงาน", ownerName, EmployeeAvatar(employeeAvatars, row.AssignTo), baName, EmployeeAvatar(employeeAvatars, baEmpId), severity, stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), row.IssueName, row.StartDate, row.EndDate, row.EndDate, overdueDays, message, $"/ProjectIssues/DevDetails/{row.IssueId}");
                if (baEmpId.HasValue)
                    AddSelectionItem(items, employees, employeeUsers, employeeUsernames, hasLine, sendStats, "ISSUE_DUE", "Issue", row.IssueId, baEmpId.Value, "BA", ownerName, EmployeeAvatar(employeeAvatars, row.AssignTo), baName, EmployeeAvatar(employeeAvatars, baEmpId), severity, stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), row.IssueName, row.StartDate, row.EndDate, row.EndDate, overdueDays, message, $"/ProjectIssues/Details/{row.IssueId}");
            }

            var supports = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Where(x => x.EndDate.HasValue && x.EndDate.Value <= riskUntil)
                .ToListAsync();

            foreach (var row in supports)
            {
                if (IsSupportDoneForSelection(row.Status, row.DevStatus))
                    continue;

                if (!TrySelectionDueState(row.EndDate, today, riskUntil, out var severity, out var stateText, out var overdueDays))
                    continue;

                var title = string.IsNullOrWhiteSpace(row.OrderTitle) ? $"Support #{row.OrderId}" : row.OrderTitle!;
                var ownerName = EmployeeName(employees, row.AssignTo);
                var baEmpId = row.Project?.BaEmpId;
                var baName = EmployeeName(employees, baEmpId);
                var message = BuildSelectionMessage(stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), title, ownerName, baName, null, null, row.StartDate, row.EndDate, row.StartDate, row.EndDate, row.EndDate, null);

                if (row.AssignTo.HasValue)
                    AddSelectionItem(items, employees, employeeUsers, employeeUsernames, hasLine, sendStats, "SUPPORT_DUE", "Support", row.OrderId, row.AssignTo.Value, "เจ้าของงาน", ownerName, EmployeeAvatar(employeeAvatars, row.AssignTo), baName, EmployeeAvatar(employeeAvatars, baEmpId), severity, stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), title, row.StartDate, row.EndDate, row.EndDate, overdueDays, message, $"/SupportOrdersDev/Details/{row.OrderId}");

                if (baEmpId.HasValue)
                    AddSelectionItem(items, employees, employeeUsers, employeeUsernames, hasLine, sendStats, "SUPPORT_DUE", "Support", row.OrderId, baEmpId.Value, "BA", ownerName, EmployeeAvatar(employeeAvatars, row.AssignTo), baName, EmployeeAvatar(employeeAvatars, baEmpId), severity, stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), title, row.StartDate, row.EndDate, row.EndDate, overdueDays, message, $"/SupportOrders/Details/{row.OrderId}");
            }

            var followups = await _context.ProjectFollowups
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Where(x => x.OwnerEmpId.HasValue
                    && x.NextFollowupDate.HasValue
                    && x.NextFollowupDate.Value <= riskUntil)
                .ToListAsync();

            foreach (var row in followups)
            {
                if (!IsFollowupOpenForSelection(row.Status))
                    continue;

                if (!TrySelectionDueState(row.NextFollowupDate, today, riskUntil, out var severity, out var stateText, out var overdueDays))
                    continue;

                var title = string.IsNullOrWhiteSpace(row.TaskTitle) ? $"Followup #{row.FollowupId}" : row.TaskTitle;
                var ownerName = EmployeeName(employees, row.OwnerEmpId);
                var baEmpId = row.Project?.BaEmpId;
                var baName = EmployeeName(employees, baEmpId);
                var startDate = row.LastContactDate ?? row.CreatedAt;
                var message = BuildSelectionMessage(stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), title, ownerName, baName, null, null, row.Project?.StartDate, row.Project?.EndDate, startDate, row.NextFollowupDate, row.NextFollowupDate, row.PartnerName);

                if (row.OwnerEmpId.HasValue)
                    AddSelectionItem(items, employees, employeeUsers, employeeUsernames, hasLine, sendStats, "FOLLOWUP_DUE", "Followup", row.FollowupId, row.OwnerEmpId.Value, "เจ้าของงาน", ownerName, EmployeeAvatar(employeeAvatars, row.OwnerEmpId), baName, EmployeeAvatar(employeeAvatars, baEmpId), severity, stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), title, startDate, row.NextFollowupDate, row.NextFollowupDate, overdueDays, message, $"/Followups/Details/{row.FollowupId}");

                if (baEmpId.HasValue)
                    AddSelectionItem(items, employees, employeeUsers, employeeUsernames, hasLine, sendStats, "FOLLOWUP_DUE", "Followup", row.FollowupId, baEmpId.Value, "BA", ownerName, EmployeeAvatar(employeeAvatars, row.OwnerEmpId), baName, EmployeeAvatar(employeeAvatars, baEmpId), severity, stateText, row.Project?.Coop?.CoopName, ProjectNameForSelection(row.Project), title, startDate, row.NextFollowupDate, row.NextFollowupDate, overdueDays, message, $"/Followups/Details/{row.FollowupId}");
            }

            return items
                .OrderBy(x => LineOverdueTypeRank(x.SourceType))
                .ThenByDescending(x => x.Recipients.Any(r => r.HasLineRecipient))
                .ThenBy(x => x.EndDate ?? x.DueDate ?? DateTime.MaxValue)
                .ThenBy(x => x.CoopName)
                .ThenBy(x => x.ProjectName)
                .ThenByDescending(x => x.Severity == "DANGER")
                .ThenByDescending(x => x.OverdueDays)
                .ThenBy(x => x.DueDate)
                .ToList();
        }

        private static int LineOverdueTypeRank(string? sourceType)
        {
            return sourceType switch
            {
                "ASSIGN_DUE" => 1,
                "ISSUE_DUE" => 2,
                "SUPPORT_DUE" => 3,
                "FOLLOWUP_DUE" => 4,
                _ => 99
            };
        }

        private static void AddSelectionItem(
            IList<LineOverdueSelectionItemViewModel> items,
            IReadOnlyDictionary<int, string> employees,
            IReadOnlyDictionary<int, int?> employeeUsers,
            IReadOnlyDictionary<int, string?> employeeUsernames,
            ISet<int> hasLine,
            IReadOnlyDictionary<string, LineSendStat> sendStats,
            string sourceType,
            string sourceLabel,
            int sourceId,
            int recipientEmpId,
            string recipientRole,
            string ownerName,
            string ownerAvatarPath,
            string baName,
            string baAvatarPath,
            string severity,
            string stateText,
            string? coopName,
            string projectName,
            string title,
            DateTime? startDate,
            DateTime? endDate,
            DateTime? dueDate,
            int overdueDays,
            string message,
            string targetUrl)
        {
            if (recipientEmpId <= 0)
                return;

            var key = $"{sourceType}:{sourceId}";
            var existingItem = items.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existingItem != null)
            {
                AddRecipient(existingItem, employees, employeeUsers, employeeUsernames, hasLine, recipientEmpId, recipientRole, targetUrl);
                return;
            }

            sendStats.TryGetValue(key, out var sendStat);

            var item = new LineOverdueSelectionItemViewModel
            {
                Key = key,
                SourceType = sourceType,
                SourceLabel = sourceLabel,
                SourceId = sourceId,
                OwnerName = ownerName,
                OwnerAvatarPath = ownerAvatarPath,
                BaName = baName,
                BaAvatarPath = baAvatarPath,
                Severity = severity,
                StateText = stateText,
                CoopName = string.IsNullOrWhiteSpace(coopName) ? "-" : coopName,
                ProjectName = projectName,
                Title = title,
                StartDate = startDate,
                EndDate = endDate,
                DueDate = dueDate,
                OverdueDays = overdueDays,
                LineSendCount = sendStat?.Count ?? 0,
                LastLineSentAt = sendStat?.LastSentAt,
                Message = message,
                TargetUrl = targetUrl
            };
            AddRecipient(item, employees, employeeUsers, employeeUsernames, hasLine, recipientEmpId, recipientRole, targetUrl);
            items.Add(item);
        }

        private static void AddRecipient(
            LineOverdueSelectionItemViewModel item,
            IReadOnlyDictionary<int, string> employees,
            IReadOnlyDictionary<int, int?> employeeUsers,
            IReadOnlyDictionary<int, string?> employeeUsernames,
            ISet<int> hasLine,
            int recipientEmpId,
            string recipientRole,
            string targetUrl)
        {
            if (recipientEmpId <= 0 || item.Recipients.Any(x => x.EmpId == recipientEmpId))
                return;

            item.Recipients.Add(new LineOverdueRecipientViewModel
            {
                EmpId = recipientEmpId,
                UserId = employeeUsers.TryGetValue(recipientEmpId, out var userId) ? userId : null,
                Username = employeeUsernames.TryGetValue(recipientEmpId, out var username) ? username : null,
                Name = EmployeeName(employees, recipientEmpId),
                Role = recipientRole,
                HasLineRecipient = hasLine.Contains(recipientEmpId),
                TargetUrl = targetUrl
            });
        }

        private async Task<Dictionary<string, LineSendStat>> LoadLineSendStatsAsync()
        {
            var sourceTypes = new[] { "ASSIGN_DUE", "ISSUE_DUE", "SUPPORT_DUE", "FOLLOWUP_DUE" };
            var rows = await _context.UserNotifications
                .AsNoTracking()
                .Where(x => sourceTypes.Contains(x.SourceType) && x.RecipientEmpId.HasValue)
                .GroupBy(x => new { x.SourceType, x.SourceId })
                .Select(x => new
                {
                    x.Key.SourceType,
                    x.Key.SourceId,
                    Count = x.Count(),
                    LastSentAt = x.Max(n => n.UpdatedAt)
                })
                .ToListAsync();

            return rows.ToDictionary(
                x => $"{x.SourceType}:{x.SourceId}",
                x => new LineSendStat(x.Count, x.LastSentAt),
                StringComparer.OrdinalIgnoreCase);
        }

        private async Task UpsertLineOverdueNotificationSendLogAsync(LineOverdueSelectionItemViewModel item, LineOverdueRecipientViewModel recipient)
        {
            var now = DateTime.Now;
            var notification = await _context.UserNotifications
                .FirstOrDefaultAsync(x => x.SourceType == item.SourceType
                    && x.SourceId == item.SourceId
                    && x.RecipientEmpId == recipient.EmpId);

            if (notification == null)
            {
                notification = new UserNotification
                {
                    RecipientEmpId = recipient.EmpId,
                    SourceType = item.SourceType,
                    SourceId = item.SourceId,
                    CreatedAt = now
                };
                _context.UserNotifications.Add(notification);
            }

            notification.RecipientUserId = recipient.UserId;
            notification.Title = Trim(BuildSelectionLineTitle(item), 255);
            notification.Message = item.Message;
            notification.TargetUrl = Trim(recipient.TargetUrl ?? "", 500);
            notification.Severity = item.Severity;
            notification.IsRead = true;
            notification.ReadAt = now;
            notification.IsResolved = true;
            notification.ResolvedAt = now;
            notification.UpdatedAt = now;
        }

        private static bool TrySelectionDueState(
            DateTime? dueDate,
            DateTime today,
            DateTime riskUntil,
            out string severity,
            out string stateText,
            out int overdueDays)
        {
            severity = "WARNING";
            stateText = "";
            overdueDays = 0;

            if (!dueDate.HasValue)
                return false;

            var due = dueDate.Value.Date;
            if (due > riskUntil)
                return false;

            if (due < today)
            {
                severity = "DANGER";
                overdueDays = (today - due).Days;
                stateText = $"ล่าช้า {overdueDays:N0} วัน";
                return true;
            }

            if (due == today)
            {
                stateText = "ครบกำหนดวันนี้";
                return true;
            }

            stateText = $"เสี่ยงล่าช้า เหลือ {(due - today).Days:N0} วัน";
            return true;
        }

        private static string BuildSelectionLineTitle(LineOverdueSelectionItemViewModel item)
        {
            var prefix = string.Equals(item.Severity, "DANGER", StringComparison.OrdinalIgnoreCase)
                ? "งานล่าช้า"
                : "งานเสี่ยงล่าช้า";

            return $"{prefix} {LineOverdueTypeTitle(item.SourceType, item.SourceLabel)}:";
        }

        private static string LineOverdueTypeTitle(string? sourceType, string fallback)
            => sourceType switch
            {
                "ASSIGN_DUE" => "Assigns",
                "ISSUE_DUE" => "Issues",
                "SUPPORT_DUE" => "Support",
                "FOLLOWUP_DUE" => "Followup",
                _ => fallback
            };

        private static string BuildSelectionMessage(
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
            string? remark,
            bool includeTitleLine = true)
        {
            var rows = new List<string>
            {
                $"สถานะ: {stateText}",
                $"สหกรณ์: {(string.IsNullOrWhiteSpace(coopName) ? "-" : coopName)}",
                $"Project: {projectName}"
            };

            if (includeTitleLine)
                rows.Add($"งาน: {(string.IsNullOrWhiteSpace(title) ? "-" : title)}");

            rows.Add($"เจ้าของงาน: {ownerName}");
            rows.Add($"BA: {baName}");

            if (phaseOrder.HasValue || periodOrder.HasValue)
                rows.Add($"ส่วน / งวด: ส่วนที่ {(phaseOrder?.ToString() ?? "-")} / งวดที่ {(periodOrder?.ToString() ?? "-")}");

            rows.Add($"Project Period: {DateText(projectStart)} - {DateText(projectEnd)}");
            rows.Add($"Plan: {DateText(planStart)} - {DateText(planEnd)}");
            rows.Add($"กำหนดส่ง: {DateText(dueDate)}");
            rows.Add($"Remark: {(string.IsNullOrWhiteSpace(remark) ? "-" : remark)}");
            return string.Join("\n", rows);
        }

        private static string EmployeeName(IReadOnlyDictionary<int, string> employees, int? empId)
        {
            if (!empId.HasValue || empId.Value <= 0)
                return "-";

            return employees.TryGetValue(empId.Value, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : $"Employee #{empId.Value}";
        }

        private static string EmployeeAvatar(IReadOnlyDictionary<int, string> employeeAvatars, int? empId)
        {
            if (!empId.HasValue || empId.Value <= 0)
                return "/images/Profile/profile.png";

            return employeeAvatars.TryGetValue(empId.Value, out var avatar) && !string.IsNullOrWhiteSpace(avatar)
                ? avatar
                : "/images/Profile/profile.png";
        }

        private static string ProfileImage(string? profileImagePath)
            => ProfileImagePathResolver.Normalize(profileImagePath);

        private static string ProjectNameForSelection(Project? project)
        {
            if (project == null)
                return "-";

            return string.IsNullOrWhiteSpace(project.ProjectName)
                ? "-"
                : project.ProjectName;
        }

        private static bool IsClosedPhaseForSelection(string? phaseStatus)
        {
            var normalized = (phaseStatus ?? "").Trim();
            return normalized is "ส่งงวดงานแล้ว" or "อนุมัติจ่ายเงินแล้ว" or "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
        }

        private static bool IsIssueDoneForSelection(string? issueStatus, string? devStatus)
        {
            var issue = Norm(issueStatus);
            return issue is "PASS" or "REJECT" or "DONE" or "CLOSED" or "RESOLVED";
        }

        private static bool IsSupportDoneForSelection(string? status, string? devStatus)
        {
            var normalized = Norm(status);
            return normalized is "PASS" or "REJECT" or "DONE" or "CLOSED" or "RESOLVED";
        }

        private static bool IsFollowupOpenForSelection(string? status)
        {
            return Norm(status) == "OPEN";
        }

        private static bool IsDone(string? workStatus, string? phaseStatus)
        {
            var work = Norm(workStatus);
            var phase = Norm(phaseStatus);
            return work == "DONE"
                || phase is "DONE" or "ส่งงวดงานแล้ว" or "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
        }

        private static string DateText(DateTime? value)
            => value.HasValue ? value.Value.ToString("dd MMM yyyy", ThaiCulture) : "-";

        private static string Trim(string value, int maxLength)
            => value.Length <= maxLength ? value : value[..maxLength];

        private static string Norm(string? value)
            => (value ?? "").Trim().ToUpperInvariant();

        private async Task<int> GetOverdueRiskDaysAsync()
        {
            var value = await _context.SystemConfigs
                .AsNoTracking()
                .Where(x => x.ConfigKey == "OVERDUE_NOTIFICATION_RISK_DAYS")
                .Select(x => x.ConfigValue)
                .FirstOrDefaultAsync();

            if (int.TryParse(value, out var riskDays))
                return Math.Clamp(riskDays, 0, 30);

            return Math.Clamp(_configuration.GetValue<int?>("OVERDUE_NOTIFICATION_RISK_DAYS") ?? 7, 0, 30);
        }

        private static readonly CultureInfo ThaiCulture = new("th-TH");

        private sealed record LineSendStat(int Count, DateTime LastSentAt);
    }
}
