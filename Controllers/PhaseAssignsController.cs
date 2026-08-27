using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System;
using System.Globalization;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    public class PhaseAssignsController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly LineMessagingService _lineMessagingService;
        private readonly LineNotificationSettingsService _lineNotificationSettings;
        private readonly TelegramMessagingService _telegramMessagingService;
        private readonly TelegramNotificationSettingsService _telegramNotificationSettings;
        private readonly StatusApprovalService _statusApprovalService;
        private readonly WorkflowStatusService _workflowStatusService;
        private readonly ILogger<PhaseAssignsController> _logger;

        public PhaseAssignsController(
            AppDbContext context,
            LineMessagingService lineMessagingService,
            LineNotificationSettingsService lineNotificationSettings,
            TelegramMessagingService telegramMessagingService,
            TelegramNotificationSettingsService telegramNotificationSettings,
            StatusApprovalService statusApprovalService,
            WorkflowStatusService workflowStatusService,
            ILogger<PhaseAssignsController> logger)
        {
            _context = context;
            _lineMessagingService = lineMessagingService;
            _lineNotificationSettings = lineNotificationSettings;
            _telegramMessagingService = telegramMessagingService;
            _telegramNotificationSettings = telegramNotificationSettings;
            _statusApprovalService = statusApprovalService;
            _workflowStatusService = workflowStatusService;
            _logger = logger;
        }

        // รองรับวันที่ไทย dd/MM/พ.ศ.
        private DateTime? ParseThaiDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            // yyyy-MM-dd
            if (DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var isoDate))
            {
                return isoDate;
            }

            // dd/MM/yyyy (พ.ศ.)
            var parts = value.Split('/');
            if (parts.Length == 3)
            {
                if (int.TryParse(parts[0], out var d) &&
                    int.TryParse(parts[1], out var m) &&
                    int.TryParse(parts[2], out var y))
                {
                    if (y > 2400)
                        y -= 543;

                    try
                    {
                        return new DateTime(y, m, d);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        // =====================================================
        // INDEX
        // =====================================================
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Index(int? projectId, int? empId)
        {
            ViewBag.Projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .Include(p => p.BA)
                .Include(p => p.TeamMembers)
                    .ThenInclude(m => m.Employee)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedEmpId = empId;

            ViewBag.EmployeeList = new List<Employee>();

            if (projectId == null)
            {
                ViewBag.PendingAssignApprovalIds = new HashSet<int>();
                return View(new List<PhaseAssign>());
            }

            var project = await _context.Projects
                .Include(p => p.Coop)
                .Include(p => p.BA)
                    .ThenInclude(e => e!.LoginUser)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId.Value);
            if (project == null)
            {
                ViewBag.PendingAssignApprovalIds = new HashSet<int>();
                return View(new List<PhaseAssign>());
            }

            ViewBag.SelectedProject = project;

            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) เพราะ EF อาจสร้าง JOIN ด้วยคอลัมน์ PhaseId2 (ไม่มีจริงใน DB)
            // ใช้ JOIN ตรงกับ ProjectPhases/Employees โดยอิง FK จริง PhaseId/EmpId
            ViewBag.EmployeeList = await (
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                where ph.ProjectId == projectId
                group new { e.EmpId, e.EmpName } by new { e.EmpId, e.EmpName } into g
                orderby g.Key.EmpName
                select new Employee
                {
                    EmpId = g.Key.EmpId,
                    EmpName = g.Key.EmpName
                }
            ).ToListAsync();

            var assignsQuery =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                join lu in _context.LoginUsers.AsNoTracking() on e.LoginUserId equals (int?)lu.UserId into loginJoin
                from lu in loginJoin.DefaultIfEmpty()
                join statusDefinition in _context.PhaseAssignStatuses.AsNoTracking()
                    on a.StatusId equals (int?)statusDefinition.StatusId into statusJoin
                from statusDefinition in statusJoin.DefaultIfEmpty()
                where ph.ProjectId == projectId
                select new PhaseAssign
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    PhaseOrder = a.PhaseOrder,
                    PhaseSort = a.PhaseSort,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    PlanStart = a.PlanStart,
                    PlanEnd = a.PlanEnd,
                    CreatedAt = a.CreatedAt,
                    WorkStatus = a.WorkStatus,
                    StatusId = a.StatusId,
                    StatusDefinition = statusDefinition,
                    Remark = a.Remark,

                    Phase = ph,
                    Employee = new Employee
                    {
                        EmpId = e.EmpId,
                        EmpName = e.EmpName,
                        Position = e.Position,
                        Status = e.Status,
                        LoginUserId = e.LoginUserId,
                        LoginUser = lu
                    },
                    Logs = _context.PhaseAssignLogs
                        .Where(l => l.AssignId == a.AssignId)
                        .OrderByDescending(l => l.RoundNo)
                        .ToList()
                };

            if (empId.HasValue)
                assignsQuery = assignsQuery.Where(x => x.EmpId == empId.Value);

            var assigns = await assignsQuery
                // เรียงตาม phase_order (ห้ามแก้ค่า แค่ใช้จัดเรียง), แล้วตาม phase_sort (สำหรับสลับแถว), แล้วค่อย fallback ด้วย assign_id
                .OrderBy(a => a.Phase == null ? (a.PhaseOrder ?? int.MaxValue) : a.Phase.PhaseOrder)
                .ThenBy(a => a.Phase == null ? int.MaxValue : a.Phase.PeriodOrder)
                .ThenBy(a => a.PhaseSort ?? int.MaxValue)
                .ThenBy(a => a.AssignId)
                .ToListAsync();

            var assignIds = assigns.Select(a => a.AssignId).ToList();
            ViewBag.PendingAssignApprovalIds = assignIds.Count == 0
                ? new HashSet<int>()
                : (await _context.StatusApprovalRequests
                    .AsNoTracking()
                    .Where(r => r.TargetType == StatusApprovalService.TargetPhaseAssign
                                && r.RequestStatus == StatusApprovalService.RequestPending
                                && assignIds.Contains(r.TargetId))
                    .Select(r => r.TargetId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();

            return View(assigns);
        }

        // =====================================================
        // CREATE (GET)
        // =====================================================
        [RequireMenu("PhaseAssigns.Create")]
        public async Task<IActionResult> Create(int projectId, int? phaseId)
        {
            var project = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            if (project == null) return NotFound();

            ViewBag.ProjectId = projectId;
            ViewBag.ProjectName = project.ProjectDisplayName;

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .OrderBy(p => p.PhaseOrder)
                .ThenBy(p => p.PeriodOrder)
                .ThenBy(p => p.PhaseSort == 0 ? int.MaxValue : p.PhaseSort)
                .ThenBy(p => p.PhaseId)
                .ToListAsync();

            var selectedPhase = phases.FirstOrDefault(p => phaseId.HasValue && p.PhaseId == phaseId.Value)
                ?? phases.FirstOrDefault();

            // ✅ ใช้ได้ 2 แบบ:
            // 1) View เดิมที่ใช้ SelectList
            ViewBag.Phases = new SelectList(phases, "PhaseId", "PhaseDisplayName", selectedPhase?.PhaseId);
            // 2) View ใหม่ที่ต้องการ data-* หรือทำ map ใน JS
            ViewBag.PhaseItems = phases;

            // ✅ เติมค่าเริ่มต้นให้แสดงทันที (กรณีมี phase อย่างน้อย 1)
            var defaultStatusId = await _workflowStatusService.ResolveIdAsync(
                WorkflowStatusTypes.PhaseAssign,
                "IN_PROGRESS");
            var defaultModel = new PhaseAssign
            {
                WorkStatus = "IN_PROGRESS",
                StatusId = defaultStatusId
            };
            if (selectedPhase != null)
            {
                defaultModel.PhaseId = selectedPhase.PhaseId;
                defaultModel.Role = selectedPhase.PhaseName;
                defaultModel.PlanStart = selectedPhase.PlanStart;
                defaultModel.PlanEnd = selectedPhase.PlanEnd;
            }

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName");
            await LoadAssignStatusLookupAsync(defaultModel.StatusId);

            return View(defaultModel);
        }

        // =====================================================
        // CREATE (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Create")]
        public async Task<IActionResult> Create(PhaseAssign model, int projectId)
        {
            // รองรับวันที่ไทย dd/MM/พ.ศ.
            model.PlanStart = ParseThaiDate(Request.Form["PlanStart"]);
            model.PlanEnd = ParseThaiDate(Request.Form["PlanEnd"]);

            ModelState.Remove("PlanStart");
            ModelState.Remove("PlanEnd");
            var selectedStatus = await _workflowStatusService.ResolveSelectionAsync(
                WorkflowStatusTypes.PhaseAssign,
                model.StatusId,
                model.WorkStatus);
            model.StatusId = selectedStatus.StatusId;
            model.WorkStatus = selectedStatus.LegacyValue;
            if (!model.StatusId.HasValue)
                ModelState.AddModelError(nameof(PhaseAssign.StatusId), "กรุณาเลือกสถานะงาน");
            var phase = await _context.ProjectPhases
                .FirstOrDefaultAsync(p => p.PhaseId == model.PhaseId);

            if (phase == null)
            {
                ModelState.AddModelError("", "ไม่พบส่วนงานที่เลือก");
            }
            else
            {
                // 1) ✅ ใช้ค่าในช่อง 🎯 Role (Auto) เป็นค่าที่ insert ลง phase_assign.role
                // รองรับหลายชื่อ field กันพัง (บาง View ตั้งชื่อไม่เหมือนกัน)
                string roleAuto =
                    (Request.Form["RoleAuto"].FirstOrDefault() ??
                     Request.Form["Role_Auto"].FirstOrDefault() ??
                     Request.Form["Role(Auto)"].FirstOrDefault() ??
                     Request.Form["roleAuto"].FirstOrDefault() ??
                     "").Trim();

                // ถ้า View ผูกช่อง Role (Auto) เข้ากับ model.Role มาแล้ว ก็ใช้ model.Role ได้
                if (string.IsNullOrWhiteSpace(roleAuto))
                    roleAuto = (model.Role ?? "").Trim();

                // ถ้ายังว่างอยู่ ค่อย fallback เป็นชื่อส่วนงาน
                model.Role = !string.IsNullOrWhiteSpace(roleAuto) ? roleAuto : phase.PhaseName;

                // 2) ดึงวันจาก project_phase (ใช้เฉพาะตอน user ไม่ได้แก้)
                if (model.PlanStart == null)
                    model.PlanStart = phase.PlanStart;

                if (model.PlanEnd == null)
                    model.PlanEnd = phase.PlanEnd;

                // 3) สำคัญ: บันทึกเลขส่วนงานจาก project_phase ลง phase_assign
                model.PhaseOrder = phase.PhaseOrder;
            }

            // 🔒 Validate Remark length (server-side protection)
            if (model.Remark != null && model.Remark.Length > 1000)
            {
                ModelState.AddModelError("Remark", "Remark ต้องไม่เกิน 1000 ตัวอักษร");
            }

            if (!ModelState.IsValid)
            {
                await ReloadCreateDropdown(projectId, model);
                return View(model);
            }
            // ✅ phase_sort is NOT NULL in MySQL, so always set a value
            // Keep a single total order per project (same order used by drag-reorder on Index)
            if (model.PhaseSort == null || model.PhaseSort <= 0)
            {
                var maxSort = await (
                    from a in _context.PhaseAssigns.AsNoTracking()
                    join ph2 in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph2.PhaseId
                    where ph2.ProjectId == projectId
                    select (int?)a.PhaseSort
                ).MaxAsync();

                model.PhaseSort = (maxSort ?? 0) + 1;
            }
            SyncActualPeriod(model);
            model.CreatedAt = DateTime.Now;
            model.EntryId = await GetCurrentEntryIdAsync();
            _context.PhaseAssigns.Add(model);
            await _context.SaveChangesAsync();
            await SendCreatedPhaseAssignNotificationSafelyAsync(model.AssignId);

            return RedirectToAction(nameof(Index), new { projectId = phase!.ProjectId });
        }

        // =====================================================
        // EDIT (GET)
        // =====================================================
        [HttpGet]
        [RequireMenu("PhaseAssigns.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) เพื่อเลี่ยง EF สร้าง/อ้าง PhaseId2
            var assign = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(a => a.Employee)
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (assign == null)
                return NotFound();

            // หา ProjectId จาก PhaseId ผ่าน ProjectPhases
            var projectId = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == assign.PhaseId)
                .Select(p => (int?)p.ProjectId)
                .FirstOrDefaultAsync();

            ViewBag.ProjectId = projectId;

            // ✅ For Edit view: allow changing Phase
            if (projectId.HasValue)
            {
                var phases = await _context.ProjectPhases
                    .AsNoTracking()
                    .Where(p => p.ProjectId == projectId.Value)
                    .OrderBy(p => p.PhaseOrder)
                    .ThenBy(p => p.PeriodOrder)
                    .ThenBy(p => p.PhaseSort == 0 ? int.MaxValue : p.PhaseSort)
                    .ThenBy(p => p.PhaseId)
                    .ToListAsync();

                ViewBag.Phases = new SelectList(phases, "PhaseId", "PhaseDisplayName", assign.PhaseId);
                ViewBag.PhaseItems = phases;
            }

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName",
                assign.EmpId
            );
            assign.StatusId ??= await _workflowStatusService.ResolveIdAsync(
                WorkflowStatusTypes.PhaseAssign,
                assign.WorkStatus);
            await LoadAssignStatusLookupAsync(assign.StatusId);

            return View(assign);
        }

        // =====================================================
        // EDIT (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Edit")]
        public async Task<IActionResult> Edit(int id, PhaseAssign model)
        {
            if (id != model.AssignId)
                return NotFound();

            // รองรับวันที่ไทย dd/MM/พ.ศ.
            model.PlanStart = ParseThaiDate(Request.Form["PlanStart"]);
            model.PlanEnd = ParseThaiDate(Request.Form["PlanEnd"]);

            ModelState.Remove("PlanStart");
            ModelState.Remove("PlanEnd");

            var selectedStatus = await _workflowStatusService.ResolveSelectionAsync(
                WorkflowStatusTypes.PhaseAssign,
                model.StatusId,
                model.WorkStatus);
            model.StatusId = selectedStatus.StatusId;
            model.WorkStatus = selectedStatus.LegacyValue;
            if (!model.StatusId.HasValue)
                ModelState.AddModelError(nameof(PhaseAssign.StatusId), "กรุณาเลือกสถานะงาน");

            var db = await _context.PhaseAssigns
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (db == null)
                return NotFound();

            // ✅ Determine project of this assignment (from current db.PhaseId)
            var projectIdOfAssign = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == db.PhaseId)
                .Select(p => (int?)p.ProjectId)
                .FirstOrDefaultAsync();

            if (projectIdOfAssign == null)
            {
                ModelState.AddModelError("", "Cannot determine project for this assignment.");
            }

            var oldStatus = db.WorkStatus;
            var oldStatusId = db.StatusId;
            var requestedStatus = model.WorkStatus;
            var requestedStatusId = model.StatusId;

            // ✅ Validate selected phase (must be in same project)
            ProjectPhase? selectedPhase = null;
            if (model.PhaseId > 0)
            {
                selectedPhase = await _context.ProjectPhases
                    .FirstOrDefaultAsync(p => p.PhaseId == model.PhaseId);

                if (selectedPhase == null)
                {
                    ModelState.AddModelError(nameof(model.PhaseId), "ไม่พบส่วนงานที่เลือก");
                }
                else if (projectIdOfAssign != null && selectedPhase.ProjectId != projectIdOfAssign.Value)
                {
                    ModelState.AddModelError(nameof(model.PhaseId), "ส่วนงานที่เลือกไม่ได้อยู่ในโครงการนี้");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(model.PhaseId), "กรุณาเลือกส่วนงาน");
            }

            // 🔒 Validate Remark length (server-side protection)
            if (model.Remark != null && model.Remark.Length > 1000)
            {
                ModelState.AddModelError("Remark", "Remark ต้องไม่เกิน 1000 ตัวอักษร");
            }

            if (!ModelState.IsValid)
            {
                // Reload dropdowns for Edit view
                if (projectIdOfAssign.HasValue)
                {
                    var phases = await _context.ProjectPhases
                        .AsNoTracking()
                        .Where(p => p.ProjectId == projectIdOfAssign.Value)
                        .OrderBy(p => p.PhaseOrder)
                        .ThenBy(p => p.PeriodOrder)
                        .ThenBy(p => p.PhaseSort == 0 ? int.MaxValue : p.PhaseSort)
                        .ThenBy(p => p.PhaseId)
                        .ToListAsync();

                    ViewBag.ProjectId = projectIdOfAssign;
                    ViewBag.Phases = new SelectList(phases, "PhaseId", "PhaseDisplayName", model.PhaseId);
                    ViewBag.PhaseItems = phases;
                }

                ViewBag.Employees = new SelectList(
                    await _context.Employees
                        .Where(e => e.Status == "ACTIVE")
                        .OrderBy(e => e.EmpName)
                        .ToListAsync(),
                    "EmpId",
                    "EmpName",
                    model.EmpId
                );
                await LoadAssignStatusLookupAsync(model.StatusId);

                return View(model);
            }

            // ✅ Apply Phase change + sync plan dates/order from selected phase
            if (selectedPhase != null)
            {
                db.PhaseId = selectedPhase.PhaseId;
                db.PlanStart = selectedPhase.PlanStart;
                db.PlanEnd = selectedPhase.PlanEnd;
                db.PhaseOrder = selectedPhase.PhaseOrder;
            }

            db.EmpId = model.EmpId;
            db.PlanStart = model.PlanStart;
            db.PlanEnd = model.PlanEnd;
            db.Remark = model.Remark;
            db.CreatedAt = DateTime.Now;
            db.EntryId = await GetCurrentEntryIdAsync();

            // ✅ Role: allow manual edit (fallback to PhaseName)
            var roleText = (model.Role ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(roleText))
            {
                db.Role = roleText;
            }
            else
            {
                db.Role = selectedPhase?.PhaseName ?? db.Role;
            }

            var requirePmApproval = StatusApprovalService.IsPhaseAssignCompletionStatus(requestedStatus)
                && !StatusApprovalService.IsPhaseAssignCompletionStatus(oldStatus)
                && !await _statusApprovalService.CanApplyCompletionStatusImmediatelyAsync(projectIdOfAssign);

            if (requirePmApproval)
            {
                db.WorkStatus = oldStatus;
                db.StatusId = oldStatusId;

                Project? project = null;
                if (projectIdOfAssign.HasValue)
                {
                    project = await _context.Projects
                        .AsNoTracking()
                        .Include(p => p.Coop)
                        .FirstOrDefaultAsync(p => p.ProjectId == projectIdOfAssign.Value);
                }

                await _statusApprovalService.QueueCompletionRequestAsync(
                    StatusApprovalService.TargetPhaseAssign,
                    db.AssignId,
                    projectIdOfAssign,
                    project?.ProjectDisplayName,
                    db.Role,
                    oldStatus,
                    requestedStatus,
                    "ขอปรับสถานะมอบหมายงานเป็นเสร็จสิ้น");

                TempData["Success"] = "บันทึกข้อมูลมอบหมายงานแล้ว และส่งคำขออนุมัติสถานะเสร็จสิ้นให้ PM แล้ว";
            }
            else
            {
                db.WorkStatus = requestedStatus;
                db.StatusId = requestedStatusId;
            }

            SyncActualPeriod(db);

            await _context.SaveChangesAsync();

            var projectId = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == db.PhaseId)
                .Select(p => (int?)p.ProjectId)
                .FirstOrDefaultAsync();

            return RedirectToAction(nameof(Index), new { projectId });
        }

        // =====================================================
        // AJAX
        // =====================================================
        [HttpGet]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> GetPhasePlan(int phaseId)
        {
            var phase = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == phaseId)
                .Select(p => new
                {
                    role = p.PhaseName,
                    planStart = p.PlanStart,
                    planEnd = p.PlanEnd,
                    phaseOrder = p.PhaseOrder,
                    periodOrder = p.PeriodOrder
                })
                .FirstOrDefaultAsync();

            if (phase == null)
                return NotFound();

            // ✅ return เป็น string yyyy-MM-dd เพื่อให้ใส่เข้า <input type="date"> ได้ทันที
            return Json(new
            {
                role = phase.role,
                planStart = phase.planStart.HasValue ? phase.planStart.Value.ToString("yyyy-MM-dd") : "",
                planEnd = phase.planEnd.HasValue ? phase.planEnd.Value.ToString("yyyy-MM-dd") : "",
                phaseOrder = phase.phaseOrder,
                periodOrder = phase.periodOrder
            });
        }

        // =====================================================
        // PRINT REPORT (⭐ FIX NULL HERE)
        // =====================================================
        [RequireMenu("PhaseAssigns.Print")]
        [HttpGet]
        public async Task<IActionResult> Print(int? projectId, int? empId, string? role, int? departmentId)
        {
            departmentId = await ReportDepartmentSupport.LoadAsync(this, _context, departmentId);
            var selectedRole = await LoadPrintReportFiltersAsync(projectId, empId, role, departmentId);
            return View(await BuildPrintReportRowsAsync(projectId, empId, selectedRole, departmentId));
        }

        [RequireMenu("PhaseAssigns.Print")]
        [HttpGet]
        public async Task<IActionResult> PrintTable(int? projectId, int? empId, string? role, int? departmentId)
        {
            departmentId = await ReportDepartmentSupport.LoadAsync(this, _context, departmentId);
            var selectedRole = await LoadPrintReportFiltersAsync(projectId, empId, role, departmentId);
            ViewBag.PrintDate = DateTime.Now;
            return View(await BuildPrintReportRowsAsync(projectId, empId, selectedRole, departmentId));
        }

        [RequireMenu("PhaseAssigns.Index")]
        [HttpGet]
        public async Task<IActionResult> ViewOnly(string? coopName, int? projectId, int? empId, string? workStatus)
        {
            var today = DateTime.Today;

            ViewBag.Projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .Include(p => p.BA)
                .Include(p => p.TeamMembers)
                    .ThenInclude(m => m.Employee)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            ViewBag.EmployeeList = await _context.Employees
                .AsNoTracking()
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            var query =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join p in _context.Projects.AsNoTracking().Include(x => x.Coop) on ph.ProjectId equals p.ProjectId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                join statusDefinition in _context.PhaseAssignStatuses.AsNoTracking()
                    on a.StatusId equals statusDefinition.StatusId into statusDefinitions
                from statusDefinition in statusDefinitions.DefaultIfEmpty()
                select new PhaseAssign
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    PhaseOrder = a.PhaseOrder,
                    PhaseSort = a.PhaseSort,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    PlanStart = a.PlanStart,
                    PlanEnd = a.PlanEnd,
                    CreatedAt = a.CreatedAt,
                    WorkStatus = a.WorkStatus,
                    StatusId = a.StatusId,
                    StatusDefinition = statusDefinition,
                    Remark = a.Remark,
                    Phase = ph,
                    Employee = e
                };

            var rows = await query.ToListAsync();
            foreach (var row in rows)
            {
                row.Phase!.Project = ViewBag.Projects is List<Project> projects
                    ? projects.FirstOrDefault(p => p.ProjectId == row.Phase.ProjectId)
                    : null;
            }

            var filtered = rows.AsEnumerable();

            if (projectId.HasValue)
                filtered = filtered.Where(x => x.Phase?.ProjectId == projectId.Value);
            else if (!string.IsNullOrWhiteSpace(coopName))
                filtered = filtered.Where(x => string.Equals(x.Phase?.Project?.Coop?.CoopName, coopName, StringComparison.OrdinalIgnoreCase));

            if (empId.HasValue)
                filtered = filtered.Where(x => x.EmpId == empId.Value);

            if (IsDelayFilter(workStatus))
            {
                filtered = filtered.Where(x =>
                    x.PlanEnd.HasValue &&
                    x.PlanEnd.Value.Date < today &&
                    !IsAssignDone(x.WorkStatus));
            }
            else if (!string.IsNullOrWhiteSpace(workStatus))
            {
                filtered = filtered.Where(x =>
                    string.Equals(x.WorkStatus, workStatus, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.StatusDefinition?.StatusCode, workStatus, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.StatusDefinition?.StatusDesc, workStatus, StringComparison.OrdinalIgnoreCase));
            }

            ViewBag.SelectedCoopName = coopName;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedEmpId = empId;
            ViewBag.SelectedWorkStatus = workStatus;
            ViewBag.PhaseAssignStatuses = await _workflowStatusService.GetActiveAsync(WorkflowStatusTypes.PhaseAssign);
            ViewBag.Today = today;

            return View(filtered
                .OrderBy(a => a.Phase?.Project?.ProjectDisplayName ?? "")
                .ThenBy(a => a.Phase == null ? (a.PhaseOrder ?? int.MaxValue) : a.Phase.PhaseOrder)
                .ThenBy(a => a.Phase == null ? int.MaxValue : a.Phase.PeriodOrder)
                .ThenBy(a => a.PhaseSort ?? int.MaxValue)
                .ThenBy(a => a.AssignId)
                .ToList());
        }

        private static bool IsDelayFilter(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized is "DELAY" or "ล่าช้า" or "OVERDUE";
        }

        private static bool IsAssignDone(string? status)
        {
            return string.Equals((status ?? "").Trim(), "DONE", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> LoadPrintReportFiltersAsync(int? projectId, int? empId, string? role, int? departmentId)
        {
            ViewBag.Projects = await _context.Projects
                .Include(p => p.Coop)
                .Where(p => !departmentId.HasValue || p.DepartmentId == departmentId.Value)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedEmpId = empId;
            ViewBag.SelectedRole = null;

            ViewBag.EmployeeList = new List<Employee>();
            ViewBag.RoleList = new List<string>();

            ViewBag.SelectedProject = projectId.HasValue
                ? await _context.Projects
                    .Include(p => p.Coop)
                    .FirstOrDefaultAsync(p => p.ProjectId == projectId.Value)
                : null;

            // Employee dropdown
            var employeeQuery =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                select new { a.EmpId, e.EmpName, ph.ProjectId };

            if (projectId.HasValue)
                employeeQuery = employeeQuery.Where(x => x.ProjectId == projectId.Value);

            ViewBag.EmployeeList = await employeeQuery
                .GroupBy(x => new { x.EmpId, x.EmpName })
                .OrderBy(g => g.Key.EmpName)
                .Select(g => new Employee
                {
                    EmpId = g.Key.EmpId,
                    EmpName = g.Key.EmpName
                })
                .ToListAsync();

            var roleQuery =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                where a.Role != null && a.Role != ""
                select new
                {
                    a.EmpId,
                    ph.ProjectId,
                    a.Role
                };

            if (projectId.HasValue)
                roleQuery = roleQuery.Where(x => x.ProjectId == projectId.Value);

            if (empId.HasValue)
                roleQuery = roleQuery.Where(x => x.EmpId == empId.Value);

            var roleList = await roleQuery
                .Select(x => x.Role!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var selectedRole = !string.IsNullOrWhiteSpace(role) && roleList.Contains(role)
                ? role
                : null;

            ViewBag.RoleList = roleList;
            ViewBag.SelectedRole = selectedRole;

            return selectedRole;
        }

        private async Task<List<PhaseAssign>> BuildPrintReportRowsAsync(int? projectId, int? empId, string? role, int? departmentId)
        {
            var query =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                join project in _context.Projects.AsNoTracking() on ph.ProjectId equals project.ProjectId
                where !departmentId.HasValue || project.DepartmentId == departmentId.Value
                select new PhaseAssign
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    PhaseOrder = a.PhaseOrder,
                    PhaseSort = a.PhaseSort,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    PlanStart = a.PlanStart,
                    PlanEnd = a.PlanEnd,
                    WorkStatus = a.WorkStatus,
                    Remark = a.Remark,

                    Phase = ph,
                    Employee = e,

                    Logs = _context.PhaseAssignLogs
                        .Where(l => l.AssignId == a.AssignId)
                        .OrderBy(l => l.RoundNo)
                        .ToList()
                };

            if (projectId.HasValue)
                query = query.Where(x => x.Phase != null && x.Phase.ProjectId == projectId.Value);

            if (empId.HasValue)
                query = query.Where(x => x.EmpId == empId.Value);

            if (!string.IsNullOrEmpty(role))
                query = query.Where(x => x.Role == role);

            return await query
                .OrderBy(a => a.Phase == null ? (a.PhaseOrder ?? int.MaxValue) : a.Phase.PhaseOrder)
                .ThenBy(a => a.Phase == null ? int.MaxValue : a.Phase.PeriodOrder)
                .ThenBy(a => a.PhaseSort ?? int.MaxValue)
                .ThenBy(a => a.AssignId)
                .ToListAsync();
        }

        // =====================================================
        // PRINT FORM
        // =====================================================
        [HttpGet]
        [RequireMenu("PhaseAssigns.Print")]
        public async Task<IActionResult> Form(int id)
        {
            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) / ThenInclude(Project) เพื่อเลี่ยง PhaseId2
            // ดึง Assign + Employee ก่อน
            var assign = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(a => a.Employee)
                .Include(a => a.Logs)
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (assign == null)
                return NotFound();

            // ดึง Phase (ProjectPhase) แยกต่างหาก
            var phase = await _context.ProjectPhases
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PhaseId == assign.PhaseId);

            if (phase != null)
            {
                // ผูก navigation เพื่อให้ View ที่อ้าง assign.Phase.* ยังทำงานได้
                assign.Phase = phase;

                // ดึง Project และผูกให้ Phase.Project เพื่อให้ View ที่อ้าง Phase.Project.* ยังทำงานได้
                var project = await _context.Projects
                    .AsNoTracking()
                    .Include(pr => pr.Coop)
                    .FirstOrDefaultAsync(pr => pr.ProjectId == phase.ProjectId);

                if (project != null)
                {
                    phase.Project = project;
                }
            }

            // ดึง BA ของโครงการนี้ (อิงจาก PhaseAssign ของ project เดียวกัน)
            if (phase != null)
            {
                ViewBag.BusinessAnalyst = await (
                    from a in _context.PhaseAssigns.AsNoTracking()
                    join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                    join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                    where ph.ProjectId == phase.ProjectId
                          && e.Position == "Business Analyst"
                          && e.Status == "ACTIVE"
                    select e
                ).FirstOrDefaultAsync();
            }
            else
            {
                ViewBag.BusinessAnalyst = null;
            }

            return View(assign);
        }

        // =====================================================
        // DELETE
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) เพื่อเลี่ยง PhaseId2
            var assign = await _context.PhaseAssigns
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (assign == null)
                return NotFound();

            var projectId = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.PhaseId == assign.PhaseId)
                .Select(p => (int?)p.ProjectId)
                .FirstOrDefaultAsync();

            assign.CreatedAt = DateTime.Now;
            assign.EntryId = await GetCurrentEntryIdAsync();
            await _context.SaveChangesAsync();

            _context.PhaseAssigns.Remove(assign);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId });
        }

        // =====================================================
        // SAVE LOG (PASS / REWORK)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Edit")]
        public async Task<IActionResult> SaveLog(int assignId, string status, string? remark)
        {
            try
            {
                status = (status ?? "").Trim().ToUpperInvariant();
                if (status != "PASS" && status != "REWORK")
                    return BadRequest(new { success = false, message = "Invalid status" });

                var assign = await _context.PhaseAssigns
                    .FirstOrDefaultAsync(x => x.AssignId == assignId);
                if (assign == null)
                    return NotFound(new { success = false, message = "Assign not found" });

                // 🔍 หา round ล่าสุด
                var lastRound = await _context.PhaseAssignLogs
                    .Where(x => x.AssignId == assignId)
                    .OrderByDescending(x => x.RoundNo)
                    .Select(x => x.RoundNo)
                    .FirstOrDefaultAsync();

                int nextRound = (lastRound ?? 0) + 1;

                var userId = HttpContext.Session.GetInt32("UserId");

                var log = new PhaseAssignLog
                {
                    AssignId = assignId,
                    Status = status,
                    Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
                    RoundNo = nextRound,
                    CreatedBy = userId,
                    CreatedAt = DateTime.Now
                };

                _context.PhaseAssignLogs.Add(log);

                assign.WorkStatus = status == "PASS" ? "DONE" : "IN_PROGRESS";
                SyncActualPeriod(assign);
                assign.CreatedAt = DateTime.Now;
                assign.EntryId = await GetCurrentEntryIdAsync();

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    round = nextRound,
                    workStatus = assign.WorkStatus
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        private static void SyncActualPeriod(PhaseAssign assign)
        {
            // ผู้ใช้ไม่ต้องกรอก Actual Start เอง: ให้ยึดวันเริ่มตามแผนเสมอ
            assign.ActualStart = assign.PlanStart?.Date;

            if (StatusApprovalService.IsPhaseAssignCompletionStatus(assign.WorkStatus))
            {
                assign.ActualEnd ??= DateTime.Today;
                return;
            }

            // เมื่อเปิดงานกลับมาแก้ไขใหม่ ให้รอบปัจจุบันยังไม่มีวันสิ้นสุด
            // ประวัติ PASS / REWORK ยังคงอยู่ใน phase_assign_logs
            assign.ActualEnd = null;
        }

        // =====================================================
        // REORDER (Drag & Drop -> Persist to phase_sort)
        // =====================================================
        public sealed class ReorderRequest
        {
            [JsonPropertyName("phaseId")]
            public int PhaseId { get; set; }

            // ordered assignIds within the phase
            [JsonPropertyName("assignIds")]
            public List<int> AssignIds { get; set; } = new();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Reorder()
        {
            // ✅ Goal: accept many payload shapes from JS drag/drop and persist to phase_sort
            // Supports:
            // - JSON: { phaseId, assignIds:[...] }
            // - JSON: { phase_id, assign_ids:[...] }
            // - JSON: { phaseId, ids:[...] }
            // - JSON: { phaseId, order:[...] } (numbers or objects with assignId)
            // - JSON: { items:[{assignId:1},{assignId:2}] }
            // - JSON: [1,2,3] (projectId/phaseId via query string)
            // - Form: projectId=.. / phaseId=.. & assignIds=1,2,3 OR assignIds[]=1&assignIds[]=2
            //
            // IMPORTANT CHANGE:
            // - Allow reordering across phases by validating by PROJECT when projectId is provided (UI uses one tbody).
            // - If projectId is not provided, fall back to old behavior (phase-based reorder).

            try
            {
                int phaseId = 0;
                int projectId = 0;
                var ids = new List<int>();
                string? rawBody = null;

                // Query string fallback
                var qProjectId = Request.Query["projectId"].FirstOrDefault()
                              ?? Request.Query["ProjectId"].FirstOrDefault()
                              ?? Request.Query["project_id"].FirstOrDefault();
                int.TryParse(qProjectId, out projectId);

                var qPhaseId = Request.Query["phaseId"].FirstOrDefault()
                            ?? Request.Query["PhaseId"].FirstOrDefault()
                            ?? Request.Query["phase_id"].FirstOrDefault();
                int.TryParse(qPhaseId, out phaseId);

                // 1) Form (application/x-www-form-urlencoded | multipart/form-data)
                if (Request.HasFormContentType)
                {
                    var projectIdStr = Request.Form["projectId"].FirstOrDefault()
                                   ?? Request.Form["ProjectId"].FirstOrDefault()
                                   ?? Request.Form["project_id"].FirstOrDefault();
                    if (projectId <= 0) int.TryParse(projectIdStr, out projectId);

                    var phaseIdStr = Request.Form["phaseId"].FirstOrDefault()
                                   ?? Request.Form["PhaseId"].FirstOrDefault()
                                   ?? Request.Form["phase_id"].FirstOrDefault();
                    if (phaseId <= 0) int.TryParse(phaseIdStr, out phaseId);

                    var csv = Request.Form["assignIds"].FirstOrDefault()
                           ?? Request.Form["AssignIds"].FirstOrDefault()
                           ?? Request.Form["assign_ids"].FirstOrDefault()
                           ?? Request.Form["ids"].FirstOrDefault()
                           ?? Request.Form["Ids"].FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(csv))
                    {
                        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (int.TryParse(part, out var v)) ids.Add(v);
                    }

                    foreach (var k in new[] { "assignIds[]", "AssignIds[]", "assign_ids[]", "ids[]", "Ids[]" })
                    {
                        if (Request.Form.TryGetValue(k, out var values))
                        {
                            foreach (var s in values)
                                if (int.TryParse(s, out var v)) ids.Add(v);
                        }
                    }
                }
                else
                {
                    // 2) JSON / Raw body
                    Request.EnableBuffering();
                    Request.Body.Position = 0;
                    using (var reader = new StreamReader(Request.Body, leaveOpen: true))
                    {
                        rawBody = await reader.ReadToEndAsync();
                    }
                    Request.Body.Position = 0;

                    var body = rawBody ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        // Try strong type first
                        try
                        {
                            var req = JsonSerializer.Deserialize<ReorderRequest>(body, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            if (req != null)
                            {
                                if (phaseId <= 0) phaseId = req.PhaseId;
                                if (req.AssignIds != null) ids.AddRange(req.AssignIds);
                            }
                        }
                        catch
                        {
                            // ignore, will try flexible parsing
                        }

                        // Flexible parsing
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            var root = doc.RootElement;

                            if (root.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var el in root.EnumerateArray())
                                {
                                    if (el.ValueKind == JsonValueKind.Number) ids.Add(el.GetInt32());
                                    else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var v)) ids.Add(v);
                                }
                            }
                            else if (root.ValueKind == JsonValueKind.Object)
                            {
                                // projectId
                                if (projectId <= 0)
                                {
                                    if (root.TryGetProperty("projectId", out var pid) && pid.ValueKind == JsonValueKind.Number) projectId = pid.GetInt32();
                                    else if (root.TryGetProperty("ProjectId", out var pid2) && pid2.ValueKind == JsonValueKind.Number) projectId = pid2.GetInt32();
                                    else if (root.TryGetProperty("project_id", out var pid3) && pid3.ValueKind == JsonValueKind.Number) projectId = pid3.GetInt32();
                                    else if (root.TryGetProperty("project_id", out var pid4) && pid4.ValueKind == JsonValueKind.String && int.TryParse(pid4.GetString(), out var vpid4)) projectId = vpid4;
                                }

                                // phaseId
                                if (phaseId <= 0)
                                {
                                    if (root.TryGetProperty("phaseId", out var pid) && pid.ValueKind == JsonValueKind.Number) phaseId = pid.GetInt32();
                                    else if (root.TryGetProperty("PhaseId", out var pid2) && pid2.ValueKind == JsonValueKind.Number) phaseId = pid2.GetInt32();
                                    else if (root.TryGetProperty("phase_id", out var pid3) && pid3.ValueKind == JsonValueKind.Number) phaseId = pid3.GetInt32();
                                    else if (root.TryGetProperty("phase_id", out var pid4) && pid4.ValueKind == JsonValueKind.String && int.TryParse(pid4.GetString(), out var vpid4)) phaseId = vpid4;
                                }

                                void ReadIdArray(string propName)
                                {
                                    if (!root.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                                    foreach (var el in arr.EnumerateArray())
                                    {
                                        if (el.ValueKind == JsonValueKind.Number) ids.Add(el.GetInt32());
                                        else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var v)) ids.Add(v);
                                        else if (el.ValueKind == JsonValueKind.Object)
                                        {
                                            if (el.TryGetProperty("assignId", out var aid) && aid.ValueKind == JsonValueKind.Number) ids.Add(aid.GetInt32());
                                            else if (el.TryGetProperty("AssignId", out var aid2) && aid2.ValueKind == JsonValueKind.Number) ids.Add(aid2.GetInt32());
                                            else if (el.TryGetProperty("assign_id", out var aid3) && aid3.ValueKind == JsonValueKind.Number) ids.Add(aid3.GetInt32());
                                            else if (el.TryGetProperty("id", out var aid4) && aid4.ValueKind == JsonValueKind.Number) ids.Add(aid4.GetInt32());
                                        }
                                    }
                                }

                                ReadIdArray("assignIds");
                                ReadIdArray("AssignIds");
                                ReadIdArray("assign_ids");
                                ReadIdArray("ids");
                                ReadIdArray("Ids");
                                ReadIdArray("order");
                                ReadIdArray("Order");
                                ReadIdArray("sortedIds");
                                ReadIdArray("SortedIds");
                                ReadIdArray("items");
                                ReadIdArray("Items");

                                if (ids.Count == 0 && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                                {
                                    if (data.TryGetProperty("assignIds", out var a1) && a1.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var el in a1.EnumerateArray())
                                            if (el.ValueKind == JsonValueKind.Number) ids.Add(el.GetInt32());
                                    }
                                    else if (data.TryGetProperty("ids", out var a2) && a2.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var el in a2.EnumerateArray())
                                            if (el.ValueKind == JsonValueKind.Number) ids.Add(el.GetInt32());
                                    }

                                    if (projectId <= 0)
                                    {
                                        if (data.TryGetProperty("projectId", out var p1) && p1.ValueKind == JsonValueKind.Number) projectId = p1.GetInt32();
                                        else if (data.TryGetProperty("project_id", out var p2) && p2.ValueKind == JsonValueKind.Number) projectId = p2.GetInt32();
                                    }

                                    if (phaseId <= 0)
                                    {
                                        if (data.TryGetProperty("phaseId", out var p1) && p1.ValueKind == JsonValueKind.Number) phaseId = p1.GetInt32();
                                        else if (data.TryGetProperty("phase_id", out var p2) && p2.ValueKind == JsonValueKind.Number) phaseId = p2.GetInt32();
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }

                // preserve order (first occurrence wins)
                var seen = new HashSet<int>();
                ids = ids.Where(x => x > 0 && seen.Add(x)).ToList();

                // Infer projectId if missing but we do have assign ids
                if (projectId <= 0 && ids.Count > 0)
                {
                    projectId = await (
                        from a in _context.PhaseAssigns.AsNoTracking()
                        join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                        where a.AssignId == ids[0]
                        select ph.ProjectId
                    ).FirstOrDefaultAsync();
                }

                // If we still don't have projectId, infer phaseId (legacy) if possible
                if (phaseId <= 0 && ids.Count > 0)
                {
                    phaseId = await _context.PhaseAssigns
                        .AsNoTracking()
                        .Where(a => a.AssignId == ids[0])
                        .Select(a => a.PhaseId)
                        .FirstOrDefaultAsync();
                }

                if (ids.Count == 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "Invalid payload",
                        projectId,
                        phaseId,
                        count = 0,
                        contentType = Request.ContentType,
                        bodyLength = rawBody?.Length ?? 0
                    });
                }

                // =====================================================
                // ✅ New behavior: project-based reorder (allows cross-phase)
                // =====================================================
                if (projectId > 0)
                {
                    // Load all assigns in the project (so we can keep a stable total order)
                    var allRows = await (
                        from a in _context.PhaseAssigns
                        join ph in _context.ProjectPhases on a.PhaseId equals ph.PhaseId
                        where ph.ProjectId == projectId
                        select a
                    ).ToListAsync();

                    if (allRows.Count == 0)
                        return NotFound(new { ok = false, message = "Project not found or no assignments", projectId });

                    var allMap = allRows.ToDictionary(x => x.AssignId, x => x);

                    // Validate: payload IDs must belong to this project
                    var extra = ids.Where(id => !allMap.ContainsKey(id)).Distinct().ToList();
                    if (extra.Count > 0)
                    {
                        return BadRequest(new
                        {
                            ok = false,
                            message = "Payload contains assignIds that do not belong to this project.",
                            projectId,
                            extraIds = extra
                        });
                    }

                    // For IDs not included in payload, append them after payload in a deterministic order
                    var remaining = allRows
                        .Where(r => !ids.Contains(r.AssignId))
                        .OrderBy(r => r.PhaseSort ?? int.MaxValue)
                        .ThenBy(r => r.AssignId)
                        .Select(r => r.AssignId)
                        .ToList();

                    var finalOrder = ids.Concat(remaining).ToList();

                    var entryId = await GetCurrentEntryIdAsync();
                    var reorderedAt = DateTime.Now;
                    int sort = 1;
                    foreach (var id in finalOrder)
                    {
                        if (allMap.TryGetValue(id, out var row))
                        {
                            row.PhaseSort = sort;
                            row.CreatedAt = reorderedAt;
                            row.EntryId = entryId;
                            sort++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    return Ok(new { ok = true, projectId, count = ids.Count, total = allRows.Count });
                }

                // =====================================================
                // ✅ Legacy behavior: phase-based reorder (same as before)
                // =====================================================
                if (phaseId <= 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "Missing projectId/phaseId",
                        projectId,
                        phaseId,
                        count = ids.Count
                    });
                }

                var rows = await _context.PhaseAssigns
                    .Where(a => a.PhaseId == phaseId)
                    .ToListAsync();

                if (rows.Count == 0)
                    return NotFound(new { ok = false, message = "Phase not found or no assignments", phaseId });

                var map = rows.ToDictionary(x => x.AssignId, x => x);

                var allIds = map.Keys.OrderBy(x => x).ToList();
                var missing = allIds.Except(ids).ToList();
                var extraPhase = ids.Except(allIds).ToList();

                if (extraPhase.Count > 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "Payload contains assignIds that do not belong to this phase.",
                        phaseId,
                        extraIds = extraPhase
                    });
                }

                if (missing.Count > 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "Incomplete reorder payload. Please send ALL assignIds in the phase in the new UI order.",
                        phaseId,
                        receivedCount = ids.Count,
                        totalCount = rows.Count,
                        missingIds = missing
                    });
                }

                int sort2 = 1;
                var legacyEntryId = await GetCurrentEntryIdAsync();
                var legacyReorderedAt = DateTime.Now;
                foreach (var id in ids)
                {
                    if (map.TryGetValue(id, out var row))
                    {
                        row.PhaseSort = sort2;
                        row.CreatedAt = legacyReorderedAt;
                        row.EntryId = legacyEntryId;
                        sort2++;
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { ok = true, phaseId, count = ids.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, message = ex.Message });
            }
        }
        // =====================================================
        // HELPER
        // =====================================================
        private async Task ReloadCreateDropdown(int projectId, PhaseAssign model)
        {
            ViewBag.ProjectId = projectId;

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .OrderBy(p => p.PhaseOrder)
                .ThenBy(p => p.PeriodOrder)
                .ThenBy(p => p.PhaseSort == 0 ? int.MaxValue : p.PhaseSort)
                .ThenBy(p => p.PhaseId)
                .ToListAsync();

            ViewBag.Phases = new SelectList(phases, "PhaseId", "PhaseDisplayName", model.PhaseId);
            ViewBag.PhaseItems = phases;

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName",
                model.EmpId);
            await LoadAssignStatusLookupAsync(model.StatusId);
        }

        private async Task LoadAssignStatusLookupAsync(int? selectedStatusId)
        {
            ViewBag.PhaseAssignStatuses = new SelectList(
                await _workflowStatusService.GetActiveAsync(WorkflowStatusTypes.PhaseAssign),
                nameof(StatusDefinitionOption.StatusId),
                nameof(StatusDefinitionOption.StatusDesc),
                selectedStatusId);
        }

        private async Task SendCreatedPhaseAssignNotificationSafelyAsync(int assignId)
        {
            var sendLine = _lineMessagingService.IsConfigured
                && await _lineNotificationSettings.IsEnabledAsync(LineNotificationFeatures.PhaseAssignsCreate, HttpContext.RequestAborted);
            var sendTelegram = _telegramMessagingService.IsConfigured
                && await _telegramNotificationSettings.IsEnabledAsync(TelegramNotificationFeatures.PhaseAssignsCreate, HttpContext.RequestAborted);

            if (!sendLine && !sendTelegram)
                return;

            try
            {
                var assign = await LoadPhaseAssignNotificationAsync(assignId);
                if (assign == null)
                    return;

                var recipients = new Dictionary<int, string>();
                var projectUrl = $"/PhaseAssigns/Index?projectId={assign.ProjectId}";
                var ownerUrl = $"/PhaseAssigns/Index?projectId={assign.ProjectId}&empId={assign.EmpId}";

                foreach (var baEmpId in assign.BaEmpIds)
                    recipients[baEmpId] = projectUrl;

                if (assign.EmpId > 0)
                    recipients.TryAdd(assign.EmpId, ownerUrl);

                if (recipients.Count == 0)
                    return;

                var title = "แจ้ง Assign ใหม่:";
                var message = BuildCreatedPhaseAssignTelegramMessage(assign);

                foreach (var recipient in recipients)
                {
                    await SendChatNotificationToEmployeeSafelyAsync(
                        recipient.Key,
                        title,
                        message,
                        recipient.Value,
                        sendLine,
                        sendTelegram);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send created phase assign notification failed. AssignId={AssignId}", assignId);
            }
        }

        private async Task SendChatNotificationToEmployeeSafelyAsync(
            int empId,
            string title,
            string message,
            string targetUrl,
            bool sendLine,
            bool sendTelegram)
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
                    _logger.LogWarning(ex, "LINE phase assign notification failed. EmpId={EmpId}", empId);
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
                    _logger.LogWarning(ex, "Telegram phase assign notification failed. EmpId={EmpId}", empId);
                }
            }
        }

        private async Task<PhaseAssignNotificationRow?> LoadPhaseAssignNotificationAsync(int assignId)
        {
            var query =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join p in _context.Projects.AsNoTracking() on ph.ProjectId equals p.ProjectId
                join coop in _context.CntMCoops.AsNoTracking() on p.CoopId equals (int?)coop.CoopId into coopJoin
                from coop in coopJoin.DefaultIfEmpty()
                join emp in _context.Employees.AsNoTracking() on a.EmpId equals emp.EmpId into empJoin
                from emp in empJoin.DefaultIfEmpty()
                join ba in _context.Employees.AsNoTracking() on p.BaEmpId equals (int?)ba.EmpId into baJoin
                from ba in baJoin.DefaultIfEmpty()
                where a.AssignId == assignId
                select new PhaseAssignNotificationRow
                {
                    AssignId = a.AssignId,
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName,
                    CoopName = coop != null ? coop.CoopName : null,
                    PhaseName = ph.PhaseName,
                    PhaseOrder = ph.PhaseOrder,
                    PeriodOrder = ph.PeriodOrder,
                    Role = a.Role,
                    EmpId = a.EmpId,
                    OwnerName = emp != null ? emp.EmpName : null,
                    BaEmpId = p.BaEmpId,
                    BaName = ba != null ? ba.EmpName : null,
                    PlanStart = a.PlanStart ?? ph.PlanStart,
                    PlanEnd = a.PlanEnd ?? ph.PlanEnd,
                    PeriodEndDate = ph.PeriodEndDate,
                    WorkStatus = a.WorkStatus,
                    Remark = a.Remark
                };

            var row = await query.FirstOrDefaultAsync(HttpContext.RequestAborted);
            if (row == null)
                return null;

            row.BaEmpIds = await _context.ProjectTeamMembers
                .AsNoTracking()
                .Where(member => member.ProjectId == row.ProjectId
                    && member.MemberRole == ProjectTeamRoles.BusinessAnalyst)
                .OrderBy(member => member.SortOrder)
                .Select(member => member.EmpId)
                .Distinct()
                .ToListAsync(HttpContext.RequestAborted);
            if (row.BaEmpIds.Count == 0 && row.BaEmpId is > 0)
                row.BaEmpIds.Add(row.BaEmpId.Value);

            return row;
        }

        private static string BuildCreatedPhaseAssignTelegramMessage(PhaseAssignNotificationRow assign)
        {
            var rows = new List<string>
            {
                $"สหกรณ์: {TextOrDash(assign.CoopName)}",
                $"Project: {ProjectNameForTelegram(assign)}",
                $"Phase: ส่วนที่ {assign.PhaseOrder} งวดที่ {assign.PeriodOrder} - {TextOrDash(assign.PhaseName)}",
                $"งาน: {TextOrDash(assign.Role)}",
                $"ผู้รับผิดชอบ: {TextOrDash(assign.OwnerName)}",
                $"BA: {TextOrDash(assign.BaName)}",
                $"Plan: {ThaiDateText(assign.PlanStart)} - {ThaiDateText(assign.PlanEnd)}",
                $"กำหนดงวดงาน: {ThaiDateText(assign.PeriodEndDate)}",
                $"สถานะ: {TextOrDash(assign.WorkStatus)}"
            };

            if (!string.IsNullOrWhiteSpace(assign.Remark))
                rows.Add($"Remark: {assign.Remark.Trim()}");

            return string.Join(Environment.NewLine, rows);
        }

        private static string ProjectNameForTelegram(PhaseAssignNotificationRow assign)
            => string.IsNullOrWhiteSpace(assign.CoopName)
                ? TextOrDash(assign.ProjectName)
                : $"{assign.CoopName} - {TextOrDash(assign.ProjectName)}";

        private static string ThaiDateText(DateTime? value)
            => value.HasValue
                ? value.Value.ToString("dd MMM yyyy", new CultureInfo("th-TH"))
                : "-";

        private static string TextOrDash(string? value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

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

        private sealed class PhaseAssignNotificationRow
        {
            public int AssignId { get; set; }
            public int ProjectId { get; set; }
            public string? ProjectName { get; set; }
            public string? CoopName { get; set; }
            public string? PhaseName { get; set; }
            public int PhaseOrder { get; set; }
            public int PeriodOrder { get; set; }
            public string? Role { get; set; }
            public int EmpId { get; set; }
            public string? OwnerName { get; set; }
            public int? BaEmpId { get; set; }
            public List<int> BaEmpIds { get; set; } = new();
            public string? BaName { get; set; }
            public DateTime? PlanStart { get; set; }
            public DateTime? PlanEnd { get; set; }
            public DateTime? PeriodEndDate { get; set; }
            public string? WorkStatus { get; set; }
            public string? Remark { get; set; }
        }
    }
}
