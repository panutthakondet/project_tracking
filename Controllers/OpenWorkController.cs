using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Services;
using ProjectTracking.Models;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class OpenWorkController : BaseController
    {
        private readonly AppDbContext _context;
        private static readonly CultureInfo ThaiCulture = new("th-TH");

        public OpenWorkController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? workType = null)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            var currentEmpId = await ResolveEmployeeIdAsync(currentUserId);
            var currentEmployeeName = currentEmpId.HasValue
                ? await _context.Employees
                    .AsNoTracking()
                    .Where(employee => employee.EmpId == currentEmpId.Value)
                    .Select(employee => employee.EmpName)
                    .FirstOrDefaultAsync() ?? "-"
                : "-";
            var isAdmin = IsAdmin();
            var today = DateTime.Today;

            var assigns = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(assign => assign.Phase)
                    .ThenInclude(phase => phase!.Project)
                        .ThenInclude(project => project!.Coop)
                .Include(assign => assign.Phase)
                    .ThenInclude(phase => phase!.Project)
                        .ThenInclude(project => project!.BA)
                            .ThenInclude(ba => ba!.LoginUser)
                .Include(assign => assign.Phase)
                    .ThenInclude(phase => phase!.Project)
                        .ThenInclude(project => project!.TeamMembers)
                            .ThenInclude(member => member.Employee)
                                .ThenInclude(employee => employee!.LoginUser)
                .Include(assign => assign.Employee)
                    .ThenInclude(employee => employee!.LoginUser)
                .ToListAsync();

            var assignItems = assigns
                .Where(IsPhaseAssignIncomplete)
                .Where(assign => CanSeeAssign(assign, currentEmpId, isAdmin))
                .Select(assign => BuildAssignItem(assign, currentEmpId, isAdmin, today))
                .ToList();

            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .Include(issue => issue.Project)
                    .ThenInclude(project => project!.Coop)
                .Include(issue => issue.Project)
                    .ThenInclude(project => project!.BA)
                        .ThenInclude(ba => ba!.LoginUser)
                .Include(issue => issue.Project)
                    .ThenInclude(project => project!.TeamMembers)
                        .ThenInclude(member => member.Employee)
                            .ThenInclude(employee => employee!.LoginUser)
                .Include(issue => issue.Employee)
                    .ThenInclude(employee => employee!.LoginUser)
                .ToListAsync();

            var issueItems = issues
                .Where(IsIssueIncomplete)
                .Where(issue => CanSeeIssue(issue, currentEmpId, isAdmin))
                .Select(issue => BuildIssueItem(issue, currentEmpId, isAdmin, today))
                .ToList();

            var supportOrders = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(order => order.Project)
                    .ThenInclude(project => project!.Coop)
                .Include(order => order.Project)
                    .ThenInclude(project => project!.BA)
                        .ThenInclude(ba => ba!.LoginUser)
                .Include(order => order.Project)
                    .ThenInclude(project => project!.TeamMembers)
                        .ThenInclude(member => member.Employee)
                            .ThenInclude(employee => employee!.LoginUser)
                .Include(order => order.Employee)
                    .ThenInclude(employee => employee!.LoginUser)
                .ToListAsync();

            var supportItems = supportOrders
                .Where(IsSupportIncomplete)
                .Where(order => CanSeeSupport(order, currentEmpId, isAdmin))
                .Select(order => BuildSupportItem(order, currentEmpId, isAdmin, today))
                .ToList();

            var followups = await _context.ProjectFollowups
                .AsNoTracking()
                .Include(followup => followup.Project)
                    .ThenInclude(project => project!.Coop)
                .Include(followup => followup.Project)
                    .ThenInclude(project => project!.BA)
                        .ThenInclude(ba => ba!.LoginUser)
                .Include(followup => followup.Project)
                    .ThenInclude(project => project!.TeamMembers)
                        .ThenInclude(member => member.Employee)
                            .ThenInclude(employee => employee!.LoginUser)
                .Include(followup => followup.Owner)
                    .ThenInclude(owner => owner!.LoginUser)
                .Include(followup => followup.CreatedByEmployee)
                    .ThenInclude(employee => employee!.LoginUser)
                .ToListAsync();

            var followupItems = followups
                .Where(IsFollowupIncomplete)
                .Where(followup => CanSeeFollowup(followup, currentEmpId, isAdmin))
                .Select(followup => BuildFollowupItem(followup, currentEmpId, isAdmin, today))
                .ToList();

            var assignSortedItems = SortOpenItems(assignItems);
            var issueSortedItems = SortOpenItems(issueItems);
            var supportSortedItems = SortOpenItems(supportItems);
            var followupSortedItems = SortOpenItems(followupItems);
            var allGroups = new List<OpenIssueSupportGroupViewModel>
            {
                new()
                {
                    Key = "assigns",
                    Label = "PhaseAssigns",
                    Icon = "A",
                    Tone = "assign",
                    Items = assignSortedItems,
                    CoopGroups = BuildCoopGroups(assignSortedItems)
                },
                new()
                {
                    Key = "issues",
                    Label = "ProjectIssues",
                    Icon = "!",
                    Tone = "issue",
                    Items = issueSortedItems,
                    CoopGroups = BuildCoopGroups(issueSortedItems)
                },
                new()
                {
                    Key = "support",
                    Label = "SupportOrders",
                    Icon = "S",
                    Tone = "support",
                    Items = supportSortedItems,
                    CoopGroups = BuildCoopGroups(supportSortedItems)
                },
                new()
                {
                    Key = "followups",
                    Label = "Followups",
                    Icon = "F",
                    Tone = "followup",
                    Items = followupSortedItems,
                    CoopGroups = BuildCoopGroups(followupSortedItems)
                }
            };
            var selectedWorkType = NormalizeWorkType(workType);
            var visibleGroups = string.IsNullOrWhiteSpace(selectedWorkType)
                ? allGroups.Where(group => group.TotalCount > 0).ToList()
                : allGroups
                    .Where(group => string.Equals(group.Key, selectedWorkType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var model = new OpenIssueSupportPageViewModel
            {
                IsAdmin = isAdmin,
                CurrentEmployeeName = currentEmployeeName,
                SelectedWorkType = selectedWorkType,
                WorkTypeOptions = allGroups
                    .Select(group => new OpenIssueSupportWorkTypeOptionViewModel
                    {
                        Value = group.Key,
                        Label = group.Label,
                        Count = group.TotalCount
                    })
                    .ToList(),
                Groups = visibleGroups
            };

            return View(model);
        }

        private static OpenIssueSupportItemViewModel BuildAssignItem(PhaseAssign assign, int? currentEmpId, bool isAdmin, DateTime today)
        {
            var phase = assign.Phase;
            var project = phase?.Project;
            var dueDate = assign.PlanEnd ?? phase?.PlanEnd;
            var startDate = assign.PlanStart ?? phase?.PlanStart;
            var isOwner = currentEmpId.HasValue && assign.EmpId == currentEmpId.Value;
            var isBa = IsProjectBa(project, currentEmpId);
            var roleText = isOwner ? "ผู้รับผิดชอบ" : isAdmin ? "Admin" : isBa ? "BA" : "User";
            var title = FirstText(assign.Role, phase?.PhaseDisplayName, $"PhaseAssign #{assign.AssignId}");

            return new OpenIssueSupportItemViewModel
            {
                Type = "PhaseAssign",
                Id = assign.AssignId,
                Title = title,
                ProjectName = ProjectDisplayName(project),
                CoopName = project?.Coop?.CoopName ?? "-",
                BaName = ProjectBaNames(project),
                BaAvatarPath = ProfileImage(project?.BusinessAnalysts.FirstOrDefault()),
                OwnerName = assign.Employee?.EmpName ?? "-",
                OwnerAvatarPath = ProfileImage(assign.Employee),
                Detail = CleanDetail(assign.Remark ?? phase?.PhaseDisplayName),
                StatusText = DisplayStatus(assign.WorkStatus),
                DevStatusText = "-",
                PriorityText = DisplayStatus(phase?.PhaseStatus),
                StartText = FormatDate(startDate),
                DueText = FormatDate(dueDate),
                DateRangeText = FormatDateRange(startDate, dueDate),
                DueDate = dueDate,
                CreatedAt = assign.CreatedAt ?? phase?.CreatedAt ?? DateTime.MinValue,
                Severity = Severity(dueDate, today),
                RecipientRole = roleText,
                TargetUrl = project == null
                    ? $"/PhaseAssigns/Index?empId={assign.EmpId}"
                    : $"/PhaseAssigns/Index?projectId={project.ProjectId}&empId={assign.EmpId}"
            };
        }

        private async Task<int?> ResolveEmployeeIdAsync(int? userId)
        {
            if (!userId.HasValue)
                return null;

            var userEmpId = await _context.LoginUsers
                .AsNoTracking()
                .Where(user => user.UserId == userId.Value)
                .Select(user => user.EmpId)
                .FirstOrDefaultAsync();

            if (userEmpId.HasValue)
                return userEmpId;

            return await _context.Employees
                .AsNoTracking()
                .Where(employee => employee.LoginUserId == userId.Value)
                .Select(employee => (int?)employee.EmpId)
                .FirstOrDefaultAsync();
        }

        private bool IsAdmin()
        {
            var role = (HttpContext.Session.GetString("Role") ?? "").Trim().ToUpperInvariant();
            return role is "ADMIN" or "ADMINISTRATOR";
        }

        private static OpenIssueSupportItemViewModel BuildIssueItem(ProjectIssue issue, int? currentEmpId, bool isAdmin, DateTime today)
        {
            var project = issue.Project;
            var isAssignee = currentEmpId.HasValue && issue.AssignTo == currentEmpId.Value;
            var isBa = IsProjectBa(project, currentEmpId);
            var roleText = isAssignee ? "Dev" : isAdmin ? "Admin" : isBa ? "BA" : "User";

            return new OpenIssueSupportItemViewModel
            {
                Type = "Issue",
                Id = issue.IssueId,
                Title = string.IsNullOrWhiteSpace(issue.IssueName) ? $"Issue #{issue.IssueId}" : issue.IssueName.Trim(),
                ProjectName = ProjectDisplayName(project),
                CoopName = project?.Coop?.CoopName ?? "-",
                BaName = ProjectBaNames(project),
                BaAvatarPath = ProfileImage(project?.BusinessAnalysts.FirstOrDefault()),
                OwnerName = issue.Employee?.EmpName ?? "-",
                OwnerAvatarPath = ProfileImage(issue.Employee),
                Detail = CleanDetail(issue.IssueDetail),
                StatusText = DisplayStatus(issue.IssueStatus),
                DevStatusText = DisplayStatus(issue.DevStatus),
                PriorityText = DisplayStatus(issue.IssuePriority),
                StartText = FormatDate(issue.StartDate),
                DueText = FormatDate(issue.EndDate),
                DateRangeText = FormatDateRange(issue.StartDate, issue.EndDate),
                DueDate = issue.EndDate,
                CreatedAt = issue.CreatedAt,
                Severity = Severity(issue.EndDate, today),
                RecipientRole = roleText,
                TargetUrl = isAssignee
                    ? $"/ProjectIssues/DevDetails/{issue.IssueId}"
                    : $"/ProjectIssues/Details/{issue.IssueId}"
            };
        }

        private static OpenIssueSupportItemViewModel BuildFollowupItem(ProjectFollowup followup, int? currentEmpId, bool isAdmin, DateTime today)
        {
            var project = followup.Project;
            var owner = followup.Owner ?? followup.CreatedByEmployee;
            var isOwner = currentEmpId.HasValue && followup.OwnerEmpId == currentEmpId.Value;
            var isCreator = currentEmpId.HasValue && followup.CreatedByEmpId == currentEmpId.Value;
            var isBa = IsProjectBa(project, currentEmpId);
            var roleText = isOwner ? "เจ้าของงาน" : isCreator ? "ผู้สร้าง" : isAdmin ? "Admin" : isBa ? "BA" : "User";

            return new OpenIssueSupportItemViewModel
            {
                Type = "Followup",
                Id = followup.FollowupId,
                Title = string.IsNullOrWhiteSpace(followup.TaskTitle) ? $"Followup #{followup.FollowupId}" : followup.TaskTitle.Trim(),
                ProjectName = ProjectDisplayName(project),
                CoopName = project?.Coop?.CoopName ?? "-",
                BaName = ProjectBaNames(project),
                BaAvatarPath = ProfileImage(project?.BusinessAnalysts.FirstOrDefault()),
                OwnerName = owner?.EmpName ?? "-",
                OwnerAvatarPath = ProfileImage(owner),
                Detail = CleanDetail(followup.PartnerName),
                StatusText = DisplayStatus(followup.Status),
                DevStatusText = "-",
                PriorityText = "-",
                StartText = FormatDate(followup.LastContactDate ?? followup.CreatedAt),
                DueText = FormatDate(followup.NextFollowupDate),
                DateRangeText = FormatDate(followup.NextFollowupDate),
                DueDate = followup.NextFollowupDate,
                CreatedAt = followup.CreatedAt,
                Severity = Severity(followup.NextFollowupDate, today),
                RecipientRole = roleText,
                TargetUrl = $"/Followups/Details/{followup.FollowupId}"
            };
        }

        private static OpenIssueSupportItemViewModel BuildSupportItem(ProjectSupportOrder order, int? currentEmpId, bool isAdmin, DateTime today)
        {
            var project = order.Project;
            var isAssignee = currentEmpId.HasValue && order.AssignTo == currentEmpId.Value;
            var isBa = IsProjectBa(project, currentEmpId);
            var roleText = isAssignee ? "Dev" : isAdmin ? "Admin" : isBa ? "BA" : "User";

            return new OpenIssueSupportItemViewModel
            {
                Type = "Support",
                Id = order.OrderId,
                Title = string.IsNullOrWhiteSpace(order.OrderTitle) ? $"Support #{order.OrderId}" : order.OrderTitle.Trim(),
                ProjectName = ProjectDisplayName(project),
                CoopName = project?.Coop?.CoopName ?? "-",
                BaName = ProjectBaNames(project),
                BaAvatarPath = ProfileImage(project?.BusinessAnalysts.FirstOrDefault()),
                OwnerName = order.Employee?.EmpName ?? "-",
                OwnerAvatarPath = ProfileImage(order.Employee),
                Detail = CleanDetail(order.OrderDetail),
                StatusText = DisplayStatus(order.Status),
                DevStatusText = DisplayStatus(order.DevStatus),
                PriorityText = DisplayStatus(order.Priority),
                StartText = FormatDate(order.StartDate),
                DueText = FormatDate(order.EndDate),
                DateRangeText = FormatDateRange(order.StartDate, order.EndDate),
                DueDate = order.EndDate,
                CreatedAt = order.CreatedAt ?? DateTime.MinValue,
                Severity = Severity(order.EndDate, today),
                RecipientRole = roleText,
                TargetUrl = isAssignee
                    ? $"/SupportOrdersDev/Details/{order.OrderId}"
                    : $"/SupportOrders/Details/{order.OrderId}"
            };
        }

        private static bool CanSeeIssue(ProjectIssue issue, int? currentEmpId, bool isAdmin)
        {
            return currentEmpId.HasValue &&
                (issue.AssignTo == currentEmpId.Value ||
                    issue.CreatedBy == currentEmpId.Value ||
                    IsProjectBa(issue.Project, currentEmpId));
        }

        private static bool CanSeeSupport(ProjectSupportOrder order, int? currentEmpId, bool isAdmin)
        {
            return currentEmpId.HasValue &&
                (order.AssignTo == currentEmpId.Value ||
                    order.CreatedBy == currentEmpId.Value ||
                    IsProjectBa(order.Project, currentEmpId));
        }

        private static bool CanSeeAssign(PhaseAssign assign, int? currentEmpId, bool isAdmin)
        {
            return currentEmpId.HasValue &&
                (assign.EmpId == currentEmpId.Value || IsProjectBa(assign.Phase?.Project, currentEmpId));
        }

        private static bool CanSeeFollowup(ProjectFollowup followup, int? currentEmpId, bool isAdmin)
        {
            return currentEmpId.HasValue &&
                (followup.OwnerEmpId == currentEmpId.Value ||
                    followup.CreatedByEmpId == currentEmpId.Value ||
                    IsProjectBa(followup.Project, currentEmpId));
        }

        private static bool IsProjectBa(Project? project, int? employeeId)
            => employeeId.HasValue
                && project != null
                && project.BusinessAnalysts.Any(employee => employee.EmpId == employeeId.Value);

        private static string ProjectBaNames(Project? project)
            => string.IsNullOrWhiteSpace(project?.BusinessAnalystNames)
                ? "-"
                : project.BusinessAnalystNames;

        private static bool IsPhaseAssignIncomplete(PhaseAssign assign)
        {
            return Norm(assign.WorkStatus) != "DONE";
        }

        private static bool IsIssueIncomplete(ProjectIssue issue)
        {
            var status = Norm(issue.IssueStatus);
            return status is not "PASS" and not "REJECT" and not "DONE" and not "CLOSED" and not "RESOLVED";
        }

        private static bool IsSupportIncomplete(ProjectSupportOrder order)
        {
            var status = Norm(order.Status);
            return status is not "PASS" and not "REJECT" and not "DONE" and not "CLOSED" and not "RESOLVED";
        }

        private static bool IsFollowupIncomplete(ProjectFollowup followup)
        {
            var status = Norm(followup.Status);
            return status is not "DONE" and not "ACK" and not "CLOSED" and not "RESOLVED";
        }

        private static string Severity(DateTime? dueDate, DateTime today)
        {
            if (!dueDate.HasValue)
                return "normal";

            var date = dueDate.Value.Date;
            if (date < today)
                return "danger";

            return date <= today.AddDays(7) ? "warning" : "normal";
        }

        private static string ProjectDisplayName(Project? project)
        {
            if (project == null)
                return "-";

            var name = project.ProjectName?.Trim();
            return string.IsNullOrWhiteSpace(name) ? "-" : name;
        }

        private static string FirstText(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "-";
        }

        private static string CleanDetail(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            var clean = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length <= 260 ? clean : clean[..260] + "...";
        }

        private static string DisplayStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            return value.Trim().Replace("_", " ");
        }

        private static string ProfileImage(Employee? employee)
            => ProfileImagePathResolver.Normalize(employee?.LoginUser?.ProfileImagePath);

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("dd MMM yyyy", ThaiCulture)
                : "-";
        }

        private static string FormatDateRange(DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue && endDate.HasValue)
                return $"{FormatDate(startDate)} - {FormatDate(endDate)}";

            if (startDate.HasValue)
                return $"เริ่ม {FormatDate(startDate)}";

            if (endDate.HasValue)
                return $"ครบกำหนด {FormatDate(endDate)}";

            return "-";
        }

        private static List<OpenIssueSupportCoopGroupViewModel> BuildCoopGroups(IEnumerable<OpenIssueSupportItemViewModel> items)
            => items
                .GroupBy(item => CoopGroupName(item.CoopName))
                .OrderBy(group => group.Key == "-" ? 1 : 0)
                .ThenBy(group => group.Key)
                .Select(group => new OpenIssueSupportCoopGroupViewModel
                {
                    CoopName = group.Key,
                    Items = group.ToList()
                })
                .ToList();

        private static List<OpenIssueSupportItemViewModel> SortOpenItems(IEnumerable<OpenIssueSupportItemViewModel> items)
            => items
                .OrderBy(item => SeverityOrder(item.Severity))
                .ThenBy(item => item.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(item => item.CreatedAt)
                .ToList();

        private static int SeverityOrder(string? severity)
        {
            return (severity ?? "").Trim().ToLowerInvariant() switch
            {
                "danger" => 0,
                "warning" => 1,
                _ => 2
            };
        }

        private static string NormalizeWorkType(string? workType)
        {
            return (workType ?? "").Trim().ToLowerInvariant().Replace("_", "").Replace("-", "") switch
            {
                "assigns" or "phaseassign" or "phaseassigns" => "assigns",
                "issues" or "issue" or "projectissue" or "projectissues" => "issues",
                "support" or "supportorder" or "supportorders" => "support",
                "followup" or "followups" => "followups",
                _ => ""
            };
        }

        private static string CoopGroupName(string? value)
        {
            var clean = value?.Trim();
            return string.IsNullOrWhiteSpace(clean) ? "-" : clean;
        }

        private static string Norm(string? value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }
    }
}
