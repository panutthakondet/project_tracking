using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.Services;

namespace ProjectTracking.Controllers
{
    public class ProjectPhasesController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly StatusApprovalService _statusApprovalService;
        private const string SubmittedStatus = "ส่งงวดงานแล้ว";
        private const string ApprovedPaymentStatus = "อนุมัติจ่ายเงินแล้ว";

        public ProjectPhasesController(
            AppDbContext context,
            StatusApprovalService statusApprovalService)
        {
            _context = context;
            _statusApprovalService = statusApprovalService;
        }

        // ===========================
        // INDEX
        // ===========================
        [RequireMenu("ProjectPhases.Index")]
        public async Task<IActionResult> Index(int? projectId)
        {
            ViewBag.Projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .Include(p => p.PM)
                    .ThenInclude(e => e!.LoginUser)
                .Include(p => p.BA)
                .Include(p => p.TeamMembers)
                    .ThenInclude(m => m.Employee)
                        .ThenInclude(e => e!.LoginUser)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            if (projectId == null)
            {
                ViewBag.SelectedProject = null;
                ViewBag.PendingPhaseApprovalIds = new HashSet<int>();
                return View(new List<ProjectPhase>());
            }

            var selectedProject = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .Include(p => p.PM)
                    .ThenInclude(e => e!.LoginUser)
                .Include(p => p.BA)
                    .ThenInclude(e => e!.LoginUser)
                .Include(p => p.TeamMembers)
                    .ThenInclude(m => m.Employee)
                        .ThenInclude(e => e!.LoginUser)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (selectedProject == null)
            {
                ViewBag.SelectedProject = null;
                ViewBag.PendingPhaseApprovalIds = new HashSet<int>();
                return View(new List<ProjectPhase>());
            }

            ViewBag.SelectedProject = selectedProject;

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .OrderBy(p => p.PhaseOrder)
                .ThenBy(p => p.PeriodOrder)
                .ThenBy(p => p.PhaseSort == 0 ? int.MaxValue : p.PhaseSort)
                .ThenBy(p => p.PhaseId)
                .ToListAsync();

            var phaseIds = phases.Select(p => p.PhaseId).ToList();
            ViewBag.PendingPhaseApprovalIds = phaseIds.Count == 0
                ? new HashSet<int>()
                : (await _context.StatusApprovalRequests
                    .AsNoTracking()
                    .Where(r => r.TargetType == StatusApprovalService.TargetProjectPhase
                                && r.RequestStatus == StatusApprovalService.RequestPending
                                && phaseIds.Contains(r.TargetId))
                    .Select(r => r.TargetId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();

            ViewBag.PhaseAssignsByPhaseId = new Dictionary<int, List<PhaseAssign>>();
            if (phaseIds.Count > 0 && CanMenu("PhaseAssigns.Index"))
            {
                var phaseAssigns = await (
                    from assign in _context.PhaseAssigns.AsNoTracking()
                    join employee in _context.Employees.AsNoTracking()
                        on assign.EmpId equals employee.EmpId
                    join loginUser in _context.LoginUsers.AsNoTracking()
                        on employee.LoginUserId equals (int?)loginUser.UserId into loginUserJoin
                    from loginUser in loginUserJoin.DefaultIfEmpty()
                    where phaseIds.Contains(assign.PhaseId)
                    orderby assign.PhaseSort ?? int.MaxValue, assign.AssignId
                    select new PhaseAssign
                    {
                        AssignId = assign.AssignId,
                        PhaseId = assign.PhaseId,
                        EmpId = assign.EmpId,
                        Role = assign.Role,
                        PlanStart = assign.PlanStart,
                        PlanEnd = assign.PlanEnd,
                        WorkStatus = assign.WorkStatus,
                        Remark = assign.Remark,
                        Employee = new Employee
                        {
                            EmpId = employee.EmpId,
                            EmpName = employee.EmpName,
                            Position = employee.Position,
                            Status = employee.Status,
                            LoginUserId = employee.LoginUserId,
                            LoginUser = loginUser
                        }
                    })
                    .ToListAsync();

                ViewBag.PhaseAssignsByPhaseId = phaseAssigns
                    .GroupBy(assign => assign.PhaseId)
                    .ToDictionary(group => group.Key, group => group.ToList());
            }

            return View(phases);
        }

        [RequireMenu("ProjectPhases.Index")]
        public async Task<IActionResult> ViewOnly(string? coopName, int? projectId, string? phaseStatus)
        {
            var today = DateTime.Today;
            var projects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .Include(p => p.BA)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Include(p => p.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(p => p.Project)
                    .ThenInclude(p => p!.BA)
                .Include(p => p.Project)
                    .ThenInclude(p => p!.TeamMembers)
                        .ThenInclude(m => m.Employee)
                .OrderBy(p => p.Project != null && p.Project.Coop != null ? p.Project.Coop.CoopName : "")
                .ThenBy(p => p.Project != null ? p.Project.ProjectName : "")
                .ThenBy(p => p.PhaseOrder)
                .ThenBy(p => p.PeriodOrder)
                .ThenBy(p => p.PhaseSort == 0 ? int.MaxValue : p.PhaseSort)
                .ThenBy(p => p.PhaseId)
                .ToListAsync();

            var query = phases.AsEnumerable();

            if (projectId.HasValue)
                query = query.Where(p => p.ProjectId == projectId.Value);
            else if (!string.IsNullOrWhiteSpace(coopName))
                query = query.Where(p => string.Equals(p.Project?.Coop?.CoopName, coopName, StringComparison.OrdinalIgnoreCase));

            if (IsDelayFilter(phaseStatus))
            {
                query = query.Where(p =>
                    p.PlanEnd.HasValue &&
                    p.PlanEnd.Value.Date < today &&
                    !IsPhaseDone(p.PhaseStatus));
            }
            else if (!string.IsNullOrWhiteSpace(phaseStatus))
            {
                query = query.Where(p => string.Equals(p.PhaseStatus, phaseStatus, StringComparison.OrdinalIgnoreCase));
            }

            ViewBag.Projects = projects;
            ViewBag.SelectedCoopName = coopName;
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedPhaseStatus = phaseStatus;
            ViewBag.Today = today;

            return View(query.ToList());
        }

        private static bool IsDelayFilter(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized is "DELAY" or "ล่าช้า" or "OVERDUE";
        }

        private static bool IsPhaseDone(string? status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized is "ส่งงวดงานแล้ว" or "อนุมัติจ่ายเงินแล้ว" or "DONE";
        }

        // ===========================
        // CREATE (GET)
        // ===========================
        [RequireMenu("ProjectPhases.Create")]
        public async Task<IActionResult> Create(int? projectId)
        {
            if (projectId == null)
                return RedirectToAction(nameof(Index));

            var project = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null)
                return RedirectToAction(nameof(Index));

            // หา section/period ล่าสุดด้วย PhaseId
            var lastPhase = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .OrderByDescending(p => p.PhaseId)
                .FirstOrDefaultAsync();

            ViewBag.SelectedProjectName = project.ProjectDisplayName;

            ViewBag.LastPlanStart = lastPhase?.PlanStart?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPlanEnd = lastPhase?.PlanEnd?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPeriodEnd = lastPhase?.ActualEnd?.ToString("yyyy-MM-dd") ?? "";

            ViewBag.PhaseTypeList = GetPhaseTypeList("MAIN");

            return View(new ProjectPhase
            {
                ProjectId = project.ProjectId,
                PhaseType = "MAIN",
                PhaseOrder = lastPhase?.PhaseOrder ?? 1,
                PeriodOrder = (lastPhase?.PeriodOrder ?? 0) + 1
            });
        }

        // ===========================
        // Helper to parse Thai Buddhist year dates
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

        // ===========================
        // CREATE (POST)
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectPhases.Create")]
        public async Task<IActionResult> Create(ProjectPhase phase)
        {
            // รองรับวันที่ไทย dd/MM/พ.ศ.
            phase.PlanStart = ParseThaiDate(Request.Form["PlanStart"]);
            phase.PlanEnd = ParseThaiDate(Request.Form["PlanEnd"]);
            phase.ActualEnd = ParseThaiDate(Request.Form["ActualEnd"]);

            ModelState.Remove("PlanStart");
            ModelState.Remove("PlanEnd");
            ModelState.Remove("ActualEnd");
            if (phase.ProjectId <= 0)
            {
                ModelState.AddModelError("ProjectId", "กรุณาเลือก Project");
            }

            if (phase.PhaseOrder <= 0)
                ModelState.AddModelError("PhaseOrder", "กรุณาระบุส่วนงาน");

            if (phase.PeriodOrder <= 0)
                ModelState.AddModelError("PeriodOrder", "กรุณาระบุงวดงาน");

            if (!ModelState.IsValid)
            {
                var project = await _context.Projects
                    .AsNoTracking()
                    .Include(p => p.Coop)
                    .FirstOrDefaultAsync(p => p.ProjectId == phase.ProjectId);

                ViewBag.SelectedProjectName = project?.ProjectDisplayName ?? "ไม่พบข้อมูลโครงการ";

                var lastPhase = await _context.ProjectPhases
                    .AsNoTracking()
                    .Where(p => p.ProjectId == phase.ProjectId)
                    .OrderByDescending(p => p.PhaseId)
                    .FirstOrDefaultAsync();

                ViewBag.LastPlanStart = lastPhase?.PlanStart?.ToString("yyyy-MM-dd") ?? "";
                ViewBag.LastPlanEnd = lastPhase?.PlanEnd?.ToString("yyyy-MM-dd") ?? "";
                ViewBag.LastPeriodEnd = lastPhase?.ActualEnd?.ToString("yyyy-MM-dd") ?? "";

                ViewBag.PhaseTypeList = GetPhaseTypeList(phase.PhaseType);

                return View(phase);
            }

            // ✅ ให้รายการใหม่ไปท้ายสุดของ Project นี้
            var lastSort = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == phase.ProjectId)
                .MaxAsync(p => (int?)p.PhaseSort) ?? 0;

            phase.PhaseSort = lastSort + 1;
            phase.CreatedAt = DateTime.Now;
            phase.EntryId = await GetCurrentEntryIdAsync();
            phase.PhaseStatus = NormalizePhaseStatus(phase.PhaseStatus);

            _context.ProjectPhases.Add(phase);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId = phase.ProjectId });
        }

        // ===========================
        // EDIT (GET)
        // ===========================
        [RequireMenu("ProjectPhases.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var phase = await _context.ProjectPhases
                .Include(p => p.Project)
                .FirstOrDefaultAsync(p => p.PhaseId == id);

            if (phase == null)
                return NotFound();

            phase.PhaseStatus = NormalizePhaseStatus(phase.PhaseStatus);

            // หาส่วนงานก่อนหน้าตาม phase_id
            var previousPhase = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == phase.ProjectId &&
                            p.PhaseId < phase.PhaseId)
                .OrderByDescending(p => p.PhaseId)
                .FirstOrDefaultAsync();

            ViewBag.LastPlanStart = previousPhase?.PlanStart?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPlanEnd = previousPhase?.PlanEnd?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPeriodEnd = previousPhase?.ActualEnd?.ToString("yyyy-MM-dd") ?? "";

            ViewBag.PhaseTypeList = GetPhaseTypeList(phase.PhaseType);

            return View(phase);
        }

        // ===========================
        // EDIT (POST)
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectPhases.Edit")]
        public async Task<IActionResult> Edit(int id, ProjectPhase phase)
        {
            if (id != phase.PhaseId)
                return NotFound();

            // รองรับวันที่ไทย dd/MM/พ.ศ.
            phase.PlanStart = ParseThaiDate(Request.Form["PlanStart"]);
            phase.PlanEnd = ParseThaiDate(Request.Form["PlanEnd"]);
            phase.ActualEnd = ParseThaiDate(Request.Form["ActualEnd"]);

            ModelState.Remove("PlanStart");
            ModelState.Remove("PlanEnd");
            ModelState.Remove("ActualEnd");

            if (phase.PhaseOrder <= 0)
                ModelState.AddModelError("PhaseOrder", "กรุณาระบุส่วนงาน");

            if (phase.PeriodOrder <= 0)
                ModelState.AddModelError("PeriodOrder", "กรุณาระบุงวดงาน");

            if (!ModelState.IsValid)
            {
                ViewBag.PhaseTypeList = GetPhaseTypeList(phase.PhaseType);
                return View(phase);
            }

            var existing = await _context.ProjectPhases.FirstOrDefaultAsync(p => p.PhaseId == id);
            if (existing == null)
                return NotFound();

            var oldStatus = existing.PhaseStatus;
            var requestedStatus = NormalizePhaseStatus(phase.PhaseStatus);

            // ✅ อัปเดตเฉพาะฟิลด์ที่แก้ได้จากฟอร์ม (คงค่า PhaseSort เดิมไว้)
            existing.PhaseName = phase.PhaseName;
            existing.PhaseType = phase.PhaseType;
            existing.PhaseOrder = phase.PhaseOrder;
            existing.PeriodOrder = phase.PeriodOrder;
            existing.PlanStart = phase.PlanStart;
            existing.PlanEnd = phase.PlanEnd;
            existing.ActualEnd = phase.ActualEnd;
            existing.CreatedAt = DateTime.Now;
            existing.EntryId = await GetCurrentEntryIdAsync();

            var requirePmApproval = StatusApprovalService.IsProjectPhaseCompletionStatus(requestedStatus)
                && !StatusApprovalService.IsProjectPhaseCompletionStatus(oldStatus)
                && !await _statusApprovalService.CanApplyCompletionStatusImmediatelyAsync(existing.ProjectId);

            if (requirePmApproval)
            {
                existing.PhaseStatus = oldStatus;

                var project = await _context.Projects
                    .AsNoTracking()
                    .Include(p => p.Coop)
                    .FirstOrDefaultAsync(p => p.ProjectId == existing.ProjectId);

                await _statusApprovalService.QueueCompletionRequestAsync(
                    StatusApprovalService.TargetProjectPhase,
                    existing.PhaseId,
                    existing.ProjectId,
                    project?.ProjectDisplayName,
                    existing.PhaseDisplayName,
                    oldStatus,
                    requestedStatus ?? SubmittedStatus,
                    "ขอปรับสถานะงวดงานเป็นเสร็จสิ้น");

                TempData["Success"] = "บันทึกข้อมูลงวดงานแล้ว และส่งคำขออนุมัติสถานะเสร็จสิ้นให้ PM แล้ว";
            }
            else
            {
                existing.PhaseStatus = requestedStatus;
            }

            var linkedAssigns = await _context.PhaseAssigns
                .Where(a => a.PhaseId == existing.PhaseId)
                .ToListAsync();

            foreach (var assign in linkedAssigns)
            {
                assign.PhaseOrder = existing.PhaseOrder;
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId = existing.ProjectId });
        }

        // ===========================
        // DELETE
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectPhases.Delete")]
        public async Task<IActionResult> Delete(int id, int projectId)
        {
            // โหลด Phase แบบ tracked เพื่อให้ลบได้จริง
            var phase = await _context.ProjectPhases
                .FirstOrDefaultAsync(p => p.PhaseId == id);

            if (phase == null)
            {
                TempData["Error"] = "ไม่พบส่วนงานที่ต้องการลบ";
                // ถ้า projectId ไม่ถูกส่งมา ก็ยังกลับไปหน้า Index ได้ (จะเป็น list ว่าง)
                return RedirectToAction(nameof(Index), new { projectId });
            }

            // ✅ ใช้ ProjectId จากข้อมูลจริงเสมอ (ไม่พึ่งค่าที่ส่งมาจากฟอร์ม)
            var realProjectId = phase.ProjectId;

            try
            {
                // ✅ ลบข้อมูลลูกก่อน เพื่อไม่ให้ FK บล็อก (phase_assign.phase_id -> project_phase.phase_id)
                var entryId = await GetCurrentEntryIdAsync();
                var assigns = await _context.Set<PhaseAssign>()
                    .Where(a => a.PhaseId == id)
                    .ToListAsync();

                if (assigns.Count > 0)
                {
                    var deletedAt = DateTime.Now;
                    foreach (var assign in assigns)
                    {
                        assign.CreatedAt = deletedAt;
                        assign.EntryId = entryId;
                    }
                    phase.CreatedAt = deletedAt;
                    phase.EntryId = entryId;
                    await _context.SaveChangesAsync();

                    _context.Set<PhaseAssign>().RemoveRange(assigns);
                }
                else
                {
                    phase.CreatedAt = DateTime.Now;
                    phase.EntryId = entryId;
                    await _context.SaveChangesAsync();
                }

                _context.ProjectPhases.Remove(phase);

                var affected = await _context.SaveChangesAsync();

                if (affected <= 0)
                    TempData["Error"] = "ลบไม่สำเร็จ: ไม่พบแถวที่ถูกลบ (0 rows affected)";
                else
                    TempData["Success"] = assigns.Count > 0
                        ? $"ลบส่วนงานและลบ Assign ที่เกี่ยวข้อง {assigns.Count} รายการเรียบร้อยแล้ว"
                        : "ลบส่วนงานเรียบร้อยแล้ว";

                return RedirectToAction(nameof(Index), new { projectId = realProjectId });
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "ลบไม่ได้: มีข้อมูลอื่นอ้างอิงส่วนงานนี้อยู่ กรุณาตรวจสอบความสัมพันธ์ของข้อมูลก่อน";
                return RedirectToAction(nameof(Index), new { projectId = realProjectId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectPhases.Create")]
        public async Task<IActionResult> ImportFromRequirementCard(int projectId)
        {
            if (projectId <= 0)
            {
                TempData["Error"] = "กรุณาเลือก Project ก่อน Import";
                return RedirectToAction(nameof(Index));
            }

            var project = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null)
            {
                TempData["Error"] = "ไม่พบ Project ที่ต้องการ Import";
                return RedirectToAction(nameof(Index));
            }

            if (!project.RequirementCardId.HasValue)
            {
                TempData["Error"] = "Project นี้ยังไม่ได้ผูก Project Board Card";
                return RedirectToAction(nameof(Index), new { projectId });
            }

            var draftItems = await _context.RequirementCardPhaseItems
                .AsNoTracking()
                .Where(x => x.CardId == project.RequirementCardId.Value)
                .OrderBy(x => x.PhaseSort == 0 ? int.MaxValue : x.PhaseSort)
                .ThenBy(x => x.PhaseOrder)
                .ThenBy(x => x.PeriodOrder)
                .ThenBy(x => x.ItemId)
                .ToListAsync();

            if (draftItems.Count == 0)
            {
                TempData["Error"] = "Project Board Card นี้ยังไม่มีร่างส่วนงาน/งวดงาน";
                return RedirectToAction(nameof(Index), new { projectId });
            }

            var existingPhases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId)
                .Select(x => new
                {
                    x.PhaseOrder,
                    x.PeriodOrder,
                    PhaseName = x.PhaseName.Trim()
                })
                .ToListAsync();

            var existingKeys = existingPhases
                .Select(x => $"{x.PhaseOrder}|{x.PeriodOrder}|{x.PhaseName}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var lastSort = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .MaxAsync(p => (int?)p.PhaseSort) ?? 0;

            var entryId = await GetCurrentEntryIdAsync();
            var now = DateTime.Now;
            var imported = 0;
            var skipped = 0;

            foreach (var item in draftItems)
            {
                var phaseName = (item.PhaseName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(phaseName))
                {
                    skipped++;
                    continue;
                }

                var phaseOrder = Math.Max(1, item.PhaseOrder);
                var periodOrder = Math.Max(1, item.PeriodOrder);
                var key = $"{phaseOrder}|{periodOrder}|{phaseName}";
                if (existingKeys.Contains(key))
                {
                    skipped++;
                    continue;
                }

                _context.ProjectPhases.Add(new ProjectPhase
                {
                    ProjectId = projectId,
                    PhaseName = phaseName,
                    PhaseType = string.Equals(item.PhaseType, "SUPPORT", StringComparison.OrdinalIgnoreCase) ? "SUPPORT" : "MAIN",
                    PhaseOrder = phaseOrder,
                    PeriodOrder = periodOrder,
                    PhaseSort = ++lastSort,
                    PhaseStatus = NormalizePhaseStatus(item.PhaseStatus),
                    PlanStart = item.PlanStart,
                    PlanEnd = item.PlanEnd,
                    PeriodEndDate = item.PeriodEndDate,
                    CreatedAt = now,
                    EntryId = entryId
                });

                existingKeys.Add(key);
                imported++;
            }

            if (imported == 0)
            {
                TempData["Error"] = skipped > 0
                    ? "ไม่มีรายการใหม่ให้ Import เพราะข้อมูลซ้ำกับส่วนงานเดิมแล้ว"
                    : "ไม่มีรายการที่พร้อม Import";
                return RedirectToAction(nameof(Index), new { projectId });
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = skipped > 0
                ? $"Import จาก Project Board แล้ว {imported} รายการ ข้ามรายการซ้ำ/ว่าง {skipped} รายการ"
                : $"Import จาก Project Board แล้ว {imported} รายการ";

            return RedirectToAction(nameof(Index), new { projectId });
        }

        // ===========================
        // REORDER (AJAX)
        // ===========================
        public class ReorderRequest
        {
            public int ProjectId { get; set; }
            public List<int> PhaseIds { get; set; } = new();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("ProjectPhases.Edit")]
        [Consumes("application/json")]
        public async Task<IActionResult> Reorder([FromBody] ReorderRequest? req)
        {
            if (req == null)
                return BadRequest(new { ok = false, message = "invalid payload: body is null" });

            if (req.ProjectId <= 0)
                return BadRequest(new { ok = false, message = "invalid payload: ProjectId" });

            if (req.PhaseIds == null || req.PhaseIds.Count == 0)
                return BadRequest(new { ok = false, message = "invalid payload: PhaseIds" });

            // ✅ รักษาลำดับตามที่ส่งมา (ห้าม Distinct แบบสุ่มลำดับ)
            var orderedIds = new List<int>(req.PhaseIds.Count);
            var seen = new HashSet<int>();
            foreach (var x in req.PhaseIds)
            {
                if (x <= 0) continue;
                if (seen.Add(x)) orderedIds.Add(x);
            }

            if (orderedIds.Count == 0)
                return BadRequest(new { ok = false, message = "invalid payload: PhaseIds empty" });

            // ✅ โหลดทุก Phase ของ Project นี้ เพื่อทำให้ลำดับ (PhaseSort) ถาวรจริง
            var allPhases = await _context.ProjectPhases
                .Where(p => p.ProjectId == req.ProjectId)
                .ToListAsync();

            if (allPhases.Count == 0)
                return NotFound(new { ok = false, message = "no phases" });

            // ✅ ทำเป็น map เพื่อหาเร็ว
            var map = allPhases.ToDictionary(p => p.PhaseId);

            // ✅ ถ้ามี id บางตัวส่งมาแต่ไม่พบใน project นี้ ให้แจ้งกลับ (กัน reorder ผิดโปรเจกต์)
            var missing = orderedIds.Where(id => !map.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                return BadRequest(new { ok = false, message = "some phases not found in project", missing });

            // ✅ 1) ใส่ลำดับตามที่ส่งมา
            var entryId = await GetCurrentEntryIdAsync();
            var reorderedAt = DateTime.Now;
            var sort = 1;
            var used = new HashSet<int>();
            foreach (var id in orderedIds)
            {
                map[id].PhaseSort = sort++;
                map[id].CreatedAt = reorderedAt;
                map[id].EntryId = entryId;
                used.Add(id);
            }

            // ✅ 2) Phase ที่ไม่ได้ส่งมา ให้ต่อท้าย โดยคงลำดับเดิม (PhaseSort เดิม/PhaseId)
            var remaining = allPhases
                .Where(p => !used.Contains(p.PhaseId))
                .OrderBy(p => p.PhaseSort == 0 ? int.MaxValue : p.PhaseSort)
                .ThenBy(p => p.PhaseId)
                .ToList();

            foreach (var p in remaining)
            {
                p.PhaseSort = sort++;
            }

            await _context.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        // ===========================
        // HELPER
        // ===========================
        private SelectList GetPhaseTypeList(string? selected = null)
        {
            return new SelectList(
                new[] { "MAIN", "SUPPORT" },
                selected
            );
        }

        private bool CanMenu(string key)
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim();
            if (role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
                return true;

            var menus = HttpContext.Session.GetString("Menus") ?? "";
            return menus
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(menu => string.Equals(menu.Trim(), key, StringComparison.OrdinalIgnoreCase));
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

        private static string? NormalizePhaseStatus(string? status)
        {
            return string.Equals((status ?? "").Trim(), ApprovedPaymentStatus, StringComparison.OrdinalIgnoreCase)
                ? SubmittedStatus
                : status;
        }
    }
}
