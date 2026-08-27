using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;

namespace ProjectTracking.Controllers
{
    [RequireMenu("PhaseAssigns.Print")]
    public class AssignedEmployeesReportController : BaseController
    {
        private readonly AppDbContext _context;

        public AssignedEmployeesReportController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // VIEW ONLY
        // =========================
        public async Task<IActionResult> Index(int? projectId, int? empId)
        {
            ViewBag.Projects = await _context.Projects
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var query = _context.PhaseAssigns
                .Include(a => a.Employee)
                .Include(a => a.Phase)
                .Include(a => a.StatusDefinition)
                .AsQueryable();

            if (projectId != null)
            {
                query = query.Where(a => a.Phase!.ProjectId == projectId);
            }

            if (empId != null)
            {
                query = query.Where(a => a.EmpId == empId);
            }

            var result = await query
                .OrderBy(a => a.Employee!.EmpName)
                .ThenBy(a => a.Phase!.PhaseOrder)
                .ThenBy(a => a.Phase!.PeriodOrder)
                .ToListAsync();

            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedEmpId = empId;

            ViewBag.EmployeeList = result
                .Select(a => a.Employee!)
                .Distinct()
                .OrderBy(e => e.EmpName)
                .ToList();

            return View(result);
        }

        // =========================
        // PRINT
        // =========================
        public async Task<IActionResult> Print(int? projectId, int? empId)
        {
            var query = _context.PhaseAssigns
                .Include(a => a.Employee)
                .Include(a => a.Phase)
                .Include(a => a.StatusDefinition)
                .AsQueryable();

            if (projectId != null)
                query = query.Where(a => a.Phase!.ProjectId == projectId);

            if (empId != null)
                query = query.Where(a => a.EmpId == empId);

            var result = await query
                .OrderBy(a => a.Employee!.EmpName)
                .ThenBy(a => a.Phase!.PhaseOrder)
                .ThenBy(a => a.Phase!.PeriodOrder)
                .ToListAsync();

            ViewBag.PrintDate = DateTime.Now;

            return View(result);
        }
    }
}
