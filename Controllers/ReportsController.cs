using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Middleware;
using ProjectTracking.Models;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class ReportsController : BaseController
    {
        private readonly AppDbContext _context;

        public ReportsController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("Reports.Index")]
        public async Task<IActionResult> Index(int? departmentId)
        {
            var departments = await _context.ProjectDepartments
                .AsNoTracking()
                .Where(row => row.IsActive)
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.DepartmentName)
                .ToListAsync();
            if (departmentId.HasValue && departments.All(row => row.DepartmentId != departmentId.Value))
                departmentId = null;
            ViewBag.ProjectDepartments = departments;
            ViewBag.SelectedDepartmentId = departmentId;

            var model = new ReportCenterViewModel
            {
                GeneratedAt = DateTime.Now,
                Reports = new List<ReportCardViewModel>
                {
                    new()
                    {
                        Group = "Executive",
                        Title = "Executive Project Summary",
                        Description = "ภาพรวมโครงการสำหรับผู้บริหาร รวมโครงการเสี่ยง งานล่าช้า Issue/Support ค้าง และภาระงานทีม",
                        Controller = "Reports",
                        Action = "Executive",
                        Icon = "/images/menu-icons/reports.svg",
                        Tone = "teal",
                        IsPrimary = true
                    },
                    new()
                    {
                        Group = "Projects",
                        Title = "Projects Report",
                        Description = "รายงานข้อมูลโครงการ BA ระบบ ฐานข้อมูล บัญชีทดสอบ Remote/Figma และช่วงเวลาโครงการ",
                        Controller = "Projects",
                        Action = "ViewOnly",
                        PermissionKey = "Projects.Index",
                        Icon = "/images/menu-icons/projects.svg",
                        Tone = "blue"
                    },
                    new()
                    {
                        Group = "Project Phases",
                        Title = "Assignment Report",
                        Description = "รายงานการมอบหมายงานตามโครงการ พนักงาน และบทบาท",
                        Controller = "PhaseAssigns",
                        Action = "Print",
                        Icon = "/images/menu-icons/assign.svg",
                        Tone = "green"
                    },
                    new()
                    {
                        Group = "Project Phases",
                        Title = "Phase Status Report",
                        Description = "รายงานสถานะงานค้าง งานล่าช้า และช่วงแผนงานตามผู้รับผิดชอบ",
                        Controller = "PhaseStatusReport",
                        Action = "Index",
                        Icon = "/images/menu-icons/phases.svg",
                        Tone = "orange"
                    },
                    new()
                    {
                        Group = "Project Phases",
                        Title = "Task Progress Report",
                        Description = "รายละเอียดงานรายปีจากรายการ Assign โดยอิงสถานะจากส่วนงานของโครงการ",
                        Controller = "Reports",
                        Action = "TaskProgress",
                        PermissionKey = "Reports.Index",
                        Icon = "/images/menu-icons/workload.svg",
                        Tone = "cyan"
                    },
                    new()
                    {
                        Group = "Project Phases",
                        Title = "Pending & Next 2 Weeks Work",
                        Description = "รายงาน List งานค้าง และงานที่จะทำใน 2 สัปดาห์ข้างหน้า รวม Assign Issue และ Support",
                        Controller = "Reports",
                        Action = "PendingWork",
                        PermissionKey = "Reports.Index",
                        Icon = "/images/menu-icons/workload.svg",
                        Tone = "orange"
                    },
                    new()
                    {
                        Group = "Test Scenario",
                        Title = "Test Scenario Report",
                        Description = "รายงาน Test Scenario ตามโครงการ กลุ่มทดสอบ สถานะ และระดับความสำคัญ",
                        Controller = "TestScenarios",
                        Action = "PrintReport",
                        PermissionKey = "TestScenarios.Export",
                        Icon = "/images/menu-icons/test-scenarios.svg",
                        Tone = "purple"
                    },
                    new()
                    {
                        Group = "Issues",
                        Title = "Issues Report",
                        Description = "รายงานปัญหาโครงการ สถานะ Issue/Dev ลำดับความสำคัญ และจำนวน FAIL",
                        Controller = "ProjectIssues",
                        Action = "ViewOnly",
                        Icon = "/images/menu-icons/issues.svg",
                        Tone = "pink"
                    },
                    new()
                    {
                        Group = "Support",
                        Title = "Support Report",
                        Description = "รายงานงานแก้ไขช่วงรับประกัน แยกตามโครงการ สถานะ ผู้รับผิดชอบ และวันที่สิ้นสุด",
                        Controller = "SupportOrders",
                        Action = "ViewOnly",
                        PermissionKey = "SupportOrders.Index",
                        Icon = "/images/menu-icons/workload.svg",
                        Tone = "orange"
                    },
                    new()
                    {
                        Group = "Field Service",
                        Title = "Field Service Report",
                        Description = "รายงานงานเข้าไซต์ แยกตามช่วงวันที่ สถานะ พนักงานผู้รับผิดชอบ และสหกรณ์",
                        Controller = "FieldService",
                        Action = "Report",
                        PermissionKey = "Reports.Index",
                        Icon = "/images/menu-icons/field-service.svg",
                        Tone = "teal"
                    },
                    new()
                    {
                        Group = "Followups",
                        Title = "Followups Report",
                        Description = "รายงานงานติดตามตามโครงการ ผู้รับผิดชอบ สถานะ วันที่ติดตามครั้งถัดไป และการติดต่อครั้งล่าสุด",
                        Controller = "Followups",
                        Action = "ViewOnly",
                        PermissionKey = "Followups.Index",
                        Icon = "/images/menu-icons/followups.svg",
                        Tone = "green"
                    },
                    new()
                    {
                        Group = "Meetings",
                        Title = "Meetings Report",
                        Description = "รายงานการประชุมตามโครงการ วันที่ เวลา สถานที่ กลุ่มผู้ประชุม และผู้เข้าร่วมประชุม",
                        Controller = "Meetings",
                        Action = "ViewOnly",
                        PermissionKey = "Meetings.Index",
                        Icon = "/images/menu-icons/meetings.svg",
                        Tone = "purple"
                    },
                    new()
                    {
                        Group = "Attendance",
                        Title = "Attendance Map",
                        Description = "รายงาน Check-in/Check-out และตำแหน่งการลงเวลาของทีม",
                        Controller = "Attendance",
                        Action = "Map",
                        Icon = "/images/menu-icons/attendance.svg",
                        Tone = "cyan"
                    }
                }
            };

            return View(model);
        }

        [RequireMenu("Reports.Index")]
        public async Task<IActionResult> PendingWork(int? projectId, int? empId, int? baEmpId, string? workType, string? section, int? departmentId)
        {
            return View(await BuildPendingWorkReportAsync(projectId, empId, baEmpId, workType, section, departmentId));
        }

        [RequireMenu("Reports.Executive")]
        public async Task<IActionResult> Executive(int? departmentId)
        {
            return View(await BuildExecutiveReportAsync(departmentId));
        }

        [RequireMenu("Home.Index")]
        public async Task<IActionResult> WorkDuration(
            int? departmentId,
            int? empId,
            DateTime? startDate,
            DateTime? endDate)
        {
            return View(await BuildWorkDurationReportAsync(departmentId, empId, startDate, endDate));
        }

        [RequireMenu("Reports.Index")]
        public async Task<IActionResult> TaskProgress(int? year, int? projectId, int? empId, int? baEmpId, string? status, string? assignStatus, int? departmentId)
        {
            var departmentOptions = await GetDepartmentOptionsAsync();
            departmentId = ValidateDepartmentId(departmentId, departmentOptions);
            var th = new System.Globalization.CultureInfo("th-TH");
            var selectedYear = year ?? DateTime.Today.Year;
            var selectedStatus = Norm(status);
            var selectedAssignStatus = Norm(assignStatus);
            var username = HttpContext.Session.GetString("Username") ?? "-";
            var phaseStatusDefinitions = await _context.ProjectPhaseStatuses
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.StatusId)
                .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                .ToListAsync();
            var assignStatusDefinitions = await _context.PhaseAssignStatuses
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.StatusId)
                .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                .ToListAsync();

            var projectQuery = _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .Include(p => p.BA)
                .Include(p => p.TeamMembers)
                    .ThenInclude(m => m.Employee)
                .AsQueryable();
            if (departmentId.HasValue)
                projectQuery = projectQuery.Where(p => p.DepartmentId == departmentId.Value);
            var projects = await projectQuery
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var projectOptions = projects
                .Select(p => new ProjectReportOptionViewModel
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectDisplayName,
                    CoopName = p.Coop?.CoopName ?? ""
                })
                .ToList();

            var baOptions = projects
                .SelectMany(p => p.BusinessAnalysts)
                .Select(employee => new EmployeeReportOptionViewModel
                {
                    EmpId = employee.EmpId,
                    EmpName = employee.EmpName
                })
                .GroupBy(x => x.EmpId)
                .Select(x => x.First())
                .OrderBy(x => x.EmpName)
                .ToList();

            var employeeQuery = _context.Employees.AsNoTracking().AsQueryable();
            if (departmentId.HasValue)
                employeeQuery = employeeQuery.Where(e => e.DepartmentId == departmentId.Value);
            var employees = await employeeQuery
                .OrderBy(e => e.EmpName)
                .Select(e => new EmployeeReportOptionViewModel
                {
                    EmpId = e.EmpId,
                    EmpName = e.EmpName ?? "-"
                })
                .ToListAsync();

            var assignQuery = _context.PhaseAssigns
                .Include(a => a.Employee)
                .Include(a => a.StatusDefinition)
                .Include(a => a.Phase)
                    .ThenInclude(p => p!.StatusDefinition)
                .Include(a => a.Phase)
                    .ThenInclude(p => p!.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(a => a.Phase)
                    .ThenInclude(p => p!.Project)
                    .ThenInclude(p => p!.TeamMembers)
                        .ThenInclude(m => m.Employee)
                .AsNoTracking().AsQueryable();
            if (departmentId.HasValue)
                assignQuery = assignQuery.Where(a => a.Phase != null && a.Phase.Project != null && a.Phase.Project.DepartmentId == departmentId.Value);
            var assigns = await assignQuery
                .ToListAsync();

            var allRows = assigns
                .Where(a => a.Phase != null)
                .Select(a =>
                {
                    var phase = a.Phase!;
                    var project = phase.Project;
                    var bucketDate = AssignPhaseBucketDate(a, phase);
                    var phaseStatusCode = phase.StatusDefinition?.StatusCode ?? Norm(phase.PhaseStatus);
                    var assignStatusCode = a.StatusDefinition?.StatusCode ?? Norm(a.WorkStatus);
                    var phaseDefinition = phaseStatusDefinitions.FirstOrDefault(x =>
                        string.Equals(x.StatusCode, phaseStatusCode, StringComparison.OrdinalIgnoreCase));
                    var assignDefinition = assignStatusDefinitions.FirstOrDefault(x =>
                        string.Equals(x.StatusCode, assignStatusCode, StringComparison.OrdinalIgnoreCase));
                    var month = bucketDate?.Month ?? 0;

                    return new TaskProgressReportRowViewModel
                    {
                        AssignId = a.AssignId,
                        ProjectId = phase.ProjectId,
                        EmpId = a.EmpId,
                        BaEmpId = project?.BaEmpId,
                        BaEmpIds = project?.BusinessAnalysts.Select(e => e.EmpId).ToList() ?? new List<int>(),
                        ProjectName = project?.ProjectDisplayName ?? "-",
                        EmployeeName = a.Employee?.EmpName ?? "-",
                        PhaseName = phase.PhaseName,
                        PhasePeriodLabel = phase.PhasePeriodLabel,
                        Role = string.IsNullOrWhiteSpace(a.Role) ? "-" : a.Role!,
                        PhaseStatusCode = phaseStatusCode,
                        StatusText = phase.StatusDefinition?.StatusDesc
                            ?? WorkflowStatusPresentation.Description(phaseStatusDefinitions, phase.PhaseStatus),
                        StatusTone = phaseDefinition == null
                            ? "muted"
                            : WorkflowStatusPresentation.Tone(phaseDefinition, phaseStatusDefinitions.IndexOf(phaseDefinition)),
                        AssignStatus = assignStatusCode,
                        AssignStatusText = a.StatusDefinition?.StatusDesc
                            ?? WorkflowStatusPresentation.Description(assignStatusDefinitions, a.WorkStatus),
                        AssignStatusTone = assignDefinition == null
                            ? "muted"
                            : WorkflowStatusPresentation.Tone(assignDefinition, assignStatusDefinitions.IndexOf(assignDefinition)),
                        PlanStart = a.PlanStart ?? phase.PlanStart,
                        PlanEnd = a.PlanEnd ?? phase.PlanEnd,
                        PeriodEnd = phase.PeriodEndDate,
                        BucketDate = bucketDate,
                        Month = month,
                        MonthName = month > 0 ? new DateTime(selectedYear, month, 1).ToString("MMM", th) : "-"
                    };
                })
                .Where(r => r.BucketDate.HasValue && r.BucketDate.Value.Year == selectedYear)
                .ToList();

            var rows = allRows
                .Where(r => !projectId.HasValue || r.ProjectId == projectId.Value)
                .Where(r => !empId.HasValue || r.EmpId == empId.Value)
                .Where(r => !baEmpId.HasValue || r.BaEmpIds.Contains(baEmpId.Value))
                .Where(r => string.IsNullOrWhiteSpace(selectedStatus) || r.PhaseStatusCode == selectedStatus)
                .Where(r => string.IsNullOrWhiteSpace(selectedAssignStatus) || r.AssignStatus == selectedAssignStatus)
                .OrderBy(r => r.ProjectName)
                .ThenBy(r => r.Month)
                .ThenBy(r => r.EmployeeName)
                .ThenBy(r => r.PhasePeriodLabel)
                .ThenBy(r => r.Role)
                .ToList();

            var seq = 1;
            foreach (var row in rows)
            {
                row.Seq = seq++;
            }

            var months = Enumerable.Range(1, 12)
                .Select(month => new TaskProgressMonthViewModel
                {
                    Month = month,
                    MonthName = new DateTime(selectedYear, month, 1).ToString("MMM", th),
                    Total = rows.Count(r => r.Month == month),
                    StatusCounts = phaseStatusDefinitions.Select((definition, index) => new TaskProgressStatusCountViewModel
                    {
                        StatusCode = definition.StatusCode,
                        StatusDesc = definition.StatusDesc,
                        Tone = WorkflowStatusPresentation.Tone(definition, index),
                        SortOrder = definition.SortOrder,
                        Count = rows.Count(r => r.Month == month
                            && string.Equals(r.PhaseStatusCode, definition.StatusCode, StringComparison.OrdinalIgnoreCase))
                    }).ToList()
                })
                .ToList();

            var availableYears = allRows
                .Where(r => r.BucketDate.HasValue)
                .Select(r => r.BucketDate!.Value.Year)
                .Concat(Enumerable.Range(DateTime.Today.Year - 2, 5))
                .Append(selectedYear)
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();

            var assignStatusOptions = assignStatusDefinitions.Select(x => x.StatusCode).ToList();

            var model = new TaskProgressReportViewModel
            {
                DepartmentId = departmentId,
                DepartmentOptions = departmentOptions,
                GeneratedAt = DateTime.Now,
                GeneratedBy = username,
                Year = selectedYear,
                ProjectId = projectId,
                EmpId = empId,
                BaEmpId = baEmpId,
                Status = selectedStatus,
                AssignStatus = selectedAssignStatus,
                YearOptions = availableYears,
                ProjectOptions = projectOptions,
                EmployeeOptions = employees,
                BaOptions = baOptions,
                AssignStatusOptions = assignStatusOptions,
                PhaseStatusDefinitions = phaseStatusDefinitions,
                AssignStatusDefinitions = assignStatusDefinitions,
                Summary = new TaskProgressSummaryViewModel
                {
                    Total = rows.Count,
                    Projects = rows.Select(r => r.ProjectId).Distinct().Count(),
                    Employees = rows.Select(r => r.EmployeeName).Distinct().Count(),
                    StatusCounts = phaseStatusDefinitions.Select((definition, index) => new TaskProgressStatusCountViewModel
                    {
                        StatusCode = definition.StatusCode,
                        StatusDesc = definition.StatusDesc,
                        Tone = WorkflowStatusPresentation.Tone(definition, index),
                        SortOrder = definition.SortOrder,
                        Count = rows.Count(r => string.Equals(r.PhaseStatusCode, definition.StatusCode, StringComparison.OrdinalIgnoreCase))
                    }).ToList()
                },
                Months = months,
                Rows = rows
            };

            return View(model);
        }

        private async Task<PendingWorkReportViewModel> BuildPendingWorkReportAsync(int? projectId, int? empId, int? baEmpId, string? workType, string? section, int? departmentId)
        {
            var departmentOptions = await GetDepartmentOptionsAsync();
            departmentId = ValidateDepartmentId(departmentId, departmentOptions);
            var today = DateTime.Today;
            var horizonDate = today.AddDays(14);
            var username = HttpContext.Session.GetString("Username") ?? "-";
            var selectedWorkType = Norm(workType);
            var selectedSection = Norm(section);

            var projectQuery = _context.Projects
                .Include(p => p.Coop)
                .Include(p => p.BA)
                .Include(p => p.TeamMembers)
                    .ThenInclude(m => m.Employee)
                .AsNoTracking().AsQueryable();
            if (departmentId.HasValue)
                projectQuery = projectQuery.Where(p => p.DepartmentId == departmentId.Value);
            var projects = await projectQuery
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var projectOptions = projects
                .Select(p => new ProjectReportOptionViewModel
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectDisplayName,
                    CoopName = p.Coop?.CoopName ?? ""
                })
                .ToList();

            var baOptions = projects
                .SelectMany(p => p.BusinessAnalysts)
                .Select(employee => new EmployeeReportOptionViewModel
                {
                    EmpId = employee.EmpId,
                    EmpName = employee.EmpName
                })
                .GroupBy(x => x.EmpId)
                .Select(x => x.First())
                .OrderBy(x => x.EmpName)
                .ToList();

            var employeeQuery = _context.Employees.AsNoTracking().AsQueryable();
            if (departmentId.HasValue)
                employeeQuery = employeeQuery.Where(e => e.DepartmentId == departmentId.Value);
            var employees = await employeeQuery
                .OrderBy(e => e.EmpName)
                .Select(e => new EmployeeReportOptionViewModel
                {
                    EmpId = e.EmpId,
                    EmpName = e.EmpName ?? "-"
                })
                .ToListAsync();

            var assignQuery = _context.PhaseAssigns
                .Include(a => a.Employee)
                .Include(a => a.Phase)
                    .ThenInclude(p => p!.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(a => a.Phase)
                    .ThenInclude(p => p!.Project)
                    .ThenInclude(p => p!.BA)
                .Include(a => a.Phase)
                    .ThenInclude(p => p!.Project)
                    .ThenInclude(p => p!.TeamMembers)
                        .ThenInclude(m => m.Employee)
                .AsNoTracking().AsQueryable();
            if (departmentId.HasValue)
                assignQuery = assignQuery.Where(a => a.Phase != null && a.Phase.Project != null && a.Phase.Project.DepartmentId == departmentId.Value);
            var assigns = await assignQuery
                .ToListAsync();

            var issueQuery = _context.ProjectIssues
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.Project)
                    .ThenInclude(p => p!.BA)
                .Include(i => i.Project)
                    .ThenInclude(p => p!.TeamMembers)
                        .ThenInclude(m => m.Employee)
                .Include(i => i.Employee)
                .AsNoTracking().AsQueryable();
            if (departmentId.HasValue)
                issueQuery = issueQuery.Where(i => i.Project != null && i.Project.DepartmentId == departmentId.Value);
            var issues = await issueQuery
                .ToListAsync();

            var supportQuery = _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Project)
                    .ThenInclude(p => p!.BA)
                .Include(o => o.Project)
                    .ThenInclude(p => p!.TeamMembers)
                        .ThenInclude(m => m.Employee)
                .Include(o => o.Employee)
                .AsNoTracking().AsQueryable();
            if (departmentId.HasValue)
                supportQuery = supportQuery.Where(o => o.Project != null && o.Project.DepartmentId == departmentId.Value);
            var supportOrders = await supportQuery
                .ToListAsync();

            var rows = new List<PendingWorkReportRowViewModel>();

            foreach (var assign in assigns.Where(a => Norm(a.WorkStatus) != "DONE"))
            {
                var phase = assign.Phase;
                var project = phase?.Project;
                var startDate = assign.PlanStart ?? phase?.PlanStart;
                var dueDate = assign.PlanEnd ?? phase?.PlanEnd ?? phase?.PeriodEndDate;
                var rowSection = PendingWorkSection(today, horizonDate, startDate, dueDate);
                if (string.IsNullOrWhiteSpace(rowSection))
                {
                    continue;
                }

                rows.Add(new PendingWorkReportRowViewModel
                {
                    Section = rowSection,
                    SectionText = PendingSectionText(rowSection),
                    Tone = PendingTone(today, dueDate, rowSection),
                    WorkType = "ASSIGN",
                    WorkTypeText = "Assign",
                    ProjectId = project?.ProjectId,
                    CoopName = project?.Coop?.CoopName ?? "",
                    ProjectName = project?.ProjectName ?? "-",
                    Title = string.IsNullOrWhiteSpace(assign.Role) ? $"Assign #{assign.AssignId}" : assign.Role!,
                    Detail = phase == null ? "-" : $"{phase.PhasePeriodLabel}: {phase.PhaseName}",
                    OwnerEmpId = assign.EmpId,
                    OwnerName = assign.Employee?.EmpName ?? "-",
                    BaEmpId = project?.BaEmpId,
                    BaEmpIds = project?.BusinessAnalysts.Select(e => e.EmpId).ToList() ?? new List<int>(),
                    BaName = string.IsNullOrWhiteSpace(project?.BusinessAnalystNames) ? "-" : project.BusinessAnalystNames,
                    Status = string.IsNullOrWhiteSpace(assign.WorkStatus) ? "-" : assign.WorkStatus!,
                    Priority = "-",
                    StartDate = startDate,
                    DueDate = dueDate,
                    PeriodEndDate = phase?.PeriodEndDate,
                    OverdueDays = DaysOverdue(today, dueDate),
                    DaysUntilDue = DaysUntil(today, dueDate),
                    TargetUrl = project?.ProjectId == null ? "/PhaseAssigns/Index" : $"/PhaseAssigns/Index?projectId={project.ProjectId}"
                });
            }

            foreach (var issue in issues.Where(i => !IsIssueResolved(i)))
            {
                var startDate = issue.StartDate ?? issue.CreatedAt;
                var dueDate = issue.EndDate;
                var rowSection = PendingWorkSection(today, horizonDate, startDate, dueDate);
                if (string.IsNullOrWhiteSpace(rowSection))
                {
                    continue;
                }

                rows.Add(new PendingWorkReportRowViewModel
                {
                    Section = rowSection,
                    SectionText = PendingSectionText(rowSection),
                    Tone = IsHighPriority(issue.IssuePriority) ? "danger" : PendingTone(today, dueDate, rowSection),
                    WorkType = "ISSUE",
                    WorkTypeText = "Issue",
                    ProjectId = issue.ProjectId,
                    CoopName = issue.Project?.Coop?.CoopName ?? "",
                    ProjectName = issue.Project?.ProjectName ?? "-",
                    Title = string.IsNullOrWhiteSpace(issue.IssueName) ? $"Issue #{issue.IssueId}" : issue.IssueName,
                    Detail = CleanReportText(issue.IssueDetail),
                    OwnerEmpId = issue.AssignTo,
                    OwnerName = issue.Employee?.EmpName ?? "-",
                    BaEmpId = issue.Project?.BaEmpId,
                    BaEmpIds = issue.Project?.BusinessAnalysts.Select(e => e.EmpId).ToList() ?? new List<int>(),
                    BaName = string.IsNullOrWhiteSpace(issue.Project?.BusinessAnalystNames) ? "-" : issue.Project.BusinessAnalystNames,
                    Status = $"Issue: {TextOrDash(issue.IssueStatus)} / Dev: {TextOrDash(issue.DevStatus)} / จำนวน FAIL: {issue.ReopenCount}",
                    Priority = TextOrDash(issue.IssuePriority),
                    StartDate = startDate,
                    DueDate = dueDate,
                    OverdueDays = DaysOverdue(today, dueDate),
                    DaysUntilDue = DaysUntil(today, dueDate),
                    TargetUrl = $"/ProjectIssues/Details/{issue.IssueId}"
                });
            }

            foreach (var order in supportOrders.Where(o => !IsSupportOrderClosed(o.Status, o.DevStatus)))
            {
                var startDate = order.StartDate ?? order.CreatedAt;
                var dueDate = order.EndDate;
                var rowSection = PendingWorkSection(today, horizonDate, startDate, dueDate);
                if (string.IsNullOrWhiteSpace(rowSection))
                {
                    continue;
                }

                rows.Add(new PendingWorkReportRowViewModel
                {
                    Section = rowSection,
                    SectionText = PendingSectionText(rowSection),
                    Tone = IsHighPriority(order.Priority) ? "danger" : PendingTone(today, dueDate, rowSection),
                    WorkType = "SUPPORT",
                    WorkTypeText = "Support",
                    ProjectId = order.ProjectId,
                    CoopName = order.Project?.Coop?.CoopName ?? "",
                    ProjectName = order.Project?.ProjectName ?? "-",
                    Title = string.IsNullOrWhiteSpace(order.OrderTitle) ? $"Support #{order.OrderId}" : order.OrderTitle!,
                    Detail = CleanReportText(order.OrderDetail),
                    OwnerEmpId = order.AssignTo,
                    OwnerName = order.Employee?.EmpName ?? "-",
                    BaEmpId = order.Project?.BaEmpId,
                    BaEmpIds = order.Project?.BusinessAnalysts.Select(e => e.EmpId).ToList() ?? new List<int>(),
                    BaName = string.IsNullOrWhiteSpace(order.Project?.BusinessAnalystNames) ? "-" : order.Project.BusinessAnalystNames,
                    Status = $"Status: {TextOrDash(order.Status)} / Dev: {TextOrDash(order.DevStatus)} / จำนวน FAIL: {order.ReopenCount}",
                    Priority = TextOrDash(order.Priority),
                    StartDate = startDate,
                    DueDate = dueDate,
                    OverdueDays = DaysOverdue(today, dueDate),
                    DaysUntilDue = DaysUntil(today, dueDate),
                    TargetUrl = $"/SupportOrders/Details/{order.OrderId}"
                });
            }

            rows = rows
                .Where(r => !projectId.HasValue || r.ProjectId == projectId.Value)
                .Where(r => !empId.HasValue || r.OwnerEmpId == empId.Value)
                .Where(r => !baEmpId.HasValue || r.BaEmpIds.Contains(baEmpId.Value))
                .Where(r => string.IsNullOrWhiteSpace(selectedWorkType) || r.WorkType == selectedWorkType)
                .Where(r => string.IsNullOrWhiteSpace(selectedSection) || r.Section == selectedSection)
                .OrderBy(r => PendingSectionOrder(r.Section))
                .ThenBy(r => r.DueDate ?? r.StartDate ?? DateTime.MaxValue)
                .ThenBy(r => r.ProjectName)
                .ThenBy(r => PendingWorkTypeOrder(r.WorkType))
                .ThenBy(r => r.WorkTypeText)
                .ThenBy(r => r.Title)
                .ToList();

            var seq = 1;
            foreach (var row in rows)
            {
                row.Seq = seq++;
            }

            return new PendingWorkReportViewModel
            {
                DepartmentId = departmentId,
                DepartmentOptions = departmentOptions,
                GeneratedAt = DateTime.Now,
                GeneratedBy = username,
                Today = today,
                HorizonDate = horizonDate,
                ProjectId = projectId,
                EmpId = empId,
                BaEmpId = baEmpId,
                WorkType = selectedWorkType,
                Section = selectedSection,
                ProjectOptions = projectOptions,
                EmployeeOptions = employees,
                BaOptions = baOptions,
                Summary = new PendingWorkSummaryViewModel
                {
                    Total = rows.Count,
                    Overdue = rows.Count(r => r.Section == "OVERDUE"),
                    Upcoming = rows.Count(r => r.Section == "UPCOMING"),
                    Projects = rows.Where(r => r.ProjectId.HasValue).Select(r => r.ProjectId!.Value).Distinct().Count(),
                    Owners = rows.Where(r => r.OwnerEmpId.HasValue).Select(r => r.OwnerEmpId!.Value).Distinct().Count(),
                    Assigns = rows.Count(r => r.WorkType == "ASSIGN"),
                    Issues = rows.Count(r => r.WorkType == "ISSUE"),
                    SupportOrders = rows.Count(r => r.WorkType == "SUPPORT")
                },
                Rows = rows
            };
        }

        private static string PendingWorkSection(DateTime today, DateTime horizonDate, DateTime? startDate, DateTime? dueDate)
        {
            if (dueDate.HasValue && dueDate.Value.Date < today.Date)
            {
                return "OVERDUE";
            }

            var start = (startDate ?? dueDate)?.Date;
            var end = (dueDate ?? startDate)?.Date;

            if (!start.HasValue || !end.HasValue)
            {
                return "";
            }

            return start.Value <= horizonDate.Date && end.Value >= today.Date
                ? "UPCOMING"
                : "";
        }

        private static string PendingSectionText(string section)
        {
            return Norm(section) switch
            {
                "OVERDUE" => "งานค้าง",
                "UPCOMING" => "งานที่จะทำใน 2 สัปดาห์",
                _ => "-"
            };
        }

        private static int PendingSectionOrder(string section)
        {
            return Norm(section) switch
            {
                "OVERDUE" => 0,
                "UPCOMING" => 1,
                _ => 9
            };
        }

        private static int PendingWorkTypeOrder(string workType)
        {
            return Norm(workType) switch
            {
                "ASSIGN" => 0,
                "ISSUE" => 1,
                "SUPPORT" => 2,
                _ => 9
            };
        }

        private static string PendingTone(DateTime today, DateTime? dueDate, string section)
        {
            if (Norm(section) == "OVERDUE")
            {
                return "danger";
            }

            if (dueDate.HasValue && dueDate.Value.Date <= today.Date.AddDays(3))
            {
                return "warning";
            }

            return "info";
        }

        private static int DaysUntil(DateTime today, DateTime? dueDate)
        {
            return dueDate.HasValue
                ? (dueDate.Value.Date - today.Date).Days
                : 0;
        }

        private static string TextOrDash(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static string CleanReportText(string? value, int maxLength = 140)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            var cleaned = string.Join(" ", value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

            return cleaned.Length <= maxLength
                ? cleaned
                : cleaned[..maxLength].TrimEnd() + "...";
        }

        private async Task<WorkDurationReportViewModel> BuildWorkDurationReportAsync(
            int? requestedDepartmentId,
            int? requestedEmpId,
            DateTime? requestedStartDate,
            DateTime? requestedEndDate)
        {
            var departmentOptions = await GetDepartmentOptionsAsync();
            var assignStatusDefinitions = await _context.PhaseAssignStatuses
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.StatusId)
                .Select(x => new StatusDefinitionOption { StatusId = x.StatusId, StatusCode = x.StatusCode, StatusDesc = x.StatusDesc, SortOrder = x.SortOrder })
                .ToListAsync();
            var hasDepartmentQuery = HttpContext.Request.Query.ContainsKey("departmentId");
            var showAllDepartments = hasDepartmentQuery && requestedDepartmentId == 0;
            var departmentId = showAllDepartments
                ? null
                : ValidateDepartmentId(requestedDepartmentId, departmentOptions);

            if (!hasDepartmentQuery)
            {
                departmentId = await ResolveCurrentUserDepartmentIdAsync(departmentOptions);
            }

            var today = DateTime.Today;
            var startDate = requestedStartDate?.Date ?? new DateTime(today.Year, 1, 1);
            var endDate = requestedEndDate?.Date ?? new DateTime(today.Year, 12, 31);
            if (startDate > endDate)
            {
                (startDate, endDate) = (endDate, startDate);
            }

            var employeeQuery = _context.Employees
                .Include(row => row.Department)
                .AsNoTracking()
                .Where(row => row.Status == "ACTIVE");
            if (departmentId.HasValue)
            {
                employeeQuery = employeeQuery.Where(row => row.DepartmentId == departmentId.Value);
            }

            var employees = await employeeQuery
                .OrderBy(row => row.EmpName)
                .ToListAsync();
            var validEmployeeIds = employees.Select(row => row.EmpId).ToHashSet();
            var empId = requestedEmpId.HasValue && validEmployeeIds.Contains(requestedEmpId.Value)
                ? requestedEmpId
                : null;

            var employeeIds = empId.HasValue
                ? new HashSet<int> { empId.Value }
                : validEmployeeIds;

            var assignments = employeeIds.Count == 0
                ? new List<PhaseAssign>()
                : await _context.PhaseAssigns
                    .Include(row => row.StatusDefinition)
                    .AsNoTracking()
                    .Where(row => employeeIds.Contains(row.EmpId))
                    .Where(row =>
                        (row.PlanStart ?? row.ActualStart ?? row.CreatedAt) <= endDate
                        && (row.PlanEnd ?? row.ActualEnd ?? row.PlanStart ?? row.ActualStart ?? row.CreatedAt) >= startDate)
                    .ToListAsync();

            var phaseIds = assignments.Select(row => row.PhaseId).Distinct().ToList();
            var phases = phaseIds.Count == 0
                ? new List<ProjectPhase>()
                : await _context.ProjectPhases
                    .AsNoTracking()
                    .Where(row => phaseIds.Contains(row.PhaseId))
                    .ToListAsync();
            var phaseMap = phases.ToDictionary(row => row.PhaseId);

            var projectIds = phases.Select(row => row.ProjectId).Distinct().ToList();
            var projects = projectIds.Count == 0
                ? new List<Project>()
                : await _context.Projects
                    .Include(row => row.Coop)
                    .AsNoTracking()
                    .Where(row => projectIds.Contains(row.ProjectId))
                    .ToListAsync();
            var projectMap = projects.ToDictionary(row => row.ProjectId);
            var employeeMap = employees.ToDictionary(row => row.EmpId);

            var tasks = assignments
                .Select(assign =>
                {
                    employeeMap.TryGetValue(assign.EmpId, out var employee);
                    phaseMap.TryGetValue(assign.PhaseId, out var phase);
                    var projectId = phase?.ProjectId ?? 0;
                    projectMap.TryGetValue(projectId, out var project);

                    var normalizedWorkStatus = Norm(assign.WorkStatus);
                    var completed = StatusApprovalService.IsPhaseAssignCompletionStatus(assign.WorkStatus);
                    var planned = normalizedWorkStatus is "PLAN" or "PLANNED" or "PENDING" or "TO DO" or "TODO" or "วางแผน";
                    var overdue = !completed && assign.PlanEnd?.Date < today;
                    var statusCode = completed ? "DONE" : overdue ? "OVERDUE" : planned ? "PLANNED" : "IN_PROGRESS";
                    var actualStart = assign.ActualStart?.Date ?? assign.PlanStart?.Date;
                    var actualEnd = assign.ActualEnd?.Date;
                    var effectiveActualEnd = completed ? actualEnd : today;
                    var planDays = InclusiveDays(assign.PlanStart, assign.PlanEnd);
                    var actualDays = actualStart.HasValue && effectiveActualEnd.HasValue && effectiveActualEnd.Value >= actualStart.Value
                        ? InclusiveDays(actualStart, effectiveActualEnd)
                        : 0;
                    var isCompletedLate = completed
                        && actualEnd.HasValue
                        && assign.PlanEnd.HasValue
                        && actualEnd.Value > assign.PlanEnd.Value.Date;
                    var scheduleText = completed
                        ? !actualEnd.HasValue
                            ? "ไม่มีวันที่เสร็จจริง"
                            : !assign.PlanEnd.HasValue
                                ? "ไม่มีวันสิ้นสุดตามแผน"
                                : isCompletedLate
                                    ? $"เสร็จล่าช้า {(actualEnd.Value - assign.PlanEnd.Value.Date).Days} วัน"
                                    : "เสร็จทันแผน"
                        : overdue
                            ? $"ล่าช้า {(today - assign.PlanEnd!.Value.Date).Days} วัน"
                            : planned
                                ? "รอเริ่มตามแผน"
                                : "อยู่ในแผน";

                    return new WorkDurationTaskViewModel
                    {
                        AssignId = assign.AssignId,
                        ProjectId = projectId,
                        EmpId = assign.EmpId,
                        EmployeeName = employee?.EmpName ?? "-",
                        Position = employee?.Position?.Trim() ?? "-",
                        DepartmentName = employee?.Department?.DepartmentName?.Trim() ?? "-",
                        ProjectName = project?.ProjectDisplayName ?? "-",
                        WorkName = string.IsNullOrWhiteSpace(assign.Role) ? phase?.PhaseName ?? "-" : assign.Role.Trim(),
                        PlanStart = assign.PlanStart?.Date,
                        PlanEnd = assign.PlanEnd?.Date,
                        ActualStart = actualStart,
                        ActualEnd = actualEnd,
                        PlanDays = planDays,
                        ActualDays = actualDays,
                        VarianceDays = actualDays > 0 && planDays > 0 ? actualDays - planDays : 0,
                        WorkflowStatusCode = assign.StatusDefinition?.StatusCode ?? Norm(assign.WorkStatus),
                        StatusCode = statusCode,
                        StatusText = overdue
                            ? "ล่าช้า"
                            : assign.StatusDefinition?.StatusDesc
                                ?? WorkflowStatusPresentation.Description(assignStatusDefinitions, assign.WorkStatus),
                        StatusTone = statusCode switch
                        {
                            "DONE" => "success",
                            "OVERDUE" => "danger",
                            "PLANNED" => "muted",
                            _ => "warning"
                        },
                        ScheduleText = scheduleText,
                        ScheduleTone = isCompletedLate || overdue
                            ? "danger"
                            : completed
                                ? "success"
                                : planned
                                    ? "muted"
                                    : "info",
                        IsCompleted = completed,
                        IsOverdue = overdue,
                        IsCompletedLate = isCompletedLate
                    };
                })
                .OrderBy(row => row.EmployeeName)
                .ThenBy(row => row.PlanStart ?? DateTime.MaxValue)
                .ThenBy(row => row.AssignId)
                .ToList();

            var employeeRows = tasks
                .GroupBy(row => new { row.EmpId, row.EmployeeName, row.Position, row.DepartmentName })
                .Select(group => new WorkDurationEmployeeViewModel
                {
                    EmpId = group.Key.EmpId,
                    EmployeeName = group.Key.EmployeeName,
                    Position = group.Key.Position,
                    DepartmentName = group.Key.DepartmentName,
                    Total = group.Count(),
                    Completed = group.Count(row => row.StatusCode == "DONE"),
                    InProgress = group.Count(row => row.StatusCode == "IN_PROGRESS"),
                    Planned = group.Count(row => row.StatusCode == "PLANNED"),
                    Overdue = group.Count(row => row.StatusCode == "OVERDUE"),
                    PlanDays = group.Sum(row => row.PlanDays),
                    ActualDays = group.Sum(row => row.ActualDays),
                    VarianceDays = group.Sum(row => row.VarianceDays),
                    CompletionPercent = group.Any()
                        ? (int)Math.Round(group.Count(row => row.IsCompleted) * 100d / group.Count())
                        : 0
                })
                .OrderByDescending(row => row.Overdue)
                .ThenByDescending(row => row.Total)
                .ThenBy(row => row.EmployeeName)
                .ToList();

            var completedCount = tasks.Count(row => row.StatusCode == "DONE");
            var measuredPlanDays = tasks.Where(row => row.PlanDays > 0).Select(row => row.PlanDays).ToList();
            var measuredActualDays = tasks.Where(row => row.ActualDays > 0).Select(row => row.ActualDays).ToList();

            return new WorkDurationReportViewModel
            {
                DepartmentFilterValue = showAllDepartments ? 0 : departmentId ?? 0,
                DepartmentId = departmentId,
                DepartmentName = departmentId.HasValue
                    ? departmentOptions.FirstOrDefault(row => row.DepartmentId == departmentId.Value)?.DepartmentName ?? "ทุกฝ่าย"
                    : "ทุกฝ่าย",
                EmpId = empId,
                StartDate = startDate,
                EndDate = endDate,
                GeneratedAt = DateTime.Now,
                GeneratedBy = HttpContext.Session.GetString("Username") ?? "-",
                DepartmentOptions = departmentOptions,
                EmployeeOptions = employees.Select(row => new EmployeeReportOptionViewModel
                {
                    EmpId = row.EmpId,
                    EmpName = row.EmpName
                }).ToList(),
                AssignStatusDefinitions = assignStatusDefinitions,
                Summary = new WorkDurationSummaryViewModel
                {
                    TotalProjects = tasks
                        .Where(row => row.ProjectId > 0)
                        .Select(row => row.ProjectId)
                        .Distinct()
                        .Count(),
                    Total = tasks.Count,
                    Completed = completedCount,
                    InProgress = tasks.Count(row => row.StatusCode == "IN_PROGRESS"),
                    Planned = tasks.Count(row => row.StatusCode == "PLANNED"),
                    Overdue = tasks.Count(row => row.StatusCode == "OVERDUE"),
                    CompletionPercent = tasks.Count == 0 ? 0 : (int)Math.Round(completedCount * 100d / tasks.Count),
                    AveragePlanDays = measuredPlanDays.Count == 0 ? 0 : Math.Round((decimal)measuredPlanDays.Average(), 1),
                    AverageActualDays = measuredActualDays.Count == 0 ? 0 : Math.Round((decimal)measuredActualDays.Average(), 1),
                    TotalVarianceDays = tasks.Sum(row => row.VarianceDays)
                },
                Employees = employeeRows,
                Tasks = tasks
            };
        }

        private static int InclusiveDays(DateTime? startDate, DateTime? endDate)
        {
            if (!startDate.HasValue || !endDate.HasValue || endDate.Value.Date < startDate.Value.Date)
            {
                return 0;
            }

            return (endDate.Value.Date - startDate.Value.Date).Days + 1;
        }

        private async Task<ExecutiveReportViewModel> BuildExecutiveReportAsync(int? departmentId)
        {
            var departmentOptions = await GetDepartmentOptionsAsync();
            departmentId = ValidateDepartmentId(departmentId, departmentOptions);
            var today = DateTime.Today;
            var next14Days = today.AddDays(14);
            var username = HttpContext.Session.GetString("Username") ?? "-";

            var projectQuery = _context.Projects
                .Include(p => p.BA)
                .Include(p => p.TeamMembers)
                    .ThenInclude(m => m.Employee)
                .Include(p => p.Coop)
                .AsNoTracking().AsQueryable();
            if (departmentId.HasValue)
                projectQuery = projectQuery.Where(p => p.DepartmentId == departmentId.Value);
            var projects = await projectQuery
                .ToListAsync();
            var projectIds = projects.Select(p => p.ProjectId).ToHashSet();

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => projectIds.Contains(p.ProjectId)).ToListAsync();

            var assigns = await _context.PhaseAssigns
                .Include(a => a.Employee)
                .AsNoTracking()
                .Where(a => a.Phase != null && projectIds.Contains(a.Phase.ProjectId)).ToListAsync();

            var issues = await _context.ProjectIssues
                .Include(i => i.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(i => i.Employee)
                .AsNoTracking()
                .Where(i => projectIds.Contains(i.ProjectId)).ToListAsync();

            var supportOrders = await _context.ProjectSupportOrders
                .Include(o => o.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(o => o.Employee)
                .AsNoTracking()
                .Where(o => projectIds.Contains(o.ProjectId)).ToListAsync();

            var followups = await _context.ProjectFollowups
                .Include(f => f.Project)
                    .ThenInclude(p => p!.Coop)
                .Include(f => f.Owner)
                .AsNoTracking()
                .Where(f => f.ProjectId.HasValue && projectIds.Contains(f.ProjectId.Value)).ToListAsync();

            var employees = await _context.Employees
                .AsNoTracking()
                .ToDictionaryAsync(e => e.EmpId, e => e.EmpName ?? "-");

            var phasesByProject = phases
                .GroupBy(p => p.ProjectId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var phaseProjectMap = phases
                .GroupBy(p => p.PhaseId)
                .ToDictionary(g => g.Key, g => g.First().ProjectId);

            var assignsByProject = assigns
                .Select(a => new
                {
                    Assign = a,
                    ProjectId = phaseProjectMap.TryGetValue(a.PhaseId, out var projectId) ? projectId : 0
                })
                .Where(x => x.ProjectId > 0)
                .GroupBy(x => x.ProjectId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Assign).ToList());

            var issuesByProject = issues
                .GroupBy(i => i.ProjectId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var supportByProject = supportOrders
                .GroupBy(o => o.ProjectId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var followupsByProject = followups
                .Where(f => f.ProjectId.HasValue)
                .GroupBy(f => f.ProjectId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var openIssues = issues.Where(i => !IsIssueResolved(i)).ToList();
            var urgentOpenIssues = openIssues.Where(i => IsHighPriority(i.IssuePriority)).ToList();
            var openSupportOrders = supportOrders.Where(o => !IsSupportOrderClosed(o.Status, o.DevStatus)).ToList();
            var overdueSupportOrders = openSupportOrders.Where(o => o.EndDate?.Date < today).ToList();
            var overdueFollowups = followups
                .Where(f => f.NextFollowupDate?.Date < today && Norm(f.Status) != "DONE")
                .ToList();
            var overduePhases = phases
                .Where(p => p.PlanEnd?.Date < today && !IsPhaseDone(p.PhaseStatus))
                .ToList();
            var overdueAssigns = assigns
                .Where(a => a.PlanEnd?.Date < today && Norm(a.WorkStatus) != "DONE")
                .ToList();
            var nearingPhases = phases
                .Where(p => p.PlanEnd?.Date >= today && p.PlanEnd?.Date <= next14Days && !IsPhaseDone(p.PhaseStatus))
                .ToList();

            var riskProjects = projects
                .Select(project =>
                {
                    phasesByProject.TryGetValue(project.ProjectId, out var projectPhases);
                    assignsByProject.TryGetValue(project.ProjectId, out var projectAssigns);
                    issuesByProject.TryGetValue(project.ProjectId, out var projectIssues);
                    supportByProject.TryGetValue(project.ProjectId, out var projectSupportOrders);
                    followupsByProject.TryGetValue(project.ProjectId, out var projectFollowups);

                    projectPhases ??= new List<ProjectPhase>();
                    projectAssigns ??= new List<PhaseAssign>();
                    projectIssues ??= new List<ProjectIssue>();
                    projectSupportOrders ??= new List<ProjectSupportOrder>();
                    projectFollowups ??= new List<ProjectFollowup>();

                    var projectOpenIssues = projectIssues.Count(i => !IsIssueResolved(i));
                    var projectUrgentIssues = projectIssues.Count(i => !IsIssueResolved(i) && IsHighPriority(i.IssuePriority));
                    var projectOverduePhases = projectPhases.Count(p => p.PlanEnd?.Date < today && !IsPhaseDone(p.PhaseStatus));
                    var projectNearingPhases = projectPhases.Count(p => p.PlanEnd?.Date >= today && p.PlanEnd?.Date <= next14Days && !IsPhaseDone(p.PhaseStatus));
                    var projectOverdueAssigns = projectAssigns.Count(a => a.PlanEnd?.Date < today && Norm(a.WorkStatus) != "DONE");
                    var projectOpenSupport = projectSupportOrders.Count(o => !IsSupportOrderClosed(o.Status, o.DevStatus));
                    var projectOverdueSupport = projectSupportOrders.Count(o => !IsSupportOrderClosed(o.Status, o.DevStatus) && o.EndDate?.Date < today);
                    var projectOverdueFollowups = projectFollowups.Count(f => f.NextFollowupDate?.Date < today && Norm(f.Status) != "DONE");
                    var projectOverdue = project.EndDate?.Date < today && !IsProjectDone(project.Status);

                    var score = 0;
                    var reasons = new List<string>();

                    if (projectOverdue)
                    {
                        score += 6;
                        reasons.Add("โครงการเลยกำหนด");
                    }

                    if (projectOverduePhases > 0)
                    {
                        score += projectOverduePhases * 3;
                        reasons.Add($"งวดล่าช้า {projectOverduePhases}");
                    }

                    if (projectOverdueAssigns > 0)
                    {
                        score += projectOverdueAssigns * 2;
                        reasons.Add($"งานค้าง {projectOverdueAssigns}");
                    }

                    if (projectUrgentIssues > 0)
                    {
                        score += projectUrgentIssues * 4;
                        reasons.Add($"Issue ด่วน {projectUrgentIssues}");
                    }

                    if (projectOpenIssues > 0)
                    {
                        score += projectOpenIssues;
                        reasons.Add($"Issue เปิด {projectOpenIssues}");
                    }

                    if (projectOverdueSupport > 0)
                    {
                        score += projectOverdueSupport * 3;
                        reasons.Add($"Support เลยกำหนด {projectOverdueSupport}");
                    }

                    if (projectOpenSupport > 0)
                    {
                        score += projectOpenSupport;
                        reasons.Add($"Support ค้าง {projectOpenSupport}");
                    }

                    if (projectOverdueFollowups > 0)
                    {
                        score += projectOverdueFollowups * 2;
                        reasons.Add($"Followup เลยกำหนด {projectOverdueFollowups}");
                    }

                    if (projectNearingPhases > 0)
                    {
                        score += projectNearingPhases;
                        reasons.Add($"ใกล้ครบกำหนด {projectNearingPhases}");
                    }

                    if (score == 0)
                    {
                        return null;
                    }

                    var progress = CalculateProjectProgress(project.Status, projectPhases, projectAssigns);
                    var riskLevel = score >= 12 ? "สูง" : score >= 6 ? "กลาง" : "เฝ้าระวัง";
                    var riskTone = score >= 12 ? "danger" : score >= 6 ? "warning" : "info";
                    var ownerName = !string.IsNullOrWhiteSpace(project.BusinessAnalystNames)
                        ? project.BusinessAnalystNames
                        : projectAssigns
                            .GroupBy(a => a.EmpId)
                            .OrderByDescending(g => g.Count())
                            .Select(g => EmployeeName(employees, g.Key))
                            .FirstOrDefault()
                            ?? "-";

                    return new ExecutiveRiskProjectViewModel
                    {
                        ProjectId = project.ProjectId,
                        ProjectName = project.ProjectDisplayName,
                        OwnerName = ownerName,
                        Progress = progress,
                        RiskLevel = riskLevel,
                        RiskTone = riskTone,
                        RiskScore = score,
                        DueText = BuildDueText(project, projectPhases, projectAssigns, projectFollowups, projectSupportOrders, today),
                        OpenIssues = projectOpenIssues,
                        UrgentIssues = projectUrgentIssues,
                        OverduePhases = projectOverduePhases,
                        OverdueAssigns = projectOverdueAssigns,
                        OpenSupportOrders = projectOpenSupport,
                        OverdueFollowups = projectOverdueFollowups,
                        Reasons = reasons.Distinct().Take(5).ToList()
                    };
                })
                .Where(x => x != null)
                .OrderByDescending(x => x!.RiskScore)
                .ThenBy(x => x!.Progress)
                .ThenBy(x => x!.ProjectName)
                .Take(8)
                .Select(x => x!)
                .ToList();

            var dueItems = BuildDueItems(today, overduePhases, overdueAssigns, overdueFollowups, overdueSupportOrders, phaseProjectMap, projects, employees)
                .Take(12)
                .ToList();

            var agingItems = BuildAgingItems(today, openIssues, openSupportOrders)
                .Take(12)
                .ToList();

            var teamWorkload = BuildTeamWorkload(assigns, openIssues, openSupportOrders, followups, employees);

            return new ExecutiveReportViewModel
            {
                DepartmentId = departmentId,
                DepartmentOptions = departmentOptions,
                GeneratedAt = DateTime.Now,
                GeneratedBy = username,
                Kpis = new List<ExecutiveKpiViewModel>
                {
                    new() { Label = "Projects", Value = projects.Count.ToString("N0"), Note = $"กำลังทำ {projects.Count(p => Norm(p.Status) == "IN_PROGRESS"):N0} / เสร็จ {projects.Count(p => IsProjectDone(p.Status)):N0}", Tone = "blue" },
                    new() { Label = "Risk Projects", Value = riskProjects.Count.ToString("N0"), Note = "โครงการที่มีงานค้างหรือความเสี่ยง", Tone = "pink" },
                    new() { Label = "Overdue Work", Value = (overduePhases.Count + overdueAssigns.Count + overdueFollowups.Count + overdueSupportOrders.Count).ToString("N0"), Note = "งวด งาน Followup และ Support เลยกำหนด", Tone = "orange" },
                    new() { Label = "Open Issues", Value = openIssues.Count.ToString("N0"), Note = $"ด่วน {urgentOpenIssues.Count:N0} รายการ", Tone = "red" },
                    new() { Label = "Open Support", Value = openSupportOrders.Count.ToString("N0"), Note = $"เลยกำหนด {overdueSupportOrders.Count:N0} รายการ", Tone = "green" },
                    new() { Label = "Due Soon", Value = nearingPhases.Count.ToString("N0"), Note = "งวดใกล้ครบกำหนดใน 14 วัน", Tone = "cyan" }
                },
                RiskProjects = riskProjects,
                DueItems = dueItems,
                AgingItems = agingItems,
                TeamWorkload = teamWorkload
            };
        }

        private static List<ExecutiveDueItemViewModel> BuildDueItems(
            DateTime today,
            IReadOnlyList<ProjectPhase> overduePhases,
            IReadOnlyList<PhaseAssign> overdueAssigns,
            IReadOnlyList<ProjectFollowup> overdueFollowups,
            IReadOnlyList<ProjectSupportOrder> overdueSupportOrders,
            IReadOnlyDictionary<int, int> phaseProjectMap,
            IReadOnlyList<Project> projects,
            IReadOnlyDictionary<int, string> employees)
        {
            var projectMap = projects.ToDictionary(p => p.ProjectId, p => p.ProjectDisplayName);

            var phaseItems = overduePhases.Select(p => new ExecutiveDueItemViewModel
            {
                Type = "Phase",
                ProjectName = projectMap.TryGetValue(p.ProjectId, out var projectName) ? projectName : "-",
                Title = p.PhaseName,
                OwnerName = "-",
                DueDate = p.PlanEnd,
                OverdueDays = DaysOverdue(today, p.PlanEnd),
                Status = p.PhaseStatus ?? "-",
                Tone = "orange"
            });

            var assignItems = overdueAssigns.Select(a =>
            {
                var projectId = phaseProjectMap.TryGetValue(a.PhaseId, out var pid) ? pid : 0;
                return new ExecutiveDueItemViewModel
                {
                    Type = "Assign",
                    ProjectName = projectMap.TryGetValue(projectId, out var projectName) ? projectName : "-",
                    Title = string.IsNullOrWhiteSpace(a.Role) ? $"Assign #{a.AssignId}" : a.Role!,
                    OwnerName = a.Employee?.EmpName ?? EmployeeName(employees, a.EmpId),
                    DueDate = a.PlanEnd,
                    OverdueDays = DaysOverdue(today, a.PlanEnd),
                    Status = a.WorkStatus ?? "-",
                    Tone = "blue"
                };
            });

            var followupItems = overdueFollowups.Select(f => new ExecutiveDueItemViewModel
            {
                Type = "Followup",
                ProjectName = f.Project?.ProjectDisplayName ?? "-",
                Title = f.TaskTitle,
                OwnerName = f.Owner?.EmpName ?? "-",
                DueDate = f.NextFollowupDate,
                OverdueDays = DaysOverdue(today, f.NextFollowupDate),
                Status = f.Status,
                Tone = "pink"
            });

            var supportItems = overdueSupportOrders.Select(o => new ExecutiveDueItemViewModel
            {
                Type = "Support",
                ProjectName = o.Project?.ProjectDisplayName ?? "-",
                Title = string.IsNullOrWhiteSpace(o.OrderTitle) ? $"Support #{o.OrderId}" : o.OrderTitle!,
                OwnerName = o.Employee?.EmpName ?? "-",
                DueDate = o.EndDate,
                OverdueDays = DaysOverdue(today, o.EndDate),
                Status = o.Status ?? "-",
                Tone = "green"
            });

            return phaseItems
                .Concat(assignItems)
                .Concat(followupItems)
                .Concat(supportItems)
                .OrderByDescending(x => x.OverdueDays)
                .ThenBy(x => x.DueDate ?? DateTime.MaxValue)
                .ToList();
        }

        private static List<ExecutiveAgingItemViewModel> BuildAgingItems(
            DateTime today,
            IReadOnlyList<ProjectIssue> openIssues,
            IReadOnlyList<ProjectSupportOrder> openSupportOrders)
        {
            var issueItems = openIssues.Select(i => new ExecutiveAgingItemViewModel
            {
                Type = "Issue",
                ProjectName = i.Project?.ProjectDisplayName ?? "-",
                Title = i.IssueName,
                OwnerName = i.Employee?.EmpName ?? "-",
                Priority = i.IssuePriority,
                Status = i.IssueStatus,
                AgeDays = Math.Max(0, (today - i.CreatedAt.Date).Days),
                Tone = IsHighPriority(i.IssuePriority) ? "danger" : "orange"
            });

            var supportItems = openSupportOrders.Select(o => new ExecutiveAgingItemViewModel
            {
                Type = "Support",
                ProjectName = o.Project?.ProjectDisplayName ?? "-",
                Title = string.IsNullOrWhiteSpace(o.OrderTitle) ? $"Support #{o.OrderId}" : o.OrderTitle!,
                OwnerName = o.Employee?.EmpName ?? "-",
                Priority = o.Priority ?? "-",
                Status = o.Status ?? "-",
                AgeDays = Math.Max(0, (today - (o.CreatedAt?.Date ?? today)).Days),
                Tone = IsHighPriority(o.Priority) ? "danger" : "green"
            });

            return issueItems
                .Concat(supportItems)
                .OrderByDescending(x => x.AgeDays)
                .ThenBy(x => x.ProjectName)
                .ToList();
        }

        private static List<ExecutiveWorkloadRowViewModel> BuildTeamWorkload(
            IReadOnlyList<PhaseAssign> assigns,
            IReadOnlyList<ProjectIssue> openIssues,
            IReadOnlyList<ProjectSupportOrder> openSupportOrders,
            IReadOnlyList<ProjectFollowup> followups,
            IReadOnlyDictionary<int, string> employees)
        {
            var ids = assigns.Where(a => Norm(a.WorkStatus) != "DONE").Select(a => a.EmpId)
                .Concat(openIssues.Select(i => i.AssignTo))
                .Concat(openSupportOrders.Where(o => o.AssignTo.HasValue).Select(o => o.AssignTo!.Value))
                .Concat(followups.Where(f => f.OwnerEmpId.HasValue && Norm(f.Status) != "DONE").Select(f => f.OwnerEmpId!.Value))
                .Distinct()
                .ToList();

            var rows = ids.Select(empId =>
            {
                var assignmentCount = assigns.Count(a => a.EmpId == empId && Norm(a.WorkStatus) != "DONE");
                var issueCount = openIssues.Count(i => i.AssignTo == empId);
                var supportCount = openSupportOrders.Count(o => o.AssignTo == empId);
                var followupCount = followups.Count(f => f.OwnerEmpId == empId && Norm(f.Status) != "DONE");
                var total = assignmentCount + issueCount + supportCount + followupCount;

                return new ExecutiveWorkloadRowViewModel
                {
                    EmployeeName = EmployeeName(employees, empId),
                    Assignments = assignmentCount,
                    Issues = issueCount,
                    SupportOrders = supportCount,
                    Followups = followupCount,
                    Total = total
                };
            })
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.EmployeeName)
            .Take(10)
            .ToList();

            var max = rows.Select(x => x.Total).DefaultIfEmpty(0).Max();
            foreach (var row in rows)
            {
                row.Percent = max <= 0 ? 0 : Math.Max(8, (int)Math.Round(row.Total * 100m / max));
            }

            return rows;
        }

        private static int CalculateProjectProgress(string? projectStatus, IReadOnlyList<ProjectPhase> phases, IReadOnlyList<PhaseAssign> assigns)
        {
            if (assigns.Count > 0)
            {
                return (int)Math.Round(assigns.Count(a => Norm(a.WorkStatus) == "DONE") * 100m / assigns.Count);
            }

            if (phases.Count > 0)
            {
                return (int)Math.Round(phases.Count(p => IsPhaseDone(p.PhaseStatus)) * 100m / phases.Count);
            }

            return Norm(projectStatus) switch
            {
                "DONE" => 100,
                "IN_PROGRESS" => 50,
                _ => 10
            };
        }

        private static string BuildDueText(
            Project project,
            IReadOnlyList<ProjectPhase> phases,
            IReadOnlyList<PhaseAssign> assigns,
            IReadOnlyList<ProjectFollowup> followups,
            IReadOnlyList<ProjectSupportOrder> supportOrders,
            DateTime today)
        {
            if (project.EndDate?.Date < today && !IsProjectDone(project.Status))
            {
                return $"โครงการเลยกำหนด {project.EndDate.Value:dd/MM/yyyy}";
            }

            var nearest = new List<DateTime?> { project.EndDate }
                .Concat(phases.Select(p => p.PlanEnd))
                .Concat(assigns.Select(a => a.PlanEnd))
                .Concat(followups.Select(f => f.NextFollowupDate))
                .Concat(supportOrders.Select(o => o.EndDate))
                .Where(d => d?.Date >= today)
                .Select(d => d!.Value.Date)
                .OrderBy(d => d)
                .FirstOrDefault();

            return nearest == default
                ? "ยังไม่มีกำหนดใกล้ถึง"
                : $"ครบกำหนดถัดไป {nearest:dd/MM/yyyy}";
        }

        private static string EmployeeName(IReadOnlyDictionary<int, string> employees, int? empId)
        {
            return empId.HasValue && employees.TryGetValue(empId.Value, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : "-";
        }

        private static int DaysOverdue(DateTime today, DateTime? dueDate)
        {
            return dueDate.HasValue && dueDate.Value.Date < today
                ? (today - dueDate.Value.Date).Days
                : 0;
        }

        private static bool IsProjectDone(string? status)
        {
            return Norm(status) == "DONE";
        }

        private static bool IsPhaseDone(string? status)
        {
            return Norm(status) is "DONE" or "SUBMITTED";
        }

        private static DateTime? AssignPhaseBucketDate(PhaseAssign assign, ProjectPhase phase)
        {
            return assign.PlanEnd
                ?? assign.PlanStart
                ?? PhaseBucketDate(phase);
        }

        private static DateTime? PhaseBucketDate(ProjectPhase phase)
        {
            if (IsPhaseDone(phase.PhaseStatus))
            {
                return phase.SubmittedDate
                    ?? phase.PlanEnd
                    ?? phase.PlanStart
                    ?? phase.PeriodEndDate
                    ?? phase.CreatedAt;
            }

            return phase.PlanStart
                ?? phase.PlanEnd
                ?? phase.PeriodEndDate
                ?? phase.CreatedAt;
        }

        private static bool IsIssueResolved(ProjectIssue issue)
        {
            var issueStatus = Norm(issue.IssueStatus);
            return issueStatus is "PASS" or "REJECT" or "DONE" or "CLOSED" or "CLOSE" or "RESOLVED";
        }

        private static bool IsSupportOrderClosed(string? status, string? devStatus)
        {
            return Norm(status) is "PASS" or "REJECT" or "DONE" or "CLOSED" or "CLOSE" or "RESOLVED";
        }

        private static bool IsHighPriority(string? priority)
        {
            return Norm(priority) is "HIGH" or "URGENT" or "CRITICAL";
        }

        private async Task<List<DepartmentReportOptionViewModel>> GetDepartmentOptionsAsync()
        {
            return await _context.ProjectDepartments
                .AsNoTracking()
                .Where(row => row.IsActive)
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.DepartmentName)
                .Select(row => new DepartmentReportOptionViewModel
                {
                    DepartmentId = row.DepartmentId,
                    DepartmentName = row.DepartmentName
                })
                .ToListAsync();
        }

        private async Task<int?> ResolveCurrentUserDepartmentIdAsync(
            IReadOnlyCollection<DepartmentReportOptionViewModel> options)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return null;
            }

            var empId = HttpContext.Session.GetInt32("EmpId")
                ?? await _context.LoginUsers
                    .AsNoTracking()
                    .Where(row => row.UserId == userId.Value)
                    .Select(row => row.EmpId)
                    .FirstOrDefaultAsync()
                ?? await _context.Employees
                    .AsNoTracking()
                    .Where(row => row.LoginUserId == userId.Value)
                    .Select(row => (int?)row.EmpId)
                    .FirstOrDefaultAsync();

            if (!empId.HasValue)
            {
                return null;
            }

            var departmentId = await _context.Employees
                .AsNoTracking()
                .Where(row => row.EmpId == empId.Value)
                .Select(row => row.DepartmentId)
                .FirstOrDefaultAsync();

            return ValidateDepartmentId(departmentId, options);
        }

        private static int? ValidateDepartmentId(int? departmentId, IReadOnlyCollection<DepartmentReportOptionViewModel> options)
        {
            return departmentId.HasValue && options.Any(row => row.DepartmentId == departmentId.Value)
                ? departmentId
                : null;
        }

        private static string Norm(string? value)
            => WorkflowStatusPresentation.Code(value);
    }
}
