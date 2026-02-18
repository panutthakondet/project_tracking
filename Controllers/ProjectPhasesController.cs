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
                .OrderBy(p => p.PhaseId)   // 🔥 เรียงตาม phase_id
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

            _context.Update(phase);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId = phase.ProjectId });
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