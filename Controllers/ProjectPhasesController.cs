using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    [RequireMenu("ProjectPhases.Index")]
    public class ProjectPhasesController : BaseController
    {
        private readonly AppDbContext _context;

        public ProjectPhasesController(AppDbContext context)
        {
            _context = context;
        }

        // ===========================
        // INDEX
        // ===========================
        public async Task<IActionResult> Index(int? projectId)
        {
            ViewBag.Projects = await _context.Projects
                .AsNoTracking()
                .OrderByDescending(p => p.ProjectId)
                .ToListAsync();

            if (projectId == null)
            {
                ViewBag.SelectedProject = null;
                return View(new List<ProjectPhase>());
            }

            var selectedProject = await _context.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (selectedProject == null)
            {
                ViewBag.SelectedProject = null;
                return View(new List<ProjectPhase>());
            }

            ViewBag.SelectedProject = selectedProject;

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                // ✅ เรียงตาม phase_sort (ถ้าเป็น 0 ให้ไปท้าย) แล้วค่อยตาม phase_id
                .OrderBy(p => p.PhaseSort == 0 ? int.MaxValue : p.PhaseSort)
                .ThenBy(p => p.PhaseId)
                .ToListAsync();

            return View(phases);
        }

        // ===========================
        // CREATE (GET)
        // ===========================
        public async Task<IActionResult> Create(int? projectId)
        {
            if (projectId == null)
                return RedirectToAction(nameof(Index));

            var project = await _context.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);

            if (project == null)
                return RedirectToAction(nameof(Index));

            // 🔥 หา Phase ล่าสุดด้วย PhaseId
            var lastPhase = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .OrderByDescending(p => p.PhaseId)
                .FirstOrDefaultAsync();

            ViewBag.SelectedProjectName = project.ProjectName;

            ViewBag.LastPlanStart = lastPhase?.PlanStart?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPlanEnd = lastPhase?.PlanEnd?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPeriodStart = lastPhase?.ActualStart?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPeriodEnd = lastPhase?.ActualEnd?.ToString("yyyy-MM-dd") ?? "";

            ViewBag.PhaseTypeList = GetPhaseTypeList("MAIN");

            return View(new ProjectPhase
            {
                ProjectId = project.ProjectId,
                PhaseType = "MAIN"
            });
        }

        // ===========================
        // CREATE (POST)
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProjectPhase phase)
        {
            if (phase.ProjectId <= 0)
            {
                ModelState.AddModelError("ProjectId", "กรุณาเลือก Project");
            }

            if (!ModelState.IsValid)
            {
                var project = await _context.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProjectId == phase.ProjectId);

                ViewBag.SelectedProjectName = project?.ProjectName ?? "ไม่พบข้อมูลโครงการ";

                var lastPhase = await _context.ProjectPhases
                    .AsNoTracking()
                    .Where(p => p.ProjectId == phase.ProjectId)
                    .OrderByDescending(p => p.PhaseId)
                    .FirstOrDefaultAsync();

                ViewBag.LastPlanStart = lastPhase?.PlanStart?.ToString("yyyy-MM-dd") ?? "";
                ViewBag.LastPlanEnd = lastPhase?.PlanEnd?.ToString("yyyy-MM-dd") ?? "";
                ViewBag.LastPeriodStart = lastPhase?.ActualStart?.ToString("yyyy-MM-dd") ?? "";
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

            _context.ProjectPhases.Add(phase);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId = phase.ProjectId });
        }

        // ===========================
        // EDIT (GET)
        // ===========================
        public async Task<IActionResult> Edit(int id)
        {
            var phase = await _context.ProjectPhases
                .Include(p => p.Project)
                .FirstOrDefaultAsync(p => p.PhaseId == id);

            if (phase == null)
                return NotFound();

            // 🔥 หา Phase ก่อนหน้าตาม phase_id
            var previousPhase = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == phase.ProjectId &&
                            p.PhaseId < phase.PhaseId)
                .OrderByDescending(p => p.PhaseId)
                .FirstOrDefaultAsync();

            ViewBag.LastPlanStart = previousPhase?.PlanStart?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPlanEnd = previousPhase?.PlanEnd?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPeriodStart = previousPhase?.ActualStart?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.LastPeriodEnd = previousPhase?.ActualEnd?.ToString("yyyy-MM-dd") ?? "";

            ViewBag.PhaseTypeList = GetPhaseTypeList(phase.PhaseType);

            return View(phase);
        }

        // ===========================
        // EDIT (POST)
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProjectPhase phase)
        {
            if (id != phase.PhaseId)
                return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.PhaseTypeList = GetPhaseTypeList(phase.PhaseType);
                return View(phase);
            }

            var existing = await _context.ProjectPhases.FirstOrDefaultAsync(p => p.PhaseId == id);
            if (existing == null)
                return NotFound();

            // ✅ อัปเดตเฉพาะฟิลด์ที่แก้ได้จากฟอร์ม (คงค่า PhaseSort เดิมไว้)
            existing.PhaseName = phase.PhaseName;
            existing.PhaseType = phase.PhaseType;
            existing.PhaseOrder = phase.PhaseOrder;
            existing.PlanStart = phase.PlanStart;
            existing.PlanEnd = phase.PlanEnd;
            existing.ActualStart = phase.ActualStart;
            existing.ActualEnd = phase.ActualEnd;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId = existing.ProjectId });
        }

        // ===========================
        // DELETE
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int projectId)
        {
            // โหลด Phase แบบ tracked เพื่อให้ลบได้จริง
            var phase = await _context.ProjectPhases
                .FirstOrDefaultAsync(p => p.PhaseId == id);

            if (phase == null)
            {
                TempData["Error"] = "ไม่พบ Phase ที่ต้องการลบ";
                // ถ้า projectId ไม่ถูกส่งมา ก็ยังกลับไปหน้า Index ได้ (จะเป็น list ว่าง)
                return RedirectToAction(nameof(Index), new { projectId });
            }

            // ✅ ใช้ ProjectId จากข้อมูลจริงเสมอ (ไม่พึ่งค่าที่ส่งมาจากฟอร์ม)
            var realProjectId = phase.ProjectId;

            try
            {
                // ✅ ลบข้อมูลลูกก่อน เพื่อไม่ให้ FK บล็อก (phase_assign.phase_id -> project_phase.phase_id)
                var assigns = await _context.Set<PhaseAssign>()
                    .Where(a => a.PhaseId == id)
                    .ToListAsync();

                if (assigns.Count > 0)
                {
                    _context.Set<PhaseAssign>().RemoveRange(assigns);
                }

                _context.ProjectPhases.Remove(phase);

                var affected = await _context.SaveChangesAsync();

                if (affected <= 0)
                    TempData["Error"] = "ลบไม่สำเร็จ: ไม่พบแถวที่ถูกลบ (0 rows affected)";
                else
                    TempData["Success"] = assigns.Count > 0
                        ? $"ลบ Phase และลบ Assign ที่เกี่ยวข้อง {assigns.Count} รายการเรียบร้อยแล้ว"
                        : "ลบ Phase เรียบร้อยแล้ว";

                return RedirectToAction(nameof(Index), new { projectId = realProjectId });
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "ลบไม่ได้: มีข้อมูลอื่นอ้างอิง Phase นี้อยู่ กรุณาตรวจสอบความสัมพันธ์ของข้อมูลก่อน";
                return RedirectToAction(nameof(Index), new { projectId = realProjectId });
            }
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
            var sort = 1;
            var used = new HashSet<int>();
            foreach (var id in orderedIds)
            {
                map[id].PhaseSort = sort++;
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
    }
}