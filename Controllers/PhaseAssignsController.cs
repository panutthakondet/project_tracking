using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    public class PhaseAssignsController : BaseController
    {
        private readonly AppDbContext _context;

        public PhaseAssignsController(AppDbContext context)
        {
            _context = context;
        }

        // =====================================================
        // INDEX
        // =====================================================
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Index(int? projectId, int? empId)
        {
            ViewBag.Projects = await _context.Projects
                .AsNoTracking()
                .OrderBy(p => p.ProjectName)
                .ToListAsync();

            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedEmpId = empId;

            ViewBag.EmployeeList = new List<Employee>();

            if (projectId == null)
                return View(new List<PhaseAssign>());

            var project = await _context.Projects.FindAsync(projectId);
            if (project == null)
                return View(new List<PhaseAssign>());

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
                where ph.ProjectId == projectId
                select new PhaseAssign
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    PlanStart = a.PlanStart,
                    PlanEnd = a.PlanEnd,
                    ActualStart = a.ActualStart,
                    ActualEnd = a.ActualEnd,
                    Remark = a.Remark,

                    Phase = ph,
                    Employee = e
                };

            if (empId.HasValue)
                assignsQuery = assignsQuery.Where(x => x.EmpId == empId.Value);

            var assigns = await assignsQuery
                .OrderBy(a => a.AssignId)
                .ToListAsync();

            return View(assigns);
        }

        // =====================================================
        // CREATE (GET)
        // =====================================================
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Create(int projectId)
        {
            var project = await _context.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            ViewBag.ProjectId = projectId;
            ViewBag.ProjectName = project.ProjectName;

            ViewBag.Phases = new SelectList(
                await _context.ProjectPhases
                    .Where(p => p.ProjectId == projectId)
                    .OrderBy(p => p.PhaseId)
                    .ToListAsync(),
                "PhaseId",
                "PhaseName");

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName");

            return View(new PhaseAssign());
        }

        // =====================================================
        // CREATE (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Create(PhaseAssign model, int projectId)
        {
            var phase = await _context.ProjectPhases
                .FirstOrDefaultAsync(p => p.PhaseId == model.PhaseId);

            if (phase == null)
            {
                ModelState.AddModelError("", "Phase not found");
            }
            else
            {
                model.Role = phase.PhaseName;
            }

            if (!ModelState.IsValid)
            {
                await ReloadCreateDropdown(projectId, model);
                return View(model);
            }

            _context.PhaseAssigns.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId = phase!.ProjectId });
        }

        // =====================================================
        // EDIT (GET)
        // =====================================================
        [HttpGet]
        [RequireMenu("PhaseAssigns.Index")]
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

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName",
                assign.EmpId
            );

            return View(assign);
        }

        // =====================================================
        // EDIT (POST)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Edit(int id, PhaseAssign model)
        {
            if (id != model.AssignId)
                return NotFound();

            var db = await _context.PhaseAssigns
                .FirstOrDefaultAsync(a => a.AssignId == id);

            if (db == null)
                return NotFound();

            db.EmpId = model.EmpId;
            db.PlanStart = model.PlanStart;
            db.PlanEnd = model.PlanEnd;
            db.ActualStart = model.ActualStart;
            db.ActualEnd = model.ActualEnd;
            db.Remark = model.Remark;

            var phase = await _context.ProjectPhases.FindAsync(db.PhaseId);
            if (phase != null)
                db.Role = phase.PhaseName;

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
                    planStart = p.PlanStart,
                    planEnd = p.PlanEnd
                })
                .FirstOrDefaultAsync();

            if (phase == null)
                return NotFound();

            return Json(phase);
        }

        // =====================================================
        // PRINT REPORT (⭐ FIX NULL HERE)
        // =====================================================
        [RequireMenu("PhaseAssigns.Print")]
        [HttpGet]
        public async Task<IActionResult> Print(int? projectId, int? empId, string? role)
        {
            ViewBag.Projects = await _context.Projects.OrderBy(p => p.ProjectName).ToListAsync();
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedEmpId = empId;
            ViewBag.SelectedRole = role;

            ViewBag.EmployeeList = new List<Employee>();
            ViewBag.RoleList = new List<string>();

            if (projectId == null)
                return View(new List<PhaseAssign>());

            ViewBag.SelectedProject = await _context.Projects.FindAsync(projectId);

            // Employee dropdown
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

            // Role dropdown
            ViewBag.RoleList = await (
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                where ph.ProjectId == projectId && a.Role != null
                group a.Role by a.Role into g
                orderby g.Key
                select g.Key!
            ).ToListAsync();

            var query =
                from a in _context.PhaseAssigns.AsNoTracking()
                join ph in _context.ProjectPhases.AsNoTracking() on a.PhaseId equals ph.PhaseId
                join e in _context.Employees.AsNoTracking() on a.EmpId equals e.EmpId
                where ph.ProjectId == projectId
                select new PhaseAssign
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    PlanStart = a.PlanStart,
                    PlanEnd = a.PlanEnd,
                    ActualStart = a.ActualStart,
                    ActualEnd = a.ActualEnd,
                    Remark = a.Remark,

                    Phase = ph,
                    Employee = e
                };

            if (empId.HasValue)
                query = query.Where(x => x.EmpId == empId.Value);

            if (!string.IsNullOrEmpty(role))
                query = query.Where(x => x.Role == role);

            return View(await query.ToListAsync());
        }

        // =====================================================
        // PRINT FORM
        // =====================================================
        [HttpGet]
        [RequireMenu("PhaseAssigns.Index")]
        public async Task<IActionResult> Form(int id)
        {
            // ✅ FIX: ไม่ใช้ Include(a => a.Phase) / ThenInclude(Project) เพื่อเลี่ยง PhaseId2
            // ดึง Assign + Employee ก่อน
            var assign = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(a => a.Employee)
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
                    .FirstOrDefaultAsync(pr => pr.ProjectId == phase.ProjectId);

                if (project != null)
                {
                    phase.Project = project;
                }
            }

            ViewBag.BusinessAnalyst = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Position == "Business Analyst")
                .FirstOrDefaultAsync();

            return View(assign);
        }

        // =====================================================
        // DELETE
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseAssigns.Index")]
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

            _context.PhaseAssigns.Remove(assign);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { projectId });
        }

        // =====================================================
        // HELPER
        // =====================================================
        private async Task ReloadCreateDropdown(int projectId, PhaseAssign model)
        {
            ViewBag.ProjectId = projectId;

            ViewBag.Phases = new SelectList(
                await _context.ProjectPhases
                    .Where(p => p.ProjectId == projectId)
                    .OrderBy(p => p.PhaseId)
                    .ToListAsync(),
                "PhaseId",
                "PhaseName",
                model.PhaseId);

            ViewBag.Employees = new SelectList(
                await _context.Employees
                    .Where(e => e.Status == "ACTIVE")
                    .OrderBy(e => e.EmpName)
                    .ToListAsync(),
                "EmpId",
                "EmpName",
                model.EmpId);
        }
    }
}