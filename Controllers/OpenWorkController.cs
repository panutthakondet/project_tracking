using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
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

        public async Task<IActionResult> Index()
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

            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .Include(issue => issue.Project)
                    .ThenInclude(project => project!.Coop)
                .Include(issue => issue.Project)
                    .ThenInclude(project => project!.BA)
                        .ThenInclude(ba => ba!.LoginUser)
                .Include(issue => issue.Employee)
                    .ThenInclude(employee => employee!.LoginUser)
                .ToListAsync();

            var issueItems = issues
                .Where(issue => IsOpenWorkStatus(issue.IssueStatus))
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
                .Include(order => order.Employee)
                    .ThenInclude(employee => employee!.LoginUser)
                .ToListAsync();

            var supportItems = supportOrders
                .Where(order => IsOpenWorkStatus(order.Status))
                .Where(order => CanSeeSupport(order, currentEmpId, isAdmin))
                .Select(order => BuildSupportItem(order, currentEmpId, isAdmin, today))
                .ToList();

            var sortedIssueItems = issueItems
                .OrderBy(item => item.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(item => item.CreatedAt)
                .ToList();

            var sortedSupportItems = supportItems
                .OrderBy(item => item.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(item => item.CreatedAt)
                .ToList();

            var model = new OpenIssueSupportPageViewModel
            {
                IsAdmin = isAdmin,
                CurrentEmployeeName = currentEmployeeName,
                Groups = new List<OpenIssueSupportGroupViewModel>
                {
                    new()
                    {
                        Key = "issues",
                        Label = "Issues",
                        Icon = "!",
                        Tone = "issue",
                        Items = sortedIssueItems,
                        CoopGroups = BuildCoopGroups(sortedIssueItems)
                    },
                    new()
                    {
                        Key = "support",
                        Label = "Support",
                        Icon = "S",
                        Tone = "support",
                        Items = sortedSupportItems,
                        CoopGroups = BuildCoopGroups(sortedSupportItems)
                    }
                }
                .Where(group => group.TotalCount > 0)
                .ToList()
            };

            return View(model);
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
            var isBa = currentEmpId.HasValue && project?.BaEmpId == currentEmpId.Value;
            var roleText = isAssignee ? "Dev" : isAdmin ? "Admin" : isBa ? "BA" : "User";

            return new OpenIssueSupportItemViewModel
            {
                Type = "Issue",
                Id = issue.IssueId,
                Title = string.IsNullOrWhiteSpace(issue.IssueName) ? $"Issue #{issue.IssueId}" : issue.IssueName.Trim(),
                ProjectName = ProjectDisplayName(project),
                CoopName = project?.Coop?.CoopName ?? "-",
                BaName = project?.BA?.EmpName ?? "-",
                BaAvatarPath = ProfileImage(project?.BA),
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

        private static OpenIssueSupportItemViewModel BuildSupportItem(ProjectSupportOrder order, int? currentEmpId, bool isAdmin, DateTime today)
        {
            var project = order.Project;
            var isAssignee = currentEmpId.HasValue && order.AssignTo == currentEmpId.Value;
            var isBa = currentEmpId.HasValue && project?.BaEmpId == currentEmpId.Value;
            var roleText = isAssignee ? "Dev" : isAdmin ? "Admin" : isBa ? "BA" : "User";

            return new OpenIssueSupportItemViewModel
            {
                Type = "Support",
                Id = order.OrderId,
                Title = string.IsNullOrWhiteSpace(order.OrderTitle) ? $"Support #{order.OrderId}" : order.OrderTitle.Trim(),
                ProjectName = ProjectDisplayName(project),
                CoopName = project?.Coop?.CoopName ?? "-",
                BaName = project?.BA?.EmpName ?? "-",
                BaAvatarPath = ProfileImage(project?.BA),
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
            if (isAdmin)
                return true;

            return currentEmpId.HasValue &&
                (issue.AssignTo == currentEmpId.Value || issue.Project?.BaEmpId == currentEmpId.Value);
        }

        private static bool CanSeeSupport(ProjectSupportOrder order, int? currentEmpId, bool isAdmin)
        {
            if (isAdmin)
                return true;

            return currentEmpId.HasValue &&
                (order.AssignTo == currentEmpId.Value || order.Project?.BaEmpId == currentEmpId.Value);
        }

        private static bool IsOpenWorkStatus(string? status)
        {
            return Norm(status) is "OPEN" or "FAIL";
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
        {
            var path = employee?.LoginUser?.ProfileImagePath;
            if (string.IsNullOrWhiteSpace(path))
                return "/images/Profile/profile.png";

            path = path.Trim();
            if (path.StartsWith("~/", StringComparison.Ordinal)) path = path[1..];
            if (!path.StartsWith("/", StringComparison.Ordinal)) path = "/" + path.TrimStart('/');
            return path;
        }

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
