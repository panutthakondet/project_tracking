using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectTracking.Data;
using ProjectTracking.Models;
using ProjectTracking.Middleware;
using System.Globalization;

namespace ProjectTracking.Controllers
{
    public class ProjectsController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ProjectsController(
            AppDbContext context,
            IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ===========================
        // LIST
        // ===========================
        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> Index(int? baEmpId)
        {
            // Load Business Analyst list for dropdown filter
            ViewBag.Employees = _context.Employees
                .Where(e => e.Status == "ACTIVE" && e.Position == "Business Analyst")
                .OrderBy(e => e.EmpName)
                .ToList();

            var query = _context.Projects
                .Include(p => p.BA)
                    .ThenInclude(e => e!.LoginUser)
                .Include(p => p.Coop)
                .AsNoTracking()
                .AsQueryable();

            if (baEmpId.HasValue)
            {
                query = query.Where(p => p.BaEmpId == baEmpId.Value);
            }

            var projects = OrderProjects(await query.ToListAsync()).ToList();

            return View(projects);
        }

        [RequireMenu("Projects.Index")]
        public async Task<IActionResult> ViewOnly(int? projectId, int? baEmpId, string? status)
        {
            var allProjects = await _context.Projects
                .Include(p => p.BA)
                .Include(p => p.Coop)
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

            var result = query
                .OrderBy(p => ProjectSortOrder(p.Status))
                .ThenBy(p => p.EndDate ?? DateTime.MaxValue)
                .ThenBy(p => p.Coop != null ? p.Coop.CoopName : "")
                .ThenBy(p => p.ProjectName)
                .ToList();

            return View(result);
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

            if (!ModelState.IsValid)
            {
                await LoadProjectFormLookupsAsync(model.RequirementCardId);

                return View(model);
            }

            // ===============================
            // ✅ UPDATE FIELD (ครบทุกช่อง)
            // ===============================
            db.ProjectName = model.ProjectName;
            db.CoopId = model.CoopId;
            db.StartDate = model.StartDate;
            db.EndDate = model.EndDate;
            db.Status = model.Status;

            // 🔹 SYSTEM / DATABASE INFO
            db.LinkName = model.LinkName;
            db.DatabaseName = model.DatabaseName;
            db.TestAccount = model.TestAccount;
            db.RemoteUrl = model.RemoteUrl;

            // 🔹 DESIGN
            db.FigmaLink = model.FigmaLink;

            // 🔹 BUSINESS ANALYST
            db.BaEmpId = model.BaEmpId;
            db.RequirementCardId = model.RequirementCardId;
            db.CreatedAt = DateTime.Now;
            db.EntryId = await GetCurrentEntryIdAsync();

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

            var targetColumn = await FindRequirementBoardColumnForProjectStatusAsync(projectStatus);
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

        private async Task<RequirementBoardColumn?> FindRequirementBoardColumnForProjectStatusAsync(string? projectStatus)
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
                .Where(c => c.IsActive)
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

            ViewBag.Coops = await _context.CntMCoops
                .AsNoTracking()
                .OrderBy(c => c.CoopName)
                .ToListAsync();

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

        // ===========================
        // DELETE (GET)
        // ===========================
        [RequireMenu("Projects.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var project = await _context.Projects.FindAsync(id);
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
