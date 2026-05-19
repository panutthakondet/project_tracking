using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Attributes;

namespace ProjectTracking.Controllers
{
    public class PhaseWorkloadController : Controller
    {
        private readonly AppDbContext _context;

        public PhaseWorkloadController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("PhaseWorkload.Index")]
        public async Task<IActionResult> Index(int? year, int? month)
        {
            var currentDate = DateTime.Today;

            int selectedYear = year ?? currentDate.Year;
            int selectedMonth = month ?? currentDate.Month;

            var monthStart = new DateTime(selectedYear, selectedMonth, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var data = await _context.PhaseAssigns
                .Include(x => x.Employee)
                .Include(x => x.Phase!)
                    .ThenInclude(p => p.Project)
                .Where(x =>
                    x.PlanStart.HasValue &&
                    x.PlanEnd.HasValue &&
                    x.PlanStart.Value <= monthEnd &&
                    x.PlanEnd.Value >= monthStart &&
                    x.Phase != null &&
                    (
                        x.Phase.PhaseStatus == "วางแผน" ||
                        x.Phase.PhaseStatus == "กำลังดำเนินการ"
                    )
                )
                .OrderBy(x => x.Employee != null ? x.Employee.EmpName : "")
                .ThenBy(x => x.PlanStart)
                .ToListAsync();

            ViewBag.Year = selectedYear;
            ViewBag.Month = selectedMonth;
            ViewBag.MonthStart = monthStart;
            ViewBag.MonthEnd = monthEnd;

            return View(data);
        }
    }
}