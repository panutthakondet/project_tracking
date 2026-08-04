using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class ProjectStatusController : Controller
    {
        private const string DefaultProfileImagePath = "/images/Profile/profile.png";

        private readonly AppDbContext _context;

        public ProjectStatusController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("Home.Index")]
        public async Task<IActionResult> Index(int? projectId, int? departmentId)
        {
            var today = DateTime.Today;
            var th = new CultureInfo("th-TH");
            var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).Date;
            var weekEnd = weekStart.AddDays(6);

            var allProjects = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .Select(p => new ProjectRow
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName,
                    CoopName = p.Coop != null ? p.Coop.CoopName : null,
                    DepartmentId = p.DepartmentId,
                    BaEmpId = p.BaEmpId,
                    Status = p.Status,
                    EndDate = p.EndDate
                })
                .ToListAsync();

            var departmentOptions = await _context.ProjectDepartments
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DepartmentName)
                .Select(x => new ProjectDepartmentOption
                {
                    DepartmentId = x.DepartmentId,
                    DepartmentName = x.DepartmentName
                })
                .ToListAsync();

            var selectedDepartmentId = departmentId.HasValue
                && departmentOptions.Any(x => x.DepartmentId == departmentId.Value)
                    ? departmentId
                    : null;
            var availableProjects = selectedDepartmentId.HasValue
                ? allProjects.Where(x => x.DepartmentId == selectedDepartmentId.Value).ToList()
                : allProjects;

            var selectedProjectId = projectId.HasValue && availableProjects.Any(x => x.ProjectId == projectId.Value)
                ? projectId.Value
                : (int?)null;

            var projects = selectedProjectId.HasValue
                ? availableProjects.Where(x => x.ProjectId == selectedProjectId.Value).ToList()
                : availableProjects;
            var selectedProjectIds = projects.Select(x => x.ProjectId).ToList();

            var employees = await (
                from employee in _context.Employees.AsNoTracking()
                join login in _context.LoginUsers.AsNoTracking()
                    on employee.LoginUserId equals (int?)login.UserId into loginJoin
                from login in loginJoin.DefaultIfEmpty()
                select new EmployeeRow
                {
                    EmpId = employee.EmpId,
                    Name = employee.EmpName,
                    Position = employee.Position,
                    Status = employee.Status,
                    AvatarPath = login != null ? login.ProfileImagePath : null
                })
                .ToListAsync();

            var assigns = await (
                from assign in _context.PhaseAssigns.AsNoTracking()
                join phase in _context.ProjectPhases.AsNoTracking()
                    on assign.PhaseId equals phase.PhaseId
                join project in _context.Projects.AsNoTracking()
                    on phase.ProjectId equals project.ProjectId
                join employee in _context.Employees.AsNoTracking()
                    on assign.EmpId equals employee.EmpId
                join login in _context.LoginUsers.AsNoTracking()
                    on employee.LoginUserId equals (int?)login.UserId into loginJoin
                from login in loginJoin.DefaultIfEmpty()
                select new AssignRow
                {
                    AssignId = assign.AssignId,
                    EmpId = assign.EmpId,
                    EmployeeName = employee.EmpName,
                    EmployeePosition = employee.Position,
                    AvatarPath = login != null ? login.ProfileImagePath : null,
                    Role = assign.Role,
                    WorkStatus = assign.WorkStatus,
                    AssignPlanStart = assign.PlanStart,
                    AssignPlanEnd = assign.PlanEnd,
                    PhaseName = phase.PhaseName,
                    PhasePlanStart = phase.PlanStart,
                    PhasePlanEnd = phase.PlanEnd,
                    ProjectId = project.ProjectId,
                    ProjectName = project.Coop != null
                        ? project.Coop.CoopName + " - " + project.ProjectName
                        : project.ProjectName
                })
                .ToListAsync();

            if (selectedProjectId.HasValue || selectedDepartmentId.HasValue)
            {
                assigns = assigns
                    .Where(x => selectedProjectIds.Contains(x.ProjectId))
                    .ToList();
            }

            var openIssueCountsQuery = _context.ProjectIssues
                .AsNoTracking()
                .Where(issue => issue.IssueStatus.ToUpper() == "OPEN");

            var openSupportCountsQuery = _context.ProjectSupportOrders
                .AsNoTracking()
                .Where(order => order.AssignTo.HasValue
                    && order.Status != null
                    && order.Status.ToUpper() == "OPEN");

            if (selectedProjectId.HasValue)
            {
                openIssueCountsQuery = openIssueCountsQuery
                    .Where(issue => issue.ProjectId == selectedProjectId.Value);

                openSupportCountsQuery = openSupportCountsQuery
                    .Where(order => order.ProjectId == selectedProjectId.Value);
            }
            else if (selectedDepartmentId.HasValue)
            {
                openIssueCountsQuery = openIssueCountsQuery
                    .Where(issue => selectedProjectIds.Contains(issue.ProjectId));

                openSupportCountsQuery = openSupportCountsQuery
                    .Where(order => selectedProjectIds.Contains(order.ProjectId));
            }

            var openIssueCounts = await openIssueCountsQuery
                .GroupBy(issue => issue.AssignTo)
                .Select(group => new { EmpId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.EmpId, x => x.Count);

            var openSupportCounts = await openSupportCountsQuery
                .GroupBy(order => order.AssignTo!.Value)
                .Select(group => new { EmpId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.EmpId, x => x.Count);

            var totalProjects = projects.Count;
            var doneProjects = projects.Count(p => IsProjectDone(p));
            var delayedProjects = projects.Count(p => IsProjectDelayed(p, today));
            var inProgressProjects = projects.Count(p =>
                !IsProjectDone(p)
                && NormalizeProjectStatus(p.Status) == "IN_PROGRESS");
            var planProjects = projects.Count(p =>
                !IsProjectDone(p)
                && NormalizeProjectStatus(p.Status) != "IN_PROGRESS");

            var statusMetrics = new List<ProjectStatusMetric>
            {
                BuildStatusMetric("เสร็จสิ้น", doneProjects, totalProjects, "#19c979"),
                BuildStatusMetric("กำลังดำเนินการ", inProgressProjects, totalProjects, "#ffb444"),
                BuildStatusMetric("วางแผน", planProjects, totalProjects, "#33a1ff")
            };

            var model = new ProjectStatusDetailViewModel
            {
                SelectedProjectId = selectedProjectId,
                SelectedDepartmentId = selectedDepartmentId,
                DepartmentOptions = departmentOptions,
                SelectedProjectName = selectedProjectId.HasValue
                    ? projects.FirstOrDefault()?.ProjectDisplayName ?? "ทุกโครงการ"
                    : "ทุกโครงการ",
                ProjectOptions = availableProjects
                    .Select(p => new ProjectStatusOption
                    {
                        ProjectId = p.ProjectId,
                        ProjectName = p.ProjectDisplayName,
                        DepartmentId = p.DepartmentId
                    })
                    .ToList(),
                TotalProjects = totalProjects,
                DoneProjects = doneProjects,
                InProgressProjects = inProgressProjects,
                PlanProjects = planProjects,
                DelayedProjects = delayedProjects,
                WeekRangeText = $"{weekStart.ToString("dd MMM", th)} - {weekEnd.ToString("dd MMM yyyy", th)}",
                StatusMetrics = statusMetrics,
                ProjectStatusChart = BuildConicGradient(statusMetrics),
                TaskOverview = BuildTaskOverview(assigns, employees, openIssueCounts, openSupportCounts, today),
                ThisWeekTasks = BuildThisWeekTasks(assigns, weekStart, weekEnd, today, th)
            };

            BuildTeamGroups(model, projects, assigns, employees);

            return View(model);
        }

        private static ProjectStatusMetric BuildStatusMetric(string label, int count, int total, string color)
        {
            return new ProjectStatusMetric
            {
                Label = label,
                Count = count,
                Percent = total <= 0 ? 0 : Math.Round(count * 100m / total, 1),
                Color = color
            };
        }

        private static string BuildConicGradient(IEnumerable<ProjectStatusMetric> metrics)
        {
            var slices = metrics.Where(x => x.Count > 0).ToList();
            var total = slices.Sum(x => x.Count);

            if (total <= 0)
            {
                return "conic-gradient(#e5eaf3 0 100%)";
            }

            var current = 0m;
            var parts = new List<string>();

            foreach (var slice in slices)
            {
                var next = current + (slice.Count * 100m / total);
                parts.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1:0.##}% {2:0.##}%",
                    slice.Color,
                    current,
                    next));
                current = next;
            }

            return $"conic-gradient({string.Join(", ", parts)})";
        }

        private static List<ProjectTaskOverviewMember> BuildTaskOverview(
            List<AssignRow> assigns,
            List<EmployeeRow> employees,
            IReadOnlyDictionary<int, int> openIssueCounts,
            IReadOnlyDictionary<int, int> openSupportCounts,
            DateTime today)
        {
            var assignGroups = assigns
                .GroupBy(x => x.EmpId)
                .ToDictionary(x => x.Key, x => x.ToList());
            var employeesById = employees.ToDictionary(x => x.EmpId);
            var memberIds = assignGroups.Keys
                .Union(openIssueCounts.Keys)
                .Union(openSupportCounts.Keys);

            var grouped = memberIds
                .Select(empId =>
                {
                    assignGroups.TryGetValue(empId, out var rows);
                    rows ??= new List<AssignRow>();
                    employeesById.TryGetValue(empId, out var employee);

                    var done = rows.Count(IsAssignDone);
                    var delay = rows.Count(x => IsAssignDelayed(x, today));
                    var inProgress = Math.Max(0, rows.Count - done - delay);
                    openIssueCounts.TryGetValue(empId, out var openIssues);
                    openSupportCounts.TryGetValue(empId, out var openSupport);
                    var total = rows.Count + openIssues + openSupport;

                    return new ProjectTaskOverviewMember
                    {
                        EmpId = empId,
                        Name = rows.FirstOrDefault()?.EmployeeName ?? employee?.Name ?? "-",
                        AvatarPath = CleanProfilePath(rows.FirstOrDefault()?.AvatarPath ?? employee?.AvatarPath),
                        DoneCount = done,
                        InProgressCount = inProgress,
                        DelayCount = delay,
                        OpenIssueCount = openIssues,
                        OpenSupportCount = openSupport,
                        TotalCount = total
                    };
                })
                .Where(x => x.TotalCount > 0)
                .OrderByDescending(x => x.TotalCount)
                .ThenBy(x => x.Name)
                .ToList();

            var maxTotal = Math.Max(1, grouped.Count == 0 ? 1 : grouped.Max(x => x.TotalCount));

            foreach (var row in grouped)
            {
                row.TotalHeightPercent = Math.Clamp((int)Math.Round(row.TotalCount * 100m / maxTotal), 24, 100);
                row.DoneHeightPercent = Percent(row.DoneCount, row.TotalCount);
                row.InProgressHeightPercent = Percent(row.InProgressCount, row.TotalCount);
                row.DelayHeightPercent = Percent(row.DelayCount, row.TotalCount);
                row.OpenIssueHeightPercent = Percent(row.OpenIssueCount, row.TotalCount);
                row.OpenSupportHeightPercent = Percent(row.OpenSupportCount, row.TotalCount);
            }

            return grouped;
        }

        private static List<ProjectWeekTask> BuildThisWeekTasks(
            List<AssignRow> assigns,
            DateTime weekStart,
            DateTime weekEnd,
            DateTime today,
            CultureInfo th)
        {
            var candidates = assigns
                .Where(x => OverlapsWeek(GetStartDate(x), GetDueDate(x), weekStart, weekEnd))
                .ToList();

            return candidates
                .OrderBy(x => TaskStatusRank(x, today))
                .ThenBy(x => GetDueDate(x) ?? DateTime.MaxValue)
                .ThenBy(x => x.ProjectName)
                .Take(5)
                .Select(x =>
                {
                    var dueDate = GetDueDate(x);
                    var status = BuildTaskStatus(x, today);

                    return new ProjectWeekTask
                    {
                        AssignId = x.AssignId,
                        Title = !string.IsNullOrWhiteSpace(x.Role) ? x.Role!.Trim() : x.PhaseName,
                        ProjectName = x.ProjectName,
                        OwnerName = x.EmployeeName,
                        AvatarPath = CleanProfilePath(x.AvatarPath),
                        DueDate = dueDate,
                        DueDateText = dueDate.HasValue ? dueDate.Value.ToString("dd MMM yy", th) : "-",
                        StatusText = status.Text,
                        StatusClass = status.ClassName
                    };
                })
                .ToList();
        }

        private static void BuildTeamGroups(
            ProjectStatusDetailViewModel model,
            List<ProjectRow> projects,
            List<AssignRow> assigns,
            List<EmployeeRow> employees)
        {
            var activeEmployees = employees
                .Where(x => string.IsNullOrWhiteSpace(x.Status) || x.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var baEmpIds = projects
                .Where(x => x.BaEmpId.HasValue)
                .Select(x => x.BaEmpId!.Value)
                .Distinct()
                .ToHashSet();

            if (model.SelectedProjectId.HasValue)
            {
                var projectTeamEmpIds = assigns
                    .Select(x => x.EmpId)
                    .Concat(baEmpIds)
                    .Distinct()
                    .ToHashSet();

                var hasProjectManager = activeEmployees.Any(employee =>
                    projectTeamEmpIds.Contains(employee.EmpId) && IsProjectManagerPosition(employee.Position));

                if (!hasProjectManager)
                {
                    foreach (var projectManager in activeEmployees.Where(employee => IsProjectManagerPosition(employee.Position)))
                    {
                        projectTeamEmpIds.Add(projectManager.EmpId);
                    }
                }

                activeEmployees = activeEmployees
                    .Where(x => projectTeamEmpIds.Contains(x.EmpId))
                    .ToList();
            }

            var assignGroups = assigns
                .GroupBy(x => x.EmpId)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var rows = group.ToList();
                        return new
                        {
                            RoleText = string.Join(" ", rows.Select(x => x.Role).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                            WorkCount = rows.Count
                        };
                    });

            var baProjectCounts = projects
                .Where(x => x.BaEmpId.HasValue)
                .GroupBy(x => x.BaEmpId!.Value)
                .ToDictionary(x => x.Key, x => x.Count());

            var teamRows = activeEmployees
                .Select(employee =>
                {
                    assignGroups.TryGetValue(employee.EmpId, out var assignInfo);
                    baProjectCounts.TryGetValue(employee.EmpId, out var baProjectCount);

                    return new TeamMemberRow
                    {
                        EmpId = employee.EmpId,
                        Name = employee.Name,
                        Position = employee.Position,
                        AvatarPath = employee.AvatarPath,
                        RoleText = baEmpIds.Contains(employee.EmpId)
                            ? $"Business Analyst {assignInfo?.RoleText}".Trim()
                            : assignInfo?.RoleText,
                        WorkCount = (assignInfo?.WorkCount ?? 0) + baProjectCount
                    };
                })
                .ToList();

            var orderedPositions = new[]
            {
                "Business Development Manager",
                "Project Manager",
                "Project Manager IT",
                "Business Analyst",
                "Research and Development",
                "Graphic Designer",
                "Programmer Mobile Developer",
                "Programmer Service",
                "Programmer Web Developer"
            };

            var used = new HashSet<int>();
            model.TeamGroups = orderedPositions
                .Select(position => new ProjectTeamGroup
                {
                    Label = position,
                    ClassName = PositionClass(position),
                    Members = PickPositionMembers(teamRows, used, position)
                })
                .Where(x => x.Members.Count > 0)
                .ToList();

            var otherMembers = PickOtherMembers(teamRows, used);
            if (otherMembers.Count > 0)
            {
                model.TeamGroups.Add(new ProjectTeamGroup
                {
                    Label = "อื่นๆ",
                    ClassName = "other",
                    Members = otherMembers
                });
            }
        }

        private static ProjectOrgMember ToOrgMember(EmployeeRow employee, string fallbackRole)
        {
            return new ProjectOrgMember
            {
                EmpId = employee.EmpId,
                Name = string.IsNullOrWhiteSpace(employee.Name) ? "-" : employee.Name.Trim(),
                Role = string.IsNullOrWhiteSpace(employee.Position) ? fallbackRole : employee.Position!.Trim(),
                AvatarPath = CleanProfilePath(employee.AvatarPath)
            };
        }

        private static List<ProjectOrgMember> PickPositionMembers(
            IEnumerable<TeamMemberRow> rows,
            HashSet<int> used,
            string position)
        {
            return rows
                .Where(x => !used.Contains(x.EmpId) && IsSamePosition(x.Position, position))
                .OrderByDescending(x => x.WorkCount)
                .ThenBy(x => x.Name)
                .Select(row =>
                {
                    used.Add(row.EmpId);

                    return new ProjectOrgMember
                    {
                        EmpId = row.EmpId,
                        Name = string.IsNullOrWhiteSpace(row.Name) ? "-" : row.Name.Trim(),
                        Role = DisplayRole(row, position),
                        AvatarPath = CleanProfilePath(row.AvatarPath),
                        WorkCount = row.WorkCount
                    };
                })
                .ToList();
        }

        private static List<ProjectOrgMember> PickOtherMembers(IEnumerable<TeamMemberRow> rows, HashSet<int> used)
        {
            return rows
                .Where(x => !used.Contains(x.EmpId))
                .OrderByDescending(x => x.WorkCount)
                .ThenBy(x => x.Position)
                .ThenBy(x => x.Name)
                .Select(row =>
                {
                    used.Add(row.EmpId);

                    return new ProjectOrgMember
                    {
                        EmpId = row.EmpId,
                        Name = string.IsNullOrWhiteSpace(row.Name) ? "-" : row.Name.Trim(),
                        Role = DisplayRole(row, "อื่นๆ"),
                        AvatarPath = CleanProfilePath(row.AvatarPath),
                        WorkCount = row.WorkCount
                    };
                })
                .ToList();
        }

        private static string DisplayRole(TeamMemberRow row, string fallbackRole)
        {
            if (!string.IsNullOrWhiteSpace(row.Position))
            {
                return row.Position.Trim();
            }

            if (!string.IsNullOrWhiteSpace(row.RoleText))
            {
                return row.RoleText.Trim();
            }

            return fallbackRole;
        }

        private static bool IsProjectManager(TeamMemberRow row)
        {
            return HasAnyRoleText(row, "project manager", "pm", "ผู้จัดการ", "ผู้จัดการโครงการ");
        }

        private static bool IsBusinessAnalyst(TeamMemberRow row)
        {
            return HasAnyRoleText(row, "business analyst", "ba", "analyst", "วิเคราะห์");
        }

        private static bool IsCg(TeamMemberRow row)
        {
            return HasAnyRoleText(row, "cg", "graphic", "designer", "กราฟิก", "ออกแบบ");
        }

        private static bool IsProgrammer(TeamMemberRow row)
        {
            return HasAnyRoleText(row, "programmer", "developer", "dev", "program", "พัฒนา", "โปรแกรม");
        }

        private static bool HasAnyRoleText(TeamMemberRow row, params string[] terms)
        {
            var text = $"{row.Position} {row.RoleText}".Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSamePosition(string? actual, string expected)
        {
            return string.Equals(
                NormalizePosition(actual),
                NormalizePosition(expected),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProjectManagerPosition(string? position)
        {
            return IsSamePosition(position, "Project Manager")
                || IsSamePosition(position, "Project Manager IT");
        }

        private static string NormalizePosition(string? value)
        {
            return string.Join(" ", (value ?? string.Empty)
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string PositionClass(string position)
        {
            return NormalizePosition(position)
                .ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("/", "-");
        }

        private sealed class TeamMemberRow
        {
            public int EmpId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Position { get; set; }
            public string? AvatarPath { get; set; }
            public string? RoleText { get; set; }
            public int WorkCount { get; set; }
        }

        private static string CleanProfilePath(string? path)
            => ProfileImagePathResolver.Normalize(path);

        private static bool IsProjectDone(ProjectRow project)
        {
            return NormalizeProjectStatus(project.Status) == "DONE";
        }

        private static bool IsProjectDelayed(ProjectRow project, DateTime today)
        {
            return !IsProjectDone(project)
                && project.EndDate.HasValue
                && project.EndDate.Value.Date < today;
        }

        private static string NormalizeProjectStatus(string? status)
        {
            var normalized = (status ?? "PLAN").Trim().ToUpperInvariant();
            return normalized switch
            {
                "DONE" or "COMPLETED" or "COMPLETE" or "FINISHED" or "เสร็จสิ้น" or "เสร็จแล้ว" => "DONE",
                "IN_PROGRESS" or "IN PROGRESS" or "WIP" or "WORKING" or "กำลังดำเนินการ" or "กำลังทำ" => "IN_PROGRESS",
                _ => "PLAN"
            };
        }

        private static bool IsAssignDone(AssignRow assign)
        {
            return string.Equals(assign.WorkStatus, "DONE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAssignDelayed(AssignRow assign, DateTime today)
        {
            var dueDate = GetDueDate(assign);
            return !IsAssignDone(assign)
                && dueDate.HasValue
                && dueDate.Value.Date < today;
        }

        private static (string Text, string ClassName) BuildTaskStatus(AssignRow assign, DateTime today)
        {
            if (IsAssignDone(assign))
            {
                return ("เสร็จแล้ว", "done");
            }

            if (IsAssignDelayed(assign, today))
            {
                return ("ล่าช้า", "stuck");
            }

            return ("กำลังทำ", "working");
        }

        private static int TaskStatusRank(AssignRow assign, DateTime today)
        {
            if (IsAssignDelayed(assign, today))
            {
                return 0;
            }

            return IsAssignDone(assign) ? 2 : 1;
        }

        private static DateTime? GetStartDate(AssignRow assign)
        {
            return assign.AssignPlanStart ?? assign.PhasePlanStart;
        }

        private static DateTime? GetDueDate(AssignRow assign)
        {
            return assign.AssignPlanEnd ?? assign.PhasePlanEnd;
        }

        private static bool OverlapsWeek(DateTime? startDate, DateTime? endDate, DateTime weekStart, DateTime weekEnd)
        {
            var start = (startDate ?? endDate)?.Date;
            var end = (endDate ?? startDate)?.Date;

            return start.HasValue
                && end.HasValue
                && start.Value <= weekEnd
                && end.Value >= weekStart;
        }

        private static int Percent(int count, int total)
        {
            return total <= 0 ? 0 : (int)Math.Round(count * 100m / total);
        }

        private sealed class ProjectRow
        {
            public int ProjectId { get; set; }
            public string ProjectName { get; set; } = string.Empty;
            public string? CoopName { get; set; }
            public int? DepartmentId { get; set; }
            public string ProjectDisplayName => string.IsNullOrWhiteSpace(CoopName)
                ? ProjectName
                : $"{CoopName} - {ProjectName}";
            public int? BaEmpId { get; set; }
            public string Status { get; set; } = "PLAN";
            public DateTime? EndDate { get; set; }
        }

        private sealed class EmployeeRow
        {
            public int EmpId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Position { get; set; }
            public string? Status { get; set; }
            public string? AvatarPath { get; set; }
        }

        private sealed class AssignRow
        {
            public int AssignId { get; set; }
            public int EmpId { get; set; }
            public string EmployeeName { get; set; } = string.Empty;
            public string? EmployeePosition { get; set; }
            public string? AvatarPath { get; set; }
            public string? Role { get; set; }
            public string? WorkStatus { get; set; }
            public DateTime? AssignPlanStart { get; set; }
            public DateTime? AssignPlanEnd { get; set; }
            public string PhaseName { get; set; } = string.Empty;
            public DateTime? PhasePlanStart { get; set; }
            public DateTime? PhasePlanEnd { get; set; }
            public int ProjectId { get; set; }
            public string ProjectName { get; set; } = string.Empty;
        }
    }
}
