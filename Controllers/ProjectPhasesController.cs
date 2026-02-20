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
            var phase = await _context.ProjectPhases.FindAsync(id);
            if (phase == null)
                return NotFound();

            _context.ProjectPhases.Remove(phase);
            await _context.SaveChangesAsync();

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
        public async Task<IActionResult> Reorder([FromBody] ReorderRequest? req)
        {
            if (req == null)
                return BadRequest(new { ok = false, message = "invalid payload: body is null" });

            if (req.ProjectId <= 0)
                return BadRequest(new { ok = false, message = "invalid payload: ProjectId" });

            if (req.PhaseIds == null || req.PhaseIds.Count == 0)
                return BadRequest(new { ok = false, message = "invalid payload: PhaseIds" });

            var ids = req.PhaseIds.Where(x => x > 0).Distinct().ToList();
            if (ids.Count == 0)
                return BadRequest(new { ok = false, message = "invalid payload: PhaseIds empty" });

            var phases = await _context.ProjectPhases
                .Where(p => p.ProjectId == req.ProjectId && ids.Contains(p.PhaseId))
                .ToListAsync();

            if (phases.Count == 0)
                return NotFound(new { ok = false, message = "no phases" });

            for (int i = 0; i < ids.Count; i++)
            {
                var ph = phases.FirstOrDefault(x => x.PhaseId == ids[i]);
                if (ph != null)
                    ph.PhaseSort = i + 1;
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