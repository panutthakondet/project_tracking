using System;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    public class SupportOrdersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly OverdueNotificationService _notificationService;
        private readonly LineMessagingService _lineMessagingService;
        private readonly LineNotificationSettingsService _lineNotificationSettings;
        private readonly TelegramMessagingService _telegramMessagingService;
        private readonly TelegramNotificationSettingsService _telegramNotificationSettings;
        private readonly ILogger<SupportOrdersController> _logger;
        private const string FilterProjectIdKey = "SupportOrders.Filter.ProjectId";
        private const string FilterEmpIdKey = "SupportOrders.Filter.EmpId";
        private const string FilterStatusKey = "SupportOrders.Filter.Status";
        private static readonly string[] SupportOrderStatuses = { "OPEN", "PASS", "FAIL", "REJECT" };
        private static readonly string[] SupportDevStatuses = { "WIP", "FIXED" };
        private static readonly CultureInfo ThaiCulture = new("th-TH");
        private static readonly (string Value, string Text)[] TesterSupportStatuses =
        {
            ("OPEN", "OPEN - เปิดงาน / รอแก้"),
            ("FAIL", "FAIL - ทดสอบไม่ผ่าน / ส่งกลับแก้"),
            ("PASS", "PASS - ทดสอบผ่าน / ปิดงาน"),
            ("REJECT", "REJECT - ปฏิเสธ / ไม่ใช่งาน Support")
        };

        public SupportOrdersController(
            AppDbContext context,
            IWebHostEnvironment env,
            OverdueNotificationService notificationService,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            ILogger<SupportOrdersController> logger)
        {
            _context = context;
            _env = env;
            _notificationService = notificationService;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _logger = logger;
        }

        // =========================
        // LIST
        // =========================
        [RequireMenu("SupportOrders.Index")]
        public async Task<IActionResult> Index(int? projectId, int? empId, string? status)
        {
            (projectId, empId) = ResolveIndexFilters(projectId, empId);
            var selectedStatus = ResolveStatusFilter(status);

            // send project list to dropdown
            var projectList = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();
            ViewBag.Projects = projectList;
            ViewBag.ProjectList = new SelectList(projectList, "ProjectId", "ProjectDisplayName", projectId);

            var query = _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Employee)
                    .ThenInclude(e => e!.LoginUser)
                .AsQueryable();

            // filter by selected project
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
                query = query.Where(o => o.Status == selectedStatus);
            }

            // send selected project name to view
            Project? selectedProject = null;
            if (projectId.HasValue && projectId.Value > 0)
            {
                selectedProject = await _context.Projects
                    .AsNoTracking()
                    .Include(p => p.Coop)
                    .FirstOrDefaultAsync(p => p.ProjectId == projectId.Value);

                ViewBag.SelectedProjectName = selectedProject?.ProjectDisplayName;
            }

            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedEmpId = empId;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedStatus = selectedStatus;
            ViewBag.StatusList = BuildStatusFilterList(TesterSupportStatuses, selectedStatus);
            ViewBag.EmpList = await BuildOwnerListAsync(projectId, empId);

            var orders = await query
                .OrderByDescending(o => o.OrderId)
                .ToListAsync();

            return View(orders);
        }

        [RequireMenu("SupportOrders.Index")]
        public async Task<IActionResult> ViewOnly(int? projectId, int? baEmpId, string? empName, string? status, string? priority, string? devStatus)
        {
            empName = string.IsNullOrWhiteSpace(empName) ? null : empName.Trim();
            status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
            priority = string.IsNullOrWhiteSpace(priority) ? null : priority.Trim().ToUpperInvariant();
            devStatus = string.IsNullOrWhiteSpace(devStatus) ? null : devStatus.Trim().ToUpperInvariant();

            var projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .Include(p => p.BA)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var query = _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Project)
                    .ThenInclude(p => p!.BA)
                .Include(o => o.Employee)
                .Include(o => o.FixImages)
                .AsQueryable();

            if (projectId.HasValue && projectId.Value > 0)
                query = query.Where(o => o.ProjectId == projectId.Value);

            if (baEmpId.HasValue && baEmpId.Value > 0)
                query = query.Where(o => o.Project != null && o.Project.BaEmpId == baEmpId.Value);

            if (!string.IsNullOrWhiteSpace(empName))
                query = query.Where(o => o.Employee != null && o.Employee.EmpName == empName);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);

            if (!string.IsNullOrWhiteSpace(priority))
                query = query.Where(o => o.Priority == priority);

            if (!string.IsNullOrWhiteSpace(devStatus))
                query = query.Where(o => o.DevStatus == devStatus);

            var orders = await query
                .OrderBy(o => o.Project != null && o.Project.Coop != null ? o.Project.Coop.CoopName : "")
                .ThenBy(o => o.Project != null ? o.Project.ProjectName : "")
                .ThenBy(o => o.Project != null && o.Project.BA != null ? o.Project.BA.EmpName : "")
                .ThenBy(o => o.EndDate ?? DateTime.MaxValue)
                .ThenByDescending(o =>
                    o.Priority == "URGENT" ? 4 :
                    o.Priority == "HIGH" ? 3 :
                    o.Priority == "MEDIUM" ? 2 :
                    o.Priority == "LOW" ? 1 : 0)
                .ThenByDescending(o => o.CreatedAt)
                .ThenBy(o => o.OrderId)
                .ToListAsync();

            var orderIds = orders.Select(o => o.OrderId).ToList();
            var imageCounts = orderIds.Any()
                ? await _context.ProjectSupportImages
                    .AsNoTracking()
                    .Where(x => orderIds.Contains(x.OrderId))
                    .GroupBy(x => x.OrderId)
                    .ToDictionaryAsync(g => g.Key, g => g.Count())
                : new Dictionary<int, int>();

            var baList = await (
                    from order in _context.ProjectSupportOrders.AsNoTracking()
                    join projectRow in _context.Projects.AsNoTracking()
                        on order.ProjectId equals projectRow.ProjectId
                    join employee in _context.Employees.AsNoTracking()
                        on projectRow.BaEmpId equals employee.EmpId
                    where projectRow.BaEmpId != null
                    select new
                    {
                        EmpId = employee.EmpId,
                        employee.EmpName
                    })
                .Distinct()
                .OrderBy(x => x.EmpName)
                .Select(x => new SelectListItem
                {
                    Value = x.EmpId.ToString(),
                    Text = x.EmpName
                })
                .ToListAsync();

            var empList = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(o => o.Employee)
                .Where(o => o.Employee != null)
                .Select(o => o.Employee!.EmpName)
                .Distinct()
                .OrderBy(name => name)
                .ToListAsync();

            ViewBag.Projects = projects;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedBA = baEmpId;
            ViewBag.SelectedEmp = empName ?? "";
            ViewBag.BAList = baList;
            ViewBag.EmpList = empList;
            ViewBag.SelectedStatus = status ?? "";
            ViewBag.SelectedPriority = priority ?? "";
            ViewBag.SelectedDevStatus = devStatus ?? "";
            ViewBag.ImageCounts = imageCounts;
            ViewBag.StatusList = SupportOrderStatuses;
            ViewBag.PriorityList = new[] { "URGENT", "HIGH", "MEDIUM", "LOW" };
            ViewBag.DevStatusList = SupportDevStatuses;

            return View(orders);
        }

        // =========================
        // DETAIL
        // =========================
        [RequireMenu("SupportOrders.Index")]
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

            // Load BA images
            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == order.OrderId)
                .ToListAsync();

            return View(order);
        }

        // =========================
        // CREATE (GET)
        // =========================
        [RequireMenu("SupportOrders.Create")]
        public IActionResult Create(int projectId)
        {
            ViewBag.EmployeeList = new SelectList(_context.Employees, "EmpId", "EmpName");

            if (projectId > 0)
            {
                var project = _context.Projects
                    .AsNoTracking()
                    .Include(p => p.Coop)
                    .FirstOrDefault(p => p.ProjectId == projectId);

                ViewBag.SelectedProjectName = project?.ProjectDisplayName;
            }

            var model = new ProjectSupportOrder
            {
                ProjectId = projectId,
                Status = "OPEN",
                DevStatus = "WIP",
                IsReopen = false,
                ReopenCount = 0,
                Priority = "MEDIUM"
            };

            return View(model);
        }

        // =========================
        // CREATE (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SupportOrders.Create")]
        public async Task<IActionResult> Create(ProjectSupportOrder order, List<IFormFile> files)
        {
            ApplySupportDateInput(order);
            ValidateSupportDateRange(order, requireDates: true);
            await PopulateSupportOrderFormAsync(order);

            if (!ModelState.IsValid)
                return View(order);

            // validate project id (prevent foreign key error)
            if (order.ProjectId <= 0)
            {
                ModelState.AddModelError("ProjectId", "Project is required.");
                return View(order);
            }

            var projectBaEmpId = await _context.Projects
                .AsNoTracking()
                .Where(p => p.ProjectId == order.ProjectId)
                .Select(p => p.BaEmpId)
                .FirstOrDefaultAsync();

            var projectExists = await _context.Projects.AnyAsync(p => p.ProjectId == order.ProjectId);
            if (!projectExists)
            {
                ModelState.AddModelError("ProjectId", "Selected project does not exist.");
                return View(order);
            }

            order.CreatedBy = projectBaEmpId ?? await GetCurrentEntryIdAsync();

            order.CreatedAt = DateTime.Now;
            order.Status = "OPEN";
            order.DevStatus = "WIP";
            order.IsReopen = false;
            order.ReopenCount = 0;

            _context.ProjectSupportOrders.Add(order);
            await _context.SaveChangesAsync();

            _context.ProjectSupportOrderStatusHistories.Add(new ProjectSupportOrderStatusHistory
            {
                OrderId = order.OrderId,
                OldStatus = null,
                NewStatus = order.Status ?? "OPEN",
                IsReopen = order.IsReopen,
                ReopenCount = order.ReopenCount,
                ChangedAt = order.CreatedAt ?? DateTime.Now,
                ChangedByEmpId = order.CreatedBy ?? order.AssignTo
            });

            await _context.SaveChangesAsync();

            // upload images
            if (files != null && files.Count > 0)
            {
                var folder = Path.Combine(_env.WebRootPath, "uploads/support", order.OrderId.ToString());

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                foreach (var file in files)
                {
                    if (file.Length == 0) continue;

                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    _context.ProjectSupportImages.Add(new ProjectSupportImage
                    {
                        OrderId = order.OrderId,
                        FileName = fileName,
                        FilePath = $"/uploads/support/{order.OrderId}/{fileName}"
                    });
                }

                await _context.SaveChangesAsync();
            }

            await SyncNotificationsSafelyAsync();
            await SendCreatedSupportTelegramSafelyAsync(order.OrderId);

            return RedirectToAction("Index", new { projectId = order.ProjectId });
        }

        // =========================
        // EDIT (GET)
        // =========================
        [RequireMenu("SupportOrders.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var order = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Employee)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            // Load BA images
            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == order.OrderId)
                .ToListAsync();

            // Send project name to view
            ViewBag.SelectedProjectName = order.Project?.ProjectDisplayName;

            ViewBag.EmployeeList = new SelectList(_context.Employees, "EmpId", "EmpName", order.AssignTo);
            ViewBag.CurrentStatus = order.Status;
            ViewBag.CurrentDevStatus = order.DevStatus;
            ViewBag.StatusList = GetStatusList(order.Status);

            return View(order);
        }

        // =========================
        // EDIT (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SupportOrders.Edit")]
        public async Task<IActionResult> Edit(int id, ProjectSupportOrder order, List<IFormFile> files, List<int> deleteImageIds)
        {
            if (id != order.OrderId)
                return NotFound();

            // โหลดข้อมูลเดิมก่อน เพื่อกันค่า DevStatus หาย
            var existingOrder = await _context.ProjectSupportOrders
                .FirstOrDefaultAsync(x => x.OrderId == order.OrderId);

            if (existingOrder == null)
                return NotFound();

            ApplySupportDateInput(order);
            ValidateSupportDateRange(order);

            var nextStatus = NormalizeSupportStatus(order.Status);
            var currentDevStatus = NormalizeSupportDevStatus(existingOrder.DevStatus);
            var oldStatus = NormalizeSupportStatus(existingOrder.Status);
            var statusChanged = !string.Equals(oldStatus, nextStatus, StringComparison.OrdinalIgnoreCase);
            var shouldNotifyFailRound = nextStatus == "FAIL" && currentDevStatus == "FIXED";
            var shouldNotifyAssigneeBaResult = IsBaResultStatus(nextStatus);
            if (RequiresFixedDevStatusForBaResult(nextStatus) && currentDevStatus != "FIXED")
            {
                ModelState.AddModelError(
                    nameof(ProjectSupportOrder.Status),
                    "ไม่สามารถบันทึกสถานะ PASS หรือ FAIL ได้ เนื่องจาก Dev Status ยังไม่เป็น FIXED");
            }

            if (!ModelState.IsValid)
            {
                order.DevStatus = existingOrder.DevStatus;
                await PopulateSupportOrderFormAsync(order);
                ViewBag.CurrentStatus = existingOrder.Status;
                ViewBag.CurrentDevStatus = existingOrder.DevStatus;
                ViewBag.StatusList = GetStatusList(order.Status);
                return View(order);
            }

            existingOrder.OrderTitle = order.OrderTitle;
            existingOrder.OrderDetail = order.OrderDetail;
            existingOrder.Priority = order.Priority;
            existingOrder.Status = nextStatus;
            existingOrder.DevStatus = currentDevStatus;
            existingOrder.StartDate = order.StartDate;
            existingOrder.EndDate = order.EndDate;
            existingOrder.AssignTo = order.AssignTo;

            if (existingOrder.Status == "OPEN" || existingOrder.Status == "FAIL")
            {
                existingOrder.DevStatus = "WIP";
            }

            if (shouldNotifyFailRound)
            {
                existingOrder.IsReopen = true;
                existingOrder.ReopenCount += 1;
            }

            if (statusChanged || shouldNotifyFailRound)
            {
                var changedByEmpId = await GetCurrentEntryIdAsync();

                _context.ProjectSupportOrderStatusHistories.Add(new ProjectSupportOrderStatusHistory
                {
                    OrderId = existingOrder.OrderId,
                    OldStatus = oldStatus,
                    NewStatus = nextStatus,
                    IsReopen = existingOrder.IsReopen,
                    ReopenCount = existingOrder.ReopenCount,
                    ChangedAt = DateTime.Now,
                    ChangedByEmpId = changedByEmpId ?? existingOrder.AssignTo
                });
            }

            // ===== Delete BA images =====
            if (deleteImageIds != null && deleteImageIds.Count > 0)
            {
                var imagesToDelete = await _context.ProjectSupportImages
                    .Where(x => deleteImageIds.Contains(x.ImageId))
                    .ToListAsync();

                foreach (var img in imagesToDelete)
                {
                    var relativePath = (img.FilePath ?? "").TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString());
                    var physicalPath = Path.Combine(_env.WebRootPath, relativePath);

                    if (System.IO.File.Exists(physicalPath))
                    {
                        System.IO.File.Delete(physicalPath);
                    }

                    _context.ProjectSupportImages.Remove(img);
                }
            }

            var folder = Path.Combine(_env.WebRootPath, "uploads/support", order.OrderId.ToString());

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            // ===== Upload BA images =====
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    if (file.Length == 0) continue;

                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    _context.ProjectSupportImages.Add(new ProjectSupportImage
                    {
                        OrderId = order.OrderId,
                        FileName = fileName,
                        FilePath = $"/uploads/support/{order.OrderId}/{fileName}"
                    });
                }
            }

            await _context.SaveChangesAsync();

            if (nextStatus != "OPEN")
            {
                await SyncNotificationsSafelyAsync();
                if (shouldNotifyAssigneeBaResult)
                    await SendBaResultSupportTelegramToAssigneeSafelyAsync(order.OrderId);
            }

            return RedirectToAction("Index", new { projectId = existingOrder.ProjectId });
        }

        // =========================
        // DELETE
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SupportOrders.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _context.ProjectSupportOrders
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            order.CreatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            _context.ProjectSupportOrders.Remove(order);
            await _context.SaveChangesAsync();

            await SyncNotificationsSafelyAsync();

            return RedirectToAction("Index");
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

        private async Task SendCreatedSupportTelegramSafelyAsync(int orderId)
        {
            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.SupportOrdersCreate, HttpContext.RequestAborted);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.SupportOrdersCreate, HttpContext.RequestAborted);

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

                if (order == null)
                    return;

                var project = order.Project;
                var recipientTargets = new Dictionary<int, string>();

                if (project?.BaEmpId is > 0)
                    recipientTargets[project.BaEmpId.Value] = $"/SupportOrders/Details/{order.OrderId}";

                if (order.AssignTo is > 0)
                    recipientTargets.TryAdd(order.AssignTo.Value, $"/SupportOrdersDev/Details/{order.OrderId}");

                if (recipientTargets.Count == 0)
                    return;

                var title = "แจ้ง Support ใหม่:";
                var message = BuildCreatedSupportTelegramMessage(order);

                foreach (var recipient in recipientTargets)
                {
                    await SendChatNotificationToEmployeeSafelyAsync(
                        recipient.Key,
                        title,
                        message,
                        recipient.Value,
                        sendLine,
                        sendTelegram,
                        "created support",
                        order.OrderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send created support Telegram notification failed. OrderId={OrderId}", orderId);
            }
        }

        private async Task SendBaResultSupportTelegramToAssigneeSafelyAsync(int orderId)
        {
            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.SupportOrdersBaResult, HttpContext.RequestAborted);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.SupportOrdersBaResult, HttpContext.RequestAborted);

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

                if (order == null || !order.AssignTo.HasValue || order.AssignTo.Value <= 0)
                    return;

                var title = $"แจ้งผลตรวจ Support: {TextOrDash(order.Status)}";
                var message = BuildBaResultSupportTelegramMessage(order);
                var targetUrl = $"/SupportOrdersDev/Details/{order.OrderId}";

                await SendChatNotificationToEmployeeSafelyAsync(
                    order.AssignTo.Value,
                    title,
                    message,
                    targetUrl,
                    sendLine,
                    sendTelegram,
                    "BA result support",
                    order.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send BA result support Telegram notification failed. OrderId={OrderId}", orderId);
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

        private static string BuildCreatedSupportTelegramMessage(ProjectSupportOrder order)
        {
            var project = order.Project;
            var rows = new List<string>
            {
                $"สหกรณ์: {TextOrDash(project?.Coop?.CoopName)}",
                $"Project: {ProjectNameForTelegram(project)}",
                $"Support: {TextOrDash(order.OrderTitle)}",
                $"รายละเอียด: {TextOrDash(order.OrderDetail)}",
                $"เจ้าของงาน: {TextOrDash(order.Employee?.EmpName)}",
                $"BA: {TextOrDash(project?.BA?.EmpName)}",
                $"Priority: {TextOrDash(order.Priority)}",
                $"Status: {TextOrDash(order.Status)} / Dev {TextOrDash(order.DevStatus)}",
                $"วันที่เริ่ม: {DateText(order.StartDate)}",
                $"วันที่สิ้นสุด: {DateText(order.EndDate)}"
            };

            return string.Join("\n", rows);
        }

        private static string BuildBaResultSupportTelegramMessage(ProjectSupportOrder order)
        {
            var project = order.Project;
            var rows = new List<string>
            {
                $"สหกรณ์: {TextOrDash(project?.Coop?.CoopName)}",
                $"Project: {ProjectNameForTelegram(project)}",
                $"Support: {TextOrDash(order.OrderTitle)}",
                $"เจ้าของงาน: {TextOrDash(order.Employee?.EmpName)}",
                $"BA: {TextOrDash(project?.BA?.EmpName)}",
                $"Status: {TextOrDash(order.Status)} / Dev {TextOrDash(order.DevStatus)}",
                $"รายละเอียดงาน: {TextOrDash(order.OrderDetail)}",
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

        private void ApplySupportDateInput(ProjectSupportOrder order)
        {
            ModelState.Remove(nameof(ProjectSupportOrder.StartDate));
            ModelState.Remove(nameof(ProjectSupportOrder.EndDate));

            var startRaw = Request.Form[nameof(ProjectSupportOrder.StartDate)].ToString();
            var endRaw = Request.Form[nameof(ProjectSupportOrder.EndDate)].ToString();

            order.StartDate = ParseSupportDate(startRaw);
            order.EndDate = ParseSupportDate(endRaw);

            if (!string.IsNullOrWhiteSpace(startRaw) && !order.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.StartDate), "รูปแบบวันที่ต้องเป็น วัน/เดือน/พ.ศ.");
            }

            if (!string.IsNullOrWhiteSpace(endRaw) && !order.EndDate.HasValue)
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.EndDate), "รูปแบบวันที่ต้องเป็น วัน/เดือน/พ.ศ.");
            }
        }

        private static DateTime? ParseSupportDate(string? value)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return null;

            var isoParts = value.Split('-');
            if (isoParts.Length == 3
                && int.TryParse(isoParts[0], out var isoYear)
                && int.TryParse(isoParts[1], out var isoMonth)
                && int.TryParse(isoParts[2], out var isoDay))
            {
                isoYear = NormalizeThaiCalendarYear(isoYear);

                try
                {
                    return new DateTime(isoYear, isoMonth, isoDay);
                }
                catch
                {
                    return null;
                }
            }

            var parts = value.Split('/');
            if (parts.Length == 3
                && int.TryParse(parts[0], out var day)
                && int.TryParse(parts[1], out var month)
                && int.TryParse(parts[2], out var year))
            {
                year = NormalizeThaiCalendarYear(year);

                try
                {
                    return new DateTime(year, month, day);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static int NormalizeThaiCalendarYear(int year)
        {
            while (year > 2200)
            {
                year -= 543;
            }

            return year;
        }

        private void ValidateSupportDateRange(ProjectSupportOrder order, bool requireDates = false)
        {
            if (requireDates && !order.StartDate.HasValue && !HasModelError(nameof(ProjectSupportOrder.StartDate)))
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.StartDate), "กรุณากรอกวันที่เริ่ม");
            }

            if (requireDates && !order.EndDate.HasValue && !HasModelError(nameof(ProjectSupportOrder.EndDate)))
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.EndDate), "กรุณากรอกวันที่สิ้นสุด");
            }

            if (order.StartDate.HasValue
                && order.EndDate.HasValue
                && order.EndDate.Value.Date < order.StartDate.Value.Date)
            {
                ModelState.AddModelError(nameof(ProjectSupportOrder.EndDate), "วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่ม");
            }
        }

        private bool HasModelError(string key)
        {
            return ModelState.TryGetValue(key, out var state) && state.Errors.Count > 0;
        }

        private async Task PopulateSupportOrderFormAsync(ProjectSupportOrder order)
        {
            ViewBag.StatusList = GetStatusList(order.Status);

            ViewBag.EmployeeList = new SelectList(
                await _context.Employees
                    .AsNoTracking()
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName",
                order.AssignTo);

            if (order.ProjectId > 0)
            {
                var project = await _context.Projects
                    .AsNoTracking()
                    .Include(p => p.Coop)
                    .FirstOrDefaultAsync(p => p.ProjectId == order.ProjectId);

                ViewBag.SelectedProjectName = project?.ProjectDisplayName;
            }

            if (order.OrderId > 0)
            {
                order.Images = await _context.ProjectSupportImages
                    .AsNoTracking()
                    .Where(x => x.OrderId == order.OrderId)
                    .ToListAsync();
            }
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

        private static string NormalizeSupportStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            if (normalized == "WAIT_TEST") return "OPEN";
            if (normalized == "DONE") return "PASS";
            return SupportOrderStatuses.Contains(normalized) ? normalized : "OPEN";
        }

        private static string NormalizeIndexSupportStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            if (normalized == "WAIT_TEST") return "OPEN";
            if (normalized == "DONE") return "PASS";
            return SupportOrderStatuses.Contains(normalized) ? normalized : "";
        }

        private string ResolveStatusFilter(string? status)
        {
            if (!Request.Query.ContainsKey("status"))
            {
                return NormalizeIndexSupportStatus(HttpContext.Session.GetString(FilterStatusKey));
            }

            var selectedStatus = NormalizeIndexSupportStatus(status);
            if (string.IsNullOrWhiteSpace(selectedStatus))
            {
                HttpContext.Session.Remove(FilterStatusKey);
                return "";
            }

            HttpContext.Session.SetString(FilterStatusKey, selectedStatus);
            return selectedStatus;
        }

        private static string NormalizeSupportDevStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            if (normalized == "IN_PROGRESS" || normalized == "TODO" || normalized == "DOING" || normalized == "BLOCK")
                return "WIP";
            return SupportDevStatuses.Contains(normalized) ? normalized : "WIP";
        }

        private static bool RequiresFixedDevStatusForBaResult(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized == "PASS" || normalized == "FAIL";
        }

        private static bool IsBaResultStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized == "PASS" || normalized == "FAIL" || normalized == "REJECT";
        }

        private SelectList GetStatusList(string? selected = null, bool includeOpen = true)
        {
            var selectedValue = NormalizeSupportStatus(selected);
            var statuses = TesterSupportStatuses.ToList();
            if (!includeOpen)
            {
                statuses.RemoveAll(x => x.Value == "OPEN");
            }

            return new SelectList(
                statuses.Select(x => new { x.Value, x.Text }),
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
