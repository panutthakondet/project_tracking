using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Services;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class HomeController : Controller
    {
        private const string DefaultProfileImagePath = "/images/Profile/profile.png";

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public HomeController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [RequireMenu("Home.Index")]
        public async Task<IActionResult> Index(string? department, string? meetingGroup)
        {
            // ===============================
            // ส่งข้อมูลที่จำเป็นให้ View
            // ===============================
            ViewBag.Username = HttpContext.Session.GetString("Username") ?? "-";
            var themeUserId = HttpContext.Session.GetInt32("UserId");
            ViewBag.DashboardDinoName = "Dino";
            if (themeUserId.HasValue)
            {
                var dinoName = await _context.UserThemePreferences
                    .AsNoTracking()
                    .Where(x => x.UserId == themeUserId.Value)
                    .Select(x => x.DinoName)
                    .FirstOrDefaultAsync();
                ViewBag.DashboardDinoName = NormalizeDashboardDinoName(dinoName);
            }

            // ===============================
            // ⏰ เวลาเข้า-ออกวันนี้
            // ===============================
            var username = HttpContext.Session.GetString("Username");

            var today = DateTime.Today;
            // 🔍 หา emp_id จาก username
            var emp = await _context.Employees
                .FirstOrDefaultAsync(e => e.LoginUser != null && e.LoginUser.Username == username);

            var todayAttendance = await _context.Attendances
                .Where(x => x.WorkDate == today && emp != null && x.EmpId == emp.EmpId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (todayAttendance != null)
            {
                ViewBag.CheckInTime = todayAttendance.CheckinTime.HasValue
                    ? todayAttendance.CheckinTime.Value.ToString("HH:mm")
                    : "-";

                ViewBag.CheckOutTime = todayAttendance.CheckoutTime.HasValue
                    ? todayAttendance.CheckoutTime.Value.ToString("HH:mm")
                    : "-";
            }
            else
            {
                ViewBag.CheckInTime = "-";
                ViewBag.CheckOutTime = "-";
            }

            var next7days = today.AddDays(7);
            var fromDate = today.AddMonths(-1);

            // 🔴 งานเลยกำหนด
            var overdue = await (
                from f in _context.ProjectFollowups
                join p in _context.Projects on f.ProjectId equals p.ProjectId
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into empJoin
                from e in empJoin.DefaultIfEmpty()
                where f.NextFollowupDate != null
                    && f.NextFollowupDate < today
                    && f.Status != "DONE"
                orderby f.NextFollowupDate
                select new {
                    FollowupId = f.FollowupId,
                    f.TaskTitle,
                    f.PartnerName,
                    f.NextFollowupDate,
                    ProjectName = p.ProjectName,
                    OwnerName = e != null ? e.EmpName : "-"
                }
            ).Take(10).ToListAsync();

            // 🟡 งานวันนี้
            var todayList = await (
                from f in _context.ProjectFollowups
                join p in _context.Projects on f.ProjectId equals p.ProjectId
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into empJoin
                from e in empJoin.DefaultIfEmpty()
                where f.NextFollowupDate != null
                    && f.NextFollowupDate == today
                    && f.Status != "DONE"
                orderby f.NextFollowupDate
                select new {
                    FollowupId = f.FollowupId,
                    f.TaskTitle,
                    f.PartnerName,
                    f.NextFollowupDate,
                    ProjectName = p.ProjectName,
                    OwnerName = e != null ? e.EmpName : "-"
                }
            ).Take(10).ToListAsync();

            // 🟢 งานใน 7 วันข้างหน้า
            var upcoming = await (
                from f in _context.ProjectFollowups
                join p in _context.Projects on f.ProjectId equals p.ProjectId
                join e in _context.Employees on f.OwnerEmpId equals e.EmpId into empJoin
                from e in empJoin.DefaultIfEmpty()
                where f.NextFollowupDate != null
                    && f.NextFollowupDate > today
                    && f.NextFollowupDate <= next7days
                    && f.Status != "DONE"
                orderby f.NextFollowupDate
                select new {
                    FollowupId = f.FollowupId,
                    f.TaskTitle,
                    f.PartnerName,
                    f.NextFollowupDate,
                    ProjectName = p.ProjectName,
                    OwnerName = e != null ? e.EmpName : "-"
                }
            ).Take(10).ToListAsync();

            // ส่งข้อมูลไป View
            ViewBag.OverdueFollowups = overdue;
            ViewBag.TodayFollowups = todayList;
            ViewBag.UpcomingFollowups = upcoming;

            ViewBag.OverdueCount = overdue.Count;
            ViewBag.OverdueTaskCount = overdue.Count;
            ViewBag.TodayFollowupCount = todayList.Count;
            ViewBag.UpcomingCount = upcoming.Count;
            ViewBag.FollowupAlertCount = await _context.ProjectFollowups
                .Where(x => x.Status == "OPEN")
                .CountAsync();

            ViewBag.FollowupDoneCount = await _context.ProjectFollowups
                .Where(x => x.Status == "DONE")
                .CountAsync();

            ViewBag.FollowupAckCount = await _context.ProjectFollowups
                .Where(x => x.Status == "ACK" && x.LastContactDate != null && x.LastContactDate >= fromDate)
                .CountAsync();

            // 📅 จำนวนการประชุมตั้งแต่วันนี้เป็นต้นไป
            ViewBag.MeetingCount = await _context.Meetings
                .Where(x => x.MeetingDate >= today)
                .CountAsync();

            var currentUserId = HttpContext.Session.GetInt32("UserId");
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim().ToUpperInvariant();
            var isAdmin = role == "ADMIN" || role == "ADMINISTRATOR";
            var currentEmpId = emp?.EmpId;
            if (!currentEmpId.HasValue && currentUserId.HasValue)
            {
                currentEmpId = await ResolveCurrentEmployeeIdAsync(currentUserId.Value);
            }

            if (string.IsNullOrWhiteSpace(department) && currentEmpId.HasValue)
            {
                department = await ResolveDefaultDashboardDepartmentAsync(currentEmpId.Value);
            }

            var menuKeys = (HttpContext.Session.GetString("Menus") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(key => key.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var canOpenStatusApprovals = role == "ADMIN"
                || menuKeys.Contains("StatusApprovals.Index");

            var pendingStatusApprovalCount = 0;
            if (canOpenStatusApprovals)
            {
                var pendingApprovalQuery = _context.StatusApprovalRequests
                    .AsNoTracking()
                    .Where(request => request.RequestStatus == StatusApprovalService.RequestPending);

                if (role != "ADMIN")
                {
                    if (currentEmpId.HasValue)
                    {
                        var pmProjectIds = _context.Projects
                            .AsNoTracking()
                            .Where(project => project.PmEmpId == currentEmpId.Value)
                            .Select(project => (int?)project.ProjectId);

                        pendingApprovalQuery = pendingApprovalQuery
                            .Where(request => request.ProjectId.HasValue
                                && pmProjectIds.Contains(request.ProjectId));
                    }
                    else
                    {
                        pendingApprovalQuery = pendingApprovalQuery.Where(_ => false);
                    }
                }

                pendingStatusApprovalCount = await pendingApprovalQuery.CountAsync();
            }

            ViewBag.PendingStatusApprovalCount = pendingStatusApprovalCount;
            var dashboard = await BuildHomeDashboardAsync(username ?? "-", today, currentEmpId, isAdmin, department, meetingGroup);

            var unreadNotificationCount = 0;

            if (currentUserId.HasValue)
            {
                var notificationQuery = _context.UserNotifications
                    .AsNoTracking()
                    .Where(notification => !notification.IsResolved
                        && !notification.IsRead
                        && notification.SourceType != "ISSUE_DUE"
                        && notification.SourceType != "SUPPORT_DUE");

                if (!isAdmin)
                {
                    notificationQuery = notificationQuery.Where(notification => notification.RecipientUserId == currentUserId.Value);
                }

                unreadNotificationCount = isAdmin
                    ? await notificationQuery
                        .Select(notification => new { notification.SourceType, notification.SourceId })
                        .Distinct()
                        .CountAsync()
                    : await notificationQuery
                        .Select(notification => new { notification.SourceType, notification.SourceId, notification.RecipientUserId, notification.RecipientEmpId })
                        .Distinct()
                        .CountAsync();

            }

            ViewBag.UnreadNotificationCount = unreadNotificationCount;
            ViewBag.OnlineUsers = await LoadOnlineUsersAsync();

            ViewBag.TotalProjectCount = dashboard.TotalProjectCount;
            ViewBag.MeetingsTodayCount = dashboard.MeetingsTodayCount;
            ViewBag.OpenIssueCount = dashboard.OpenIssueCount;
            ViewBag.ActiveMemberCount = dashboard.ActiveMemberCount;
            ViewBag.OverdueTaskCount = dashboard.OverdueTaskCount;

            return View(dashboard);
        }

        private async Task<int?> ResolveCurrentEmployeeIdAsync(int userId)
        {
            var userEmpId = await _context.LoginUsers
                .AsNoTracking()
                .Where(user => user.UserId == userId)
                .Select(user => user.EmpId)
                .FirstOrDefaultAsync();

            if (userEmpId.HasValue)
                return userEmpId;

            return await _context.Employees
                .AsNoTracking()
                .Where(employee => employee.LoginUserId == userId)
                .Select(employee => (int?)employee.EmpId)
                .FirstOrDefaultAsync();
        }

        private async Task<string?> ResolveDefaultDashboardDepartmentAsync(int employeeId)
        {
            var employee = await _context.Employees
                .AsNoTracking()
                .Where(row => row.EmpId == employeeId)
                .Select(row => new { row.DepartmentId, row.Position })
                .FirstOrDefaultAsync();
            if (employee == null)
                return null;

            var departments = await _context.ProjectDepartments
                .AsNoTracking()
                .Where(row => row.IsActive)
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.DepartmentName)
                .Select(row => new { row.DepartmentId, row.DepartmentCode, row.DepartmentName })
                .ToListAsync();

            if (employee.DepartmentId.HasValue
                && departments.Any(row => row.DepartmentId == employee.DepartmentId.Value))
            {
                return employee.DepartmentId.Value.ToString(CultureInfo.InvariantCulture);
            }

            var normalizedPosition = NormalizeDepartmentLookupText(employee.Position);
            if (!string.IsNullOrWhiteSpace(normalizedPosition))
            {
                var positionTokens = normalizedPosition
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var matchedDepartment = departments
                    .OrderByDescending(row => NormalizeDepartmentLookupText(row.DepartmentName).Length)
                    .FirstOrDefault(row =>
                    {
                        var normalizedName = NormalizeDepartmentLookupText(row.DepartmentName);
                        var normalizedCode = NormalizeDepartmentLookupText(row.DepartmentCode);
                        if (normalizedName.Length > 2 && normalizedPosition.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (normalizedCode.Length > 2 && normalizedPosition.Contains(normalizedCode, StringComparison.OrdinalIgnoreCase))
                            return true;

                        return string.Equals(normalizedName, "IT", StringComparison.OrdinalIgnoreCase)
                            && positionTokens.Contains("IT");
                    });
                if (matchedDepartment != null)
                    return matchedDepartment.DepartmentId.ToString(CultureInfo.InvariantCulture);
            }

            var responsibleProjectDepartmentId = await _context.Projects
                .AsNoTracking()
                .Where(project => project.DepartmentId.HasValue
                    && (project.PmEmpId == employeeId || project.BaEmpId == employeeId))
                .GroupBy(project => project.DepartmentId!.Value)
                .OrderByDescending(group => group.Count())
                .Select(group => (int?)group.Key)
                .FirstOrDefaultAsync();
            if (responsibleProjectDepartmentId.HasValue
                && departments.Any(row => row.DepartmentId == responsibleProjectDepartmentId.Value))
            {
                return responsibleProjectDepartmentId.Value.ToString(CultureInfo.InvariantCulture);
            }

            var assignedProjectDepartmentId = await (
                from assign in _context.PhaseAssigns.AsNoTracking()
                join phase in _context.ProjectPhases.AsNoTracking() on assign.PhaseId equals phase.PhaseId
                join project in _context.Projects.AsNoTracking() on phase.ProjectId equals project.ProjectId
                where assign.EmpId == employeeId && project.DepartmentId.HasValue
                group project by project.DepartmentId!.Value into departmentGroup
                orderby departmentGroup.Count() descending
                select (int?)departmentGroup.Key)
                .FirstOrDefaultAsync();

            return assignedProjectDepartmentId.HasValue
                && departments.Any(row => row.DepartmentId == assignedProjectDepartmentId.Value)
                    ? assignedProjectDepartmentId.Value.ToString(CultureInfo.InvariantCulture)
                    : null;
        }

        private static string NormalizeDepartmentLookupText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(' ', value
                .Trim()
                .ToUpperInvariant()
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private async Task<List<RequirementBoardOnlineUserViewModel>> LoadOnlineUsersAsync()
        {
            var onlineCutoff = DateTime.Now.AddMinutes(-5);
            var onlineRows = await (
                from user in _context.LoginUsers.AsNoTracking()
                join employee in _context.Employees.AsNoTracking()
                    on user.UserId equals employee.LoginUserId into employeeJoin
                from employee in employeeJoin.DefaultIfEmpty()
                where user.Status == "ACTIVE"
                      && user.LastSeenAt.HasValue
                      && user.LastSeenAt.Value >= onlineCutoff
                orderby user.LastSeenAt descending
                select new
                {
                    user.UserId,
                    user.Username,
                    user.ProfileImagePath,
                    user.LastSeenAt,
                    EmployeeName = employee != null ? employee.EmpName : null
                })
                .ToListAsync();

            return onlineRows
                .GroupBy(x => x.UserId)
                .Select((group, index) =>
                {
                    var row = group.First();
                    var displayName = !string.IsNullOrWhiteSpace(row.EmployeeName)
                        ? row.EmployeeName!
                        : row.Username;

                    return new RequirementBoardOnlineUserViewModel
                    {
                        UserId = row.UserId,
                        DisplayName = displayName,
                        AvatarPath = ResolveProfileImagePath(row.ProfileImagePath),
                        ColorClass = $"c{(index % 5) + 1}",
                        LastSeenAt = row.LastSeenAt
                    };
                })
                .ToList();
        }

        [HttpGet]
        [RequireMenu("Home.Index")]
        public async Task<IActionResult> DashboardSearch(string? q)
        {
            var keyword = (q ?? string.Empty).Trim();
            if (keyword.Length < 2)
            {
                return Json(Array.Empty<DashboardSearchResult>());
            }

            if (keyword.Length > 100)
            {
                keyword = keyword[..100];
            }

            var results = new List<DashboardSearchResult>();

            var projectRows = await _context.Projects
                .AsNoTracking()
                .Where(p =>
                    p.ProjectName.Contains(keyword) ||
                    p.Status.Contains(keyword) ||
                    (p.Coop != null && p.Coop.CoopName.Contains(keyword)))
                .OrderBy(p => p.Status == "IN_PROGRESS" ? 1 : p.Status == "PLAN" ? 2 : p.Status == "DONE" ? 3 : 4)
                .ThenBy(p => p.EndDate)
                .ThenBy(p => p.ProjectName)
                .Select(p => new
                {
                    p.ProjectId,
                    p.ProjectName,
                    p.Status,
                    CoopName = p.Coop != null ? p.Coop.CoopName : null
                })
                .Take(6)
                .ToListAsync();

            results.AddRange(projectRows.Select(p => new DashboardSearchResult
            {
                Type = "Projects",
                Title = p.ProjectName,
                Detail = $"{ProjectStatusText(p.Status)}{(string.IsNullOrWhiteSpace(p.CoopName) ? string.Empty : $" · {p.CoopName}")}",
                Url = $"/Projects/Edit/{p.ProjectId}",
                Color = "blue"
            }));

            var phaseRows = await (
                from phase in _context.ProjectPhases.AsNoTracking()
                join project in _context.Projects.AsNoTracking() on phase.ProjectId equals project.ProjectId
                where phase.PhaseName.Contains(keyword) ||
                      (phase.PhaseStatus != null && phase.PhaseStatus.Contains(keyword)) ||
                      phase.PhaseType.Contains(keyword) ||
                      project.ProjectName.Contains(keyword) ||
                      (project.Coop != null && project.Coop.CoopName.Contains(keyword))
                orderby project.ProjectName, phase.PhaseOrder, phase.PeriodOrder, phase.PhaseSort
                select new
                {
                    phase.PhaseId,
                    phase.ProjectId,
                    phase.PhaseName,
                    phase.PhaseStatus,
                    phase.PhaseOrder,
                    phase.PeriodOrder,
                    project.ProjectName
                })
                .Take(6)
                .ToListAsync();

            results.AddRange(phaseRows.Select(p => new DashboardSearchResult
            {
                Type = "ProjectPhases",
                Title = p.PhaseName,
                Detail = $"{p.ProjectName} · ส่วนที่ {p.PhaseOrder} งวดที่ {p.PeriodOrder} · {p.PhaseStatus ?? "-"}",
                Url = $"/ProjectPhases?projectId={p.ProjectId}",
                Color = "purple"
            }));

            var assignRows = await (
                from assign in _context.PhaseAssigns.AsNoTracking()
                join phase in _context.ProjectPhases.AsNoTracking() on assign.PhaseId equals phase.PhaseId
                join project in _context.Projects.AsNoTracking() on phase.ProjectId equals project.ProjectId
                join emp in _context.Employees.AsNoTracking() on assign.EmpId equals emp.EmpId
                where (assign.Role != null && assign.Role.Contains(keyword)) ||
                      (assign.WorkStatus != null && assign.WorkStatus.Contains(keyword)) ||
                      emp.EmpName.Contains(keyword) ||
                      phase.PhaseName.Contains(keyword) ||
                      project.ProjectName.Contains(keyword) ||
                      (project.Coop != null && project.Coop.CoopName.Contains(keyword))
                orderby emp.EmpName, project.ProjectName, phase.PhaseOrder, phase.PeriodOrder
                select new
                {
                    assign.AssignId,
                    assign.EmpId,
                    assign.Role,
                    assign.WorkStatus,
                    phase.ProjectId,
                    phase.PhaseName,
                    phase.PhaseOrder,
                    phase.PeriodOrder,
                    project.ProjectName,
                    emp.EmpName
                })
                .Take(6)
                .ToListAsync();

            results.AddRange(assignRows.Select(a => new DashboardSearchResult
            {
                Type = "PhaseAssigns",
                Title = string.IsNullOrWhiteSpace(a.Role) ? a.PhaseName : a.Role!,
                Detail = $"{a.EmpName} · {a.ProjectName} · ส่วนที่ {a.PhaseOrder} งวดที่ {a.PeriodOrder} · {a.WorkStatus ?? "-"}",
                Url = $"/PhaseAssigns?projectId={a.ProjectId}&empId={a.EmpId}",
                Color = "green"
            }));

            var issueRows = await (
                from issue in _context.ProjectIssues.AsNoTracking()
                join project in _context.Projects.AsNoTracking() on issue.ProjectId equals project.ProjectId
                join emp in _context.Employees.AsNoTracking() on issue.AssignTo equals emp.EmpId into empJoin
                from emp in empJoin.DefaultIfEmpty()
                where issue.IssueName.Contains(keyword) ||
                      (issue.IssueDetail != null && issue.IssueDetail.Contains(keyword)) ||
                      issue.IssueStatus.Contains(keyword) ||
                      issue.DevStatus.Contains(keyword) ||
                      issue.IssuePriority.Contains(keyword) ||
                      project.ProjectName.Contains(keyword) ||
                      (project.Coop != null && project.Coop.CoopName.Contains(keyword)) ||
                      (emp != null && emp.EmpName.Contains(keyword))
                orderby issue.CreatedAt descending
                select new
                {
                    issue.IssueId,
                    issue.IssueName,
                    issue.IssueStatus,
                    issue.DevStatus,
                    issue.IssuePriority,
                    project.ProjectName,
                    OwnerName = emp != null ? emp.EmpName : "-"
                })
                .Take(6)
                .ToListAsync();

            results.AddRange(issueRows.Select(i => new DashboardSearchResult
            {
                Type = "ProjectIssues",
                Title = i.IssueName,
                Detail = $"{i.ProjectName} · เจ้าของ: {i.OwnerName} · {i.IssueStatus}/{i.DevStatus} · {i.IssuePriority}",
                Url = $"/ProjectIssues/Details/{i.IssueId}",
                Color = "pink"
            }));

            var supportRows = await (
                from support in _context.ProjectSupportOrders.AsNoTracking()
                join project in _context.Projects.AsNoTracking() on support.ProjectId equals project.ProjectId
                join emp in _context.Employees.AsNoTracking() on support.AssignTo equals emp.EmpId into empJoin
                from emp in empJoin.DefaultIfEmpty()
                where (support.OrderTitle != null && support.OrderTitle.Contains(keyword)) ||
                      (support.OrderDetail != null && support.OrderDetail.Contains(keyword)) ||
                      (support.Status != null && support.Status.Contains(keyword)) ||
                      (support.DevStatus != null && support.DevStatus.Contains(keyword)) ||
                      (support.Priority != null && support.Priority.Contains(keyword)) ||
                      project.ProjectName.Contains(keyword) ||
                      (project.Coop != null && project.Coop.CoopName.Contains(keyword)) ||
                      (emp != null && emp.EmpName.Contains(keyword))
                orderby support.CreatedAt descending
                select new
                {
                    support.OrderId,
                    support.OrderTitle,
                    support.Status,
                    support.DevStatus,
                    support.Priority,
                    project.ProjectName,
                    OwnerName = emp != null ? emp.EmpName : "-"
                })
                .Take(6)
                .ToListAsync();

            results.AddRange(supportRows.Select(s => new DashboardSearchResult
            {
                Type = "SupportOrders",
                Title = string.IsNullOrWhiteSpace(s.OrderTitle) ? $"Support #{s.OrderId}" : s.OrderTitle!,
                Detail = $"{s.ProjectName} · เจ้าของ: {s.OwnerName} · {s.Status ?? "-"}{(string.IsNullOrWhiteSpace(s.DevStatus) ? string.Empty : $"/{s.DevStatus}")} · {s.Priority ?? "-"}",
                Url = $"/SupportOrders/Details/{s.OrderId}",
                Color = "orange"
            }));

            return Json(results.Take(30));
        }

        [RequireMenu("Home.Index")]
        public async Task<IActionResult> LineOverdueOverview(string? coopName, int? projectId, int? empId, string? status)
        {
            var model = await BuildLineOverdueOverviewDetailAsync(coopName, projectId, empId, status);
            return View(model);
        }

        private async Task<HomeDashboardViewModel> BuildHomeDashboardAsync(
            string username,
            DateTime today,
            int? currentEmpId,
            bool isAdmin,
            string? requestedDepartment,
            string? requestedMeetingGroup)
        {
            var th = new CultureInfo("th-TH");
            var now = DateTime.Now;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);
            var previousMonthStart = monthStart.AddMonths(-1);
            var yearStart = new DateTime(today.Year, 1, 1);
            var attendanceRangeStart = previousMonthStart < yearStart ? previousMonthStart : yearStart;

            var projects = await _context.Projects
                .AsNoTracking()
                .Select(p => new DashboardProjectRow
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName,
                    CoopName = p.Coop != null ? p.Coop.CoopName : null,
                    DepartmentId = p.DepartmentId,
                    DepartmentName = p.Department != null ? p.Department.DepartmentName : null,
                    PmEmpId = p.PmEmpId,
                    BaEmpId = p.BaEmpId,
                    Status = p.Status,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    CreatedAt = p.CreatedAt,
                    EntryId = p.EntryId
                })
                .ToListAsync();

            var projectOverviewDepartments = await _context.ProjectDepartments
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.SortOrder)
                .ThenBy(d => d.DepartmentName)
                .Select(d => new HomeDashboardProjectDepartmentOption
                {
                    DepartmentId = d.DepartmentId,
                    DepartmentName = d.DepartmentName
                })
                .ToListAsync();

            if (projects.Any(p => !p.DepartmentId.HasValue))
            {
                projectOverviewDepartments.Add(new HomeDashboardProjectDepartmentOption
                {
                    DepartmentId = null,
                    DepartmentName = "ยังไม่กำหนดฝ่าย"
                });
            }

            var selectedDashboardDepartment = "all";
            var selectedDashboardDepartmentName = "ทุกฝ่าย";
            int? selectedDashboardDepartmentId = null;
            var includeUnassignedDepartment = string.Equals(
                requestedDepartment?.Trim(),
                "unassigned",
                StringComparison.OrdinalIgnoreCase);

            if (includeUnassignedDepartment && projects.Any(p => !p.DepartmentId.HasValue))
            {
                selectedDashboardDepartment = "unassigned";
                selectedDashboardDepartmentName = "ยังไม่กำหนดฝ่าย";
            }
            else if (int.TryParse(requestedDepartment, out var parsedDepartmentId))
            {
                var selectedOption = projectOverviewDepartments
                    .FirstOrDefault(option => option.DepartmentId == parsedDepartmentId);
                if (selectedOption != null)
                {
                    selectedDashboardDepartmentId = parsedDepartmentId;
                    selectedDashboardDepartment = parsedDepartmentId.ToString(CultureInfo.InvariantCulture);
                    selectedDashboardDepartmentName = selectedOption.DepartmentName;
                }
            }

            var scopedProjects = selectedDashboardDepartment == "all"
                ? projects
                : projects
                    .Where(project => selectedDashboardDepartment == "unassigned"
                        ? !project.DepartmentId.HasValue
                        : project.DepartmentId == selectedDashboardDepartmentId)
                    .ToList();
            var scopedProjectIds = scopedProjects.Select(project => project.ProjectId).ToHashSet();

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Select(p => new DashboardPhaseRow
                {
                    PhaseId = p.PhaseId,
                    ProjectId = p.ProjectId,
                    PhaseName = p.PhaseName,
                    PhaseType = p.PhaseType,
                    PhaseStatus = p.PhaseStatus,
                    PlanStart = p.PlanStart,
                    PlanEnd = p.PlanEnd,
                    SubmittedDate = p.SubmittedDate,
                    PeriodEndDate = p.PeriodEndDate,
                    CreatedAt = p.CreatedAt,
                    EntryId = p.EntryId
                })
                .ToListAsync();

            var assigns = await _context.PhaseAssigns
                .AsNoTracking()
                .Select(a => new DashboardAssignRow
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    WorkStatus = a.WorkStatus,
                    PlanStart = a.PlanStart,
                    PlanEnd = a.PlanEnd,
                    CreatedAt = a.CreatedAt,
                    EntryId = a.EntryId
                })
                .ToListAsync();

            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .Select(i => new DashboardIssueRow
                {
                    IssueId = i.IssueId,
                    ProjectId = i.ProjectId,
                    IssueName = i.IssueName,
                    IssueStatus = i.IssueStatus,
                    DevStatus = i.DevStatus,
                    IssuePriority = i.IssuePriority,
                    IsReopen = i.IsReopen,
                    ReopenCount = i.ReopenCount,
                    StartDate = i.StartDate,
                    EndDate = i.EndDate,
                    CreatedAt = i.CreatedAt,
                    CreatedBy = i.CreatedBy,
                    EmpId = i.AssignTo
                })
                .ToListAsync();

            var followups = await _context.ProjectFollowups
                .AsNoTracking()
                .Select(f => new DashboardFollowupRow
                {
                    FollowupId = f.FollowupId,
                    ProjectId = f.ProjectId,
                    TaskTitle = f.TaskTitle,
                    OwnerEmpId = f.OwnerEmpId,
                    CreatedByEmpId = f.CreatedByEmpId,
                    Status = f.Status,
                    NextFollowupDate = f.NextFollowupDate,
                    CreatedAt = f.CreatedAt
                })
                .ToListAsync();

            var supportOrders = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Select(o => new DashboardSupportOrderRow
                {
                    OrderId = o.OrderId,
                    ProjectId = o.ProjectId,
                    OrderTitle = o.OrderTitle,
                    Status = o.Status,
                    DevStatus = o.DevStatus,
                    Priority = o.Priority,
                    CreatedBy = o.CreatedBy,
                    AssignTo = o.AssignTo,
                    StartDate = o.StartDate,
                    EndDate = o.EndDate,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            var requirementCards = await _context.RequirementCards
                .AsNoTracking()
                .Where(c => !c.IsArchived)
                .Select(c => new DashboardRequirementCardRow
                {
                    CardId = c.CardId,
                    Title = c.Title,
                    ColumnName = c.Column != null ? c.Column.ColumnName : null,
                    CreatedByUserId = c.CreatedByUserId,
                    CreatedByUsername = c.CreatedByUser != null ? c.CreatedByUser.Username : null,
                    CreatedByUserProfileImagePath = c.CreatedByUser != null ? c.CreatedByUser.ProfileImagePath : null,
                    CreatedByEmpId = c.CreatedByEmpId,
                    CreatedByEmployeeName = c.CreatedByEmployee != null ? c.CreatedByEmployee.EmpName : null,
                    CreatedByEmployeeProfileImagePath = c.CreatedByEmployee != null && c.CreatedByEmployee.LoginUser != null
                        ? c.CreatedByEmployee.LoginUser.ProfileImagePath
                        : null,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync();

            var employees = await _context.Employees
                .AsNoTracking()
                .Select(e => new DashboardEmployeeRow
                {
                    EmpId = e.EmpId,
                    EmpName = e.EmpName,
                    Status = e.Status,
                    LoginUserId = e.LoginUserId,
                    ProfileImagePath = e.LoginUser != null ? e.LoginUser.ProfileImagePath : null
                })
                .ToListAsync();
            await FillMissingEmployeeProfileImagesAsync(employees);

            var meetingCalendarGroups = await _context.MeetingGroups
                .AsNoTracking()
                .Where(group => group.IsActive)
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.GroupName)
                .Select(group => new HomeDashboardMeetingGroupOption
                {
                    GroupId = group.GroupId,
                    GroupName = group.GroupName
                })
                .ToListAsync();
            var selectedMeetingGroup = "all";
            int? selectedMeetingGroupId = null;
            if (int.TryParse(requestedMeetingGroup, out var parsedMeetingGroupId)
                && meetingCalendarGroups.Any(group => group.GroupId == parsedMeetingGroupId))
            {
                selectedMeetingGroupId = parsedMeetingGroupId;
                selectedMeetingGroup = parsedMeetingGroupId.ToString(CultureInfo.InvariantCulture);
            }

            var todayMeetings = await (
                from m in _context.Meetings.AsNoTracking()
                join p in _context.Projects.AsNoTracking()
                    on m.ProjectId equals p.ProjectId into projectJoin
                from p in projectJoin.DefaultIfEmpty()
                where m.MeetingDate == today
                orderby m.StartTime
                select new DashboardMeetingRow
                {
                    Id = m.Id,
                    ProjectId = m.ProjectId,
                    GroupId = m.Calendar != null ? m.Calendar.GroupId : null,
                    GroupName = m.Calendar != null && m.Calendar.Group != null ? m.Calendar.Group.GroupName : null,
                    Title = m.Title,
                    StartTime = m.StartTime,
                    Location = m.Location,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,
                    CreatedBy = m.CreatedBy,
                    ProjectName = p != null ? p.ProjectName : null
                }
            ).ToListAsync();

            var recentMeetings = await (
                from m in _context.Meetings.AsNoTracking()
                join p in _context.Projects.AsNoTracking()
                    on m.ProjectId equals p.ProjectId into projectJoin
                from p in projectJoin.DefaultIfEmpty()
                orderby m.CreatedAt descending
                select new DashboardMeetingRow
                {
                    Id = m.Id,
                    ProjectId = m.ProjectId,
                    GroupId = m.Calendar != null ? m.Calendar.GroupId : null,
                    GroupName = m.Calendar != null && m.Calendar.Group != null ? m.Calendar.Group.GroupName : null,
                    Title = m.Title,
                    StartTime = m.StartTime,
                    Location = m.Location,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,
                    CreatedBy = m.CreatedBy,
                    ProjectName = p != null ? p.ProjectName : null
                }
            )
            .Take(50)
            .ToListAsync();

            var fieldServiceQuery = _context.FieldServiceVisits
                .AsNoTracking()
                .Include(x => x.Coop)
                .Include(x => x.Assignees).ThenInclude(x => x.Employee)
                .AsQueryable();
            if (!isAdmin)
            {
                fieldServiceQuery = currentEmpId.HasValue
                    ? fieldServiceQuery.Where(x => x.Assignees.Any(a => a.EmpId == currentEmpId.Value))
                    : fieldServiceQuery.Where(x => false);
            }
            var fieldServiceVisits = await fieldServiceQuery.ToListAsync();
            static string FieldServiceStatusText(string? status) => (status ?? "").ToUpperInvariant() switch
            {
                "PLANNED" => "วางแผนแล้ว",
                "IN_PROGRESS" => "กำลังดำเนินการ",
                "COMPLETED" => "เสร็จสิ้น",
                "CANCELLED" => "ยกเลิก",
                _ => string.IsNullOrWhiteSpace(status) ? "-" : status
            };
            static string FieldServiceStatusColor(string? status) => (status ?? "").ToUpperInvariant() switch
            {
                "COMPLETED" => "green",
                "IN_PROGRESS" => "orange",
                "CANCELLED" => "muted",
                _ => "blue"
            };
            string FieldServiceDateText(FieldServiceVisit visit)
            {
                var endDate = (visit.EndVisitDate ?? visit.VisitDate).Date;
                var startText = $"{visit.VisitDate.ToString("dd MMM", th)} {visit.VisitDate.Year + 543}";
                return endDate == visit.VisitDate.Date
                    ? startText
                    : $"{startText} - {endDate.ToString("dd MMM", th)} {endDate.Year + 543}";
            }
            var currentAndPreviousMonthAttendance = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.WorkDate >= attendanceRangeStart && a.WorkDate < nextMonthStart)
                .Select(a => new DashboardAttendanceRow
                {
                    EmpId = a.EmpId,
                    WorkDate = a.WorkDate,
                    CheckinTime = a.CheckinTime,
                    CheckoutTime = a.CheckoutTime,
                    DistanceKm = a.DistanceKm ?? 0m
                })
                .ToListAsync();
            var attendancePolicy = await GetAttendancePolicyAsync();

            var scopedPhases = selectedDashboardDepartment == "all"
                ? phases
                : phases.Where(phase => scopedProjectIds.Contains(phase.ProjectId)).ToList();
            var scopedPhaseIds = scopedPhases.Select(phase => phase.PhaseId).ToHashSet();
            var scopedAssigns = selectedDashboardDepartment == "all"
                ? assigns
                : assigns.Where(assign => scopedPhaseIds.Contains(assign.PhaseId)).ToList();
            var scopedIssues = selectedDashboardDepartment == "all"
                ? issues
                : issues.Where(issue => scopedProjectIds.Contains(issue.ProjectId)).ToList();
            var scopedSupportOrders = selectedDashboardDepartment == "all"
                ? supportOrders
                : supportOrders.Where(order => scopedProjectIds.Contains(order.ProjectId)).ToList();
            var scopedFollowups = selectedDashboardDepartment == "all"
                ? followups
                : followups
                    .Where(followup => followup.ProjectId.HasValue && scopedProjectIds.Contains(followup.ProjectId.Value))
                    .ToList();

            var scopedEmployeeIds = selectedDashboardDepartment == "all"
                ? employees.Select(employee => employee.EmpId).ToHashSet()
                : scopedProjects
                    .SelectMany(project => new int?[] { project.PmEmpId, project.BaEmpId })
                    .Where(empId => empId.HasValue)
                    .Select(empId => empId!.Value)
                    .Concat(scopedAssigns.Select(assign => assign.EmpId))
                    .Concat(scopedIssues.Select(issue => issue.EmpId))
                    .Concat(scopedSupportOrders.Where(order => order.AssignTo.HasValue).Select(order => order.AssignTo!.Value))
                    .ToHashSet();
            var scopedEmployees = selectedDashboardDepartment == "all"
                ? employees
                : employees.Where(employee => scopedEmployeeIds.Contains(employee.EmpId)).ToList();
            var scopedAttendance = selectedDashboardDepartment == "all"
                ? currentAndPreviousMonthAttendance
                : currentAndPreviousMonthAttendance.Where(row => scopedEmployeeIds.Contains(row.EmpId)).ToList();
            var scopedFieldServiceVisits = selectedDashboardDepartment == "all"
                ? fieldServiceVisits
                : fieldServiceVisits
                    .Where(visit => visit.Assignees.Any(assignee => scopedEmployeeIds.Contains(assignee.EmpId)))
                    .ToList();
            var scopedUpcomingFieldServiceVisits = scopedFieldServiceVisits
                .Where(x => !string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                    && (x.EndVisitDate ?? x.VisitDate).Date >= today)
                .OrderBy(x => x.VisitDate)
                .ThenBy(x => x.StartTime)
                .Take(6)
                .Select(x => new HomeDashboardFieldServiceItem
                {
                    VisitId = x.VisitId,
                    Title = x.Title,
                    CoopName = x.Coop?.CoopName ?? "ไม่ระบุสหกรณ์",
                    DateText = FieldServiceDateText(x),
                    AssigneeText = x.Assignees.Any(a => a.Employee != null)
                        ? string.Join(", ", x.Assignees.Where(a => a.Employee != null).OrderBy(a => a.Employee!.EmpName).Select(a => a.Employee!.EmpName))
                        : "ยังไม่กำหนด",
                    StatusText = FieldServiceStatusText(x.Status),
                    StatusColor = FieldServiceStatusColor(x.Status)
                })
                .ToList();
            var scopedFieldServiceStatusMetrics = new List<HomeDashboardMetric>
            {
                CreateMetric("วางแผนแล้ว", scopedFieldServiceVisits.Count(x => string.Equals(x.Status, "PLANNED", StringComparison.OrdinalIgnoreCase)), scopedFieldServiceVisits.Count, "blue"),
                CreateMetric("กำลังดำเนินการ", scopedFieldServiceVisits.Count(x => string.Equals(x.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase)), scopedFieldServiceVisits.Count, "orange"),
                CreateMetric("เสร็จสิ้น", scopedFieldServiceVisits.Count(x => string.Equals(x.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)), scopedFieldServiceVisits.Count, "green"),
                CreateMetric("ยกเลิก", scopedFieldServiceVisits.Count(x => string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)), scopedFieldServiceVisits.Count, "muted")
            };
            var scopedTodayMeetings = selectedDashboardDepartment == "all"
                ? todayMeetings
                : todayMeetings
                    .Where(meeting => meeting.ProjectId.HasValue && scopedProjectIds.Contains(meeting.ProjectId.Value))
                    .ToList();
            var scopedRecentMeetings = selectedDashboardDepartment == "all"
                ? recentMeetings
                : recentMeetings
                    .Where(meeting => meeting.ProjectId.HasValue && scopedProjectIds.Contains(meeting.ProjectId.Value))
                    .ToList();
            var scopedRequirementCards = selectedDashboardDepartment == "all"
                ? requirementCards
                : new List<DashboardRequirementCardRow>();
            if (selectedMeetingGroupId.HasValue)
            {
                scopedTodayMeetings = scopedTodayMeetings
                    .Where(meeting => meeting.GroupId == selectedMeetingGroupId.Value)
                    .ToList();
            }

            var empNameById = employees
                .GroupBy(e => e.EmpId)
                .ToDictionary(
                    g => g.Key,
                    g => string.IsNullOrWhiteSpace(g.First().EmpName) ? $"EMP#{g.Key}" : g.First().EmpName);

            string EmployeeName(int? empId)
            {
                if (empId == null) return "ระบบ";
                return empNameById.TryGetValue(empId.Value, out var name) ? name : $"EMP#{empId.Value}";
            }

            var empAvatarById = employees
                .GroupBy(e => e.EmpId)
                .ToDictionary(g => g.Key, g => ResolveProfileImagePath(g.First().ProfileImagePath));

            string EmployeeAvatar(int? empId)
            {
                if (empId == null) return DefaultProfileImagePath;
                return empAvatarById.TryGetValue(empId.Value, out var avatarPath)
                    ? avatarPath
                    : DefaultProfileImagePath;
            }

            var projectNameById = projects
                .GroupBy(p => p.ProjectId)
                .ToDictionary(g => g.Key, g => g.First().ProjectDisplayName);

            string ProjectName(int? projectId)
            {
                if (projectId == null) return "-";
                return projectNameById.TryGetValue(projectId.Value, out var name) ? name : "-";
            }

            var completedProjectCount = scopedProjects.Count(p => Norm(p.Status) == "DONE");
            var inProgressProjectCount = scopedProjects.Count(p => Norm(p.Status) == "IN_PROGRESS");
            var pendingProjectCount = scopedProjects.Count(p => Norm(p.Status) == "PLAN");

            var projectStatusMetrics = new List<HomeDashboardMetric>
            {
                CreateMetric("Completed", completedProjectCount, scopedProjects.Count, "green"),
                CreateMetric("In Progress", inProgressProjectCount, scopedProjects.Count, "blue"),
                CreateMetric("Pending", pendingProjectCount, scopedProjects.Count, "orange")
            };

            var issueMetrics = new List<HomeDashboardMetric>
            {
                CreateMetric("OPEN", scopedIssues.Count(i => Norm(i.IssueStatus) == "OPEN"), scopedIssues.Count, "blue"),
                CreateMetric("WIP", scopedIssues.Count(i => Norm(i.DevStatus) == "WIP" && !IsIssueResolved(i)), scopedIssues.Count, "orange"),
                CreateMetric("FIXED", scopedIssues.Count(i => Norm(i.DevStatus) == "FIXED" && !IsIssueResolved(i)), scopedIssues.Count, "cyan"),
                CreateMetric("FAIL", scopedIssues.Count(i => Norm(i.IssueStatus) == "FAIL"), scopedIssues.Count, "danger"),
                CreateMetric("PASS", scopedIssues.Count(i => Norm(i.IssueStatus) == "PASS"), scopedIssues.Count, "lime"),
                CreateMetric("REJECT", scopedIssues.Count(i => Norm(i.IssueStatus) == "REJECT"), scopedIssues.Count, "violet")
            };

            var supportMetrics = new List<HomeDashboardMetric>
            {
                CreateMetric("OPEN", scopedSupportOrders.Count(o => Norm(o.Status) == "OPEN"), scopedSupportOrders.Count, "blue"),
                CreateMetric("WIP", scopedSupportOrders.Count(o => Norm(o.DevStatus) == "WIP" && !IsSupportOrderClosed(o.Status, o.DevStatus)), scopedSupportOrders.Count, "orange"),
                CreateMetric("FIXED", scopedSupportOrders.Count(o => Norm(o.DevStatus) == "FIXED" && !IsSupportOrderClosed(o.Status, o.DevStatus)), scopedSupportOrders.Count, "cyan"),
                CreateMetric("FAIL", scopedSupportOrders.Count(o => Norm(o.Status) == "FAIL"), scopedSupportOrders.Count, "danger"),
                CreateMetric("PASS", scopedSupportOrders.Count(o => Norm(o.Status) == "PASS"), scopedSupportOrders.Count, "lime"),
                CreateMetric("REJECT", scopedSupportOrders.Count(o => Norm(o.Status) == "REJECT"), scopedSupportOrders.Count, "violet")
            };

            var lineOverview = await BuildLineOverdueOverviewAsync(scopedProjects, scopedPhases, scopedAssigns, today);

            var phaseTypeRows = scopedPhases
                .GroupBy(p => string.IsNullOrWhiteSpace(p.PhaseType) ? "OTHERS" : Norm(p.PhaseType))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Take(5)
                .Select((g, index) => CreateMetric(PhaseTypeLabel(g.Key), g.Count(), scopedPhases.Count, ColorByIndex(index)))
                .ToList();

            var monthlyPoints = BuildMonthlyProjectPoints(scopedProjects, today.Year, th);
            var maxMonthlyValue = monthlyPoints
                .SelectMany(m => new[] { m.Completed, m.InProgress, m.Pending })
                .DefaultIfEmpty(0)
                .Max();

            var overviewSeries = new List<HomeDashboardChartSeries>
            {
                new() { Name = "Completed", Color = "green", Points = BuildPolyline(monthlyPoints.Select(m => m.Completed).ToList(), maxMonthlyValue) },
                new() { Name = "In Progress", Color = "blue", Points = BuildPolyline(monthlyPoints.Select(m => m.InProgress).ToList(), maxMonthlyValue) },
                new() { Name = "Pending", Color = "orange", Points = BuildPolyline(monthlyPoints.Select(m => m.Pending).ToList(), maxMonthlyValue) }
            };

            var projectOverviewProjects = scopedProjects
                .OrderBy(p => ProjectOverviewSort(p.Status))
                .ThenBy(p => p.EndDate ?? DateTime.MaxValue)
                .ThenBy(p => p.ProjectDisplayName)
                .Select(p => new HomeDashboardProjectOverviewItem
                {
                    ProjectId = p.ProjectId,
                    DepartmentId = p.DepartmentId,
                    DepartmentName = string.IsNullOrWhiteSpace(p.DepartmentName) ? "ยังไม่กำหนดฝ่าย" : p.DepartmentName,
                    ProjectName = p.ProjectDisplayName,
                    StatusCode = Norm(p.Status),
                    StatusText = ProjectStatusText(p.Status),
                    StatusColor = ProjectActivityColor(p.Status),
                    StartText = FormatDashboardDate(p.StartDate, th),
                    EndText = FormatDashboardDate(p.EndDate, th)
                })
                .ToList();

            var topProjectProgress = scopedProjects
                .Select((project, index) =>
                {
                    var projectPhases = scopedPhases.Where(p => p.ProjectId == project.ProjectId).ToList();
                    var progress = CalculateProjectProgress(project.Status, projectPhases.Select(p => p.PhaseStatus).ToList());

                    return new HomeDashboardProjectProgress
                    {
                        Name = project.ProjectDisplayName,
                        Value = progress,
                        Color = ColorByIndex(index)
                    };
                })
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Name)
                .Take(5)
                .ToList();

            var meetingIds = scopedTodayMeetings.Select(m => m.Id).ToList();
            var attendeeCounts = meetingIds.Count == 0
                ? new Dictionary<int, int>()
                : await _context.MeetingAttendees
                    .AsNoTracking()
                    .Where(a => meetingIds.Contains(a.MeetingId))
                    .GroupBy(a => a.MeetingId)
                    .Select(g => new { MeetingId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.MeetingId, x => x.Count);

            var meetingAvatarRows = meetingIds.Count == 0
                ? new List<DashboardMeetingAttendeeRow>()
                : await (
                    from a in _context.MeetingAttendees.AsNoTracking()
                    join e in _context.Employees.AsNoTracking()
                        on a.UserId equals e.EmpId
                    join u in _context.LoginUsers.AsNoTracking()
                        on e.LoginUserId equals u.UserId into userJoin
                    from u in userJoin.DefaultIfEmpty()
                    where meetingIds.Contains(a.MeetingId)
                    orderby a.MeetingId, a.Id
                    select new DashboardMeetingAttendeeRow
                    {
                        MeetingId = a.MeetingId,
                        AttendeeId = a.Id,
                        ProfileImagePath = u != null ? u.ProfileImagePath : null
                    })
                    .ToListAsync();

            var meetingAvatarById = meetingAvatarRows
                .GroupBy(a => a.MeetingId)
                .ToDictionary(
                    g => g.Key,
                    g => ResolveProfileImagePath(g
                        .Select(x => x.ProfileImagePath)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))));

            var meetingCards = scopedTodayMeetings
                .Take(5)
                .Select((meeting, index) => new HomeDashboardMeeting
                {
                    Id = meeting.Id,
                    Title = string.IsNullOrWhiteSpace(meeting.Title) ? "Untitled Meeting" : meeting.Title,
                    Detail = $"{(string.IsNullOrWhiteSpace(meeting.GroupName) ? "ไม่ระบุ Group" : meeting.GroupName)} · {(string.IsNullOrWhiteSpace(meeting.ProjectName) ? "ไม่ระบุโครงการ" : meeting.ProjectName)} · {(string.IsNullOrWhiteSpace(meeting.Location) ? "ไม่ระบุสถานที่" : meeting.Location)}",
                    GroupName = meeting.GroupName ?? "ไม่ระบุ Group",
                    TimeText = FormatMeetingTime(meeting.StartTime),
                    TimeColor = ColorByIndex(index + 3),
                    AttendeeCount = attendeeCounts.TryGetValue(meeting.Id, out var count) ? count : 0,
                    AvatarPath = meetingAvatarById.TryGetValue(meeting.Id, out var avatarPath)
                        ? avatarPath
                        : DefaultProfileImagePath
                })
                .ToList();

            var recentActivities = BuildRecentActivities(scopedProjects, scopedPhases, scopedAssigns, scopedIssues, scopedFollowups, scopedSupportOrders, scopedRequirementCards, scopedRecentMeetings, EmployeeName, EmployeeAvatar, ProjectName, now);
            var yearlyTasks = BuildYearlyTasks(scopedAssigns, scopedPhases, today, out var yearlyTaskAxisMax);
            var watchProjects = BuildWatchProjects(scopedProjects, scopedPhases, scopedAssigns, scopedIssues, scopedFollowups, scopedSupportOrders, EmployeeName, EmployeeAvatar, today);
            var timeSummary = BuildTimeSummary(
                scopedAttendance,
                scopedEmployees,
                EmployeeName,
                monthStart,
                nextMonthStart,
                previousMonthStart,
                yearStart,
                today,
                now,
                attendancePolicy);
            var teamWorkload = BuildTeamWorkload(
                scopedAssigns,
                scopedEmployees.Where(x => string.Equals(x.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)).Select(x => x.EmpId),
                EmployeeName,
                EmployeeAvatar);
            var activeFieldServiceCounts = scopedFieldServiceVisits
                .Where(x => !string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.Assignees)
                .GroupBy(x => x.EmpId)
                .ToDictionary(x => x.Key, x => x.Count());
            var taskOverview = BuildDashboardTaskOverview(scopedAssigns, scopedPhases, scopedIssues, scopedSupportOrders, activeFieldServiceCounts, EmployeeName, EmployeeAvatar, today);
            var projectBaById = projects.ToDictionary(project => project.ProjectId, project => project.BaEmpId);
            var phaseById = phases
                .GroupBy(phase => phase.PhaseId)
                .ToDictionary(group => group.Key, group => group.First());
            int? ProjectBaEmpId(int? projectId)
            {
                return projectId.HasValue && projectBaById.TryGetValue(projectId.Value, out var baEmpId)
                    ? baEmpId
                    : null;
            }

            var visibleOpenAssigns = scopedAssigns
                .Where(assign => !IsDashboardAssignDone(assign))
                .Where(assign => CanSeeOpenAssign(assign, phaseById, ProjectBaEmpId, currentEmpId, isAdmin))
                .ToList();
            var visibleOpenIssues = scopedIssues
                .Where(i => !IsIssueResolved(i))
                .Where(i => CanSeeOpenIssue(i, ProjectBaEmpId, currentEmpId, isAdmin))
                .ToList();
            var visibleOpenSupportOrders = scopedSupportOrders
                .Where(o => !IsSupportOrderClosed(o.Status, o.DevStatus))
                .Where(o => CanSeeOpenSupport(o, ProjectBaEmpId, currentEmpId, isAdmin))
                .ToList();
            var visibleOpenFollowups = scopedFollowups
                .Where(followup => !IsFollowupClosed(followup.Status))
                .Where(followup => CanSeeOpenFollowup(followup, ProjectBaEmpId, currentEmpId, isAdmin))
                .ToList();
            var openIssueSupportCount = visibleOpenAssigns.Count + visibleOpenIssues.Count + visibleOpenSupportOrders.Count + visibleOpenFollowups.Count;
            var openIssueSupportItems = BuildOpenIssueSupportItems(
                visibleOpenAssigns,
                visibleOpenIssues,
                visibleOpenSupportOrders,
                visibleOpenFollowups,
                phaseById,
                ProjectName,
                EmployeeName,
                ProjectBaEmpId,
                currentEmpId,
                isAdmin,
                th);

            var overduePlanPhaseCount = scopedPhases.Count(phase =>
                phase.PlanEnd.HasValue &&
                phase.PlanEnd.Value.Date < today &&
                !IsPhaseDone(phase.PhaseStatus));

            var overduePlanAssignCount = scopedAssigns.Count(assign =>
                assign.PlanEnd.HasValue &&
                assign.PlanEnd.Value.Date < today &&
                Norm(assign.WorkStatus) != "DONE");

            return new HomeDashboardViewModel
            {
                Username = username,
                TotalProjectCount = scopedProjects.Count,
                MeetingsTodayCount = scopedTodayMeetings.Count,
                OpenIssueCount = overduePlanPhaseCount,
                ActiveMemberCount = scopedEmployees.Count(e => Norm(e.Status) == "ACTIVE"),
                OverdueTaskCount = overduePlanAssignCount,
                OpenIssuesNote = "เลยกำหนด Plan",
                OverdueTasksNote = "เลยกำหนด Plan",
                ProjectStatusMetrics = projectStatusMetrics,
                ProjectStatusDonut = BuildDonut(projectStatusMetrics),
                PhaseTypeMetrics = phaseTypeRows,
                PhaseTypeTotal = scopedPhases.Count,
                PhaseTypeDonut = BuildDonut(phaseTypeRows),
                IssueMetrics = issueMetrics,
                IssueTotal = scopedIssues.Count,
                IssueDonut = BuildDonut(issueMetrics),
                SupportMetrics = supportMetrics,
                SupportTotal = scopedSupportOrders.Count,
                SupportDonut = BuildDonut(supportMetrics),
                LineOverdueMetrics = lineOverview.Metrics,
                LineOverdueTotal = lineOverview.Total,
                LineOverdueProjectCount = lineOverview.ProjectCount,
                LineOverdueDonut = BuildDonut(lineOverview.Metrics),
                LineOverdueLinkedCount = lineOverview.LinkedCount,
                LineOverdueMissingLineCount = lineOverview.MissingLineCount,
                ProjectOverviewSeries = overviewSeries,
                ProjectOverviewMonths = monthlyPoints,
                ProjectOverviewTooltip = monthlyPoints.ElementAtOrDefault(Math.Clamp(today.Month - 1, 0, 11)),
                ProjectOverviewProjects = projectOverviewProjects,
                ProjectOverviewDepartments = projectOverviewDepartments,
                SelectedDashboardDepartment = selectedDashboardDepartment,
                SelectedDashboardDepartmentName = selectedDashboardDepartmentName,
                TopProjectProgress = topProjectProgress,
                RecentActivities = recentActivities,
                TodayMeetings = meetingCards,
                MeetingCalendarGroups = meetingCalendarGroups,
                SelectedMeetingGroup = selectedMeetingGroup,
                FieldServiceTodayCount = scopedFieldServiceVisits.Count(x =>
                    x.VisitDate.Date <= today && (x.EndVisitDate ?? x.VisitDate).Date >= today
                    && !string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)),
                FieldServicePlannedCount = scopedFieldServiceVisits.Count(x => string.Equals(x.Status, "PLANNED", StringComparison.OrdinalIgnoreCase)),
                FieldServiceInProgressCount = scopedFieldServiceVisits.Count(x => string.Equals(x.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase)),
                FieldServiceCompletedMonthCount = scopedFieldServiceVisits.Count(x =>
                    string.Equals(x.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                    && (x.EndVisitDate ?? x.VisitDate).Date >= monthStart
                    && (x.EndVisitDate ?? x.VisitDate).Date < nextMonthStart),
                FieldServiceTotalCount = scopedFieldServiceVisits.Count,
                FieldServiceStatusMetrics = scopedFieldServiceStatusMetrics,
                FieldServiceStatusDonut = BuildDonut(scopedFieldServiceStatusMetrics),
                FieldServiceScopeText = selectedDashboardDepartment == "all"
                    ? isAdmin ? "ภาพรวมงานเข้าไซต์ทั้งหมด" : "งานเข้าไซต์ที่มอบหมายให้คุณ"
                    : $"งานเข้าไซต์ของฝ่าย {selectedDashboardDepartmentName}",
                UpcomingFieldServiceVisits = scopedUpcomingFieldServiceVisits,
                YearlyTasks = yearlyTasks,
                YearlyTaskAxisMax = yearlyTaskAxisMax,
                WatchProjects = watchProjects,
                TeamWorkload = teamWorkload,
                TaskOverview = taskOverview,
                OpenIssueSupportCount = openIssueSupportCount,
                OpenIssueSupportItems = openIssueSupportItems,
                MonthWorkHours = timeSummary.MonthWorkHours,
                ClosedWorkHours = timeSummary.ClosedWorkHours,
                OpenWorkHours = timeSummary.OpenWorkHours,
                PendingCheckoutCount = timeSummary.PendingCheckoutCount,
                TodayCheckinCount = timeSummary.TodayCheckinCount,
                TodayCheckoutCount = timeSummary.TodayCheckoutCount,
                TodayMissingCheckinCount = timeSummary.TodayMissingCheckinCount,
                MonthAttendanceDays = timeSummary.MonthAttendanceDays,
                AverageHoursPerDay = timeSummary.AverageHoursPerDay,
                LongShiftCount = timeSummary.LongShiftCount,
                LongDistanceCount = timeSummary.LongDistanceCount,
                PendingCheckoutNames = timeSummary.PendingCheckoutNames,
                TimeTrackingDonut = BuildTwoPartDonut(timeSummary.ClosedWorkHours, timeSummary.OpenWorkHours, ChartColorCss("success"), ChartColorCss("primary")),
                WorkHourTrendText = timeSummary.TrendText,
                WorkHourTrendClass = timeSummary.TrendClass,
                TimeTargetHours = timeSummary.TimeTargetHours,
                TimeTargetProgressPercent = timeSummary.TimeTargetProgressPercent,
                ActiveEmployeeCount = timeSummary.ActiveEmployeeCount,
                TodayOnTimeCount = timeSummary.TodayOnTimeCount,
                TodayLateCount = timeSummary.TodayLateCount,
                MonthLateCount = timeSummary.MonthLateCount,
                MonthIncompleteCheckoutCount = timeSummary.MonthIncompleteCheckoutCount,
                YearLateCount = timeSummary.YearLateCount,
                MonthRecordedEmployeeDays = timeSummary.MonthRecordedEmployeeDays,
                MonthExpectedEmployeeDays = timeSummary.MonthExpectedEmployeeDays,
                TodayAttendanceRate = timeSummary.TodayAttendanceRate,
                MonthAttendanceRate = timeSummary.MonthAttendanceRate,
                MonthPunctualityRate = timeSummary.MonthPunctualityRate,
                YearAttendanceRate = timeSummary.YearAttendanceRate,
                AttendanceTargetPercent = timeSummary.AttendanceTargetPercent,
                AttendancePolicyText = timeSummary.AttendancePolicyText,
                AttendanceTrendText = timeSummary.TrendText,
                AttendanceTrendClass = timeSummary.TrendClass,
                AttendanceDonut = BuildTwoPartDonut(
                    timeSummary.MonthRecordedEmployeeDays,
                    Math.Max(0, timeSummary.MonthExpectedEmployeeDays - timeSummary.MonthRecordedEmployeeDays),
                    ChartColorCss("success"),
                    ChartColorCss("muted")),
                TimeTrendDays = timeSummary.TimeTrendDays,
                TimeHeatmapDays = timeSummary.TimeHeatmapDays
            };
        }

        private async Task<LineOverdueOverviewResult> BuildLineOverdueOverviewAsync(
            IReadOnlyList<DashboardProjectRow> projects,
            IReadOnlyList<DashboardPhaseRow> phases,
            IReadOnlyList<DashboardAssignRow> assigns,
            DateTime today)
        {
            var riskDays = await GetOverdueRiskDaysAsync();
            var riskUntil = today.AddDays(riskDays);
            var projectById = projects
                .GroupBy(x => x.ProjectId)
                .ToDictionary(x => x.Key, x => x.First());
            var phaseById = phases
                .GroupBy(x => x.PhaseId)
                .ToDictionary(x => x.Key, x => x.First());

            var lineLinkedEmpIds = await _context.LineRecipients
                .AsNoTracking()
                .Where(x => x.IsActive && x.EmpId.HasValue && x.LineUserId != null && x.LineUserId != "")
                .Select(x => x.EmpId!.Value)
                .Distinct()
                .ToListAsync();

            var telegramLinkedEmpIds = await _context.TelegramRecipients
                .AsNoTracking()
                .Where(x => x.IsActive && x.EmpId.HasValue && x.TelegramChatId != null && x.TelegramChatId != "")
                .Select(x => x.EmpId!.Value)
                .Distinct()
                .ToListAsync();
            var linkedEmpIdSet = lineLinkedEmpIds
                .Concat(telegramLinkedEmpIds)
                .Distinct()
                .ToHashSet();
            var items = new List<LineOverdueOverviewItem>();
            var affectedProjectIds = assigns
                .Select(assign =>
                {
                    if (!phaseById.TryGetValue(assign.PhaseId, out var phase))
                        return (ProjectId: (int?)null, IsAffected: false);

                    var isActiveDue = !IsLineOverdueAssignDone(assign.WorkStatus, phase.PhaseStatus)
                        && TryLineOverdueSeverity(assign.PlanEnd ?? phase.PlanEnd, today, riskUntil, out _);

                    return (ProjectId: (int?)phase.ProjectId, IsAffected: isActiveDue);
                })
                .Where(x => x.IsAffected && x.ProjectId.HasValue)
                .Select(x => x.ProjectId!.Value)
                .Distinct()
                .ToHashSet();

            foreach (var assign in assigns)
            {
                if (!phaseById.TryGetValue(assign.PhaseId, out var phase))
                    continue;

                if (!affectedProjectIds.Contains(phase.ProjectId))
                    continue;

                if (IsLineOverdueAssignDone(assign.WorkStatus, phase.PhaseStatus))
                {
                    projectById.TryGetValue(phase.ProjectId, out var doneProject);
                    AddLineOverdueOverviewItem(items, "DONE", assign.EmpId, doneProject?.BaEmpId);
                    continue;
                }

                if (!TryLineOverdueSeverity(assign.PlanEnd ?? phase.PlanEnd, today, riskUntil, out var severity))
                    continue;

                projectById.TryGetValue(phase.ProjectId, out var project);
                AddLineOverdueOverviewItem(items, severity, assign.EmpId, project?.BaEmpId);
            }

            var total = items.Count;
            var doneCount = items.Count(x => x.Severity == "DONE");
            var dangerCount = items.Count(x => x.Severity == "DANGER");
            var warningCount = items.Count(x => x.Severity == "WARNING");
            var metrics = new List<HomeDashboardMetric>
            {
                CreateMetric("เสร็จสิ้นแล้ว", doneCount, total, "green"),
                CreateMetric("กำลังดำเนินการเสี่ยงล่าช้า", warningCount, total, "warning"),
                CreateMetric("กำลังดำเนินการล่าช้า", dangerCount, total, "danger")
            };
            var activeItems = items.Where(x => x.Severity != "DONE").ToList();
            var linkedCount = activeItems.Count(x => x.RecipientEmpIds.Count > 0 && x.RecipientEmpIds.All(linkedEmpIdSet.Contains));

            return new LineOverdueOverviewResult
            {
                Total = total,
                ProjectCount = affectedProjectIds.Count,
                Metrics = metrics,
                LinkedCount = linkedCount,
                MissingLineCount = activeItems.Count - linkedCount
            };
        }

        private async Task<LineOverdueOverviewDetailViewModel> BuildLineOverdueOverviewDetailAsync(string? coopName, int? projectId, int? empId, string? status)
        {
            var today = DateTime.Today;
            var riskDays = await GetOverdueRiskDaysAsync();
            var riskUntil = today.AddDays(riskDays);
            var statusFilter = Norm(status);

            var assigns = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(a => a.Employee)
                    .ThenInclude(e => e!.LoginUser)
                .Include(a => a.Phase!)
                    .ThenInclude(p => p!.Project)
                        .ThenInclude(p => p!.Coop)
                .Include(a => a.Phase!)
                    .ThenInclude(p => p!.Project)
                        .ThenInclude(p => p!.BA)
                            .ThenInclude(ba => ba!.LoginUser)
                .ToListAsync();

            var affectedProjectIds = assigns
                .Where(assign => assign.Phase?.Project != null
                    && !IsLineOverdueAssignDone(assign.WorkStatus, assign.Phase.PhaseStatus)
                    && TryLineOverdueSeverity(assign.PlanEnd ?? assign.Phase.PlanEnd, today, riskUntil, out _))
                .Select(assign => assign.Phase!.ProjectId)
                .Distinct()
                .ToHashSet();

            var allRows = new List<LineOverdueOverviewAssignViewModel>();

            foreach (var assign in assigns)
            {
                var phase = assign.Phase;
                var project = phase?.Project;
                if (phase == null || project == null || !affectedProjectIds.Contains(phase.ProjectId))
                    continue;

                var dueDate = assign.PlanEnd ?? phase.PlanEnd;
                var isDone = IsLineOverdueAssignDone(assign.WorkStatus, phase.PhaseStatus);
                string category;
                if (isDone)
                {
                    category = "DONE";
                }
                else if (TryLineOverdueSeverity(dueDate, today, riskUntil, out var severity))
                {
                    category = severity;
                }
                else
                {
                    continue;
                }

                var overdueDays = category == "DANGER" && dueDate.HasValue
                    ? Math.Max(0, (today - dueDate.Value.Date).Days)
                    : 0;
                var daysLeft = category == "WARNING" && dueDate.HasValue
                    ? Math.Max(0, (dueDate.Value.Date - today).Days)
                    : 0;

                allRows.Add(new LineOverdueOverviewAssignViewModel
                {
                    AssignId = assign.AssignId,
                    ProjectId = phase.ProjectId,
                    EmpId = assign.EmpId,
                    CoopName = project.Coop?.CoopName ?? "-",
                    ProjectName = project.ProjectName,
                    PhaseName = phase.PhaseDisplayName,
                    PhasePeriodLabel = phase.PhasePeriodLabel,
                    Role = string.IsNullOrWhiteSpace(assign.Role) ? "-" : assign.Role!,
                    OwnerName = assign.Employee?.EmpName ?? "-",
                    OwnerAvatarPath = ResolveProfileImagePath(assign.Employee?.LoginUser?.ProfileImagePath),
                    BaName = project.BA?.EmpName ?? "-",
                    BaAvatarPath = ResolveProfileImagePath(project.BA?.LoginUser?.ProfileImagePath),
                    StatusCategory = category,
                    StatusText = category switch
                    {
                        "DONE" => "เสร็จสิ้นแล้ว",
                        "DANGER" => $"ล่าช้า {overdueDays} วัน",
                        "WARNING" => daysLeft == 0 ? "ครบกำหนดวันนี้" : $"เสี่ยงล่าช้า เหลือ {daysLeft} วัน",
                        _ => "-"
                    },
                    StatusTone = category switch
                    {
                        "DONE" => "done",
                        "DANGER" => "danger",
                        _ => "warning"
                    },
                    PlanStart = assign.PlanStart ?? phase.PlanStart,
                    PlanEnd = dueDate,
                    PeriodEnd = phase.PeriodEndDate,
                    OverdueDays = overdueDays,
                    Remark = string.IsNullOrWhiteSpace(assign.Remark) ? "-" : assign.Remark!
                });
            }

            var projectOptions = allRows
                .GroupBy(x => new { x.ProjectId, x.ProjectName, x.CoopName })
                .Select(x => new ProjectReportOptionViewModel
                {
                    ProjectId = x.Key.ProjectId,
                    ProjectName = x.Key.ProjectName,
                    CoopName = x.Key.CoopName == "-" ? "" : x.Key.CoopName
                })
                .OrderBy(x => x.CoopName)
                .ThenBy(x => x.ProjectName)
                .ToList();

            var employeeOptions = allRows
                .GroupBy(x => new { x.EmpId, x.OwnerName })
                .Select(x => new EmployeeReportOptionViewModel
                {
                    EmpId = x.Key.EmpId,
                    EmpName = x.Key.OwnerName
                })
                .OrderBy(x => x.EmpName)
                .ToList();

            var rows = allRows
                .Where(x => string.IsNullOrWhiteSpace(coopName) || string.Equals(x.CoopName, coopName, StringComparison.OrdinalIgnoreCase))
                .Where(x => !projectId.HasValue || x.ProjectId == projectId.Value)
                .Where(x => !empId.HasValue || x.EmpId == empId.Value)
                .Where(x => string.IsNullOrWhiteSpace(statusFilter) || x.StatusCategory == statusFilter)
                .OrderBy(x => x.CoopName)
                .ThenBy(x => x.ProjectName)
                .ThenBy(x => LineOverdueOverviewStatusRank(x.StatusCategory))
                .ThenBy(x => x.PlanEnd ?? DateTime.MaxValue)
                .ThenBy(x => x.OwnerName)
                .ThenBy(x => x.Role)
                .ToList();

            var coopGroups = rows
                .GroupBy(x => string.IsNullOrWhiteSpace(x.CoopName) ? "-" : x.CoopName)
                .Select(x => new LineOverdueOverviewCoopGroupViewModel
                {
                    CoopName = x.Key,
                    ProjectCount = x.Select(row => row.ProjectId).Distinct().Count(),
                    TotalCount = x.Count(),
                    DoneCount = x.Count(row => row.StatusCategory == "DONE"),
                    WarningCount = x.Count(row => row.StatusCategory == "WARNING"),
                    DangerCount = x.Count(row => row.StatusCategory == "DANGER"),
                    Rows = x.ToList()
                })
                .OrderBy(x => x.CoopName)
                .ToList();

            return new LineOverdueOverviewDetailViewModel
            {
                GeneratedAt = DateTime.Now,
                Today = today,
                RiskUntil = riskUntil,
                RiskDays = riskDays,
                CoopName = coopName,
                ProjectId = projectId,
                EmpId = empId,
                Status = statusFilter,
                ProjectCount = rows.Select(x => x.ProjectId).Distinct().Count(),
                TotalCount = rows.Count,
                DoneCount = rows.Count(x => x.StatusCategory == "DONE"),
                WarningCount = rows.Count(x => x.StatusCategory == "WARNING"),
                DangerCount = rows.Count(x => x.StatusCategory == "DANGER"),
                ProjectOptions = projectOptions,
                EmployeeOptions = employeeOptions,
                CoopGroups = coopGroups,
                Rows = rows
            };
        }

        private static void AddLineOverdueOverviewItem(
            IList<LineOverdueOverviewItem> items,
            string severity,
            params int?[] recipientEmpIds)
        {
            var recipients = recipientEmpIds
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x!.Value)
                .Distinct()
                .ToHashSet();

            items.Add(new LineOverdueOverviewItem
            {
                Severity = severity,
                RecipientEmpIds = recipients
            });
        }

        private static int LineOverdueOverviewStatusRank(string? status)
        {
            return status switch
            {
                "DANGER" => 1,
                "WARNING" => 2,
                "DONE" => 3,
                _ => 9
            };
        }

        private static bool TryLineOverdueSeverity(DateTime? dueDate, DateTime today, DateTime riskUntil, out string severity)
        {
            severity = "WARNING";
            if (!dueDate.HasValue)
                return false;

            var due = dueDate.Value.Date;
            if (due > riskUntil)
                return false;

            severity = due < today ? "DANGER" : "WARNING";
            return true;
        }

        private static bool IsLineOverdueAssignDone(string? workStatus, string? phaseStatus)
        {
            var work = Norm(workStatus);
            var phase = Norm(phaseStatus);
            return work == "DONE"
                || phase is "DONE" or "ส่งงวดงานแล้ว" or "เสร็จสิ้น" or "เสร็จสิ้นแล้ว" or "อนุมัติจ่ายเงินแล้ว";
        }

        private static bool IsLineOverdueIssueDone(string? issueStatus, string? devStatus)
        {
            var issue = Norm(issueStatus);
            return issue is "PASS" or "REJECT" or "DONE" or "CLOSED" or "RESOLVED";
        }

        private static bool IsLineOverdueSupportDone(string? status, string? devStatus)
        {
            var normalized = Norm(status);
            return normalized is "PASS" or "REJECT" or "DONE" or "CLOSED" or "RESOLVED";
        }

        private static bool IsLineOverdueFollowupOpen(string? status)
        {
            return Norm(status) == "OPEN";
        }

        private static bool IsLineOverdueFollowupDone(string? status)
        {
            return Norm(status) is "DONE" or "CLOSED" or "RESOLVED" or "FIXED" or "PASS" or "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
        }

        [RequireMenu("Home.Index")]
        public async Task<IActionResult> Activities()
        {
            var now = DateTime.Now;

            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .Select(i => new DashboardIssueRow
                {
                    IssueId = i.IssueId,
                    ProjectId = i.ProjectId,
                    IssueName = i.IssueName,
                    IssueStatus = i.IssueStatus,
                    DevStatus = i.DevStatus,
                    StartDate = i.StartDate,
                    EndDate = i.EndDate,
                    CreatedAt = i.CreatedAt,
                    CreatedBy = i.CreatedBy,
                    EmpId = i.AssignTo
                })
                .ToListAsync();

            var followups = await _context.ProjectFollowups
                .AsNoTracking()
                .Select(f => new DashboardFollowupRow
                {
                    FollowupId = f.FollowupId,
                    ProjectId = f.ProjectId,
                    TaskTitle = f.TaskTitle,
                    OwnerEmpId = f.OwnerEmpId,
                    CreatedByEmpId = f.CreatedByEmpId,
                    Status = f.Status,
                    NextFollowupDate = f.NextFollowupDate,
                    CreatedAt = f.CreatedAt
                })
                .ToListAsync();

            var supportOrders = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Select(o => new DashboardSupportOrderRow
                {
                    OrderId = o.OrderId,
                    ProjectId = o.ProjectId,
                    OrderTitle = o.OrderTitle,
                    Status = o.Status,
                    DevStatus = o.DevStatus,
                    Priority = o.Priority,
                    CreatedBy = o.CreatedBy,
                    AssignTo = o.AssignTo,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            var requirementCards = await _context.RequirementCards
                .AsNoTracking()
                .Where(c => !c.IsArchived)
                .Select(c => new DashboardRequirementCardRow
                {
                    CardId = c.CardId,
                    Title = c.Title,
                    ColumnName = c.Column != null ? c.Column.ColumnName : null,
                    CreatedByUserId = c.CreatedByUserId,
                    CreatedByUsername = c.CreatedByUser != null ? c.CreatedByUser.Username : null,
                    CreatedByUserProfileImagePath = c.CreatedByUser != null ? c.CreatedByUser.ProfileImagePath : null,
                    CreatedByEmpId = c.CreatedByEmpId,
                    CreatedByEmployeeName = c.CreatedByEmployee != null ? c.CreatedByEmployee.EmpName : null,
                    CreatedByEmployeeProfileImagePath = c.CreatedByEmployee != null && c.CreatedByEmployee.LoginUser != null
                        ? c.CreatedByEmployee.LoginUser.ProfileImagePath
                        : null,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync();

            var meetings = await (
                from m in _context.Meetings.AsNoTracking()
                join p in _context.Projects.AsNoTracking()
                    on m.ProjectId equals p.ProjectId into projectJoin
                from p in projectJoin.DefaultIfEmpty()
                select new DashboardMeetingRow
                {
                    Id = m.Id,
                    Title = m.Title,
                    StartTime = m.StartTime,
                    Location = m.Location,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,
                    CreatedBy = m.CreatedBy,
                    ProjectName = p != null ? p.ProjectName : null
                }
            ).ToListAsync();

            var employees = await _context.Employees
                .AsNoTracking()
                .Select(e => new DashboardEmployeeRow
                {
                    EmpId = e.EmpId,
                    EmpName = e.EmpName,
                    Status = e.Status,
                    LoginUserId = e.LoginUserId,
                    ProfileImagePath = e.LoginUser != null ? e.LoginUser.ProfileImagePath : null
                })
                .ToListAsync();
            await FillMissingEmployeeProfileImagesAsync(employees);

            var projects = await _context.Projects
                .AsNoTracking()
                .Select(p => new DashboardProjectRow
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName,
                    CoopName = p.Coop != null ? p.Coop.CoopName : null,
                    BaEmpId = p.BaEmpId,
                    CreatedAt = p.CreatedAt,
                    EntryId = p.EntryId
                })
                .ToListAsync();

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Select(p => new DashboardPhaseRow
                {
                    PhaseId = p.PhaseId,
                    ProjectId = p.ProjectId,
                    PhaseName = p.PhaseName,
                    PhaseType = p.PhaseType,
                    PhaseStatus = p.PhaseStatus,
                    CreatedAt = p.CreatedAt,
                    EntryId = p.EntryId
                })
                .ToListAsync();

            var assigns = await _context.PhaseAssigns
                .AsNoTracking()
                .Select(a => new DashboardAssignRow
                {
                    AssignId = a.AssignId,
                    PhaseId = a.PhaseId,
                    EmpId = a.EmpId,
                    Role = a.Role,
                    WorkStatus = a.WorkStatus,
                    CreatedAt = a.CreatedAt,
                    EntryId = a.EntryId
                })
                .ToListAsync();

            var empNameById = employees
                .GroupBy(e => e.EmpId)
                .ToDictionary(
                    g => g.Key,
                    g => string.IsNullOrWhiteSpace(g.First().EmpName) ? $"EMP#{g.Key}" : g.First().EmpName);

            var empAvatarById = employees
                .GroupBy(e => e.EmpId)
                .ToDictionary(g => g.Key, g => ResolveProfileImagePath(g.First().ProfileImagePath));

            var projectNameById = projects
                .GroupBy(p => p.ProjectId)
                .ToDictionary(g => g.Key, g => g.First().ProjectDisplayName);

            string EmployeeName(int? empId)
            {
                if (empId == null) return "ระบบ";
                return empNameById.TryGetValue(empId.Value, out var name) ? name : $"EMP#{empId.Value}";
            }

            string EmployeeAvatar(int? empId)
            {
                if (empId == null) return DefaultProfileImagePath;
                return empAvatarById.TryGetValue(empId.Value, out var avatarPath)
                    ? avatarPath
                    : DefaultProfileImagePath;
            }

            string ProjectName(int? projectId)
            {
                if (projectId == null) return "-";
                return projectNameById.TryGetValue(projectId.Value, out var name) ? name : "-";
            }

            var activities = BuildRecentActivities(projects, phases, assigns, issues, followups, supportOrders, requirementCards, meetings, EmployeeName, EmployeeAvatar, ProjectName, now, 100);

            return View(activities);
        }

        private static HomeDashboardMetric CreateMetric(string label, int count, int total, string color)
        {
            return new HomeDashboardMetric
            {
                Label = label,
                Count = count,
                Percent = total <= 0 ? 0 : Math.Round(count * 100m / total, 1),
                Color = color,
                HexColor = ChartColorCss(color)
            };
        }

        private static string BuildDonut(IReadOnlyList<HomeDashboardMetric> metrics)
        {
            var nonZero = metrics.Where(m => m.Count > 0).ToList();
            if (nonZero.Count == 0) return "conic-gradient(var(--pt-chart-muted, #263450) 0 100%)";

            var cursor = 0m;
            var segments = new List<string>();
            for (var i = 0; i < nonZero.Count; i++)
            {
                var metric = nonZero[i];
                var next = i == nonZero.Count - 1 ? 100m : cursor + metric.Percent;
                segments.Add($"{metric.HexColor} {CssPercent(cursor)}% {CssPercent(next)}%");
                cursor = next;
            }

            return $"conic-gradient({string.Join(", ", segments)})";
        }

        private static string BuildTwoPartDonut(decimal first, decimal second, string firstColor, string secondColor)
        {
            var total = first + second;
            if (total <= 0) return "conic-gradient(var(--pt-chart-muted, #263450) 0 100%)";

            var split = Math.Round(first * 100m / total, 1);
            return $"conic-gradient({firstColor} 0 {CssPercent(split)}%, {secondColor} {CssPercent(split)}% 100%)";
        }

        private static string BuildThreePartDonut(decimal first, decimal second, decimal third, string firstColor, string secondColor, string thirdColor)
        {
            var total = first + second + third;
            if (total <= 0) return "conic-gradient(var(--pt-chart-muted, #263450) 0 100%)";

            var firstEnd = Math.Round(first * 100m / total, 1);
            var secondEnd = Math.Round((first + second) * 100m / total, 1);
            return $"conic-gradient({firstColor} 0 {CssPercent(firstEnd)}%, {secondColor} {CssPercent(firstEnd)}% {CssPercent(secondEnd)}%, {thirdColor} {CssPercent(secondEnd)}% 100%)";
        }

        private static string BuildPolyline(IReadOnlyList<int> values, int maxValue)
        {
            const decimal startX = 35m;
            const decimal endX = 690m;
            const decimal topY = 55m;
            const decimal bottomY = 222m;

            if (values.Count == 0) return "";

            var safeMax = Math.Max(maxValue, 1);
            var step = values.Count == 1 ? 0 : (endX - startX) / (values.Count - 1);
            var points = values.Select((value, index) =>
            {
                var x = startX + (step * index);
                var y = bottomY - ((decimal)value / safeMax * (bottomY - topY));
                return $"{CssNumber(x)},{CssNumber(y)}";
            });

            return string.Join(" ", points);
        }

        private static List<HomeDashboardMonthPoint> BuildMonthlyProjectPoints(
            IReadOnlyList<DashboardProjectRow> projects,
            int year,
            CultureInfo culture)
        {
            var months = new List<HomeDashboardMonthPoint>();
            for (var month = 1; month <= 12; month++)
            {
                var monthStart = new DateTime(year, month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                months.Add(new HomeDashboardMonthPoint
                {
                    Label = monthStart.ToString("MMM", culture),
                    Completed = projects.Count(p =>
                        Norm(p.Status) == "DONE" &&
                        (p.EndDate == null || p.EndDate.Value.Date <= monthEnd)),
                    InProgress = projects.Count(p =>
                        Norm(p.Status) == "IN_PROGRESS" &&
                        DateRangeIntersects(p.StartDate, p.EndDate, monthStart, monthEnd)),
                    Pending = projects.Count(p =>
                        Norm(p.Status) == "PLAN" &&
                        DateRangeIntersects(p.StartDate, p.EndDate, monthStart, monthEnd))
                });
            }

            return months;
        }

        private static bool DateRangeIntersects(DateTime? start, DateTime? end, DateTime rangeStart, DateTime rangeEnd)
        {
            var actualStart = start?.Date ?? DateTime.MinValue.Date;
            var actualEnd = end?.Date ?? DateTime.MaxValue.Date;
            return actualStart <= rangeEnd && actualEnd >= rangeStart;
        }

        private static int CalculateProjectProgress(string? projectStatus, IReadOnlyList<string?> phaseStatuses)
        {
            if (phaseStatuses.Count == 0)
            {
                return Norm(projectStatus) switch
                {
                    "DONE" => 100,
                    "IN_PROGRESS" => 50,
                    "PLAN" => 0,
                    _ => 0
                };
            }

            var completed = phaseStatuses.Count(IsPhaseDone);
            return (int)Math.Round(completed * 100m / phaseStatuses.Count);
        }

        private static List<HomeDashboardActivity> BuildRecentActivities(
            IReadOnlyList<DashboardProjectRow> projects,
            IReadOnlyList<DashboardPhaseRow> phases,
            IReadOnlyList<DashboardAssignRow> assigns,
            IReadOnlyList<DashboardIssueRow> issues,
            IReadOnlyList<DashboardFollowupRow> followups,
            IReadOnlyList<DashboardSupportOrderRow> supportOrders,
            IReadOnlyList<DashboardRequirementCardRow> requirementCards,
            IReadOnlyList<DashboardMeetingRow> meetings,
            Func<int?, string> employeeName,
            Func<int?, string> employeeAvatar,
            Func<int?, string> projectName,
            DateTime now,
            int take = 5)
        {
            var activities = new List<(DateTime createdAt, HomeDashboardActivity item)>();
            var phaseById = phases
                .GroupBy(p => p.PhaseId)
                .ToDictionary(g => g.Key, g => g.First());
            var projectOwnerById = projects
                .GroupBy(p => p.ProjectId)
                .ToDictionary(g => g.Key, g => g.First().BaEmpId);

            string ProjectOwnerName(int? projectId)
            {
                if (projectId == null) return "ระบบ";
                return projectOwnerById.TryGetValue(projectId.Value, out var ownerEmpId)
                    ? employeeName(ownerEmpId)
                    : "ระบบ";
            }

            activities.AddRange(projects
                .Where(p => p.CreatedAt.HasValue && p.CreatedAt.Value > DateTime.MinValue.AddYears(1))
                .Select(p => (
                    p.CreatedAt!.Value,
                    new HomeDashboardActivity
                    {
                        Actor = employeeName(p.EntryId),
                        Detail = $"อัปเดตโครงการ: {p.ProjectName}",
                        OwnerText = $"เจ้าของงาน: {employeeName(p.BaEmpId)}",
                        TimeText = RelativeTimeThai(p.CreatedAt.Value, now),
                        Color = ProjectActivityColor(p.Status),
                        AvatarPath = employeeAvatar(p.EntryId),
                        Url = "/Projects"
                    })));

            activities.AddRange(phases
                .Where(p => p.CreatedAt.HasValue && p.CreatedAt.Value > DateTime.MinValue.AddYears(1))
                .Select(p =>
                {
                    return (
                        p.CreatedAt!.Value,
                        new HomeDashboardActivity
                        {
                            Actor = employeeName(p.EntryId),
                            Detail = $"อัปเดตงวดงาน: {p.PhaseName} ({projectName(p.ProjectId)})",
                            OwnerText = $"เจ้าของงาน: {ProjectOwnerName(p.ProjectId)}",
                            TimeText = RelativeTimeThai(p.CreatedAt.Value, now),
                            Color = PhaseActivityColor(p.PhaseStatus, p.PhaseType),
                            AvatarPath = employeeAvatar(p.EntryId),
                            Url = $"/ProjectPhases?projectId={p.ProjectId}"
                        });
                }));

            activities.AddRange(assigns
                .Where(a => a.CreatedAt.HasValue && a.CreatedAt.Value > DateTime.MinValue.AddYears(1))
                .Select(a =>
                {
                    phaseById.TryGetValue(a.PhaseId, out var phase);
                    var projectId = phase?.ProjectId;
                    var workName = string.IsNullOrWhiteSpace(a.Role)
                        ? phase?.PhaseName ?? $"Assign #{a.AssignId}"
                        : a.Role;

                    return (
                        a.CreatedAt!.Value,
                        new HomeDashboardActivity
                        {
                            Actor = employeeName(a.EntryId),
                            Detail = $"อัปเดตงานที่มอบหมาย: {workName} ({projectName(projectId)})",
                            OwnerText = $"เจ้าของงาน: {employeeName(a.EmpId)}",
                            TimeText = RelativeTimeThai(a.CreatedAt.Value, now),
                            Color = Norm(a.WorkStatus) == "DONE" ? "green" : "orange",
                            AvatarPath = employeeAvatar(a.EntryId),
                            Url = projectId.HasValue
                                ? $"/PhaseAssigns?projectId={projectId.Value}&empId={a.EmpId}"
                                : "/PhaseAssigns"
                        });
                }));

            activities.AddRange(issues
                .Where(i => i.CreatedAt > DateTime.MinValue.AddYears(1))
                .Select(i => (
                    i.CreatedAt,
                    new HomeDashboardActivity
                    {
                        Actor = employeeName(i.CreatedBy ?? i.EmpId),
                        Detail = $"แจ้ง Issue: {i.IssueName}",
                        OwnerText = $"เจ้าของงาน: {employeeName(i.EmpId)}",
                        TimeText = RelativeTimeThai(i.CreatedAt, now),
                        Color = IsIssueResolved(i) ? "green" : IsIssueInProgress(i) ? "orange" : "pink",
                        AvatarPath = employeeAvatar(i.CreatedBy ?? i.EmpId),
                        Url = $"/ProjectIssues/Details/{i.IssueId}"
                    })));

            activities.AddRange(followups
                .Where(f => f.CreatedAt > DateTime.MinValue.AddYears(1))
                .Select(f => (
                    f.CreatedAt,
                    new HomeDashboardActivity
                    {
                        Actor = employeeName(f.OwnerEmpId),
                        Detail = $"ติดตามงาน: {f.TaskTitle} ({projectName(f.ProjectId)})",
                        OwnerText = $"เจ้าของงาน: {employeeName(f.OwnerEmpId)}",
                        TimeText = RelativeTimeThai(f.CreatedAt, now),
                        Color = Norm(f.Status) == "DONE" ? "green" : "cyan",
                        AvatarPath = employeeAvatar(f.OwnerEmpId),
                        Url = $"/Followups/Details/{f.FollowupId}"
                    })));

            activities.AddRange(supportOrders
                .Where(o => o.CreatedAt.HasValue && o.CreatedAt.Value > DateTime.MinValue.AddYears(1))
                .Select(o => (
                    o.CreatedAt!.Value,
                    new HomeDashboardActivity
                    {
                        Actor = employeeName(o.CreatedBy ?? o.AssignTo),
                        Detail = $"งาน Support: {(string.IsNullOrWhiteSpace(o.OrderTitle) ? $"Order #{o.OrderId}" : o.OrderTitle)} ({projectName(o.ProjectId)})",
                        OwnerText = $"เจ้าของงาน: {employeeName(o.AssignTo)}",
                        TimeText = RelativeTimeThai(o.CreatedAt.Value, now),
                        Color = SupportOrderActivityColor(o.Status, o.DevStatus),
                        AvatarPath = employeeAvatar(o.CreatedBy ?? o.AssignTo),
                        Url = $"/SupportOrders/Details/{o.OrderId}"
                    })));

            activities.AddRange(requirementCards
                .Where(c => (c.UpdatedAt ?? c.CreatedAt) > DateTime.MinValue.AddYears(1))
                .Select(c =>
                {
                    var activityAt = c.UpdatedAt ?? c.CreatedAt;
                    var isUpdated = c.UpdatedAt.HasValue && c.UpdatedAt.Value > c.CreatedAt.AddSeconds(1);
                    var title = string.IsNullOrWhiteSpace(c.Title) ? $"Card #{c.CardId}" : c.Title;
                    var columnName = string.IsNullOrWhiteSpace(c.ColumnName) ? "-" : c.ColumnName;
                    var actorName = !string.IsNullOrWhiteSpace(c.CreatedByEmployeeName)
                        ? c.CreatedByEmployeeName
                        : (!string.IsNullOrWhiteSpace(c.CreatedByUsername)
                            ? c.CreatedByUsername
                            : employeeName(c.CreatedByEmpId));
                    var avatarPath = !string.IsNullOrWhiteSpace(c.CreatedByEmployeeProfileImagePath)
                        ? ResolveProfileImagePath(c.CreatedByEmployeeProfileImagePath)
                        : (!string.IsNullOrWhiteSpace(c.CreatedByUserProfileImagePath)
                            ? ResolveProfileImagePath(c.CreatedByUserProfileImagePath)
                            : employeeAvatar(c.CreatedByEmpId));

                    return (
                        activityAt,
                        new HomeDashboardActivity
                        {
                            Actor = actorName,
                            Detail = $"{(isUpdated ? "อัปเดต" : "เพิ่ม")}การ์ด Project Board: {title}",
                            OwnerText = $"หัวข้อ: {columnName}",
                            TimeText = RelativeTimeThai(activityAt, now),
                            Color = "purple",
                            AvatarPath = avatarPath,
                            Url = $"/RequirementBoard?cardId={c.CardId}"
                        });
                }));

            activities.AddRange(meetings
                .Where(m => (m.UpdatedAt ?? m.CreatedAt) > DateTime.MinValue.AddYears(1))
                .Select(m =>
                {
                    var activityAt = m.UpdatedAt ?? m.CreatedAt;
                    var isUpdated = m.UpdatedAt.HasValue && m.UpdatedAt.Value > m.CreatedAt.AddSeconds(1);

                    return (
                        activityAt,
                        new HomeDashboardActivity
                        {
                            Actor = employeeName(m.CreatedBy),
                            Detail = $"{(isUpdated ? "อัปเดต" : "สร้าง")}การประชุม: {m.Title}",
                            OwnerText = $"เจ้าของงาน: {employeeName(m.CreatedBy)}",
                            TimeText = RelativeTimeThai(activityAt, now),
                            Color = "blue",
                            AvatarPath = employeeAvatar(m.CreatedBy),
                            Url = $"/Meetings/Show/{m.Id}"
                        });
                }));

            return activities
                .OrderByDescending(x => x.createdAt)
                .Take(take)
                .Select(x => x.item)
                .ToList();
        }

        private static List<HomeDashboardTaskPeriod> BuildYearlyTasks(
            IReadOnlyList<DashboardAssignRow> assigns,
            IReadOnlyList<DashboardPhaseRow> phases,
            DateTime today,
            out int axisMax)
        {
            var th = new CultureInfo("th-TH");
            var phaseById = phases
                .GroupBy(p => p.PhaseId)
                .ToDictionary(g => g.Key, g => g.First());

            var rows = Enumerable.Range(1, 12)
                .Select(month =>
                {
                    var monthStart = new DateTime(today.Year, month, 1);
                    var nextMonthStart = monthStart.AddMonths(1);
                    var completed = assigns.Count(a =>
                    {
                        if (!phaseById.TryGetValue(a.PhaseId, out var phase)) return false;
                        var date = AssignPhaseBucketDate(a, phase)?.Date;
                        return date >= monthStart &&
                               date < nextMonthStart &&
                               IsPhaseDone(phase.PhaseStatus);
                    });
                    var inProgress = assigns.Count(a =>
                    {
                        if (!phaseById.TryGetValue(a.PhaseId, out var phase)) return false;
                        var date = AssignPhaseBucketDate(a, phase)?.Date;
                        var status = Norm(phase.PhaseStatus);
                        return date >= monthStart &&
                               date < nextMonthStart &&
                               (status == "กำลังดำเนินการ" || status == "IN_PROGRESS");
                    });
                    var pending = assigns.Count(a =>
                    {
                        if (!phaseById.TryGetValue(a.PhaseId, out var phase)) return false;
                        var date = AssignPhaseBucketDate(a, phase)?.Date;
                        var status = Norm(phase.PhaseStatus);
                        return date >= monthStart &&
                               date < nextMonthStart &&
                               (status == "วางแผน" || status == "PENDING" || status == "PLAN");
                    });

                    return new HomeDashboardTaskPeriod
                    {
                        PeriodLabel = monthStart.ToString("MMM", th),
                        Completed = completed,
                        InProgress = inProgress,
                        Pending = pending
                    };
                })
                .ToList();

            axisMax = 30;

            foreach (var row in rows)
            {
                row.CompletedHeight = HeightFromValue(row.Completed, axisMax);
                row.InProgressHeight = HeightFromValue(row.InProgress, axisMax);
                row.PendingHeight = HeightFromValue(row.Pending, axisMax);
            }

            return rows;
        }

        private static List<HomeDashboardWatchProject> BuildWatchProjects(
            IReadOnlyList<DashboardProjectRow> projects,
            IReadOnlyList<DashboardPhaseRow> phases,
            IReadOnlyList<DashboardAssignRow> assigns,
            IReadOnlyList<DashboardIssueRow> issues,
            IReadOnlyList<DashboardFollowupRow> followups,
            IReadOnlyList<DashboardSupportOrderRow> supportOrders,
            Func<int?, string> employeeName,
            Func<int?, string> employeeAvatar,
            DateTime today)
        {
            var th = new CultureInfo("th-TH");
            var next14Days = today.AddDays(14);
            var phasesByProject = phases
                .GroupBy(p => p.ProjectId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var projectByPhase = phases
                .GroupBy(p => p.PhaseId)
                .ToDictionary(g => g.Key, g => g.First().ProjectId);
            var assignsByProject = assigns
                .Select(a => new
                {
                    Assign = a,
                    ProjectId = projectByPhase.TryGetValue(a.PhaseId, out var projectId) ? projectId : 0
                })
                .Where(x => x.ProjectId > 0)
                .GroupBy(x => x.ProjectId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Assign).ToList());
            var issuesByProject = issues
                .GroupBy(i => i.ProjectId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var followupsByProject = followups
                .Where(f => f.ProjectId != null)
                .GroupBy(f => f.ProjectId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
            var supportByProject = supportOrders
                .GroupBy(o => o.ProjectId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return projects
                .Select(project =>
                {
                    phasesByProject.TryGetValue(project.ProjectId, out var projectPhases);
                    assignsByProject.TryGetValue(project.ProjectId, out var projectAssigns);
                    issuesByProject.TryGetValue(project.ProjectId, out var projectIssues);
                    followupsByProject.TryGetValue(project.ProjectId, out var projectFollowups);
                    supportByProject.TryGetValue(project.ProjectId, out var projectSupportOrders);

                    projectPhases ??= new List<DashboardPhaseRow>();
                    projectAssigns ??= new List<DashboardAssignRow>();
                    projectIssues ??= new List<DashboardIssueRow>();
                    projectFollowups ??= new List<DashboardFollowupRow>();
                    projectSupportOrders ??= new List<DashboardSupportOrderRow>();

                    var progress = CalculateWatchProjectProgress(project.Status, projectPhases, projectAssigns);
                    var openIssues = projectIssues.Count(i => !IsIssueResolved(i));
                    var urgentIssues = projectIssues.Count(i => !IsIssueResolved(i) && IsHighPriority(i.IssuePriority));
                    var failedIssueRounds = projectIssues
                        .Where(i => !IsIssueResolved(i))
                        .Sum(i => i.ReopenCount);
                    var overduePhases = projectPhases.Count(p => p.PlanEnd?.Date < today && !IsPhaseDone(p.PhaseStatus));
                    var upcomingPhases = projectPhases.Count(p =>
                        p.PlanEnd?.Date >= today &&
                        p.PlanEnd?.Date <= next14Days &&
                        !IsPhaseDone(p.PhaseStatus));
                    var overdueAssigns = projectAssigns.Count(a => a.PlanEnd?.Date < today && Norm(a.WorkStatus) != "DONE");
                    var overdueFollowups = projectFollowups.Count(f =>
                        f.NextFollowupDate?.Date < today &&
                        Norm(f.Status) != "DONE");
                    var openSupportOrders = projectSupportOrders.Count(o => !IsSupportOrderClosed(o.Status, o.DevStatus));
                    var urgentSupportOrders = projectSupportOrders.Count(o =>
                        !IsSupportOrderClosed(o.Status, o.DevStatus) &&
                        IsHighPriority(o.Priority));
                    var overdueSupportOrders = projectSupportOrders.Count(o =>
                        !IsSupportOrderClosed(o.Status, o.DevStatus) &&
                        o.EndDate?.Date < today);
                    var projectOverdue = project.EndDate?.Date < today && Norm(project.Status) != "DONE";

                    var score = 0;
                    var reasons = new List<string>();

                    if (projectOverdue)
                    {
                        score += 6;
                        reasons.Add("โครงการเลยกำหนด");
                    }

                    if (overduePhases > 0)
                    {
                        score += overduePhases * 3;
                        reasons.Add($"งวดล่าช้า {overduePhases}");
                    }

                    if (overdueAssigns > 0)
                    {
                        score += overdueAssigns * 2;
                        reasons.Add($"งานค้าง {overdueAssigns}");
                    }

                    if (urgentIssues > 0)
                    {
                        score += urgentIssues * 4;
                        reasons.Add($"Issue ด่วน {urgentIssues}");
                    }

                    if (openIssues > 0)
                    {
                        score += openIssues;
                        reasons.Add($"Issue เปิด {openIssues}");
                    }

                    if (failedIssueRounds > 0)
                    {
                        score += failedIssueRounds * 2;
                        reasons.Add($"FAIL {failedIssueRounds}");
                    }

                    if (overdueFollowups > 0)
                    {
                        score += overdueFollowups * 2;
                        reasons.Add($"Followup เลยกำหนด {overdueFollowups}");
                    }

                    if (overdueSupportOrders > 0)
                    {
                        score += overdueSupportOrders * 3;
                        reasons.Add($"Support เลยกำหนด {overdueSupportOrders}");
                    }

                    if (urgentSupportOrders > 0)
                    {
                        score += urgentSupportOrders * 3;
                        reasons.Add($"Support ด่วน {urgentSupportOrders}");
                    }

                    if (openSupportOrders > 0)
                    {
                        score += openSupportOrders;
                        reasons.Add($"Support ค้าง {openSupportOrders}");
                    }

                    if (upcomingPhases > 0)
                    {
                        score += upcomingPhases;
                        reasons.Add($"ใกล้ครบกำหนด {upcomingPhases}");
                    }

                    if (score <= 0)
                    {
                        return null;
                    }

                    var ownerEmpId = project.BaEmpId
                        ?? projectAssigns
                            .GroupBy(a => a.EmpId)
                            .OrderByDescending(g => g.Count())
                            .Select(g => (int?)g.Key)
                            .FirstOrDefault();
                    var dueText = BuildProjectDueText(project, projectPhases, projectAssigns, projectFollowups, projectSupportOrders, today, th);
                    var riskLevel = score >= 12 ? "สูง" : score >= 6 ? "กลาง" : "เฝ้าระวัง";
                    var riskColor = score >= 12 ? "pink" : score >= 6 ? "orange" : "blue";

                    return new HomeDashboardWatchProject
                    {
                        ProjectId = project.ProjectId,
                        ProjectName = project.ProjectDisplayName,
                        OwnerName = employeeName(ownerEmpId),
                        AvatarPath = employeeAvatar(ownerEmpId),
                        RiskLevel = riskLevel,
                        RiskColor = riskColor,
                        DueText = dueText,
                        RiskScore = score,
                        Progress = progress,
                        Reasons = reasons.Distinct().Take(4).ToList()
                    };
                })
                .Where(x => x != null)
                .OrderByDescending(x => x!.RiskScore)
                .ThenBy(x => x!.Progress)
                .ThenBy(x => x!.ProjectName)
                .Take(5)
                .Select(x => x!)
                .ToList();
        }

        private static string BuildProjectDueText(
            DashboardProjectRow project,
            IReadOnlyList<DashboardPhaseRow> phases,
            IReadOnlyList<DashboardAssignRow> assigns,
            IReadOnlyList<DashboardFollowupRow> followups,
            IReadOnlyList<DashboardSupportOrderRow> supportOrders,
            DateTime today,
            CultureInfo th)
        {
            if (project.EndDate?.Date < today && Norm(project.Status) != "DONE")
            {
                return $"เลยกำหนด {project.EndDate.Value.ToString("dd MMM yy", th)}";
            }

            var nearestDue = new List<DateTime?> { project.EndDate }
                .Concat(phases.Select(p => p.PlanEnd))
                .Concat(assigns.Select(a => a.PlanEnd))
                .Concat(followups.Select(f => f.NextFollowupDate))
                .Concat(supportOrders.Select(o => o.EndDate))
                .Where(d => d?.Date >= today)
                .Select(d => d!.Value.Date)
                .OrderBy(d => d)
                .FirstOrDefault();

            return nearestDue == default
                ? "ยังไม่มีกำหนดใกล้ถึง"
                : $"ครบกำหนด {nearestDue.ToString("dd MMM yy", th)}";
        }

        private static int CalculateWatchProjectProgress(
            string? projectStatus,
            IReadOnlyList<DashboardPhaseRow> phases,
            IReadOnlyList<DashboardAssignRow> assigns)
        {
            if (assigns.Count > 0)
            {
                var completedAssigns = assigns.Count(a => Norm(a.WorkStatus) == "DONE");
                return (int)Math.Round(completedAssigns * 100m / assigns.Count);
            }

            return CalculateProjectProgress(projectStatus, phases.Select(p => p.PhaseStatus).ToList());
        }

        private static DashboardTimeSummary BuildTimeSummary(
            IReadOnlyList<DashboardAttendanceRow> attendances,
            IReadOnlyList<DashboardEmployeeRow> employees,
            Func<int?, string> employeeName,
            DateTime monthStart,
            DateTime nextMonthStart,
            DateTime previousMonthStart,
            DateTime yearStart,
            DateTime today,
            DateTime now,
            DashboardAttendancePolicy policy)
        {
            var activeEmployeeIds = employees
                .Where(e => Norm(e.Status) == "ACTIVE")
                .Select(e => e.EmpId)
                .ToHashSet();
            var canonicalRows = attendances
                .Where(a => activeEmployeeIds.Contains(a.EmpId))
                .GroupBy(a => new { a.EmpId, WorkDate = a.WorkDate.Date })
                .Select(group => new DashboardAttendanceRow
                {
                    EmpId = group.Key.EmpId,
                    WorkDate = group.Key.WorkDate,
                    CheckinTime = group
                        .Where(row => row.CheckinTime.HasValue)
                        .Select(row => row.CheckinTime)
                        .Min(),
                    CheckoutTime = group
                        .Where(row => row.CheckoutTime.HasValue)
                        .Select(row => row.CheckoutTime)
                        .Max(),
                    DistanceKm = group.Max(row => row.DistanceKm)
                })
                .ToList();
            var monthRows = canonicalRows
                .Where(a => a.WorkDate >= monthStart && a.WorkDate < nextMonthStart)
                .ToList();
            var previousRows = canonicalRows
                .Where(a => a.WorkDate >= previousMonthStart && a.WorkDate < monthStart)
                .ToList();
            var yearRows = canonicalRows
                .Where(a => a.WorkDate >= yearStart && a.WorkDate < nextMonthStart)
                .ToList();
            var lateThreshold = policy.WorkStart.Add(TimeSpan.FromMinutes(policy.LateGraceMinutes));

            static bool IsWorkday(DateTime day) =>
                day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
            bool IsElapsedWorkday(DateTime day) =>
                IsWorkday(day) && (day.Date < today.Date || (day.Date == today.Date && now.TimeOfDay >= lateThreshold));
            bool HasCheckin(DashboardAttendanceRow row) => row.CheckinTime.HasValue;
            bool IsLate(DashboardAttendanceRow row) =>
                row.CheckinTime.HasValue && row.CheckinTime.Value.TimeOfDay > lateThreshold;
            static decimal Rate(int actual, int expected) => expected <= 0
                ? 0m
                : Math.Round(Math.Clamp(actual * 100m / expected, 0m, 100m), 1);
            int CountWorkdays(DateTime start, DateTime endExclusive, bool elapsedOnly) =>
                Enumerable.Range(0, Math.Max(0, (endExclusive.Date - start.Date).Days))
                    .Select(offset => start.Date.AddDays(offset))
                    .Count(day => IsWorkday(day) && (!elapsedOnly || IsElapsedWorkday(day)));

            var closedHours = monthRows.Sum(a => WorkHours(a.CheckinTime, a.CheckoutTime));
            var openRows = monthRows
                .Where(a => a.WorkDate.Date == today.Date && a.CheckinTime != null && a.CheckoutTime == null)
                .ToList();
            var openHours = openRows.Sum(a => WorkHours(a.CheckinTime, now));
            var monthHours = closedHours + openHours;
            var todayRows = monthRows.Where(a => a.WorkDate.Date == today.Date).ToList();
            var todayCheckinCount = todayRows.Count(HasCheckin);
            var todayCheckoutCount = todayRows.Count(a => a.CheckoutTime.HasValue);
            var todayOnTimeCount = todayRows.Count(a => HasCheckin(a) && !IsLate(a));
            var todayLateCount = todayRows.Count(IsLate);
            var todayExpectedCount = IsElapsedWorkday(today) ? activeEmployeeIds.Count : 0;
            var todayMissingCheckinCount = Math.Max(0, todayExpectedCount - todayCheckinCount);
            var monthExpectedEmployeeDays = activeEmployeeIds.Count * CountWorkdays(monthStart, nextMonthStart, true);
            var monthRecordedEmployeeDays = monthRows.Count(row => HasCheckin(row) && IsElapsedWorkday(row.WorkDate));
            var monthAttendanceRate = Rate(monthRecordedEmployeeDays, monthExpectedEmployeeDays);
            var monthPunctualityRows = monthRows
                .Where(row => HasCheckin(row) && IsWorkday(row.WorkDate) && row.WorkDate.Date <= today.Date)
                .ToList();
            var monthLateCount = monthPunctualityRows.Count(IsLate);
            var monthPunctualityRate = Rate(monthPunctualityRows.Count - monthLateCount, monthPunctualityRows.Count);
            var previousExpectedEmployeeDays = activeEmployeeIds.Count * CountWorkdays(previousMonthStart, monthStart, false);
            var previousRecordedEmployeeDays = previousRows.Count(row => HasCheckin(row) && IsWorkday(row.WorkDate));
            var previousAttendanceRate = Rate(previousRecordedEmployeeDays, previousExpectedEmployeeDays);
            var yearExpectedEmployeeDays = activeEmployeeIds.Count * CountWorkdays(yearStart, nextMonthStart, true);
            var yearRecordedEmployeeDays = yearRows.Count(row => HasCheckin(row) && IsElapsedWorkday(row.WorkDate));
            var yearAttendanceRate = Rate(yearRecordedEmployeeDays, yearExpectedEmployeeDays);
            var yearLateCount = yearRows.Count(row =>
                HasCheckin(row) && IsWorkday(row.WorkDate) && row.WorkDate.Date <= today.Date && IsLate(row));
            var monthIncompleteCheckoutCount = monthRows.Count(row =>
                row.WorkDate.Date < today.Date && row.CheckinTime.HasValue && !row.CheckoutTime.HasValue);
            var monthAttendanceDays = monthRows
                .Where(a => a.CheckinTime != null || a.CheckoutTime != null)
                .Select(a => a.WorkDate.Date)
                .Distinct()
                .Count();
            var averageHoursPerDay = monthAttendanceDays <= 0 ? 0m : Math.Round(monthHours / monthAttendanceDays, 1);
            var longShiftCount = monthRows.Count(a => RawWorkHours(a.CheckinTime, a.CheckoutTime) > 12m);
            var longDistanceCount = monthRows.Count(a => a.DistanceKm > 5m);
            var pendingCheckoutNames = openRows
                .OrderBy(a => a.CheckinTime)
                .Select(a => employeeName(a.EmpId))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .Take(4)
                .ToList();
            var presentByDay = canonicalRows
                .Where(HasCheckin)
                .GroupBy(row => row.WorkDate.Date)
                .ToDictionary(group => group.Key, group => group.Count());
            var lastSevenDays = Enumerable.Range(0, 7)
                .Select(offset => today.AddDays(offset - 6).Date)
                .ToList();
            var timeTrendDays = lastSevenDays
                .Select(day =>
                {
                    var isWorkday = IsWorkday(day);
                    var isElapsed = IsElapsedWorkday(day);
                    var presentCount = isElapsed && presentByDay.TryGetValue(day, out var count) ? count : 0;
                    var expectedCount = isElapsed ? activeEmployeeIds.Count : 0;
                    var attendanceRate = Rate(presentCount, expectedCount);
                    var percent = expectedCount <= 0 ? 4 : Math.Clamp((int)Math.Round(attendanceRate), 6, 100);
                    var tone = !isWorkday
                        ? "weekend"
                        : !isElapsed ? "future"
                        : attendanceRate >= policy.TargetPercent ? "high"
                        : attendanceRate >= 80m ? "medium"
                        : "low";

                    return new HomeDashboardTimeTrendDay
                    {
                        Label = day.ToString("dd/MM", CultureInfo.InvariantCulture),
                        AttendanceRate = attendanceRate,
                        PresentCount = presentCount,
                        ExpectedCount = expectedCount,
                        Percent = percent,
                        Tone = tone,
                        IsWorkday = isWorkday
                    };
                })
                .ToList();
            var heatmapDays = Enumerable.Range(0, Math.Max(0, (nextMonthStart.Date - monthStart.Date).Days))
                .Select(offset =>
                {
                    var day = monthStart.Date.AddDays(offset);
                    var isWorkday = IsWorkday(day);
                    var isElapsed = IsElapsedWorkday(day);
                    var presentCount = isElapsed && presentByDay.TryGetValue(day, out var count) ? count : 0;
                    var expectedCount = isElapsed ? activeEmployeeIds.Count : 0;
                    var attendanceRate = Rate(presentCount, expectedCount);
                    var tone = day > today || (day == today && !isElapsed)
                        ? "future"
                        : !isWorkday ? "weekend"
                        : attendanceRate >= policy.TargetPercent ? "high"
                        : attendanceRate >= 80m ? "medium"
                        : "low";
                    var label = !isWorkday
                        ? $"{day:dd/MM/yyyy} · วันหยุด"
                        : !isElapsed
                            ? $"{day:dd/MM/yyyy} · ยังไม่ถึงเวลาประเมิน"
                            : $"{day:dd/MM/yyyy} · เข้างาน {presentCount}/{expectedCount} คน ({attendanceRate:0.#}%)";

                    return new HomeDashboardTimeHeatDay
                    {
                        Day = day.Day,
                        Label = label,
                        AttendanceRate = attendanceRate,
                        PresentCount = presentCount,
                        ExpectedCount = expectedCount,
                        Tone = tone,
                        IsToday = day == today,
                        IsWorkday = isWorkday
                    };
                })
                .ToList();

            var trendClass = "neutral";
            var trendText = "ยังไม่มีข้อมูลเดือนก่อน";
            if (previousExpectedEmployeeDays > 0)
            {
                var diff = Math.Round(monthAttendanceRate - previousAttendanceRate, 1);
                trendClass = diff > 0 ? "positive" : diff < 0 ? "negative" : "neutral";
                trendText = Math.Abs(diff) < 0.1m
                    ? "ใกล้เคียงเดือนก่อน"
                    : $"{(diff > 0 ? "สูงขึ้น" : "ลดลง")} {Math.Abs(diff):0.#} จุดจากเดือนก่อน";
            }

            return new DashboardTimeSummary
            {
                MonthWorkHours = Math.Round(monthHours, 1),
                ClosedWorkHours = Math.Round(closedHours, 1),
                OpenWorkHours = Math.Round(openHours, 1),
                PendingCheckoutCount = openRows.Count,
                TodayCheckinCount = todayCheckinCount,
                TodayCheckoutCount = todayCheckoutCount,
                TodayMissingCheckinCount = todayMissingCheckinCount,
                MonthAttendanceDays = monthAttendanceDays,
                AverageHoursPerDay = averageHoursPerDay,
                LongShiftCount = longShiftCount,
                LongDistanceCount = longDistanceCount,
                PendingCheckoutNames = pendingCheckoutNames,
                TimeTargetHours = policy.TargetPercent,
                TimeTargetProgressPercent = monthAttendanceRate,
                ActiveEmployeeCount = activeEmployeeIds.Count,
                TodayOnTimeCount = todayOnTimeCount,
                TodayLateCount = todayLateCount,
                MonthLateCount = monthLateCount,
                MonthIncompleteCheckoutCount = monthIncompleteCheckoutCount,
                YearLateCount = yearLateCount,
                MonthRecordedEmployeeDays = monthRecordedEmployeeDays,
                MonthExpectedEmployeeDays = monthExpectedEmployeeDays,
                TodayAttendanceRate = Rate(todayCheckinCount, todayExpectedCount),
                MonthAttendanceRate = monthAttendanceRate,
                MonthPunctualityRate = monthPunctualityRate,
                YearAttendanceRate = yearAttendanceRate,
                AttendanceTargetPercent = policy.TargetPercent,
                AttendancePolicyText = $"ตรงเวลาไม่เกิน {DateTime.Today.Add(lateThreshold):HH:mm} น. · วันทำการ จ.-ศ.",
                TimeTrendDays = timeTrendDays,
                TimeHeatmapDays = heatmapDays,
                TrendClass = trendClass,
                TrendText = trendText
            };
        }

        private static List<HomeDashboardWorkload> BuildTeamWorkload(
            IReadOnlyList<DashboardAssignRow> assigns,
            IEnumerable<int> employeeIds,
            Func<int?, string> employeeName,
            Func<int?, string> employeeAvatar)
        {
            var activeAssigns = assigns
                .Where(a => Norm(a.WorkStatus) != "DONE")
                .GroupBy(a => a.EmpId)
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = employeeIds
                .Distinct()
                .Select(empId =>
                {
                    activeAssigns.TryGetValue(empId, out var assignCount);
                    return new
                    {
                        EmpId = empId,
                        Count = assignCount,
                        AvatarPath = employeeAvatar(empId)
                    };
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => employeeName(x.EmpId))
                .ToList();

            var max = rows.Select(x => x.Count).DefaultIfEmpty(0).Max();
            return rows
                .Select((row, index) => new HomeDashboardWorkload
                {
                    Name = employeeName(row.EmpId),
                    ActiveTaskCount = row.Count,
                    Value = max <= 0 || row.Count == 0
                        ? 0
                        : (int)Math.Round(row.Count * 100m / max),
                    Color = ColorByIndex(index),
                    AvatarPath = row.AvatarPath
                })
                .ToList();
        }

        private static List<ProjectTaskOverviewMember> BuildDashboardTaskOverview(
            IReadOnlyList<DashboardAssignRow> assigns,
            IReadOnlyList<DashboardPhaseRow> phases,
            IReadOnlyList<DashboardIssueRow> issues,
            IReadOnlyList<DashboardSupportOrderRow> supportOrders,
            IReadOnlyDictionary<int, int> fieldServiceCounts,
            Func<int?, string> employeeName,
            Func<int?, string> employeeAvatar,
            DateTime today)
        {
            var phaseById = phases
                .GroupBy(p => p.PhaseId)
                .ToDictionary(g => g.Key, g => g.First());
            var assignGroups = assigns
                .GroupBy(a => a.EmpId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var openIssueCounts = issues
                .Where(i => Norm(i.IssueStatus) == "OPEN")
                .GroupBy(i => i.EmpId)
                .ToDictionary(g => g.Key, g => g.Count());
            var openSupportCounts = supportOrders
                .Where(o => o.AssignTo.HasValue && Norm(o.Status) == "OPEN")
                .GroupBy(o => o.AssignTo!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            var memberIds = assignGroups.Keys
                .Union(openIssueCounts.Keys)
                .Union(openSupportCounts.Keys)
                .Union(fieldServiceCounts.Keys);

            var rows = memberIds
                .Select(empId =>
                {
                    assignGroups.TryGetValue(empId, out var memberAssigns);
                    memberAssigns ??= new List<DashboardAssignRow>();

                    var done = memberAssigns.Count(IsDashboardAssignDone);
                    var delay = memberAssigns.Count(assign => IsDashboardAssignDelayed(assign, phaseById, today));
                    var inProgress = Math.Max(0, memberAssigns.Count - done - delay);
                    openIssueCounts.TryGetValue(empId, out var openIssues);
                    openSupportCounts.TryGetValue(empId, out var openSupport);
                    fieldServiceCounts.TryGetValue(empId, out var fieldService);
                    var total = memberAssigns.Count + openIssues + openSupport + fieldService;

                    return new ProjectTaskOverviewMember
                    {
                        EmpId = empId,
                        Name = employeeName(empId),
                        AvatarPath = employeeAvatar(empId),
                        DoneCount = done,
                        InProgressCount = inProgress,
                        DelayCount = delay,
                        OpenIssueCount = openIssues,
                        OpenSupportCount = openSupport,
                        FieldServiceCount = fieldService,
                        TotalCount = total
                    };
                })
                .Where(x => x.TotalCount > 0)
                .OrderByDescending(x => x.TotalCount)
                .ThenBy(x => x.Name)
                .ToList();

            var maxTotal = Math.Max(1, rows.Select(x => x.TotalCount).DefaultIfEmpty(0).Max());

            foreach (var row in rows)
            {
                row.TotalHeightPercent = Math.Clamp((int)Math.Round(row.TotalCount * 100m / maxTotal), 24, 100);
                row.DoneHeightPercent = Percent(row.DoneCount, row.TotalCount);
                row.InProgressHeightPercent = Percent(row.InProgressCount, row.TotalCount);
                row.DelayHeightPercent = Percent(row.DelayCount, row.TotalCount);
                row.OpenIssueHeightPercent = Percent(row.OpenIssueCount, row.TotalCount);
                row.OpenSupportHeightPercent = Percent(row.OpenSupportCount, row.TotalCount);
                row.FieldServiceHeightPercent = Percent(row.FieldServiceCount, row.TotalCount);
            }

            return rows;
        }

        private static bool IsDashboardAssignDone(DashboardAssignRow assign)
        {
            return Norm(assign.WorkStatus) == "DONE";
        }

        private static bool IsDashboardAssignDelayed(
            DashboardAssignRow assign,
            IReadOnlyDictionary<int, DashboardPhaseRow> phaseById,
            DateTime today)
        {
            var dueDate = assign.PlanEnd
                ?? (phaseById.TryGetValue(assign.PhaseId, out var phase) ? phase.PlanEnd : null);

            return !IsDashboardAssignDone(assign)
                && dueDate.HasValue
                && dueDate.Value.Date < today;
        }

        private static int Percent(int count, int total)
        {
            return total <= 0 ? 0 : (int)Math.Round(count * 100m / total);
        }

        private static List<HomeDashboardOpenWorkItem> BuildOpenIssueSupportItems(
            IReadOnlyList<DashboardAssignRow> assigns,
            IReadOnlyList<DashboardIssueRow> issues,
            IReadOnlyList<DashboardSupportOrderRow> supportOrders,
            IReadOnlyList<DashboardFollowupRow> followups,
            IReadOnlyDictionary<int, DashboardPhaseRow> phaseById,
            Func<int?, string> projectName,
            Func<int?, string> employeeName,
            Func<int?, int?> projectBaEmpId,
            int? currentEmpId,
            bool isAdmin,
            CultureInfo culture)
        {
            var rows = new List<(DateTime? dueDate, DateTime createdAt, HomeDashboardOpenWorkItem item)>();

            rows.AddRange(assigns
                .Select(assign =>
                {
                    phaseById.TryGetValue(assign.PhaseId, out var phase);
                    var projectId = phase?.ProjectId;
                    var dueDate = assign.PlanEnd ?? phase?.PlanEnd;
                    var title = !string.IsNullOrWhiteSpace(assign.Role)
                        ? assign.Role
                        : !string.IsNullOrWhiteSpace(phase?.PhaseName)
                            ? phase.PhaseName
                            : $"PhaseAssign #{assign.AssignId}";

                    return (
                        dueDate,
                        createdAt: assign.CreatedAt ?? phase?.CreatedAt ?? DateTime.MinValue,
                        item: new HomeDashboardOpenWorkItem
                        {
                            Type = "PhaseAssign",
                            Title = title,
                            ProjectName = projectName(projectId),
                            OwnerName = employeeName(assign.EmpId),
                            DueText = dueDate.HasValue ? FormatDashboardDate(dueDate, culture) : "ยังไม่กำหนด",
                            Url = projectId.HasValue
                                ? $"/PhaseAssigns/Index?projectId={projectId.Value}&empId={assign.EmpId}"
                                : $"/PhaseAssigns/Index?empId={assign.EmpId}",
                            Color = "blue"
                        });
                }));

            rows.AddRange(issues
                .Select(i => (
                    dueDate: i.EndDate,
                    createdAt: i.CreatedAt,
                    item: new HomeDashboardOpenWorkItem
                    {
                        Type = "ProjectIssue",
                        Title = string.IsNullOrWhiteSpace(i.IssueName) ? $"Issue #{i.IssueId}" : i.IssueName,
                        ProjectName = projectName(i.ProjectId),
                        OwnerName = employeeName(i.EmpId),
                        DueText = i.EndDate.HasValue ? FormatDashboardDate(i.EndDate, culture) : "ยังไม่กำหนด",
                        Url = ShouldUseBaOpenWorkRoute(projectBaEmpId(i.ProjectId), currentEmpId, isAdmin)
                            ? $"/ProjectIssues/Details/{i.IssueId}"
                            : $"/ProjectIssues/DevDetails/{i.IssueId}",
                        Color = IsHighPriority(i.IssuePriority) ? "pink" : "orange"
                    })));

            rows.AddRange(supportOrders
                .Select(o => (
                    dueDate: o.EndDate,
                    createdAt: o.CreatedAt ?? DateTime.MinValue,
                    item: new HomeDashboardOpenWorkItem
                    {
                        Type = "SupportOrder",
                        Title = string.IsNullOrWhiteSpace(o.OrderTitle) ? $"Support #{o.OrderId}" : o.OrderTitle,
                        ProjectName = projectName(o.ProjectId),
                        OwnerName = employeeName(o.AssignTo),
                        DueText = o.EndDate.HasValue ? FormatDashboardDate(o.EndDate, culture) : "ยังไม่กำหนด",
                        Url = ShouldUseBaOpenWorkRoute(projectBaEmpId(o.ProjectId), currentEmpId, isAdmin)
                            ? $"/SupportOrders/Details/{o.OrderId}"
                            : $"/SupportOrdersDev/Details/{o.OrderId}",
                        Color = IsHighPriority(o.Priority) ? "pink" : "cyan"
                    })));

            rows.AddRange(followups
                .Select(followup => (
                    dueDate: followup.NextFollowupDate,
                    createdAt: followup.CreatedAt,
                    item: new HomeDashboardOpenWorkItem
                    {
                        Type = "Followup",
                        Title = string.IsNullOrWhiteSpace(followup.TaskTitle) ? $"Followup #{followup.FollowupId}" : followup.TaskTitle,
                        ProjectName = projectName(followup.ProjectId),
                        OwnerName = employeeName(followup.OwnerEmpId ?? followup.CreatedByEmpId),
                        DueText = followup.NextFollowupDate.HasValue ? FormatDashboardDate(followup.NextFollowupDate, culture) : "ยังไม่กำหนด",
                        Url = $"/Followups/Details/{followup.FollowupId}",
                        Color = "green"
                    })));

            return rows
                .OrderBy(row => OpenWorkSeverityOrder(row.dueDate, DateTime.Today))
                .ThenBy(row => row.dueDate ?? DateTime.MaxValue)
                .ThenByDescending(row => row.createdAt)
                .Take(10)
                .Select(row => row.item)
                .ToList();
        }

        private static int OpenWorkSeverityOrder(DateTime? dueDate, DateTime today)
        {
            if (!dueDate.HasValue)
                return 2;

            var date = dueDate.Value.Date;
            if (date < today)
                return 0;

            return date <= today.AddDays(7) ? 1 : 2;
        }

        private static bool CanSeeOpenIssue(
            DashboardIssueRow issue,
            Func<int?, int?> projectBaEmpId,
            int? currentEmpId,
            bool isAdmin)
        {
            return currentEmpId.HasValue &&
                (issue.EmpId == currentEmpId.Value ||
                    issue.CreatedBy == currentEmpId.Value ||
                    projectBaEmpId(issue.ProjectId) == currentEmpId.Value);
        }

        private static bool CanSeeOpenSupport(
            DashboardSupportOrderRow supportOrder,
            Func<int?, int?> projectBaEmpId,
            int? currentEmpId,
            bool isAdmin)
        {
            return currentEmpId.HasValue &&
                (supportOrder.AssignTo == currentEmpId.Value ||
                    supportOrder.CreatedBy == currentEmpId.Value ||
                    projectBaEmpId(supportOrder.ProjectId) == currentEmpId.Value);
        }

        private static bool CanSeeOpenAssign(
            DashboardAssignRow assign,
            IReadOnlyDictionary<int, DashboardPhaseRow> phaseById,
            Func<int?, int?> projectBaEmpId,
            int? currentEmpId,
            bool isAdmin)
        {
            if (!currentEmpId.HasValue)
                return false;

            var projectId = phaseById.TryGetValue(assign.PhaseId, out var phase)
                ? phase.ProjectId
                : (int?)null;

            return assign.EmpId == currentEmpId.Value ||
                projectBaEmpId(projectId) == currentEmpId.Value;
        }

        private static bool CanSeeOpenFollowup(
            DashboardFollowupRow followup,
            Func<int?, int?> projectBaEmpId,
            int? currentEmpId,
            bool isAdmin)
        {
            return currentEmpId.HasValue &&
                (followup.OwnerEmpId == currentEmpId.Value ||
                    followup.CreatedByEmpId == currentEmpId.Value ||
                    projectBaEmpId(followup.ProjectId) == currentEmpId.Value);
        }

        private static bool ShouldUseBaOpenWorkRoute(int? projectBaEmpId, int? currentEmpId, bool isAdmin)
        {
            return isAdmin || (currentEmpId.HasValue && projectBaEmpId == currentEmpId.Value);
        }

        private static string RelativeTimeThai(DateTime value, DateTime now)
        {
            var span = now - value;
            if (span.TotalSeconds < 60) return "เมื่อสักครู่";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} นาทีที่แล้ว";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} ชั่วโมงที่แล้ว";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays} วันที่แล้ว";
            return value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private static decimal WorkHours(DateTime? checkin, DateTime? checkout)
        {
            if (checkin == null || checkout == null || checkout <= checkin) return 0m;
            return Math.Min((decimal)(checkout.Value - checkin.Value).TotalHours, 16m);
        }

        private static decimal WorkHours(DateTime? checkin, DateTime checkout)
        {
            if (checkin == null || checkout <= checkin) return 0m;
            return Math.Min((decimal)(checkout - checkin.Value).TotalHours, 16m);
        }

        private static decimal RawWorkHours(DateTime? checkin, DateTime? checkout)
        {
            if (checkin == null || checkout == null || checkout <= checkin) return 0m;
            return (decimal)(checkout.Value - checkin.Value).TotalHours;
        }

        private static DateTime? PhaseBucketDate(DashboardPhaseRow phase)
        {
            return IsPhaseDone(phase.PhaseStatus)
                ? phase.SubmittedDate ?? phase.PlanEnd ?? phase.PlanStart ?? phase.PeriodEndDate ?? phase.CreatedAt
                : phase.PlanStart ?? phase.PlanEnd ?? phase.PeriodEndDate ?? phase.CreatedAt;
        }

        private static DateTime? AssignPhaseBucketDate(DashboardAssignRow assign, DashboardPhaseRow phase)
        {
            return assign.PlanEnd ?? assign.PlanStart ?? PhaseBucketDate(phase);
        }

        private static int HeightFromValue(int value, int maxValue)
        {
            if (value <= 0 || maxValue <= 0) return 0;
            return Math.Clamp((int)Math.Round(value * 100m / maxValue), 12, 100);
        }

        private static int NiceChartMax(int maxValue)
        {
            if (maxValue <= 0) return 4;
            if (maxValue <= 4) return 4;
            if (maxValue <= 10) return 10;
            if (maxValue <= 20) return 20;
            if (maxValue <= 50) return 50;
            if (maxValue <= 100) return (int)Math.Ceiling(maxValue / 10m) * 10;
            if (maxValue <= 500) return (int)Math.Ceiling(maxValue / 50m) * 50;
            return (int)Math.Ceiling(maxValue / 100m) * 100;
        }

        private static bool IsPhaseDone(string? status)
        {
            var normalized = Norm(status);
            return normalized is "ส่งงวดงานแล้ว" or "อนุมัติจ่ายเงินแล้ว" or "DONE";
        }

        private static bool IsIssueResolved(DashboardIssueRow issue)
        {
            var issueStatus = Norm(issue.IssueStatus);
            return issueStatus is "PASS" or "REJECT" or "DONE" or "CLOSED" or "RESOLVED";
        }

        private static bool IsIssueInProgress(DashboardIssueRow issue)
        {
            var devStatus = Norm(issue.DevStatus);
            return devStatus is "WIP" or "DOING" or "IN_PROGRESS";
        }

        private static bool IsHighPriority(string? priority)
        {
            return Norm(priority) is "HIGH" or "URGENT" or "CRITICAL";
        }

        private static bool IsOpenWorkStatus(string? status)
        {
            return Norm(status) is "OPEN" or "FAIL";
        }

        private static bool IsSupportOrderClosed(string? status, string? devStatus)
        {
            return Norm(status) is "PASS" or "REJECT" or "DONE" or "CLOSED" or "RESOLVED";
        }

        private static bool IsFollowupClosed(string? status)
        {
            return Norm(status) is "DONE" or "ACK" or "CLOSED" or "RESOLVED";
        }

        private static bool IsSupportOrderInProgress(DashboardSupportOrderRow order)
        {
            return Norm(order.DevStatus) is "WIP" or "IN_PROGRESS" or "DOING";
        }

        private static string SupportOrderActivityColor(string? status, string? devStatus)
        {
            var normalizedStatus = Norm(status);
            var normalizedDevStatus = Norm(devStatus);

            if (normalizedStatus is "PASS" or "REJECT" or "DONE" or "CLOSED" or "RESOLVED")
            {
                return "green";
            }

            if (normalizedDevStatus == "FIXED")
            {
                return "cyan";
            }

            if (normalizedDevStatus is "WIP" or "IN_PROGRESS")
            {
                return "orange";
            }

            return "cyan";
        }

        private static string ProjectActivityColor(string? status)
        {
            return Norm(status) switch
            {
                "DONE" => "green",
                "IN_PROGRESS" => "blue",
                "PLAN" => "orange",
                _ => "blue"
            };
        }

        private static string PhaseActivityColor(string? status, string? phaseType)
        {
            if (IsPhaseDone(status)) return "green";
            if (Norm(status) == "กำลังดำเนินการ") return "orange";
            return Norm(phaseType) == "SUPPORT" ? "cyan" : "blue";
        }

        private static string PhaseTypeLabel(string phaseType)
        {
            return phaseType switch
            {
                "MAIN" => "Main Phase",
                "SUPPORT" => "Support Phase",
                _ => phaseType
            };
        }

        private static string FormatMeetingTime(TimeSpan startTime)
        {
            var label = startTime.Hours < 12 ? "AM" : "PM";
            return $"{startTime.Hours:D2}:{startTime.Minutes:D2}<br />{label}";
        }

        private static string ResolveProfileImagePath(string? profileImagePath)
            => ProfileImagePathResolver.Normalize(profileImagePath);

        private async Task FillMissingEmployeeProfileImagesAsync(List<DashboardEmployeeRow> employees)
        {
            var rowsMissingProfile = employees
                .Where(x => string.IsNullOrWhiteSpace(x.ProfileImagePath))
                .ToList();
            if (rowsMissingProfile.Count == 0)
            {
                return;
            }

            var employeeIds = rowsMissingProfile.Select(x => x.EmpId).ToHashSet();
            var linkedUserIds = rowsMissingProfile
                .Where(x => x.LoginUserId.HasValue)
                .Select(x => x.LoginUserId!.Value)
                .ToHashSet();
            var profileUsers = await _context.LoginUsers
                .AsNoTracking()
                .Where(x => linkedUserIds.Contains(x.UserId)
                    || (x.EmpId.HasValue && employeeIds.Contains(x.EmpId.Value)))
                .Select(x => new { x.UserId, x.EmpId, x.ProfileImagePath })
                .ToListAsync();

            foreach (var employee in rowsMissingProfile)
            {
                employee.ProfileImagePath = profileUsers
                    .FirstOrDefault(x => employee.LoginUserId.HasValue
                        && x.UserId == employee.LoginUserId.Value
                        && !string.IsNullOrWhiteSpace(x.ProfileImagePath))
                    ?.ProfileImagePath
                    ?? profileUsers.FirstOrDefault(x => x.EmpId == employee.EmpId
                        && !string.IsNullOrWhiteSpace(x.ProfileImagePath))
                    ?.ProfileImagePath;
            }
        }

        private static string Norm(string? value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static string NormalizeDashboardDinoName(string? value)
        {
            var name = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return "Dino";

            return name.Length <= 24 ? name : name[..24];
        }

        private static string ColorByIndex(int index)
        {
            string[] colors = { "green", "blue", "orange", "purple", "pink", "cyan", "lime" };
            return colors[Math.Abs(index) % colors.Length];
        }

        private static string ChartColorCss(string color)
        {
            return color switch
            {
                "green" or "success" => "var(--pt-chart-success, #10d58f)",
                "blue" or "primary" => "var(--pt-chart-primary, #1688f5)",
                "orange" or "warning" => "var(--pt-chart-warning, #fb9a13)",
                "pink" or "danger" => "var(--pt-chart-danger, #ff2d62)",
                "purple" or "violet" => "var(--pt-chart-alt, #8b4df4)",
                "cyan" or "info" => "var(--pt-chart-info, #0ad0c8)",
                "lime" => "#8cff3f",
                "dark" or "secondary" or "muted" => "var(--pt-chart-muted, #64748b)",
                _ => "var(--pt-chart-primary, #1688f5)"
            };
        }

        private static string CssPercent(decimal value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string CssNumber(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatDashboardDate(DateTime? date, CultureInfo culture)
        {
            return date?.ToString("dd MMM yyyy", culture) ?? "-";
        }

        private static int ProjectOverviewSort(string? status)
        {
            return Norm(status).Replace(" ", "_").Replace("-", "_") switch
            {
                "IN_PROGRESS" => 1,
                "PLAN" => 2,
                "DONE" => 3,
                _ => 4
            };
        }

        private static string ProjectStatusText(string? status)
        {
            return Norm(status) switch
            {
                "DONE" => "Completed",
                "IN_PROGRESS" => "In Progress",
                "PLAN" => "Pending",
                _ => string.IsNullOrWhiteSpace(status) ? "-" : status.Trim()
            };
        }

        private sealed class DashboardProjectRow
        {
            public int ProjectId { get; set; }
            public string ProjectName { get; set; } = "";
            public string? CoopName { get; set; }
            public int? DepartmentId { get; set; }
            public string? DepartmentName { get; set; }
            public int? PmEmpId { get; set; }
            public string ProjectDisplayName =>
                string.IsNullOrWhiteSpace(CoopName)
                    ? ProjectName
                    : $"{CoopName} - {ProjectName}";
            public int? BaEmpId { get; set; }
            public string? Status { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public DateTime? CreatedAt { get; set; }
            public int? EntryId { get; set; }
        }

        private sealed class DashboardSearchResult
        {
            public string Type { get; set; } = "";
            public string Title { get; set; } = "";
            public string Detail { get; set; } = "";
            public string Url { get; set; } = "";
            public string Color { get; set; } = "blue";
        }

        private sealed class DashboardPhaseRow
        {
            public int PhaseId { get; set; }
            public int ProjectId { get; set; }
            public string PhaseName { get; set; } = "";
            public string? PhaseType { get; set; }
            public string? PhaseStatus { get; set; }
            public DateTime? PlanStart { get; set; }
            public DateTime? PlanEnd { get; set; }
            public DateTime? SubmittedDate { get; set; }
            public DateTime? PeriodEndDate { get; set; }
            public DateTime? CreatedAt { get; set; }
            public int? EntryId { get; set; }
        }

        private sealed class DashboardAssignRow
        {
            public int AssignId { get; set; }
            public int PhaseId { get; set; }
            public int EmpId { get; set; }
            public string? Role { get; set; }
            public string? WorkStatus { get; set; }
            public DateTime? PlanStart { get; set; }
            public DateTime? PlanEnd { get; set; }
            public DateTime? CreatedAt { get; set; }
            public int? EntryId { get; set; }
        }

        private sealed class DashboardIssueRow
        {
            public int IssueId { get; set; }
            public int ProjectId { get; set; }
            public string IssueName { get; set; } = "";
            public string? IssueStatus { get; set; }
            public string? DevStatus { get; set; }
            public string? IssuePriority { get; set; }
            public bool IsReopen { get; set; }
            public int ReopenCount { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public DateTime CreatedAt { get; set; }
            public int? CreatedBy { get; set; }
            public int EmpId { get; set; }
        }

        private sealed class DashboardFollowupRow
        {
            public int FollowupId { get; set; }
            public int? ProjectId { get; set; }
            public string TaskTitle { get; set; } = "";
            public int? OwnerEmpId { get; set; }
            public int? CreatedByEmpId { get; set; }
            public string? Status { get; set; }
            public DateTime? NextFollowupDate { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private sealed class DashboardSupportOrderRow
        {
            public int OrderId { get; set; }
            public int ProjectId { get; set; }
            public string? OrderTitle { get; set; }
            public string? Status { get; set; }
            public string? DevStatus { get; set; }
            public string? Priority { get; set; }
            public int? CreatedBy { get; set; }
            public int? AssignTo { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public DateTime? CreatedAt { get; set; }
        }

        private sealed class DashboardRequirementCardRow
        {
            public int CardId { get; set; }
            public string Title { get; set; } = "";
            public string? ColumnName { get; set; }
            public int? CreatedByUserId { get; set; }
            public string? CreatedByUsername { get; set; }
            public string? CreatedByUserProfileImagePath { get; set; }
            public int? CreatedByEmpId { get; set; }
            public string? CreatedByEmployeeName { get; set; }
            public string? CreatedByEmployeeProfileImagePath { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }

        private sealed class DashboardEmployeeRow
        {
            public int EmpId { get; set; }
            public string EmpName { get; set; } = "";
            public string? Status { get; set; }
            public int? LoginUserId { get; set; }
            public string? ProfileImagePath { get; set; }
        }

        private sealed class DashboardMeetingRow
        {
            public int Id { get; set; }
            public int? ProjectId { get; set; }
            public int? GroupId { get; set; }
            public string? GroupName { get; set; }
            public string Title { get; set; } = "";
            public TimeSpan StartTime { get; set; }
            public string? Location { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public int? CreatedBy { get; set; }
            public string? ProjectName { get; set; }
        }

        private sealed class DashboardMeetingAttendeeRow
        {
            public int MeetingId { get; set; }
            public int AttendeeId { get; set; }
            public string? ProfileImagePath { get; set; }
        }

        private sealed class DashboardAttendanceRow
        {
            public int EmpId { get; set; }
            public DateTime WorkDate { get; set; }
            public DateTime? CheckinTime { get; set; }
            public DateTime? CheckoutTime { get; set; }
            public decimal DistanceKm { get; set; }
        }

        private async Task<int> GetOverdueRiskDaysAsync()
        {
            var value = await _context.SystemConfigs
                .AsNoTracking()
                .Where(x => x.ConfigKey == "OVERDUE_NOTIFICATION_RISK_DAYS")
                .Select(x => x.ConfigValue)
                .FirstOrDefaultAsync();

            if (int.TryParse(value, out var riskDays))
                return Math.Clamp(riskDays, 0, 30);

            return Math.Clamp(_configuration.GetValue<int?>("OVERDUE_NOTIFICATION_RISK_DAYS") ?? 7, 0, 30);
        }

        private async Task<DashboardAttendancePolicy> GetAttendancePolicyAsync()
        {
            string[] keys =
            {
                "ATTENDANCE_WORK_START_TIME",
                "ATTENDANCE_LATE_GRACE_MINUTES",
                "ATTENDANCE_TARGET_PERCENT"
            };
            var values = await _context.SystemConfigs
                .AsNoTracking()
                .Where(config => config.ConfigKey != null && keys.Contains(config.ConfigKey))
                .ToDictionaryAsync(config => config.ConfigKey!, config => config.ConfigValue);

            var workStart = TimeSpan.FromHours(9);
            if (values.TryGetValue("ATTENDANCE_WORK_START_TIME", out var workStartValue)
                && TimeSpan.TryParse(workStartValue, CultureInfo.InvariantCulture, out var parsedWorkStart))
            {
                workStart = parsedWorkStart;
            }

            var lateGraceMinutes = 0;
            if (values.TryGetValue("ATTENDANCE_LATE_GRACE_MINUTES", out var graceValue)
                && int.TryParse(graceValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGrace))
            {
                lateGraceMinutes = Math.Clamp(parsedGrace, 0, 180);
            }

            var targetPercent = 95m;
            if (values.TryGetValue("ATTENDANCE_TARGET_PERCENT", out var targetValue)
                && decimal.TryParse(targetValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTarget))
            {
                targetPercent = Math.Clamp(parsedTarget, 1m, 100m);
            }

            return new DashboardAttendancePolicy
            {
                WorkStart = workStart,
                LateGraceMinutes = lateGraceMinutes,
                TargetPercent = targetPercent
            };
        }

        private sealed class DashboardTimeSummary
        {
            public decimal MonthWorkHours { get; set; }
            public decimal ClosedWorkHours { get; set; }
            public decimal OpenWorkHours { get; set; }
            public int PendingCheckoutCount { get; set; }
            public int TodayCheckinCount { get; set; }
            public int TodayCheckoutCount { get; set; }
            public int TodayMissingCheckinCount { get; set; }
            public int MonthAttendanceDays { get; set; }
            public decimal AverageHoursPerDay { get; set; }
            public int LongShiftCount { get; set; }
            public int LongDistanceCount { get; set; }
            public List<string> PendingCheckoutNames { get; set; } = new();
            public decimal TimeTargetHours { get; set; }
            public decimal TimeTargetProgressPercent { get; set; }
            public int ActiveEmployeeCount { get; set; }
            public int TodayOnTimeCount { get; set; }
            public int TodayLateCount { get; set; }
            public int MonthLateCount { get; set; }
            public int MonthIncompleteCheckoutCount { get; set; }
            public int YearLateCount { get; set; }
            public int MonthRecordedEmployeeDays { get; set; }
            public int MonthExpectedEmployeeDays { get; set; }
            public decimal TodayAttendanceRate { get; set; }
            public decimal MonthAttendanceRate { get; set; }
            public decimal MonthPunctualityRate { get; set; }
            public decimal YearAttendanceRate { get; set; }
            public decimal AttendanceTargetPercent { get; set; }
            public string AttendancePolicyText { get; set; } = "";
            public List<HomeDashboardTimeTrendDay> TimeTrendDays { get; set; } = new();
            public List<HomeDashboardTimeHeatDay> TimeHeatmapDays { get; set; } = new();
            public string TrendText { get; set; } = "";
            public string TrendClass { get; set; } = "neutral";
        }

        private sealed class DashboardAttendancePolicy
        {
            public TimeSpan WorkStart { get; set; } = TimeSpan.FromHours(9);
            public int LateGraceMinutes { get; set; }
            public decimal TargetPercent { get; set; } = 95m;
        }

        private sealed class LineOverdueOverviewResult
        {
            public int Total { get; set; }
            public int ProjectCount { get; set; }
            public List<HomeDashboardMetric> Metrics { get; set; } = new();
            public int LinkedCount { get; set; }
            public int MissingLineCount { get; set; }
        }

        private sealed class LineOverdueOverviewItem
        {
            public string Severity { get; set; } = "WARNING";
            public HashSet<int> RecipientEmpIds { get; set; } = new();
        }

        // ===============================
        // Logout
        // ===============================
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Auth");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}
