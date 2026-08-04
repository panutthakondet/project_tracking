using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using ProjectTracking.Services;
using ProjectTracking.ViewModels;
using System.Globalization;

namespace ProjectTracking.Controllers
{
    public class ProjectsController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly StatusApprovalService _statusApprovalService;

        public ProjectsController(
            AppDbContext context,
            IWebHostEnvironment env,
            StatusApprovalService statusApprovalService)
        {
            _context = context;
            _env = env;
            _statusApprovalService = statusApprovalService;
        }

        // ===========================
        // LIST
        // ===========================
        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> Index(int? baEmpId, int? departmentId)
        {
            // Load Business Analyst list for dropdown filter
            ViewBag.Employees = _context.Employees
                .Where(e => e.Status == "ACTIVE" && e.Position == "Business Analyst")
                .OrderBy(e => e.EmpName)
                .ToList();

            var query = _context.Projects
                .Include(p => p.BA)
                    .ThenInclude(e => e!.LoginUser)
                .Include(p => p.PM)
                    .ThenInclude(e => e!.LoginUser)
                .Include(p => p.Coop)
                .Include(p => p.Department)
                .AsNoTracking()
                .AsQueryable();

            if (baEmpId.HasValue)
            {
                query = query.Where(p => p.BaEmpId == baEmpId.Value);
            }

            if (departmentId.HasValue)
            {
                query = query.Where(p => p.DepartmentId == departmentId.Value);
            }

            ViewBag.ProjectDepartments = await ActiveProjectDepartmentsQuery().ToListAsync();
            ViewBag.SelectedDepartmentId = departmentId;

            var projects = OrderProjects(await query.ToListAsync()).ToList();
            var projectIds = projects.Select(p => p.ProjectId).ToList();

            ViewBag.PendingProjectApprovalIds = projectIds.Count == 0
                ? new HashSet<int>()
                : (await _context.StatusApprovalRequests
                    .AsNoTracking()
                    .Where(r => r.TargetType == StatusApprovalService.TargetProject
                                && r.RequestStatus == StatusApprovalService.RequestPending
                                && projectIds.Contains(r.TargetId))
                    .Select(r => r.TargetId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();

            return View(projects);
        }

        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> ViewOnly(int? projectId, int? baEmpId, string? status, int? departmentId)
        {
            var allProjects = await _context.Projects
                .Include(p => p.BA)
                .Include(p => p.PM)
                .Include(p => p.Coop)
                .Include(p => p.Department)
                .AsNoTracking()
                .OrderBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToListAsync();

            var query = allProjects.AsEnumerable();

            if (projectId.HasValue)
                query = query.Where(p => p.ProjectId == projectId.Value);

            if (baEmpId.HasValue)
                query = query.Where(p => p.BaEmpId == baEmpId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(p => string.Equals(p.Status, status, StringComparison.OrdinalIgnoreCase));

            if (departmentId.HasValue)
                query = query.Where(p => p.DepartmentId == departmentId.Value);

            ViewBag.Projects = allProjects;
            ViewBag.BaList = allProjects
                .Where(p => p.BA != null)
                .Select(p => p.BA!)
                .GroupBy(e => e.EmpId)
                .Select(g => g.First())
                .OrderBy(e => e.EmpName)
                .ToList();
            ViewBag.StatusList = allProjects
                .Select(p => p.Status)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
            ViewBag.SelectedProjectId = projectId;
            ViewBag.SelectedBaEmpId = baEmpId;
            ViewBag.SelectedStatus = status ?? "";
            ViewBag.ProjectDepartments = await ActiveProjectDepartmentsQuery().ToListAsync();
            ViewBag.SelectedDepartmentId = departmentId;

            var result = query
                .OrderBy(p => ProjectSortOrder(p.Status))
                .ThenBy(p => p.EndDate ?? DateTime.MaxValue)
                .ThenBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToList();

            return View(result);
        }

        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> ProductionMemo(int id)
        {
            var project = await _context.Projects
                .AsNoTracking()
                .Include(p => p.Coop)
                .Include(p => p.BA)
                .Include(p => p.PM)
                .Include(p => p.Department)
                .FirstOrDefaultAsync(p => p.ProjectId == id);

            if (project == null)
                return NotFound();

            var phases = await _context.ProjectPhases
                .AsNoTracking()
                .Where(p => p.ProjectId == id)
                .OrderBy(p => p.PhaseOrder)
                .ThenBy(p => p.PeriodOrder)
                .ThenBy(p => p.PhaseSort)
                .ThenBy(p => p.PhaseId)
                .ToListAsync();

            var periodTotal = phases.Count;

            var assignments = await (
                    from assign in _context.PhaseAssigns.AsNoTracking()
                    join phase in _context.ProjectPhases.AsNoTracking() on assign.PhaseId equals phase.PhaseId
                    join emp in _context.Employees.AsNoTracking() on assign.EmpId equals emp.EmpId
                    where phase.ProjectId == id
                    select new
                    {
                        emp.EmpId,
                        emp.EmpName,
                        emp.Position,
                        assign.Role,
                        PhaseOrder = assign.PhaseOrder ?? phase.PhaseOrder,
                        phase.PeriodOrder,
                        assign.AssignId
                    })
                .OrderBy(x => x.PhaseOrder)
                .ThenBy(x => x.PeriodOrder)
                .ThenBy(x => x.AssignId)
                .ToListAsync();

            var owners = assignments
                .GroupBy(x => x.EmpId)
                .Select(g =>
                {
                    var first = g.First();

                    return new ProjectProductionMemoOwnerViewModel
                    {
                        Name = first.EmpName,
                        Role = first.Position ?? ""
                    };
                })
                .OrderBy(x => x.Role)
                .ThenBy(x => x.Name)
                .ToList();

            var ownerNames = owners
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var projectManagers = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Status == "ACTIVE"
                    && e.Position == "Project Manager")
                .OrderBy(e => e.Position)
                .ThenBy(e => e.EmpName)
                .ToListAsync();

            foreach (var manager in projectManagers)
            {
                if (!ownerNames.Add(manager.EmpName))
                    continue;

                owners.Add(new ProjectProductionMemoOwnerViewModel
                {
                    Name = manager.EmpName,
                    Role = string.IsNullOrWhiteSpace(manager.Position)
                        ? "ผู้จัดการโครงการ"
                        : manager.Position
                });
            }

            var model = new ProjectProductionMemoViewModel
            {
                ProjectId = project.ProjectId,
                CoopName = project.Coop?.CoopName ?? "",
                ProjectName = project.ProjectName,
                ProjectDisplayName = project.ProjectDisplayName,
                DepartmentName = project.Department?.DepartmentName ?? "",
                ProjectDetail = project.ProjectDetail,
                LinkName = project.LinkName,
                DatabaseName = project.DatabaseName,
                TestAccount = project.TestAccount,
                RemoteUrl = project.RemoteUrl,
                StartDate = project.StartDate,
                EndDate = project.EndDate,
                GeneratedAt = DateTime.Now,
                BusinessAnalystName = project.BA?.EmpName ?? "",
                Phases = phases
                    .Select((p, index) => new
                    {
                        Phase = p,
                        DurationStart = index == 0
                            ? project.StartDate
                            : phases[index - 1].PeriodEndDate
                    })
                    .Select(p => new ProjectProductionMemoPhaseViewModel
                    {
                        PhaseOrder = p.Phase.PhaseOrder,
                        PeriodOrder = p.Phase.PeriodOrder,
                        PeriodTotal = periodTotal,
                        PhaseName = p.Phase.PhaseName,
                        PlanStart = p.Phase.PlanStart,
                        PlanEnd = p.Phase.PlanEnd,
                        PeriodEndDate = p.Phase.PeriodEndDate,
                        DurationDays = CalculateDurationDays(p.DurationStart, p.Phase.PeriodEndDate)
                    })
                    .ToList(),
                Owners = owners
            };

            return View(model);
        }

        // ===========================
        // CREATE (GET)
        // ===========================
        [RequireMenu("Projects.Create")]
        public async Task<IActionResult> Create()
        {
            await LoadProjectFormLookupsAsync();

            return View(new Project());
        }

        // ===========================
        // CREATE (POST)
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Projects.Create")]
        public async Task<IActionResult> Create(
            Project project
        )
        {
            ModelState.Remove(nameof(Project.StartDate));
            ModelState.Remove(nameof(Project.EndDate));
            project.StartDate = ParseProjectDate(Request.Form["StartDate"]);
            project.EndDate = ParseProjectDate(Request.Form["EndDate"]);
            await ValidateProjectDepartmentAsync(project.DepartmentId);

            if (!ModelState.IsValid)
            {
                await LoadProjectFormLookupsAsync(project.RequirementCardId);

                return View(project);
            }

            // 1️⃣ Save Project ก่อน
            project.CreatedAt = DateTime.Now;
            project.EntryId = await GetCurrentEntryIdAsync();
            _context.Projects.Add(project);
            await SyncRequirementCardColumnForProjectStatusAsync(project.RequirementCardId, project.Status);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ===========================
        // EDIT (GET)
        // ===========================
        [RequireMenu("Projects.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null)
            {
                return NotFound();
            }

            await LoadProjectFormLookupsAsync(project.RequirementCardId);

            return View(project);
        }

        // ===========================
        // EDIT (POST)
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireMenu("Projects.Edit")]
        public async Task<IActionResult> Edit(
            int id,
            Project model
        )
        {
            if (id == 0 && model.ProjectId > 0)
            {
                id = model.ProjectId;
            }

            if (id != model.ProjectId)
            {
                return NotFound();
            }

            var db = await _context.Projects.FindAsync(id);
            if (db == null)
            {
                return NotFound();
            }

            ModelState.Remove(nameof(Project.StartDate));
            ModelState.Remove(nameof(Project.EndDate));
            model.StartDate = ParseProjectDate(Request.Form["StartDate"]);
            model.EndDate = ParseProjectDate(Request.Form["EndDate"]);
            await ValidateProjectDepartmentAsync(model.DepartmentId);

            if (!ModelState.IsValid)
            {
                await LoadProjectFormLookupsAsync(model.RequirementCardId);

                return View(model);
            }

            // ===============================
            // ✅ UPDATE FIELD (ครบทุกช่อง)
            // ===============================
            var oldStatus = db.Status;
            var requestedStatus = model.Status;

            db.ProjectName = model.ProjectName;
            db.ProjectDetail = model.ProjectDetail;
            db.CoopId = model.CoopId;
            db.DepartmentId = model.DepartmentId;
            db.StartDate = model.StartDate;
            db.EndDate = model.EndDate;

            // 🔹 SYSTEM / DATABASE INFO
            db.LinkName = model.LinkName;
            db.DatabaseName = model.DatabaseName;
            db.TestAccount = model.TestAccount;
            db.RemoteUrl = model.RemoteUrl;

            // 🔹 DESIGN
            db.FigmaLink = model.FigmaLink;

            // 🔹 BUSINESS ANALYST
            db.BaEmpId = model.BaEmpId;
            db.PmEmpId = model.PmEmpId;
            db.RequirementCardId = model.RequirementCardId;
            db.CreatedAt = DateTime.Now;
            db.EntryId = await GetCurrentEntryIdAsync();

            var requirePmApproval = StatusApprovalService.IsProjectCompletionStatus(requestedStatus)
                && !StatusApprovalService.IsProjectCompletionStatus(oldStatus)
                && !await _statusApprovalService.CanApplyCompletionStatusImmediatelyAsync(db.ProjectId);

            if (requirePmApproval)
            {
                db.Status = oldStatus;

                var coopName = db.CoopId.HasValue
                    ? await _context.CntMCoops
                        .AsNoTracking()
                        .Where(c => c.CoopId == db.CoopId.Value)
                        .Select(c => c.CoopName)
                        .FirstOrDefaultAsync()
                    : null;
                var projectDisplayName = string.IsNullOrWhiteSpace(coopName)
                    ? db.ProjectName
                    : $"{coopName} - {db.ProjectName}";

                await _statusApprovalService.QueueCompletionRequestAsync(
                    StatusApprovalService.TargetProject,
                    db.ProjectId,
                    db.ProjectId,
                    projectDisplayName,
                    projectDisplayName,
                    oldStatus,
                    requestedStatus,
                    "ขอปรับสถานะโครงการเป็นเสร็จสิ้น");

                TempData["Success"] = "บันทึกข้อมูลแล้ว และส่งคำขออนุมัติสถานะเสร็จสิ้นให้ PM แล้ว";
            }
            else
            {
                db.Status = requestedStatus;
            }

            await SyncRequirementCardColumnForProjectStatusAsync(db.RequirementCardId, db.Status);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private async Task SyncRequirementCardColumnForProjectStatusAsync(int? requirementCardId, string? projectStatus)
        {
            if (!requirementCardId.HasValue) return;

            var card = await _context.RequirementCards
                .FirstOrDefaultAsync(c => c.CardId == requirementCardId.Value && !c.IsArchived);
            if (card == null) return;

            var sourceColumn = await _context.RequirementBoardColumns
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ColumnId == card.ColumnId);
            if (sourceColumn == null) return;

            var targetColumn = await FindRequirementBoardColumnForProjectStatusAsync(projectStatus, sourceColumn.BoardId);
            if (targetColumn == null || targetColumn.ColumnId == card.ColumnId) return;

            var cardsInTargetColumn = await _context.RequirementCards
                .Where(c => c.ColumnId == targetColumn.ColumnId && !c.IsArchived)
                .ToListAsync();

            foreach (var item in cardsInTargetColumn)
            {
                item.SortOrder += 1;
            }

            card.ColumnId = targetColumn.ColumnId;
            card.SortOrder = 1;
            card.UpdatedAt = DateTime.Now;
        }

        private async Task<RequirementBoardColumn?> FindRequirementBoardColumnForProjectStatusAsync(string? projectStatus, int boardId)
        {
            var candidates = NormalizeProjectStatus(projectStatus) switch
            {
                "DONE" => new[] { "Completed/Guaranteed", "Completed", "Complete", "Done", "DONE", "เสร็จสิ้น" },
                "IN_PROGRESS" => new[] { "In Progress", "IN_PROGRESS", "Doing", "กำลังดำเนินการ" },
                "PLAN" => new[] { "Pending", "To Do", "PLAN", "วางแผน" },
                _ => Array.Empty<string>()
            };

            if (candidates.Length == 0) return null;

            var columns = await _context.RequirementBoardColumns
                .Where(c => c.BoardId == boardId && c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.ColumnId)
                .ToListAsync();

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = NormalizeBoardColumnName(candidate);
                var exactMatch = columns.FirstOrDefault(c =>
                    NormalizeBoardColumnName(c.ColumnName) == normalizedCandidate);
                if (exactMatch != null) return exactMatch;
            }

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = NormalizeBoardColumnName(candidate);
                var containsMatch = columns.FirstOrDefault(c =>
                    NormalizeBoardColumnName(c.ColumnName).Contains(normalizedCandidate));
                if (containsMatch != null) return containsMatch;
            }

            return null;
        }

        private async Task LoadProjectFormLookupsAsync(int? selectedRequirementCardId = null)
        {
            ViewBag.Employees = await _context.Employees
                .Where(e => e.Status == "ACTIVE" && e.Position == "Business Analyst")
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.ProjectManagers = await _context.Employees
                .Where(e => e.Status == "ACTIVE"
                    && (e.Position == "Project Manager"
                        || e.Position == "Project Manager IT"
                        || e.Position == "PM"))
                .OrderBy(e => e.EmpName)
                .ToListAsync();

            ViewBag.Coops = await _context.CntMCoops
                .AsNoTracking()
                .OrderBy(c => c.CoopName)
                .ToListAsync();

            ViewBag.ProjectDepartments = await ActiveProjectDepartmentsQuery().ToListAsync();

            var cards = await _context.RequirementCards
                .AsNoTracking()
                .Include(c => c.Column)
                .Where(c => !c.IsArchived)
                .OrderByDescending(c => c.UpdatedAt)
                .ThenBy(c => c.Title)
                .ToListAsync();

            ViewBag.RequirementCards = cards
                .Select(c => new SelectListItem
                {
                    Value = c.CardId.ToString(CultureInfo.InvariantCulture),
                    Text = $"{(c.Column?.ColumnName ?? "No List")} - {c.Title}",
                    Selected = c.CardId == selectedRequirementCardId
                })
                .ToList();
        }

        private static DateTime? ParseProjectDate(string? value)
        {
            var raw = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (DateTime.TryParseExact(
                    raw,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date)
                || DateTime.TryParseExact(
                    raw,
                    "dd/MM/yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date))
            {
                return date.Year > 2400 ? date.AddYears(-543) : date;
            }

            return null;
        }

        private IQueryable<ProjectDepartment> ActiveProjectDepartmentsQuery()
        {
            return _context.ProjectDepartments
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DepartmentName);
        }

        private async Task ValidateProjectDepartmentAsync(int? departmentId)
        {
            if (!departmentId.HasValue)
            {
                ModelState.AddModelError(nameof(Project.DepartmentId), "กรุณาเลือกฝ่าย");
                return;
            }

            var exists = await _context.ProjectDepartments
                .AsNoTracking()
                .AnyAsync(x => x.DepartmentId == departmentId.Value && x.IsActive);
            if (!exists)
            {
                ModelState.AddModelError(nameof(Project.DepartmentId), "ฝ่ายที่เลือกไม่พร้อมใช้งาน");
            }
        }

        private static int? CalculateDurationDays(DateTime? start, DateTime? end)
        {
            if (!start.HasValue || !end.HasValue)
                return null;

            var days = (end.Value.Date - start.Value.Date).Days + 1;
            return days > 0 ? days : null;
        }

        // ===========================
        // DELETE (GET)
        // ===========================
        [RequireMenu("Projects.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var project = await _context.Projects
                .AsNoTracking()
                .Include(x => x.Department)
                .FirstOrDefaultAsync(x => x.ProjectId == id);
            if (project == null)
            {
                return NotFound();
            }

            return View(project);
        }

        // ===========================
        // DELETE (POST)
        // ===========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequireMenu("Projects.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project != null)
            {
                project.CreatedAt = DateTime.Now;
                project.EntryId = await GetCurrentEntryIdAsync();
                await _context.SaveChangesAsync();

                _context.Projects.Remove(project);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<int?> GetCurrentEntryIdAsync()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue) return null;

            var empId = await _context.Employees
                .AsNoTracking()
                .Where(e => e.LoginUserId == userId.Value)
                .Select(e => (int?)e.EmpId)
                .FirstOrDefaultAsync();

            if (empId.HasValue) return empId;

            return await _context.LoginUsers
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => u.EmpId)
                .FirstOrDefaultAsync();
        }

        private static IOrderedEnumerable<Project> OrderProjects(IEnumerable<Project> projects)
        {
            return projects
                .OrderBy(p => ProjectSortOrder(p.Status))
                .ThenBy(p => p.EndDate ?? DateTime.MaxValue)
                .ThenBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName);
        }

        private static int ProjectSortOrder(string? status)
        {
            return NormalizeProjectStatus(status) switch
            {
                "IN_PROGRESS" => 1,
                "PLAN" => 2,
                "DONE" => 3,
                _ => 4
            };
        }

        private static string NormalizeProjectStatus(string? status)
        {
            return (status ?? "")
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", "_")
                .Replace("-", "_");
        }

        private static string NormalizeBoardColumnName(string? value)
        {
            return (value ?? "")
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "")
                .Replace("/", "");
        }
    }
}
