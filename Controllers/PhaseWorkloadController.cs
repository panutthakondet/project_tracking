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
        private const string FilterCoopIdKey = "PhaseWorkload.Filter.CoopId";
        private const string FilterProjectIdKey = "PhaseWorkload.Filter.ProjectId";

        private readonly AppDbContext _context;

        public PhaseWorkloadController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("PhaseWorkload.Index")]
        public async Task<IActionResult> Index(int? year, int? yearTo, int? month, int? monthTo, string? periodStart, string? periodEnd, string? empId, int? coopId, int? projectId, bool syncProjectPeriod = false)
        {
            var currentDate = DateTime.Today;
            var hasFilterQuery =
                year.HasValue ||
                yearTo.HasValue ||
                month.HasValue ||
                monthTo.HasValue ||
                !string.IsNullOrWhiteSpace(periodStart) ||
                !string.IsNullOrWhiteSpace(periodEnd) ||
                !string.IsNullOrWhiteSpace(empId) ||
                coopId.HasValue ||
                projectId.HasValue;

            if (TryParsePeriodMonth(periodStart, out var parsedStart))
            {
                year = parsedStart.Year;
                month = parsedStart.Month;
            }

            if (TryParsePeriodMonth(periodEnd, out var parsedEnd))
            {
                yearTo = parsedEnd.Year;
                monthTo = parsedEnd.Month;
            }

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
            var selectedEmpId = int.TryParse(empId, out var parsedEmpId)
                ? parsedEmpId
                : (int?)null;
            var selectedCoopId = hasFilterQuery
                ? coopId
                : PositiveOrNull(HttpContext.Session.GetInt32(FilterCoopIdKey));
            var selectedProjectId = hasFilterQuery
                ? projectId
                : PositiveOrNull(HttpContext.Session.GetInt32(FilterProjectIdKey));

            var projectOptions = await _context.Projects
                .AsNoTracking()
                .Include(x => x.Coop)
                .OrderBy(x => x.Coop != null ? x.Coop.CoopName : "")
                .ThenBy(x => x.ProjectName)
                .ToListAsync();

            var selectedProject = selectedProjectId.HasValue
                ? projectOptions.FirstOrDefault(x => x.ProjectId == selectedProjectId.Value)
                : null;

            if (selectedProject == null)
            {
                selectedProjectId = null;
            }
            else if (!selectedCoopId.HasValue)
            {
                selectedCoopId = selectedProject.CoopId;
            }
            else if (selectedProject.CoopId != selectedCoopId)
            {
                selectedProjectId = null;
            }

            if (syncProjectPeriod && (selectedProjectId.HasValue || selectedCoopId.HasValue))
            {
                var periodProjects = selectedProjectId.HasValue
                    ? projectOptions.Where(x => x.ProjectId == selectedProjectId.Value)
                    : projectOptions.Where(x => x.CoopId == selectedCoopId!.Value);
                var periodProjectList = periodProjects.ToList();
                var projectStart = periodProjectList
                    .Where(x => x.StartDate.HasValue)
                    .Select(x => x.StartDate!.Value)
                    .DefaultIfEmpty()
                    .Min();
                var projectEnd = periodProjectList
                    .Where(x => x.EndDate.HasValue)
                    .Select(x => x.EndDate!.Value)
                    .DefaultIfEmpty()
                    .Max();

                if (projectStart != default && projectEnd != default && projectEnd >= projectStart)
                {
                    selectedYear = projectStart.Year;
                    selectedMonth = projectStart.Month;
                    selectedYearTo = projectEnd.Year;
                    selectedMonthTo = projectEnd.Month;
                    monthStart = new DateTime(selectedYear, selectedMonth, 1);
                    monthEnd = new DateTime(
                        selectedYearTo,
                        selectedMonthTo,
                        DateTime.DaysInMonth(selectedYearTo, selectedMonthTo));
                }
            }

            SaveFilters(selectedYear, selectedYearTo, selectedMonth, selectedMonthTo, empId, selectedCoopId, selectedProjectId);

            var phaseAssigns = await _context.PhaseAssigns
                .Include(x => x.Employee)
                .Include(x => x.Phase!)
                    .ThenInclude(p => p.Project)
                    .ThenInclude(p => p!.Coop)
                .Where(x =>
                    x.Phase != null &&
                    (
                        (
                            x.PlanStart.HasValue &&
                            x.PlanEnd.HasValue &&
                            x.PlanStart.Value <= monthEnd &&
                            x.PlanEnd.Value >= monthStart
                        ) || (
                            x.Phase.PlanStart.HasValue &&
                            x.Phase.PeriodEndDate.HasValue &&
                            x.Phase.PlanStart.Value <= monthEnd &&
                            x.Phase.PeriodEndDate.Value >= monthStart
                        )
                    ) &&
                    (
                        !selectedEmpId.HasValue
                        || x.EmpId == selectedEmpId.Value
                    ) && (
                        !selectedCoopId.HasValue
                        || x.Phase.Project!.CoopId == selectedCoopId.Value
                    ) && (
                        !selectedProjectId.HasValue
                        || x.Phase.ProjectId == selectedProjectId.Value
                    )
                )
                .OrderBy(x => x.Employee != null ? x.Employee.EmpName : "")
                .ThenBy(x => x.PlanStart)
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
                    ProjectName = x.Phase?.Project?.ProjectDisplayName ?? "-",
                    PhaseSort = x.PhaseSort,
                    PhaseOrder = x.PhaseOrder ?? x.Phase?.PhaseOrder ?? 0,
                    PeriodOrder = x.Phase?.PeriodOrder ?? 0,
                    PhasePeriodLabel = x.Phase?.PhasePeriodLabel ?? "",
                    Title = x.Role ?? x.Phase?.PhaseName ?? "-",
                    Detail = x.Phase?.PhaseName ?? "-",
                    AssignStartDate = x.PlanStart,
                    AssignEndDate = x.PlanEnd,
                    PhasePlanStartDate = x.Phase?.PlanStart,
                    PhasePlanEndDate = x.Phase?.PeriodEndDate,
                    StartDate = x.PlanStart ?? x.Phase?.PlanStart,
                    EndDate = x.PlanEnd ?? x.Phase?.PlanEnd,
                    PeriodEndDate = x.Phase?.PeriodEndDate,
                    Status = x.WorkStatus ?? "",
                    WorkState = NormalizePhaseAssignState(x.WorkStatus),
                    Url = $"/PhaseAssigns?projectId={x.Phase?.ProjectId}&phaseId={x.Phase?.PhaseId}",
                    SortOrder = 10
                })
                .OrderBy(x => x.EmpName)
                .ThenBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ThenBy(x => x.PeriodOrder)
                .ThenBy(x => x.PhaseSort ?? int.MaxValue)
                .ThenBy(x => x.ItemId)
                .ThenBy(x => x.Title)
                .ToList();

            var scenarioProjectIds = selectedProjectId.HasValue
                ? new List<int> { selectedProjectId.Value }
                : phaseAssigns
                    .Where(x => x.Phase != null)
                    .Select(x => x.Phase!.ProjectId)
                    .Distinct()
                    .ToList();

            var testScenarioQuery = _context.TestScenarios
                .AsNoTracking()
                .Where(x => scenarioProjectIds.Contains(x.project_id));

            var scenarioSummary = await testScenarioQuery
                .GroupBy(x => (x.scenario_type ?? "BA").ToUpper())
                .Select(group => new
                {
                    Type = group.Key,
                    Total = group.Count(),
                    Completed = group.Count(x => x.status != null && x.status.ToUpper() == "PASSED")
                })
                .ToListAsync();

            var devScenario = scenarioSummary.FirstOrDefault(x => x.Type == "DEV");
            var baScenario = scenarioSummary.FirstOrDefault(x => x.Type == "BA");
            var completion = new PhaseWorkloadCompletionViewModel
            {
                PhaseAssignTotal = phaseAssigns.Count,
                PhaseAssignCompleted = phaseAssigns.Count(x =>
                    string.Equals(x.WorkStatus, "DONE", StringComparison.OrdinalIgnoreCase)),
                DevScenarioTotal = devScenario?.Total ?? 0,
                DevScenarioCompleted = devScenario?.Completed ?? 0,
                BaScenarioTotal = baScenario?.Total ?? 0,
                BaScenarioCompleted = baScenario?.Completed ?? 0
            };

            ViewBag.Year = selectedYear;
            ViewBag.YearTo = selectedYearTo;
            ViewBag.Month = selectedMonth;
            ViewBag.MonthTo = selectedMonthTo;
            ViewBag.SelectedEmpId = empId;
            ViewBag.SelectedCoopId = selectedCoopId;
            ViewBag.SelectedProjectId = selectedProjectId;
            ViewBag.CoopOptions = projectOptions
                .Where(x => x.Coop != null)
                .Select(x => x.Coop!)
                .GroupBy(x => x.CoopId)
                .Select(x => x.First())
                .OrderBy(x => x.CoopName)
                .ToList();
            ViewBag.ProjectOptions = projectOptions
                .Where(x => !selectedCoopId.HasValue || x.CoopId == selectedCoopId.Value)
                .ToList();
            ViewBag.MonthStart = monthStart;
            ViewBag.MonthEnd = monthEnd;

            return View(new PhaseWorkloadViewModel
            {
                Items = items,
                Completion = completion
            });
        }

        [HttpGet]
        public IActionResult KeepAlive()
        {
            HttpContext.Session.SetString("PhaseWorkload.LastSeen", DateTime.UtcNow.ToString("O"));
            return NoContent();
        }

        private static string NormalizePhaseAssignState(string? status)
        {
            return string.Equals(status, "DONE", StringComparison.OrdinalIgnoreCase)
                ? "DONE"
                : "IN_PROGRESS";
        }

        private static int ClampMonth(int month)
        {
            return Math.Clamp(month, 1, 12);
        }

        private static int? PositiveOrNull(int? value)
        {
            return value.HasValue && value.Value > 0 ? value : null;
        }

        private static bool TryParsePeriodMonth(string? value, out DateTime month)
        {
            month = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var parts = value.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var year) ||
                !int.TryParse(parts[1], out var monthNumber) ||
                monthNumber < 1 ||
                monthNumber > 12)
            {
                return false;
            }

            month = new DateTime(year, monthNumber, 1);
            return true;
        }

        private void SaveFilters(int year, int yearTo, int month, int monthTo, string? empId, int? coopId, int? projectId)
        {
            HttpContext.Session.SetInt32(FilterYearKey, year);
            HttpContext.Session.SetInt32(FilterYearToKey, yearTo);
            HttpContext.Session.SetInt32(FilterMonthKey, month);
            HttpContext.Session.SetInt32(FilterMonthToKey, monthTo);
            HttpContext.Session.SetString(FilterEmpIdKey, empId ?? "");
            HttpContext.Session.SetInt32(FilterCoopIdKey, coopId ?? 0);
            HttpContext.Session.SetInt32(FilterProjectIdKey, projectId ?? 0);
        }
    }
}
