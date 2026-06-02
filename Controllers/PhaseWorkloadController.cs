using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.ViewModels;

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

            var selectedEmpId = int.TryParse(empId, out var parsedEmpId)
                ? parsedEmpId
                : (int?)null;

            var phaseAssigns = await _context.PhaseAssigns
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
                        !selectedEmpId.HasValue
                        || x.EmpId == selectedEmpId.Value
                    )
                )
                .OrderBy(x => x.Employee != null ? x.Employee.EmpName : "")
                .ThenBy(x => x.PlanStart)
                .ToListAsync();

            var issues = await _context.ProjectIssues
                .Include(x => x.Employee)
                .Include(x => x.Project)
                .Where(x =>
                    x.StartDate.HasValue &&
                    x.EndDate.HasValue &&
                    x.StartDate.Value <= monthEnd &&
                    x.EndDate.Value >= monthStart &&
                    (
                        !selectedEmpId.HasValue
                        || x.AssignTo == selectedEmpId.Value
                    )
                )
                .OrderBy(x => x.Employee != null ? x.Employee.EmpName : "")
                .ThenBy(x => x.StartDate)
                .ToListAsync();

            var supportOrders = await _context.ProjectSupportOrders
                .Include(x => x.Employee)
                .Include(x => x.Project)
                .Where(x =>
                    x.AssignTo.HasValue &&
                    x.StartDate.HasValue &&
                    x.EndDate.HasValue &&
                    x.StartDate.Value <= monthEnd &&
                    x.EndDate.Value >= monthStart &&
                    (
                        !selectedEmpId.HasValue
                        || x.AssignTo == selectedEmpId.Value
                    )
                )
                .OrderBy(x => x.Employee != null ? x.Employee.EmpName : "")
                .ThenBy(x => x.StartDate)
                .ToListAsync();

            var items = phaseAssigns.Select(x => new PhaseWorkloadItemViewModel
                {
                    WorkType = "PHASE",
                    WorkTypeLabel = "Assigns",
                    WorkTypeClass = "phase",
                    ItemId = x.AssignId,
                    EmpId = x.EmpId,
                    EmpName = x.Employee?.EmpName ?? $"Employee #{x.EmpId}",
                    ProjectId = x.Phase?.ProjectId ?? 0,
                    ProjectName = x.Phase?.Project?.ProjectName ?? "-",
                    Title = x.Role ?? x.Phase?.PhaseName ?? "-",
                    Detail = x.Phase?.PhaseName ?? "-",
                    StartDate = x.PlanStart,
                    EndDate = x.PlanEnd,
                    PeriodStartDate = x.Phase?.PeriodStartDate,
                    PeriodEndDate = x.Phase?.PeriodEndDate,
                    Status = x.WorkStatus ?? "",
                    WorkState = NormalizePhaseAssignState(x.WorkStatus),
                    Url = $"/PhaseAssigns?projectId={x.Phase?.ProjectId}&phaseId={x.Phase?.PhaseId}",
                    SortOrder = 10
                })
                .Concat(issues.Select(x => new PhaseWorkloadItemViewModel
                {
                    WorkType = "ISSUE",
                    WorkTypeLabel = "Issue",
                    WorkTypeClass = "issue",
                    ItemId = x.IssueId,
                    EmpId = x.AssignTo,
                    EmpName = x.Employee?.EmpName ?? $"Employee #{x.AssignTo}",
                    ProjectId = x.ProjectId,
                    ProjectName = x.Project?.ProjectName ?? "-",
                    Title = x.IssueName,
                    Detail = x.IssueDetail ?? "",
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    Status = x.IssueStatus ?? "",
                    WorkState = NormalizeIssueState(x.IssueStatus, x.DevStatus),
                    Url = $"/ProjectIssues/Details/{x.IssueId}",
                    SortOrder = 20
                }))
                .Concat(supportOrders.Select(x => new PhaseWorkloadItemViewModel
                {
                    WorkType = "SUPPORT",
                    WorkTypeLabel = "Support",
                    WorkTypeClass = "support",
                    ItemId = x.OrderId,
                    EmpId = x.AssignTo!.Value,
                    EmpName = x.Employee?.EmpName ?? $"Employee #{x.AssignTo}",
                    ProjectId = x.ProjectId,
                    ProjectName = x.Project?.ProjectName ?? "-",
                    Title = string.IsNullOrWhiteSpace(x.OrderTitle) ? $"Support #{x.OrderId}" : x.OrderTitle!,
                    Detail = x.OrderDetail ?? "",
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    Status = x.Status ?? "",
                    WorkState = NormalizeSupportState(x.Status, x.DevStatus),
                    Url = $"/SupportOrders/Details/{x.OrderId}",
                    SortOrder = 30
                }))
                .OrderBy(x => x.EmpName)
                .ThenBy(x => x.ProjectName)
                .ThenBy(x => x.StartDate)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Title)
                .ToList();

            ViewBag.Year = selectedYear;
            ViewBag.YearTo = selectedYearTo;
            ViewBag.Month = selectedMonth;
            ViewBag.MonthTo = selectedMonthTo;
            ViewBag.SelectedEmpId = empId;
            ViewBag.MonthStart = monthStart;
            ViewBag.MonthEnd = monthEnd;

            return View(new PhaseWorkloadViewModel
            {
                Items = items
            });
        }

        private static string NormalizePhaseAssignState(string? status)
        {
            return string.Equals(status, "DONE", StringComparison.OrdinalIgnoreCase)
                ? "DONE"
                : "IN_PROGRESS";
        }

        private static string NormalizeIssueState(string? issueStatus, string? devStatus)
        {
            var issue = (issueStatus ?? "").Trim().ToUpperInvariant();
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();

            return issue is "FIXED" or "PASS" || dev == "FIXED"
                ? "DONE"
                : "IN_PROGRESS";
        }

        private static string NormalizeSupportState(string? status, string? devStatus)
        {
            var orderStatus = (status ?? "").Trim().ToUpperInvariant();
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();

            return orderStatus == "DONE" || dev == "FIXED"
                ? "DONE"
                : "IN_PROGRESS";
        }
    }
}
