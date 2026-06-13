using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class HomeController : Controller
    {
        private const string DefaultProfileImagePath = "/images/Profile/profile.png";

        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        [RequireMenu("Home.Index")]
        public async Task<IActionResult> Index()
        {
            // ===============================
            // ส่งข้อมูลที่จำเป็นให้ View
            // ===============================
            ViewBag.Username = HttpContext.Session.GetString("Username") ?? "-";

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

            var dashboard = await BuildHomeDashboardAsync(username ?? "-", today, currentEmpId, isAdmin);

            var unreadNotificationCount = 0;
            var unreadMailboxCount = 0;

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

                unreadMailboxCount = await _context.MailboxRecipients
                    .AsNoTracking()
                    .Where(mail => mail.RecipientUserId == currentUserId.Value && !mail.IsRead && !mail.IsDeleted)
                    .CountAsync();
            }

            ViewBag.UnreadNotificationCount = unreadNotificationCount;
            ViewBag.UnreadMailboxCount = unreadMailboxCount;
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

        private async Task<HomeDashboardViewModel> BuildHomeDashboardAsync(string username, DateTime today, int? currentEmpId, bool isAdmin)
        {
            var th = new CultureInfo("th-TH");
            var now = DateTime.Now;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);
            var previousMonthStart = monthStart.AddMonths(-1);

            var projects = await _context.Projects
                .AsNoTracking()
                .Select(p => new DashboardProjectRow
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName,
                    CoopName = p.Coop != null ? p.Coop.CoopName : null,
                    BaEmpId = p.BaEmpId,
                    Status = p.Status,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
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
                    ProfileImagePath = e.LoginUser != null ? e.LoginUser.ProfileImagePath : null
                })
                .ToListAsync();

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

            var currentAndPreviousMonthAttendance = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.WorkDate >= previousMonthStart && a.WorkDate < nextMonthStart)
                .Select(a => new DashboardAttendanceRow
                {
                    EmpId = a.EmpId,
                    WorkDate = a.WorkDate,
                    CheckinTime = a.CheckinTime,
                    CheckoutTime = a.CheckoutTime,
                    DistanceKm = a.DistanceKm ?? 0m
                })
                .ToListAsync();

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

            var completedProjectCount = projects.Count(p => Norm(p.Status) == "DONE");
            var inProgressProjectCount = projects.Count(p => Norm(p.Status) == "IN_PROGRESS");
            var pendingProjectCount = projects.Count(p => Norm(p.Status) == "PLAN");

            var projectStatusMetrics = new List<HomeDashboardMetric>
            {
                CreateMetric("Completed", completedProjectCount, projects.Count, "green"),
                CreateMetric("In Progress", inProgressProjectCount, projects.Count, "blue"),
                CreateMetric("Pending", pendingProjectCount, projects.Count, "orange")
            };

            var issueMetrics = new List<HomeDashboardMetric>
            {
                CreateMetric("OPEN", issues.Count(i => Norm(i.IssueStatus) == "OPEN"), issues.Count, "warning"),
                CreateMetric("WIP", issues.Count(i => Norm(i.IssueStatus) == "WIP"), issues.Count, "info"),
                CreateMetric("FIXED", issues.Count(i => Norm(i.IssueStatus) == "FIXED"), issues.Count, "cyan"),
                CreateMetric("FAIL", issues.Count(i => Norm(i.IssueStatus) == "FAIL"), issues.Count, "danger"),
                CreateMetric("PASS", issues.Count(i => Norm(i.IssueStatus) == "PASS"), issues.Count, "lime"),
                CreateMetric("REJECT", issues.Count(i => Norm(i.IssueStatus) == "REJECT"), issues.Count, "violet")
            };

            var supportMetrics = new List<HomeDashboardMetric>
            {
                CreateMetric("OPEN", supportOrders.Count(o => Norm(o.Status) == "OPEN"), supportOrders.Count, "warning"),
                CreateMetric("WIP", supportOrders.Count(o => Norm(o.Status) == "WIP"), supportOrders.Count, "info"),
                CreateMetric("FIXED", supportOrders.Count(o => Norm(o.Status) == "FIXED"), supportOrders.Count, "cyan"),
                CreateMetric("FAIL", supportOrders.Count(o => Norm(o.Status) == "FAIL"), supportOrders.Count, "danger"),
                CreateMetric("PASS", supportOrders.Count(o => Norm(o.Status) == "PASS"), supportOrders.Count, "lime"),
                CreateMetric("REJECT", supportOrders.Count(o => Norm(o.Status) == "REJECT"), supportOrders.Count, "violet")
            };

            var phaseTypeRows = phases
                .GroupBy(p => string.IsNullOrWhiteSpace(p.PhaseType) ? "OTHERS" : Norm(p.PhaseType))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Take(5)
                .Select((g, index) => CreateMetric(PhaseTypeLabel(g.Key), g.Count(), phases.Count, ColorByIndex(index)))
                .ToList();

            var monthlyPoints = BuildMonthlyProjectPoints(projects, today.Year, th);
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

            var projectOverviewProjects = projects
                .OrderBy(p => ProjectOverviewSort(p.Status))
                .ThenBy(p => p.EndDate ?? DateTime.MaxValue)
                .ThenBy(p => p.ProjectDisplayName)
                .Select(p => new HomeDashboardProjectOverviewItem
                {
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectDisplayName,
                    StatusText = ProjectStatusText(p.Status),
                    StatusColor = ProjectActivityColor(p.Status),
                    StartText = FormatDashboardDate(p.StartDate, th),
                    EndText = FormatDashboardDate(p.EndDate, th)
                })
                .ToList();

            var topProjectProgress = projects
                .Select((project, index) =>
                {
                    var projectPhases = phases.Where(p => p.ProjectId == project.ProjectId).ToList();
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

            var meetingIds = todayMeetings.Select(m => m.Id).ToList();
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

            var meetingCards = todayMeetings
                .Take(5)
                .Select((meeting, index) => new HomeDashboardMeeting
                {
                    Id = meeting.Id,
                    Title = string.IsNullOrWhiteSpace(meeting.Title) ? "Untitled Meeting" : meeting.Title,
                    Detail = $"{(string.IsNullOrWhiteSpace(meeting.ProjectName) ? "ไม่ระบุโครงการ" : meeting.ProjectName)} · {(string.IsNullOrWhiteSpace(meeting.Location) ? "ไม่ระบุสถานที่" : meeting.Location)}",
                    TimeText = FormatMeetingTime(meeting.StartTime),
                    TimeColor = ColorByIndex(index + 3),
                    AttendeeCount = attendeeCounts.TryGetValue(meeting.Id, out var count) ? count : 0,
                    AvatarPath = meetingAvatarById.TryGetValue(meeting.Id, out var avatarPath)
                        ? avatarPath
                        : DefaultProfileImagePath
                })
                .ToList();

            var recentActivities = BuildRecentActivities(projects, phases, assigns, issues, followups, supportOrders, requirementCards, recentMeetings, EmployeeName, EmployeeAvatar, ProjectName, now);
            var yearlyTasks = BuildYearlyTasks(assigns, phases, today, out var yearlyTaskAxisMax);
            var watchProjects = BuildWatchProjects(projects, phases, assigns, issues, followups, supportOrders, EmployeeName, EmployeeAvatar, today);
            var timeSummary = BuildTimeSummary(currentAndPreviousMonthAttendance, employees, EmployeeName, monthStart, nextMonthStart, previousMonthStart, today, now);
            var teamWorkload = BuildTeamWorkload(assigns, EmployeeName, EmployeeAvatar);
            var projectBaById = projects.ToDictionary(project => project.ProjectId, project => project.BaEmpId);
            int? ProjectBaEmpId(int? projectId)
            {
                return projectId.HasValue && projectBaById.TryGetValue(projectId.Value, out var baEmpId)
                    ? baEmpId
                    : null;
            }

            var visibleOpenIssues = issues
                .Where(i => IsOpenWorkStatus(i.IssueStatus))
                .Where(i => CanSeeOpenIssue(i, ProjectBaEmpId, currentEmpId, isAdmin))
                .ToList();
            var visibleOpenSupportOrders = supportOrders
                .Where(o => IsOpenWorkStatus(o.Status))
                .Where(o => CanSeeOpenSupport(o, ProjectBaEmpId, currentEmpId, isAdmin))
                .ToList();
            var openIssueSupportCount = visibleOpenIssues.Count + visibleOpenSupportOrders.Count;
            var openIssueSupportItems = BuildOpenIssueSupportItems(
                visibleOpenIssues,
                visibleOpenSupportOrders,
                ProjectName,
                EmployeeName,
                ProjectBaEmpId,
                currentEmpId,
                isAdmin,
                th);

            var overduePlanPhaseCount = phases.Count(phase =>
                phase.PlanEnd.HasValue &&
                phase.PlanEnd.Value.Date < today &&
                !IsPhaseDone(phase.PhaseStatus));

            var overduePlanAssignCount = assigns.Count(assign =>
                assign.PlanEnd.HasValue &&
                assign.PlanEnd.Value.Date < today &&
                Norm(assign.WorkStatus) != "DONE");

            return new HomeDashboardViewModel
            {
                Username = username,
                TotalProjectCount = projects.Count,
                MeetingsTodayCount = todayMeetings.Count,
                OpenIssueCount = overduePlanPhaseCount,
                ActiveMemberCount = employees.Count(e => Norm(e.Status) == "ACTIVE"),
                OverdueTaskCount = overduePlanAssignCount,
                OpenIssuesNote = "เลยกำหนด Plan",
                OverdueTasksNote = "เลยกำหนด Plan",
                ProjectStatusMetrics = projectStatusMetrics,
                ProjectStatusDonut = BuildDonut(projectStatusMetrics),
                PhaseTypeMetrics = phaseTypeRows,
                PhaseTypeTotal = phases.Count,
                PhaseTypeDonut = BuildDonut(phaseTypeRows),
                IssueMetrics = issueMetrics,
                IssueTotal = issues.Count,
                IssueDonut = BuildDonut(issueMetrics),
                SupportMetrics = supportMetrics,
                SupportTotal = supportOrders.Count,
                SupportDonut = BuildDonut(supportMetrics),
                ProjectOverviewSeries = overviewSeries,
                ProjectOverviewMonths = monthlyPoints,
                ProjectOverviewTooltip = monthlyPoints.ElementAtOrDefault(Math.Clamp(today.Month - 1, 0, 11)),
                ProjectOverviewProjects = projectOverviewProjects,
                TopProjectProgress = topProjectProgress,
                RecentActivities = recentActivities,
                TodayMeetings = meetingCards,
                YearlyTasks = yearlyTasks,
                YearlyTaskAxisMax = yearlyTaskAxisMax,
                WatchProjects = watchProjects,
                TeamWorkload = teamWorkload,
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
                TimeTrackingDonut = BuildThreePartDonut(timeSummary.ClosedWorkHours, timeSummary.OpenWorkHours, timeSummary.TodayCheckinCount, "#10d58f", "#1688f5", "#22c7f5"),
                WorkHourTrendText = timeSummary.TrendText,
                WorkHourTrendClass = timeSummary.TrendClass
            };
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
                    ProfileImagePath = e.LoginUser != null ? e.LoginUser.ProfileImagePath : null
                })
                .ToListAsync();

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
                HexColor = ColorHex(color)
            };
        }

        private static string BuildDonut(IReadOnlyList<HomeDashboardMetric> metrics)
        {
            var nonZero = metrics.Where(m => m.Count > 0).ToList();
            if (nonZero.Count == 0) return "conic-gradient(#263450 0 100%)";

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
            if (total <= 0) return "conic-gradient(#263450 0 100%)";

            var split = Math.Round(first * 100m / total, 1);
            return $"conic-gradient({firstColor} 0 {CssPercent(split)}%, {secondColor} {CssPercent(split)}% 100%)";
        }

        private static string BuildThreePartDonut(decimal first, decimal second, decimal third, string firstColor, string secondColor, string thirdColor)
        {
            var total = first + second + third;
            if (total <= 0) return "conic-gradient(#263450 0 100%)";

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
                    var reopenedIssues = projectIssues.Count(i => !IsIssueResolved(i) && (i.IsReopen || i.ReopenCount > 0));
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

                    if (reopenedIssues > 0)
                    {
                        score += reopenedIssues * 2;
                        reasons.Add($"Reopen {reopenedIssues}");
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
            DateTime today,
            DateTime now)
        {
            var monthRows = attendances
                .Where(a => a.WorkDate >= monthStart && a.WorkDate < nextMonthStart)
                .ToList();
            var previousRows = attendances
                .Where(a => a.WorkDate >= previousMonthStart && a.WorkDate < monthStart)
                .ToList();

            var closedHours = monthRows.Sum(a => WorkHours(a.CheckinTime, a.CheckoutTime));
            var openRows = monthRows
                .Where(a => a.WorkDate.Date == today && a.CheckinTime != null && a.CheckoutTime == null)
                .ToList();
            var openHours = openRows.Sum(a => WorkHours(a.CheckinTime, now));
            var monthHours = closedHours + openHours;
            var previousHours = previousRows.Sum(a => WorkHours(a.CheckinTime, a.CheckoutTime));
            var todayRows = monthRows
                .Where(a => a.WorkDate.Date == today)
                .ToList();
            var activeEmployeeIds = employees
                .Where(e => Norm(e.Status) == "ACTIVE")
                .Select(e => e.EmpId)
                .ToHashSet();
            var todayCheckinCount = todayRows
                .Count(a => activeEmployeeIds.Contains(a.EmpId) && a.CheckinTime != null);
            var todayCheckoutCount = todayRows
                .Count(a => activeEmployeeIds.Contains(a.EmpId) && a.CheckoutTime != null);
            var monthAttendanceDays = monthRows
                .Where(a => a.CheckinTime != null || a.CheckoutTime != null)
                .Select(a => a.WorkDate.Date)
                .Distinct()
                .Count();
            var averageHoursPerDay = monthAttendanceDays <= 0
                ? 0m
                : Math.Round(monthHours / monthAttendanceDays, 1);
            var longShiftCount = monthRows.Count(a => RawWorkHours(a.CheckinTime, a.CheckoutTime) > 12m);
            var longDistanceCount = monthRows.Count(a => a.DistanceKm > 5m);
            var pendingCheckoutNames = openRows
                .OrderBy(a => a.CheckinTime)
                .Select(a => employeeName(a.EmpId))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .Take(4)
                .ToList();

            var trendClass = "neutral";
            var trendText = "ข้อมูลเดือนนี้จาก attendance";
            if (previousHours > 0)
            {
                var diff = Math.Round((monthHours - previousHours) * 100m / previousHours, 1);
                trendClass = diff >= 0 ? "positive" : "negative";
                trendText = $"{Math.Abs(diff):0.#}% จากเดือนที่แล้ว";
            }
            else if (monthHours > 0)
            {
                trendText = "ยังไม่มีข้อมูลเดือนก่อน";
            }

            return new DashboardTimeSummary
            {
                MonthWorkHours = Math.Round(monthHours, 1),
                ClosedWorkHours = Math.Round(closedHours, 1),
                OpenWorkHours = Math.Round(openHours, 1),
                PendingCheckoutCount = openRows.Count,
                TodayCheckinCount = todayCheckinCount,
                TodayCheckoutCount = todayCheckoutCount,
                TodayMissingCheckinCount = Math.Max(0, activeEmployeeIds.Count - todayCheckinCount),
                MonthAttendanceDays = monthAttendanceDays,
                AverageHoursPerDay = averageHoursPerDay,
                LongShiftCount = longShiftCount,
                LongDistanceCount = longDistanceCount,
                PendingCheckoutNames = pendingCheckoutNames,
                TrendClass = trendClass,
                TrendText = trendText
            };
        }

        private static List<HomeDashboardWorkload> BuildTeamWorkload(
            IReadOnlyList<DashboardAssignRow> assigns,
            Func<int?, string> employeeName,
            Func<int?, string> employeeAvatar)
        {
            var activeAssigns = assigns
                .Where(a => Norm(a.WorkStatus) != "DONE")
                .GroupBy(a => a.EmpId)
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = activeAssigns.Keys
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
                .Where(x => x.Count > 0)
                .OrderByDescending(x => x.Count)
                .ThenBy(x => employeeName(x.EmpId))
                .Take(5)
                .ToList();

            var max = rows.Select(x => x.Count).DefaultIfEmpty(0).Max();
            return rows
                .Select((row, index) => new HomeDashboardWorkload
                {
                    Name = employeeName(row.EmpId),
                    ActiveTaskCount = row.Count,
                    Value = max <= 0 ? 0 : Math.Max(8, (int)Math.Round(row.Count * 100m / max)),
                    Color = ColorByIndex(index),
                    AvatarPath = row.AvatarPath
                })
                .ToList();
        }

        private static List<HomeDashboardOpenWorkItem> BuildOpenIssueSupportItems(
            IReadOnlyList<DashboardIssueRow> issues,
            IReadOnlyList<DashboardSupportOrderRow> supportOrders,
            Func<int?, string> projectName,
            Func<int?, string> employeeName,
            Func<int?, int?> projectBaEmpId,
            int? currentEmpId,
            bool isAdmin,
            CultureInfo culture)
        {
            var rows = new List<(DateTime? dueDate, DateTime createdAt, HomeDashboardOpenWorkItem item)>();

            rows.AddRange(issues
                .Where(i => IsOpenWorkStatus(i.IssueStatus))
                .Select(i => (
                    dueDate: i.EndDate,
                    createdAt: i.CreatedAt,
                    item: new HomeDashboardOpenWorkItem
                    {
                        Type = "Issue",
                        Title = string.IsNullOrWhiteSpace(i.IssueName) ? $"Issue #{i.IssueId}" : i.IssueName,
                        ProjectName = projectName(i.ProjectId),
                        OwnerName = employeeName(i.EmpId),
                        DueText = i.EndDate.HasValue ? FormatDashboardDate(i.EndDate, culture) : "ยังไม่กำหนด",
                        Url = ShouldUseBaOpenWorkRoute(projectBaEmpId(i.ProjectId), currentEmpId, isAdmin)
                            ? $"/ProjectIssues/Edit/{i.IssueId}"
                            : $"/ProjectIssues/DevEdit/{i.IssueId}",
                        Color = IsHighPriority(i.IssuePriority) ? "pink" : "orange"
                    })));

            rows.AddRange(supportOrders
                .Where(o => IsOpenWorkStatus(o.Status))
                .Select(o => (
                    dueDate: o.EndDate,
                    createdAt: o.CreatedAt ?? DateTime.MinValue,
                    item: new HomeDashboardOpenWorkItem
                    {
                        Type = "Support",
                        Title = string.IsNullOrWhiteSpace(o.OrderTitle) ? $"Support #{o.OrderId}" : o.OrderTitle,
                        ProjectName = projectName(o.ProjectId),
                        OwnerName = employeeName(o.AssignTo),
                        DueText = o.EndDate.HasValue ? FormatDashboardDate(o.EndDate, culture) : "ยังไม่กำหนด",
                        Url = ShouldUseBaOpenWorkRoute(projectBaEmpId(o.ProjectId), currentEmpId, isAdmin)
                            ? $"/SupportOrders/Edit/{o.OrderId}"
                            : $"/SupportOrdersDev/Edit/{o.OrderId}",
                        Color = IsHighPriority(o.Priority) ? "pink" : "cyan"
                    })));

            return rows
                .OrderBy(row => row.dueDate ?? DateTime.MaxValue)
                .ThenByDescending(row => row.createdAt)
                .Take(10)
                .Select(row => row.item)
                .ToList();
        }

        private static bool CanSeeOpenIssue(
            DashboardIssueRow issue,
            Func<int?, int?> projectBaEmpId,
            int? currentEmpId,
            bool isAdmin)
        {
            return isAdmin ||
                (currentEmpId.HasValue &&
                    (issue.EmpId == currentEmpId.Value || projectBaEmpId(issue.ProjectId) == currentEmpId.Value));
        }

        private static bool CanSeeOpenSupport(
            DashboardSupportOrderRow supportOrder,
            Func<int?, int?> projectBaEmpId,
            int? currentEmpId,
            bool isAdmin)
        {
            return isAdmin ||
                (currentEmpId.HasValue &&
                    (supportOrder.AssignTo == currentEmpId.Value || projectBaEmpId(supportOrder.ProjectId) == currentEmpId.Value));
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
            var devStatus = Norm(issue.DevStatus);
            return issueStatus is "FIXED" or "PASS" or "REJECT" or "DONE" or "CLOSED" or "RESOLVED"
                || devStatus is "FIXED" or "DONE" or "RESOLVED";
        }

        private static bool IsIssueInProgress(DashboardIssueRow issue)
        {
            var issueStatus = Norm(issue.IssueStatus);
            var devStatus = Norm(issue.DevStatus);
            return issueStatus is "WIP" or "IN_PROGRESS" or "DOING"
                || devStatus is "WIP" or "DOING" or "IN_PROGRESS";
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
            return Norm(status) is "FIXED" or "PASS" or "REJECT" or "DONE"
                || Norm(devStatus) == "FIXED";
        }

        private static bool IsSupportOrderInProgress(DashboardSupportOrderRow order)
        {
            return Norm(order.Status) is "WIP" or "IN_PROGRESS" or "DOING"
                || Norm(order.DevStatus) is "WIP" or "IN_PROGRESS" or "DOING";
        }

        private static string SupportOrderActivityColor(string? status, string? devStatus)
        {
            var normalizedStatus = Norm(status);
            var normalizedDevStatus = Norm(devStatus);

            if (normalizedStatus is "FIXED" or "PASS" or "REJECT" or "DONE" || normalizedDevStatus == "FIXED")
            {
                return "green";
            }

            if (normalizedStatus == "WIP" || normalizedDevStatus is "WIP" or "IN_PROGRESS")
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
        {
            if (string.IsNullOrWhiteSpace(profileImagePath))
            {
                return DefaultProfileImagePath;
            }

            var path = profileImagePath.Trim();
            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }

        private static string Norm(string? value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static string ColorByIndex(int index)
        {
            string[] colors = { "green", "blue", "orange", "purple", "pink", "cyan", "lime" };
            return colors[Math.Abs(index) % colors.Length];
        }

        private static string ColorHex(string color)
        {
            return color switch
            {
                "green" => "#10d58f",
                "blue" => "#1688f5",
                "orange" => "#fb9a13",
                "pink" => "#ff2d62",
                "purple" => "#8b4df4",
                "violet" => "#a855f7",
                "cyan" => "#0ad0c8",
                "lime" => "#52d11f",
                "warning" => "#f59e0b",
                "info" => "#22c7f5",
                "success" => "#10d58f",
                "primary" => "#3b82f6",
                "danger" => "#ef4444",
                "dark" => "#64748b",
                "secondary" => "#94a3b8",
                "muted" => "#64748b",
                _ => "#1688f5"
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
            public string? ProfileImagePath { get; set; }
        }

        private sealed class DashboardMeetingRow
        {
            public int Id { get; set; }
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
            public string TrendText { get; set; } = "";
            public string TrendClass { get; set; } = "neutral";
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
