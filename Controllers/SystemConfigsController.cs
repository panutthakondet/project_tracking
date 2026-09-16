using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
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

        [HttpGet]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> MasterData(string? type, CancellationToken cancellationToken)
        {
            var activeSection = string.Equals(type, "departments", StringComparison.OrdinalIgnoreCase)
                ? "departments"
                : "positions";

            var model = new SystemConfigMasterDataViewModel
            {
                ActiveSection = activeSection,
                Positions = await _context.EmployeePositions
                    .AsNoTracking()
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.PositionName)
                    .Select(x => new EmployeePositionConfigViewModel
                    {
                        PositionId = x.PositionId,
                        PositionName = x.PositionName,
                        SortOrder = x.SortOrder,
                        IsActive = x.IsActive,
                        EmployeeCount = x.Employees.Count
                    })
                    .ToListAsync(cancellationToken),
                Departments = await _context.ProjectDepartments
                    .AsNoTracking()
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DepartmentName)
                    .Select(x => new ProjectDepartmentConfigViewModel
                    {
                        DepartmentId = x.DepartmentId,
                        DepartmentCode = x.DepartmentCode,
                        DepartmentName = x.DepartmentName,
                        SortOrder = x.SortOrder,
                        IsActive = x.IsActive,
                        EmployeeCount = x.Employees.Count,
                        ProjectCount = x.ProjectLinks.Count
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> SavePosition(
            int positionId,
            string? positionName,
            int sortOrder,
            bool isActive,
            CancellationToken cancellationToken)
        {
            positionName = (positionName ?? "").Trim();
            if (positionName.Length == 0 || positionName.Length > 150)
            {
                TempData["Error"] = "กรุณาระบุชื่อตำแหน่งไม่เกิน 150 ตัวอักษร";
                return RedirectToMasterData("positions");
            }

            var duplicate = await _context.EmployeePositions
                .AsNoTracking()
                .AnyAsync(x => x.PositionId != positionId && x.PositionName == positionName, cancellationToken);
            if (duplicate)
            {
                TempData["Error"] = $"มีตำแหน่ง “{positionName}” อยู่แล้ว";
                return RedirectToMasterData("positions");
            }

            EmployeePosition position;
            if (positionId > 0)
            {
                var existingPosition = await _context.EmployeePositions
                    .FirstOrDefaultAsync(x => x.PositionId == positionId, cancellationToken);
                if (existingPosition == null)
                {
                    TempData["Error"] = "ไม่พบตำแหน่งที่ต้องการแก้ไข";
                    return RedirectToMasterData("positions");
                }

                position = existingPosition;
            }
            else
            {
                position = new EmployeePosition();
                _context.EmployeePositions.Add(position);
            }

            position.PositionName = positionName;
            position.SortOrder = sortOrder;
            position.IsActive = isActive;
            await _context.SaveChangesAsync(cancellationToken);

            TempData["Success"] = positionId > 0 ? "บันทึกตำแหน่งแล้ว" : "เพิ่มตำแหน่งแล้ว";
            return RedirectToMasterData("positions");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> TogglePosition(int positionId, CancellationToken cancellationToken)
        {
            var position = await _context.EmployeePositions
                .FirstOrDefaultAsync(x => x.PositionId == positionId, cancellationToken);
            if (position == null)
            {
                TempData["Error"] = "ไม่พบตำแหน่งที่ต้องการปรับสถานะ";
                return RedirectToMasterData("positions");
            }

            position.IsActive = !position.IsActive;
            await _context.SaveChangesAsync(cancellationToken);
            TempData["Success"] = position.IsActive ? "เปิดใช้งานตำแหน่งแล้ว" : "ปิดใช้งานตำแหน่งแล้ว";
            return RedirectToMasterData("positions");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> SaveDepartment(
            int departmentId,
            string? departmentCode,
            string? departmentName,
            int sortOrder,
            bool isActive,
            CancellationToken cancellationToken)
        {
            departmentCode = (departmentCode ?? "").Trim().ToUpperInvariant();
            departmentName = (departmentName ?? "").Trim();
            if (departmentCode.Length == 0 || departmentCode.Length > 50
                || departmentName.Length == 0 || departmentName.Length > 150)
            {
                TempData["Error"] = "กรุณาระบุรหัสฝ่ายและชื่อฝ่ายให้ถูกต้อง";
                return RedirectToMasterData("departments");
            }

            var duplicate = await _context.ProjectDepartments
                .AsNoTracking()
                .AnyAsync(x => x.DepartmentId != departmentId
                    && (x.DepartmentCode == departmentCode || x.DepartmentName == departmentName), cancellationToken);
            if (duplicate)
            {
                TempData["Error"] = "รหัสฝ่ายหรือชื่อฝ่ายนี้มีอยู่แล้ว";
                return RedirectToMasterData("departments");
            }

            ProjectDepartment department;
            if (departmentId > 0)
            {
                var existingDepartment = await _context.ProjectDepartments
                    .FirstOrDefaultAsync(x => x.DepartmentId == departmentId, cancellationToken);
                if (existingDepartment == null)
                {
                    TempData["Error"] = "ไม่พบฝ่ายที่ต้องการแก้ไข";
                    return RedirectToMasterData("departments");
                }

                department = existingDepartment;
            }
            else
            {
                department = new ProjectDepartment();
                _context.ProjectDepartments.Add(department);
            }

            department.DepartmentCode = departmentCode;
            department.DepartmentName = departmentName;
            department.SortOrder = sortOrder;
            department.IsActive = isActive;
            await _context.SaveChangesAsync(cancellationToken);

            TempData["Success"] = departmentId > 0 ? "บันทึกฝ่ายแล้ว" : "เพิ่มฝ่ายแล้ว";
            return RedirectToMasterData("departments");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> ToggleDepartment(int departmentId, CancellationToken cancellationToken)
        {
            var department = await _context.ProjectDepartments
                .FirstOrDefaultAsync(x => x.DepartmentId == departmentId, cancellationToken);
            if (department == null)
            {
                TempData["Error"] = "ไม่พบฝ่ายที่ต้องการปรับสถานะ";
                return RedirectToMasterData("departments");
            }

            department.IsActive = !department.IsActive;
            await _context.SaveChangesAsync(cancellationToken);
            TempData["Success"] = department.IsActive ? "เปิดใช้งานฝ่ายแล้ว" : "ปิดใช้งานฝ่ายแล้ว";
            return RedirectToMasterData("departments");
        }

        private RedirectToActionResult RedirectToMasterData(string type)
        {
            return RedirectToAction(nameof(MasterData), new { type });
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
