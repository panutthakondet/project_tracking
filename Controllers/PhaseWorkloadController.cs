using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class PhaseWorkloadController : Controller
    {
        private const string FilterYearKey = "PhaseWorkload.Filter.Year";
        private const string FilterYearToKey = "PhaseWorkload.Filter.YearTo";
        private const string FilterMonthKey = "PhaseWorkload.Filter.Month";
        private const string FilterMonthToKey = "PhaseWorkload.Filter.MonthTo";
        private const string FilterEmpIdKey = "PhaseWorkload.Filter.EmpId";
        private const string FilterWorkTypeKey = "PhaseWorkload.Filter.WorkType";
        private const string FilterViewModeKey = "PhaseWorkload.Filter.ViewMode";

        private readonly AppDbContext _context;

        public PhaseWorkloadController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("PhaseWorkload.Index")]
        public async Task<IActionResult> Index(int? year, int? yearTo, int? month, int? monthTo, string? empId, string? workType, string? viewMode)
        {
            var currentDate = DateTime.Today;
            var hasFilterQuery =
                year.HasValue ||
                yearTo.HasValue ||
                month.HasValue ||
                monthTo.HasValue ||
                !string.IsNullOrWhiteSpace(empId) ||
                !string.IsNullOrWhiteSpace(workType) ||
                !string.IsNullOrWhiteSpace(viewMode);

            int selectedYear = year ?? HttpContext.Session.GetInt32(FilterYearKey) ?? currentDate.Year;
            int selectedYearTo = yearTo ?? HttpContext.Session.GetInt32(FilterYearToKey) ?? selectedYear;

            int selectedMonth = ClampMonth(month ?? HttpContext.Session.GetInt32(FilterMonthKey) ?? 1);
            int selectedMonthTo = ClampMonth(monthTo ?? HttpContext.Session.GetInt32(FilterMonthToKey) ?? 12);

            if (!hasFilterQuery && !HttpContext.Session.GetInt32(FilterMonthKey).HasValue)
            {
                selectedMonth = 1;
                selectedMonthTo = 12;
                selectedYearTo = selectedYear;
            }

            var monthStart = new DateTime(selectedYear, selectedMonth, 1);
            var monthEnd = new DateTime(
                selectedYearTo,
                selectedMonthTo,
                DateTime.DaysInMonth(selectedYearTo, selectedMonthTo)
            );

            if (monthEnd < monthStart)
            {
                selectedYearTo = selectedYear;
                selectedMonthTo = selectedMonth;
                monthEnd = new DateTime(
                    selectedYearTo,
                    selectedMonthTo,
                    DateTime.DaysInMonth(selectedYearTo, selectedMonthTo)
                );
            }

            empId = hasFilterQuery ? empId : HttpContext.Session.GetString(FilterEmpIdKey);
            workType = hasFilterQuery ? workType : HttpContext.Session.GetString(FilterWorkTypeKey);
            viewMode = hasFilterQuery ? viewMode : HttpContext.Session.GetString(FilterViewModeKey);

            var selectedEmpId = int.TryParse(empId, out var parsedEmpId)
                ? parsedEmpId
                : (int?)null;
            var selectedViewMode = NormalizeViewMode(viewMode);
            var selectedWorkType = NormalizeWorkType(workType);

            SaveFilters(selectedYear, selectedYearTo, selectedMonth, selectedMonthTo, empId, selectedWorkType, selectedViewMode);

            var phaseAssigns = selectedWorkType is "ALL" or "PHASE"
                ? await _context.PhaseAssigns
                .Include(x => x.Employee)
                .Include(x => x.Phase!)
                    .ThenInclude(p => p.Project)
                    .ThenInclude(p => p!.Coop)
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
                .ToListAsync()
                : new List<ProjectTracking.Models.PhaseAssign>();

            var issues = selectedWorkType is "ALL" or "ISSUE"
                ? await _context.ProjectIssues
                .Include(x => x.Employee)
                .Include(x => x.Project)
                    .ThenInclude(p => p!.Coop)
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
                .ToListAsync()
                : new List<ProjectTracking.Models.ProjectIssue>();

            var supportOrders = selectedWorkType is "ALL" or "SUPPORT"
                ? await _context.ProjectSupportOrders
                .Include(x => x.Employee)
                .Include(x => x.Project)
                    .ThenInclude(p => p!.Coop)
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
                .ToListAsync()
                : new List<ProjectTracking.Models.ProjectSupportOrder>();

            var items = phaseAssigns.Select(x => new PhaseWorkloadItemViewModel
                {
                    WorkType = "PHASE",
                    WorkTypeLabel = "Assigns",
                    WorkTypeClass = "phase",
                    ItemId = x.AssignId,
                    EmpId = x.EmpId,
                    EmpName = x.Employee?.EmpName ?? $"Employee #{x.EmpId}",
                    ProjectId = x.Phase?.ProjectId ?? 0,
                    ProjectName = x.Phase?.Project?.ProjectDisplayName ?? "-",
                    PhasePeriodLabel = x.Phase?.PhasePeriodLabel ?? "",
                    Title = x.Role ?? x.Phase?.PhaseName ?? "-",
                    Detail = x.Phase?.PhaseName ?? "-",
                    StartDate = x.PlanStart,
                    EndDate = x.PlanEnd,
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
                    ProjectName = x.Project?.ProjectDisplayName ?? "-",
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
                    ProjectName = x.Project?.ProjectDisplayName ?? "-",
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
            ViewBag.SelectedWorkType = selectedWorkType;
            ViewBag.ViewMode = selectedViewMode;
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

            return issue is "FIXED" or "PASS" or "REJECT" || dev == "FIXED"
                ? "DONE"
                : "IN_PROGRESS";
        }

        private static string NormalizeSupportState(string? status, string? devStatus)
        {
            var orderStatus = (status ?? "").Trim().ToUpperInvariant();
            var dev = (devStatus ?? "").Trim().ToUpperInvariant();

            return orderStatus is "FIXED" or "PASS" or "REJECT" or "DONE" || dev == "FIXED"
                ? "DONE"
                : "IN_PROGRESS";
        }

        private static string NormalizeViewMode(string? viewMode)
        {
            return string.Equals(viewMode, "week", StringComparison.OrdinalIgnoreCase)
                ? "week"
                : "day";
        }

        private static string NormalizeWorkType(string? workType)
        {
            return (workType ?? "").Trim().ToUpperInvariant() switch
            {
                "PHASE" or "ASSIGN" or "ASSIGNS" => "PHASE",
                "ISSUE" or "ISSUES" => "ISSUE",
                "SUPPORT" => "SUPPORT",
                _ => "ALL"
            };
        }

        private static int ClampMonth(int month)
        {
            return Math.Clamp(month, 1, 12);
        }

        private void SaveFilters(int year, int yearTo, int month, int monthTo, string? empId, string workType, string viewMode)
        {
            HttpContext.Session.SetInt32(FilterYearKey, year);
            HttpContext.Session.SetInt32(FilterYearToKey, yearTo);
            HttpContext.Session.SetInt32(FilterMonthKey, month);
            HttpContext.Session.SetInt32(FilterMonthToKey, monthTo);
            HttpContext.Session.SetString(FilterEmpIdKey, empId ?? "");
            HttpContext.Session.SetString(FilterWorkTypeKey, workType);
            HttpContext.Session.SetString(FilterViewModeKey, viewMode);
        }
    }
}
