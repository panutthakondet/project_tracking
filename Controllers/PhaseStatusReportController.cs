using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
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
        public async Task<IActionResult> Index(string? empName, string? projectName, string? phaseStatus)
        {
            var allRows = await BuildPhaseOwnerStatusRowsAsync();

            ViewBag.EmpList = allRows
                .Select(x => x.EmpName)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewBag.ProjectList = allRows
                .Select(x => x.ProjectName)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewBag.StatusList = allRows
                .Select(x => x.PhaseStatus)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(StatusRank)
                .ThenBy(x => x)
                .ToList();

            var result = allRows.AsEnumerable();

            if (!string.IsNullOrEmpty(empName))
                result = result.Where(x => x.EmpName == empName);

            if (!string.IsNullOrEmpty(projectName))
                result = result.Where(x => x.ProjectName == projectName);

            if (!string.IsNullOrEmpty(phaseStatus))
                result = result.Where(x => x.PhaseStatus == phaseStatus);

            ViewBag.SelectedEmp = empName;
            ViewBag.SelectedProject = projectName;
            ViewBag.SelectedStatus = phaseStatus;

            return View(result
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ThenBy(x => x.PeriodOrder)
                .ThenBy(x => x.PhaseId)
                .ToList());
        }

        // =====================================================
        // PRINT
        // =====================================================
        [RequireMenu("PhaseStatusReport.Print")]
        public async Task<IActionResult> Print(string? empName, string? projectName, string? phaseStatus)
        {
            var result = (await BuildPhaseOwnerStatusRowsAsync()).AsEnumerable();

            if (!string.IsNullOrEmpty(empName))
                result = result.Where(x => x.EmpName == empName);

            if (!string.IsNullOrEmpty(projectName))
                result = result.Where(x => x.ProjectName == projectName);

            if (!string.IsNullOrEmpty(phaseStatus))
                result = result.Where(x => x.PhaseStatus == phaseStatus);

            ViewBag.EmpName = string.IsNullOrEmpty(empName) ? "All Employees" : empName;
            ViewBag.ProjectName = string.IsNullOrEmpty(projectName) ? "All Projects" : projectName;
            ViewBag.PhaseStatus = string.IsNullOrEmpty(phaseStatus) ? "All Statuses" : phaseStatus;
            ViewBag.PrintDate = DateTime.Now;

            return View(result
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ThenBy(x => x.PeriodOrder)
                .ThenBy(x => x.PhaseId)
                .ToList());
        }

        [RequireMenu("PhaseStatusReport.Print")]
        public async Task<IActionResult> PrintTable(string? empName, string? projectName, string? phaseStatus)
        {
            var result = (await BuildPhaseOwnerStatusRowsAsync()).AsEnumerable();

            if (!string.IsNullOrEmpty(empName))
                result = result.Where(x => x.EmpName == empName);

            if (!string.IsNullOrEmpty(projectName))
                result = result.Where(x => x.ProjectName == projectName);

            if (!string.IsNullOrEmpty(phaseStatus))
                result = result.Where(x => x.PhaseStatus == phaseStatus);

            ViewBag.EmpName = string.IsNullOrEmpty(empName) ? "All Employees" : empName;
            ViewBag.ProjectName = string.IsNullOrEmpty(projectName) ? "All Projects" : projectName;
            ViewBag.PhaseStatus = string.IsNullOrEmpty(phaseStatus) ? "All Statuses" : phaseStatus;
            ViewBag.PrintDate = DateTime.Now;

            return View(result
                .OrderBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ThenBy(x => x.PeriodOrder)
                .ThenBy(x => x.PhaseId)
                .ToList());
        }

        // =====================================================
        // 🔔 SEND EMAIL (กดปุ่มจากหน้า Report)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("PhaseStatusReport.SendMail")]
        public async Task<IActionResult> SendOverdueMail()
        {
            await _overdueMailService.SendOncePerDayAsync();

            TempData["Success"] = "ส่ง Email แจ้ง Phase Overdue เรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index));
        }

        private async Task<List<VwPhaseOwnerStatus>> BuildPhaseOwnerStatusRowsAsync()
        {
            var today = DateTime.Today;

            var rows = await (
                from assign in _context.PhaseAssigns.AsNoTracking()
                join phase in _context.ProjectPhases.AsNoTracking()
                    on assign.PhaseId equals phase.PhaseId
                join project in _context.Projects.AsNoTracking()
                    on phase.ProjectId equals project.ProjectId
                join employee in _context.Employees.AsNoTracking()
                    on assign.EmpId equals employee.EmpId
                select new
                {
                    project.ProjectId,
                    project.ProjectName,
                    project.StartDate,
                    project.EndDate,
                    phase.PhaseId,
                    phase.PhaseOrder,
                    phase.PeriodOrder,
                    phase.PhaseStatus,
                    PhasePlanStart = phase.PlanStart,
                    PhasePlanEnd = phase.PlanEnd,
                    phase.PeriodStartDate,
                    phase.PeriodEndDate,
                    assign.EmpId,
                    employee.EmpName,
                    assign.Role,
                    AssignPlanStart = assign.PlanStart,
                    AssignPlanEnd = assign.PlanEnd,
                    assign.WorkStatus,
                    assign.Remark
                }
            ).ToListAsync();

            return rows.Select(row =>
            {
                var planStart = row.AssignPlanStart ?? row.PhasePlanStart;
                var planEnd = row.AssignPlanEnd ?? row.PhasePlanEnd;
                var isDone = IsDone(row.WorkStatus, row.PhaseStatus);
                var overdueDays = !isDone && planEnd != null && planEnd.Value.Date < today
                    ? (today - planEnd.Value.Date).Days
                    : 0;

                return new VwPhaseOwnerStatus
                {
                    ProjectId = row.ProjectId,
                    ProjectName = row.ProjectName,
                    StartDate = row.StartDate,
                    EndDate = row.EndDate,
                    PhaseId = row.PhaseId,
                    PhaseOrder = row.PhaseOrder,
                    PeriodOrder = row.PeriodOrder,
                    EmpId = row.EmpId,
                    EmpName = row.EmpName,
                    Role = row.Role ?? "",
                    PlanStart = planStart,
                    PlanEnd = planEnd,
                    ActualStart = row.PeriodStartDate,
                    ActualEnd = row.PeriodEndDate,
                    PlanDays = DaysInclusive(planStart, planEnd),
                    ActualDays = DaysInclusive(row.PeriodStartDate, row.PeriodEndDate),
                    PhaseStatus = NormalizeReportStatus(row.WorkStatus, row.PhaseStatus, overdueDays),
                    OverdueDays = overdueDays,
                    Remark = row.Remark
                };
            }).ToList();
        }

        private static int? DaysInclusive(DateTime? start, DateTime? end)
        {
            if (start == null || end == null) return null;
            if (end.Value.Date < start.Value.Date) return null;
            return (end.Value.Date - start.Value.Date).Days + 1;
        }

        private static bool IsDone(string? workStatus, string? phaseStatus)
        {
            var work = Norm(workStatus);
            var phase = Norm(phaseStatus);
            return work == "DONE"
                || phase is "DONE" or "ส่งงวดงานแล้ว";
        }

        private static string NormalizeReportStatus(string? workStatus, string? phaseStatus, int overdueDays)
        {
            if (IsDone(workStatus, phaseStatus)) return "DONE";
            if (overdueDays > 0) return "DELAY";

            var work = Norm(workStatus);
            if (work == "IN_PROGRESS") return "IN_PROGRESS";

            var phase = Norm(phaseStatus);
            if (phase == "กำลังดำเนินการ") return "IN_PROGRESS";
            if (phase == "วางแผน") return "PLAN";

            return string.IsNullOrWhiteSpace(work) ? phase : work;
        }

        private static string Norm(string? value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static int StatusRank(string? status)
        {
            return Norm(status) switch
            {
                "DELAY" => 1,
                "IN_PROGRESS" => 2,
                "PLAN" => 3,
                "DONE" => 4,
                _ => 99
            };
        }
    }
}
