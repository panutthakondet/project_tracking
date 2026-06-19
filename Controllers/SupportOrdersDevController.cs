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
        private readonly ILogger<SupportOrdersDevController> _logger;
        private static readonly CultureInfo ThaiCulture = new("th-TH");

        public SupportOrdersDevController(
            AppDbContext context,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            ILogger<SupportOrdersDevController> logger)
        {
            _context = context;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _logger = logger;
        }

        // =========================
        // Programmer Order List
        // =========================
        [RequireMenu("SupportOrdersDev.Index")]
        public async Task<IActionResult> Index(int? projectId)
        {
            var query = _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Employee)
                .AsQueryable();

            if (projectId.HasValue)
            {
                query = query.Where(o => o.ProjectId == projectId);
            }

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.ProjectList = new SelectList(
                await _context.Projects
                    .AsNoTracking()
                    .Include(p => p.Coop)
                    .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                    .ThenBy(p => p.ProjectName)
                    .ToListAsync(),
                "ProjectId",
                "ProjectDisplayName",
                projectId
            );

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
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFound();

            order.Images = await _context.ProjectSupportImages
                .Where(x => x.OrderId == id)
                .ToListAsync();

            order.FixImages = await _context.ProjectSupportFixImages
                .Where(x => x.OrderId == id)
                .ToListAsync();

            return View(order);
        }

        [HttpPost]
        [RequireMenu("SupportOrdersDev.Edit")]
        public async Task<IActionResult> Edit(int id, ProjectSupportOrder order, List<IFormFile> afterFiles, List<int> deleteImageIds)
        {
            var dbOrder = await _context.ProjectSupportOrders
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (dbOrder == null)
                return NotFound();

            var oldDevStatus = NormalizeSupportDevStatus(dbOrder.DevStatus);
            var nextDevStatus = NormalizeSupportDevStatus(order.DevStatus);
            var shouldNotifyBaFixed = oldDevStatus != "FIXED" && nextDevStatus == "FIXED";
            dbOrder.DevStatus = nextDevStatus;
            dbOrder.DevDetail = order.DevDetail;

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

            if (shouldNotifyBaFixed)
                await SendFixedSupportTelegramToBaSafelyAsync(dbOrder.OrderId);

            return RedirectToAction(nameof(Index), new { projectId = dbOrder.ProjectId });
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

        private static string NormalizeSupportDevStatus(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            if (normalized == "IN_PROGRESS" || normalized == "TODO" || normalized == "DOING" || normalized == "BLOCK")
                return "WIP";
            return normalized == "FIXED" ? "FIXED" : "WIP";
        }
    }
}
