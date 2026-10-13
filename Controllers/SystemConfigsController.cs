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
                TempData["Error"] = "ไม่พบรายการค่าระบบสำหรับบันทึก";
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

            TempData["Success"] = $"บันทึกค่าระบบแล้ว {updatedCount} รายการ";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> ToggleStatus(string? configKey, CancellationToken cancellationToken)
        {
            configKey = (configKey ?? "").Trim();
            var config = await _context.SystemConfigs
                .FirstOrDefaultAsync(x => x.ConfigKey == configKey, cancellationToken);
            if (config == null)
            {
                TempData["Error"] = "ไม่พบค่าระบบที่ต้องการปรับสถานะ";
                return RedirectToAction(nameof(Index));
            }

            if (!bool.TryParse(config.ConfigValue, out var currentStatus))
            {
                TempData["Error"] = "รายการนี้ไม่ใช่ค่าประเภทเปิดหรือปิดใช้งาน";
                return RedirectToAction(nameof(Index));
            }

            var nextStatus = !currentStatus;
            config.ConfigValue = nextStatus.ToString().ToLowerInvariant();
            config.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync(cancellationToken);

            TempData["Success"] = nextStatus ? "ปรับสถานะเป็นเปิดใช้งานแล้ว" : "ปรับสถานะเป็นปิดใช้งานแล้ว";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> MasterData(string? type, CancellationToken cancellationToken)
        {
            var activeSection = (type ?? "").Trim().ToLowerInvariant() switch
            {
                "departments" => "departments",
                "employee-document-types" => "employee-document-types",
                "project-document-types" => "project-document-types",
                _ => "positions"
            };

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
                    .ToListAsync(cancellationToken),
                EmployeeDocumentTypes = await _context.EmployeeDocumentTypes
                    .AsNoTracking()
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DocumentTypeName)
                    .Select(x => new DocumentTypeConfigViewModel
                    {
                        DocumentTypeId = x.DocumentTypeId,
                        DocumentTypeCode = x.DocumentTypeCode,
                        DocumentTypeName = x.DocumentTypeName,
                        SortOrder = x.SortOrder,
                        IsActive = x.IsActive,
                        DocumentCount = _context.EmployeeDocuments.Count(d => d.DocumentType == x.DocumentTypeCode)
                    })
                    .ToListAsync(cancellationToken),
                ProjectDocumentTypes = await _context.ProjectDocumentTypes
                    .AsNoTracking()
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DocumentTypeName)
                    .Select(x => new DocumentTypeConfigViewModel
                    {
                        DocumentTypeId = x.DocumentTypeId,
                        DocumentTypeCode = x.DocumentTypeCode,
                        DocumentTypeName = x.DocumentTypeName,
                        SortOrder = x.SortOrder,
                        IsActive = x.IsActive,
                        DocumentCount = _context.ProjectDocuments.Count(d => d.DocumentType == x.DocumentTypeCode)
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
            bool? isActive,
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
            var isNewPosition = positionId <= 0;
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
            if (isNewPosition)
                position.IsActive = isActive ?? true;
            else if (isActive.HasValue)
                position.IsActive = isActive.Value;
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
            bool? isActive,
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
            var isNewDepartment = departmentId <= 0;
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
            if (isNewDepartment)
                department.IsActive = isActive ?? true;
            else if (isActive.HasValue)
                department.IsActive = isActive.Value;
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> SaveEmployeeDocumentType(
            int documentTypeId,
            string? documentTypeCode,
            string? documentTypeName,
            int sortOrder,
            bool? isActive,
            CancellationToken cancellationToken)
        {
            documentTypeCode = (documentTypeCode ?? "").Trim().ToUpperInvariant();
            documentTypeName = (documentTypeName ?? "").Trim();
            if (documentTypeCode.Length == 0 || documentTypeCode.Length > 20
                || documentTypeName.Length == 0 || documentTypeName.Length > 100)
            {
                TempData["Error"] = "กรุณาระบุรหัสและชื่อประเภทเอกสารให้ถูกต้อง";
                return RedirectToMasterData("employee-document-types");
            }

            var duplicate = await _context.EmployeeDocumentTypes
                .AsNoTracking()
                .AnyAsync(x => x.DocumentTypeId != documentTypeId
                    && (x.DocumentTypeCode == documentTypeCode || x.DocumentTypeName == documentTypeName), cancellationToken);
            if (duplicate)
            {
                TempData["Error"] = "รหัสหรือชื่อประเภทเอกสารพนักงานนี้มีอยู่แล้ว";
                return RedirectToMasterData("employee-document-types");
            }

            var isNew = documentTypeId <= 0;
            EmployeeDocumentType documentType;
            if (isNew)
            {
                documentType = new EmployeeDocumentType { CreatedAt = DateTime.Now };
                _context.EmployeeDocumentTypes.Add(documentType);
            }
            else
            {
                var existing = await _context.EmployeeDocumentTypes
                    .FirstOrDefaultAsync(x => x.DocumentTypeId == documentTypeId, cancellationToken);
                if (existing == null)
                {
                    TempData["Error"] = "ไม่พบประเภทเอกสารพนักงานที่ต้องการแก้ไข";
                    return RedirectToMasterData("employee-document-types");
                }

                documentType = existing;
            }

            documentType.DocumentTypeCode = documentTypeCode;
            documentType.DocumentTypeName = documentTypeName;
            documentType.SortOrder = sortOrder;
            documentType.UpdatedAt = DateTime.Now;
            if (isNew)
                documentType.IsActive = isActive ?? true;
            else if (isActive.HasValue)
                documentType.IsActive = isActive.Value;

            await _context.SaveChangesAsync(cancellationToken);
            TempData["Success"] = isNew ? "เพิ่มประเภทเอกสารพนักงานแล้ว" : "บันทึกประเภทเอกสารพนักงานแล้ว";
            return RedirectToMasterData("employee-document-types");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> ToggleEmployeeDocumentType(int documentTypeId, CancellationToken cancellationToken)
        {
            var documentType = await _context.EmployeeDocumentTypes
                .FirstOrDefaultAsync(x => x.DocumentTypeId == documentTypeId, cancellationToken);
            if (documentType == null)
            {
                TempData["Error"] = "ไม่พบประเภทเอกสารพนักงานที่ต้องการปรับสถานะ";
                return RedirectToMasterData("employee-document-types");
            }

            documentType.IsActive = !documentType.IsActive;
            documentType.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync(cancellationToken);
            TempData["Success"] = documentType.IsActive ? "เปิดใช้งานประเภทเอกสารพนักงานแล้ว" : "ปิดใช้งานประเภทเอกสารพนักงานแล้ว";
            return RedirectToMasterData("employee-document-types");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> SaveProjectDocumentType(
            int documentTypeId,
            string? documentTypeCode,
            string? documentTypeName,
            int sortOrder,
            bool? isActive,
            CancellationToken cancellationToken)
        {
            documentTypeCode = (documentTypeCode ?? "").Trim().ToUpperInvariant();
            documentTypeName = (documentTypeName ?? "").Trim();
            if (documentTypeCode.Length == 0 || documentTypeCode.Length > 20
                || documentTypeName.Length == 0 || documentTypeName.Length > 100)
            {
                TempData["Error"] = "กรุณาระบุรหัสและชื่อประเภทเอกสารให้ถูกต้อง";
                return RedirectToMasterData("project-document-types");
            }

            var duplicate = await _context.ProjectDocumentTypes
                .AsNoTracking()
                .AnyAsync(x => x.DocumentTypeId != documentTypeId
                    && (x.DocumentTypeCode == documentTypeCode || x.DocumentTypeName == documentTypeName), cancellationToken);
            if (duplicate)
            {
                TempData["Error"] = "รหัสหรือชื่อประเภทเอกสารโครงการนี้มีอยู่แล้ว";
                return RedirectToMasterData("project-document-types");
            }

            var isNew = documentTypeId <= 0;
            ProjectDocumentType documentType;
            if (isNew)
            {
                documentType = new ProjectDocumentType { CreatedAt = DateTime.Now };
                _context.ProjectDocumentTypes.Add(documentType);
            }
            else
            {
                var existing = await _context.ProjectDocumentTypes
                    .FirstOrDefaultAsync(x => x.DocumentTypeId == documentTypeId, cancellationToken);
                if (existing == null)
                {
                    TempData["Error"] = "ไม่พบประเภทเอกสารโครงการที่ต้องการแก้ไข";
                    return RedirectToMasterData("project-document-types");
                }

                documentType = existing;
            }

            documentType.DocumentTypeCode = documentTypeCode;
            documentType.DocumentTypeName = documentTypeName;
            documentType.SortOrder = sortOrder;
            documentType.UpdatedAt = DateTime.Now;
            if (isNew)
                documentType.IsActive = isActive ?? true;
            else if (isActive.HasValue)
                documentType.IsActive = isActive.Value;

            await _context.SaveChangesAsync(cancellationToken);
            TempData["Success"] = isNew ? "เพิ่มประเภทเอกสารโครงการแล้ว" : "บันทึกประเภทเอกสารโครงการแล้ว";
            return RedirectToMasterData("project-document-types");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("SystemConfigs.Index")]
        public async Task<IActionResult> ToggleProjectDocumentType(int documentTypeId, CancellationToken cancellationToken)
        {
            var documentType = await _context.ProjectDocumentTypes
                .FirstOrDefaultAsync(x => x.DocumentTypeId == documentTypeId, cancellationToken);
            if (documentType == null)
            {
                TempData["Error"] = "ไม่พบประเภทเอกสารโครงการที่ต้องการปรับสถานะ";
                return RedirectToMasterData("project-document-types");
            }

            documentType.IsActive = !documentType.IsActive;
            documentType.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync(cancellationToken);
            TempData["Success"] = documentType.IsActive ? "เปิดใช้งานประเภทเอกสารโครงการแล้ว" : "ปิดใช้งานประเภทเอกสารโครงการแล้ว";
            return RedirectToMasterData("project-document-types");
        }

        private RedirectToActionResult RedirectToMasterData(string type)
        {
            return RedirectToAction(nameof(MasterData), new { type });
        }

        private static string GetGroup(string? key)
        {
            key = (key ?? "").Trim().ToUpperInvariant();

            if (key.StartsWith("LINE_NOTIFICATION_", StringComparison.Ordinal))
                return "การแจ้งเตือน LINE";
            if (key.StartsWith("TELEGRAM_NOTIFICATION_", StringComparison.Ordinal))
                return "การแจ้งเตือน Telegram";
            if (key.StartsWith("MEETING_NOTIFICATION_", StringComparison.Ordinal))
                return "แจ้งเตือนประชุมอัตโนมัติ";
            if (key.StartsWith("OVERDUE_NOTIFICATION_", StringComparison.Ordinal))
                return "แจ้งเตือนงานล่าช้าอัตโนมัติ";
            if (key.StartsWith("WFH_", StringComparison.Ordinal)
                || key.StartsWith("ATTENDANCE_", StringComparison.Ordinal))
                return "การลงเวลา";

            return "ค่าระบบทั่วไป";
        }

        private static int GetGroupOrder(string group)
        {
            return group switch
            {
                "แจ้งเตือนประชุมอัตโนมัติ" => 10,
                "แจ้งเตือนงานล่าช้าอัตโนมัติ" => 20,
                "การแจ้งเตือน LINE" => 30,
                "การแจ้งเตือน Telegram" => 40,
                "การลงเวลา" => 50,
                "ค่าระบบทั่วไป" => 90,
                _ => 999
            };
        }
    }
}
