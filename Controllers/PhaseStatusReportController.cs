using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Services;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    public class PhaseStatusReportController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly OverdueMailService _overdueMailService;

        public PhaseStatusReportController(
            AppDbContext context,
            OverdueMailService overdueMailService)
        {
            _context = context;
            _overdueMailService = overdueMailService;
        }

        // =====================================================
        // INDEX
        // =====================================================
        [RequireMenu("PhaseStatusReport.Index")]
        public async Task<IActionResult> Index(string? empName, string? projectName)
        {
            ViewBag.EmpList = await _context.VwPhaseOwnerStatuses
                .Select(x => x.EmpName)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            ViewBag.ProjectList = await _context.VwPhaseOwnerStatuses
                .Select(x => x.ProjectName)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var query = _context.VwPhaseOwnerStatuses.AsQueryable();

            if (!string.IsNullOrEmpty(empName))
                query = query.Where(x => x.EmpName == empName);

            if (!string.IsNullOrEmpty(projectName))
                query = query.Where(x => x.ProjectName == projectName);

            var result = await query
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ToListAsync();

            ViewBag.SelectedEmp = empName;
            ViewBag.SelectedProject = projectName;

            return View(result);
        }

        // =====================================================
        // PRINT
        // =====================================================
        [RequireMenu("PhaseStatusReport.Index")]
        public async Task<IActionResult> Print(string? empName, string? projectName)
        {
            var query = _context.VwPhaseOwnerStatuses.AsQueryable();

            if (!string.IsNullOrEmpty(empName))
                query = query.Where(x => x.EmpName == empName);

            if (!string.IsNullOrEmpty(projectName))
                query = query.Where(x => x.ProjectName == projectName);

            var result = await query
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ToListAsync();

            ViewBag.EmpName = string.IsNullOrEmpty(empName) ? "All Employees" : empName;
            ViewBag.ProjectName = string.IsNullOrEmpty(projectName) ? "All Projects" : projectName;
            ViewBag.PrintDate = DateTime.Now;

            return View(result);
        }

        // =====================================================
        // TIMELINE (Gantt)
        // =====================================================
        [RequireMenu("PhaseStatusReport.Timeline")]
        public async Task<IActionResult> Timeline(string? projectName)
        {
            ViewBag.ProjectList = await _context.VwPhaseOwnerStatuses
                .Select(x => x.ProjectName)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            ViewBag.SelectedProject = projectName;

            var query = _context.VwPhaseOwnerStatuses.AsQueryable();

            if (!string.IsNullOrEmpty(projectName))
                query = query.Where(x => x.ProjectName == projectName);

            var result = await query
                .Where(x => x.PlanStart != null && x.PlanEnd != null)
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ToListAsync();

            return View(result);
        }

        // =====================================================
        // 🔔 SEND EMAIL (กดปุ่มจากหน้า Report)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseStatusReport.Index")]
        public async Task<IActionResult> SendOverdueMail()
        {
            await _overdueMailService.SendOncePerDayAsync();

            TempData["Success"] = "ส่ง Email แจ้ง Phase Overdue เรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index));
        }
    }
}