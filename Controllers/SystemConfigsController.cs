using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class SystemConfigsController : BaseController
    {
        private readonly AppDbContext _context;

        public SystemConfigsController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var configs = await _context.SystemConfigs
                .AsNoTracking()
                .OrderBy(x => x.ConfigKey)
                .ToListAsync(cancellationToken);

            var model = new SystemConfigPageViewModel
            {
                Items = configs
                    .Where(x => !string.IsNullOrWhiteSpace(x.ConfigKey))
                    .Select(x => new SystemConfigItemViewModel
                    {
                        ConfigKey = x.ConfigKey!.Trim(),
                        ConfigValue = x.ConfigValue ?? "",
                        Description = x.Description ?? "",
                        Group = GetGroup(x.ConfigKey),
                        UpdatedAt = x.UpdatedAt
                    })
                    .OrderBy(x => GetGroupOrder(x.Group))
                    .ThenBy(x => x.ConfigKey)
                    .ToList()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> Save(SystemConfigPostViewModel model, CancellationToken cancellationToken)
        {
            var postedItems = (model.Items ?? new List<SystemConfigInputViewModel>())
                .Where(x => !string.IsNullOrWhiteSpace(x.ConfigKey))
                .Select(x => new SystemConfigInputViewModel
                {
                    ConfigKey = x.ConfigKey.Trim(),
                    ConfigValue = x.ConfigValue?.Trim() ?? "",
                    Description = x.Description?.Trim() ?? ""
                })
                .GroupBy(x => x.ConfigKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Last())
                .ToList();

            if (postedItems.Count == 0)
            {
                TempData["Error"] = "ไม่พบรายการ System Config สำหรับบันทึก";
                return RedirectToAction(nameof(Index));
            }

            var keys = postedItems.Select(x => x.ConfigKey).ToList();
            var configs = await _context.SystemConfigs
                .Where(x => x.ConfigKey != null && keys.Contains(x.ConfigKey))
                .ToListAsync(cancellationToken);

            var configByKey = configs
                .Where(x => !string.IsNullOrWhiteSpace(x.ConfigKey))
                .ToDictionary(x => x.ConfigKey!.Trim(), StringComparer.OrdinalIgnoreCase);

            var updatedCount = 0;
            foreach (var item in postedItems)
            {
                if (!configByKey.TryGetValue(item.ConfigKey, out var config))
                    continue;

                config.ConfigValue = item.ConfigValue;
                config.Description = item.Description;
                config.UpdatedAt = DateTime.Now;
                updatedCount++;
            }

            if (updatedCount > 0)
                await _context.SaveChangesAsync(cancellationToken);

            TempData["Success"] = $"บันทึก System Config แล้ว {updatedCount} รายการ";
            return RedirectToAction(nameof(Index));
        }

        private static string GetGroup(string? key)
        {
            key = (key ?? "").Trim().ToUpperInvariant();

            if (key.StartsWith("LINE_NOTIFICATION_", StringComparison.Ordinal))
                return "LINE Notification";
            if (key.StartsWith("TELEGRAM_NOTIFICATION_", StringComparison.Ordinal))
                return "Telegram Notification";
            if (key.StartsWith("MEETING_NOTIFICATION_", StringComparison.Ordinal))
                return "Auto Meetings";
            if (key.StartsWith("OVERDUE_NOTIFICATION_", StringComparison.Ordinal))
                return "Auto Overdue";
            if (key.StartsWith("WFH_", StringComparison.Ordinal)
                || key.StartsWith("ATTENDANCE_", StringComparison.Ordinal))
                return "Attendance";

            return "System";
        }

        private static int GetGroupOrder(string group)
        {
            return group switch
            {
                "Auto Meetings" => 10,
                "Auto Overdue" => 20,
                "LINE Notification" => 30,
                "Telegram Notification" => 40,
                "Attendance" => 50,
                "System" => 90,
                _ => 999
            };
        }
    }
}
