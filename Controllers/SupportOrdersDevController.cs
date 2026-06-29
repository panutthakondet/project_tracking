using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using System.Globalization;

namespace ProjectTracking.Controllers
{
    public class SupportOrdersDevController : Controller
    {
        private readonly AppDbContext _context;
        private readonly LineMessagingService _lineMessagingService;
        private readonly LineNotificationSettingsService _lineNotificationSettings;
        private readonly TelegramMessagingService _telegramMessagingService;
        private readonly TelegramNotificationSettingsService _telegramNotificationSettings;
        private readonly OverdueNotificationService _notificationService;
        private readonly ILogger<SupportOrdersDevController> _logger;
        private const string FilterProjectIdKey = "SupportOrdersDev.Filter.ProjectId";
        private const string FilterEmpIdKey = "SupportOrdersDev.Filter.EmpId";
        private const string FilterStatusKey = "SupportOrdersDev.Filter.Status";
        private static readonly (string Value, string Text)[] SupportDevStatuses =
        {
            ("WIP", "WIP - กำลังแก้"),
            ("FIXED", "FIXED - แก้เสร็จ / ส่งตรวจ")
        };
        private static readonly CultureInfo ThaiCulture = new("th-TH");

        public SupportOrdersDevController(
            AppDbContext context,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            OverdueNotificationService notificationService,
            ILogger<SupportOrdersDevController> logger)
        {
            _context = context;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _notificationService = notificationService;
            _logger = logger;
        }

        // =========================
        // Programmer Order List
        // =========================
        [RequireMenu("SupportOrdersDev.Index")]
        public async Task<IActionResult> Index(int? projectId, int? empId, string? status)
        {
            (projectId, empId) = ResolveIndexFilters(projectId, empId);
            var selectedStatus = ResolveStatusFilter(status);

            var query = _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Employee)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(o => o.ProjectId == projectId.Value);
            }

            if (empId.HasValue && empId.Value > 0)
            {
                query = query.Where(o => o.AssignTo == empId.Value);
            }

            if (!string.IsNullOrWhiteSpace(selectedStatus))
            {
                query = query.Where(o => o.DevStatus == selectedStatus);
            }

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var projectList = await _context.Projects
                    .AsNoTracking()
                    .Include(p => p.Coop)
                    .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                    .ThenBy(p => p.ProjectName)
                    .ToListAsync();

            ViewBag.Projects = projectList;
            ViewBag.ProjectList = new SelectList(
                projectList,
                "ProjectId",
                "ProjectDisplayName",
                projectId
            );

            ViewBag.SelectedProject = projectId.HasValue && projectId.Value > 0
                ? projectList.FirstOrDefault(p => p.ProjectId == projectId.Value)
                : null;

            ViewBag.SelectedEmpId = empId;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedStatus = selectedStatus;
            ViewBag.StatusList = BuildStatusFilterList(SupportDevStatuses, selectedStatus);
            ViewBag.EmpList = await BuildOwnerListAsync(projectId, empId);

