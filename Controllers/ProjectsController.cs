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

            var projects = await query
                .OrderByDescending(p => p.EndDate ?? DateTime.MinValue)
                .ThenByDescending(p => p.ProjectId)
                .ToListAsync();

            return View(projects);
        }

        // ===========================
        // VIEW ONLY (Standalone page)
        // ===========================
        [HttpGet]
        [RequireMenu("Projects.ViewOnly")]
        public async Task<IActionResult> ViewOnly(int? baEmpId)
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

            var projects = await query
                .OrderByDescending(p => p.EndDate ?? DateTime.MinValue)
                .ThenByDescending(p => p.ProjectId)
                .ToListAsync();

            return View("ViewOnly", projects);
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
            // รองรับวันที่แบบ วัน/เดือน/พ.ศ. และ yyyy-MM-dd
            if (!string.IsNullOrWhiteSpace(Request.Form["StartDate"]))
            {
                var raw = Request.Form["StartDate"].ToString().Trim();

                if (DateTime.TryParseExact(
                        raw,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dt)
                    || DateTime.TryParseExact(
                        raw,
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out dt))
                {
                    if (dt.Year > 2400)
                    {
                        dt = dt.AddYears(-543);
                    }

                    project.StartDate = dt;
                }
            }

            if (!string.IsNullOrWhiteSpace(Request.Form["EndDate"]))
            {
                var raw = Request.Form["EndDate"].ToString().Trim();

                if (DateTime.TryParseExact(
                        raw,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dt)
                    || DateTime.TryParseExact(
                        raw,
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out dt))
                {
                    if (dt.Year > 2400)
                    {
                        dt = dt.AddYears(-543);
                    }

                    project.EndDate = dt;
                }
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Employees = _context.Employees
                    .Where(e => e.Status == "ACTIVE" && e.Position == "Business Analyst")
                    .OrderBy(e => e.EmpName)
                    .ToList();

                return View(project);
            }

            // 1️⃣ Save Project ก่อน
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
            if (id != model.ProjectId)
            {
                return NotFound();
            }

            var db = await _context.Projects.FindAsync(id);
            if (db == null)
            {
                return NotFound();
            }

            // รองรับวันที่แบบ วัน/เดือน/พ.ศ. และ yyyy-MM-dd
            if (!string.IsNullOrWhiteSpace(Request.Form["StartDate"]))
            {
                var raw = Request.Form["StartDate"].ToString().Trim();

                if (DateTime.TryParseExact(
                        raw,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dt)
                    || DateTime.TryParseExact(
                        raw,
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out dt))
                {
                    if (dt.Year > 2400)
                    {
                        dt = dt.AddYears(-543);
                    }

                    model.StartDate = dt;
                }
            }

            if (!string.IsNullOrWhiteSpace(Request.Form["EndDate"]))
            {
                var raw = Request.Form["EndDate"].ToString().Trim();

                if (DateTime.TryParseExact(
                        raw,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dt)
                    || DateTime.TryParseExact(
                        raw,
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out dt))
                {
                    if (dt.Year > 2400)
                    {
                        dt = dt.AddYears(-543);
                    }

                    model.EndDate = dt;
                }
            }

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

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
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
                _context.Projects.Remove(project);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}