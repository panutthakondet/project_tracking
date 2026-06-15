using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Services;
using ProjectTracking.Middleware;
using ProjectTracking.ViewModels;

namespace ProjectTracking.Controllers
{
    public class PhaseStatusReportController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly OverdueNotificationService _overdueNotificationService;
        private readonly LineMessagingService _lineMessagingService;
        private readonly IConfiguration _configuration;

        public PhaseStatusReportController(
            AppDbContext context,
            OverdueNotificationService overdueNotificationService,
            LineMessagingService lineMessagingService,
            IConfiguration configuration)
        {
            _context = context;
            _overdueNotificationService = overdueNotificationService;
            _lineMessagingService = lineMessagingService;
            _configuration = configuration;
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

            var projectList = allRows
                .GroupBy(x => x.ProjectName)
                .Select(g => new SelectListItem
                {
                    Value = g.Key,
                    Text = g.First().ProjectDisplayName,
                    Selected = g.Key == projectName,
                    Group = new SelectListGroup { Name = g.First().CoopName ?? "" }
                })
                .OrderBy(x => x.Text)
                .ToList();

            ViewBag.CoopList = allRows
                .Select(x => x.CoopName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewBag.ProjectList = projectList;

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
            ViewBag.SelectedProjectDisplay = string.IsNullOrEmpty(projectName)
                ? null
                : projectList.FirstOrDefault(x => x.Value == projectName)?.Text;
            ViewBag.SelectedStatus = phaseStatus;

            return View(result
                .OrderBy(x => x.CoopName)
                .ThenBy(x => x.ProjectName)
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
                .OrderBy(x => x.CoopName)
                .ThenBy(x => x.ProjectName)
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
                .OrderBy(x => x.CoopName)
                .ThenBy(x => x.ProjectName)
                .ThenBy(x => x.PhaseOrder)
                .ThenBy(x => x.PeriodOrder)
                .ThenBy(x => x.PhaseId)
                .ToList());
        }

        [RequireMenu("Employees.LineOverdue")]
        public IActionResult LineOverdue()
        {
            return RedirectToAction("LineOverdue", "Employees");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Employees.LineOverdue")]
        public IActionResult SendSelectedLineOverdue(List<string>? selectedKeys)
        {
            return RedirectToAction("LineOverdue", "Employees");
        }

        // =====================================================
        // 🔔 SEND LINE (กดปุ่มจากหน้า Report)
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Employees.LineOverdue")]
        public IActionResult SendOverdueLine(string? empName, string? projectName, string? phaseStatus)
        {
            return RedirectToAction("LineOverdue", "Employees");
        }

        private IEnumerable<VwPhaseOwnerStatus> ApplyFilters(
            IEnumerable<VwPhaseOwnerStatus> rows,
            string? empName,
            string? projectName,
            string? phaseStatus)
        {
            var result = rows;

            if (!string.IsNullOrEmpty(empName))
                result = result.Where(x => x.EmpName == empName);

            if (!string.IsNullOrEmpty(projectName))
                result = result.Where(x => x.ProjectName == projectName);

            if (!string.IsNullOrEmpty(phaseStatus))
                result = result.Where(x => x.PhaseStatus == phaseStatus);

            return result;
        }

        private bool IsLineDueRow(VwPhaseOwnerStatus row)
        {
            if (IsDone(null, row.PhaseStatus))
                return false;

            if ((row.OverdueDays ?? 0) > 0)
                return true;

            var riskDays = Math.Clamp(_configuration.GetValue<int?>("OVERDUE_NOTIFICATION_RISK_DAYS") ?? 7, 0, 30);
            var today = DateTime.Today;
            return row.PlanEnd.HasValue
                && row.PlanEnd.Value.Date >= today
                && row.PlanEnd.Value.Date <= today.AddDays(riskDays);
        }

        private static string BuildLineTitle(VwPhaseOwnerStatus row)
            => (row.OverdueDays ?? 0) > 0
                ? $"งานล่าช้า: {row.Role}"
                : $"งานเสี่ยงล่าช้า: {row.Role}";

        private static string BuildLineMessage(VwPhaseOwnerStatus row)
        {
            return string.Join("\n", new[]
            {
                $"สหกรณ์: {(string.IsNullOrWhiteSpace(row.CoopName) ? "-" : row.CoopName)}",
                $"Project: {row.ProjectName}",
                $"Employee: {row.EmpName}",
                $"Role: {(string.IsNullOrWhiteSpace(row.Role) ? "-" : row.Role)}",
                $"Status: {row.PhaseStatus}",
                $"ส่วน / งวด: ส่วนที่ {row.PhaseOrder} / งวดที่ {row.PeriodOrder}",
                $"Project Period: {DateText(row.StartDate)} - {DateText(row.EndDate)}",
                $"Plan: {DateText(row.PlanStart)} - {DateText(row.PlanEnd)}",
                $"Plan Days: {(row.PlanDays?.ToString() ?? "-")}",
                $"กำหนดส่งงวดงาน: {DateText(row.ActualEnd)}",
                $"Overdue: {(row.OverdueDays ?? 0)} วัน",
                $"Remark: {(string.IsNullOrWhiteSpace(row.Remark) ? "-" : row.Remark)}"
            });
        }

        private static string BuildPhaseStatusReportUrl(
            VwPhaseOwnerStatus row,
            string? empName,
            string? projectName,
            string? phaseStatus)
        {
            var query = new List<string>
            {
                $"projectName={Uri.EscapeDataString(projectName ?? row.ProjectName ?? "")}",
                $"empName={Uri.EscapeDataString(empName ?? row.EmpName ?? "")}",
                $"phaseStatus={Uri.EscapeDataString(phaseStatus ?? row.PhaseStatus ?? "")}"
            };

            return $"/PhaseStatusReport?{string.Join("&", query)}";
        }

        private static string DateText(DateTime? value)
            => value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "-";

        private async Task<List<LineOverdueSelectionItemViewModel>> BuildLineOverdueSelectionItemsAsync()
        {
            var today = DateTime.Today;
            var riskDays = Math.Clamp(_configuration.GetValue<int?>("OVERDUE_NOTIFICATION_RISK_DAYS") ?? 7, 0, 30);
            var riskUntil = today.AddDays(riskDays);

            var employeeRows = await _context.Employees
                .AsNoTracking()
                .Select(x => new { x.EmpId, x.EmpName, x.LoginUserId })
                .ToListAsync();

            var employees = employeeRows.ToDictionary(x => x.EmpId, x => x.EmpName ?? $"Employee #{x.EmpId}");
            var lineEmpIds = await _context.LineRecipients
                .AsNoTracking()
                .Where(x => x.IsActive && x.EmpId.HasValue && x.LineUserId != null && x.LineUserId != "")
                .Select(x => x.EmpId!.Value)
                .Distinct()
                .ToListAsync();
            var hasLine = lineEmpIds.ToHashSet();

            var items = new List<LineOverdueSelectionItemViewModel>();

            var assigns = await _context.PhaseAssigns
                .AsNoTracking()
                .Include(x => x.Phase!)
                    .ThenInclude(x => x.Project)
                        .ThenInclude(x => x!.Coop)
                .Where(x => x.Phase != null
                    && ((x.PlanEnd ?? x.Phase!.PlanEnd).HasValue)
                    && (x.PlanEnd ?? x.Phase!.PlanEnd)!.Value <= riskUntil)
                .ToListAsync();

            foreach (var row in assigns)
            {
                if (IsDone(row.WorkStatus, row.Phase?.PhaseStatus) || IsClosedPhaseForSelection(row.Phase?.PhaseStatus))
                    continue;

                var dueDate = row.PlanEnd ?? row.Phase?.PlanEnd;
                if (!TrySelectionDueState(dueDate, today, riskUntil, out var severity, out var stateText, out var overdueDays))
                    continue;

                var project = row.Phase?.Project;
                var title = string.IsNullOrWhiteSpace(row.Role) ? row.Phase?.PhaseName ?? $"Assign #{row.AssignId}" : row.Role!;
                var ownerName = EmployeeName(employees, row.EmpId);
                var baEmpId = project?.BaEmpId;
                var baName = EmployeeName(employees, baEmpId);
                var message = BuildSelectionMessage(
                    stateText,
                    project?.Coop?.CoopName,
                    ProjectDisplayNameForSelection(project),
                    title,
                    ownerName,
                    baName,
                    row.Phase?.PhaseOrder,
                    row.Phase?.PeriodOrder,
                    project?.StartDate,
                    project?.EndDate,
                    row.PlanStart ?? row.Phase?.PlanStart,
                    row.PlanEnd ?? row.Phase?.PlanEnd,
                    row.Phase?.PeriodEndDate,
                    row.Remark);

                AddSelectionItem(items, employees, hasLine, "ASSIGN_DUE", "Phase Assign", row.AssignId, row.EmpId, "เจ้าของงาน", ownerName, baName, severity, stateText, project?.Coop?.CoopName, ProjectDisplayNameForSelection(project), title, row.PlanStart ?? row.Phase?.PlanStart, row.PlanEnd ?? row.Phase?.PlanEnd, row.Phase?.PeriodEndDate, overdueDays, message, project != null ? $"/PhaseAssigns?projectId={project.ProjectId}&empId={row.EmpId}" : $"/PhaseAssigns?empId={row.EmpId}");

                if (baEmpId.HasValue)
                {
                    AddSelectionItem(items, employees, hasLine, "ASSIGN_DUE", "Phase Assign", row.AssignId, baEmpId.Value, "BA", ownerName, baName, severity, stateText, project?.Coop?.CoopName, ProjectDisplayNameForSelection(project), title, row.PlanStart ?? row.Phase?.PlanStart, row.PlanEnd ?? row.Phase?.PlanEnd, row.Phase?.PeriodEndDate, overdueDays, message, project != null ? $"/PhaseAssigns?projectId={project.ProjectId}" : "/PhaseAssigns");
                }
            }

            var issues = await _context.ProjectIssues
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Where(x => x.EndDate.HasValue && x.EndDate.Value <= riskUntil)
                .ToListAsync();

            foreach (var row in issues)
            {
                if (IsIssueDoneForSelection(row.IssueStatus, row.DevStatus))
                    continue;

                if (!TrySelectionDueState(row.EndDate, today, riskUntil, out var severity, out var stateText, out var overdueDays))
                    continue;

                var ownerName = EmployeeName(employees, row.AssignTo);
                var baEmpId = row.Project?.BaEmpId;
                var baName = EmployeeName(employees, baEmpId);
                var message = BuildSelectionMessage(stateText, row.Project?.Coop?.CoopName, ProjectDisplayNameForSelection(row.Project), row.IssueName, ownerName, baName, null, null, row.StartDate, row.EndDate, row.StartDate, row.EndDate, row.EndDate, null);

                AddSelectionItem(items, employees, hasLine, "ISSUE_DUE", "Issue", row.IssueId, row.AssignTo, "เจ้าของงาน", ownerName, baName, severity, stateText, row.Project?.Coop?.CoopName, ProjectDisplayNameForSelection(row.Project), row.IssueName, row.StartDate, row.EndDate, row.EndDate, overdueDays, message, $"/ProjectIssues/DevEdit/{row.IssueId}");
                if (baEmpId.HasValue)
                    AddSelectionItem(items, employees, hasLine, "ISSUE_DUE", "Issue", row.IssueId, baEmpId.Value, "BA", ownerName, baName, severity, stateText, row.Project?.Coop?.CoopName, ProjectDisplayNameForSelection(row.Project), row.IssueName, row.StartDate, row.EndDate, row.EndDate, overdueDays, message, $"/ProjectIssues/Edit/{row.IssueId}");
            }

            var supports = await _context.ProjectSupportOrders
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(x => x!.Coop)
                .Where(x => x.EndDate.HasValue && x.EndDate.Value <= riskUntil)
                .ToListAsync();

            foreach (var row in supports)
            {
                if (IsSupportDoneForSelection(row.Status, row.DevStatus))
                    continue;

                if (!TrySelectionDueState(row.EndDate, today, riskUntil, out var severity, out var stateText, out var overdueDays))
                    continue;

                var title = string.IsNullOrWhiteSpace(row.OrderTitle) ? $"Support #{row.OrderId}" : row.OrderTitle!;
                var ownerName = EmployeeName(employees, row.AssignTo);
                var baEmpId = row.Project?.BaEmpId;
                var baName = EmployeeName(employees, baEmpId);
                var message = BuildSelectionMessage(stateText, row.Project?.Coop?.CoopName, ProjectDisplayNameForSelection(row.Project), title, ownerName, baName, null, null, row.StartDate, row.EndDate, row.StartDate, row.EndDate, row.EndDate, null);

                if (row.AssignTo.HasValue)
                    AddSelectionItem(items, employees, hasLine, "SUPPORT_DUE", "Support", row.OrderId, row.AssignTo.Value, "เจ้าของงาน", ownerName, baName, severity, stateText, row.Project?.Coop?.CoopName, ProjectDisplayNameForSelection(row.Project), title, row.StartDate, row.EndDate, row.EndDate, overdueDays, message, $"/SupportOrdersDev/Edit/{row.OrderId}");

                if (baEmpId.HasValue)
                    AddSelectionItem(items, employees, hasLine, "SUPPORT_DUE", "Support", row.OrderId, baEmpId.Value, "BA", ownerName, baName, severity, stateText, row.Project?.Coop?.CoopName, ProjectDisplayNameForSelection(row.Project), title, row.StartDate, row.EndDate, row.EndDate, overdueDays, message, $"/SupportOrders/Edit/{row.OrderId}");
            }

            return items
                .OrderBy(x => LineOverdueTypeRank(x.SourceType))
                .ThenByDescending(x => x.HasLineRecipient)
                .ThenBy(x => x.EndDate ?? x.DueDate ?? DateTime.MaxValue)
                .ThenBy(x => x.CoopName)
                .ThenBy(x => x.ProjectName)
                .ThenByDescending(x => x.Severity == "DANGER")
                .ThenByDescending(x => x.OverdueDays)
                .ThenBy(x => x.DueDate)
                .ToList();
        }

        private static int LineOverdueTypeRank(string? sourceType)
        {
            return sourceType switch
            {
                "ASSIGN_DUE" => 1,
                "ISSUE_DUE" => 2,
                "SUPPORT_DUE" => 3,
                _ => 99
            };
        }

        private static void AddSelectionItem(
            IList<LineOverdueSelectionItemViewModel> items,
            IReadOnlyDictionary<int, string> employees,
            ISet<int> hasLine,
            string sourceType,
            string sourceLabel,
            int sourceId,
            int recipientEmpId,
            string recipientRole,
            string ownerName,
            string baName,
            string severity,
            string stateText,
            string? coopName,
            string projectName,
            string title,
            DateTime? startDate,
            DateTime? endDate,
            DateTime? dueDate,
            int overdueDays,
            string message,
            string targetUrl)
        {
            if (recipientEmpId <= 0)
                return;

            items.Add(new LineOverdueSelectionItemViewModel
            {
                Key = $"{sourceType}:{sourceId}:{recipientEmpId}",
                SourceType = sourceType,
                SourceLabel = sourceLabel,
                SourceId = sourceId,
                RecipientEmpId = recipientEmpId,
                RecipientName = EmployeeName(employees, recipientEmpId),
                RecipientRole = recipientRole,
                OwnerName = ownerName,
                BaName = baName,
                Severity = severity,
                StateText = stateText,
                CoopName = string.IsNullOrWhiteSpace(coopName) ? "-" : coopName,
                ProjectName = projectName,
                Title = title,
                StartDate = startDate,
                EndDate = endDate,
                DueDate = dueDate,
                OverdueDays = overdueDays,
                HasLineRecipient = hasLine.Contains(recipientEmpId),
                Message = message,
                TargetUrl = targetUrl
            });
        }

        private static bool TrySelectionDueState(
            DateTime? dueDate,
            DateTime today,
            DateTime riskUntil,
            out string severity,
            out string stateText,
            out int overdueDays)
        {
            severity = "WARNING";
            stateText = "";
            overdueDays = 0;

            if (!dueDate.HasValue)
                return false;

            var due = dueDate.Value.Date;
            if (due > riskUntil)
                return false;

            if (due < today)
            {
                severity = "DANGER";
                overdueDays = (today - due).Days;
                stateText = $"ล่าช้า {overdueDays:N0} วัน";
                return true;
            }

            if (due == today)
            {
                stateText = "ครบกำหนดวันนี้";
                return true;
            }

            stateText = $"เสี่ยงล่าช้า เหลือ {(due - today).Days:N0} วัน";
            return true;
        }

        private static string BuildSelectionLineTitle(LineOverdueSelectionItemViewModel item)
            => string.Equals(item.Severity, "DANGER", StringComparison.OrdinalIgnoreCase)
                ? $"งานล่าช้า {item.SourceLabel}: {item.Title}"
                : $"งานเสี่ยงล่าช้า {item.SourceLabel}: {item.Title}";

        private static string BuildSelectionMessage(
            string stateText,
            string? coopName,
            string projectName,
            string title,
            string ownerName,
            string baName,
            int? phaseOrder,
            int? periodOrder,
            DateTime? projectStart,
            DateTime? projectEnd,
            DateTime? planStart,
            DateTime? planEnd,
            DateTime? dueDate,
            string? remark)
        {
            var rows = new List<string>
            {
                $"สถานะ: {stateText}",
                $"สหกรณ์: {(string.IsNullOrWhiteSpace(coopName) ? "-" : coopName)}",
                $"Project: {projectName}",
                $"หัวข้อ: {title}",
                $"เจ้าของงาน: {ownerName}",
                $"BA: {baName}"
            };

            if (phaseOrder.HasValue || periodOrder.HasValue)
                rows.Add($"ส่วน / งวด: ส่วนที่ {(phaseOrder?.ToString() ?? "-")} / งวดที่ {(periodOrder?.ToString() ?? "-")}");

            rows.Add($"Project Period: {DateText(projectStart)} - {DateText(projectEnd)}");
            rows.Add($"Plan: {DateText(planStart)} - {DateText(planEnd)}");
            rows.Add($"กำหนดส่ง: {DateText(dueDate)}");
            rows.Add($"Remark: {(string.IsNullOrWhiteSpace(remark) ? "-" : remark)}");
            return string.Join("\n", rows);
        }

        private static string EmployeeName(IReadOnlyDictionary<int, string> employees, int? empId)
        {
            if (!empId.HasValue || empId.Value <= 0)
                return "-";

            return employees.TryGetValue(empId.Value, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : $"Employee #{empId.Value}";
        }

        private static string ProjectDisplayNameForSelection(Project? project)
        {
            if (project == null)
                return "-";

            return string.IsNullOrWhiteSpace(project.ProjectDisplayName)
                ? project.ProjectName ?? "-"
                : project.ProjectDisplayName;
        }

        private static bool IsClosedPhaseForSelection(string? phaseStatus)
        {
            var normalized = (phaseStatus ?? "").Trim();
            return normalized is "ส่งงวดงานแล้ว" or "อนุมัติจ่ายเงินแล้ว" or "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
        }

        private static bool IsIssueDoneForSelection(string? issueStatus, string? devStatus)
        {
            var issue = Norm(issueStatus);
            var dev = Norm(devStatus);
            return issue is "FIXED" or "PASS" or "REJECT" || dev == "FIXED";
        }

        private static bool IsSupportDoneForSelection(string? status, string? devStatus)
        {
            var normalized = Norm(status);
            var dev = Norm(devStatus);
            return normalized is "FIXED" or "PASS" or "REJECT" or "DONE" || dev == "FIXED";
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
                    CoopName = project.Coop != null ? project.Coop.CoopName : null,
                    project.StartDate,
                    project.EndDate,
                    phase.PhaseId,
                    phase.PhaseOrder,
                    phase.PeriodOrder,
                    phase.PhaseStatus,
                    PhasePlanStart = phase.PlanStart,
                    PhasePlanEnd = phase.PlanEnd,
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
                    CoopName = row.CoopName,
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
                    ActualEnd = row.PeriodEndDate,
                    PlanDays = DaysInclusive(planStart, planEnd),
                    ActualDays = null,
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
                || phase is "DONE" or "ส่งงวดงานแล้ว" or "เสร็จสิ้น" or "เสร็จสิ้นแล้ว";
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
