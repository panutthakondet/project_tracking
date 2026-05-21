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
        public async Task<IActionResult> Index(int? year, int? yearTo, int? month, int? monthTo, string? empId)
        {
            var currentDate = DateTime.Today;

            int selectedYear = year ?? currentDate.Year;
            int selectedYearTo = yearTo ?? selectedYear;

            int selectedMonth = month ?? currentDate.Month;
            int selectedMonthTo = monthTo ?? selectedMonth;

            var monthStart = new DateTime(selectedYear, selectedMonth, 1);
            var monthEnd = new DateTime(
                selectedYearTo,
                selectedMonthTo,
                DateTime.DaysInMonth(selectedYearTo, selectedMonthTo)
            );

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
                    ) &&
                    (
                        string.IsNullOrEmpty(empId)
                        || x.EmpId.ToString() == empId
                    )
                )
                .Select(x => new Models.PhaseAssign
                {
                    AssignId = x.AssignId,
                    EmpId = x.EmpId,
                    Employee = x.Employee,
                    PhaseId = x.PhaseId,
                    Phase = x.Phase,
                    Role = x.Role,
                    PlanStart = x.PlanStart,
                    PlanEnd = x.PlanEnd
                })
                .OrderBy(x => x.Employee != null ? x.Employee.EmpName : "")
                .ThenBy(x => x.PlanStart)
                .ToListAsync();

            ViewBag.Year = selectedYear;
            ViewBag.YearTo = selectedYearTo;
            ViewBag.Month = selectedMonth;
            ViewBag.MonthTo = selectedMonthTo;
            ViewBag.SelectedEmpId = empId;
            ViewBag.MonthStart = monthStart;
            ViewBag.MonthEnd = monthEnd;

            return View(data);
        }
    }
}