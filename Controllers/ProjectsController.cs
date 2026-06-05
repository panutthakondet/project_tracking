using Microsoft.AspNetCore.Mvc;
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
                .AsNoTracking()
                .OrderBy(p => p.ProjectName)
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
                .ThenBy(p => p.ProjectName)
                .ToList();

            return View(result);
        }

        // ===========================
        // CREATE (GET)
        // ===========================
        [RequireMenu("Projects.Create")]
        public IActionResult Create()
        {
            ViewBag.Employees = _context.Employees
                .Where(e => e.Status == "ACTIVE" && e.Position == "Business Analyst")
                .OrderBy(e => e.EmpName)
                .ToList();

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
                ViewBag.Employees = _context.Employees
                    .Where(e => e.Status == "ACTIVE" && e.Position == "Business Analyst")
                    .OrderBy(e => e.EmpName)
                    .ToList();

                return View(project);
            }

            // 1️⃣ Save Project ก่อน
            project.CreatedAt = DateTime.Now;
            project.EntryId = await GetCurrentEntryIdAsync();
            _context.Projects.Add(project);
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

            ViewBag.Employees = _context.Employees
                .Where(e => e.Status == "ACTIVE" && e.Position == "Business Analyst")
                .OrderBy(e => e.EmpName)
                .ToList();

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
                ViewBag.Employees = _context.Employees
                    .Where(e => e.Status == "ACTIVE" && e.Position == "Business Analyst")
                    .OrderBy(e => e.EmpName)
                    .ToList();

                return View(model);
            }

            // ===============================
            // ✅ UPDATE FIELD (ครบทุกช่อง)
            // ===============================
            db.ProjectName = model.ProjectName;
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
            db.CreatedAt = DateTime.Now;
            db.EntryId = await GetCurrentEntryIdAsync();

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
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
                .ThenBy(p => p.ProjectName);
        }

        private static int ProjectSortOrder(string? status)
        {
            return NormalizeProjectStatus(status) switch
            {
                "IN_PROGRESS" => 1,
                "PLAN" => 2,
                "DONE" => 3,
                "CANCELLED" => 4,
                _ => 5
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
    }
}