            return View(orders);
        }

        // =========================
        // View Details
        // =========================
        [RequireMenu("SupportOrdersDev.Details")]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Employee)
                .Include(o => o.FixImages)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == id)
                .ToListAsync();

            ViewBag.GitHistories = await _context.ProjectSupportOrderGitHistories
                .AsNoTracking()
                .Where(x => x.OrderId == order.OrderId)
                .OrderByDescending(x => x.EntryDate)
                .ToListAsync();

            return View(order);
        }

        // =========================
        // Edit (Programmer Fix)
        // =========================
        [RequireMenu("SupportOrdersDev.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var order = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Employee)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == id)
                .ToListAsync();

            order.FixImages = await _context.ProjectSupportFixImages
                .Where(x => x.OrderId == id)
                .ToListAsync();

            ViewBag.CurrentDevStatus = order.DevStatus;
            ViewBag.DevStatusList = GetDevStatusList(order.DevStatus);
            ViewBag.GitHistories = await _context.ProjectSupportOrderGitHistories
                .AsNoTracking()
                .Where(x => x.OrderId == order.OrderId)
                .OrderByDescending(x => x.EntryDate)
                .ToListAsync();

            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SupportOrdersDev.Edit")]
        public async Task<IActionResult> Edit(
            int id,
            ProjectSupportOrder order,
            List<IFormFile> afterFiles,
            List<int> deleteImageIds,
            List<string>? gitTypes,
            List<string>? gitIds)
        {
            var dbOrder = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Employee)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (dbOrder == null)
                return NotFound();

            ModelState.Remove(nameof(ProjectSupportOrder.Project));
            ModelState.Remove(nameof(ProjectSupportOrder.Employee));
            ModelState.Remove(nameof(ProjectSupportOrder.Images));
            ModelState.Remove(nameof(ProjectSupportOrder.FixImages));
            ModelState.Remove(nameof(ProjectSupportOrder.OrderTitle));
            ModelState.Remove(nameof(ProjectSupportOrder.OrderDetail));
            ModelState.Remove(nameof(ProjectSupportOrder.Priority));
            ModelState.Remove(nameof(ProjectSupportOrder.Status));
            ModelState.Remove(nameof(ProjectSupportOrder.AssignTo));
            ModelState.Remove(nameof(ProjectSupportOrder.StartDate));
            ModelState.Remove(nameof(ProjectSupportOrder.EndDate));
            ModelState.Remove(nameof(ProjectSupportOrder.CreatedAt));
            ModelState.Remove(nameof(ProjectSupportOrder.CreatedBy));

            var currentDbDevStatus = NormalizeSupportDevStatus(dbOrder.DevStatus);
            var canAddGitHistory = currentDbDevStatus == "WIP";
            var nextDevStatus = NormalizeSupportDevStatus(order.DevStatus);
            var gitHistoryRows = canAddGitHistory
                ? BuildGitHistoryRows(gitTypes, gitIds, ModelState)
                : new List<(string GitType, string GitId)>();

            if (!ModelState.IsValid)
            {
                dbOrder.Images = await _context.ProjectSupportImages
                    .Where(x => x.OrderId == id)
                    .ToListAsync();

                dbOrder.FixImages = await _context.ProjectSupportFixImages
                    .Where(x => x.OrderId == id)
                    .ToListAsync();

                ViewBag.CurrentDevStatus = dbOrder.DevStatus;
                ViewBag.DevStatusList = GetDevStatusList(order.DevStatus);
                ViewBag.GitHistories = await _context.ProjectSupportOrderGitHistories
                    .AsNoTracking()
                    .Where(x => x.OrderId == dbOrder.OrderId)
                    .OrderByDescending(x => x.EntryDate)
                    .ToListAsync();
                dbOrder.DevStatus = order.DevStatus;
                dbOrder.DevDetail = order.DevDetail;
                return View(dbOrder);
            }

            var shouldNotifyBaFixed = nextDevStatus == "FIXED";
            dbOrder.DevStatus = nextDevStatus;
            dbOrder.DevDetail = order.DevDetail;

            if (gitHistoryRows.Count > 0)
            {
                var entryDate = DateTime.Now;
                var currentEmpId = await GetCurrentEntryIdAsync();

                foreach (var row in gitHistoryRows)
                {
                    _context.ProjectSupportOrderGitHistories.Add(new ProjectSupportOrderGitHistory
                    {
                        OrderId = dbOrder.OrderId,
                        GitType = row.GitType,
                        GitId = row.GitId,
                        EntryDate = entryDate,
                        CreatedByEmpId = currentEmpId
                    });
                }
            }

            var folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot/uploads/support",
                id.ToString()
            );

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            if (afterFiles != null && afterFiles.Count > 0)
            {
                foreach (var file in afterFiles)
                {
                    if (file.Length == 0) continue;

                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    _context.ProjectSupportFixImages.Add(new ProjectSupportFixImage
                    {
                        OrderId = id,
                        FilePath = $"/uploads/support/{id}/{fileName}",
                        ImageType = "AFTER"
                    });
                }
            }

            // Delete selected images (same style as BA)
            if (deleteImageIds != null && deleteImageIds.Any())
            {
                var images = await _context.ProjectSupportFixImages
                    .Where(x => deleteImageIds.Contains(x.ImageId))
                    .ToListAsync();

                foreach (var img in images)
                {
                    var filePath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        img.FilePath.TrimStart('/')
                    );

                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                }

                _context.ProjectSupportFixImages.RemoveRange(images);
            }

            await _context.SaveChangesAsync();

            await SyncNotificationsSafelyAsync();
            if (shouldNotifyBaFixed)
                await SendFixedSupportTelegramToBaSafelyAsync(dbOrder.OrderId);

            return RedirectToAction(nameof(Index), new { projectId = dbOrder.ProjectId });
        }

        private async Task SyncNotificationsSafelyAsync()
        {
            try
            {
                await _notificationService.SyncAsync(HttpContext.RequestAborted);
            }
            catch
            {
                // Notification sync should not block the main save flow.
            }
        }

        private async Task SendFixedSupportTelegramToBaSafelyAsync(int orderId)
        {
            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.SupportOrdersFixed, HttpContext.RequestAborted);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.SupportOrdersFixed, HttpContext.RequestAborted);

            if (!sendLine && !sendTelegram)
                return;

            try
            {
                var order = await _context.ProjectSupportOrders
                    .AsNoTracking()
                    .Include(x => x.Employee)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.Coop)
                    .Include(x => x.Project)
                        .ThenInclude(p => p!.BA)
                    .FirstOrDefaultAsync(x => x.OrderId == orderId);

                var baEmpId = order?.Project?.BaEmpId;
                if (order == null || !baEmpId.HasValue || baEmpId.Value <= 0)
                    return;

                var message = BuildFixedSupportTelegramMessage(order);
                await SendChatNotificationToEmployeeSafelyAsync(
                    baEmpId.Value,
                    "แจ้ง Support แก้เสร็จ:",
                    message,
                    $"/SupportOrders/Details/{order.OrderId}",
                    sendLine,
                    sendTelegram,
                    "fixed support",
                    order.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send fixed support Telegram notification failed. OrderId={OrderId}", orderId);
            }
        }

        private async Task SendChatNotificationToEmployeeSafelyAsync(
            int empId,
            string title,
            string message,
            string targetUrl,
            bool sendLine,
            bool sendTelegram,
            string context,
            int sourceId)
        {
            if (sendLine)
            {
                try
                {
                    await _lineMessagingService.SendNotificationToEmployeeAsync(
                        empId,
                        title,
                        message,
                        targetUrl,
                        HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LINE notification failed. Context={Context}, SourceId={SourceId}, EmpId={EmpId}", context, sourceId, empId);
                }
            }

            if (sendTelegram)
            {
                try
                {
                    await _telegramMessagingService.SendNotificationToEmployeeAsync(
                        empId,
                        title,
                        message,
                        targetUrl,
                        HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telegram notification failed. Context={Context}, SourceId={SourceId}, EmpId={EmpId}", context, sourceId, empId);
                }
            }
        }

        private static string BuildFixedSupportTelegramMessage(ProjectSupportOrder order)
        {
            var project = order.Project;
            var rows = new List<string>
            {
                $"สหกรณ์: {TextOrDash(project?.Coop?.CoopName)}",
                $"Project: {ProjectNameForTelegram(project)}",
                $"Support: {TextOrDash(order.OrderTitle)}",
                $"เจ้าของงาน: {TextOrDash(order.Employee?.EmpName)}",
                $"BA: {TextOrDash(project?.BA?.EmpName)}",
                $"Dev Status: {TextOrDash(order.DevStatus)}",
                $"รายละเอียดการแก้ไข: {TextOrDash(order.DevDetail)}",
                $"วันที่เริ่ม: {DateText(order.StartDate)}",
                $"วันที่สิ้นสุด: {DateText(order.EndDate)}"
            };

            return string.Join("\n", rows);
        }

        private static string ProjectNameForTelegram(Project? project)
            => string.IsNullOrWhiteSpace(project?.ProjectName) ? "-" : project.ProjectName.Trim();

        private static string TextOrDash(string? value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

        private static string DateText(DateTime? value)
            => value.HasValue ? value.Value.ToString("dd MMM yyyy", ThaiCulture) : "-";

        private static List<(string GitType, string GitId)> BuildGitHistoryRows(
            List<string>? gitTypes,
            List<string>? gitIds,
            Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
        {
            var rows = new List<(string GitType, string GitId)>();
            var count = Math.Max(gitTypes?.Count ?? 0, gitIds?.Count ?? 0);

            for (var i = 0; i < count; i++)
            {
                var gitId = i < (gitIds?.Count ?? 0) ? (gitIds![i] ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(gitId))
                    continue;

                if (gitId.Length > 80)
                {
                    modelState.AddModelError("gitIds", "Git ID ต้องไม่เกิน 80 ตัวอักษร");
                    continue;
                }

                var gitType = NormalizeGitType(i < (gitTypes?.Count ?? 0) ? gitTypes![i] : null);
                if (gitType == null)
                {
                    modelState.AddModelError("gitTypes", "ประเภท Git ต้องเป็น GITHUB หรือ GITLAB เท่านั้น");
                    continue;
                }

                rows.Add((gitType, gitId));
            }

            return rows;
        }

        private static string? NormalizeGitType(string? gitType)
        {
            var value = (gitType ?? "").Trim().ToUpperInvariant();
            return value is "GITHUB" or "GITLAB" ? value : null;
        }

        private async Task<int?> GetCurrentEntryIdAsync()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue) return null;

            var empId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.LoginUserId == userId.Value)
                .Select(e => (int?)e.EmpId)
                .FirstOrDefaultAsync();

            if (empId.HasValue) return empId;

            return await _context.LoginUsers
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => u.EmpId)
                .FirstOrDefaultAsync();
        }

        private async Task<SelectList> BuildOwnerListAsync(int? projectId, int? selectedEmpId)
        {
            var query = _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(o => o.Employee)
                .Where(o => o.AssignTo.HasValue
                    && o.Employee != null
                    && o.Employee.EmpName != null
                    && o.Employee.EmpName != "");

            if (projectId.HasValue && projectId.Value > 0)
            {
                query = query.Where(o => o.ProjectId == projectId.Value);
            }

            var owners = await query
                .Select(o => new
                {
                    EmpId = o.AssignTo!.Value,
                    EmpName = o.Employee!.EmpName!
                })
                .Distinct()
                .OrderBy(x => x.EmpName)
                .ToListAsync();

            return new SelectList(owners, "EmpId", "EmpName", selectedEmpId);
        }

        private (int? ProjectId, int? EmpId) ResolveIndexFilters(int? projectId, int? empId)
        {
            var hasProjectQuery = Request.Query.ContainsKey("projectId");
            var hasEmpQuery = Request.Query.ContainsKey("empId");
            var storedProjectId = HttpContext.Session.GetInt32(FilterProjectIdKey);
            var projectChangedByQuery = false;

            if (!hasProjectQuery)
            {
                projectId = storedProjectId;
            }
            else if (projectId.HasValue && projectId.Value > 0)
            {
                projectChangedByQuery = storedProjectId.HasValue && storedProjectId.Value != projectId.Value;
                HttpContext.Session.SetInt32(FilterProjectIdKey, projectId.Value);
            }
            else
            {
                HttpContext.Session.Remove(FilterProjectIdKey);
                HttpContext.Session.Remove(FilterEmpIdKey);
                empId = null;
            }

            if (!projectId.HasValue || projectId.Value <= 0)
            {
                HttpContext.Session.Remove(FilterEmpIdKey);
                return (projectId, null);
            }

            if (!hasEmpQuery)
            {
                if (projectChangedByQuery)
                {
                    HttpContext.Session.Remove(FilterEmpIdKey);
                    empId = null;
                }
                else
                {
                    empId = HttpContext.Session.GetInt32(FilterEmpIdKey);
                }
            }
            else
            {
                if (empId.HasValue && empId.Value > 0)
                {
                    HttpContext.Session.SetInt32(FilterEmpIdKey, empId.Value);
                }
                else
                {
                    HttpContext.Session.Remove(FilterEmpIdKey);
                    empId = null;
                }
            }

            return (projectId, empId);
        }

        private static string NormalizeSupportDevStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            if (normalized == "IN_PROGRESS" || normalized == "TODO" || normalized == "DOING" || normalized == "BLOCK")
                return "WIP";
            return normalized == "FIXED" ? "FIXED" : "WIP";
        }

        private static string NormalizeIndexSupportDevStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            if (normalized == "IN_PROGRESS" || normalized == "TODO" || normalized == "DOING" || normalized == "BLOCK")
                return "WIP";
            return normalized == "FIXED" || normalized == "WIP" ? normalized : "";
        }

        private string ResolveStatusFilter(string? status)
        {
            if (!Request.Query.ContainsKey("status"))
            {
                return NormalizeIndexSupportDevStatus(HttpContext.Session.GetString(FilterStatusKey));
            }

            var selectedStatus = NormalizeIndexSupportDevStatus(status);
            if (string.IsNullOrWhiteSpace(selectedStatus))
            {
                HttpContext.Session.Remove(FilterStatusKey);
                return "";
            }

            HttpContext.Session.SetString(FilterStatusKey, selectedStatus);
            return selectedStatus;
        }

        private SelectList GetDevStatusList(string? selected = null)
        {
            var selectedValue = NormalizeSupportDevStatus(selected);
            return new SelectList(
                SupportDevStatuses.Select(x => new { x.Value, x.Text }),
                "Value",
                "Text",
                selectedValue
            );
        }

        private static SelectList BuildStatusFilterList(
            IEnumerable<(string Value, string Text)> statuses,
            string? selected)
        {
            return new SelectList(
                statuses.Select(x => new { x.Value, x.Text }),
                "Value",
                "Text",
                selected
            );
        }
    }
}
